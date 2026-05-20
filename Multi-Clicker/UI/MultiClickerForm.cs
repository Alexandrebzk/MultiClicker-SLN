using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MultiClicker.Core;
using MultiClicker.Models;
using MultiClicker.Services;
using MultiClicker.Properties;

namespace MultiClicker.UI
{
    /// <summary>
    /// Enhanced panel class with selection visualization, rounded corners and hover animation.
    /// </summary>
    public class ExtendedPanel : Panel
    {
        private const int CornerRadius = 10;
        private bool _isSelected;
        private bool _isHovered;
        private int _glowAlpha;
        private readonly System.Windows.Forms.Timer _animTimer;

        public string BackgroundImagePath { get; set; }
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                _animTimer.Start();
            }
        }

        public ExtendedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            DoubleBuffered = true;
            _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _animTimer.Tick += (s, e) =>
            {
                var target = (_isSelected ? 220 : (_isHovered ? 90 : 0));
                if (_glowAlpha == target) { _animTimer.Stop(); return; }
                _glowAlpha += Math.Sign(target - _glowAlpha) * Math.Min(40, Math.Abs(target - _glowAlpha));
                Invalidate();
            };
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            _animTimer.Start();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            _animTimer.Start();
        }

        private static System.Drawing.Drawing2D.GraphicsPath BuildRoundedPath(RectangleF r, float radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            var d = radius * 2f;
            if (d > Math.Min(r.Width, r.Height)) d = Math.Min(r.Width, r.Height);
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Suppress default background fill; OnPaint draws everything.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            var parentColor = Parent?.BackColor ?? BackColor;
            var fullRect = new RectangleF(0, 0, Width, Height);

            // 1) Paint the parent background everywhere (corners will keep this color).
            using (var bg = new SolidBrush(parentColor))
                g.FillRectangle(bg, fullRect);

            // 2) Fill the rounded body with the panel's own background color, clipped to the rounded path.
            using (var bodyPath = BuildRoundedPath(fullRect, CornerRadius))
            {
                var prevClip = g.Clip;
                g.SetClip(bodyPath, System.Drawing.Drawing2D.CombineMode.Replace);

                using (var body = new SolidBrush(BackColor))
                    g.FillRectangle(body, fullRect);

                if (BackgroundImage != null)
                {
                    var img = BackgroundImage;
                    // Center image (matches BackgroundImageLayout.Center used by the form).
                    var x = (Width - img.Width) / 2f;
                    var y = (Height - img.Height) / 2f;
                    g.DrawImage(img, x, y, img.Width, img.Height);
                }

                g.Clip = prevClip;
            }

            // 3) Draw the anti-aliased ring just inside the rounded path.
            var ringRect = new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f);
            using (var ringPath = BuildRoundedPath(ringRect, CornerRadius - 0.5f))
            {
                if (_glowAlpha > 0 && _isSelected)
                {
                    using (var pen = new Pen(Color.FromArgb(Math.Min(255, _glowAlpha), 70, 130, 180), 1.25f))
                        g.DrawPath(pen, ringPath);
                }
                else if (_glowAlpha > 0)
                {
                    using (var pen = new Pen(Color.FromArgb(Math.Min(255, _glowAlpha), 70, 130, 180), 1.1f))
                        g.DrawPath(pen, ringPath);
                }
                else
                {
                    using (var pen = new Pen(Color.FromArgb(110, 0, 0, 0), 1f))
                        g.DrawPath(pen, ringPath);
                }
            }

            // 4) Selected-state accent: small filled dot in the top-right corner.
            if (_isSelected)
            {
                const float dotSize = 8f;
                const float dotInset = 6f;
                var dotRect = new RectangleF(Width - dotSize - dotInset, dotInset, dotSize, dotSize);
                using (var halo = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                    g.FillEllipse(halo, dotRect.X - 1, dotRect.Y - 1, dotRect.Width + 2, dotRect.Height + 2);
                using (var dot = new SolidBrush(Color.FromArgb(70, 130, 180)))
                    g.FillEllipse(dot, dotRect);
                using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 1f))
                    g.DrawEllipse(pen, dotRect);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _animTimer?.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Main application form with improved architecture
    /// </summary>
    public partial class MultiClickerForm : Form
    {
        private readonly Panel _titleBar;
        private readonly FlowLayoutPanel _flowLayoutPanel;
        private readonly OpenFileDialog _openFileDialog;
        private ContextMenuStrip _contextMenu;
        private ContextMenuStrip _titleBarMenu;
        private bool _isDragging;
        private Point _dragStartPoint;
        private readonly Dictionary<string, Image> _imageCache;

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

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            InitializeHooks();
            GenerateUI();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            Application.Exit();
        }

        private const int CS_DROPSHADOW = 0x00020000;
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

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

            var closeButton = new Button
            {
                Text = "✕",
                Dock = DockStyle.Right,
                Width = 30,
                Height = 30,
                BackColor = Color.FromArgb(232, 17, 35),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.Click += (s, e) => Close();

            var menuButton = new Button
            {
                Text = "☰",
                Dock = DockStyle.Left,
                Width = 30,
                Height = 30,
                BackColor = Color.FromArgb(70, 130, 180),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold)
            };
            menuButton.FlatAppearance.BorderSize = 0;
            menuButton.Click += MenuButton_Click;

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

            var modeItem = new ToolStripMenuItem(LocalizationService.GetString("ModeMenu")) { Image = CreateMenuIcon("🎮", Color.DarkGreen) };
            var automaticModeItem = new ToolStripMenuItem(LocalizationService.GetString("ModeAutomatic")) { Checked = ConfigurationService.Current.General.DisplayMode == PanelDisplayMode.AUTOMATIC };
            automaticModeItem.Click += (s, e) => SetDisplayMode(PanelDisplayMode.AUTOMATIC);
            var manualModeItem = new ToolStripMenuItem(LocalizationService.GetString("ModeManual")) { Checked = ConfigurationService.Current.General.DisplayMode == PanelDisplayMode.MANUAL };
            manualModeItem.Click += (s, e) => SetDisplayMode(PanelDisplayMode.MANUAL);
            modeItem.DropDownItems.Add(automaticModeItem);
            modeItem.DropDownItems.Add(manualModeItem);

            var keybindsItem = new ToolStripMenuItem(Strings.Keybinds) { Image = CreateMenuIcon("⌨", Color.DarkBlue) };
            keybindsItem.Click += (s, e) => OpenKeybindsConfiguration();

            var selectCharactersItem = new ToolStripMenuItem(LocalizationService.GetString("SelectCharacters")) { Image = CreateMenuIcon("👥", Color.DarkRed), Visible = ConfigurationService.Current.General.DisplayMode == PanelDisplayMode.MANUAL };
            selectCharactersItem.Click += (s, e) => OpenCharacterSelection();

            var backgroundClicksItem = new ToolStripMenuItem(LocalizationService.GetString("PreferBackgroundClicks"))
            {
                Image = CreateMenuIcon("🖱", Color.DarkSlateGray),
                Checked = ConfigurationService.Current.General.PreferBackgroundClicks,
                CheckOnClick = true
            };
            backgroundClicksItem.CheckedChanged += (s, e) =>
            {
                ConfigurationService.Current.General.PreferBackgroundClicks = backgroundClicksItem.Checked;
                ConfigurationService.SaveConfig();
            };

            var languageItem = new ToolStripMenuItem(Strings.Language) { Image = CreateMenuIcon("🌐", Color.DarkGoldenrod) };
            languageItem.Click += (s, e) => ShowLanguageSelection();

            menu.Items.AddRange(new ToolStripItem[] { modeItem, new ToolStripSeparator(), keybindsItem, selectCharactersItem, backgroundClicksItem, new ToolStripSeparator(), languageItem });
            return menu;
        }

        private void MenuButton_Click(object sender, EventArgs e)
        {
            var button = sender as Button;
            if (button != null)
                _titleBarMenu.Show(button, new Point(0, button.Height));
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
                    var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(text, font, brush, new RectangleF(0, 0, 16, 16), format);
                }
            }
            return bitmap;
        }

        private void ShowLanguageSelection()
        {
            if (LocalizationService.ShowLanguageSelectionDialog(this))
                MessageBox.Show(Strings.LanguageChanged, Strings.Language, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private class CustomMenuRenderer : ToolStripProfessionalRenderer
        {
            public CustomMenuRenderer() : base(new CustomColorTable()) { }
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (e.Item.Selected)
                {
                    var rect = new Rectangle(Point.Empty, e.Item.Size);
                    using (var brush = new SolidBrush(Color.FromArgb(70, 130, 180)))
                        e.Graphics.FillRectangle(brush, rect);
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

        private void SetDisplayMode(PanelDisplayMode mode)
        {
            ConfigurationService.Current.General.DisplayMode = mode;
            ConfigurationService.SaveConfig();
            GenerateUI();
            UpdateFormSize();
            _titleBarMenu = CreateTitleBarMenu();
        }

        private void OpenCharacterSelection()
        {
            try
            {
                using (var form = new CharacterSelectionForm())
                {
                    if (form.ShowDialog(this) == DialogResult.OK)
                    {
                        ConfigurationService.Current.General.DisplayMode = PanelDisplayMode.MANUAL;
                        ConfigurationService.SaveConfig();
                        GenerateUI();
                        UpdateFormSize();
                        _titleBarMenu = CreateTitleBarMenu();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationService.GetString("ErrorOpeningCharacterSelection", ex.Message), LocalizationService.GetString("ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenKeybindsConfiguration()
        {
            try
            {
                using (var keybindsForm = new KeybindsConfigForm())
                    keybindsForm.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Strings.ErrorOpeningKeybinds, ex.Message), Strings.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            var moveUpItem = new ToolStripMenuItem(Strings.MoveUp); moveUpItem.Click += MoveUpItem_Click; contextMenu.Items.Add(moveUpItem);
            var moveDownItem = new ToolStripMenuItem(Strings.MoveDown); moveDownItem.Click += MoveDownItem_Click; contextMenu.Items.Add(moveDownItem);
            var changeImageItem = new ToolStripMenuItem(Strings.ChangeBackground); changeImageItem.Click += ChangeImagePanel_Click; contextMenu.Items.Add(changeImageItem);

            return contextMenu;
        }

        private void SetupEventHandlers()
        {
            Resize += OnResize;
            _titleBar.MouseDown += TitleBar_MouseDown;
            _titleBar.MouseUp += TitleBar_MouseUp;
            _titleBar.MouseMove += TitleBar_MouseMove;

            HookManagementService.ShouldOpenPositionConfiguration += HandleKeyBindFormRequest;
        }

        private void InitializeHooks() { }
        private void CleanupHooks() { }

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
                Trace.WriteLine($"Error generating UI: {ex.Message}");
            }
        }

        private void LoadImageCache()
        {
            _imageCache.Clear();
            const string defaultImagePath = @"cosmetics\\default.png";
            if (System.IO.File.Exists(defaultImagePath)) _imageCache[defaultImagePath] = Image.FromFile(defaultImagePath);
            foreach (var panelConfig in ConfigurationService.Current.Panels.Values)
            {
                var imgPath = panelConfig.Background ?? defaultImagePath;
                if (!_imageCache.ContainsKey(imgPath) && System.IO.File.Exists(imgPath)) _imageCache[imgPath] = Image.FromFile(imgPath);
            }
        }

        private void EnsurePanelConfigsExist()
        {
            const string defaultImagePath = @"cosmetics\\default.png";
            foreach (var windowEntry in WindowManagementService.WindowHandles)
            {
                var characterName = windowEntry.Value.CharacterName;
                if (!ConfigurationService.Current.Panels.ContainsKey(characterName)) ConfigurationService.Current.Panels[characterName] = new PanelConfig { Background = defaultImagePath };
            }
            var selected = ConfigurationService.Current.General.SelectedCharacters ?? new List<string>();
            foreach (var name in selected)
                if (!ConfigurationService.Current.Panels.ContainsKey(name)) ConfigurationService.Current.Panels[name] = new PanelConfig { Background = defaultImagePath };
        }

        private void CreatePanels()
        {
            _flowLayoutPanel.SuspendLayout();
            _flowLayoutPanel.Controls.Clear();
            var orderedCharacterNames = new List<string>();

            List<string> charactersToDisplay = GetCharactersToDisplay();
            const string defaultImagePath = @"cosmetics\\default.png";

            foreach (var characterName in charactersToDisplay)
            {
                PanelConfig panelConfig = ConfigurationService.Current.Panels != null && ConfigurationService.Current.Panels.ContainsKey(characterName)
                    ? ConfigurationService.Current.Panels[characterName]
                    : new PanelConfig { Background = defaultImagePath };

                var panel = CreatePanel(characterName, panelConfig);
                if (panel != null) { _flowLayoutPanel.Controls.Add(panel); orderedCharacterNames.Add(panel.Name); }
            }

            // Merge: keep existing PanelConfig entries (preserving Background) and only update from rendered panels.
            if (ConfigurationService.Current.Panels == null)
                ConfigurationService.Current.Panels = new Dictionary<string, PanelConfig>();
            foreach (ExtendedPanel panel in _flowLayoutPanel.Controls.Cast<ExtendedPanel>())
            {
                if (ConfigurationService.Current.Panels.TryGetValue(panel.Name, out var existing))
                {
                    existing.Background = panel.BackgroundImagePath ?? existing.Background;
                }
                else
                {
                    ConfigurationService.Current.Panels[panel.Name] = new PanelConfig { Background = panel.BackgroundImagePath };
                }
            }
            ConfigurationService.SaveConfig();

            WindowManagementService.ReorderWindowHandles(orderedCharacterNames);
            _flowLayoutPanel.ResumeLayout();
        }

        private List<string> GetCharactersToDisplay()
        {
            var mode = ConfigurationService.Current.General.DisplayMode;
            if (mode == PanelDisplayMode.AUTOMATIC) return WindowManagementService.WindowHandles.Values.Select(w => w.CharacterName).Distinct().OrderBy(c => c).ToList();
            var selectedCharacters = ConfigurationService.Current.General.SelectedCharacters ?? new List<string>();
            if (selectedCharacters.Count == 0) return WindowManagementService.WindowHandles.Values.Select(w => w.CharacterName).Distinct().OrderBy(c => c).ToList();
            return selectedCharacters;
        }

        private ExtendedPanel CreatePanel(string panelName, PanelConfig panelConfig)
        {
            var windowEntry = WindowManagementService.WindowHandles.FirstOrDefault(e => e.Value.CharacterName == panelName);
            if (windowEntry.Key == IntPtr.Zero)
            {
                try
                {
                    var matchingProcess = Process.GetProcesses().FirstOrDefault(p =>
                    {
                        try
                        {
                            if (p.MainWindowHandle != IntPtr.Zero)
                            {
                                var title = string.IsNullOrWhiteSpace(p.MainWindowTitle) ? p.ProcessName : p.MainWindowTitle;
                                return string.Equals(title, panelName, StringComparison.OrdinalIgnoreCase) || string.Equals(p.ProcessName, panelName, StringComparison.OrdinalIgnoreCase);
                            }
                        }
                        catch { }
                        return false;
                    });

                    if (matchingProcess != null && matchingProcess.MainWindowHandle != IntPtr.Zero)
                    {
                        var handle = matchingProcess.MainWindowHandle;
                        var title = string.IsNullOrWhiteSpace(matchingProcess.MainWindowTitle) ? matchingProcess.ProcessName : matchingProcess.MainWindowTitle;
                        if (!WindowManagementService.WindowHandles.ContainsKey(handle)) WindowManagementService.WindowHandles[handle] = new WindowInfo { WindowName = title, CharacterName = panelName };
                        windowEntry = WindowManagementService.WindowHandles.FirstOrDefault(e => e.Key == handle);
                    }
                }
                catch { }
            }

            if (windowEntry.Key == IntPtr.Zero) return null;

            const string defaultImagePath = @"cosmetics\\default.png";
            var imgPath = panelConfig.Background ?? defaultImagePath;
            var panelImage = _imageCache.ContainsKey(imgPath) ? _imageCache[imgPath] : (_imageCache.ContainsKey(defaultImagePath) ? _imageCache[defaultImagePath] : null);
            var avgColor = PanelManagementService.GetAverageColor(panelImage);

            var panel = new ExtendedPanel
            {
                ContextMenuStrip = _contextMenu,
                Size = new Size(70, 70),
                Margin = new Padding(5),
                BackgroundImage = panelImage,
                BackgroundImagePath = imgPath,
                BackgroundImageLayout = ImageLayout.Center,
                Tag = windowEntry.Key,
                Name = panelName,
                BackColor = avgColor,
                Cursor = Cursors.Hand
            };

            // Reflect any per-panel visual flags here.

            windowEntry.Value.RelatedPanel = panel;

            var label = new Label { Text = panelName, AutoSize = false, Dock = DockStyle.Bottom, ForeColor = PanelManagementService.IsColorDark(panel.BackColor) ? Color.White : Color.Black, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), BackColor = Color.Transparent, Height = 22 };
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
            Location = new Point(Screen.PrimaryScreen.WorkingArea.Width - ClientSize.Width - (Screen.PrimaryScreen.WorkingArea.Width / 20), _titleBar.Height + (Screen.PrimaryScreen.WorkingArea.Height / 10));
        }

        private void SelectFirstPanel() { if (_flowLayoutPanel.Controls.Count > 0) PanelManagementService.HandlePanelClick(_flowLayoutPanel.Controls[0], EventArgs.Empty); }

        private void TitleBar_MouseDown(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _isDragging = true; _dragStartPoint = e.Location; } }
        private void TitleBar_MouseUp(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) _isDragging = false; }
        private void TitleBar_MouseMove(object sender, MouseEventArgs e) { if (_isDragging) { var p = PointToScreen(e.Location); Location = new Point(p.X - _dragStartPoint.X, p.Y - _dragStartPoint.Y); } }

        private void UpdateLayout() { _flowLayoutPanel.Width = ClientSize.Width; _flowLayoutPanel.Height = ClientSize.Height - _titleBar.Height; }

        private void OnResize(object sender, EventArgs e)
        {
            UpdateLayout();
        }

        private void HandleKeyBindFormRequest() { Invoke((MethodInvoker)delegate { using (var inputForm = new PositionConfigurationForm()) inputForm.ShowDialog(); }); }

        private void ChangeImagePanel_Click(object sender, EventArgs e)
        {
            try
            {
                using (var picker = new ImagePickerDialog())
                {
                    if (picker.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(picker.SelectedImagePath))
                        return;

                    var panel = (ExtendedPanel)_contextMenu.SourceControl;
                    var newPath = picker.SelectedImagePath;
                    try
                    {
                        var img = Image.FromFile(newPath);
                        panel.BackgroundImage = img;
                        panel.BackgroundImagePath = newPath;
                        _imageCache[newPath] = img;
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Failed to load image '{newPath}': {ex.Message}");
                        return;
                    }

                    if (ConfigurationService.Current.Panels == null)
                        ConfigurationService.Current.Panels = new Dictionary<string, PanelConfig>();
                    if (!ConfigurationService.Current.Panels.TryGetValue(panel.Name, out var cfg))
                        ConfigurationService.Current.Panels[panel.Name] = cfg = new PanelConfig();
                    cfg.Background = newPath;
                    ConfigurationService.SaveConfig();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error changing panel image: {ex.Message}");
            }
        }

        private void MoveUpItem_Click(object sender, EventArgs e) { var panel = (ExtendedPanel)_contextMenu.SourceControl; var index = _flowLayoutPanel.Controls.IndexOf(panel); if (index > 0) { var previousPanel = (ExtendedPanel)_flowLayoutPanel.Controls[index - 1]; _flowLayoutPanel.Controls.SetChildIndex(panel, index - 1); _flowLayoutPanel.Controls.SetChildIndex(previousPanel, index); UpdatePanelOrder(); } }
        private void MoveDownItem_Click(object sender, EventArgs e) { var panel = (ExtendedPanel)_contextMenu.SourceControl; var index = _flowLayoutPanel.Controls.IndexOf(panel); if (index < _flowLayoutPanel.Controls.Count - 1) { var nextPanel = (ExtendedPanel)_flowLayoutPanel.Controls[index + 1]; _flowLayoutPanel.Controls.SetChildIndex(panel, index + 1); _flowLayoutPanel.Controls.SetChildIndex(nextPanel, index); UpdatePanelOrder(); } }

        private void UpdatePanelOrder()
        {
            var orderedCharacterNames = new List<string>();
            foreach (ExtendedPanel panel in _flowLayoutPanel.Controls.Cast<ExtendedPanel>()) orderedCharacterNames.Add(panel.Name);
            WindowManagementService.ReorderWindowHandles(orderedCharacterNames);
            if (ConfigurationService.Current.Panels == null)
                ConfigurationService.Current.Panels = new Dictionary<string, PanelConfig>();
            foreach (ExtendedPanel panel in _flowLayoutPanel.Controls.Cast<ExtendedPanel>())
            {
                if (ConfigurationService.Current.Panels.TryGetValue(panel.Name, out var existing))
                {
                    existing.Background = panel.BackgroundImagePath ?? existing.Background;
                }
                else
                {
                    ConfigurationService.Current.Panels[panel.Name] = new PanelConfig { Background = panel.BackgroundImagePath };
                }
            }
            ConfigurationService.SaveConfig();
        }
    }

    public partial class CharacterSelectionForm : Form
    {
        private List<string> _availableCharacters;
        private List<string> _selectedCharacters;
        private CheckedListBox _charactersListBox;
        private Button _saveButton;
        private Button _cancelButton;
        private Button _selectAllButton;
        private Button _deselectAllButton;

        public CharacterSelectionForm()
        {
            InitializeComponent();
            _availableCharacters = new List<string>();
            _selectedCharacters = new List<string>();
            LoadAvailableCharacters();
            CreateInterface();
        }

        private void LoadAvailableCharacters()
        {
            var characterNames = new List<string>();
            try
            {
                string _;
                var dofusWindows = WindowManagementService.EnumerateDofusWindows(out _);
                foreach (var wi in dofusWindows)
                {
                    if (!string.IsNullOrWhiteSpace(wi.CharacterName))
                        characterNames.Add(wi.CharacterName);
                }
            }
            catch { }

            _availableCharacters = characterNames.Distinct().OrderBy(c => c).ToList();
            _selectedCharacters = new List<string>(ConfigurationService.Current.General.SelectedCharacters ?? new List<string>());
        }

        private void CreateInterface()
        {
            this.Text = LocalizationService.GetString("CharacterSelectionTitle");
            this.Size = new Size(400, 450);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(44, 47, 51);
            this.ForeColor = Color.White;

            // Use a TableLayoutPanel so the list always starts below the title
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(10)
            };
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // title
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // instruction
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // list
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // quick buttons
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // action buttons

            // Title
            var titleLabel = new Label
            {
                Text = LocalizationService.GetString("CharactersTitle"),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Dock = DockStyle.Fill,
                Height = 40,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White
            };
            table.Controls.Add(titleLabel, 0, 0);

            // Instruction
            var instructionLabel = new Label
            {
                Text = LocalizationService.GetString("CharactersInstruction"),
                Dock = DockStyle.Fill,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.LightGray
            };
            table.Controls.Add(instructionLabel, 0, 1);

            // Characters list (fills remaining space)
            _charactersListBox = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(36, 37, 38),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                CheckOnClick = true,
                IntegralHeight = false
            };

            foreach (var character in _availableCharacters)
            {
                int index = _charactersListBox.Items.Add(character);
                _charactersListBox.SetItemChecked(index, _selectedCharacters.Contains(character));
            }
            table.Controls.Add(_charactersListBox, 0, 2);

            // Quick select buttons
            var quickButtonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Height = 35,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true
            };

            _selectAllButton = new Button
            {
                Text = LocalizationService.GetString("SelectAll"),
                Size = new Size(120, 30),
                BackColor = Color.FromArgb(70, 130, 180),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _selectAllButton.Click += (s, e) => { for (int i = 0; i < _charactersListBox.Items.Count; i++) _charactersListBox.SetItemChecked(i, true); };
            quickButtonsPanel.Controls.Add(_selectAllButton);

            _deselectAllButton = new Button
            {
                Text = LocalizationService.GetString("DeselectAll"),
                Size = new Size(120, 30),
                BackColor = Color.FromArgb(120, 81, 169),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _deselectAllButton.Click += (s, e) => { for (int i = 0; i < _charactersListBox.Items.Count; i++) _charactersListBox.SetItemChecked(i, false); };
            quickButtonsPanel.Controls.Add(_deselectAllButton);

            table.Controls.Add(quickButtonsPanel, 0, 3);

            // Action buttons (Save / Cancel)
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Height = 50,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true
            };

            _saveButton = new Button
            {
                Text = LocalizationService.GetString("Save"),
                Size = new Size(100, 30),
                BackColor = Color.LightGreen,
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat
            };
            _saveButton.Click += SaveButton_Click;
            buttonPanel.Controls.Add(_saveButton);

            _cancelButton = new Button
            {
                Text = LocalizationService.GetString("Cancel"),
                Size = new Size(100, 30),
                BackColor = Color.LightCoral,
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat
            };
            _cancelButton.Click += CancelButton_Click;
            buttonPanel.Controls.Add(_cancelButton);

            table.Controls.Add(buttonPanel, 0, 4);

            Controls.Add(table);
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            _selectedCharacters.Clear();
            foreach (var item in _charactersListBox.CheckedItems) _selectedCharacters.Add(item.ToString());
            if (_selectedCharacters.Count == 0) { MessageBox.Show(LocalizationService.GetString("NoCharactersSelected"), LocalizationService.GetString("ErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            ConfigurationService.Current.General.SelectedCharacters = _selectedCharacters;
            ConfigurationService.SaveConfig();
            DialogResult = DialogResult.OK; Close();
        }

        private void CancelButton_Click(object sender, EventArgs e) { DialogResult = DialogResult.Cancel; Close(); }

        private void InitializeComponent() { this.SuspendLayout(); this.AutoScaleDimensions = new SizeF(6F, 13F); this.AutoScaleMode = AutoScaleMode.Font; this.ClientSize = new Size(400, 450); this.Name = "CharacterSelectionForm"; this.ResumeLayout(false); }
    }
}
