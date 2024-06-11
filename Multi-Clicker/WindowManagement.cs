using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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

        private const int ALT = 0xA4;
        private const int EXTENDEDKEY = 0x1;
        public const int SM_CYCAPTION = 4;
        private const int KEYUP = 0x2;
        private const uint Restore = 9;
        private static string ocrLanguage = "fra";
        private static string tessdataPath = @"./tessdata";
        public static Dictionary<IntPtr, WindowInfo> windowHandles = new Dictionary<IntPtr, WindowInfo>();

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
                    // Ignore processes where access to the MainModule is denied
                }

                return true;
            }, IntPtr.Zero);

            return windowHandles;
        }
        public static void SimulateClick(IntPtr windowHandle, int x, int y, int delay)
        {
            // Wait for a random amount of time between 1 and 1000 milliseconds
            System.Threading.Thread.Sleep(delay);
            // Calculate the lParam (the coordinates of the cursor within the window)
            IntPtr lParam = (IntPtr)((y << 16) | x);

            // Send the WM_LBUTTONDOWN message
            SendMessage(windowHandle, Constants.WM_LBUTTONDOWN, new IntPtr(1), lParam);
            System.Threading.Thread.Sleep(10);
            // Send the WM_LBUTTONUP message
            SendMessage(windowHandle, Constants.WM_LBUTTONUP, IntPtr.Zero, lParam);
        }

        public static void SimulateDoubleClick(IntPtr windowHandle, int x, int y)
        {
            // Calculate the lParam (the coordinates of the cursor within the window)
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

            // Wait for a specified amount of time
            System.Threading.Thread.Sleep(delay);
            if (HookManagement.keysPressed.Contains(Keys.LShiftKey))
            {
                PostMessage(windowHandle, HookManagement.WM_KEYUP, (IntPtr)Keys.LShiftKey, IntPtr.Zero);
            }
            // Send the WM_KEYDOWN message
            PostMessage(windowHandle, HookManagement.WM_KEYDOWN, (IntPtr)key, IntPtr.Zero);


            // Send the WM_KEYUP message
            PostMessage(windowHandle, HookManagement.WM_KEYUP, (IntPtr)key, IntPtr.Zero);
        }

        public static void SetHandleToForeGround(IntPtr handle)
        {

            //check if window is minimized
            if (IsIconic(handle))
            {
                ShowWindow(handle, Restore);
            }

            // Simulate a key press & release to trick Windows into allowing the SetForegroundWindow call
            keybd_event((byte)ALT, 0x45, EXTENDEDKEY | 0, 0);
            keybd_event((byte)ALT, 0x45, EXTENDEDKEY | KEYUP, 0);

            SetForegroundWindow(handle);
        }

        public static void PerformWindowClick(POINT cursorPos, Boolean noDelay)
        {
            Task.Run(() =>
            {
                Random random = new Random();
                foreach (KeyValuePair<IntPtr, WindowInfo> entry in WindowManagement.windowHandles)
                {
                    int delay = noDelay ? 20 : random.Next(200, 400);
                    RECT rect = new RECT();
                    GetWindowRect(entry.Key, ref rect);

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
            // Check if the window is in fullscreen mode
            bool isFullscreen = rect.Right - rect.Left == Screen.PrimaryScreen.Bounds.Width &&
                                rect.Bottom - rect.Top == Screen.PrimaryScreen.Bounds.Height;

            int absoluteX, absoluteY;
            if (isFullscreen)
            {
                // If the window is in fullscreen mode, the cursor position is the same as the relative position
                absoluteX = cursorPos.X;
                absoluteY = cursorPos.Y;
            }
            else
            {
                // If the window is not in fullscreen mode, calculate the absolute position
                absoluteX = cursorPos.X - rect.Left - 5;
                int titleBarHeight = GetSystemMetrics(SM_CYCAPTION); // Get the height of the title bar
                absoluteY = cursorPos.Y - rect.Top - titleBarHeight - 5; // Subtract the height of the title bar

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
        private static object lockObject = new object();

        public static void StartCheckingForegroundWindowForText()
        {
            System.Timers.Timer timer = new System.Timers.Timer(700);
            timer.Elapsed += (sender, e) =>
            {
                lock (lockObject)
                {
                    CheckForegroundWindowForText();
                }
            };
            timer.Start();
        }

        public static void FillSellPriceBasedOnForeGroundWindow()
        {

            Dictionary<Rectangle, int> ValuesMap = new Dictionary<Rectangle, int>{
                {GetRectangleFromPosition(ConfigManagement.config.Positions[TRIGGERS_POSITIONS.SELL_CURRENT_MODE]), 0},
                {GetRectangleFromPosition(ConfigManagement.config.Positions[TRIGGERS_POSITIONS.SELL_LOT_1]), 0},
                {GetRectangleFromPosition(ConfigManagement.config.Positions[TRIGGERS_POSITIONS.SELL_LOT_10]), 0},
                {GetRectangleFromPosition(ConfigManagement.config.Positions[TRIGGERS_POSITIONS.SELL_LOT_100]), 0},
            };

            try
            {
                using (var engine = new TesseractEngine(tessdataPath, ocrLanguage, EngineMode.Default))
                {
                    engine.SetVariable("tessedit_char_whitelist", "0123456789");

                    foreach (var elt in ValuesMap.Keys.ToList())
                    {
                        var currentSellingModeBitmap = CaptureArea(elt);
                        using (var pix = PixConverter.ToPix(currentSellingModeBitmap))
                        {
                            using (var page = engine.Process(pix, PageSegMode.SingleLine))
                            {
                                string recognizedText = page.GetText().Trim();
                                Debug.WriteLine($"Recognized text: {recognizedText}");
                                if (int.TryParse(recognizedText, out int parsedValue))
                                {
                                    ValuesMap[elt] = parsedValue;
                                }
                                else
                                {
                                    Debug.WriteLine($"Failed to parse recognized text: {recognizedText}");
                                }
                            }
                        }
                        currentSellingModeBitmap.Dispose();
                    }
                }

                if (
                    ValuesMap[GetRectangleFromPosition(ConfigManagement.config.Positions[TRIGGERS_POSITIONS.SELL_CURRENT_MODE])] == 0
                    || ValuesMap[GetRectangleFromPosition(ConfigManagement.config.Positions[TRIGGERS_POSITIONS.SELL_LOT_1])] == 0
                    || ValuesMap[GetRectangleFromPosition(ConfigManagement.config.Positions[TRIGGERS_POSITIONS.SELL_LOT_10])] == 0
                    || ValuesMap[GetRectangleFromPosition(ConfigManagement.config.Positions[TRIGGERS_POSITIONS.SELL_LOT_100])] == 0
                    )
                {
                    return;
                }
                switch (ValuesMap[GetRectangleFromPosition(ConfigManagement.config.Positions[TRIGGERS_POSITIONS.SELL_CURRENT_MODE])])
                {
                    case 1:
                        SendKeys.SendWait((ValuesMap[GetRectangleFromPosition(ConfigManagement.config.Positions[TRIGGERS_POSITIONS.SELL_LOT_1])] - 1).ToString());
                        break;
                    case 10:
                        SendKeys.SendWait((ValuesMap[GetRectangleFromPosition(ConfigManagement.config.Positions[TRIGGERS_POSITIONS.SELL_LOT_10])] - 1).ToString());
                        break;
                    case 100:
                        SendKeys.SendWait((ValuesMap[GetRectangleFromPosition(ConfigManagement.config.Positions[TRIGGERS_POSITIONS.SELL_LOT_100])] - 1).ToString());
                        break;
                }
            }
            catch (Exception ex)
            {
                // Log or handle exceptions from the OCR process
                Debug.WriteLine($"OCR processing failed: {ex.Message}");
            }
        }
        private static Rectangle GetRectangleFromPosition(Position position)
        {
            return new Rectangle(position.X, position.Y, position.Width, position.Height);
        }
        private static void CheckForegroundWindowForText()
        {

            Bitmap captureBitmap = CaptureArea(GetRectangleFromPosition(ConfigManagement.config.Positions[TRIGGERS_POSITIONS.FIGHT_ANALISYS]));
            using (var engine = new TesseractEngine(tessdataPath, ocrLanguage, EngineMode.Default))
            {
                // Recognize the text from the captured image
                using (var pix = PixConverter.ToPix(captureBitmap))
                {
                    using (var page = engine.Process(pix))
                    {
                        string recognizedText = page.GetText();
                        recognizedText = recognizedText.Replace(" ", ""); // Remove all spaces
                        foreach (var window in windowHandles)
                        {
                            var value = window.Value;
                            if (recognizedText.Contains(value.CharacterName))
                            {
                                PanelManagement.Panel_Select(value.CharacterName);
                            };
                        }
                    }
                    captureBitmap.Dispose();
                }
            }
        }
        private static Bitmap CaptureArea(Rectangle rectangle)
        {
            int width = (rectangle.Right - rectangle.Left);
            int height = (rectangle.Bottom - rectangle.Top);

            // Create a bitmap to hold the captured image
            Bitmap bitmap = new Bitmap(width, height);

            // Use Graphics.CopyFromScreen to capture the specified area of the window
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(rectangle.Left, rectangle.Top, 0, 0, new Size(width, height));
            }
            return bitmap;
        }

        public static Boolean IsRelatedHandle(IntPtr activeWindowHandle)
        {

            if (!WindowManagement.windowHandles.ContainsKey(activeWindowHandle))
            {
                return false;
            }
            return true;
        }

    }
}
