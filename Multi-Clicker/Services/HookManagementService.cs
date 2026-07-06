using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using MultiClicker.Models;

namespace MultiClicker.Services
{
    /// <summary>
    /// Service responsible for managing global hooks and input handling.
    ///
    /// Design invariants:
    ///  * Input state (KeysPressed / mouse button flags) is fed exclusively by
    ///    REAL hardware events. Events we inject ourselves (SendInput,
    ///    keybd_event) carry the LLxHF_INJECTED flag and are filtered out, so
    ///    a broadcast can never poison the tracked state and re-fire triggers.
    ///  * Trigger evaluation is edge-triggered: a key-down only fires keybinds
    ///    whose primary key matches the key that just went down; mouse-down
    ///    only fires keybinds bound to the button that just went down.
    ///  * A periodic reconciliation pass uses GetAsyncKeyState to PRUNE state
    ///    that drifted out of sync with the OS (lost up-events). It never adds
    ///    state, so our own injected keys cannot create phantom modifiers.
    ///  * Hook callbacks do the minimum possible work. Every trigger action is
    ///    executed on a single dedicated dispatcher thread, which serializes
    ///    broadcasts so two triggers can never interleave their injected input.
    /// </summary>
    public static class HookManagementService
    {
        #region Win32 API Declarations
        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        #endregion

        #region Constants and Enums
        public enum MouseMessages
        {
            WM_LBUTTONDOWN = 0x0201,
            WM_LBUTTONUP = 0x0202,
            WM_MOUSEMOVE = 0x0200,
            WM_MOUSEWHEEL = 0x020A,
            WM_RBUTTONDOWN = 0x0204,
            WM_RBUTTONUP = 0x0205,
            WM_MBUTTONDOWN = 0x0207,
            WM_MBUTTONUP = 0x0208,
            WM_XBUTTONDOWN = 0x020B,
            WM_XBUTTONUP = 0x020C
        }

        public const int WH_MOUSE_LL = 14;
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;

        // Virtual key codes used by reconciliation and modifier handling.
        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;
        private const int VK_MBUTTON = 0x04;
        private const int VK_XBUTTON1 = 0x05;
        private const int VK_XBUTTON2 = 0x06;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12; // Alt

        // Threshold (ms) above which a hook callback is considered too slow
        // (Windows LowLevelHooksTimeout defaults to ~300 ms; warn well before).
        private const long HookCallbackWarnThresholdMs = 50;
        #endregion

        #region Structures
        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // LLKHF_INJECTED / LLMHF_INJECTED - set by the OS when the event was
        // synthesized via SendInput/keybd_event/mouse_event (i.e. by *us*).
        private const uint LLHF_INJECTED = 0x00000010;
        #endregion

        #region Private Fields
        private static readonly Dictionary<TRIGGERS, (Action<object> action, TimeSpan minimumCooldown)> KeyActions =
            new Dictionary<TRIGGERS, (Action<object>, TimeSpan)>();

        // Input state - all access guarded by InputLock.
        private static readonly HashSet<Keys> KeysPressed = new HashSet<Keys>();
        private static readonly HashSet<MouseMessages> MouseButtonsPressed = new HashSet<MouseMessages>();
        private static bool _xButton1Pressed;
        private static bool _xButton2Pressed;
        private static readonly object InputLock = new object();

        private static readonly Random Random = new Random();
        private static POINT _cursorPosition;

        // Cooldown / reentrancy bookkeeping - safe to access from worker threads.
        private static readonly ConcurrentDictionary<TRIGGERS, DateTime> _lastExecutionTime =
            new ConcurrentDictionary<TRIGGERS, DateTime>();
        private static readonly ConcurrentDictionary<TRIGGERS, DateTime> _runningTriggers =
            new ConcurrentDictionary<TRIGGERS, DateTime>();

        // Single dispatcher thread: serializes every trigger action so that two
        // broadcasts can never interleave their SetForegroundWindow/SendInput calls.
        private static BlockingCollection<(TRIGGERS trigger, Action<object> action, object context)> _dispatchQueue;
        private static Thread _dispatchThread;

