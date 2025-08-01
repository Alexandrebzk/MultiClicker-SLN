using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Tesseract;
using MultiClicker.Models;
using System.Threading;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace MultiClicker.Services
{
    /// <summary>
    /// Service responsible for window management operations
    /// </summary>
    public static class WindowManagementService
    {
        #region Private Constants and Fields
        private const int ALT = 0xA4;
        private const int EXTENDEDKEY = 0x1;
        public const int SM_CYCAPTION = 4;
        private const int KEYUP = 0x2;
        private const uint Restore = 9;
        private const int ClickOffset = 5;
        private const int TitleBarOffset = 4;
        private const int BinarizationThreshold = 140;
        private const int MaxClickRetries = 3; // Maximum number of click retry attempts
        private static readonly string OcrLanguage = "fra";
        private static readonly string TessdataPath = @"tessdata";
        private static readonly object TessEngineLock = new object();
        private static TesseractEngine _ocrEngine = null;
        #endregion

        #region Win32 API Declarations
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, ref RECT rect);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ShowWindow(IntPtr hWnd, uint Msg);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, UInt32 Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        #endregion

        #region Constants
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;
        #endregion

        #region Input Structures
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Dictionary containing all tracked window handles and their information
        /// </summary>
        public static Dictionary<IntPtr, WindowInfo> WindowHandles { get; private set; } = new Dictionary<IntPtr, WindowInfo>();
        #endregion

        #region Public Methods
        /// <summary>
        /// Finds windows matching the specified title pattern
        /// </summary>
        /// <param name="titlePattern">The pattern to match in window titles</param>
        /// <returns>Dictionary of window handles and their information</returns>
        public static Dictionary<IntPtr, WindowInfo> FindWindows(string titlePattern)
        {
            var foundWindows = new Dictionary<IntPtr, WindowInfo>();

            try
            {
                EnumWindows((hWnd, lParam) =>
                {
                    if (IsWindow(hWnd))
                    {
                        var windowTitle = GetWindowTitle(hWnd);
                        if (!string.IsNullOrEmpty(windowTitle) && windowTitle.Contains(titlePattern))
                        {
                            var characterName = ExtractCharacterName(windowTitle, titlePattern);
                            if (!string.IsNullOrEmpty(characterName))
                            {
                                foundWindows[hWnd] = new WindowInfo
                                {
                                    WindowName = windowTitle,
                                    CharacterName = characterName,
                                    RelatedPanel = null
                                };
                            }
                        }
                    }
                    return true;
                }, IntPtr.Zero);

                WindowHandles = foundWindows;
                Trace.WriteLine($"Found {foundWindows.Count} windows matching pattern: {titlePattern}");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error finding windows: {ex.Message}");
            }

            return foundWindows;
        }

        /// <summary>
        /// Checks if the specified handle is related to tracked windows
        /// </summary>
        /// <param name="handle">The window handle to check</param>
        /// <returns>True if the handle is tracked, false otherwise</returns>
        public static bool IsRelatedHandle(IntPtr handle)
        {
            return WindowHandles.ContainsKey(handle);
        }

        /// <summary>
        /// Sets the specified window to foreground - exact legacy behavior
        /// </summary>
        /// <param name="handle">The window handle</param>
        public static void SetHandleToForeground(IntPtr handle)
        {
            try
            {
                if (IsIconic(handle))
                {
                    ShowWindow(handle, Restore);
                }

                keybd_event((byte)ALT, 0x45, EXTENDEDKEY | 0, 0);
                keybd_event((byte)ALT, 0x45, EXTENDEDKEY | KEYUP, 0);

                SetForegroundWindow(handle);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error setting window to foreground: {ex.Message}");
            }
        }

        /// <summary>
        /// Performs a click operation on windows at the specified position
        /// </summary>
        /// <param name="position">The position to click</param>
        /// <param name="isNoDelay">Whether to use no delay mode</param>
        public static void PerformWindowClick(POINT position, bool isNoDelay)
        {
            try
            {
                var delay = !isNoDelay ? GetRandomDelay() : 0;

                // Use parallel execution for better performance while maintaining reliability
                var tasks = new List<Task>();
                
                foreach (var windowEntry in WindowHandles.ToList()) // ToList to avoid collection modification issues
                {
                    try
                    {
                        if (!isNoDelay)
                        {
                            Thread.Sleep(delay);
                            PerformClickOnWindowWithRetry(windowEntry.Key, position);
                        } else
                        {
                            Task.Run(() => PerformClickOnWindowWithRetry(windowEntry.Key, position));

                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Error clicking on window {windowEntry.Value.WindowName}: {ex.Message}");
                    }
                }

                Task.WhenAll(tasks).ContinueWith(t =>
                {
                    if (t.Exception != null)
                    {
                        Trace.WriteLine($"Some click operations failed: {t.Exception.Flatten().Message}");
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error performing window click: {ex.Message}");
            }
        }

        /// <summary>
        /// Performs a double click operation on windows at the specified position
        /// </summary>
        /// <param name="position">The position to double click</param>
        public static void PerformWindowDoubleClick(POINT position)
        {
            try
            {
                var delay = GetRandomDelay();

                // Use parallel execution for better performance
                var tasks = new List<Task>();
                
                foreach (var windowEntry in WindowHandles.ToList()) // ToList to avoid collection modification issues
                {
                    try
                    {
                        Task.Run(() => PerformDoubleClickOnWindowWithRetry(windowEntry.Key, position));
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Error double clicking on window {windowEntry.Value.WindowName}: {ex.Message}");
                    }
                }

                // Track completion for debugging
                Task.WhenAll(tasks).ContinueWith(t =>
                {
                    if (t.Exception != null)
                    {
                        Trace.WriteLine($"Some double click operations failed: {t.Exception.Flatten().Message}");
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error performing window double click: {ex.Message}");
            }
        }

        /// <summary>
        /// Simulates a key combination with modifiers
        /// </summary>
        /// <param name="windowHandle">The target window handle</param>
        /// <param name="keyCombination">The key combination to simulate</param>
        public static void SimulateKeyCombination(IntPtr windowHandle, KeyCombination keyCombination)
        {
            try
            {
                if (keyCombination.Key == Keys.None) return;

                SetHandleToForeground(windowHandle);
                Thread.Sleep(25);

                var keys = new List<Keys>();
                
                // Add modifiers first
                if (keyCombination.Control) keys.Add(Keys.LControlKey);
                if (keyCombination.Shift) keys.Add(Keys.LShiftKey);
                if (keyCombination.Alt) keys.Add(Keys.LMenu);
                
                // Add the main key
                keys.Add(keyCombination.Key);

                // Simulate the key combination
                SimulateKeyPressListToWindow(windowHandle, keys, 0);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error simulating key combination for window {windowHandle}: {ex.Message}");
            }
        }

        /// <summary>
        /// Simulates key press with special handling for TAB and ENTER keys using scan codes
        /// </summary>
        /// <param name="windowHandle">The target window handle</param>
        /// <param name="key">The key to press</param>
        public static void SimulateKeyPress(IntPtr windowHandle, Keys key)
        {
            try
            {
                SetHandleToForeground(windowHandle);
                Thread.Sleep(25);
                
                if (key == Keys.Tab)
                {
                    SendTab(windowHandle);
                    return;
                }
                
                if (key == Keys.Enter || key == Keys.Return)
                {
                    SendEnter(windowHandle);
                    return;
                }
                
                INPUT[] inputs = new INPUT[2];
                
                inputs[0] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = (ushort)key,
                            dwFlags = 0
                        }
                    }
                };
                
                inputs[1] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = (ushort)key,
                            dwFlags = KEYEVENTF_KEYUP
                        }
                    }
                };
                
                SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error simulating key press for window {windowHandle}: {ex.Message}");
            }
        }

        /// <summary>
        /// Simulates a list of key presses on the specified window with delay - exact legacy behavior
        /// </summary>
        /// <param name="windowHandle">The target window handle</param>
        /// <param name="keys">The list of keys to press</param>
        /// <param name="delay">The delay between operations</param>
        public static void SimulateKeyPressListToWindow(IntPtr windowHandle, List<Keys> keys, int delay)
        {
            SetHandleToForeground(windowHandle);
            Thread.Sleep(10);

            List<INPUT> inputs = new List<INPUT>();

            foreach (var key in keys)
            {
                INPUT input = new INPUT();
                input.type = INPUT_KEYBOARD;
                input.u.ki = new KEYBDINPUT();
                input.u.ki.wVk = (ushort)key;
                input.u.ki.wScan = 0;
                input.u.ki.dwFlags = 0;
                input.u.ki.time = 0;
                input.u.ki.dwExtraInfo = IntPtr.Zero;
                inputs.Add(input);
            }

            for (int i = keys.Count - 1; i >= 0; i--)
            {
                INPUT input = new INPUT();
                input.type = INPUT_KEYBOARD;
                input.u.ki = new KEYBDINPUT();
                input.u.ki.wVk = (ushort)keys[i];
                input.u.ki.wScan = 0;
                input.u.ki.dwFlags = KEYEVENTF_KEYUP;
                input.u.ki.time = 0;
                input.u.ki.dwExtraInfo = IntPtr.Zero;
                inputs.Add(input);
            }

            if (inputs.Count > 0)
            {
                SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
            }

            Thread.Sleep(10);
        }

        /// <summary>
        /// Sends TAB key using keybd_event with scan codes
        /// </summary>
        /// <param name="windowHandle">The target window handle</param>
        public static void SendTab(IntPtr windowHandle)
        {
            try
            {
                keybd_event((byte)Keys.Tab, 0x0F, 0, 0);
                Thread.Sleep(5);
                keybd_event((byte)Keys.Tab, 0x0F, KEYUP, 0);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error sending TAB key to window {windowHandle}: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends ENTER key using keybd_event with scan codes
        /// </summary>
        /// <param name="windowHandle">The target window handle</param>
        public static void SendEnter(IntPtr windowHandle)
        {
            try
            {
                keybd_event((byte)Keys.Enter, 0x1C, 0, 0);
                Thread.Sleep(5);
                keybd_event((byte)Keys.Enter, 0x1C, KEYUP, 0);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error sending ENTER key to window {windowHandle}: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends text to multiple windows
        /// </summary>
        /// <param name="text">The text to send</param>
        /// <param name="targetWindows">The list of target windows</param>
        public static void SendTextToWindows(string text, List<KeyValuePair<IntPtr, WindowInfo>> targetWindows)
        {
            try
            {
                foreach (var windowEntry in targetWindows)
                {
                    SetHandleToForeground(windowEntry.Key);
                    var discussionKeybind = ConfigurationService.Current.Keybinds[TRIGGERS.DOFUS_OPEN_DISCUSSION];
                    SimulateKeyCombination(windowEntry.Key, discussionKeybind);
                    Thread.Sleep(50);

                    foreach (char c in text)
                    {
                        Keys key;
                        bool needsShift = false;
                        
                        switch (c)
                        {
                            case '/':
                                key = Keys.OemQuestion;
                                needsShift = true;
                                break;
                            case ' ':
                                key = Keys.Space;
                                break;
                            default:
                                var keyCode = VkKeyScan(c);
                                if (keyCode == -1)
                                {
                                    Trace.WriteLine($"Warning: Could not convert character '{c}' to key code");
                                    continue;
                                }
                                else
                                {
                                    key = (Keys)(keyCode & 0xFF);
                                    needsShift = (keyCode & 0x100) != 0;
                                }
                                break;
                        }
                        
                        if (needsShift)
                        {
                            SimulateKeyPressListToWindow(windowEntry.Key, new List<Keys> { Keys.LShiftKey, key }, 0);
                        }
                        else
                        {
                            SimulateKeyPress(windowEntry.Key, key);
                        }
                    }

                    SendEnter(windowEntry.Key);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error sending text to windows: {ex.Message}");
            }
        }

        public static void GroupCharacters()
        {
            try
            {
                var selectedPanel = PanelManagementService.SelectedPanel;
                if (selectedPanel == null)
                {
                    Trace.WriteLine("No panel selected for group characters");
                    return;
                }

                var selectedWindow = WindowHandles.FirstOrDefault(w => w.Value.RelatedPanel == selectedPanel);
                if (selectedWindow.Key == IntPtr.Zero)
                {
                    Trace.WriteLine("No window found for selected panel");
                    return;
                }

                Trace.WriteLine($"Sending group invitations from SELECTED panel: {selectedWindow.Value.CharacterName}");

                var otherWindows = WindowHandles.Where(w => w.Key != selectedWindow.Key).ToList();

                var discussionKeybind = ConfigurationService.Current.Keybinds[TRIGGERS.DOFUS_OPEN_DISCUSSION];
                SimulateKeyCombination(selectedWindow.Key, discussionKeybind);
                Thread.Sleep(100);

                foreach (var windowEntry in otherWindows)
                {
                    var windowInfo = windowEntry.Value;
                    var characterName = windowInfo.CharacterName;

                    if (string.IsNullOrEmpty(characterName))
                    {
                        Trace.WriteLine("Character name is empty, cannot send group characters command");
                        continue;
                    }

                    Trace.WriteLine($"Sending group characters invitation for character: {characterName}");

                    SetHandleToForeground(selectedWindow.Key);
                    Thread.Sleep(50);

                    var inviteCommand = $"/invite {characterName}";
                    foreach (char c in inviteCommand)
                    {
                        Keys key;
                        bool needsShift = false;

                        switch (c)
                        {
                            case '/':
                                key = Keys.OemQuestion;
                                needsShift = true;
                                break;
                            case ' ':
                                key = Keys.Space;
                                break;
                            default:
                                var keyCode = VkKeyScan(c);
                                if (keyCode == -1)
                                {
                                    Trace.WriteLine($"Warning: Could not convert character '{c}' to key code");
                                    continue;
                                }
                                else
                                {
                                    key = (Keys)(keyCode & 0xFF);
                                    needsShift = (keyCode & 0x100) != 0; // Check if Shift is needed
                                }
                                break;
                        }

                        if (needsShift)
                        {
                            SimulateKeyPressListToWindow(selectedWindow.Key, new List<Keys> { Keys.LShiftKey, key }, 0);
                        }
                        else
                        {
                            SimulateKeyPress(selectedWindow.Key, key);
                        }
                    }

                    // Use optimized SendEnter function instead of SimulateKeyPress for better ENTER handling
                    SendEnter(selectedWindow.Key);

                    Trace.WriteLine($"Group characters invitation sent successfully for: {characterName}");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error in GroupCharacters: {ex.Message}");
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Gets the title of the specified window
        /// </summary>
        /// <param name="hWnd">The window handle</param>
        /// <returns>The window title</returns>
        private static string GetWindowTitle(IntPtr hWnd)
        {
            try
            {
                var length = GetWindowTextLength(hWnd);
                if (length == 0) return string.Empty;

                var builder = new StringBuilder(length + 1);
                GetWindowText(hWnd, builder, builder.Capacity);
                return builder.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Extracts character name from window title - takes only the first word until the first space
        /// </summary>
        /// <param name="windowTitle">The full window title</param>
        /// <param name="titlePattern">The pattern used to identify game windows</param>
        /// <returns>The extracted character name (first word only)</returns>
        private static string ExtractCharacterName(string windowTitle, string titlePattern)
        {
            try
            {
                var index = windowTitle.IndexOf(titlePattern, StringComparison.OrdinalIgnoreCase);
                if (index > 0)
                {
                    var fullName = windowTitle.Substring(0, index).Trim();
                    
                    // Take only the first word until the first space
                    var spaceIndex = fullName.IndexOf(' ');
                    if (spaceIndex > 0)
                    {
                        return fullName.Substring(0, spaceIndex).Trim();
                    }
                    
                    return fullName;
                }
                
                // If no pattern found, take the first word of the entire title
                var firstSpaceIndex = windowTitle.IndexOf(' ');
                if (firstSpaceIndex > 0)
                {
                    return windowTitle.Substring(0, firstSpaceIndex).Trim();
                }
                
                return windowTitle;
            }
            catch
            {
                return windowTitle;
            }
        }

        /// <summary>
        /// Gets a random delay based on configuration (optimized for faster response)
        /// </summary>
        /// <returns>Random delay in milliseconds</returns>
        private static int GetRandomDelay()
        {
            var config = Services.ConfigurationService.Current.General;
            var random = new Random();
            var minDelay = Math.Max(5, config.MinimumFollowDelay);
            var maxDelay = Math.Max(minDelay + 5, config.MaximumFollowDelay);
            return random.Next(minDelay, maxDelay);
        }

        /// <summary>
        /// Performs a click operation on the specified window with retry mechanism
        /// </summary>
        /// <param name="windowHandle">The target window handle</param>
        /// <param name="position">The position to click</param>
        private static void PerformClickOnWindowWithRetry(IntPtr windowHandle, POINT position)
        {
            for (int attempt = 0; attempt < MaxClickRetries; attempt++)
            {
                try
                {
                    if (!IsWindow(windowHandle))
                    {
                        Trace.WriteLine($"Window handle is no longer valid on attempt {attempt + 1}");
                        break;
                    }

                    var lParam = MakeLParam(position.X, position.Y);
                    
                    var result1 = SendMessage(windowHandle, 0x0201, IntPtr.Zero, lParam); // WM_LBUTTONDOWN
                    var result2 = SendMessage(windowHandle, 0x0202, IntPtr.Zero, lParam); // WM_LBUTTONUP

                    if (result1 != IntPtr.Zero || result2 != IntPtr.Zero)
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Click attempt {attempt + 1} failed: {ex.Message}");
                    if (attempt == MaxClickRetries - 1)
                        throw;
                }
            }
        }

        /// <summary>
        /// Performs a click operation on the specified window (legacy method for compatibility)
        /// </summary>
        /// <param name="windowHandle">The target window handle</param>
        /// <param name="position">The position to click</param>
        private static void PerformClickOnWindow(IntPtr windowHandle, POINT position)
        {
            try
            {
                var lParam = MakeLParam(position.X, position.Y);
                PostMessage(windowHandle, 0x0201, IntPtr.Zero, lParam); // WM_LBUTTONDOWN
                Thread.Sleep(25);
                PostMessage(windowHandle, 0x0202, IntPtr.Zero, lParam); // WM_LBUTTONUP
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error performing click on window: {ex.Message}");
            }
        }

        /// <summary>
        /// Performs a double click operation on the specified window with retry mechanism
        /// </summary>
        /// <param name="windowHandle">The target window handle</param>
        /// <param name="position">The position to double click</param>
        private static void PerformDoubleClickOnWindowWithRetry(IntPtr windowHandle, POINT position)
        {
            try
            {
                PerformClickOnWindowWithRetry(windowHandle, position);
                Thread.Sleep(50);
                PerformClickOnWindowWithRetry(windowHandle, position);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error performing double click with retry on window: {ex.Message}");
            }
        }

        /// <summary>
        /// Performs a double click operation on the specified window (legacy method for compatibility)
        /// </summary>
        /// <param name="windowHandle">The target window handle</param>
        /// <param name="position">The position to double click</param>
        private static void PerformDoubleClickOnWindow(IntPtr windowHandle, POINT position)
        {
            try
            {
                PerformClickOnWindow(windowHandle, position);
                Thread.Sleep(50);
                PerformClickOnWindow(windowHandle, position);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error performing double click on window: {ex.Message}");
            }
        }

        /// <summary>
        /// Fills sell price based on the foreground window using OCR - exact legacy implementation
        /// </summary>
        public static void FillSellPriceBasedOnForeGroundWindow()
        {
            Trace.WriteLine("Starting price analysis");
            
            var sellCurrentModeValue = GetRectangleFromPosition(ConfigurationService.Current.Positions[TRIGGERS_POSITIONS.SELL_CURRENT_MODE]);
            var sellLot1Value = GetRectangleFromPosition(ConfigurationService.Current.Positions[TRIGGERS_POSITIONS.SELL_LOT_1]);
            var sellLot10Value = GetRectangleFromPosition(ConfigurationService.Current.Positions[TRIGGERS_POSITIONS.SELL_LOT_10]);
            var sellLot100Value = GetRectangleFromPosition(ConfigurationService.Current.Positions[TRIGGERS_POSITIONS.SELL_LOT_100]);
            var sellLot1000Value = GetRectangleFromPosition(ConfigurationService.Current.Positions[TRIGGERS_POSITIONS.SELL_LOT_1000]);

            Dictionary<Rectangle, int> ValuesMap = new Dictionary<Rectangle, int>
            {
                {sellCurrentModeValue, 0},
                {sellLot1Value, 0},
                {sellLot10Value, 0},
                {sellLot100Value, 0},
                {sellLot1000Value, 0},
            };

            try
            {
                InitializeOCREngine();
                if (_ocrEngine == null)
                {
                    Trace.WriteLine("OCR engine not available");
                    return;
                }

                lock (TessEngineLock)
                {
                    _ocrEngine.SetVariable("tessedit_char_whitelist", "0123456789");

                    foreach (var elt in ValuesMap.Keys.ToList())
                    {
                        using (var originalBitmap = CaptureWindowArea((IntPtr)PanelManagementService.SelectedPanel.Tag, elt))
                        using (var preprocessedBitmap = PreprocessForOCR(originalBitmap))
                        using (var pix = PixConverter.ToPix(preprocessedBitmap))
                        using (var page = _ocrEngine.Process(pix, PageSegMode.SingleLine))
                        {
                            string recognizedText = Regex.Replace(page.GetText(), @"\s+", "");
                            Match match = Regex.Match(recognizedText, @"\d+");

                            if (match.Success)
                            {
                                string firstSequenceOfDigits = match.Value;
                                Trace.WriteLine($"Recognized price: {recognizedText}");
                                if (int.TryParse(firstSequenceOfDigits, out int parsedValue))
                                {
                                    ValuesMap[elt] = parsedValue;
                                }
                                else
                                {
                                    Trace.WriteLine($"Failed to parse first sequence of digits: {firstSequenceOfDigits}");
                                }
                            }
                            else
                            {
                                Trace.WriteLine("No digits found in recognized text.");
                            }
                        }
                    }
                }
                
                if (ValuesMap[sellCurrentModeValue] == 0)
                {
                    return;
                }
                
                var AmountToFill = 0;
                switch (ValuesMap[sellCurrentModeValue])
                {
                    case 1:
                        AmountToFill = ValuesMap[sellLot1Value];
                        break;
                    case 10:
                        AmountToFill = ValuesMap[sellLot10Value];
                        break;
                    case 100:
                        AmountToFill = ValuesMap[sellLot100Value];
                        break;
                }
                AmountToFill = int.Parse(AmountToFill.ToString().Trim());

                Trace.WriteLine($"Amount to fill: {AmountToFill - 1}; current sell quantity: {ValuesMap[sellCurrentModeValue]}; recognized selling price: {AmountToFill}");
                System.Threading.Thread.Sleep(100);
                SendKeys.SendWait("^a");
                System.Threading.Thread.Sleep(200);
                SendKeys.SendWait("{DELETE}");
                SendKeys.SendWait((AmountToFill - 1).ToString().Trim());
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"HDV OCR processing failed: {ex.Message}, Trace : {ex.StackTrace}");
            }
            Trace.WriteLine("-------------------------");
        }

        /// <summary>
        /// Initializes the OCR engine if not already initialized
        /// </summary>
        private static void InitializeOCREngine()
        {
            if (_ocrEngine == null)
            {
                lock (TessEngineLock)
                {
                    if (_ocrEngine == null)
                    {
                        try
                        {
                            _ocrEngine = new TesseractEngine(TessdataPath, OcrLanguage, EngineMode.Default);
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"Failed to initialize OCR engine: {ex.Message}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Advanced preprocessing for OCR - exact legacy implementation
        /// </summary>
        private static Bitmap PreprocessForOCR(Bitmap src)
        {
            int scale = 4;
            Bitmap upscaled = new Bitmap(src.Width * scale, src.Height * scale);
            using (Graphics g = Graphics.FromImage(upscaled))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, new Rectangle(0, 0, upscaled.Width, upscaled.Height));
            }

            BitmapData data = upscaled.LockBits(new Rectangle(0, 0, upscaled.Width, upscaled.Height), 
                ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            int stride = data.Stride;
            IntPtr scan0 = data.Scan0;
            int bytes = stride * upscaled.Height;
            byte[] pixelBuffer = new byte[bytes];
            System.Runtime.InteropServices.Marshal.Copy(scan0, pixelBuffer, 0, bytes);

            int minGray = 255, maxGray = 0;
            
            for (int y = 0; y < upscaled.Height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < upscaled.Width; x++)
                {
                    int idx = row + x * 3;
                    if (idx + 2 >= pixelBuffer.Length) continue;
                    
                    int b = pixelBuffer[idx];
                    int g = pixelBuffer[idx + 1];
                    int r = pixelBuffer[idx + 2];
                    int gray = (int)(0.299 * r + 0.587 * g + 0.114 * b);
                    
                    if (gray < minGray) minGray = gray;
                    if (gray > maxGray) maxGray = gray;
                }
            }

            int grayRange = maxGray - minGray;
            int threshold;
            
            if (grayRange < 20)
            {
                threshold = (minGray + maxGray) / 2;
            }
            else
            {
                threshold = minGray + (int)(grayRange * 0.4);
            }

            for (int y = 0; y < upscaled.Height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < upscaled.Width; x++)
                {
                    int idx = row + x * 3;
                    if (idx + 2 >= pixelBuffer.Length) continue;
                    
                    int b = pixelBuffer[idx];
                    int g = pixelBuffer[idx + 1];
                    int r = pixelBuffer[idx + 2];
                    int gray = (int)(0.299 * r + 0.587 * g + 0.114 * b);
                    
                    byte finalValue;
                    if (gray > threshold)
                    {
                        finalValue = 255;
                    }
                    else
                    {
                        finalValue = 0;
                    }
                    
                    pixelBuffer[idx] = finalValue;     // B
                    pixelBuffer[idx + 1] = finalValue; // G
                    pixelBuffer[idx + 2] = finalValue; // R
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(pixelBuffer, 0, scan0, bytes);
            upscaled.UnlockBits(data);

            return upscaled;
        }

        /// <summary>
        /// Converts Position to Rectangle
        /// </summary>
        private static Rectangle GetRectangleFromPosition(Position position)
        {
            return new Rectangle(position.X, position.Y, position.Width, position.Height);
        }

        /// <summary>
        /// Captures a specific area of a window - exact legacy implementation
        /// </summary>
        private static Bitmap CaptureWindowArea(IntPtr windowHandle, Rectangle screenRectangle)
        {
            Screen windowScreen = Screen.FromHandle(windowHandle);

            Rectangle adjustedRectangle = new Rectangle(
                screenRectangle.Left + windowScreen.WorkingArea.Left,
                screenRectangle.Top + windowScreen.WorkingArea.Top,
                screenRectangle.Width,
                screenRectangle.Height);

            Bitmap capturedBitmap = new Bitmap(adjustedRectangle.Width, adjustedRectangle.Height);

            using (Graphics graphics = Graphics.FromImage(capturedBitmap))
            {
                graphics.CopyFromScreen(adjustedRectangle.Left, adjustedRectangle.Top, 0, 0, 
                    new Size(adjustedRectangle.Width, adjustedRectangle.Height));
            }

            return capturedBitmap;
        }

        /// <summary>
        /// Creates an lParam value from x and y coordinates
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <returns>lParam value</returns>
        private static IntPtr MakeLParam(int x, int y)
        {
            return (IntPtr)((y << 16) | (x & 0xFFFF));
        }

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);
        #endregion
    }
}
