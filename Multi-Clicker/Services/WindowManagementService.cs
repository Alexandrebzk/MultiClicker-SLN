using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using MultiClicker.Models;
using System.Threading;
using System.Windows.Forms;

namespace MultiClicker.Services
{
    /// <summary>
    /// Service responsible for window discovery and input broadcasting.
    ///
    /// Coordinate model: a broadcast click is anchored to the window the user
    /// actually clicked (the "source"). The click point is converted to the
    /// source's client coordinates, then mapped into each target window via
    /// ClientToScreen. This stays correct when windows are not maximized, sit
    /// on different monitors, or have different positions.
    /// </summary>
    public static class WindowManagementService
    {
        #region Private Constants and Fields
        private const int ALT = 0xA4;
        private const int EXTENDEDKEY = 0x1;
        private const int KEYUP = 0x2;
        private const uint Restore = 9;

        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        // Virtual-desktop metrics (multi-monitor safe SendInput normalization).
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        // Background click messages
        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;
        private const int MK_LBUTTON = 0x0001;
        private const uint WM_MOUSEMOVE = 0x0200;
        private const uint WM_ACTIVATE = 0x0006;
        private const int WA_CLICKACTIVE = 2;

        private const uint CWP_SKIPINVISIBLE = 0x0001;
        private const uint CWP_SKIPDISABLED = 0x0002;
        private const uint CWP_SKIPTRANSPARENT = 0x0004;

        private const uint GA_ROOT = 2;

        private static readonly Random Random = new Random();

        /// <summary>
        /// True while a trigger action (broadcast, paste, group invite, ...) is
        /// executing on the dispatcher thread. The UI window-watcher skips its
        /// refresh while this is set so panels are never rebuilt mid-broadcast.
        /// </summary>
        public static volatile bool IsBroadcasting;
        #endregion

        #region Win32 API Declarations
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

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern int GetWindowTextLength(IntPtr hWnd);

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

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr ChildWindowFromPointEx(IntPtr hwndParent, POINT pt, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);
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
        /// Dictionary containing all tracked window handles and their information.
        /// Always replaced wholesale (never mutated in place) so readers on other
        /// threads can safely snapshot the reference.
        /// </summary>
        public static Dictionary<IntPtr, WindowInfo> WindowHandles { get; private set; } = new Dictionary<IntPtr, WindowInfo>();
        #endregion

        #region Window discovery
        private static readonly Regex DofusVersionRegex = new Regex(@"\b(\d+\.\d+(?:\.\d+){0,2})\b", RegexOptions.Compiled);

