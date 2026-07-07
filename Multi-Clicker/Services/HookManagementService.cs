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

        // Fire-on-release model. When a combination is satisfied on a key/mouse
        // DOWN we do not execute immediately; we "arm" the trigger keyed by the
        // triggering key/button and execute it on the matching UP. This
        // guarantees no physical key/button is held when a broadcast starts
        // (Windows blocks SetForegroundWindow while input is captured), which is
        // what made the first target of a sweep miss its click. It also gives a
        // single, consistent "fires just after you release the shortcut" feel.
        private struct ArmedTrigger
        {
            public TRIGGERS Trigger;
            public Action<object> Action;
            public TimeSpan Cooldown;
            public POINT Cursor;
        }
        private static readonly object ArmLock = new object();
        private static readonly Dictionary<Keys, List<ArmedTrigger>> _armedByKey =
            new Dictionary<Keys, List<ArmedTrigger>>();
        private static readonly Dictionary<int, List<ArmedTrigger>> _armedByMouse =
            new Dictionary<int, List<ArmedTrigger>>();

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
            set
            {
                _triggersSuspended = value;
                // Drop anything armed just before suspension so it can't fire
                // while (or right after) the keybinds dialog is open.
                if (value) ClearArms();
            }
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

                // Arm matching triggers on the initial down (no auto-repeat) when
                // the foreground is a tracked window; execute them on the release.
                if (isDown && wasNewPress && !_triggersSuspended && !ConfigurationService.IsModifyingKeyBinds)
                {
                    var fg = GetForegroundWindow();
                    if (WindowManagementService.IsRelatedHandle(fg))
                    {
                        ArmKeyTriggers(key);
                    }
                }
                else if (isUp)
                {
                    FireArmedKey(key);
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
                bool isMouseUp =
                    message == MouseMessages.WM_LBUTTONUP ||
                    message == MouseMessages.WM_RBUTTONUP ||
                    message == MouseMessages.WM_MBUTTONUP ||
                    message == MouseMessages.WM_XBUTTONUP;

                // Execute any trigger armed on the matching button's DOWN, now
                // that the button has been released.
                if (isMouseUp)
                {
                    int upButtonId = MouseButtonId(message, xButton);
                    if (upButtonId >= 0) FireArmedMouseButton(upButtonId);
                    return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
                }

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
                    ArmMouseTriggers(message, xButton, mouseHookStruct.pt);
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

        #region Trigger arming (fire on release)
        /// <summary>
        /// Maps a mouse message + xButton payload to a stable button id used to
        /// pair a DOWN arm with its UP execution. -1 for non-button messages.
        /// </summary>
        private static int MouseButtonId(MouseMessages message, int xButton)
        {
            switch (message)
            {
                case MouseMessages.WM_LBUTTONDOWN:
                case MouseMessages.WM_LBUTTONUP: return 0;
                case MouseMessages.WM_RBUTTONDOWN:
                case MouseMessages.WM_RBUTTONUP: return 1;
                case MouseMessages.WM_MBUTTONDOWN:
                case MouseMessages.WM_MBUTTONUP: return 2;
                case MouseMessages.WM_XBUTTONDOWN:
                case MouseMessages.WM_XBUTTONUP: return xButton == 2 ? 4 : 3;
                default: return -1;
            }
        }

        private static int MouseButtonVk(int buttonId)
        {
            switch (buttonId)
            {
                case 0: return VK_LBUTTON;
                case 1: return VK_RBUTTON;
                case 2: return VK_MBUTTON;
                case 3: return VK_XBUTTON1;
                case 4: return VK_XBUTTON2;
                default: return 0;
            }
        }

        private static void Arm(Dictionary<int, List<ArmedTrigger>> map, int id, ArmedTrigger armed)
        {
            lock (ArmLock)
            {
                if (!map.TryGetValue(id, out var list))
                    map[id] = list = new List<ArmedTrigger>();
                // Replace an existing arm for the same trigger (re-press before release).
                list.RemoveAll(a => a.Trigger == armed.Trigger);
                list.Add(armed);
            }
        }

        private static void ArmKey(Keys id, ArmedTrigger armed)
        {
            lock (ArmLock)
            {
                if (!_armedByKey.TryGetValue(id, out var list))
                    _armedByKey[id] = list = new List<ArmedTrigger>();
                list.RemoveAll(a => a.Trigger == armed.Trigger);
                list.Add(armed);
            }
        }

        private static void ArmKeyTriggers(Keys pressedKey)
        {
            List<KeyValuePair<TRIGGERS, KeyCombination>> candidates;
            lock (KeybindIndexLock)
            {
                if (!_keybindsByKey.TryGetValue(pressedKey, out candidates) || candidates.Count == 0)
                    return;
                candidates = new List<KeyValuePair<TRIGGERS, KeyCombination>>(candidates);
            }

            // Snapshot the cursor at press time (used by click broadcasts bound
            // to a key); the position must be where the user acted.
            POINT cursor;
            if (!GetCursorPos(out cursor)) cursor = _cursorPosition;

            foreach (var kvp in candidates)
            {
                if (kvp.Value.HasMouseButtons) continue;
                if (!IsKeyCombinationPressed(kvp.Value)) continue;
                if (!KeyActions.TryGetValue(kvp.Key, out var actionData)) continue;

                Trace.WriteLine($"Key combination armed: {kvp.Key} -> {kvp.Value}");
                ArmKey(pressedKey, new ArmedTrigger
                {
                    Trigger = kvp.Key,
                    Action = actionData.action,
                    Cooldown = actionData.minimumCooldown,
                    Cursor = cursor
                });
            }
        }

        private static void ArmMouseTriggers(MouseMessages downMessage, int xButton, POINT cursor)
        {
            List<KeyValuePair<TRIGGERS, KeyCombination>> candidates;
            lock (KeybindIndexLock)
            {
                if (!_keybindsByMouse.TryGetValue(downMessage, out candidates) || candidates.Count == 0)
                    return;
                candidates = new List<KeyValuePair<TRIGGERS, KeyCombination>>(candidates);
            }

            int buttonId = MouseButtonId(downMessage, xButton);
            if (buttonId < 0) return;

            foreach (var kvp in candidates)
            {
                // Edge-trigger on the button that actually went down: an X1-bound
                // combo must not arm because X2 was pressed.
                if (downMessage == MouseMessages.WM_XBUTTONDOWN)
                {
                    bool matchesEvent = (kvp.Value.XButton1 && xButton == 1) ||
                                        (kvp.Value.XButton2 && xButton == 2);
                    if (!matchesEvent) continue;
                }

                if (!IsKeyCombinationPressed(kvp.Value)) continue;
                if (!KeyActions.TryGetValue(kvp.Key, out var actionData)) continue;

                Trace.WriteLine($"Click combination armed: {kvp.Key} -> {kvp.Value}");
                Arm(_armedByMouse, buttonId, new ArmedTrigger
                {
                    Trigger = kvp.Key,
                    Action = actionData.action,
                    Cooldown = actionData.minimumCooldown,
                    Cursor = cursor
                });
            }
        }

        private static void FireArmedKey(Keys releasedKey)
        {
            List<ArmedTrigger> armed;
            lock (ArmLock)
            {
                if (!_armedByKey.TryGetValue(releasedKey, out armed) || armed.Count == 0) return;
                _armedByKey.Remove(releasedKey);
            }
            foreach (var a in armed)
            {
                Trace.WriteLine($"Key trigger fired on release: {a.Trigger}");
                EnqueueTrigger(a.Trigger, (a.Action, a.Cooldown), a.Cursor);
            }
        }

        private static void FireArmedMouseButton(int buttonId)
        {
            List<ArmedTrigger> armed;
            lock (ArmLock)
            {
                if (!_armedByMouse.TryGetValue(buttonId, out armed) || armed.Count == 0) return;
                _armedByMouse.Remove(buttonId);
            }
            foreach (var a in armed)
            {
                Trace.WriteLine($"Click trigger fired on release: {a.Trigger}");
                EnqueueTrigger(a.Trigger, (a.Action, a.Cooldown), a.Cursor);
            }
        }

        /// <summary>
        /// Safety net: if a release event was lost (focus change, load), fire any
        /// armed trigger whose key/button the OS now reports as up. Runs from the
        /// reconcile timer, outside InputLock, to avoid a lock-order inversion
        /// with the arm path (which takes ArmLock then InputLock).
        /// </summary>
        private static void FireStaleArms()
        {
            List<Keys> armedKeys;
            List<int> armedButtons;
            lock (ArmLock)
            {
                if (_armedByKey.Count == 0 && _armedByMouse.Count == 0) return;
                armedKeys = new List<Keys>(_armedByKey.Keys);
                armedButtons = new List<int>(_armedByMouse.Keys);
            }

            foreach (var k in armedKeys)
            {
                if ((GetAsyncKeyState((int)k) & 0x8000) == 0)
                {
                    Trace.WriteLine($"[Reconcile] Firing armed key {k} (release event was missed).");
                    FireArmedKey(k);
                }
            }
            foreach (var b in armedButtons)
            {
                if ((GetAsyncKeyState(MouseButtonVk(b)) & 0x8000) == 0)
                {
                    Trace.WriteLine($"[Reconcile] Firing armed button {b} (release event was missed).");
                    FireArmedMouseButton(b);
                }
            }
        }

        private static void ClearArms()
        {
            lock (ArmLock)
            {
                _armedByKey.Clear();
                _armedByMouse.Clear();
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

                // Executed outside InputLock: fires arms whose release we missed.
                FireStaleArms();
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
            ClearArms();
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
