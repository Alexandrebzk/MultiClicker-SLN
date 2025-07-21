using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using static MultiClicker.WindowManagement;

namespace MultiClicker
{

    public struct POINT
    {
        public int X;
        public int Y;
    }

    public class ExtendedPanel : Panel
    {
        public string BackgroundImagePath { get; set; }
        public bool IsSelected { get; set; } // Ajout
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (IsSelected)
            {
                using (Pen pen = new Pen(Color.LimeGreen, 4))
                {
                    Rectangle rect = new Rectangle(1, 1, this.Width - 5, this.Height - 5);
                    e.Graphics.DrawRectangle(pen, rect);
                }
            }
        }
    }
    public partial class MultiClicker : Form
    {

        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);



        public static IntPtr _mouseHookID = IntPtr.Zero;
        public static IntPtr _keyboardHookID = IntPtr.Zero;
        private HookProc _mouseProc;
        private HookProc _keyboardProc;
        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }


        Panel titleBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 30,
            BackColor = ColorTranslator.FromHtml("#3a3b3b")
        };


        OpenFileDialog openFileDialog = new OpenFileDialog
        {
            Filter = "Image Files(*.BMP;*.JPG;*.GIF;*.PNG)|*.BMP;*.JPG;*.GIF;*.PNG|All files (*.*)|*.*",
            Title = "Select Image"
        };
        ContextMenuStrip contextMenu = new ContextMenuStrip();
        int totalWidth = 0;
        int totalHeight = 0;
        private bool isDragging = false;
        private Point dragStartPoint;

        private void MultiClicker_Resize(object sender, EventArgs e)
        {
            flowLayoutPanel.Width = this.ClientSize.Width;
            flowLayoutPanel.Height = this.ClientSize.Height - titleBar.Height;
        }
        public MultiClicker()
        {
            InitializeComponent();
            this.BackColor = Color.FromArgb(28, 29, 30);
            this.DoubleBuffered = true;
            this.Resize += MultiClicker_Resize;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _mouseProc = HookManagement.MouseHookCallback;
            _mouseHookID = SetHook(HookManagement.WH_MOUSE_LL, _mouseProc);
            _keyboardProc = HookManagement.KeyboardHookCallback;
            _keyboardHookID = SetHook(HookManagement.WH_KEYBOARD_LL, _keyboardProc);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            UnhookWindowsHookEx(_mouseHookID);
            UnhookWindowsHookEx(_keyboardHookID);
            base.OnFormClosing(e);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            Application.Exit();
        }

        private IntPtr SetHook(int listener, HookProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(listener, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private void TitleBar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = true;
                dragStartPoint = e.Location;
            }
        }

        private void TitleBar_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = false;
            }
        }

        private void TitleBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                Point p = PointToScreen(e.Location);
                Location = new Point(p.X - this.dragStartPoint.X, p.Y - this.dragStartPoint.Y);
            }
        }


        public static FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
        {
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoScroll = false,
            WrapContents = false,
            BackColor = Color.FromArgb(36, 37, 38)
        };

        private void Form1_Load(object sender, EventArgs e)
        {
            WindowManagement.windowHandles = WindowManagement.FindWindows("- " + ConfigManagement.config.General.GameVersion + " -");
            this.MaximizeBox = false;
            this.ShowIcon = false; 
            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            base.OnLostFocus(e);
            SetForegroundWindow(this.Handle);
            titleBar.MouseDown += TitleBar_MouseDown;
            titleBar.MouseUp += TitleBar_MouseUp;
            titleBar.MouseMove += TitleBar_MouseMove;
            flowLayoutPanel.Top = titleBar.Bottom;
            this.Controls.Add(flowLayoutPanel);
            this.Controls.Add(titleBar);

            Button closeButton = new Button
            {
                Text = "X",
                Dock = DockStyle.Right,
                Width = 30,
                Height = 30,
                BackColor = Color.Red,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 }
            };

            Button helpButton = new Button
            {
                Text = "?",
                Dock = DockStyle.Left,
                Width = 30,
                Height = 30,
                BackColor = Color.Violet,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 }
            };
            Dictionary<string, string> keyBinds = new Dictionary<string, string>
            {
                { "CLICK with Delay", ConfigManagement.config.Keybinds[TRIGGERS.SIMPLE_CLICK].ToString() },
                { "Click without Delay", ConfigManagement.config.Keybinds[TRIGGERS.SIMPLE_CLICK_NO_DELAY].ToString() },
                { "Double Click", ConfigManagement.config.Keybinds[TRIGGERS.DOUBLE_CLICK].ToString() },
                { "Next character", ConfigManagement.config.Keybinds[TRIGGERS.SELECT_NEXT].ToString() },
                { "Previous Character", ConfigManagement.config.Keybinds[TRIGGERS.SELECT_PREVIOUS].ToString() },
                { "Havenbag (Dofus --> h)", ConfigManagement.config.Keybinds[TRIGGERS.HAVENBAG].ToString() },
                { "INVITE GROUP", ConfigManagement.config.Keybinds[TRIGGERS.GROUP_INVITE].ToString() },
                { "Input chat commands (Dofus Open discussion --> Tab)", ConfigManagement.config.Keybinds[TRIGGERS.TRAVEL].ToString() },
                { "Auto complete HDV (see position config) -->", "² + MRbutton" },
                { "Positions' config -->", ConfigManagement.config.Keybinds[TRIGGERS.OPTIONS].ToString() },
            };

            ToolTip toolTip = new ToolTip();
            toolTip.InitialDelay = 10;
            toolTip.ReshowDelay = 500;
            toolTip.ShowAlways = true;

            StringBuilder toolTipText = new StringBuilder();
            toolTipText.AppendLine("FUNCTION ----> Bind\n");
            foreach (var keyBind in keyBinds)
            {
                toolTipText.AppendLine($"{keyBind.Key}----->{keyBind.Value}");
            }
            toolTipText.AppendLine($"Ctrl + Alt + V -----> paste on all windows");
            toolTip.SetToolTip(helpButton, toolTipText.ToString());


            closeButton.Click += (s, ev) => this.Close();


            titleBar.Controls.Add(closeButton);
            titleBar.Controls.Add(helpButton);
            generateUI();
        }

        public void generateUI()
        {
            totalHeight = 0;
            totalWidth = 0;
            contextMenu.Items.Clear();
            ToolStripMenuItem moveUpItem = new ToolStripMenuItem("Move Up");
            moveUpItem.Click += MoveUpItem_Click;
            contextMenu.Items.Add(moveUpItem);

            ToolStripMenuItem moveDownItem = new ToolStripMenuItem("Move Down");
            moveDownItem.Click += MoveDownItem_Click;
            contextMenu.Items.Add(moveDownItem);
            ToolStripMenuItem changeImageItem = new ToolStripMenuItem("Change Image");
            changeImageItem.Click += ChangeImagePanel_Click;
            contextMenu.Items.Add(changeImageItem);
            HookManagement.ShouldOpenMenuTravel += () =>
            {
                this.Invoke((MethodInvoker)delegate
                {
                    using (ReplicateTextForm inputForm = new ReplicateTextForm())
                    {
                        if (inputForm.ShowDialog() == DialogResult.OK)
                        {
                            string inputText = inputForm.InputText;
                            WindowManagement.sentTextToHandles(inputText, windowHandles.ToList());
                        }
                    }
                });
            };
            HookManagement.ShouldOpenKeyBindForm += () =>
            {
                this.Invoke((MethodInvoker)delegate
                {
                    using (KeyBindForm inputForm = new KeyBindForm())
                    {

                        if (inputForm.ShowDialog() == DialogResult.OK)
                        {
                        }
                    }
                });
            };

            var imageCache = new Dictionary<string, Image>();
            string defaultImagePath = @"cosmetics\default.png";
            imageCache[defaultImagePath] = Image.FromFile(defaultImagePath);

            foreach (var panelCfg in ConfigManagement.config.Panels)
            {
                string imgPath = panelCfg.Value.Background ?? defaultImagePath;
                if (!imageCache.ContainsKey(imgPath))
                {
                    imageCache[imgPath] = File.Exists(imgPath) ? Image.FromFile(imgPath) : imageCache[defaultImagePath];
                }
            }

            flowLayoutPanel.SuspendLayout();
            flowLayoutPanel.Controls.Clear();

            foreach (var panelEntry in ConfigManagement.config.Panels)
            {
                string panelName = panelEntry.Key;
                PanelConfig panelConfig = panelEntry.Value;
                string imgPath = panelConfig.Background ?? defaultImagePath;

                var handleEntry = WindowManagement.windowHandles.FirstOrDefault(e => e.Value.CharacterName == panelName);
                if (handleEntry.Key == IntPtr.Zero)
                    continue;

                Image panelImage = imageCache[imgPath];
                Color avgColor = GetAverageColor(panelImage);

                ExtendedPanel panel = new ExtendedPanel
                {
                    ContextMenuStrip = contextMenu,
                    Size = new Size(70, 70),
                    Margin = new Padding(5, 5, 5, 5),
                    BackgroundImage = panelImage,
                    BackgroundImagePath = imgPath,
                    BackgroundImageLayout = ImageLayout.Center,
                    Tag = handleEntry.Key,
                    Name = panelName,
                    BackColor = avgColor,
                    BorderStyle = BorderStyle.FixedSingle,
                    Cursor = Cursors.Hand
                };
                handleEntry.Value.relatedPanel = panel;

                Label label = new Label
                {
                    Text = panelName,
                    AutoSize = false,
                    Dock = DockStyle.Bottom,
                    ForeColor = PanelManagement.IsColorDark(panel.BackColor) ? Color.White : Color.Black,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                    BackColor = Color.Transparent,
                    Height = 22
                };

                panel.Controls.Add(label);
                panel.Click += PanelManagement.Panel_Click;
                flowLayoutPanel.Controls.Add(panel);
            }

            flowLayoutPanel.ResumeLayout();
            flowLayoutPanel.AutoSize = true;
            flowLayoutPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            Size panelSize = flowLayoutPanel.PreferredSize;
            int width = panelSize.Width;
            int height = panelSize.Height + titleBar.Height;

            this.ClientSize = new Size(width, height);

            // (Optionnel) repositionne la fenêtre si besoin
            this.Location = new Point(
                Screen.PrimaryScreen.WorkingArea.Width - this.ClientSize.Width - (Screen.PrimaryScreen.WorkingArea.Width / 20),
                0 + titleBar.Height + (Screen.PrimaryScreen.WorkingArea.Height / 10)
            );
            if (flowLayoutPanel.Controls.Count > 0)
                PanelManagement.Panel_Click(flowLayoutPanel.Controls[0], EventArgs.Empty);

            string updatedConfigJson = JsonConvert.SerializeObject(ConfigManagement.config, Formatting.Indented);
            File.WriteAllText("config.json", updatedConfigJson);
        }
        private Color GetAverageColor(Image image)
        {
            if (image == null) return Color.FromArgb(44, 47, 51);
            using (var bmp = new Bitmap(image))
            {
                long r = 0, g = 0, b = 0;
                int pixelCount = bmp.Width * bmp.Height;
                for (int x = 0; x < bmp.Width; x++)
                {
                    for (int y = 0; y < bmp.Height; y++)
                    {
                        Color pixel = bmp.GetPixel(x, y);
                        r += pixel.R;
                        g += pixel.G;
                        b += pixel.B;
                    }
                }
                if (pixelCount == 0) return Color.FromArgb(44, 47, 51);
                return Color.FromArgb((int)(r / pixelCount), (int)(g / pixelCount), (int)(b / pixelCount));
            }
        }

        private void ChangeImagePanel_Click(object sender, EventArgs e)
        {
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                ExtendedPanel panel = (ExtendedPanel)contextMenu.SourceControl;
                panel.BackgroundImage = Image.FromFile(openFileDialog.FileName);
                ConfigManagement.config.Panels[panel.Name].Background = openFileDialog.FileName;
                ConfigManagement.SaveConfig();
            }
        }
        private void MoveUpItem_Click(object sender, EventArgs e)
        {
            ExtendedPanel panel = (ExtendedPanel)contextMenu.SourceControl;
            int index = flowLayoutPanel.Controls.IndexOf(panel);
            if (index > 0)
            {

                ExtendedPanel previousPanel = (ExtendedPanel)flowLayoutPanel.Controls[index - 1];
                flowLayoutPanel.Controls.SetChildIndex(panel, index - 1);
                flowLayoutPanel.Controls.SetChildIndex(previousPanel, index);


                ConfigManagement.config.Panels.Clear();
                foreach (ExtendedPanel p in flowLayoutPanel.Controls.Cast<ExtendedPanel>())
                {
                    ConfigManagement.config.Panels[p.Name] = new PanelConfig { Background = p.BackgroundImagePath };
                }
                ConfigManagement.SaveConfig();
            }
        }

        private void MoveDownItem_Click(object sender, EventArgs e)
        {
            ExtendedPanel panel = (ExtendedPanel)contextMenu.SourceControl;
            int index = flowLayoutPanel.Controls.IndexOf(panel);
            if (index < flowLayoutPanel.Controls.Count - 1)
            {

                ExtendedPanel nextPanel = (ExtendedPanel)flowLayoutPanel.Controls[index + 1];
                flowLayoutPanel.Controls.SetChildIndex(panel, index + 1);
                flowLayoutPanel.Controls.SetChildIndex(nextPanel, index);


                ConfigManagement.config.Panels.Clear();
                foreach (ExtendedPanel p in flowLayoutPanel.Controls.Cast<ExtendedPanel>())
                {
                    ConfigManagement.config.Panels[p.Name] = new PanelConfig { Background = p.BackgroundImagePath };
                }
                ConfigManagement.SaveConfig();
            }
        }

    }
}