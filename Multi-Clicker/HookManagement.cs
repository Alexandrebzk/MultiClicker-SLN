using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static MultiClicker.MultiClicker;
using static MultiClicker.WindowManagement;

namespace MultiClicker
{
    public static class HookManagement
    {

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
        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam); 
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT Point);
        public static List<Keys> keysPressed = new List<Keys>();
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WH_MOUSE_LL = 14;
        public const int WH_KEYBOARD_LL = 13;
        public static event Action F6Pressed;

        public static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (!WindowManagement.IsRelatedHandle(GetForegroundWindow()))
            {
                return CallNextHookEx(MultiClicker._keyboardHookID, nCode, wParam, lParam);
            }
            Random random = new Random();
            if (nCode >= 0)
            {
                if (wParam == (IntPtr)WM_KEYDOWN)
                {
                    keysPressed.Add((Keys)Marshal.ReadInt32(lParam));
                    foreach (Keys key in keysPressed.ToList())
                    {
                        switch (key)
                        {
                            case Keys.F1:
                                PanelManagement.SelectNextPanel();
                                keysPressed.Remove(key);
                                return (IntPtr)1;
                            case Keys.F2:
                                PanelManagement.SelectPreviousPanel();
                                keysPressed.Remove(key);
                                return (IntPtr)1;
                            case Keys.F3:
                                    Task.Run(() =>
                                    {
                                        foreach (KeyValuePair<IntPtr, WindowInfo> entry in WindowManagement.windowHandles)
                                        {
                                            int delay = random.Next(200, 400);
                                            WindowManagement.SimulateKeyPress(entry.Key, Keys.H, delay);
                                        }
                                    });
                                keysPressed.Remove(key);
                                break;
                            case Keys.F5:
                                Task.Run(() =>
                                {
                                    List<String> commands = new List<String>();
                                    foreach (KeyValuePair<IntPtr, WindowInfo> entry in WindowManagement.windowHandles)
                                    {
                                        if (entry.Value == WindowManagement.windowHandles.First().Value)
                                            continue;
                                        int delay = random.Next(800, 1200);
                                        string inviteCommand = "/invite " + entry.Value.CharacterName;
                                        commands.Add(inviteCommand);
                                    }
                                    commands.ForEach(command =>
                                    {
                                        WindowManagement.sentTextToHandles(command, new List<IntPtr> { WindowManagement.windowHandles.First().Key });
                                    });
                                });
                                keysPressed.Remove(key);
                                break;
                        }

                    }
                }
                else if (wParam == (IntPtr)WM_KEYUP)
                {
                    Keys key = (Keys)Marshal.ReadInt32(lParam);
                    if (key == Keys.F6)
                    {
                        F6Pressed?.Invoke();
                    }
                    keysPressed.Remove(key);
                }
            }
            return CallNextHookEx(MultiClicker._keyboardHookID, nCode, wParam, lParam);
        }

        public static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (!WindowManagement.IsRelatedHandle(GetForegroundWindow()))
            {
                return CallNextHookEx(MultiClicker._keyboardHookID, nCode, wParam, lParam);
            }
            if (nCode >= 0)
            {
                POINT cursorPos;
                GetCursorPos(out cursorPos);
                IntPtr hWnd = WindowFromPoint(cursorPos);
                MouseMessages message = (MouseMessages)wParam;

                if (WindowManagement.windowHandles.ContainsKey(hWnd))
                {
                    switch (message)
                    {
                        case MouseMessages.WM_MBUTTONUP:
                            System.Threading.Thread.Sleep(150);
                            WindowManagement.PerformWindowDoubleClick(cursorPos);
                            break;
                        case MouseMessages.WM_XBUTTONDOWN:
                            MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                            int xButton = (int)(hookStruct.mouseData >> 16);
                            if (xButton == 1)
                            {
                                WindowManagement.PerformWindowClick(cursorPos, false);
                            }
                            else if (xButton == 2)
                            {
                                WindowManagement.PerformWindowClick(cursorPos, true);
                            }
                            break;
                    }
                }
            }
            return CallNextHookEx(MultiClicker._mouseHookID, nCode, wParam, lParam);
        }
    }
}
