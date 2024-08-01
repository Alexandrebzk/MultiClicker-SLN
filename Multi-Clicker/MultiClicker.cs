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

        public MultiClicker()
        {
            InitializeComponent();
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
            WrapContents = false
        };

        private void Form1_Load(object sender, EventArgs e)
        {
            WindowManagement.windowHandles = WindowManagement.FindWindows("- Dofus");
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
            StartCheckingForegroundWindowForText();
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

            foreach (KeyValuePair<IntPtr, WindowInfo> entry in WindowManagement.windowHandles)
            {

                ExtendedPanel panel = new ExtendedPanel
                {
                    ContextMenuStrip = contextMenu,
                    Size = new Size(75, 75),
                    Margin = new Padding(0),
                    BackgroundImage = Image.FromFile(@"cosmetics\default.png"),
                    BackgroundImagePath = @"cosmetics\default.png",
                    BackgroundImageLayout = ImageLayout.Center,
                    Tag = entry.Key,
                    Name = entry.Value.CharacterName
                };

                Label label = new Label
                {
                    Text = entry.Value.CharacterName,
                    AutoSize = false,
                    Dock = DockStyle.Bottom,
                    ForeColor = Color.White,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Arial", 8.25F, FontStyle.Bold),
                    BackColor = Color.Transparent
                };

                panel.Controls.Add(label);
                panel.Click += PanelManagement.Panel_Click;

                totalWidth = panel.Right;
                totalHeight += panel.Bottom;
                flowLayoutPanel.Controls.Add(panel);


                if (!ConfigManagement.config.Panels.ContainsKey(panel.Name))
                {
                    ConfigManagement.config.Panels[panel.Name] = new PanelConfig { Background = @"cosmetics\default.png" };
                }
            }
            RearrangePanels();
            totalHeight += titleBar.Height;
            this.ClientSize = new Size(totalWidth, totalHeight);
            this.Location = new Point(Screen.PrimaryScreen.WorkingArea.Width - totalWidth - (Screen.PrimaryScreen.WorkingArea.Width/20), 0 + totalHeight + (Screen.PrimaryScreen.WorkingArea.Height / 10));
            PanelManagement.Panel_Click(flowLayoutPanel.Controls[0], EventArgs.Empty);


            string updatedConfigJson = JsonConvert.SerializeObject(ConfigManagement.config, Formatting.Indented);


            File.WriteAllText("config.json", updatedConfigJson);
        }

        private void RearrangePanels()
        {

            List<ExtendedPanel> panels = new List<ExtendedPanel>();


            foreach (var panel in ConfigManagement.config.Panels)
            {
                ExtendedPanel extendedPanel = flowLayoutPanel.Controls.Find(panel.Key, true).FirstOrDefault() as ExtendedPanel;
                if (extendedPanel != null)
                {
                    PanelManagement.SetPanelBackground(extendedPanel, panel.Value.Background);
                    panels.Add(extendedPanel);
                }
            }


            flowLayoutPanel.Controls.Clear();


            foreach (var panel in panels)
            {
                flowLayoutPanel.Controls.Add(panel);
            }

            Dictionary<IntPtr, WindowInfo> newWindowHandles = new Dictionary<IntPtr, WindowInfo>();


            foreach (ExtendedPanel panel in flowLayoutPanel.Controls)
            {
                IntPtr handle = (IntPtr)panel.Tag;
                if (WindowManagement.windowHandles.ContainsKey(handle))
                {
                    newWindowHandles[handle] = WindowManagement.windowHandles[handle];
                }
            }


            WindowManagement.windowHandles = newWindowHandles;
            PanelManagement.Panel_Click(flowLayoutPanel.Controls[0], EventArgs.Empty);
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