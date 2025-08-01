using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using MultiClicker.Core;
using MultiClicker.Models;
using MultiClicker.Services;
using MultiClicker.Properties;

namespace MultiClicker.UI
{
    /// <summary>
    /// Enhanced panel class with selection visualization
    /// </summary>
    public class ExtendedPanel : Panel
    {
        public string BackgroundImagePath { get; set; }
        public bool IsSelected { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (IsSelected)
            {
                using (var pen = new Pen(Color.LimeGreen, 4))
                {
                    var rect = new Rectangle(1, 1, Width - 5, Height - 5);
                    e.Graphics.DrawRectangle(pen, rect);
                }
            }
        }
    }

    /// <summary>
    /// Main application form with improved architecture
    /// </summary>
    public partial class MultiClickerForm : Form
    {
        #region Private Fields
        private readonly Panel _titleBar;
        private readonly FlowLayoutPanel _flowLayoutPanel;
        private readonly OpenFileDialog _openFileDialog;
        private readonly ContextMenuStrip _contextMenu;
        private readonly ContextMenuStrip _titleBarMenu;
        private bool _isDragging;
        private Point _dragStartPoint;
        private readonly Dictionary<string, Image> _imageCache;
        #endregion

        #region Constructor
        public MultiClickerForm()
        {
            InitializeComponent();
            
            _imageCache = new Dictionary<string, Image>();
            _titleBar = CreateTitleBar();
            _titleBarMenu = CreateTitleBarMenu();
            _flowLayoutPanel = CreateFlowLayoutPanel();
            _openFileDialog = CreateOpenFileDialog();
            _contextMenu = CreateContextMenu();
            
            InitializeForm();
            SetupEventHandlers();
        }
        #endregion

        #region Form Events
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            InitializeHooks();
            GenerateUI();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            CleanupHooks();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            Application.Exit();
        }

        private void OnResize(object sender, EventArgs e)
        {
            UpdateLayout();
        }
        #endregion

        #region Initialization Methods
        private void InitializeForm()
        {
            BackColor = Color.FromArgb(28, 29, 30);
            DoubleBuffered = true;
            MaximizeBox = false;
            ShowIcon = false;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;

            Controls.Add(_flowLayoutPanel);
            Controls.Add(_titleBar);
        }

        private Panel CreateTitleBar()
        {
            var titleBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 30,
                BackColor = ColorTranslator.FromHtml("#3a3b3b")
            };

