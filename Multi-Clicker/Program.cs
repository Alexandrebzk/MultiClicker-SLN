using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace MultiClicker
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Trace.Listeners.Add(new FileTraceListener("trace.log")); 
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            Application.Run(new MultiClicker());
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = (Exception)e.ExceptionObject;

            string errorMessage = $"Unhandled exception: {ex.Message}\nStack Trace:\n{ex.StackTrace}";

            string logFilePath = "error.logs";
            if (!File.Exists(logFilePath))
            {
                using (var stream = File.Create(logFilePath))
                {

                }
            }

            File.AppendAllText(logFilePath, errorMessage + Environment.NewLine);
            MessageBox.Show("An unexpected error occurred. Please check the error.logs file for more details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }


    }
}
