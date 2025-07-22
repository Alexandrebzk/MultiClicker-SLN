using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MultiClicker.Models;
using MultiClicker.Services;
using MultiClicker.UI;

namespace MultiClicker.Services
{
    /// <summary>
    /// Service responsible for managing UI panels and their selection
    /// </summary>
    public static class PanelManagementService
    {
        #region Win32 API Declarations
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        #endregion

        #region Private Fields
        private static ExtendedPanel _selectedPanel;
        private static FlowLayoutPanel _flowLayoutPanel;
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets or sets the currently selected panel
        /// </summary>
        public static ExtendedPanel SelectedPanel
        {
            get => _selectedPanel;
            private set => _selectedPanel = value;
        }

        /// <summary>
        /// Gets or sets the flow layout panel containing all panels
        /// </summary>
        public static FlowLayoutPanel FlowLayoutPanel
        {
            get => _flowLayoutPanel;
            set => _flowLayoutPanel = value;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Selects the next panel in the sequence - exact legacy behavior
        /// </summary>
        public static void SelectNextPanel()
        {
            try
            {
                if (_flowLayoutPanel?.Controls == null || _flowLayoutPanel.Controls.Count == 0)
                    return;

                var index = _flowLayoutPanel.Controls.IndexOf(_selectedPanel);
                
                // Legacy behavior: if index is -1 (no selection) or at the end, go to first panel
                if (index == -1 || index == _flowLayoutPanel.Controls.Count - 1)
                {
                    HandlePanelClick(_flowLayoutPanel.Controls[0], EventArgs.Empty);
                }
                else
                {
                    // Go to next panel
                    HandlePanelClick(_flowLayoutPanel.Controls[index + 1], EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error selecting next panel: {ex.Message}");
            }
        }

        /// <summary>
        /// Selects the previous panel in the sequence - exact legacy behavior
        /// </summary>
        public static void SelectPreviousPanel()
        {
            try
            {
                if (_flowLayoutPanel?.Controls == null || _flowLayoutPanel.Controls.Count == 0)
                    return;

                var index = _flowLayoutPanel.Controls.IndexOf(_selectedPanel);
                
                // Legacy behavior: if index is -1 (no selection) or at the beginning, go to last panel
                if (index == -1 || index == 0)
                {
                    HandlePanelClick(_flowLayoutPanel.Controls[_flowLayoutPanel.Controls.Count - 1], EventArgs.Empty);
                }
                else
                {
                    // Go to previous panel
                    HandlePanelClick(_flowLayoutPanel.Controls[index - 1], EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error selecting previous panel: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the background image for a panel
        /// </summary>
        /// <param name="panel">The panel to update</param>
        /// <param name="imagePath">The path to the image file</param>
        public static void SetPanelBackground(object panel, string imagePath)
        {
            try
            {
                if (panel is Panel panelControl && File.Exists(imagePath))
                {
                    panelControl.BackgroundImage = Image.FromFile(imagePath);
                    
                    // If it's an ExtendedPanel, set the BackgroundImagePath property
                    if (panel.GetType().GetProperty("BackgroundImagePath") != null)
                    {
                        panel.GetType().GetProperty("BackgroundImagePath").SetValue(panel, imagePath);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error setting panel background: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles panel click events
        /// </summary>
        /// <param name="sender">The panel that was clicked</param>
        /// <param name="e">Event arguments</param>
        public static void HandlePanelClick(object sender, EventArgs e)
        {
            try
            {
                var panel = sender as Control;
                if (panel?.Tag is IntPtr handle)
                {
                    SelectPanel(panel);

                    if (handle == GetForegroundWindow())
                        return;

                    WindowManagementService.SetHandleToForeground(handle);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error handling panel click: {ex.Message}");
            }
        }

        /// <summary>
        /// Selects a panel by its name
        /// </summary>
        /// <param name="panelName">The name of the panel to select</param>
        public static void SelectPanelByName(string panelName)
        {
            try
            {
                if (_flowLayoutPanel?.Controls == null)
                    return;

                var panel = _flowLayoutPanel.Controls
                    .Cast<Control>()
                    .FirstOrDefault(p => p.Name == panelName);

                if (panel?.Tag is IntPtr handle)
                {
                    if (handle == GetForegroundWindow())
                        return;

                    SelectPanel(panel);
                    WindowManagementService.SetHandleToForeground(handle);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error selecting panel by name: {ex.Message}");
            }
        }

        /// <summary>
        /// Determines if a color is considered dark
        /// </summary>
        /// <param name="color">The color to check</param>
        /// <returns>True if the color is dark, false otherwise</returns>
        public static bool IsColorDark(Color color)
        {
            var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
            return luminance < 128;
        }

        /// <summary>
        /// Calculates the average color of an image
        /// </summary>
        /// <param name="image">The image to analyze</param>
        /// <returns>The average color</returns>
        public static Color GetAverageColor(Image image)
        {
            if (image == null) 
                return Color.FromArgb(44, 47, 51);

            try
            {
                using (var bmp = new Bitmap(image))
                {
                    long r = 0, g = 0, b = 0;
                    var pixelCount = bmp.Width * bmp.Height;

                    for (int x = 0; x < bmp.Width; x++)
                    {
                        for (int y = 0; y < bmp.Height; y++)
                        {
                            var pixel = bmp.GetPixel(x, y);
                            r += pixel.R;
                            g += pixel.G;
                            b += pixel.B;
                        }
                    }

                    if (pixelCount == 0) 
                        return Color.FromArgb(44, 47, 51);

                    return Color.FromArgb(
                        (int)(r / pixelCount), 
                        (int)(g / pixelCount), 
                        (int)(b / pixelCount));
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error calculating average color: {ex.Message}");
                return Color.FromArgb(44, 47, 51);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Selects the specified panel and updates the UI - exact legacy behavior
        /// </summary>
        /// <param name="panel">The panel to select</param>
        private static void SelectPanel(Control panel)
        {
            try
            {
                if (!(panel is ExtendedPanel extendedPanel))
                    return;

                var handle = (IntPtr)extendedPanel.Tag;

                // Deselect the previously selected panel (legacy behavior)
                if (_selectedPanel != null)
                {
                    UpdatePanelSelection(_selectedPanel, false);
                }

                // Select the new panel (legacy behavior)
                UpdatePanelSelection(extendedPanel, true);
                _selectedPanel = extendedPanel;

                // Check if the window is already in foreground AFTER selecting the panel (legacy behavior)
                if (handle == GetForegroundWindow()) 
                    return;

                // Set the corresponding window to foreground (legacy behavior)
                WindowManagementService.SetHandleToForeground(handle);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error selecting panel: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the visual selection state of a panel
        /// </summary>
        /// <param name="panel">The panel to update</param>
        /// <param name="isSelected">Whether the panel should be selected</param>
        private static void UpdatePanelSelection(Control panel, bool isSelected)
        {
            try
            {
                // Check if panel has IsSelected property (ExtendedPanel)
                var isSelectedProperty = panel.GetType().GetProperty("IsSelected");
                if (isSelectedProperty != null)
                {
                    isSelectedProperty.SetValue(panel, isSelected);
                }

                // Update background color
                panel.BackColor = isSelected 
                    ? ColorTranslator.FromHtml("#ddfe00") 
                    : Color.Transparent;

                // Trigger panel redraw
                panel.Invalidate();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error updating panel selection: {ex.Message}");
            }
        }
        #endregion
    }
}
