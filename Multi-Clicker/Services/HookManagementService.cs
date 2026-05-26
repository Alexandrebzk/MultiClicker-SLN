using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MultiClicker.Models;

namespace MultiClicker.Services
{
    /// <summary>
    /// Service responsible for managing global hooks and input handling.
    ///
    /// Design notes (post-audit):
    ///  * Input state (KeysPressed / mouse button flags) is always updated for
    ///    key/mouse-up events, regardless of foreground window, so that focus
    ///    changes can never leave a key "stuck" pressed.
    ///  * Trigger evaluation is edge-triggered: a key-down only fires keybinds
    ///    whose primary key matches the key that just went down; mouse-down
    ///    only fires keybinds whose required mouse button matches the event.
    ///  * A periodic reconciliation pass uses GetAsyncKeyState to prune any
    ///    state that drifted out of sync with the OS (lost up-events, etc.).
    ///  * Per-event work inside the hook callback is kept minimal; all heavy
    ///    actions are dispatched via Task.Run to avoid LowLevelHooksTimeout.
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
        public static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        static extern short GetKeyState(int nVirtKey);

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

        // Virtual key codes used by reconciliation.
        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;
        private const int VK_MBUTTON = 0x04;
        private const int VK_XBUTTON1 = 0x05;
        private const int VK_XBUTTON2 = 0x06;
        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12; // Alt
        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;
        private const int VK_LCONTROL = 0xA2;
        private const int VK_RCONTROL = 0xA3;
        private const int VK_LMENU = 0xA4;
        private const int VK_RMENU = 0xA5;

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

        // LLKHF_INJECTED / LLMHF_INJECTED — set by the OS when the event was
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

        // Pre-computed keybind indices for O(1) edge-triggered lookup.
        // Rebuilt on init and whenever ConfigurationService.KeybindsChanged fires.
        private static readonly object KeybindIndexLock = new object();
        private static Dictionary<Keys, List<KeyValuePair<TRIGGERS, KeyCombination>>> _keybindsByKey =
            new Dictionary<Keys, List<KeyValuePair<TRIGGERS, KeyCombination>>>();
        private static Dictionary<MouseMessages, List<KeyValuePair<TRIGGERS, KeyCombination>>> _keybindsByMouse =
            new Dictionary<MouseMessages, List<KeyValuePair<TRIGGERS, KeyCombination>>>();

        private static System.Threading.Timer _reconcileTimer;
        private static volatile bool _initialized;
        #endregion

        #region Public Events
        public static event Action ShouldOpenPositionConfiguration;
        #endregion

        #region Public Properties
        public static POINT CursorPosition => _cursorPosition;
        #endregion

