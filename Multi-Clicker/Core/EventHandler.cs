using System;
using System.Diagnostics;
using MultiClicker.Services;

namespace MultiClicker.Core
{
    /// <summary>
    /// Centralized event handler for application-wide events
    /// </summary>
    public static class EventHandler
    {
        #region Event Registration
        /// <summary>
        /// Registers all application event handlers
        /// </summary>
        public static void RegisterEventHandlers()
        {
            try
            {
                // Register hook service events
                HookManagementService.ShouldOpenMenuTravel += OnTravelMenuRequested;
                HookManagementService.ShouldOpenPositionConfiguration += OnKeyBindFormRequested;

                Trace.WriteLine("Application event handlers registered successfully");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error registering event handlers: {ex.Message}");
            }
        }

        /// <summary>
        /// Unregisters all application event handlers
        /// </summary>
        public static void UnregisterEventHandlers()
        {
            try
            {
                // Unregister hook service events
                HookManagementService.ShouldOpenMenuTravel -= OnTravelMenuRequested;
                HookManagementService.ShouldOpenPositionConfiguration -= OnKeyBindFormRequested;

                Trace.WriteLine("Application event handlers unregistered successfully");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error unregistering event handlers: {ex.Message}");
            }
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Handles travel menu request events
        /// </summary>
        private static void OnTravelMenuRequested()
        {
            try
            {
                Trace.WriteLine("Travel menu requested");
                
                // This event will be handled by the main form
                // The event is propagated through the service layer
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error handling travel menu request: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles key bind form request events
        /// </summary>
        private static void OnKeyBindFormRequested()
        {
            try
            {
                Trace.WriteLine("Key bind form requested");
                
                // This event will be handled by the main form
                // The event is propagated through the service layer
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error handling key bind form request: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles application shutdown events
        /// </summary>
        /// <param name="sender">The event sender</param>
        /// <param name="e">Event arguments</param>
        public static void OnApplicationShutdown(object sender, EventArgs e)
        {
            try
            {
                Trace.WriteLine("Application shutdown initiated");
                
                // Save configuration
                ConfigurationService.SaveConfig();
                
                // Dispose services
                OCRService.Dispose();
                
                // Unregister event handlers
                UnregisterEventHandlers();
                
                Trace.WriteLine("Application shutdown completed");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error during application shutdown: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles configuration change events
        /// </summary>
        /// <param name="configSection">The section that changed</param>
        public static void OnConfigurationChanged(string configSection)
        {
            try
            {
                Trace.WriteLine($"Configuration changed: {configSection}");
                
                // Auto-save configuration
                ConfigurationService.SaveConfig();
                
                // Refresh related services if needed
                switch (configSection.ToLower())
                {
                    case "general":
                        // Refresh window handles if game version changed
                        ApplicationManager.Instance.RefreshWindowHandles();
                        break;
                        
                    case "keybinds":
                        // No specific action needed, changes are automatically picked up
                        break;
                        
                    case "positions":
                        // No specific action needed
                        break;
                        
                    case "panels":
                        // UI refresh might be needed
                        break;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error handling configuration change: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles window focus change events
        /// </summary>
        /// <param name="windowHandle">The handle of the focused window</param>
        public static void OnWindowFocusChanged(IntPtr windowHandle)
        {
            try
            {
                if (WindowManagementService.IsRelatedHandle(windowHandle))
                {
                    var windowInfo = WindowManagementService.WindowHandles[windowHandle];
                    Trace.WriteLine($"Focus changed to: {windowInfo.CharacterName}");
                    
                    // Update panel selection if needed
                    PanelManagementService.SelectPanelByName(windowInfo.CharacterName);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error handling window focus change: {ex.Message}");
            }
        }
        #endregion
    }
}
