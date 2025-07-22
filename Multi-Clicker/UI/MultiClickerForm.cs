using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MultiClicker.Core;
using MultiClicker.Models;
using MultiClicker.Services;

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

            var closeButton = new Button
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

            var helpButton = CreateHelpButton();

            closeButton.Click += (s, e) => Close();
            titleBar.Controls.Add(closeButton);
            titleBar.Controls.Add(helpButton);

            return titleBar;
        }

        private Button CreateHelpButton()
        {
            var helpButton = new Button
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

            var toolTip = new ToolTip
            {
                InitialDelay = 10,
                ReshowDelay = 500,
                ShowAlways = true
            };

            var tooltipText = GenerateTooltipText();
            toolTip.SetToolTip(helpButton, tooltipText);

            return helpButton;
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
                Filter = "Image Files(*.BMP;*.JPG;*.GIF;*.PNG)|*.BMP;*.JPG;*.GIF;*.PNG|All files (*.*)|*.*",
                Title = "Select Image"
            };
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var contextMenu = new ContextMenuStrip();

            var moveUpItem = new ToolStripMenuItem("Move Up");
            moveUpItem.Click += MoveUpItem_Click;
            contextMenu.Items.Add(moveUpItem);

            var moveDownItem = new ToolStripMenuItem("Move Down");
            moveDownItem.Click += MoveDownItem_Click;
            contextMenu.Items.Add(moveDownItem);

            var changeImageItem = new ToolStripMenuItem("Change Image");
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
            HookManagementService.ShouldOpenKeyBindForm += HandleKeyBindFormRequest;
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
            Invoke((MethodInvoker)delegate
            {
                using (var inputForm = new ReplicateTextForm())
                {
                    if (inputForm.ShowDialog() == DialogResult.OK)
                    {
                        var inputText = inputForm.InputText;
                        var windowList = WindowManagementService.WindowHandles.ToList();
                        WindowManagementService.SendTextToWindows(inputText, windowList);
                    }
                }
            });
        }

        private void HandleKeyBindFormRequest()
        {
            Invoke((MethodInvoker)delegate
            {
                using (var inputForm = new KeyBindForm())
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

        #region Helper Methods
        private string GenerateTooltipText()
        {
            var keyBinds = new Dictionary<string, string>
            {
                { "CLICK with Delay", ConfigurationService.Current.Keybinds[TRIGGERS.SIMPLE_CLICK].ToString() },
                { "Click without Delay", ConfigurationService.Current.Keybinds[TRIGGERS.SIMPLE_CLICK_NO_DELAY].ToString() },
                { "Double Click", ConfigurationService.Current.Keybinds[TRIGGERS.DOUBLE_CLICK].ToString() },
                { "Next character", ConfigurationService.Current.Keybinds[TRIGGERS.SELECT_NEXT].ToString() },
                { "Previous Character", ConfigurationService.Current.Keybinds[TRIGGERS.SELECT_PREVIOUS].ToString() },
                { "Input chat commands (Dofus Open discussion --> Tab)", ConfigurationService.Current.Keybinds[TRIGGERS.TRAVEL].ToString() },
                { "Auto complete HDV (see position config) -->", "² + MRbutton" },
                { "Positions' config -->", ConfigurationService.Current.Keybinds[TRIGGERS.OPTIONS].ToString() },
            };

            var tooltipText = new System.Text.StringBuilder();
            tooltipText.AppendLine("FUNCTION ----> Bind\n");
            
            foreach (var keyBind in keyBinds)
            {
                tooltipText.AppendLine($"{keyBind.Key}----->{keyBind.Value}");
            }
            
            tooltipText.AppendLine("Ctrl + Alt + V -----> paste on all windows");
            
            return tooltipText.ToString();
        }
        #endregion
    }
}