            // Close button (on the right)
            var closeButton = new Button
            {
                Text = "✕",
                Dock = DockStyle.Right,
                Width = 40,
                Height = 30,
                BackColor = Color.FromArgb(232, 17, 35),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 },
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 50, 50);
            closeButton.Click += (s, e) => Close();

            // Menu button (on the left)
            var menuButton = new Button
            {
                Text = "☰",
                Dock = DockStyle.Left,
                Width = 40,
                Height = 30,
                BackColor = Color.FromArgb(70, 130, 180),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 },
                Font = new Font("Segoe UI", 12F, FontStyle.Bold)
            };
            menuButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(100, 150, 200);
            menuButton.Click += MenuButton_Click;

            // Application title in the center
            var titleLabel = new Label
            {
                Text = "MultiClicker",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                BackColor = Color.Transparent
            };

            titleBar.Controls.Add(titleLabel);
            titleBar.Controls.Add(closeButton);
            titleBar.Controls.Add(menuButton);

            return titleBar;
        }

        private ContextMenuStrip CreateTitleBarMenu()
        {
            var menu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                ShowImageMargin = true,
                Renderer = new CustomMenuRenderer()
            };

            // Keybinds menu item
            var keybindsItem = new ToolStripMenuItem
            {
                Text = Strings.Keybinds,
                Image = CreateMenuIcon("⌨", Color.DarkBlue),
                ForeColor = Color.White
            };
            keybindsItem.Click += (s, e) => OpenKeybindsConfiguration();

            // Language menu item
            var languageItem = new ToolStripMenuItem
            {
                Text = Strings.Language,
                Image = CreateMenuIcon("🌐", Color.DarkGreen),
                ForeColor = Color.White
            };
            languageItem.Click += (s, e) => ShowLanguageSelection();

            menu.Items.AddRange(new ToolStripItem[] { keybindsItem, languageItem });

            return menu;
        }

        private void MenuButton_Click(object sender, EventArgs e)
        {
            var button = sender as Button;
            if (button != null)
            {
                _titleBarMenu.Show(button, new Point(0, button.Height));
            }
        }

        private Bitmap CreateMenuIcon(string text, Color color)
        {
            var bitmap = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                using (var brush = new SolidBrush(color))
                using (var font = new Font("Segoe UI", 10F, FontStyle.Bold))
                {
                    var format = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    g.DrawString(text, font, brush, new RectangleF(0, 0, 16, 16), format);
                }
            }
            return bitmap;
        }

        private void ShowLanguageSelection()
        {
            if (LocalizationService.ShowLanguageSelectionDialog(this))
            {
                MessageBox.Show(Strings.LanguageChanged, Strings.Language, 
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // Custom renderer for modern dark menu styling
        private class CustomMenuRenderer : ToolStripProfessionalRenderer
        {
            public CustomMenuRenderer() : base(new CustomColorTable()) { }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (e.Item.Selected)
                {
                    var rect = new Rectangle(Point.Empty, e.Item.Size);
                    using (var brush = new SolidBrush(Color.FromArgb(70, 130, 180)))
                    {
                        e.Graphics.FillRectangle(brush, rect);
                    }
                }
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = Color.White;
                base.OnRenderItemText(e);
            }
        }

        private class CustomColorTable : ProfessionalColorTable
        {
            public override Color MenuBorder => Color.FromArgb(60, 60, 60);
            public override Color MenuItemBorder => Color.FromArgb(70, 130, 180);
            public override Color MenuItemSelected => Color.FromArgb(70, 130, 180);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(70, 130, 180);
            public override Color MenuItemSelectedGradientEnd => Color.FromArgb(70, 130, 180);
        }

        private void OpenKeybindsConfiguration()
        {
            try
            {
                using (var keybindsForm = new KeybindsConfigForm())
                {
                    keybindsForm.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Strings.ErrorOpeningKeybinds, ex.Message), 
                    Strings.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private FlowLayoutPanel CreateFlowLayoutPanel()
        {
            var panel = new FlowLayoutPanel
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

            PanelManagementService.FlowLayoutPanel = panel;
            return panel;
        }

        private OpenFileDialog CreateOpenFileDialog()
        {
            return new OpenFileDialog
            {
                Filter = $"{Strings.ImageFiles}(*.BMP;*.JPG;*.GIF;*.PNG)|*.BMP;*.JPG;*.GIF;*.PNG|{Strings.AllFiles}|*.*",
                Title = Strings.SelectBackgroundImage
            };
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var contextMenu = new ContextMenuStrip();

            var moveUpItem = new ToolStripMenuItem(Strings.MoveUp);
            moveUpItem.Click += MoveUpItem_Click;
            contextMenu.Items.Add(moveUpItem);

            var moveDownItem = new ToolStripMenuItem(Strings.MoveDown);
            moveDownItem.Click += MoveDownItem_Click;
            contextMenu.Items.Add(moveDownItem);

            var changeImageItem = new ToolStripMenuItem(Strings.ChangeBackground);
            changeImageItem.Click += ChangeImagePanel_Click;
            contextMenu.Items.Add(changeImageItem);

            return contextMenu;
        }
        #endregion

        #region Event Handlers Setup
        private void SetupEventHandlers()
        {
            Resize += OnResize;
            _titleBar.MouseDown += TitleBar_MouseDown;
            _titleBar.MouseUp += TitleBar_MouseUp;
            _titleBar.MouseMove += TitleBar_MouseMove;

            HookManagementService.ShouldOpenMenuTravel += HandleTravelMenuRequest;
            HookManagementService.ShouldOpenPositionConfiguration += HandleKeyBindFormRequest;
        }

        private void InitializeHooks()
        {
            // The hooks are now managed by the HookManagementService
            // which is initialized by the ApplicationManager
        }

        private void CleanupHooks()
        {
            // Cleanup is handled by the ApplicationManager
        }
        #endregion

        #region UI Generation
        public void GenerateUI()
        {
            try
            {
                LoadImageCache();
                EnsurePanelConfigsExist();
                CreatePanels();
                UpdateFormSize();
                SelectFirstPanel();
                ConfigurationService.SaveConfig();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Error generating UI: {ex.Message}");
            }
        }

        private void LoadImageCache()
        {
            _imageCache.Clear();
            const string defaultImagePath = @"cosmetics\default.png";
            
            if (System.IO.File.Exists(defaultImagePath))
            {
                _imageCache[defaultImagePath] = Image.FromFile(defaultImagePath);
            }

            foreach (var panelConfig in ConfigurationService.Current.Panels.Values)
            {
                var imgPath = panelConfig.Background ?? defaultImagePath;
                if (!_imageCache.ContainsKey(imgPath) && System.IO.File.Exists(imgPath))
                {
                    _imageCache[imgPath] = Image.FromFile(imgPath);
                }
            }
        }

        private void EnsurePanelConfigsExist()
        {
            const string defaultImagePath = @"cosmetics\default.png";
            
            foreach (var windowEntry in WindowManagementService.WindowHandles)
            {
                var characterName = windowEntry.Value.CharacterName;
                if (!ConfigurationService.Current.Panels.ContainsKey(characterName))
                {
                    ConfigurationService.Current.Panels[characterName] = new PanelConfig { Background = defaultImagePath };
                }
            }
        }

        private void CreatePanels()
        {
            _flowLayoutPanel.SuspendLayout();
            _flowLayoutPanel.Controls.Clear();

            foreach (var panelEntry in ConfigurationService.Current.Panels)
            {
                var panel = CreatePanel(panelEntry.Key, panelEntry.Value);
                if (panel != null)
                {
                    _flowLayoutPanel.Controls.Add(panel);
                }
            }

            _flowLayoutPanel.ResumeLayout();
        }

        private ExtendedPanel CreatePanel(string panelName, PanelConfig panelConfig)
        {
            var windowEntry = WindowManagementService.WindowHandles
                .FirstOrDefault(e => e.Value.CharacterName == panelName);

            if (windowEntry.Key == IntPtr.Zero)
                return null;

            const string defaultImagePath = @"cosmetics\default.png";
            var imgPath = panelConfig.Background ?? defaultImagePath;
            var panelImage = _imageCache.ContainsKey(imgPath) 
                ? _imageCache[imgPath] 
                : (_imageCache.ContainsKey(defaultImagePath) ? _imageCache[defaultImagePath] : null);

            var avgColor = PanelManagementService.GetAverageColor(panelImage);

            var panel = new ExtendedPanel
            {
                ContextMenuStrip = _contextMenu,
                Size = new Size(70, 70),
                Margin = new Padding(5, 5, 5, 5),
                BackgroundImage = panelImage,
                BackgroundImagePath = imgPath,
                BackgroundImageLayout = ImageLayout.Center,
                Tag = windowEntry.Key,
                Name = panelName,
                BackColor = avgColor,
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };

            windowEntry.Value.RelatedPanel = panel;

            var label = new Label
            {
                Text = panelName,
                AutoSize = false,
                Dock = DockStyle.Bottom,
                ForeColor = PanelManagementService.IsColorDark(panel.BackColor) ? Color.White : Color.Black,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                BackColor = Color.Transparent,
                Height = 22
            };

            panel.Controls.Add(label);
            panel.Click += PanelManagementService.HandlePanelClick;

            return panel;
        }

        private void UpdateFormSize()
        {
            _flowLayoutPanel.AutoSize = true;
            _flowLayoutPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var panelSize = _flowLayoutPanel.PreferredSize;
            var width = panelSize.Width;
            var height = panelSize.Height + _titleBar.Height;

            ClientSize = new Size(width, height);

            // Position window
            Location = new Point(
                Screen.PrimaryScreen.WorkingArea.Width - ClientSize.Width - (Screen.PrimaryScreen.WorkingArea.Width / 20),
                0 + _titleBar.Height + (Screen.PrimaryScreen.WorkingArea.Height / 10)
            );
        }

        private void SelectFirstPanel()
        {
            if (_flowLayoutPanel.Controls.Count > 0)
            {
                PanelManagementService.HandlePanelClick(_flowLayoutPanel.Controls[0], EventArgs.Empty);
            }
        }
        #endregion

        #region Event Handlers
        private void TitleBar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                _dragStartPoint = e.Location;
            }
        }

        private void TitleBar_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = false;
            }
        }

        private void TitleBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                var p = PointToScreen(e.Location);
                Location = new Point(p.X - _dragStartPoint.X, p.Y - _dragStartPoint.Y);
            }
        }

        private void UpdateLayout()
        {
            _flowLayoutPanel.Width = ClientSize.Width;
            _flowLayoutPanel.Height = ClientSize.Height - _titleBar.Height;
        }

        private void HandleTravelMenuRequest()
        {
            Thread.Sleep(100);
            Invoke((MethodInvoker)delegate
            {
                using (var inputForm = new ReplicateTextForm())
                {
                    if (inputForm.ShowDialog() == DialogResult.OK)
                    {
                        var inputText = inputForm.InputText;
                        var windowList = WindowManagementService.WindowHandles.ToList();
                        WindowManagementService.SendTextToWindows(inputText, windowList);
                        if (inputText.Contains("/travel"))
                        {
                            foreach (var windowEntry in windowList)
                            {
                                WindowManagementService.SimulateKeyPress(windowEntry.Key, Keys.Enter);
                            }
                        }
                    }
                }
            });
        }

        private void HandleKeyBindFormRequest()
        {
            Invoke((MethodInvoker)delegate
            {
                using (var inputForm = new PositionConfigurationForm())
                {
                    inputForm.ShowDialog();
                }
            });
        }

        private void ChangeImagePanel_Click(object sender, EventArgs e)
        {
            if (_openFileDialog.ShowDialog() == DialogResult.OK)
            {
                var panel = (ExtendedPanel)_contextMenu.SourceControl;
                panel.BackgroundImage = Image.FromFile(_openFileDialog.FileName);
                ConfigurationService.Current.Panels[panel.Name].Background = _openFileDialog.FileName;
                ConfigurationService.SaveConfig();
            }
        }

        private void MoveUpItem_Click(object sender, EventArgs e)
        {
            var panel = (ExtendedPanel)_contextMenu.SourceControl;
            var index = _flowLayoutPanel.Controls.IndexOf(panel);
            
            if (index > 0)
            {
                var previousPanel = (ExtendedPanel)_flowLayoutPanel.Controls[index - 1];
                _flowLayoutPanel.Controls.SetChildIndex(panel, index - 1);
                _flowLayoutPanel.Controls.SetChildIndex(previousPanel, index);

                UpdatePanelOrder();
            }
        }

        private void MoveDownItem_Click(object sender, EventArgs e)
        {
            var panel = (ExtendedPanel)_contextMenu.SourceControl;
            var index = _flowLayoutPanel.Controls.IndexOf(panel);
            
            if (index < _flowLayoutPanel.Controls.Count - 1)
            {
                var nextPanel = (ExtendedPanel)_flowLayoutPanel.Controls[index + 1];
                _flowLayoutPanel.Controls.SetChildIndex(panel, index + 1);
                _flowLayoutPanel.Controls.SetChildIndex(nextPanel, index);

                UpdatePanelOrder();
            }
        }

        private void UpdatePanelOrder()
        {
            ConfigurationService.Current.Panels.Clear();
            foreach (ExtendedPanel panel in _flowLayoutPanel.Controls.Cast<ExtendedPanel>())
            {
                ConfigurationService.Current.Panels[panel.Name] = new PanelConfig { Background = panel.BackgroundImagePath };
            }
            ConfigurationService.SaveConfig();
        }
        #endregion

    }
}