        /// <summary>
        /// Enumerates running Dofus clients without mutating <see cref="WindowHandles"/>.
        /// </summary>
        /// <param name="detectedVersion">The detected game version, or null if unknown.</param>
        /// <returns>The list of discovered Dofus windows.</returns>
        public static List<WindowInfo> EnumerateDofusWindows(out string detectedVersion)
        {
            var results = new List<WindowInfo>();
            string version = null;
            try
            {
                var processes = Process.GetProcesses();
                foreach (var p in processes)
                {
                    try
                    {
                        if (!p.ProcessName.StartsWith("Dofus", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var hWnd = p.MainWindowHandle;
                        if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
                            continue;

                        var title = GetWindowTitle(hWnd);
                        if (string.IsNullOrWhiteSpace(title))
                            continue;

                        var vm = DofusVersionRegex.Match(title);
                        if (vm.Success)
                        {
                            var candidate = vm.Groups[1].Value;
                            if (version == null || candidate.Length > version.Length)
                                version = candidate;
                        }

                        var characterName = ExtractDofusCharacterName(title);
                        if (string.IsNullOrEmpty(characterName))
                            continue;

                        results.Add(new WindowInfo
                        {
                            Handle = hWnd,
                            WindowName = title,
                            CharacterName = characterName,
                            RelatedPanel = null
                        });
                    }
                    catch { }
                    finally
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error enumerating Dofus windows: {ex.Message}");
            }
            detectedVersion = version;
            return results;
        }

        /// <summary>
        /// Discovers Dofus client windows, refreshes <see cref="WindowHandles"/>
        /// (in discovery order), and caches the detected game version.
        /// </summary>
        public static Dictionary<IntPtr, WindowInfo> FindDofusWindows()
        {
            string detectedVersion;
            var list = EnumerateDofusWindows(out detectedVersion);
            var found = new Dictionary<IntPtr, WindowInfo>();
            foreach (var wi in list)
            {
                if (wi.Handle != IntPtr.Zero && !found.ContainsKey(wi.Handle))
                    found[wi.Handle] = wi;
            }

            CacheDetectedVersion(detectedVersion);

            WindowHandles = found;
            Trace.WriteLine($"Found {found.Count} Dofus windows (version: {detectedVersion ?? "unknown"})");
            return found;
        }

        /// <summary>
        /// Re-scans running Dofus clients and merges the result into
        /// <see cref="WindowHandles"/>, preserving the user's panel order for
        /// windows that are still alive and appending new ones at the end.
        /// Returns true when the tracked set actually changed (window opened,
        /// closed, or its character changed) so the UI knows to rebuild panels.
        /// </summary>
        public static bool SynchronizeWindowList()
        {
            try
            {
                string detectedVersion;
                var discovered = EnumerateDofusWindows(out detectedVersion);
                var discoveredByHandle = new Dictionary<IntPtr, WindowInfo>();
                foreach (var wi in discovered)
                {
                    if (wi.Handle != IntPtr.Zero && !discoveredByHandle.ContainsKey(wi.Handle))
                        discoveredByHandle[wi.Handle] = wi;
                }

                var current = WindowHandles;
                bool changed = false;
                var updated = new Dictionary<IntPtr, WindowInfo>();

                // Keep still-alive windows in their existing (user-chosen) order.
                foreach (var kvp in current)
                {
                    if (discoveredByHandle.TryGetValue(kvp.Key, out var fresh))
                    {
                        if (!string.Equals(kvp.Value.CharacterName, fresh.CharacterName, StringComparison.Ordinal))
                        {
                            // The user switched character on this client.
                            kvp.Value.CharacterName = fresh.CharacterName;
                            changed = true;
                        }
                        kvp.Value.WindowName = fresh.WindowName;
                        updated[kvp.Key] = kvp.Value;
                    }
                    else
                    {
                        changed = true; // window closed
                    }
                }

                // Append newly discovered windows at the end.
                foreach (var kvp in discoveredByHandle)
                {
                    if (!updated.ContainsKey(kvp.Key))
                    {
                        updated[kvp.Key] = kvp.Value;
                        changed = true;
                    }
                }

                if (changed)
                {
                    WindowHandles = updated;
                    Trace.WriteLine($"Window list synchronized: {updated.Count} Dofus windows tracked.");
                }

                CacheDetectedVersion(detectedVersion);
                return changed;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error synchronizing window list: {ex.Message}");
                return false;
            }
        }

        private static void CacheDetectedVersion(string detectedVersion)
        {
            if (string.IsNullOrEmpty(detectedVersion)) return;
            try
            {
                if (ConfigurationService.Current.General.GameVersion != detectedVersion)
                {
                    ConfigurationService.Current.General.GameVersion = detectedVersion;
                    ConfigurationService.SaveConfig();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error caching detected Dofus version: {ex.Message}");
            }
        }

        private static string ExtractDofusCharacterName(string windowTitle)
        {
            try
            {
                var sepIdx = windowTitle.IndexOf(" - ", StringComparison.Ordinal);
                var head = sepIdx > 0 ? windowTitle.Substring(0, sepIdx).Trim() : windowTitle.Trim();
                var spaceIdx = head.IndexOf(' ');
                return spaceIdx > 0 ? head.Substring(0, spaceIdx).Trim() : head;
            }
            catch
            {
                return windowTitle;
            }
        }

        /// <summary>
        /// Checks if the specified handle is a tracked window.
        /// </summary>
        public static bool IsRelatedHandle(IntPtr handle)
        {
            return WindowHandles.ContainsKey(handle);
        }

        /// <summary>
        /// True when the given screen point is over a tracked window (resolves
        /// child windows to their top-level ancestor first).
        /// </summary>
        public static bool IsPointOverTrackedWindow(POINT screenPoint)
        {
            try
            {
                var hWnd = WindowFromPoint(screenPoint);
                if (hWnd == IntPtr.Zero) return false;
                if (WindowHandles.ContainsKey(hWnd)) return true;
                var root = GetAncestor(hWnd, GA_ROOT);
                return root != IntPtr.Zero && WindowHandles.ContainsKey(root);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Reorders the WindowHandles dictionary to match the order of character names provided
        /// </summary>
        public static void ReorderWindowHandles(List<string> orderedCharacterNames)
        {
            try
            {
                var current = WindowHandles;
                var reorderedHandles = new Dictionary<IntPtr, WindowInfo>();

                foreach (var characterName in orderedCharacterNames)
                {
                    var windowEntry = current.FirstOrDefault(kvp => kvp.Value.CharacterName == characterName);
                    if (windowEntry.Key != IntPtr.Zero)
                    {
                        reorderedHandles[windowEntry.Key] = windowEntry.Value;
                    }
                }

                foreach (var kvp in current)
                {
                    if (!reorderedHandles.ContainsKey(kvp.Key))
                    {
                        reorderedHandles[kvp.Key] = kvp.Value;
                    }
                }

                WindowHandles = reorderedHandles;
                Trace.WriteLine($"Reordered WindowHandles to match panel order. Total windows: {reorderedHandles.Count}");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error reordering window handles: {ex.Message}");
            }
        }

        /// <summary>
        /// Registers a window discovered outside the normal enumeration path
        /// (manual-mode panels matched by process name). Replaces the dictionary
        /// wholesale to preserve snapshot semantics for concurrent readers.
        /// </summary>
        public static void RegisterWindow(IntPtr handle, WindowInfo info)
        {
            var current = WindowHandles;
            if (current.ContainsKey(handle)) return;
            var updated = new Dictionary<IntPtr, WindowInfo>(current) { [handle] = info };
            WindowHandles = updated;
        }
        #endregion

        #region Focus helpers
        /// <summary>
        /// Sets the specified window to foreground. The injected ALT tap grants
        /// this process the right to change the foreground window; the event is
        /// flagged LLKHF_INJECTED and therefore ignored by our own hooks.
        /// </summary>
        public static void SetHandleToForeground(IntPtr handle)
        {
            try
            {
                if (IsIconic(handle))
                {
                    ShowWindow(handle, Restore);
                }

                if (GetForegroundWindow() == handle) return;

                // AttachThreadInput ties our input queue to the target's UI thread
                // (and the current foreground thread), which lifts the OS
                // foreground lock so SetForegroundWindow actually takes effect.
                // This is what makes the FIRST target of a broadcast sweep switch
                // reliably instead of intermittently keeping the previous window.
                uint currentThread = GetCurrentThreadId();
                uint targetThread = GetWindowThreadProcessId(handle, out _);
                uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);

                bool attachedTarget = false, attachedForeground = false;
                try
                {
                    if (targetThread != 0 && targetThread != currentThread)
                        attachedTarget = AttachThreadInput(currentThread, targetThread, true);
                    if (foregroundThread != 0 && foregroundThread != currentThread && foregroundThread != targetThread)
                        attachedForeground = AttachThreadInput(currentThread, foregroundThread, true);

                    // ALT tap: a further safeguard that grants foreground rights.
                    keybd_event((byte)ALT, 0x45, EXTENDEDKEY | 0, 0);
                    keybd_event((byte)ALT, 0x45, EXTENDEDKEY | KEYUP, 0);

                    BringWindowToTop(handle);
                    SetForegroundWindow(handle);
                    SetActiveWindow(handle);
                    SetFocus(handle);
                }
                finally
                {
                    if (attachedTarget) AttachThreadInput(currentThread, targetThread, false);
                    if (attachedForeground) AttachThreadInput(currentThread, foregroundThread, false);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error setting window to foreground: {ex.Message}");
            }
        }

        /// <summary>
        /// Waits until the given window actually is the foreground window.
        /// SetForegroundWindow is asynchronous in practice; injecting input
        /// before the switch completes sends it to the wrong window.
        /// </summary>
        private static bool WaitForForeground(IntPtr handle, int timeoutMs)
        {
            if (GetForegroundWindow() == handle) return true;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                Thread.Sleep(5);
                if (GetForegroundWindow() == handle) return true;
            }
            var ok = GetForegroundWindow() == handle;
            if (!ok) Trace.WriteLine($"WaitForForeground timed out for window {handle}.");
            return ok;
        }
        #endregion

        #region Click broadcasting
        /// <summary>
        /// Broadcasts a click (or double click) to every tracked window. The
        /// click point is anchored to the source window under the cursor and
        /// mapped into each target's client area.
        /// </summary>
        public static void BroadcastClick(POINT screenPoint, bool doubleClick)
        {
            try
            {
                var handles = WindowHandles;
                if (handles.Count == 0) return;

                // Resolve the source window: the tracked window under the click,
                // falling back to the tracked foreground window.
                IntPtr source = ResolveTrackedWindowAt(screenPoint, handles);
                if (source == IntPtr.Zero)
                {
                    var fg = GetForegroundWindow();
                    if (handles.ContainsKey(fg)) source = fg;
                }
                if (source == IntPtr.Zero)
                {
                    Trace.WriteLine("Broadcast aborted: click origin is not over a tracked window.");
                    return;
                }

                var clientPoint = screenPoint;
                if (!ScreenToClient(source, ref clientPoint))
                {
                    Trace.WriteLine("Broadcast aborted: ScreenToClient failed for source window.");
                    return;
                }

                bool background = ConfigurationService.Current.General.PreferBackgroundClicks;
                var targets = GetReorderedWindowList(PanelManagementService.SelectedPanel);

                POINT originalCursor;
                bool hasCursor = GetCursorPos(out originalCursor);

                // Windows refuses foreground changes while a mouse button is
                // physically held (input-capture lock). The trigger fires on
                // button-DOWN, so without this wait the very first target of the
                // sweep loses its focus switch and its click lands on the still
                // -foreground source window instead.
                if (!background)
                {
                    WaitForPhysicalMouseRelease(1000);
                }

                foreach (var entry in targets)
                {
                    var target = entry.Key;
                    if (!IsWindow(target)) continue;

                    var targetPoint = clientPoint;
                    if (!ClientToScreen(target, ref targetPoint)) continue;

                    int delay = GetRandomDelay();

                    if (background)
                    {
                        if (!TryPerformBackgroundClick(target, targetPoint.X, targetPoint.Y, delay, doubleClick))
                        {
                            Trace.WriteLine($"Background click delivery failed for '{entry.Value.CharacterName}'.");
                        }
                    }
                    else if (!PerformForegroundClick(target, targetPoint.X, targetPoint.Y, delay, doubleClick))
                    {
                        Trace.WriteLine($"Skipped '{entry.Value.CharacterName}': window refused the foreground switch.");
                    }

                    Thread.Sleep(Math.Max(5, delay / 4));
                }

                if (!background && hasCursor)
                {
                    // The reordered target list ends on the selected window, so
                    // focus lands back where the user started; also put the real
                    // cursor back where it was.
                    MoveCursorAbsolute(originalCursor.X, originalCursor.Y);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error broadcasting click: {ex.Message}");
            }
        }

        private static IntPtr ResolveTrackedWindowAt(POINT screenPoint, Dictionary<IntPtr, WindowInfo> handles)
        {
            try
            {
                var hWnd = WindowFromPoint(screenPoint);
                if (hWnd == IntPtr.Zero) return IntPtr.Zero;
                if (handles.ContainsKey(hWnd)) return hWnd;
                var root = GetAncestor(hWnd, GA_ROOT);
                return root != IntPtr.Zero && handles.ContainsKey(root) ? root : IntPtr.Zero;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Attempts to deliver a left-click to the deepest descendant of the target window
        /// at the given screen coordinates without stealing focus. Returns true on success.
        /// </summary>
        private static bool TryPerformBackgroundClick(IntPtr topLevelHandle, int screenX, int screenY, int delay, bool doubleClick)
        {
            try
            {
                // Drill down to the deepest visible/enabled child at the point so
                // input reaches Dofus's 3D canvas / sub-controls instead of the top-level frame.
                IntPtr target = topLevelHandle;
                for (int i = 0; i < 8; i++)
                {
                    var clientPt = new POINT { X = screenX, Y = screenY };
                    if (!ScreenToClient(target, ref clientPt))
                    {
                        break;
                    }

                    var child = ChildWindowFromPointEx(target, clientPt,
                        CWP_SKIPINVISIBLE | CWP_SKIPDISABLED | CWP_SKIPTRANSPARENT);

                    if (child == IntPtr.Zero || child == target)
                    {
                        break;
                    }
                    target = child;
                }

                var localPoint = new POINT { X = screenX, Y = screenY };
                if (!ScreenToClient(target, ref localPoint))
                {
                    return false;
                }

                int lParam = (localPoint.Y << 16) | (localPoint.X & 0xFFFF);

                // Hover first so the canvas updates its internal state.
                PostMessage(target, WM_MOUSEMOVE, IntPtr.Zero, (IntPtr)lParam);

                // Nudge activation without stealing the foreground.
                SendMessage(target, WM_ACTIVATE, (IntPtr)WA_CLICKACTIVE, IntPtr.Zero);

                // SendMessage is synchronous: the target window's WndProc has to process it.
                SendMessage(target, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, (IntPtr)lParam);
                Thread.Sleep(Math.Max(1, delay / 4));
                SendMessage(target, WM_LBUTTONUP, IntPtr.Zero, (IntPtr)lParam);

                if (doubleClick)
                {
                    // A second down/up pair within double-click time. Games track
                    // their own click timing; WM_LBUTTONDBLCLK is only generated
                    // for CS_DBLCLKS window classes and Unity ignores it.
                    Thread.Sleep(50);
                    SendMessage(target, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, (IntPtr)lParam);
                    Thread.Sleep(Math.Max(1, delay / 4));
                    SendMessage(target, WM_LBUTTONUP, IntPtr.Zero, (IntPtr)lParam);
                }

                Thread.Sleep(Math.Max(1, delay / 4));
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Background click failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// SendInput-based click path: brings the target to the foreground, waits
        /// for the switch to actually complete, then clicks at the target point.
        /// Returns false WITHOUT clicking when the window never reached the
        /// foreground - clicking anyway would deliver the click to whichever
        /// window is stacked at those coordinates (usually the wrong client).
        /// </summary>
        private static bool PerformForegroundClick(IntPtr windowHandle, int screenX, int screenY, int delay, bool doubleClick)
        {
            bool focused = false;
            for (int attempt = 0; attempt < 3 && !focused; attempt++)
            {
                SetHandleToForeground(windowHandle);
                focused = WaitForForeground(windowHandle, 300);
            }
            if (!focused) return false;

            Thread.Sleep(Math.Max(1, delay / 4));
            SendAbsoluteLeftClick(screenX, screenY);
            if (doubleClick)
            {
                Thread.Sleep(60);
                SendAbsoluteLeftClick(screenX, screenY);
            }
            Thread.Sleep(Math.Max(1, delay / 4));
            return true;
        }

        /// <summary>
        /// Waits until every physical mouse button is released. The button that
        /// triggered a broadcast is still down when the action starts, and a held
        /// button blocks SetForegroundWindow system-wide.
        /// </summary>
        private static void WaitForPhysicalMouseRelease(int timeoutMs)
        {
            int[] buttons = { 0x01, 0x02, 0x04, 0x05, 0x06 }; // L, R, M, X1, X2
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                bool anyDown = false;
                foreach (var vk in buttons)
                {
                    if ((GetAsyncKeyState(vk) & 0x8000) != 0) { anyDown = true; break; }
                }
                if (!anyDown) return;
                Thread.Sleep(10);
            }
            Trace.WriteLine("Proceeding with broadcast although a mouse button is still held down.");
        }

        /// <summary>
        /// Sends a left click at absolute screen coordinates, normalized against
        /// the virtual desktop so multi-monitor setups are addressed correctly.
        /// </summary>
        private static void SendAbsoluteLeftClick(int screenX, int screenY)
        {
            if (!TryNormalizeToVirtualDesktop(screenX, screenY, out var nx, out var ny)) return;

            var inputs = new INPUT[3];
            inputs[0] = MouseInput(nx, ny, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK);
            inputs[1] = MouseInput(nx, ny, MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK);
            inputs[2] = MouseInput(nx, ny, MOUSEEVENTF_LEFTUP | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK);
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private static void MoveCursorAbsolute(int screenX, int screenY)
        {
            if (!TryNormalizeToVirtualDesktop(screenX, screenY, out var nx, out var ny)) return;
            var inputs = new[] { MouseInput(nx, ny, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK) };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private static bool TryNormalizeToVirtualDesktop(int screenX, int screenY, out int nx, out int ny)
        {
            nx = ny = 0;
            int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            if (vw <= 1 || vh <= 1) return false;
            nx = (int)(((screenX - vx) * 65535L) / (vw - 1));
            ny = (int)(((screenY - vy) * 65535L) / (vh - 1));
            return true;
        }

        private static INPUT MouseInput(int dx, int dy, uint flags)
        {
            return new INPUT
            {
                type = INPUT_MOUSE,
                u = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = dx,
                        dy = dy,
                        mouseData = 0,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }
        #endregion

        #region Key simulation
        /// <summary>
        /// Simulates a key combination with modifiers on the target window.
        /// </summary>
        public static void SimulateKeyCombination(IntPtr windowHandle, KeyCombination keyCombination)
        {
            try
            {
                if (keyCombination.Key == Keys.None) return;

                var keys = new List<Keys>();
                if (keyCombination.Control) keys.Add(Keys.LControlKey);
                if (keyCombination.Shift) keys.Add(Keys.LShiftKey);
                if (keyCombination.Alt) keys.Add(Keys.LMenu);
                keys.Add(keyCombination.Key);

                SimulateKeyPressListToWindow(windowHandle, keys, 0);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error simulating key combination for window {windowHandle}: {ex.Message}");
            }
        }

        /// <summary>
        /// Simulates a key press with special handling for TAB and ENTER keys using scan codes.
        /// </summary>
        public static void SimulateKeyPress(IntPtr windowHandle, Keys key)
        {
            try
            {
                SetHandleToForeground(windowHandle);
                WaitForForeground(windowHandle, 250);

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

                var inputs = new INPUT[2];
                inputs[0] = KeyInput(key, 0);
                inputs[1] = KeyInput(key, KEYEVENTF_KEYUP);
                SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error simulating key press for window {windowHandle}: {ex.Message}");
            }
        }

        /// <summary>
        /// Simulates a chord: all keys pressed together (in order), then released
        /// in reverse order, sent as a single atomic SendInput batch.
        /// </summary>
        public static void SimulateKeyPressListToWindow(IntPtr windowHandle, List<Keys> keys, int delay)
        {
            SetHandleToForeground(windowHandle);
            WaitForForeground(windowHandle, 250);

            var inputs = new List<INPUT>();

            foreach (var key in keys)
            {
                if (key == Keys.Tab)
                {
                    SendTab(windowHandle);
                    continue;
                }

                if (key == Keys.Enter || key == Keys.Return)
                {
                    SendEnter(windowHandle);
                    continue;
                }
                inputs.Add(KeyInput(key, 0));
            }

            for (int i = keys.Count - 1; i >= 0; i--)
            {
                if (keys[i] == Keys.Tab || keys[i] == Keys.Enter || keys[i] == Keys.Return)
                {
                    continue;
                }
                inputs.Add(KeyInput(keys[i], KEYEVENTF_KEYUP));
            }

            if (inputs.Count > 0)
            {
                SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
            }

            Thread.Sleep(Math.Max(10, delay));
        }

        private const uint MAPVK_VK_TO_VSC = 0;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        /// <summary>
        /// Keys that must carry the extended-key flag; without it the OS maps
        /// them onto their numeric-keypad twins (Delete becomes numpad '.', etc).
        /// </summary>
        private static bool IsExtendedKey(Keys key)
        {
            switch (key)
            {
                case Keys.Insert:
                case Keys.Delete:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.NumLock:
                case Keys.RControlKey:
                case Keys.RMenu:
                case Keys.Divide:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Builds a keyboard INPUT carrying BOTH the virtual key and its hardware
        /// scan code. Dofus 3.x (Unity) consumes Raw Input, which reports the scan
        /// code: events injected with wScan = 0 are seen as "key 0" and dropped.
        /// This is why the codebase already sends TAB/ENTER via keybd_event with
        /// explicit scan codes.
        /// </summary>
        private static INPUT KeyInput(Keys key, uint flags)
        {
            uint scan = MapVirtualKey((uint)key, MAPVK_VK_TO_VSC);
            if (IsExtendedKey(key)) flags |= KEYEVENTF_EXTENDEDKEY;

            return new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = (ushort)key,
                        wScan = (ushort)scan,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }

        /// <summary>
        /// Presses then releases a single key (with modifiers held around it),
        /// as one atomic SendInput batch.
        /// </summary>
        private static void TapKey(Keys key, Keys[] modifiers = null)
        {
            var inputs = new List<INPUT>();
            if (modifiers != null)
                foreach (var m in modifiers) inputs.Add(KeyInput(m, 0));

            inputs.Add(KeyInput(key, 0));
            inputs.Add(KeyInput(key, KEYEVENTF_KEYUP));

            if (modifiers != null)
                for (int i = modifiers.Length - 1; i >= 0; i--) inputs.Add(KeyInput(modifiers[i], KEYEVENTF_KEYUP));

            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>
        /// Types digits into whichever control currently has keyboard focus.
        /// Uses VkKeyScan so the active layout is respected: on the French AZERTY
        /// layout the top-row digits require Shift, and injecting the bare key
        /// would type "&amp;é\"'(-è_çà" instead of numbers.
        /// </summary>
        private static void TypeDigitsToFocusedField(string digits, int perKeyDelayMs = 12)
        {
            foreach (char c in digits)
            {
                short scan = VkKeyScan(c);
                if (scan == -1)
                {
                    Trace.WriteLine($"Cannot map character '{c}' on the current keyboard layout.");
                    continue;
                }

                var key = (Keys)(scan & 0xFF);
                bool needsShift = (scan & 0x100) != 0;

                TapKey(key, needsShift ? new[] { Keys.LShiftKey } : null);
                Thread.Sleep(perKeyDelayMs);
            }
        }

        /// <summary>
        /// Empties the focused text field: select-all + Delete, then a run of
        /// End+BackSpace as a fallback for fields that ignore Ctrl+A.
        /// </summary>
        private static void ClearFocusedField(int expectedMaxLength = 10)
        {
            TapKey(Keys.A, new[] { Keys.LControlKey });
            Thread.Sleep(20);
            TapKey(Keys.Delete);
            Thread.Sleep(20);

            TapKey(Keys.End);
            Thread.Sleep(12);
            for (int i = 0; i < expectedMaxLength; i++)
            {
                TapKey(Keys.Back);
                Thread.Sleep(8);
            }
        }

        /// <summary>
        /// Sends TAB using keybd_event with its scan code (required by the game).
        /// </summary>
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
        /// Sends ENTER using keybd_event with its scan code (required by the game).
        /// </summary>
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
        /// Types a string into the (foreground) target window character by character.
        /// </summary>
        private static void TypeTextToWindow(IntPtr windowHandle, string text)
        {
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
                        key = (Keys)(keyCode & 0xFF);
                        needsShift = (keyCode & 0x100) != 0;
                        break;
                }

                if (needsShift)
                {
                    SimulateKeyPressListToWindow(windowHandle, new List<Keys> { Keys.LShiftKey, key }, 0);
                }
                else
                {
                    SimulateKeyPress(windowHandle, key);
                }
            }
        }

        public static void GroupCharacters()
        {
            try
            {
                var handles = WindowHandles;
                var selectedPanel = PanelManagementService.SelectedPanel;
                if (selectedPanel == null)
                {
                    Trace.WriteLine("No panel selected for group characters");
                    return;
                }

                var selectedWindow = handles.FirstOrDefault(w => w.Value.RelatedPanel == selectedPanel);
                if (selectedWindow.Key == IntPtr.Zero)
                {
                    Trace.WriteLine("No window found for selected panel");
                    return;
                }

                Trace.WriteLine($"Sending group invitations from SELECTED panel: {selectedWindow.Value.CharacterName}");

                var otherWindows = handles.Where(w => w.Key != selectedWindow.Key).ToList();

                var discussionKeybind = ConfigurationService.Current.Keybinds[TRIGGERS.DOFUS_OPEN_DISCUSSION];
                SimulateKeyCombination(selectedWindow.Key, discussionKeybind);
                Thread.Sleep(100);

                foreach (var windowEntry in otherWindows)
                {
                    var characterName = windowEntry.Value.CharacterName;
                    if (string.IsNullOrEmpty(characterName))
                    {
                        Trace.WriteLine("Character name is empty, cannot send group characters command");
                        continue;
                    }

                    Trace.WriteLine($"Sending group characters invitation for character: {characterName}");

                    SetHandleToForeground(selectedWindow.Key);
                    WaitForForeground(selectedWindow.Key, 250);
                    Thread.Sleep(50);

                    TypeTextToWindow(selectedWindow.Key, $"/invite {characterName}");
                    SendEnter(selectedWindow.Key);

                    Trace.WriteLine($"Group characters invitation sent for: {characterName}");
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
        /// Gets a random delay based on configuration.
        /// </summary>
        private static int GetRandomDelay()
        {
            var config = ConfigurationService.Current.General;
            var minDelay = Math.Max(5, config.MinimumFollowDelay);
            var maxDelay = Math.Max(minDelay + 5, config.MaximumFollowDelay);
            lock (Random)
            {
                return Random.Next(minDelay, maxDelay);
            }
        }

        /// <summary>
        /// Reorders the window list to start from the next window after the
        /// selected one, ending on the selected window itself (so a foreground
        /// broadcast finishes with focus back on the user's active client).
        /// </summary>
        private static List<KeyValuePair<IntPtr, WindowInfo>> GetReorderedWindowList(object selectedPanel)
        {
            try
            {
                var windowList = WindowHandles.ToList();
                var selectedWindowEntry = windowList.FirstOrDefault(w => w.Value.RelatedPanel == selectedPanel);

                if (selectedWindowEntry.Key != IntPtr.Zero)
                {
                    var selectedIndex = windowList.FindIndex(kvp => kvp.Key == selectedWindowEntry.Key);
                    if (selectedIndex >= 0 && selectedIndex < windowList.Count - 1)
                    {
                        return windowList
                            .Skip(selectedIndex + 1)
                            .Concat(windowList.Take(selectedIndex + 1))
                            .ToList();
                    }
                }

                return windowList;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error reordering window list: {ex.Message}");
                return WindowHandles.ToList();
            }
        }
        #endregion

        #region OCR (Fill HDV)
        private static readonly int[] AllowedSellModes = { 1, 10, 100, 1000 };

        /// <summary>
        /// Reads the HDV sell panel via OCR and types the undercut price.
        /// Flow: read the current sell quantity (constrained to 1/10/100/1000),
        /// then read only the matching lot price, then fill price-1.
        /// Aborts loudly rather than typing a number it isn't sure about.
        /// </summary>
        public static void FillSellPriceBasedOnForeGroundWindow()
        {
            Trace.WriteLine("Starting price analysis");
            try
            {
                var positions = ConfigurationService.Current.Positions;

                var modeRect = GetRectangleFromPosition(positions[TRIGGERS_POSITIONS.SELL_CURRENT_MODE]);
                int mode = OCRService.RecognizeNumberOnScreen(modeRect, AllowedSellModes);
                if (mode < 0)
                {
                    Trace.WriteLine("FILL_HDV aborted: could not read the current sell quantity (1/10/100/1000).");
                    return;
                }

                TRIGGERS_POSITIONS lotPosition;
                switch (mode)
                {
                    case 1: lotPosition = TRIGGERS_POSITIONS.SELL_LOT_1; break;
                    case 10: lotPosition = TRIGGERS_POSITIONS.SELL_LOT_10; break;
                    case 100: lotPosition = TRIGGERS_POSITIONS.SELL_LOT_100; break;
                    default: lotPosition = TRIGGERS_POSITIONS.SELL_LOT_1000; break;
                }

                var lotRect = GetRectangleFromPosition(positions[lotPosition]);
                int price = OCRService.RecognizeNumberOnScreen(lotRect);
                if (price < 0)
                {
                    Trace.WriteLine($"FILL_HDV aborted: could not read the lot-of-{mode} price.");
                    return;
                }
                if (price <= 1)
                {
                    Trace.WriteLine($"FILL_HDV aborted: recognized price {price} cannot be undercut.");
                    return;
                }

                int amountToFill = price - 1;
                var target = GetForegroundWindow();
                Trace.WriteLine($"FILL_HDV: sell quantity x{mode}, market price {price}, filling {amountToFill} " +
                                $"into focused window '{GetWindowTitle(target)}' (tracked: {IsRelatedHandle(target)})");

                // Warn only: window tracking can lag behind a client relog, and
                // refusing to type would be a worse failure than typing into a
                // window the user themselves just clicked.
                if (!IsRelatedHandle(target))
                    Trace.WriteLine("FILL_HDV warning: focused window is not in the tracked client list.");

                // SendKeys was used here previously; it injects virtual keys with
                // no scan code, which Dofus (Unity Raw Input) discards - the price
                // was recognized correctly but never appeared in the field.
                var sw = Stopwatch.StartNew();
                ClearFocusedField();
                TypeDigitsToFocusedField(amountToFill.ToString());
                Trace.WriteLine($"FILL_HDV: typed {amountToFill} in {sw.ElapsedMilliseconds} ms.");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"HDV OCR processing failed: {ex.Message}, Trace : {ex.StackTrace}");
            }
            Trace.WriteLine("-------------------------");
        }

        /// <summary>
        /// Converts Position to Rectangle (positions are absolute screen coordinates).
        /// </summary>
        private static Rectangle GetRectangleFromPosition(Position position)
        {
            return new Rectangle(position.X, position.Y, position.Width, position.Height);
        }
        #endregion
    }
}