        // Pre-computed keybind indices for O(1) edge-triggered lookup.
        // Rebuilt on init and whenever ConfigurationService.KeybindsChanged fires.
        private static readonly object KeybindIndexLock = new object();
        private static Dictionary<Keys, List<KeyValuePair<TRIGGERS, KeyCombination>>> _keybindsByKey =
            new Dictionary<Keys, List<KeyValuePair<TRIGGERS, KeyCombination>>>();
        private static Dictionary<MouseMessages, List<KeyValuePair<TRIGGERS, KeyCombination>>> _keybindsByMouse =
            new Dictionary<MouseMessages, List<KeyValuePair<TRIGGERS, KeyCombination>>>();

        private static System.Threading.Timer _reconcileTimer;
        private static volatile bool _initialized;
        private static volatile bool _triggersSuspended;
        #endregion

        #region Public Events
        public static event Action ShouldOpenPositionConfiguration;

        /// <summary>
        /// Raised (on a worker thread) when an extended mouse button goes down,
        /// so the keybinds dialog can capture X1/X2 without installing its own hook.
        /// The int is 1 for XButton1, 2 for XButton2.
        /// </summary>
        public static event Action<int> XButtonCaptured;
        #endregion

        #region Public Properties
        public static POINT CursorPosition => _cursorPosition;

        /// <summary>
        /// While true, hook callbacks keep tracking input state but do not fire
        /// any trigger. Set by the keybinds dialog during capture so that
        /// pressing a currently-bound combination doesn't launch a broadcast.
        /// </summary>
        public static bool TriggersSuspended
        {
            get => _triggersSuspended;
            set => _triggersSuspended = value;
        }
        #endregion

