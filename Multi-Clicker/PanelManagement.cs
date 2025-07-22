using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static MultiClicker.MultiClicker;

namespace MultiClicker
{
    public static class PanelManagement
    {

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        public static ExtendedPanel selectedPanel = null;
        public static void SelectNextPanel()
        {
            int index = MultiClicker.flowLayoutPanel.Controls.IndexOf(selectedPanel);
            if (index <= MultiClicker.flowLayoutPanel.Controls.Count - 1)
            {
                if (index == MultiClicker.flowLayoutPanel.Controls.Count - 1)
                {
                    Panel_Click(MultiClicker.flowLayoutPanel.Controls[0], EventArgs.Empty);
                }
                else
                {
                    Panel_Click(MultiClicker.flowLayoutPanel.Controls[index + 1], EventArgs.Empty);
                }
            }
        }

        public static void SelectPreviousPanel()
        {
            int index = MultiClicker.flowLayoutPanel.Controls.IndexOf(selectedPanel);
            if (index <= MultiClicker.flowLayoutPanel.Controls.Count - 1)
            {
                if (index == 0)
                {
                    Panel_Click(MultiClicker.flowLayoutPanel.Controls[MultiClicker.flowLayoutPanel.Controls.Count - 1], EventArgs.Empty);
                }
                else
                {
                    Panel_Click(MultiClicker.flowLayoutPanel.Controls[index - 1], EventArgs.Empty);
                }
            }
        }

        public static void SetPanelBackground(ExtendedPanel panel, string imagePath)
        {

            if (panel != null && File.Exists(imagePath))
            {
                panel.BackgroundImage = Image.FromFile(imagePath);
                panel.BackgroundImagePath = imagePath;
            }
            else
            {
                //ignore
            }
        }

        public static void Panel_Click(object sender, EventArgs e)
        {
            ExtendedPanel panel = (ExtendedPanel)sender;
            IntPtr handle = (IntPtr)panel.Tag;

            if (selectedPanel != null)
            {
                selectedPanel.IsSelected = false;
                selectedPanel.Invalidate();
                // Restore the background color
                selectedPanel.BackColor = Color.Transparent;
            }

            panel.IsSelected = true;
            panel.Invalidate();
            panel.BackColor = ColorTranslator.FromHtml("#ddfe00");
            selectedPanel = panel;

            if (handle == GetForegroundWindow()) return;
            WindowManagement.SetHandleToForeGround(handle);
        }

        public static void Panel_Select(String panelName)
        {

            ExtendedPanel panel = flowLayoutPanel.Controls.OfType<ExtendedPanel>().FirstOrDefault(p => p.Name == panelName);
            IntPtr handle = (IntPtr)panel.Tag;

            if (handle == GetForegroundWindow()) return;
            if (selectedPanel != null)
            {
                selectedPanel.BackColor = Color.Transparent;
            }

            panel.BackColor = ColorTranslator.FromHtml("#ddfe00");
            selectedPanel = panel;
            WindowManagement.SetHandleToForeGround(handle);
        }

        // Add this utility method in PanelManagement
        public static bool IsColorDark(Color color)
        {
            double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
            return luminance < 128;
        }
    }
}
