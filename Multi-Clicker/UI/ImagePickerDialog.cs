using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MultiClicker.Properties;

namespace MultiClicker.UI
{
    /// <summary>
    /// Modal dialog showing every image in the cosmetics directory as a clickable grid.
    /// Thumbnails are decoded on a background thread so the UI never blocks while opening.
    /// Returns the selected file path via <see cref="SelectedImagePath"/>.
    /// </summary>
    public class ImagePickerDialog : Form
    {
        private const int ThumbSize = 64;
        private const int TileSize = 72;

        public string SelectedImagePath { get; private set; }

        private readonly string _cosmeticsDir;
        private readonly List<Image> _ownedImages = new List<Image>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private FlowLayoutPanel _flow;

        public ImagePickerDialog()
        {
            _cosmeticsDir = ResolveCosmeticsDirectory();

            Text = Strings.SelectBackgroundImage;
            BackColor = Color.FromArgb(36, 37, 38);
            ForeColor = Color.White;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(720, 520);
            MinimumSize = new Size(480, 360);
            ShowIcon = false;
            MaximizeBox = false;
            MinimizeBox = false;

            _flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(28, 29, 30),
                Padding = new Padding(8),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };
            Controls.Add(_flow);

            Load += (s, e) => BuildTilesAndLoadAsync();
            FormClosing += (s, e) => _cts.Cancel();
        }

        private static string ResolveCosmeticsDirectory()
        {
            var candidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cosmetics"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mandatory_assets", "cosmetics"),
                Path.Combine(Environment.CurrentDirectory, "cosmetics")
            };
            return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
        }

        private void BuildTilesAndLoadAsync()
        {
            if (!Directory.Exists(_cosmeticsDir))
            {
                _flow.Controls.Add(new Label
                {
                    Text = _cosmeticsDir,
                    ForeColor = Color.LightGray,
                    AutoSize = true,
                    Margin = new Padding(8)
                });
                return;
            }

            string[] files;
            try
            {
                files = Directory.EnumerateFiles(_cosmeticsDir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return;
            }

            // 1) Build all placeholder tiles synchronously in a single layout pass (cheap, ~ms).
            _flow.SuspendLayout();
            var tiles = new PictureBox[files.Length];
            for (int i = 0; i < files.Length; i++)
            {
                var file = files[i];
                var tile = new PictureBox
                {
                    Width = TileSize,
                    Height = TileSize,
                    BackColor = Color.FromArgb(44, 47, 51),
                    BorderStyle = BorderStyle.FixedSingle,
                    SizeMode = PictureBoxSizeMode.CenterImage,
                    Margin = new Padding(4),
                    Cursor = Cursors.Hand,
                    Tag = file
                };
                var toolTip = new ToolTip();
                toolTip.SetToolTip(tile, Path.GetFileName(file));
                tile.Click += Tile_Click;
                tiles[i] = tile;
                _flow.Controls.Add(tile);
            }
            _flow.ResumeLayout();

            // 2) Decode thumbnails on a worker thread, push them back via Invoke as they arrive.
            var token = _cts.Token;
            Task.Run(() =>
            {
                for (int i = 0; i < files.Length; i++)
                {
                    if (token.IsCancellationRequested) return;

                    Bitmap thumb = null;
                    try
                    {
                        using (var src = Image.FromFile(files[i]))
                            thumb = new Bitmap(src, new Size(ThumbSize, ThumbSize));
                    }
                    catch
                    {
                        continue;
                    }

                    if (token.IsCancellationRequested) { thumb.Dispose(); return; }

                    var index = i;
                    var bmp = thumb;
                    try
                    {
                        BeginInvoke((MethodInvoker)(() =>
                        {
                            if (IsDisposed || token.IsCancellationRequested) { bmp.Dispose(); return; }
                            var pb = tiles[index];
                            if (pb == null || pb.IsDisposed) { bmp.Dispose(); return; }
                            pb.Image = bmp;
                            _ownedImages.Add(bmp);
                        }));
                    }
                    catch
                    {
                        bmp.Dispose();
                        return;
                    }
                }
            }, token);
        }

        private void Tile_Click(object sender, EventArgs e)
        {
            SelectedImagePath = (string)((PictureBox)sender).Tag;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cts.Cancel();
                _cts.Dispose();
                foreach (var img in _ownedImages)
                {
                    try { img.Dispose(); } catch { }
                }
                _ownedImages.Clear();
            }
            base.Dispose(disposing);
        }
    }
}