        #region Public Methods
        public static void Initialize()
        {
            if (_initialized) return;
            InitializeKeyActions();
            RebuildKeybindIndex();
            ConfigurationService.KeybindsChanged += RebuildKeybindIndex;

            _dispatchQueue = new BlockingCollection<(TRIGGERS, Action<object>, object)>();
            _dispatchThread = new Thread(DispatchLoop)
            {
                IsBackground = true,
                Name = "TriggerDispatcher",
                Priority = ThreadPriority.AboveNormal
            };
            _dispatchThread.Start();

            // Periodic reconciliation against OS state every 500 ms.
            _reconcileTimer = new System.Threading.Timer(_ => ReconcileInputState(),
                null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
            _initialized = true;
        }

        public static void Shutdown()
        {
            try
            {
                ConfigurationService.KeybindsChanged -= RebuildKeybindIndex;
                _reconcileTimer?.Dispose();
                _reconcileTimer = null;
                _dispatchQueue?.CompleteAdding();
                _dispatchThread?.Join(TimeSpan.FromSeconds(1));
                ClearInputState();
                _initialized = false;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error during HookManagementService shutdown: {ex.Message}");
            }
        }

        /// <summary>
        /// Debug helper that dumps the current tracked input state to the trace log.
        /// </summary>
        public static void DumpInputState()
        {
            lock (InputLock)
            {
                Trace.WriteLine($"[HookState] Keys: [{string.Join(", ", KeysPressed)}] " +
                                $"Mouse: [{string.Join(", ", MouseButtonsPressed)}] " +
                                $"X1={_xButton1Pressed} X2={_xButton2Pressed} " +
                                $"Running: [{string.Join(", ", _runningTriggers.Keys)}]");
            }
        }

        public static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (nCode < 0)
                    return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

                var msg = wParam.ToInt32();
                bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;
                if (!isDown && !isUp)
                    return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

                var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                // Ignore events we synthesized ourselves (SendInput/keybd_event from
                // broadcasts). Without this, our own injected keys would poison the
                // tracked state and any subsequent real key/click would re-satisfy
                // the combination and re-fire the trigger.
                if ((hookStruct.flags & LLHF_INJECTED) != 0)
                    return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

                var key = NormalizeKey((Keys)hookStruct.vkCode);

                // Always update key state, even when the foreground window is not
                // a tracked one, so key-up events are never lost across focus changes.
                bool wasNewPress;
                lock (InputLock)
                {
                    if (isDown)
                        wasNewPress = KeysPressed.Add(key);
                    else
                    {
                        KeysPressed.Remove(key);
                        wasNewPress = false;
                    }
                }

                // Only evaluate triggers when the foreground window is one we care
                // about, and only on the *initial* down event (no auto-repeat).
                if (isDown && wasNewPress && !_triggersSuspended && !ConfigurationService.IsModifyingKeyBinds)
                {
                    var fg = GetForegroundWindow();
                    if (WindowManagementService.IsRelatedHandle(fg))
                    {
                        EvaluateKeyTriggers(key);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error in keyboard hook callback: {ex.Message}");
            }
            finally
            {
                sw.Stop();
                if (sw.ElapsedMilliseconds > HookCallbackWarnThresholdMs)
                    Trace.WriteLine($"[Perf] Keyboard hook took {sw.ElapsedMilliseconds} ms");
            }

            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        public static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (nCode < 0)
                    return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

                var message = (MouseMessages)wParam.ToInt32();

                // Fast path for high-frequency events that carry no actionable
                // state change. WM_MOUSEMOVE arrives constantly and must do as
                // little as possible to avoid hook timeouts.
                if (message == MouseMessages.WM_MOUSEMOVE || message == MouseMessages.WM_MOUSEWHEEL)
                {
                    return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
                }

                // Ignore events we synthesized ourselves (SendInput broadcasts).
                var mouseHookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if ((mouseHookStruct.flags & LLHF_INJECTED) != 0)
                    return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

                int xButton = 0;
                if (message == MouseMessages.WM_XBUTTONDOWN || message == MouseMessages.WM_XBUTTONUP)
                    xButton = (int)(mouseHookStruct.mouseData >> 16);

                // Always update mouse button state, regardless of foreground.
                // This prevents losing UP events when the user drags off a
                // tracked window before releasing.
                lock (InputLock)
                {
                    UpdateMouseButtonStateLocked(message, xButton);
                }

                bool isMouseDown =
                    message == MouseMessages.WM_LBUTTONDOWN ||
                    message == MouseMessages.WM_RBUTTONDOWN ||
                    message == MouseMessages.WM_MBUTTONDOWN ||
                    message == MouseMessages.WM_XBUTTONDOWN;

                if (!isMouseDown)
                    return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

                // The hook struct carries the exact event coordinates - cheaper and
                // more precise than a GetCursorPos round-trip.
                _cursorPosition = mouseHookStruct.pt;

                // Keybind capture dialog: report X button presses, never fire triggers.
                if (message == MouseMessages.WM_XBUTTONDOWN && XButtonCaptured != null && _triggersSuspended)
                {
                    var captured = xButton;
                    ThreadPool.QueueUserWorkItem(_ => XButtonCaptured?.Invoke(captured));
                }

                // Position picker: a right-click defines a rectangle corner. Runs
                // off-thread; the form marshals its own UI updates.
                if (message == MouseMessages.WM_RBUTTONDOWN && ConfigurationService.IsModifyingKeyBinds)
                {
                    var point = mouseHookStruct.pt;
                    ThreadPool.QueueUserWorkItem(_ => SafeChoosePosition(point));
                    return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
                }

                if (_triggersSuspended || ConfigurationService.IsModifyingKeyBinds)
                    return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

                var fg = GetForegroundWindow();
                if (WindowManagementService.IsRelatedHandle(fg) &&
                    WindowManagementService.IsPointOverTrackedWindow(mouseHookStruct.pt))
                {
                    EvaluateMouseTriggers(message, xButton, mouseHookStruct.pt);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error in mouse hook callback: {ex.Message}");
            }
            finally
            {
                sw.Stop();
                if (sw.ElapsedMilliseconds > HookCallbackWarnThresholdMs)
                    Trace.WriteLine($"[Perf] Mouse hook took {sw.ElapsedMilliseconds} ms");
            }

            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }
        #endregion

        #region Trigger evaluation
        private static void EvaluateKeyTriggers(Keys pressedKey)
        {
            List<KeyValuePair<TRIGGERS, KeyCombination>> candidates;
            lock (KeybindIndexLock)
            {
                if (!_keybindsByKey.TryGetValue(pressedKey, out candidates) || candidates.Count == 0)
                    return;
                // Copy to avoid holding the lock during evaluation.
                candidates = new List<KeyValuePair<TRIGGERS, KeyCombination>>(candidates);
            }

            // Keyboard-triggered actions still need a click position (e.g. a click
            // broadcast bound to a key); snapshot the cursor at trigger time.
            POINT cursor;
            if (!GetCursorPos(out cursor)) cursor = _cursorPosition;

            foreach (var kvp in candidates)
            {
                // Keyboard-only triggers fire on key-down. Keybinds that *also*
                // require a mouse button are handled on the mouse-down path.
                if (kvp.Value.HasMouseButtons) continue;

                if (IsKeyCombinationPressed(kvp.Value))
                {
                    Trace.WriteLine($"Key combination triggered: {kvp.Key} -> {kvp.Value}");
                    if (KeyActions.TryGetValue(kvp.Key, out var actionData))
                    {
                        EnqueueTrigger(kvp.Key, actionData, cursor);
                    }
                }
            }
        }

        private static void EvaluateMouseTriggers(MouseMessages downMessage, int xButton, POINT cursor)
        {
            List<KeyValuePair<TRIGGERS, KeyCombination>> candidates;
            lock (KeybindIndexLock)
            {
                if (!_keybindsByMouse.TryGetValue(downMessage, out candidates) || candidates.Count == 0)
                    return;
                candidates = new List<KeyValuePair<TRIGGERS, KeyCombination>>(candidates);
            }

            foreach (var kvp in candidates)
            {
                // Edge-trigger on the button that actually went down: an X1-bound
                // combo must not fire because X2 was pressed while X1 was stuck.
                if (downMessage == MouseMessages.WM_XBUTTONDOWN)
                {
                    bool matchesEvent = (kvp.Value.XButton1 && xButton == 1) ||
                                        (kvp.Value.XButton2 && xButton == 2);
                    if (!matchesEvent) continue;
                }

                if (IsKeyCombinationPressed(kvp.Value))
                {
                    Trace.WriteLine($"Click combination triggered: {kvp.Key} -> {kvp.Value}");
                    if (KeyActions.TryGetValue(kvp.Key, out var actionData))
                    {
                        EnqueueTrigger(kvp.Key, actionData, cursor);
                    }
                }
            }
        }
        #endregion

        #region Cooldown & dispatch
        private static bool EnqueueTrigger(TRIGGERS trigger,
            (Action<object> action, TimeSpan minimumCooldown) actionData, object context)
        {
            var now = DateTime.UtcNow;

            // Reentrancy guard with a stale-flag watchdog. If a trigger was marked
            // running more than 10x its cooldown ago, assume the task died and
            // force-clear so the user is not locked out.
            if (_runningTriggers.TryGetValue(trigger, out var startedAt))
            {
                var stuckThreshold = TimeSpan.FromTicks(Math.Max(actionData.minimumCooldown.Ticks * 10,
                    TimeSpan.FromSeconds(5).Ticks));
                if (now - startedAt < stuckThreshold)
                {
                    Trace.WriteLine($"Trigger {trigger} is already running/queued, skipping.");
                    return false;
                }

                Trace.WriteLine($"Trigger {trigger} marked running too long; clearing stale flag.");
                _runningTriggers.TryRemove(trigger, out _);
            }

            if (_lastExecutionTime.TryGetValue(trigger, out var last))
            {
                if (now - last < actionData.minimumCooldown)
                    return false;
            }

            var queue = _dispatchQueue;
            if (queue == null || queue.IsAddingCompleted)
                return false;

            _lastExecutionTime[trigger] = now;
            _runningTriggers[trigger] = now;

            try
            {
                queue.Add((trigger, actionData.action, context));
            }
            catch (InvalidOperationException)
            {
                _runningTriggers.TryRemove(trigger, out _);
                return false;
            }

            return true;
        }

        private static void DispatchLoop()
        {
            foreach (var item in _dispatchQueue.GetConsumingEnumerable())
            {
                WindowManagementService.IsBroadcasting = true;
                try
                {
                    item.action(item.context);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Error executing trigger {item.trigger}: {ex.Message}");
                }
                finally
                {
                    WindowManagementService.IsBroadcasting = false;
                    _runningTriggers.TryRemove(item.trigger, out _);
                }
            }
        }

        private static void SafeChoosePosition(POINT point)
        {
            try
            {
                PositionConfigurationForm.choosePosition(point);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error handling position pick: {ex.Message}");
            }
        }
        #endregion

        #region State helpers
        private static void InitializeKeyActions()
        {
            KeyActions[TRIGGERS.SELECT_NEXT] = (obj => PanelManagementService.SelectNextPanel(), TimeSpan.FromMilliseconds(100));
            KeyActions[TRIGGERS.SELECT_PREVIOUS] = (obj => PanelManagementService.SelectPreviousPanel(), TimeSpan.FromMilliseconds(100));
            KeyActions[TRIGGERS.SIMPLE_CLICK] = (obj => WindowManagementService.BroadcastClick((POINT)obj, doubleClick: false), TimeSpan.FromMilliseconds(100));
            KeyActions[TRIGGERS.DOUBLE_CLICK] = (obj => WindowManagementService.BroadcastClick((POINT)obj, doubleClick: true), TimeSpan.FromMilliseconds(100));
            KeyActions[TRIGGERS.GROUP_CHARACTERS] = (obj => WindowManagementService.GroupCharacters(), TimeSpan.FromMilliseconds(1000));
            KeyActions[TRIGGERS.OPTIONS] = (obj => ShouldOpenPositionConfiguration?.Invoke(), TimeSpan.FromMilliseconds(1000));
            KeyActions[TRIGGERS.PASTE_ON_ALL_WINDOWS] = (obj => HandlePasteOnAllWindows(), TimeSpan.FromMilliseconds(500));
            KeyActions[TRIGGERS.FILL_HDV] = (obj =>
            {
                Trace.WriteLine("Starting price analysis");
                Thread.Sleep(500);
                WindowManagementService.FillSellPriceBasedOnForeGroundWindow();
            }, TimeSpan.FromMilliseconds(500));
        }

        /// <summary>
        /// Low-level hooks report side-specific virtual keys, but be defensive:
        /// fold any generic modifier code into its left-hand variant so state
        /// tracking and combination matching use one canonical value.
        /// </summary>
        private static Keys NormalizeKey(Keys key)
        {
            switch (key)
            {
                case Keys.ShiftKey: return Keys.LShiftKey;
                case Keys.ControlKey: return Keys.LControlKey;
                case Keys.Menu: return Keys.LMenu;
                default: return key;
            }
        }

        private static void UpdateMouseButtonStateLocked(MouseMessages message, int xButton)
        {
            switch (message)
            {
                case MouseMessages.WM_LBUTTONDOWN: MouseButtonsPressed.Add(MouseMessages.WM_LBUTTONDOWN); break;
                case MouseMessages.WM_LBUTTONUP: MouseButtonsPressed.Remove(MouseMessages.WM_LBUTTONDOWN); break;
                case MouseMessages.WM_RBUTTONDOWN: MouseButtonsPressed.Add(MouseMessages.WM_RBUTTONDOWN); break;
                case MouseMessages.WM_RBUTTONUP: MouseButtonsPressed.Remove(MouseMessages.WM_RBUTTONDOWN); break;
                case MouseMessages.WM_MBUTTONDOWN: MouseButtonsPressed.Add(MouseMessages.WM_MBUTTONDOWN); break;
                case MouseMessages.WM_MBUTTONUP: MouseButtonsPressed.Remove(MouseMessages.WM_MBUTTONDOWN); break;
                case MouseMessages.WM_XBUTTONDOWN:
                    if (xButton == 1) _xButton1Pressed = true;
                    else if (xButton == 2) _xButton2Pressed = true;
                    break;
                case MouseMessages.WM_XBUTTONUP:
                    if (xButton == 1) _xButton1Pressed = false;
                    else if (xButton == 2) _xButton2Pressed = false;
                    break;
            }
        }

        /// <summary>
        /// Reconciles tracked state with hardware state via GetAsyncKeyState.
        /// PRUNE-ONLY: removes any key/mouse-button entries whose real state is
        /// "up" - the safety net for lost up-events under load, UAC, or focus
        /// changes. It never adds state, so injected input can't create phantoms.
        /// </summary>
        private static void ReconcileInputState()
        {
            try
            {
                lock (InputLock)
                {
                    List<Keys> stale = null;
                    foreach (var k in KeysPressed)
                    {
                        if ((GetAsyncKeyState((int)k) & 0x8000) == 0)
                            (stale ??= new List<Keys>()).Add(k);
                    }
                    if (stale != null)
                    {
                        foreach (var k in stale)
                        {
                            Trace.WriteLine($"[Reconcile] Pruning stuck key {k}");
                            KeysPressed.Remove(k);
                        }
                    }

                    PruneMouseButton(MouseMessages.WM_LBUTTONDOWN, VK_LBUTTON);
                    PruneMouseButton(MouseMessages.WM_RBUTTONDOWN, VK_RBUTTON);
                    PruneMouseButton(MouseMessages.WM_MBUTTONDOWN, VK_MBUTTON);
                    if (_xButton1Pressed && (GetAsyncKeyState(VK_XBUTTON1) & 0x8000) == 0) _xButton1Pressed = false;
                    if (_xButton2Pressed && (GetAsyncKeyState(VK_XBUTTON2) & 0x8000) == 0) _xButton2Pressed = false;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error reconciling input state: {ex.Message}");
            }
        }

        private static void PruneMouseButton(MouseMessages msg, int vk)
        {
            if (MouseButtonsPressed.Contains(msg) && (GetAsyncKeyState(vk) & 0x8000) == 0)
                MouseButtonsPressed.Remove(msg);
        }

        private static void ClearInputState()
        {
            lock (InputLock)
            {
                KeysPressed.Clear();
                MouseButtonsPressed.Clear();
                _xButton1Pressed = false;
                _xButton2Pressed = false;
            }
        }
        #endregion

        #region Combination matching
        private static bool IsKeyCombinationPressed(KeyCombination combination)
        {
            if (combination.IsEmpty) return false;

            lock (InputLock)
            {
                bool keyPressed = combination.Key == Keys.None || KeysPressed.Contains(combination.Key);

                bool ctrlDown = KeysPressed.Contains(Keys.LControlKey) || KeysPressed.Contains(Keys.RControlKey);
                bool shiftDown = KeysPressed.Contains(Keys.LShiftKey) || KeysPressed.Contains(Keys.RShiftKey);
                bool altDown = KeysPressed.Contains(Keys.LMenu) || KeysPressed.Contains(Keys.RMenu);

                // Required modifiers must be down; non-required modifiers must NOT be down
                // (so e.g. plain "F1" does not fire when Ctrl+F1 is the actual press).
                if (combination.Control != ctrlDown) return false;
                if (combination.Shift != shiftDown) return false;
                if (combination.Alt != altDown) return false;

                bool leftOk = !combination.LeftMouseButton || MouseButtonsPressed.Contains(MouseMessages.WM_LBUTTONDOWN);
                bool rightOk = !combination.RightMouseButton || MouseButtonsPressed.Contains(MouseMessages.WM_RBUTTONDOWN);
                bool midOk = !combination.MiddleMouseButton || MouseButtonsPressed.Contains(MouseMessages.WM_MBUTTONDOWN);
                bool x1Ok = !combination.XButton1 || _xButton1Pressed;
                bool x2Ok = !combination.XButton2 || _xButton2Pressed;

                return keyPressed && leftOk && rightOk && midOk && x1Ok && x2Ok;
            }
        }
        #endregion

        #region Keybind index
        private static void RebuildKeybindIndex()
        {
            try
            {
                var byKey = new Dictionary<Keys, List<KeyValuePair<TRIGGERS, KeyCombination>>>();
                var byMouse = new Dictionary<MouseMessages, List<KeyValuePair<TRIGGERS, KeyCombination>>>();

                var keybinds = ConfigurationService.Current?.Keybinds;
                if (keybinds != null)
                {
                    foreach (var kvp in keybinds)
                    {
                        var combo = kvp.Value;
                        if (combo == null || combo.IsEmpty) continue;
                        combo.Normalize();

                        // Key-only combos are indexed by their non-modifier key.
                        if (combo.Key != Keys.None && !combo.HasMouseButtons)
                        {
                            if (!byKey.TryGetValue(combo.Key, out var list))
                                byKey[combo.Key] = list = new List<KeyValuePair<TRIGGERS, KeyCombination>>();
                            list.Add(kvp);
                        }

                        // Mouse-button combos fire on the matching DOWN event.
                        if (combo.HasMouseButtons)
                        {
                            void AddMouse(MouseMessages m)
                            {
                                if (!byMouse.TryGetValue(m, out var list))
                                    byMouse[m] = list = new List<KeyValuePair<TRIGGERS, KeyCombination>>();
                                list.Add(kvp);
                            }
                            if (combo.LeftMouseButton) AddMouse(MouseMessages.WM_LBUTTONDOWN);
                            if (combo.RightMouseButton) AddMouse(MouseMessages.WM_RBUTTONDOWN);
                            if (combo.MiddleMouseButton) AddMouse(MouseMessages.WM_MBUTTONDOWN);
                            if (combo.XButton1 || combo.XButton2) AddMouse(MouseMessages.WM_XBUTTONDOWN);
                        }
                    }
                }

                lock (KeybindIndexLock)
                {
                    _keybindsByKey = byKey;
                    _keybindsByMouse = byMouse;
                }

                Trace.WriteLine($"Keybind index rebuilt: {byKey.Count} key buckets, {byMouse.Count} mouse buckets.");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error rebuilding keybind index: {ex.Message}");
            }
        }
        #endregion

        #region Actions
        private static void HandlePasteOnAllWindows()
        {
            // Wait for the physical Ctrl/Alt/V of the triggering combination to be
            // released; otherwise the user's real modifiers combine with our
            // injected Ctrl+V inside the game (e.g. Ctrl+Alt+V instead of Ctrl+V).
            WaitForPhysicalRelease(new[] { VK_CONTROL, VK_MENU, (int)Keys.V }, timeoutMs: 2000);

            var windows = WindowManagementService.WindowHandles.ToList();
            foreach (var entry in windows)
            {
                var delay = Random.Next(
                    ConfigurationService.Current.General.MinimumFollowDelay,
                    ConfigurationService.Current.General.MaximumFollowDelay);

                WindowManagementService.SimulateKeyPressListToWindow(
                    entry.Key,
                    new List<Keys> { Keys.LControlKey, Keys.V },
                    delay);
            }
        }

        private static void WaitForPhysicalRelease(int[] vKeys, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                bool anyDown = false;
                foreach (var vk in vKeys)
                {
                    if ((GetAsyncKeyState(vk) & 0x8000) != 0) { anyDown = true; break; }
                }
                if (!anyDown) return;
                Thread.Sleep(15);
            }
        }
        #endregion
    }
}
