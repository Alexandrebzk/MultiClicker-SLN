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

            // If the panel was found and the image file exists, set the background image
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

            if (handle == GetForegroundWindow()) return;
            // If a panel was previously selected, remove its border
            if (selectedPanel != null)
            {
                selectedPanel.BackColor = Color.Transparent;
            }

            panel.BackColor = ColorTranslator.FromHtml("#ddfe00");
            selectedPanel = panel;
            WindowManagement.SetHandleToForeGround(handle);
        }
    }
}