        #region Public Methods
        public static void Initialize()
        {
            if (_initialized) return;
            InitializeKeyActions();
            RebuildKeybindIndex();
            ConfigurationService.KeybindsChanged += RebuildKeybindIndex;

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
                // PerformForegroundClick, SetHandleToForeground, SimulateKeyPressListToWindow,
                // etc.). Without this, our own clicks would re-enter the hook, leave
                // modifier/mouse state "pressed", and any subsequent real key/click would
                // re-satisfy the combination and re-fire the trigger.
                if ((hookStruct.flags & LLHF_INJECTED) != 0)
                    return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

                var key = (Keys)hookStruct.vkCode;

                // Always reconcile modifiers and update key state, even when the
                // foreground window is not a tracked one. This guarantees that
                // key-up events are never lost across focus changes.
                bool wasNewPress;
                lock (InputLock)
                {
                    UpdateModifierKeysLocked();
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
                if (isDown && wasNewPress)
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

                // Ignore events we synthesized ourselves (SendInput in
                // PerformForegroundClick). Otherwise our own click re-enters this
                // hook, marks the left mouse button as "down", and the next real
                // input event re-satisfies a mouse-bound combination, re-firing
                // the trigger.
                var mouseHookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if ((mouseHookStruct.flags & LLHF_INJECTED) != 0)
                    return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

                // Always update mouse button state, regardless of foreground.
                // This prevents losing UP events when the user drags off a
                // tracked window before releasing.
                lock (InputLock)
                {
                    UpdateMouseButtonStateLocked(message, lParam);
                    UpdateModifierKeysLocked();
                }

                bool isMouseDown =
                    message == MouseMessages.WM_LBUTTONDOWN ||
                    message == MouseMessages.WM_RBUTTONDOWN ||
                    message == MouseMessages.WM_MBUTTONDOWN ||
                    message == MouseMessages.WM_XBUTTONDOWN;

                if (!isMouseDown)
                    return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

                // Update cursor position lazily (only when a click event arrives).
                GetCursorPos(out _cursorPosition);
                var hWnd = WindowFromPoint(_cursorPosition);

                var fg = GetForegroundWindow();
                bool fgRelated = WindowManagementService.IsRelatedHandle(fg);
                bool hoverRelated = WindowManagementService.WindowHandles.ContainsKey(hWnd);

                // Special right-click handler for keybind position picker - this
                // intentionally fires regardless of foreground.
                if (message == MouseMessages.WM_RBUTTONDOWN && ConfigurationService.IsModifyingKeyBinds)
                {
                    PositionConfigurationForm.choosePosition();
                }

                if (fgRelated && hoverRelated)
                {
                    EvaluateMouseTriggers(message);
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
                        ExecuteWithCooldown(kvp.Key, actionData);
                    }
                }
            }
        }

        private static void EvaluateMouseTriggers(MouseMessages downMessage)
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
                if (IsKeyCombinationPressed(kvp.Value))
                {
                    Trace.WriteLine($"Click combination triggered: {kvp.Key} -> {kvp.Value}");
                    if (KeyActions.TryGetValue(kvp.Key, out var actionData))
                    {
                        ExecuteWithCooldown(kvp.Key, actionData);
                    }
                }
            }
        }
        #endregion

        #region Cooldown & dispatch
        private static bool ExecuteWithCooldown(TRIGGERS trigger, (Action<object> action, TimeSpan minimumCooldown) actionData)
        {
            var now = DateTime.UtcNow;

            // Reentrancy guard with a stale-flag watchdog. If a trigger was marked
            // running more than 10x its cooldown ago, assume the task died or
            // failed to schedule and force-clear so the user is not locked out.
            if (_runningTriggers.TryGetValue(trigger, out var startedAt))
            {
                var stuckThreshold = TimeSpan.FromTicks(Math.Max(actionData.minimumCooldown.Ticks * 10,
                    TimeSpan.FromSeconds(5).Ticks));
                if (now - startedAt < stuckThreshold)
                {
                    Trace.WriteLine($"Trigger {trigger} is already running, skipping execution.");
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

            _lastExecutionTime[trigger] = now;
            _runningTriggers[trigger] = now;

            Task.Run(() =>
            {
                try
                {
                    actionData.action(null);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Error executing trigger {trigger}: {ex.Message}");
                }
                finally
                {
                    _runningTriggers.TryRemove(trigger, out _);
                }
            });

            return true;
        }
        #endregion

        #region State helpers (must be called under InputLock unless noted)
        private static void InitializeKeyActions()
        {
            KeyActions[TRIGGERS.SELECT_NEXT] = (obj => PanelManagementService.SelectNextPanel(), TimeSpan.FromMilliseconds(100));
            KeyActions[TRIGGERS.SELECT_PREVIOUS] = (obj => PanelManagementService.SelectPreviousPanel(), TimeSpan.FromMilliseconds(100));
            KeyActions[TRIGGERS.SIMPLE_CLICK] = (obj => WindowManagementService.PerformWindowClick(_cursorPosition, false), TimeSpan.FromMilliseconds(100));
            KeyActions[TRIGGERS.DOUBLE_CLICK] = (obj => WindowManagementService.PerformWindowDoubleClick(_cursorPosition), TimeSpan.FromMilliseconds(100));
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
        /// Synchronises tracked modifier state with the OS, treating L/R variants
        /// independently so a missed up-event on one side cannot leave the other
        /// side stuck.
        /// </summary>
        private static void UpdateModifierKeysLocked()
        {
            SetKeyState(Keys.LControlKey, (GetKeyState(VK_LCONTROL) & 0x8000) != 0);
            SetKeyState(Keys.RControlKey, (GetKeyState(VK_RCONTROL) & 0x8000) != 0);
            SetKeyState(Keys.LShiftKey, (GetKeyState(VK_LSHIFT) & 0x8000) != 0);
            SetKeyState(Keys.RShiftKey, (GetKeyState(VK_RSHIFT) & 0x8000) != 0);
            SetKeyState(Keys.LMenu, (GetKeyState(VK_LMENU) & 0x8000) != 0);
            SetKeyState(Keys.RMenu, (GetKeyState(VK_RMENU) & 0x8000) != 0);

            // Legacy "Keys.Alt" flag kept for any code that still inspects it.
            SetKeyState(Keys.Alt, (GetKeyState(VK_MENU) & 0x8000) != 0);
        }

        private static void SetKeyState(Keys key, bool pressed)
        {
            if (pressed) KeysPressed.Add(key);
            else KeysPressed.Remove(key);
        }

        private static void UpdateMouseButtonStateLocked(MouseMessages message, IntPtr lParam)
        {
            switch (message)
            {
                case MouseMessages.WM_LBUTTONDOWN: MouseButtonsPressed.Add(MouseMessages.WM_LBUTTONDOWN); break;
                case MouseMessages.WM_LBUTTONUP: MouseButtonsPressed.Remove(MouseMessages.WM_LBUTTONDOWN); break;
                case MouseMessages.WM_RBUTTONDOWN: MouseButtonsPressed.Add(MouseMessages.WM_RBUTTONDOWN); break;
                case MouseMessages.WM_RBUTTONUP: MouseButtonsPressed.Remove(MouseMessages.WM_RBUTTONDOWN); break;
                case MouseMessages.WM_MBUTTONDOWN: MouseButtonsPressed.Add(MouseMessages.WM_MBUTTONDOWN); break;
                case MouseMessages.WM_MBUTTONUP: MouseButtonsPressed.Remove(MouseMessages.WM_MBUTTONDOWN); break;
                case MouseMessages.WM_XBUTTONDOWN: SetXButtonStateLocked(lParam, true); break;
                case MouseMessages.WM_XBUTTONUP: SetXButtonStateLocked(lParam, false); break;
            }
        }

        private static void SetXButtonStateLocked(IntPtr lParam, bool isPressed)
        {
            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var xButton = (int)(hookStruct.mouseData >> 16);
            if (xButton == 1) _xButton1Pressed = isPressed;
            else if (xButton == 2) _xButton2Pressed = isPressed;
        }

        /// <summary>
        /// Reconciles tracked state with hardware state via GetAsyncKeyState.
        /// Removes any key/mouse-button entries whose real state is "up" - this
        /// is the safety net for lost up-events under load, UAC, or focus changes.
        /// </summary>
        private static void ReconcileInputState()
        {
            try
            {
                lock (InputLock)
                {
                    UpdateModifierKeysLocked();

                    // Prune any non-modifier keys that the OS says are no longer down.
                    var stale = new List<Keys>();
                    foreach (var k in KeysPressed)
                    {
                        if (IsModifierKey(k)) continue;
                        if ((GetAsyncKeyState((int)k) & 0x8000) == 0)
                            stale.Add(k);
                    }
                    foreach (var k in stale) KeysPressed.Remove(k);

                    // Mouse buttons.
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

        private static bool IsModifierKey(Keys k)
        {
            return k == Keys.LControlKey || k == Keys.RControlKey
                || k == Keys.LShiftKey || k == Keys.RShiftKey
                || k == Keys.LMenu || k == Keys.RMenu
                || k == Keys.Alt || k == Keys.Control || k == Keys.Shift;
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
                bool altDown = KeysPressed.Contains(Keys.LMenu) || KeysPressed.Contains(Keys.RMenu) || KeysPressed.Contains(Keys.Alt);

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

                        // Key-only combos (and combos using a key as their primary
                        // trigger) are indexed by their non-modifier key.
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
            Thread.Sleep(500);
            var delay = Random.Next(
                ConfigurationService.Current.General.MinimumFollowDelay,
                ConfigurationService.Current.General.MaximumFollowDelay);

            foreach (var entry in WindowManagementService.WindowHandles)
            {
                PanelManagementService.SelectNextPanel();
                WindowManagementService.SimulateKeyPressListToWindow(
                    entry.Key,
                    new List<Keys> { Keys.LControlKey, Keys.V },
                    delay);
            }
        }
        #endregion
    }
}
