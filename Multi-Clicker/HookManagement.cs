using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using static MultiClicker.WindowManagement;

namespace MultiClicker
{
    public static class HookManagement
    {
        public static Dictionary<TRIGGERS, Action<object>> KeyActions = new Dictionary<TRIGGERS, Action<object>>
        {
            { TRIGGERS.SELECT_NEXT,obj => PanelManagement.SelectNextPanel() },
            { TRIGGERS.SELECT_PREVIOUS, obj => PanelManagement.SelectPreviousPanel() },
            { TRIGGERS.HAVENBAG, obj => HavenbagHandler() },
            { TRIGGERS.GROUP_INVITE, obj => GroupHandler() },
            { TRIGGERS.SIMPLE_CLICK, obj => WindowManagement.PerformWindowClick(cursorPos, false) },
            { TRIGGERS.SIMPLE_CLICK_NO_DELAY, obj => WindowManagement.PerformWindowClick(cursorPos, true) },
            { TRIGGERS.DOUBLE_CLICK, obj => WindowManagement.PerformWindowDoubleClick(cursorPos) },
        };


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
        public static event Action ShouldOpenMenuTravel;
        public static Random Random = new Random();
        private static POINT cursorPos;

        public static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (!WindowManagement.IsRelatedHandle(GetForegroundWindow()))
            {
                return CallNextHookEx(MultiClicker._keyboardHookID, nCode, wParam, lParam);
            }
            Random random = new Random();
            if (nCode >= 0)
            {
                Keys key = (Keys)Marshal.ReadInt32(lParam);
                if (wParam == (IntPtr)WM_KEYDOWN)
                {
                    keysPressed.Add(key);
                    if (ConfigManagement.config.Keybinds.Values.Contains(key))
                    {
                        var trigger = ConfigManagement.config.Keybinds.First(kvp => kvp.Value == key).Key;
                        if (KeyActions.ContainsKey(trigger))
                        {
                            KeyActions[trigger](null);
                            keysPressed.Remove(key);
                        }
                    }
                }
                else if (wParam == (IntPtr)WM_KEYUP)
                {
                    if (key == ConfigManagement.config.Keybinds[TRIGGERS.TRAVEL])
                    {
                        ShouldOpenMenuTravel?.Invoke();
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

        public static void HavenbagHandler()
        {
            int delay = Random.Next(200, 400);
            Task.Run(() =>
            {
                foreach (KeyValuePair<IntPtr, WindowInfo> entry in WindowManagement.windowHandles)
                {
                    WindowManagement.SimulateKeyPress(entry.Key, ConfigManagement.config.Keybinds[TRIGGERS.DOFUS_HAVENBAG], delay);
                }
            });
        }
        private static void GroupHandler()
        {
            Task.Run(() =>
            {
                List<String> commands = new List<String>();
                foreach (KeyValuePair<IntPtr, WindowInfo> entry in WindowManagement.windowHandles)
                {
                    if (entry.Value == WindowManagement.windowHandles.First().Value)
                        continue;
                    string inviteCommand = "/invite " + entry.Value.CharacterName;
                    commands.Add(inviteCommand);
                }
                commands.ForEach(command =>
                {
                    WindowManagement.sentTextToHandles(command, new List<KeyValuePair<IntPtr, WindowInfo>> { windowHandles.First() });
                });
            });
        }

    }
}
