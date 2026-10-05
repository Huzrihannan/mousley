using System;
using System.Drawing;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mousely.Tray.Server;

namespace Mousely.Tray.Forms
{
    public class PairingForm : Form
    {
        private readonly int _port;
        private readonly PictureBox _qrBox;
        private readonly Label _urlLabel;
        private readonly Label _statusLabel;
        private readonly string _connectUrl;

        public PairingForm(int port)
        {
            _port = port;
            string localIp = UdpDiscoveryBeacon.GetLocalIpAddress();
            _connectUrl = $"http://{localIp}:{_port}";

            // Form Properties
            Text = "Mousely - Connect iPhone";
            Size = new Size(420, 520);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(248, 250, 252);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Header Panel
            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 70,
                BackColor = Color.White
            };

            var titleLabel = new Label
            {
                Text = "🎵 Mousely Media Remote",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(20, 14),
                AutoSize = true
            };

            var subtitleLabel = new Label
            {
                Text = "Connect your iPhone 7 Plus on the same Wi-Fi",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(22, 42),
                AutoSize = true
            };

            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(subtitleLabel);
            Controls.Add(headerPanel);

            // QR Code Container
            _qrBox = new PictureBox
            {
                Size = new Size(190, 190),
                Location = new Point(105, 90),
                SizeMode = PictureBoxSizeMode.CenterImage,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };
            Controls.Add(_qrBox);

            // URL Label
            _urlLabel = new Label
            {
                Text = _connectUrl,
                Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(79, 70, 229),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(20, 290),
                Size = new Size(365, 28)
            };
            Controls.Add(_urlLabel);

            // Instructions Box
            var instructionsLabel = new Label
            {
                Text = "1. Ensure iPhone is connected to the same Wi-Fi.\n2. Open Mousely on iPhone (auto-discovery will connect).\n3. Or scan the QR code / open the URL in Safari.",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(71, 85, 105),
                Location = new Point(35, 325),
                Size = new Size(340, 60),
                TextAlign = ContentAlignment.TopCenter
            };
            Controls.Add(instructionsLabel);

            // Buttons Row
            var copyBtn = new Button
            {
                Text = "Copy URL",
                Location = new Point(60, 400),
                Size = new Size(130, 36),
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(30, 41, 59),
                Cursor = Cursors.Hand
            };
            copyBtn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            copyBtn.Click += (s, e) =>
            {
                Clipboard.SetText(_connectUrl);
                MessageBox.Show("URL copied to clipboard!", "Mousely", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            Controls.Add(copyBtn);

            var openBrowserBtn = new Button
            {
                Text = "Open in Browser",
                Location = new Point(210, 400),
                Size = new Size(140, 36),
                BackColor = Color.FromArgb(79, 70, 229),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            openBrowserBtn.FlatAppearance.BorderSize = 0;
            openBrowserBtn.Click += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _connectUrl,
                        UseShellExecute = true
                    });
                }
                catch { }
            };
            Controls.Add(openBrowserBtn);

            _statusLabel = new Label
            {
                Text = "Ready for connection",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(20, 445),
                Size = new Size(365, 20)
            };
            Controls.Add(_statusLabel);

            // Load QR Code asynchronously
            _ = LoadQrCodeAsync();
        }

        private async Task LoadQrCodeAsync()
        {
            try
            {
                string qrApiUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=180x180&data={Uri.EscapeDataString(_connectUrl)}";
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
                var stream = await client.GetStreamAsync(qrApiUrl);
                _qrBox.Image = Image.FromStream(stream);
            }
            catch
            {
                // Fallback: draw placeholder text
                var bmp = new Bitmap(180, 180);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    using var font = new Font("Segoe UI", 9f);
                    using var brush = new SolidBrush(Color.FromArgb(100, 116, 139));
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString($"Open in Safari:\n{_connectUrl}", font, brush, new RectangleF(10, 10, 160, 160), sf);
                }
                _qrBox.Image = bmp;
            }
        }
    }
}
