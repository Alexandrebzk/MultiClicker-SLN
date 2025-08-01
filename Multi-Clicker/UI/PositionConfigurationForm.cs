using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using MultiClicker.Models;
using MultiClicker.Services;
using MultiClicker.Properties;

namespace MultiClicker
{
    public partial class PositionConfigurationForm : Form
    {
        private PositionOverlayForm overlayForm;
        private static POINT? firstClick;
        private static TRIGGERS_POSITIONS currentTrigger;
        private TableLayoutPanel tableLayoutPanel;
        public delegate void UpdateEventHandler();
        public static event UpdateEventHandler OnUpdateCompleted;
        private Dictionary<TRIGGERS_POSITIONS, Color> positionColors = new Dictionary<TRIGGERS_POSITIONS, Color>();

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            OnUpdateCompleted -= UpdateManager_OnUpdateCompleted;

            if (overlayForm != null)
            {
                overlayForm.Close();
            }
        }
        
        public PositionConfigurationForm()
        {
            InitializeComponent();
            InitializeTableLayoutPanel();
            PopulateTable();
            OnUpdateCompleted += UpdateManager_OnUpdateCompleted;

            overlayForm = new PositionOverlayForm
            {
                Positions = ConfigurationService.Current.Positions,
                PositionColors = positionColors
            };
            overlayForm.Show();
        }

        private void InitializeTableLayoutPanel()
        {
            this.Shown += (sender, e) => this.Activate();
            this.TopMost = true;
            this.StartPosition = FormStartPosition.CenterScreen;

            tableLayoutPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoScroll = true
            };
            Controls.Add(tableLayoutPanel);


            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }

        private void PopulateTable()
        {
            tableLayoutPanel.Controls.Clear();
            tableLayoutPanel.ColumnStyles.Clear();
            tableLayoutPanel.RowStyles.Clear();
            positionColors.Clear();

            int numberOfTriggers = ConfigurationService.Current.Positions.Count;
            tableLayoutPanel.RowCount = numberOfTriggers + 1;
            tableLayoutPanel.ColumnCount = 4;

            tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));

            AddCell(new Label { Text = Strings.TriggerNameColumn, TextAlign = ContentAlignment.MiddleCenter }, 0, 0);
            AddCell(new Label { Text = Strings.PositionColumn, TextAlign = ContentAlignment.MiddleCenter }, 1, 0);
            AddCell(new Label { Text = Strings.UpdateColumn, TextAlign = ContentAlignment.MiddleCenter }, 2, 0);
            AddCell(new Label { Text = Strings.ColorColumn, TextAlign = ContentAlignment.MiddleCenter }, 3, 0);

            Random rand = new Random();
            int rowIndex = 1;
            foreach (var trigger in ConfigurationService.Current.Positions.Keys)
            {
                Position position = ConfigurationService.Current.Positions[trigger];
                AddCell(new Label { Text = trigger.ToString(), TextAlign = ContentAlignment.MiddleCenter }, 0, rowIndex);
                AddCell(new Label { Text = $"X: {position.X}, Y: {position.Y}, W: {position.Width}, H: {position.Height}", TextAlign = ContentAlignment.MiddleCenter }, 1, rowIndex);

                Button updateButton = new Button { Text = Strings.UpdateButton };
                updateButton.Click += (sender, e) => UpdateButton_Click(trigger);
                AddCell(updateButton, 2, rowIndex);

                Color randomColor = Color.FromArgb(rand.Next(256), rand.Next(256), rand.Next(256));
                positionColors[trigger] = randomColor;

                Panel colorPanel = new Panel
                {
                    BackColor = randomColor,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(1)
                };
                AddCell(colorPanel, 3, rowIndex);

                rowIndex++;
            }

            tableLayoutPanel.PerformLayout();
        }

        private void AddCell(Control control, int column, int row)
        {
            tableLayoutPanel.Controls.Add(control, column, row);
            control.Dock = DockStyle.Fill;
        }
        private void UpdateButton_Click(TRIGGERS_POSITIONS trigger)
        {
            ConfigurationService.IsModifyingKeyBinds = true;
            currentTrigger = trigger;
            firstClick = null;
        }

        public static void TriggerUpdate()
        {
            OnUpdateCompleted?.Invoke();
        }
        
        private void UpdateManager_OnUpdateCompleted()
        {
            this.Invoke((MethodInvoker)delegate
            {
                overlayForm.Positions = ConfigurationService.Current.Positions;
                overlayForm.PositionColors = positionColors;
                overlayForm.RefreshOverlay();
                PopulateTable();
            });
        }
        
        public static void choosePosition()
        {
            if (firstClick == null)
            {
                firstClick = HookManagementService.CursorPosition;
            }
            else
            {
                Trace.WriteLine($"First click: {firstClick.Value}, Second click: {HookManagementService.CursorPosition}");
                Position newPosition = new Position
                {
                    X = Math.Min(firstClick.Value.X, HookManagementService.CursorPosition.X),
                    Y = Math.Min(firstClick.Value.Y, HookManagementService.CursorPosition.Y),
                    Width = Math.Abs(firstClick.Value.X - HookManagementService.CursorPosition.X),
                    Height = Math.Abs(firstClick.Value.Y - HookManagementService.CursorPosition.Y)
                };
                ConfigurationService.Current.Positions[currentTrigger] = newPosition;
                ConfigurationService.SaveConfig();
                TriggerUpdate();
                firstClick = null;
                ConfigurationService.IsModifyingKeyBinds = false;
            }
        }
    }
}
