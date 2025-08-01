using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MultiClicker.Models;
using MultiClicker.Services;
using MultiClicker.Properties;

namespace MultiClicker.UI
{
    /// <summary>
    /// Form for configuring keybinds with user-friendly interface
    /// </summary>
    public partial class KeybindsConfigForm : Form
    {
        #region P/Invoke Declarations
        [DllImport("user32.dll")]
        static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        private const int WH_MOUSE_LL = 14;
        private const int WM_XBUTTONDOWN = 0x020B;

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
        #endregion

        private Dictionary<TRIGGERS, KeyCombination> _pendingKeybinds;
        private Dictionary<TRIGGERS, string> _triggerDescriptions;
        private TableLayoutPanel _mainTableLayout;
        private Button _saveButton;
        private Button _cancelButton;
        private Button _resetButton;
        private bool _isCapturingInput = false;
        private TextBox _activeTextBox = null;
        private KeyCombination _currentCombination = new KeyCombination();
        private IntPtr _mouseHook = IntPtr.Zero;
        private LowLevelMouseProc _mouseProc;

        public KeybindsConfigForm()
        {
            InitializeComponent();
            InitializeTriggerDescriptions();
            LoadCurrentKeybinds();
            CreateInterface();
            SetupMouseHook();
        }

        private void SetupMouseHook()
        {
            _mouseProc = MouseHookCallback;
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_XBUTTONDOWN && _isCapturingInput && _activeTextBox != null)
            {
                var hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                var xButton = (int)(hookStruct.mouseData >> 16);

                if (xButton == 1)
                {
                    _currentCombination.XButton1 = true;
                    this.Invoke(new Action(() => UpdateCombinationDisplay(_activeTextBox)));
                }
                else if (xButton == 2)
                {
                    _currentCombination.XButton2 = true;
                    this.Invoke(new Action(() => UpdateCombinationDisplay(_activeTextBox)));
                }
            }

            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            base.OnFormClosed(e);
        }

        private void InitializeTriggerDescriptions()
        {
            _triggerDescriptions = new Dictionary<TRIGGERS, string>
            {
                { TRIGGERS.SELECT_NEXT, Strings.SELECT_NEXT },
                { TRIGGERS.SELECT_PREVIOUS, Strings.SELECT_PREVIOUS },
                { TRIGGERS.TRAVEL, Strings.TRAVEL },
                { TRIGGERS.OPTIONS, Strings.OPTIONS },
                { TRIGGERS.SIMPLE_CLICK, Strings.SIMPLE_CLICK },
                { TRIGGERS.DOUBLE_CLICK, Strings.DOUBLE_CLICK },
                { TRIGGERS.SIMPLE_CLICK_NO_DELAY, Strings.SIMPLE_CLICK_NO_DELAY },
                { TRIGGERS.DOFUS_HAVENBAG, Strings.DOFUS_HAVENBAG },
                { TRIGGERS.DOFUS_OPEN_DISCUSSION, Strings.DOFUS_OPEN_DISCUSSION },
                { TRIGGERS.GROUP_CHARACTERS, Strings.GROUP_CHARACTERS },
                { TRIGGERS.FILL_HDV, Strings.FILL_HDV },
                { TRIGGERS.PASTE_ON_ALL_WINDOWS, Strings.PasteOnAllWindows }
            };
        }

        private void LoadCurrentKeybinds()
        {
            _pendingKeybinds = new Dictionary<TRIGGERS, KeyCombination>();
            
            foreach (TRIGGERS trigger in Enum.GetValues(typeof(TRIGGERS)))
            {
                if (ConfigurationService.Current.Keybinds.ContainsKey(trigger))
                {
                    var currentKeybind = ConfigurationService.Current.Keybinds[trigger];
                    _pendingKeybinds[trigger] = new KeyCombination(
                        currentKeybind.Key,
                        currentKeybind.Control,
                        currentKeybind.Shift,
                        currentKeybind.Alt,
                        currentKeybind.LeftMouseButton,
                        currentKeybind.RightMouseButton,
                        currentKeybind.MiddleMouseButton,
                        currentKeybind.XButton1,
                        currentKeybind.XButton2
                    );
                }
                else
                {
                    _pendingKeybinds[trigger] = new KeyCombination();
                }
            }
        }

        private void CreateInterface()
        {
            this.Text = Strings.KeybindsFormTitle;
            this.Size = new Size(600, 500);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // Main container
            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };
            this.Controls.Add(mainPanel);

            // Title
            var titleLabel = new Label
            {
                Text = Strings.KeybindsFormTitle,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleCenter
            };
            mainPanel.Controls.Add(titleLabel);

            // Instruction
            var instructionLabel = new Label
            {
                Text = Strings.KeybindsInstructions,
                Dock = DockStyle.Top,
                Height = 60,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DarkBlue
            };
            mainPanel.Controls.Add(instructionLabel);

