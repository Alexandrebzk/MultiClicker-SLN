using System.Drawing;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using MultiClicker.Properties;

namespace MultiClicker
{
    public partial class ReplicateTextForm : Form
    {
        private System.Windows.Forms.TextBox textBox;
        private System.Windows.Forms.Button button;

        public string InputText => textBox.Text;
        public ReplicateTextForm()
        {
            InitializeComponent();
            this.Text = Strings.Menu;
            this.Shown += (sender, e) => this.Activate();
            textBox = new System.Windows.Forms.TextBox
            {
                Location = new Point(10, 10),
                Size = new Size(200, 20),
            };
            this.Controls.Add(textBox);

            button = new System.Windows.Forms.Button
            {
                Location = new Point(10, 40),
                Text = Strings.Validate,
                DialogResult = DialogResult.OK
            };
            button.Click += (sender, e) => this.Close();
            this.Controls.Add(button);

            this.AcceptButton = button; // Set the button to be clicked when Enter is pressed

            this.ClientSize = new Size(textBox.Width + 20, textBox.Height + button.Height + 30);

            this.StartPosition = FormStartPosition.CenterScreen;
            // Handle the KeyDown event of the form
            this.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    this.Close();
                }
            };
        }
    }
}
