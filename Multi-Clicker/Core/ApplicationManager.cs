using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MultiClicker.Models;
using MultiClicker.Services;

namespace MultiClicker.Core
{
    /// <summary>
    /// Core application manager that coordinates all services and handles application lifecycle
    /// </summary>
    public class ApplicationManager
    {
        #region Singleton Implementation
        private static ApplicationManager _instance;
        private static readonly object _lock = new object();

        public static ApplicationManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new ApplicationManager();
                    }
                }
                return _instance;
            }
        }

        private ApplicationManager()
        {
            Initialize();
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets a value indicating whether the application is initialized
        /// </summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// Gets the application configuration
        /// </summary>
        public Config Configuration => ConfigurationService.Current;
        #endregion

        #region Public Methods
        /// <summary>
        /// Initializes the application manager and all services
        /// </summary>
        public void Initialize()
        {
            try
            {
                if (IsInitialized)
                    return;

                Trace.WriteLine("Initializing Application Manager...");

                // Initialize configuration service
                ConfigurationService.LoadConfig();
                Trace.WriteLine("Configuration service initialized");

                // Initialize OCR service
                OCRService.InitializeEngine();
                Trace.WriteLine("OCR service initialized");

                // Initialize hook management service
                HookManagementService.Initialize();
                Trace.WriteLine("Hook management service initialized");

                // Find and initialize window handles
                RefreshWindowHandles();

                IsInitialized = true;
                Trace.WriteLine("Application Manager initialization completed");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to initialize Application Manager: {ex.Message}");
                IsInitialized = false;
                throw;
            }
        }

        /// <summary>
        /// Refreshes the list of tracked windows
        /// </summary>
        public void RefreshWindowHandles()
        {
            try
            {
                var gameVersion = Configuration.General.GameVersion;
                WindowManagementService.FindWindows("- " + gameVersion + " -");
                Trace.WriteLine($"Window handles refreshed. Found {WindowManagementService.WindowHandles.Count} windows");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error refreshing window handles: {ex.Message}");
            }
        }

        /// <summary>
        /// Shuts down the application manager and cleans up resources
        /// </summary>
        public void Shutdown()
        {
            try
            {
                Trace.WriteLine("Shutting down Application Manager...");

                // Save configuration before shutdown
                ConfigurationService.SaveConfig();

                // Dispose OCR resources
                OCRService.Dispose();

                IsInitialized = false;
                Trace.WriteLine("Application Manager shutdown completed");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error during Application Manager shutdown: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates a panel configuration
        /// </summary>
        /// <param name="panelName">The name of the panel</param>
        /// <param name="config">The panel configuration</param>
        public void UpdatePanelConfiguration(string panelName, PanelConfig config)
        {
            try
            {
                ConfigurationService.UpdatePanelConfig(panelName, config);
                Trace.WriteLine($"Panel configuration updated for: {panelName}");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error updating panel configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates a keybind configuration
        /// </summary>
        /// <param name="trigger">The trigger to update</param>
        /// <param name="key">The new key binding</param>
        public void UpdateKeybind(TRIGGERS trigger, System.Windows.Forms.Keys key)
        {
            try
            {
                ConfigurationService.UpdateKeybind(trigger, key);
                Trace.WriteLine($"Keybind updated for {trigger}: {key}");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error updating keybind: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the list of available character names
        /// </summary>
        /// <returns>List of character names</returns>
        public List<string> GetCharacterNames()
        {
            try
            {
                return WindowManagementService.WindowHandles.Values
                    .Select(w => w.CharacterName)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error getting character names: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// Gets window information for a specific character
        /// </summary>
        /// <param name="characterName">The character name</param>
        /// <returns>Window information or null if not found</returns>
        public WindowInfo GetWindowInfoForCharacter(string characterName)
        {
            try
            {
                return WindowManagementService.WindowHandles.Values
                    .FirstOrDefault(w => w.CharacterName == characterName);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error getting window info for character {characterName}: {ex.Message}");
                return null;
            }
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Handles unhandled exceptions
        /// </summary>
        /// <param name="sender">The sender</param>
        /// <param name="e">Exception event arguments</param>
        public void HandleUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var ex = (Exception)e.ExceptionObject;
                var errorMessage = $"Unhandled exception: {ex.Message}\nStack Trace:\n{ex.StackTrace}";

                Trace.WriteLine(errorMessage);

                // Log to file
                LogErrorToFile(errorMessage);

                // Show user-friendly message
                MessageBox.Show(
                    "An unexpected error occurred. Please check the error.logs file for more details.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch (Exception logEx)
            {
                // Last resort logging
                Trace.WriteLine($"Failed to handle unhandled exception: {logEx.Message}");
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Logs error message to file
        /// </summary>
        /// <param name="errorMessage">The error message to log</param>
        private void LogErrorToFile(string errorMessage)
        {
            try
            {
                var logFilePath = "error.logs";
                if (!File.Exists(logFilePath))
                {
                    using (var stream = File.Create(logFilePath))
                    {
                        // File created
                    }
                }

                File.AppendAllText(logFilePath, errorMessage + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to log error to file: {ex.Message}");
            }
        }
        #endregion
    }
}
