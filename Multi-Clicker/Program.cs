using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MultiClicker.Core;
using MultiClicker.Services;
using MultiClicker.UI;

namespace MultiClicker
{
    internal static class Program
    {
        #region Win32 API for Hook Management
        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static IntPtr _mouseHookID = IntPtr.Zero;
        private static IntPtr _keyboardHookID = IntPtr.Zero;
        private static HookProc _mouseProc;
        private static HookProc _keyboardProc;
        #endregion

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            try
            {
                // Initialize tracing
                Trace.Listeners.Add(new FileTraceListener("trace.log"));

                // Configure Windows Forms. Per-monitor DPI awareness is required
                // for correct click coordinates on scaled displays: without it,
                // the OS virtualizes our screen coordinates while the game (DPI
                // aware) interprets the client coordinates we post in physical
                // pixels, so background clicks land in the wrong place.
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Initialize localization system
                LocalizationService.Initialize();

                // Set up global exception handling
                AppDomain.CurrentDomain.UnhandledException += ApplicationManager.Instance.HandleUnhandledException;
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) =>
                {
                    Trace.WriteLine($"Unhandled UI exception: {e.Exception}");
                };
                Application.ApplicationExit += (s, e) => Cleanup();
                
                // Initialize the application manager
                ApplicationManager.Instance.Initialize();
                
                // Setup hooks
                SetupHooks();
                
                // Start the main form
                Application.Run(new MultiClickerForm());
            }
            catch (Exception ex)
            {
                // Handle any startup errors
                var errorMessage = $"Failed to start application: {ex.Message}\nStack Trace:\n{ex.StackTrace}";
                Trace.WriteLine(errorMessage);
                
                MessageBox.Show(
                    "Failed to start the application. Please check the error logs for more details.",
                    "Startup Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                Cleanup();
            }
        }

        /// <summary>
        /// Sets up the global hooks for mouse and keyboard input
        /// </summary>
        private static void SetupHooks()
        {
            try
            {
                _mouseProc = HookManagementService.MouseHookCallback;
                _keyboardProc = HookManagementService.KeyboardHookCallback;
                
                _mouseHookID = SetHook(14, _mouseProc); // WH_MOUSE_LL
                _keyboardHookID = SetHook(13, _keyboardProc); // WH_KEYBOARD_LL
                
                Trace.WriteLine("Global hooks setup successfully");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error setting up hooks: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets up a specific hook
        /// </summary>
        /// <param name="hookType">The type of hook to install</param>
        /// <param name="proc">The hook procedure</param>
        /// <returns>The hook handle</returns>
        private static IntPtr SetHook(int hookType, HookProc proc)
        {
            try
            {
                using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
                using (var curModule = curProcess.MainModule)
                {
                    return SetWindowsHookEx(hookType, proc, GetModuleHandle(curModule.ModuleName), 0);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error setting hook: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Cleanup resources before application exit
        /// </summary>
        private static void Cleanup()
        {
            try
            {
                // Cleanup hooks
                if (_mouseHookID != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_mouseHookID);
                    _mouseHookID = IntPtr.Zero;
                }
                
                if (_keyboardHookID != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_keyboardHookID);
                    _keyboardHookID = IntPtr.Zero;
                }

                // Shutdown hook service (cancels reconciliation timer, clears state)
                HookManagementService.Shutdown();

                // Shutdown application manager
                ApplicationManager.Instance.Shutdown();
                
                Trace.WriteLine("Application cleanup completed");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }
    }
}
