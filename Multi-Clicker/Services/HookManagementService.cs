using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MultiClicker.Models;
using MultiClicker.Services;

namespace MultiClicker.Services
{
    /// <summary>
    /// Service responsible for managing global hooks and input handling
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

        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WH_MOUSE_LL = 14;
        public const int WH_KEYBOARD_LL = 13;
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
        #endregion

        #region Private Fields
        private static readonly Dictionary<TRIGGERS, (Action<object> action, bool hasCooldown)> KeyActions = new Dictionary<TRIGGERS, (Action<object>, bool)>();
        private static readonly HashSet<Keys> KeysPressed = new HashSet<Keys>();
        private static readonly HashSet<MouseMessages> MouseButtonsPressed = new HashSet<MouseMessages>();
        private static bool _xButton1Pressed = false;
        private static bool _xButton2Pressed = false;
        private static readonly Random Random = new Random();
        private static POINT _cursorPosition;
        private static readonly Dictionary<TRIGGERS, DateTime> _lastExecutionTime = new Dictionary<TRIGGERS, DateTime>();
        private static readonly TimeSpan _executionCooldown = TimeSpan.FromMilliseconds(500); // 500ms cooldown
        #endregion

        #region Public Events
        public static event Action ShouldOpenMenuTravel;
        public static event Action ShouldOpenPositionConfiguration;
        #endregion

        #region Public Properties
        public static POINT CursorPosition => _cursorPosition;
        #endregion

        #region Public Methods
        public static void Initialize()
        {
            InitializeKeyActions();
        }

        public static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0 || !WindowManagementService.IsRelatedHandle(GetForegroundWindow()))
                return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

            try
            {
                var key = (Keys)Marshal.ReadInt32(lParam);
                UpdateModifierKeys();

                if (wParam == (IntPtr)WM_KEYDOWN)
                {
                    HandleKeyDown(key);
                }
                else if (wParam == (IntPtr)WM_KEYUP)
                {
                    HandleKeyUp(key);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error in keyboard hook callback: {ex.Message}");
            }

            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        public static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0 || !WindowManagementService.IsRelatedHandle(GetForegroundWindow()))
                return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

            try
            {
                GetCursorPos(out _cursorPosition);
                var hWnd = WindowFromPoint(_cursorPosition);
                var message = (MouseMessages)wParam;
                UpdateModifierKeys();

                if (!WindowManagementService.WindowHandles.ContainsKey(hWnd))
                    return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

                HandleMouseMessage(message, lParam);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error in mouse hook callback: {ex.Message}");
            }

            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }
        #endregion

        #region Private Methods
        private static bool ExecuteWithCooldown(TRIGGERS trigger, Action<object> action)
        {
            var now = DateTime.Now;
            
            if (_lastExecutionTime.ContainsKey(trigger))
            {
                var timeSinceLastExecution = now - _lastExecutionTime[trigger];
                if (timeSinceLastExecution < _executionCooldown)
                {
                    return false; // Still in cooldown period
                }
            }
            
            _lastExecutionTime[trigger] = now;
            Task.Run(() => action(null));
            return true;
        }

        private static void InitializeKeyActions()
        {
            KeyActions[TRIGGERS.SELECT_NEXT] = (obj => PanelManagementService.SelectNextPanel(), false);
            KeyActions[TRIGGERS.SELECT_PREVIOUS] = (obj => PanelManagementService.SelectPreviousPanel(), false);
            KeyActions[TRIGGERS.SIMPLE_CLICK] = (obj => WindowManagementService.PerformWindowClick(_cursorPosition, false), false);
            KeyActions[TRIGGERS.SIMPLE_CLICK_NO_DELAY] = (obj => WindowManagementService.PerformWindowClick(_cursorPosition, true), false);
            KeyActions[TRIGGERS.DOUBLE_CLICK] = (obj => WindowManagementService.PerformWindowDoubleClick(_cursorPosition), false);
            KeyActions[TRIGGERS.GROUP_CHARACTERS] = (obj => WindowManagementService.GroupCharacters(), true);
            KeyActions[TRIGGERS.TRAVEL] = (obj => ShouldOpenMenuTravel?.Invoke(), true);
            KeyActions[TRIGGERS.OPTIONS] = (obj => ShouldOpenPositionConfiguration?.Invoke(), true);
            KeyActions[TRIGGERS.PASTE_ON_ALL_WINDOWS] = (obj => HandlePasteOnAllWindows(), true);
            KeyActions[TRIGGERS.FILL_HDV] = (obj =>
            {
                Trace.WriteLine("Starting price analysis");
                Thread.Sleep(500);
                WindowManagementService.FillSellPriceBasedOnForeGroundWindow();
            }, true);
        }

