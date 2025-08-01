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
        private static readonly Dictionary<TRIGGERS, Action<object>> KeyActions = new Dictionary<TRIGGERS, Action<object>>();
        private static readonly HashSet<Keys> KeysPressed = new HashSet<Keys>();
        private static readonly Random Random = new Random();
        private static POINT _cursorPosition;
        #endregion

        #region Public Events
        /// <summary>
        /// Event raised when the travel menu should be opened
        /// </summary>
        public static event Action ShouldOpenMenuTravel;

        /// <summary>
        /// Event raised when the key bind form should be opened
        /// </summary>
        public static event Action ShouldOpenKeyBindForm;
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the current cursor position
        /// </summary>
        public static POINT CursorPosition => _cursorPosition;
        #endregion

        #region Public Methods
        /// <summary>
        /// Initializes the hook management service
        /// </summary>
        public static void Initialize()
        {
            InitializeKeyActions();
        }

        /// <summary>
        /// Keyboard hook callback function
        /// </summary>
        /// <param name="nCode">Hook code</param>
        /// <param name="wParam">wParam</param>
        /// <param name="lParam">lParam</param>
        /// <returns>Hook result</returns>
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

        /// <summary>
        /// Mouse hook callback function
        /// </summary>
        /// <param name="nCode">Hook code</param>
        /// <param name="wParam">wParam</param>
        /// <param name="lParam">lParam</param>
        /// <returns>Hook result</returns>
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
        /// <summary>
        /// Initializes the key action mappings
        /// </summary>
        private static void InitializeKeyActions()
        {
            KeyActions[TRIGGERS.SELECT_NEXT] = obj => PanelManagementService.SelectNextPanel();
            KeyActions[TRIGGERS.SELECT_PREVIOUS] = obj => PanelManagementService.SelectPreviousPanel();
            KeyActions[TRIGGERS.SIMPLE_CLICK] = obj => WindowManagementService.PerformWindowClick(_cursorPosition, false);
            KeyActions[TRIGGERS.SIMPLE_CLICK_NO_DELAY] = obj => WindowManagementService.PerformWindowClick(_cursorPosition, true);
            KeyActions[TRIGGERS.DOUBLE_CLICK] = obj => WindowManagementService.PerformWindowDoubleClick(_cursorPosition);
            KeyActions[TRIGGERS.GROUP_CHARACTERS] = obj => WindowManagementService.GroupCharacters();
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

        /// <summary>
        /// Handles key down events
        /// </summary>
        /// <param name="key">The pressed key</param>
        private static void HandleKeyDown(Keys key)
        {
            if (key == Keys.Oem7)
            {
                KeysPressed.Add(Keys.Oem7);
            }

            if (KeysPressed.Add(key) && ConfigurationService.Current.Keybinds.Values.Contains(key))
            {
                var trigger = ConfigurationService.Current.Keybinds.First(kvp => kvp.Value == key).Key;
                if (KeyActions.TryGetValue(trigger, out var action))
                {
                    Task.Run(() => action(null));
                }
            }

            // Handle Ctrl+Alt+V for paste on all windows
            if (key == Keys.V && KeysPressed.Contains(Keys.Alt) && KeysPressed.Contains(Keys.LControlKey))
            {
                Task.Run(HandlePasteOnAllWindows);
            }
        }

        /// <summary>
        /// Handles key up events
        /// </summary>
        /// <param name="key">The released key</param>
        private static void HandleKeyUp(Keys key)
        {
            KeysPressed.Remove(key);

            if (key == ConfigurationService.Current.Keybinds[TRIGGERS.TRAVEL])
            {
                ShouldOpenMenuTravel?.Invoke();
            }

            if (key == ConfigurationService.Current.Keybinds[TRIGGERS.OPTIONS])
            {
                ShouldOpenKeyBindForm?.Invoke();
            }
        }

        /// <summary>
        /// Handles mouse messages
        /// </summary>
        /// <param name="message">The mouse message</param>
        /// <param name="lParam">Message parameters</param>
        private static void HandleMouseMessage(MouseMessages message, IntPtr lParam)
        {
            switch (message)
            {
                case MouseMessages.WM_RBUTTONDOWN:
                    if (ConfigurationService.IsModifyingKeyBinds)
                    {
                        KeyBindForm.choosePosition();
                    }
                    break;

                case MouseMessages.WM_LBUTTONDOWN:
                    if (KeysPressed.Contains(Keys.Oem7))
                    {
                        Task.Run(() =>
                        {
                            Trace.WriteLine("Starting price analysis");
                            Thread.Sleep(500);
                            WindowManagementService.FillSellPriceBasedOnForeGroundWindow();
                            KeysPressed.Remove(Keys.Oem7);
                        });
                    }
                    break;

                case MouseMessages.WM_MBUTTONUP:
                    Task.Run(() => WindowManagementService.PerformWindowDoubleClick(_cursorPosition));
                    break;

                case MouseMessages.WM_XBUTTONDOWN:
                    HandleXButtonDown(lParam);
                    break;
            }
        }

        /// <summary>
        /// Handles X button mouse events
        /// </summary>
        /// <param name="lParam">Message parameters</param>
        private static void HandleXButtonDown(IntPtr lParam)
        {
            var hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
            var xButton = (int)(hookStruct.mouseData >> 16);

            Task.Run(() =>
            {
                if (xButton == 1)
                    WindowManagementService.PerformWindowClick(_cursorPosition, false);
                else if (xButton == 2)
                    WindowManagementService.PerformWindowClick(_cursorPosition, true);
            });
        }

        /// <summary>
        /// Handles paste on all windows action
        /// </summary>
        private static void HandlePasteOnAllWindows()
        {
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
