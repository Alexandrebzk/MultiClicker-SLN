using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tesseract;
using static MultiClicker.MultiClicker;

namespace MultiClicker
{
    public static class WindowManagement
    {

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
        private static readonly object tessEngineLock = new object();
        private static TesseractEngine _engine = null;

        private const int ALT = 0xA4;
        private const int EXTENDEDKEY = 0x1;
        public const int SM_CYCAPTION = 4;
        private const int KEYUP = 0x2;
        private const uint Restore = 9;
        private static string ocrLanguage = "fra";
        private static string tessdataPath = @"tessdata";
        public static Dictionary<IntPtr, WindowInfo> windowHandles = new Dictionary<IntPtr, WindowInfo>();
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

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, [MarshalAs(UnmanagedType.LPArray), In] INPUT[] pInputs, int cbSize);

        public class WindowInfo
        {
            public string WindowName { get; set; }
            public string CharacterName { get; set; }
            public ExtendedPanel relatedPanel { get; set; }
        }
        public static Dictionary<IntPtr, WindowInfo> FindWindows(string windowTitle)
        {
            Dictionary<IntPtr, WindowInfo> windowHandles = new Dictionary<IntPtr, WindowInfo>();

            EnumWindows((hWnd, lParam) =>
            {
                int length = GetWindowTextLength(hWnd);
                StringBuilder windowName = new StringBuilder(length + 1);
                GetWindowText(hWnd, windowName, windowName.Capacity);

                GetWindowThreadProcessId(hWnd, out uint windowProcessId);
                Process process = Process.GetProcessById((int)windowProcessId);

                try
                {
                    if (windowName.ToString().Contains(windowTitle) && !windowHandles.ContainsKey(hWnd))
                    {
                        int index = windowName.ToString().IndexOf(" - ");
                        windowHandles.Add(hWnd, new WindowInfo
                        {
                            CharacterName = windowName.ToString().Substring(0, index),
                            WindowName = windowName.ToString(),
                        });
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }

                return true;
            }, IntPtr.Zero);

            return windowHandles;
        }
        public static void SimulateClick(IntPtr windowHandle, int x, int y, int delay)
        {
            System.Threading.Thread.Sleep(delay);
            IntPtr lParam = (IntPtr)((y << 16) | x);

            SendMessage(windowHandle, Constants.WM_LBUTTONDOWN, new IntPtr(1), lParam);
            System.Threading.Thread.Sleep(10);
            SendMessage(windowHandle, Constants.WM_LBUTTONUP, IntPtr.Zero, lParam);
        }

        public static void SimulateDoubleClick(IntPtr windowHandle, int x, int y)
        {
            IntPtr lParam = (IntPtr)((y << 16) | x);

            SendMessage(windowHandle, Constants.WM_LBUTTONDOWN, new IntPtr(1), lParam);
            System.Threading.Thread.Sleep(10);
            SendMessage(windowHandle, Constants.WM_LBUTTONUP, IntPtr.Zero, lParam);
            System.Threading.Thread.Sleep(SystemInformation.DoubleClickTime / 2);
            SendMessage(windowHandle, Constants.WM_LBUTTONDOWN, new IntPtr(1), lParam);
            System.Threading.Thread.Sleep(10);
            SendMessage(windowHandle, Constants.WM_LBUTTONUP, IntPtr.Zero, lParam);
        }

        public static void SimulateKeyPress(IntPtr windowHandle, Keys key, int delay)
        {
            System.Threading.Thread.Sleep(delay);
            ReleasePressedKeys();
            PostMessage(windowHandle, HookManagement.WM_KEYDOWN, (IntPtr)key, IntPtr.Zero);
            PostMessage(windowHandle, HookManagement.WM_KEYUP, (IntPtr)key, IntPtr.Zero);
        }

        public static void SimulateKeyPressListToCurrentWindow(List<Keys> keys, int delay)
        {
            ReleasePressedKeys();
            List<INPUT> inputs = new List<INPUT>();
            foreach (var key in keys)
            {
                inputs.Add(new INPUT
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
                });
            }
            foreach (var key in keys.AsEnumerable().Reverse())
            {
                inputs.Add(new INPUT
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
                });
            }
            System.Threading.Thread.Sleep(50);
            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
            System.Threading.Thread.Sleep(50);
        }

        public static void SetHandleToForeGround(IntPtr handle)
        {

            // check if window is minimized
            if (IsIconic(handle))
            {
                ShowWindow(handle, Restore);
            }

            keybd_event((byte)ALT, 0x45, EXTENDEDKEY | 0, 0);
            keybd_event((byte)ALT, 0x45, EXTENDEDKEY | KEYUP, 0);

            SetForegroundWindow(handle);
        }

        public static void PerformWindowClick(POINT cursorPos, Boolean noDelay)
        {
            Task.Run(() =>
            {
                Random random = new Random();
                bool isFirstEntry = true;
                foreach (KeyValuePair<IntPtr, WindowInfo> entry in WindowManagement.windowHandles)
                {
                    int delay = noDelay ? ConfigManagement.config.General.FollowNoDelay : random.Next(ConfigManagement.config.General.MinimumFollowDelay, ConfigManagement.config.General.MaximumFollowDelay);
                    RECT rect = new RECT();
                    GetWindowRect(entry.Key, ref rect);

                    if(isFirstEntry){
                        delay = ConfigManagement.config.General.FollowNoDelay;
                        isFirstEntry = false;
                    }
                    POINT finalPositions = AdjustClickPosition(rect, cursorPos);
                    SimulateClick(entry.Key, finalPositions.X, finalPositions.Y, delay);
                }
            });
        }

        public static void PerformWindowDoubleClick(POINT cursorPos)
        {
            Task.Run(() =>
            {
                Random random = new Random();
                foreach (KeyValuePair<IntPtr, WindowInfo> entry in WindowManagement.windowHandles)
                {
                    RECT rect = new RECT();
                    GetWindowRect(entry.Key, ref rect);

                    POINT finalPositions = AdjustClickPosition(rect, cursorPos);
                    SimulateDoubleClick(entry.Key, finalPositions.X, finalPositions.Y);
                }
            });
        }
        private static POINT AdjustClickPosition(RECT rect, POINT cursorPos)
        {
            bool isFullscreen = rect.Right - rect.Left == Screen.PrimaryScreen.Bounds.Width &&
                                rect.Bottom - rect.Top == Screen.PrimaryScreen.Bounds.Height;

            int absoluteX, absoluteY;
            if (isFullscreen)
            {
                absoluteX = cursorPos.X;
                absoluteY = cursorPos.Y;
            }
            else
            {
                absoluteX = cursorPos.X - rect.Left - 5;
                int titleBarHeight = GetSystemMetrics(SM_CYCAPTION);
                absoluteY = cursorPos.Y - rect.Top - titleBarHeight - 5;

            }
            return new POINT
            {
                X = absoluteX,
                Y = absoluteY
            };
        }

        public static void sentTextToHandles(string inputText, List<KeyValuePair<IntPtr, WindowInfo>> handles)
        {
            foreach (KeyValuePair<IntPtr, WindowInfo> entry in handles)
            {
                PanelManagement.Panel_Select(entry.Value.CharacterName);
                System.Threading.Thread.Sleep(500);
                SimulateKeyPress(entry.Key, ConfigManagement.config.Keybinds[TRIGGERS.DOFUS_OPEN_DISCUSSION], 150);
                SendKeys.SendWait(inputText);
                SimulateKeyPress(entry.Key, Keys.Enter, 150);
                SimulateKeyPress(entry.Key, Keys.Enter, 500);
            }
        }

        public static void StartCheckingForegroundWindowForText()
        {
            System.Timers.Timer timer = new System.Timers.Timer(500);
            timer.Elapsed += (sender, e) =>
            {
                IntPtr activeWindowHandle = GetForegroundWindow();
                if (IsRelatedHandle(activeWindowHandle))
                {
                    CheckForegroundWindowForText();
                }
            };
            timer.Start();
        }

        public static void FillSellPriceBasedOnForeGroundWindow()
        {
            var sellCurrentModeValue = GetRectangleFromPosition(ConfigManagement.config.Positions[TRIGGERS_POSITIONS.SELL_CURRENT_MODE]);
            var sellLot1Value = GetRectangleFromPosition(ConfigManagement.config.Positions[TRIGGERS_POSITIONS.SELL_LOT_1]);
            var sellLot10Value = GetRectangleFromPosition(ConfigManagement.config.Positions[TRIGGERS_POSITIONS.SELL_LOT_10]);
            var sellLot100Value = GetRectangleFromPosition(ConfigManagement.config.Positions[TRIGGERS_POSITIONS.SELL_LOT_100]);

            Dictionary<Rectangle, int> ValuesMap = new Dictionary<Rectangle, int>{
                {sellCurrentModeValue, 0},
                {sellLot1Value, 0},
                {sellLot10Value, 0},
                {sellLot100Value, 0},
            };

            try
            {
                using (var engine = new TesseractEngine(tessdataPath, ocrLanguage, EngineMode.Default))
                {
                    engine.SetVariable("tessedit_char_whitelist", "0123456789");

                    foreach (var elt in ValuesMap.Keys.ToList())
                    {
                        var currentSellingModeBitmap = CaptureWindowArea((IntPtr)PanelManagement.selectedPanel.Tag, elt);
                        using (var pix = PixConverter.ToPix(currentSellingModeBitmap))
                        {
                            using (var page = engine.Process(pix, PageSegMode.SingleLine))
                            {
                                string recognizedText = page.GetText().Trim();
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
                        currentSellingModeBitmap.Dispose();
                    }
                }
                if (!(ValuesMap[sellCurrentModeValue] == 1 && ValuesMap[sellLot1Value] != 0) &&
    (ValuesMap[sellCurrentModeValue] == 0 || ValuesMap[sellLot1Value] == 0 || ValuesMap[sellLot10Value] == 0 || ValuesMap[sellLot100Value] == 0))
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
                Trace.WriteLine($"Amount to fill: {AmountToFill}");
                SendKeys.SendWait((AmountToFill - 1).ToString());
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"HDV OCR processing failed: {ex.Message}, Trace : {ex.StackTrace}");
            }
            Trace.WriteLine("-------------------------");
        }
        private static Rectangle GetRectangleFromPosition(Position position)
        {
            return new Rectangle(position.X, position.Y, position.Width, position.Height);
        }
        private static void CheckForegroundWindowForText()
        {
            try
            {
                TesseractEngine engine = GetTesseractEngine();

                using (var captureBitmap = CaptureWindowArea((IntPtr)PanelManagement.selectedPanel.Tag, GetRectangleFromPosition(ConfigManagement.config.Positions[TRIGGERS_POSITIONS.FIGHT_ANALISYS])))
                using (var pix = PixConverter.ToPix(captureBitmap))
                {
                    string recognizedText;
                    lock (tessEngineLock)
                    {
                        using (var page = engine.Process(pix))
                        {
                            recognizedText = page.GetText().Trim();
                        }
                    }

                    if (string.IsNullOrEmpty(recognizedText))
                    {
                        return;
                    }
                    Trace.WriteLine($"Recognized text: {recognizedText}");

                    foreach (var window in windowHandles)
                    {
                        var value = window.Value;
                        if (recognizedText.Contains(value.CharacterName))
                        {
                            PanelManagement.Panel_Select(value.CharacterName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error processing combat OCR: {ex.Message}");
            }
        }
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
                graphics.CopyFromScreen(adjustedRectangle.Left, adjustedRectangle.Top, 0, 0, new Size(adjustedRectangle.Width, adjustedRectangle.Height));
            }

            return capturedBitmap;
        }

        private static TesseractEngine GetTesseractEngine()
        {
            if (_engine == null)
            {
                lock (tessEngineLock)
                {
                    if (_engine == null)
                    {
                        _engine = new TesseractEngine(tessdataPath, ocrLanguage, EngineMode.Default);
                        _engine.SetVariable("tessedit_char_whitelist", "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ-_[]");
                    }
                }
            }
            return _engine;
        }

        public static Boolean IsRelatedHandle(IntPtr activeWindowHandle)
        {

            if (!WindowManagement.windowHandles.ContainsKey(activeWindowHandle))
            {
                return false;
            }
            return true;
        }
        private static void ReleasePressedKeys()
        {
            List<Keys> commonKeys = new List<Keys>
            {
                Keys.LShiftKey, Keys.RShiftKey, Keys.LControlKey, Keys.RControlKey,
                Keys.LMenu, Keys.RMenu, Keys.Alt, Keys.Shift, Keys.Control
            };

            List<INPUT> inputs = new List<INPUT>();

            foreach (var key in commonKeys)
            {
                if (HookManagement.keysPressed.Contains(key))
                {
                    inputs.Add(new INPUT
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
                    });
                }
            }

            if (inputs.Count > 0)
            {
                SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
            }
        }

    }
}