            // Scroll panel for keybinds
            var scrollPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle
            };
            mainPanel.Controls.Add(scrollPanel);

            // Table layout for keybinds
            _mainTableLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 3,
                RowCount = _pendingKeybinds.Count + 1,
                Padding = new Padding(5)
            };

            _mainTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _mainTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            _mainTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15F));

            scrollPanel.Controls.Add(_mainTableLayout);

            CreateKeybindRows();

            // Button panel
            var buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50
            };
            mainPanel.Controls.Add(buttonPanel);

            CreateButtons(buttonPanel);
        }

        private void CreateKeybindRows()
        {
            // Header
            var headerDescription = new Label
            {
                Text = Strings.ActionColumn,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill
            };
            _mainTableLayout.Controls.Add(headerDescription, 0, 0);

            var headerKeybind = new Label
            {
                Text = Strings.ShortcutColumn,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill
            };
            _mainTableLayout.Controls.Add(headerKeybind, 1, 0);

            var headerClear = new Label
            {
                Text = Strings.ClearColumn,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            _mainTableLayout.Controls.Add(headerClear, 2, 0);

            int row = 1;
            foreach (var trigger in _pendingKeybinds.Keys.OrderBy(t => t.ToString()))
            {
                CreateKeybindRow(trigger, row);
                row++;
            }
        }

        private void CreateKeybindRow(TRIGGERS trigger, int row)
        {
            // Description
            var descriptionLabel = new Label
            {
                Text = _triggerDescriptions.ContainsKey(trigger) ? _triggerDescriptions[trigger] : trigger.ToString(),
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(3)
            };
            _mainTableLayout.Controls.Add(descriptionLabel, 0, row);

            // Keybind input
            var keybindTextBox = new TextBox
            {
                Text = _pendingKeybinds[trigger].ToString(),
                ReadOnly = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(3),
                BackColor = Color.White,
                Tag = trigger
            };

            keybindTextBox.KeyDown += KeybindTextBox_KeyDown;
            keybindTextBox.Enter += KeybindTextBox_Enter;
            keybindTextBox.Leave += KeybindTextBox_Leave;
            keybindTextBox.MouseDown += KeybindTextBox_MouseDown;

            _mainTableLayout.Controls.Add(keybindTextBox, 1, row);

            // Clear button
            var clearButton = new Button
            {
                Text = Strings.ClearButton,
                Size = new Size(25, 23),
                Anchor = AnchorStyles.None,
                Tag = trigger,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.Red
            };

            clearButton.Click += ClearButton_Click;
            _mainTableLayout.Controls.Add(clearButton, 2, row);
        }

        private void KeybindTextBox_Enter(object sender, EventArgs e)
        {
            var textBox = sender as TextBox;
            _activeTextBox = textBox;
            _isCapturingInput = false; // Start without capturing, wait for user input
            _currentCombination = new KeyCombination(); // Reset current combination
            textBox.BackColor = Color.LightYellow;
            textBox.Text = Strings.PressKeysPrompt;
        }

        private void KeybindTextBox_Leave(object sender, EventArgs e)
        {
            var textBox = sender as TextBox;
            textBox.BackColor = Color.White;
            _isCapturingInput = false;
            _activeTextBox = null;
            _currentCombination = new KeyCombination();
            var trigger = (TRIGGERS)textBox.Tag;
            textBox.Text = _pendingKeybinds[trigger].ToString();
        }

        private void KeybindTextBox_MouseDown(object sender, MouseEventArgs e)
        {
            var textBox = sender as TextBox;
            
            if (textBox == _activeTextBox && _isCapturingInput)
            {
                // Capture mouse button while capturing input
                switch (e.Button)
                {
                    case MouseButtons.Left:
                        _currentCombination.LeftMouseButton = true;
                        break;
                    case MouseButtons.Right:
                        _currentCombination.RightMouseButton = true;
                        break;
                    case MouseButtons.Middle:
                        _currentCombination.MiddleMouseButton = true;
                        break;
                    case MouseButtons.XButton1:
                        _currentCombination.XButton1 = true;
                        break;
                    case MouseButtons.XButton2:
                        _currentCombination.XButton2 = true;
                        break;
                }
                
                // Update the display
                UpdateCombinationDisplay(textBox);
            }
            else if (textBox == _activeTextBox)
            {
                // If already focused and user clicks again, prepare to capture next input
                _isCapturingInput = true;
                _currentCombination = new KeyCombination();
                textBox.Text = Strings.ReadyToCapture;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Override to handle global key capture when a textbox is active
            if (_activeTextBox != null && _isCapturingInput)
            {
                // Ignore mouse buttons
                if (keyData == Keys.LButton || keyData == Keys.RButton || keyData == Keys.MButton)
                    return false;

                // Handle Tab key specially - prevent default tab navigation and let KeyDown handle it
                if (keyData == Keys.Tab || keyData == (Keys.Tab | Keys.Shift))
                {
                    // Create a synthetic KeyEventArgs for Tab and call KeyDown directly
                    var tabArgs = new KeyEventArgs(keyData);
                    KeybindTextBox_KeyDown(_activeTextBox, tabArgs);
                    return true; // Prevent default tab navigation
                }

                // Let the textbox handle other keys
                return false;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void KeybindTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;

            var textBox = sender as TextBox;
            var trigger = (TRIGGERS)textBox.Tag;

            // If we're not capturing input yet, start capturing on any key press
            if (!_isCapturingInput)
            {
                _isCapturingInput = true;
                _currentCombination = new KeyCombination();
                textBox.Text = Strings.PressKeysPrompt;
                return;
            }

            // Ignore modifier keys alone
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu)
                return;

            // Ignore mouse button keys that might be converted to key events
            if (e.KeyCode == Keys.LButton || e.KeyCode == Keys.RButton || e.KeyCode == Keys.MButton)
                return;

            // Handle special keys like Enter to confirm the combination
            if (e.KeyCode == Keys.Enter)
            {
                if (!_currentCombination.IsEmpty)
                {
                    ConfirmKeybind(textBox, trigger);
                }
                return;
            }

            // Handle Escape to cancel
            if (e.KeyCode == Keys.Escape)
            {
                CancelKeybindCapture(textBox, trigger);
                return;
            }

            // Update the current combination with the new key
            _currentCombination.Key = e.KeyCode;
            _currentCombination.Control = e.Control;
            _currentCombination.Shift = e.Shift;
            _currentCombination.Alt = e.Alt;

            UpdateCombinationDisplay(textBox);
        }

        private void UpdateCombinationDisplay(TextBox textBox)
        {
            if (_currentCombination.IsEmpty)
            {
                textBox.Text = Strings.PressKeysPrompt;
            }
            else
            {
                textBox.Text = _currentCombination.ToString() + Strings.KeyCombinationInstruction;
            }
        }

        private void ConfirmKeybind(TextBox textBox, TRIGGERS trigger)
        {
            _pendingKeybinds[trigger] = new KeyCombination(
                _currentCombination.Key,
                _currentCombination.Control,
                _currentCombination.Shift,
                _currentCombination.Alt,
                _currentCombination.LeftMouseButton,
                _currentCombination.RightMouseButton,
                _currentCombination.MiddleMouseButton,
                _currentCombination.XButton1,
                _currentCombination.XButton2
            );
            
            textBox.Text = _pendingKeybinds[trigger].ToString();
            textBox.BackColor = Color.LightGreen;
            _isCapturingInput = false;
            _currentCombination = new KeyCombination();
        }

        private void CancelKeybindCapture(TextBox textBox, TRIGGERS trigger)
        {
            _isCapturingInput = false;
            _currentCombination = new KeyCombination();
            textBox.Text = _pendingKeybinds[trigger].ToString();
            textBox.BackColor = Color.White;
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            var button = sender as Button;
            var trigger = (TRIGGERS)button.Tag;
            
            _pendingKeybinds[trigger] = new KeyCombination();
            
            // Update the corresponding textbox
            foreach (Control control in _mainTableLayout.Controls)
            {
                if (control is TextBox textBox && textBox.Tag.Equals(trigger))
                {
                    textBox.Text = "";
                    break;
                }
            }
        }

        private void CreateButtons(Panel buttonPanel)
        {
            _saveButton = new Button
            {
                Text = Strings.SaveButton,
                Size = new Size(100, 30),
                Location = new Point(10, 10),
                BackColor = Color.LightGreen,
                FlatStyle = FlatStyle.Flat
            };
            _saveButton.Click += SaveButton_Click;
            buttonPanel.Controls.Add(_saveButton);

            _cancelButton = new Button
            {
                Text = Strings.CancelButton,
                Size = new Size(100, 30),
                Location = new Point(120, 10),
                BackColor = Color.LightCoral,
                FlatStyle = FlatStyle.Flat
            };
            _cancelButton.Click += CancelButton_Click;
            buttonPanel.Controls.Add(_cancelButton);

            _resetButton = new Button
            {
                Text = Strings.ResetButton,
                Size = new Size(100, 30),
                Location = new Point(230, 10),
                BackColor = Color.LightBlue,
                FlatStyle = FlatStyle.Flat
            };
            _resetButton.Click += ResetButton_Click;
            buttonPanel.Controls.Add(_resetButton);
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            // Save all keybinds
            foreach (var kvp in _pendingKeybinds)
            {
                ConfigurationService.UpdateKeybind(kvp.Key, kvp.Value);
            }

            MessageBox.Show(Strings.ConfigSavedSuccessfully, Strings.SuccessTitle, 
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void ResetButton_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(Strings.ResetConfirmation, Strings.ConfirmationTitle, 
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                LoadCurrentKeybinds();
                RefreshInterface();
            }
        }

        private void RefreshInterface()
        {
            _mainTableLayout.Controls.Clear();
            CreateKeybindRows();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.AutoScaleDimensions = new SizeF(6F, 13F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(600, 500);
            this.Name = "KeybindsConfigForm";
            this.ResumeLayout(false);
        }
    }
}
