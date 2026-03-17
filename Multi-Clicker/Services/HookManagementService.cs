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

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        // Advanced input simulation APIs
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        // Hardware scan code simulation
        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);
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
        
        // Advanced input constants
        private const uint KEYEVENTF_SCANCODE = 0x0008;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HWND_TOP = new IntPtr(0);
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
        private static readonly Dictionary<TRIGGERS, (Action<object> action, TimeSpan minimumCooldown)> KeyActions = new Dictionary<TRIGGERS, (Action<object>, TimeSpan)>();
        private static readonly HashSet<Keys> KeysPressed = new HashSet<Keys>();
        private static readonly HashSet<MouseMessages> MouseButtonsPressed = new HashSet<MouseMessages>();
        private static bool _xButton1Pressed = false;
        private static bool _xButton2Pressed = false;
        private static readonly Random Random = new Random();
        private static POINT _cursorPosition;
        private static readonly Dictionary<TRIGGERS, DateTime> _lastExecutionTime = new Dictionary<TRIGGERS, DateTime>();
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
        private static bool ExecuteWithCooldown(TRIGGERS trigger, (Action<object> action, TimeSpan minimumCooldown) actionData)
        {
            var now = DateTime.Now;
            
            if (_lastExecutionTime.ContainsKey(trigger))
            {
                var timeSinceLastExecution = now - _lastExecutionTime[trigger];
                if (timeSinceLastExecution < actionData.minimumCooldown)
                {
                    return false;
                }
            }
            
            _lastExecutionTime[trigger] = now;
            Task.Run(() => actionData.action(null));
            return true;
        }

        private static void InitializeKeyActions()
        {
            KeyActions[TRIGGERS.SELECT_NEXT] = (obj => PanelManagementService.SelectNextPanel(), TimeSpan.FromMilliseconds(100));
            KeyActions[TRIGGERS.SELECT_PREVIOUS] = (obj => PanelManagementService.SelectPreviousPanel(), TimeSpan.FromMilliseconds(100));
            KeyActions[TRIGGERS.SIMPLE_CLICK] = (obj => WindowManagementService.PerformWindowClick(_cursorPosition, false), TimeSpan.FromMilliseconds(100));
            KeyActions[TRIGGERS.SIMPLE_CLICK_NO_DELAY] = (obj => WindowManagementService.PerformWindowClick(_cursorPosition, true), TimeSpan.FromMilliseconds(300));
            KeyActions[TRIGGERS.DOUBLE_CLICK] = (obj => WindowManagementService.PerformWindowDoubleClick(_cursorPosition), TimeSpan.FromMilliseconds(100));
            KeyActions[TRIGGERS.GROUP_CHARACTERS] = (obj => WindowManagementService.GroupCharacters(), TimeSpan.FromMilliseconds(1000));
            KeyActions[TRIGGERS.TRAVEL] = (obj => ShouldOpenMenuTravel?.Invoke(), TimeSpan.FromMilliseconds(1000));
            KeyActions[TRIGGERS.OPTIONS] = (obj => ShouldOpenPositionConfiguration?.Invoke(), TimeSpan.FromMilliseconds(1000));
            KeyActions[TRIGGERS.PASTE_ON_ALL_WINDOWS] = (obj => HandlePasteOnAllWindows(), TimeSpan.FromMilliseconds(500));
            KeyActions[TRIGGERS.TOGGLE_AUTOPILOT] = (obj => HandleToggleAutoPilot(), TimeSpan.FromMilliseconds(1000));
            KeyActions[TRIGGERS.FILL_HDV] = (obj =>
            {
                Trace.WriteLine("Starting price analysis");
                Thread.Sleep(500);
                WindowManagementService.FillSellPriceBasedOnForeGroundWindow();
            }, TimeSpan.FromMilliseconds(500));
        }

        /// <summary>
        /// Updates the state of modifier keys
        /// </summary>
        private static void UpdateModifierKeys()
        {
            var isAltPressed = (GetKeyState(0x12) & 0x8000) != 0;
            var isCtrlPressed = (GetKeyState(0x11) & 0x8000) != 0;
            var isShiftPressed = (GetKeyState(0x10) & 0x8000) != 0;

            if (isAltPressed) KeysPressed.Add(Keys.Alt); else KeysPressed.Remove(Keys.Alt);
            if (isCtrlPressed) KeysPressed.Add(Keys.LControlKey); else KeysPressed.Remove(Keys.LControlKey);
            if (isShiftPressed) KeysPressed.Add(Keys.LShiftKey); else KeysPressed.Remove(Keys.LShiftKey);
        }        private static bool IsKeyCombinationPressed(KeyCombination combination)
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

            // Debug: log current key state when F8 is pressed
            if (key == Keys.F8)
            {
                Trace.WriteLine($"F8 pressed. Current KeysPressed: {string.Join(", ", KeysPressed)}");
            }

            // Check all keybind combinations (including those with mouse buttons)
            foreach (var keybind in ConfigurationService.Current.Keybinds)
            {
                if (IsKeyCombinationPressed(keybind.Value))
                {
                    Trace.WriteLine($"Key combination triggered: {keybind.Key} -> {keybind.Value}");
                    if (KeyActions.TryGetValue(keybind.Key, out var actionData))
                    {
                        ExecuteWithCooldown(keybind.Key, actionData);
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
                        Trace.WriteLine($"click combination triggered: {keybind.Key} -> {keybind.Value}");
                        ExecuteWithCooldown(keybind.Key, actionData);
                    }
                }
            }

            // Handle special mouse events
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

        /// <summary>
        /// Handles the toggle autopilot action by sending the command to all windows except the currently selected one
        /// </summary>
        private static void HandleToggleAutoPilot()
        {
            try
            {
                var autoPilotKey = ConfigurationService.Current.Keybinds.ContainsKey(TRIGGERS.DOFUS_AUTOPILOT_SHORTCUT) 
                    ? ConfigurationService.Current.Keybinds[TRIGGERS.DOFUS_AUTOPILOT_SHORTCUT] 
                    : null;
                    
                if (autoPilotKey == null || autoPilotKey.IsEmpty)
                {
                    Trace.WriteLine("DOFUS_AUTOPILOT_SHORTCUT key combination is not configured");
                    return;
                }

                Trace.WriteLine($"Sending autopilot command to all windows except current: {autoPilotKey}");
                var currentWindow = GetForegroundWindow();


                foreach (var entry in WindowManagementService.WindowHandles)
                {

                    if (entry.Key == currentWindow)
                    {
                        Trace.WriteLine($"Skipping current window: {entry.Value.CharacterName}");
                        continue;
                    }

                    Trace.WriteLine($"Sending autopilot to window: {entry.Value.CharacterName}");

                    PanelManagementService.SelectNextPanel();
                    SendAutoPilotKeyCombination(entry.Key, autoPilotKey);
                    SendAutoPilotKeyCombination(entry.Key, autoPilotKey);
                }

                PanelManagementService.SelectNextPanel();

                Trace.WriteLine("AutoPilot command sent to all other windows successfully");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error in HandleToggleAutoPilot: {ex.Message}");
            }
        }


        /// <summary>
        /// Sends autopilot key combination using hardware scan codes with aggressive focus management
        /// </summary>
        private static void SendAutoPilotKeyCombination(IntPtr targetWindow, KeyCombination keyCombination)
        {
            try
            {
                Trace.WriteLine($"AutoPilot key combination to window {targetWindow}: {keyCombination}");
                
                SendScanCodes(keyCombination);
                
                Trace.WriteLine($"AutoPilot scan codes sent to window {targetWindow}");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"AutoPilot key combination error: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends scan codes with detailed verification and timing
        /// </summary>
        private static void SendScanCodes(KeyCombination keyCombination)
        {
            try
            {
                var modifierScanCodes = new List<(byte scanCode, string name)>();
                var mainKeyScanCode = (byte)0;
                
                // Get scan codes for modifiers
                if (keyCombination.Control)
                {
                    byte ctrlScanCode = (byte)MapVirtualKey((uint)Keys.ControlKey, 0);
                    modifierScanCodes.Add((ctrlScanCode, "Ctrl"));
                    Trace.WriteLine($"AutoPilot: Ctrl scan code = {ctrlScanCode}");
                }
                if (keyCombination.Shift)
                {
                    byte shiftScanCode = (byte)MapVirtualKey((uint)Keys.ShiftKey, 0);
                    modifierScanCodes.Add((shiftScanCode, "Shift"));
                    Trace.WriteLine($"AutoPilot: Shift scan code = {shiftScanCode}");
                }
                if (keyCombination.Alt)
                {
                    byte altScanCode = (byte)MapVirtualKey((uint)Keys.Menu, 0);
                    modifierScanCodes.Add((altScanCode, "Alt"));
                    Trace.WriteLine($"AutoPilot: Alt scan code = {altScanCode}");
                }
                
                // Get scan code for main key
                if (keyCombination.Key != Keys.None)
                {
                    mainKeyScanCode = (byte)MapVirtualKey((uint)keyCombination.Key, 0);
                    Trace.WriteLine($"AutoPilot: {keyCombination.Key} scan code = {mainKeyScanCode}");
                }
                
                // Send modifier keys DOWN with verification
                foreach (var (scanCode, name) in modifierScanCodes)
                {
                    keybd_event(0, scanCode, KEYEVENTF_SCANCODE, 0);
                    Trace.WriteLine($"AutoPilot: {name} (scan {scanCode}) DOWN");
                    Thread.Sleep(5); // Small delay between modifiers
                }
                
                // Send main key DOWN and UP with proper timing
                if (mainKeyScanCode != 0)
                {
                    keybd_event(0, mainKeyScanCode, KEYEVENTF_SCANCODE, 0);
                    Trace.WriteLine($"AutoPilot: {keyCombination.Key} (scan {mainKeyScanCode}) DOWN");
                    
                    // Hold the key for a realistic duration
                    Thread.Sleep(20);
                    
                    keybd_event(0, mainKeyScanCode, KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP, 0);
                    Trace.WriteLine($"AutoPilot: {keyCombination.Key} (scan {mainKeyScanCode}) UP");
                }
                
                // Release modifier keys UP in reverse order
                for (int i = modifierScanCodes.Count - 1; i >= 0; i--)
                {
                    var (scanCode, name) = modifierScanCodes[i];
                    Thread.Sleep(5); // Small delay between releases
                    keybd_event(0, scanCode, KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP, 0);
                    Trace.WriteLine($"AutoPilot: {name} (scan {scanCode}) UP");
                }
                
                // Final verification delay
                Thread.Sleep(50);
                Trace.WriteLine("AutoPilot: Scan code sequence completed");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"SendScanCodesWithVerification error: {ex.Message}");
            }
        }
        #endregion
    }
}