/// <summary>
/// Updates the state of modifier keys
/// </summary>
private static void UpdateModifierKeys()
        {
            var isAltPressed = (GetKeyState(0x12) & 0x8000) != 0;
            var isCtrlPressed = (GetKeyState(0x11) & 0x8000) != 0;

            if (isAltPressed) KeysPressed.Add(Keys.Alt); else KeysPressed.Remove(Keys.Alt);
            if (isCtrlPressed) KeysPressed.Add(Keys.LControlKey); else KeysPressed.Remove(Keys.LControlKey);
        }

        private static bool IsKeyCombinationPressed(KeyCombination combination)
        {
            if (combination.IsEmpty) return false;

            // Check keyboard modifiers and key
            bool controlPressed = combination.Control && (KeysPressed.Contains(Keys.LControlKey) || KeysPressed.Contains(Keys.RControlKey));
            bool shiftPressed = combination.Shift && (KeysPressed.Contains(Keys.LShiftKey) || KeysPressed.Contains(Keys.RShiftKey));
            bool altPressed = combination.Alt && (KeysPressed.Contains(Keys.LMenu) || KeysPressed.Contains(Keys.RMenu));
            bool keyPressed = combination.Key == Keys.None || KeysPressed.Contains(combination.Key);

            // Check mouse buttons
            bool leftMousePressed = !combination.LeftMouseButton || MouseButtonsPressed.Contains(MouseMessages.WM_LBUTTONDOWN);
            bool rightMousePressed = !combination.RightMouseButton || MouseButtonsPressed.Contains(MouseMessages.WM_RBUTTONDOWN);
            bool middleMousePressed = !combination.MiddleMouseButton || MouseButtonsPressed.Contains(MouseMessages.WM_MBUTTONDOWN);
            bool xButton1Pressed = !combination.XButton1 || _xButton1Pressed;
            bool xButton2Pressed = !combination.XButton2 || _xButton2Pressed;

            // If no modifiers required, only check the key and mouse buttons
            if (!combination.Control && !combination.Shift && !combination.Alt)
            {
                return keyPressed && leftMousePressed && rightMousePressed && middleMousePressed && xButton1Pressed && xButton2Pressed;
            }

            // Check that all required modifiers are pressed and no extra modifiers
            bool controlMatch = combination.Control ? controlPressed : !KeysPressed.Contains(Keys.LControlKey) && !KeysPressed.Contains(Keys.RControlKey);
            bool shiftMatch = combination.Shift ? shiftPressed : !KeysPressed.Contains(Keys.LShiftKey) && !KeysPressed.Contains(Keys.RShiftKey);
            bool altMatch = combination.Alt ? altPressed : !KeysPressed.Contains(Keys.LMenu) && !KeysPressed.Contains(Keys.RMenu);

            return keyPressed && controlMatch && shiftMatch && altMatch && leftMousePressed && rightMousePressed && middleMousePressed && xButton1Pressed && xButton2Pressed;
        }

        private static void HandleKeyDown(Keys key)
        {
            KeysPressed.Add(key);

            // Check all keybind combinations (including those with mouse buttons)
            foreach (var keybind in ConfigurationService.Current.Keybinds)
            {
                if (IsKeyCombinationPressed(keybind.Value))
                {
                    if (KeyActions.TryGetValue(keybind.Key, out var actionData))
                    {
                        if (actionData.hasCooldown)
                        {
                            if (ExecuteWithCooldown(keybind.Key, actionData.action))
                            {
                                return;
                            }
                        }
                        else
                        {
                            // Execute directly without cooldown
                            Task.Run(() => actionData.action(null));
                            return;
                        }
                    }
                }
            }
        }

        private static void HandleKeyUp(Keys key)
        {
            KeysPressed.Remove(key);
        }

        private static void HandleMouseMessage(MouseMessages message, IntPtr lParam)
        {
            // Track mouse button states for combination checking
            switch (message)
            {
                case MouseMessages.WM_LBUTTONDOWN:
                    MouseButtonsPressed.Add(MouseMessages.WM_LBUTTONDOWN);
                    break;
                case MouseMessages.WM_LBUTTONUP:
                    MouseButtonsPressed.Remove(MouseMessages.WM_LBUTTONDOWN);
                    break;
                case MouseMessages.WM_RBUTTONDOWN:
                    MouseButtonsPressed.Add(MouseMessages.WM_RBUTTONDOWN);
                    break;
                case MouseMessages.WM_RBUTTONUP:
                    MouseButtonsPressed.Remove(MouseMessages.WM_RBUTTONDOWN);
                    break;
                case MouseMessages.WM_MBUTTONDOWN:
                    MouseButtonsPressed.Add(MouseMessages.WM_MBUTTONDOWN);
                    break;
                case MouseMessages.WM_MBUTTONUP:
                    MouseButtonsPressed.Remove(MouseMessages.WM_MBUTTONDOWN);
                    break;
                case MouseMessages.WM_XBUTTONDOWN:
                    HandleXButtonState(lParam, true);
                    break;
                case MouseMessages.WM_XBUTTONUP:
                    HandleXButtonState(lParam, false);
                    break;
            }

            // Check for keybind combinations that include mouse buttons
            foreach (var keybind in ConfigurationService.Current.Keybinds)
            {
                if (keybind.Value.HasMouseButtons && IsKeyCombinationPressed(keybind.Value))
                {
                    if (KeyActions.TryGetValue(keybind.Key, out var actionData))
                    {
                        bool executed = false;
                        if (actionData.hasCooldown)
                        {
                            executed = ExecuteWithCooldown(keybind.Key, actionData.action);
                        }
                        else
                        {
                            // Execute directly without cooldown
                            Task.Run(() => actionData.action(null));
                            executed = true;
                        }
                        
                        if (executed)
                        {
                            return; // Only execute one action per event
                        }
                    }
                }
            }

            // Handle original mouse events
            switch (message)
            {
                case MouseMessages.WM_RBUTTONDOWN:
                    if (ConfigurationService.IsModifyingKeyBinds)
                    {
                        PositionConfigurationForm.choosePosition();
                    }
                    break;
            }
        }

        private static void HandleXButtonState(IntPtr lParam, bool isPressed)
        {
            var hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
            var xButton = (int)(hookStruct.mouseData >> 16);

            if (xButton == 1)
            {
                _xButton1Pressed = isPressed;
            }
            else if (xButton == 2)
            {
                _xButton2Pressed = isPressed;
            }
        }

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
