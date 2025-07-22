using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MultiClicker
{
    public partial class PositionOverlayForm : Form
    {
        public Dictionary<TRIGGERS_POSITIONS, Position> Positions { get; set; }
        public Dictionary<TRIGGERS_POSITIONS, Color> PositionColors { get; set; }
        public PositionOverlayForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.Manual;
            this.Bounds = Screen.PrimaryScreen.Bounds;
            this.BackColor = Color.Magenta; // A color that is unlikely to be used
            this.TransparencyKey = this.BackColor; // Makes the magenta color transparent
            this.ShowInTaskbar = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Positions == null || PositionColors == null) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            foreach (var trigger in Positions.Keys)
            {
                if (!PositionColors.TryGetValue(trigger, out Color color))
                    continue;

                Position position = Positions[trigger];
                using (Pen pen = new Pen(color, 3)) // Adjust thickness as needed
                {
                    Rectangle rect = new Rectangle(position.X, position.Y, position.Width, position.Height);
                    g.DrawRectangle(pen, rect);
                }
            }
        }

        public void RefreshOverlay()
        {
            this.Invalidate(); // Causes the form to be redrawn
        }
    }
}
