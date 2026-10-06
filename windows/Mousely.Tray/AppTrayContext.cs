using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;
using Mousely.Tray.Audio;
using Mousely.Tray.Forms;
using Mousely.Tray.Media;
using Mousely.Tray.Server;

namespace Mousely.Tray
{
    public class AppTrayContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _clientsItem;
        private readonly ToolStripMenuItem _autostartItem;

        private readonly CoreAudioVolume _audioVolume;
        private readonly WindowsMediaManager _mediaManager;
        private readonly WebSocketMediaServer _server;
        private readonly UdpDiscoveryBeacon _beacon;
        private readonly UdpInputServer _udpInputServer;

        private PairingForm? _pairingForm;
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegistryName = "MouselyMediaRemote";

        public AppTrayContext()
        {
            const int port = 58920;
            const int udpInputPort = 58922;
            string wwwRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");

            // Initialize Core Components
            _audioVolume = new CoreAudioVolume();
            _mediaManager = new WindowsMediaManager();
            _ = _mediaManager.InitializeAsync();

            _server = new WebSocketMediaServer(port, wwwRoot, _audioVolume, _mediaManager);
            _server.Start();

            _beacon = new UdpDiscoveryBeacon(port);
            _beacon.Start();

            _udpInputServer = new UdpInputServer(udpInputPort);
            _udpInputServer.Start();

            // Setup Context Menu
            _contextMenu = new ContextMenuStrip();

            var titleItem = new ToolStripMenuItem("🎵 Mousely Remote")
            {
                Enabled = false,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            _contextMenu.Items.Add(titleItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            string localIp = UdpDiscoveryBeacon.GetLocalIpAddress();
            _statusItem = new ToolStripMenuItem($"🌐 Running on {localIp}:{port}")
            {
                Enabled = false
            };
            _contextMenu.Items.Add(_statusItem);

            _clientsItem = new ToolStripMenuItem("📱 Connected: 0")
            {
                Enabled = false
            };
            _contextMenu.Items.Add(_clientsItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            var pairItem = new ToolStripMenuItem("📱 Pair iPhone / Show QR Code", null, OnPairClicked);
            _contextMenu.Items.Add(pairItem);

            var openBrowserItem = new ToolStripMenuItem("🌐 Open Web Controller in Browser", null, OnOpenBrowserClicked);
            _contextMenu.Items.Add(openBrowserItem);

            string gamepadModeStr = GamepadManager.IsViGEmActive ? "🎮 Gamepad: Xbox 360 (ViGEm)" : "🎮 Gamepad: SendInput Hardware Active";
            var gamepadItem = new ToolStripMenuItem(gamepadModeStr, null, OnGamepadInfoClicked);
            _contextMenu.Items.Add(gamepadItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            _autostartItem = new ToolStripMenuItem("⚙️ Start with Windows", null, OnToggleAutostartClicked)
            {
                Checked = IsAutostartEnabled()
            };
            _contextMenu.Items.Add(_autostartItem);

            var exitItem = new ToolStripMenuItem("❌ Exit", null, OnExitClicked);
            _contextMenu.Items.Add(exitItem);

            // Setup System Tray Icon
            _trayIcon = new NotifyIcon
            {
                Icon = CreateTrayIcon(),
                ContextMenuStrip = _contextMenu,
                Text = $"Mousely Remote ({localIp}:{port})",
                Visible = true
            };
            _trayIcon.DoubleClick += (s, e) => ShowPairingDialog();

            // Wire up live client count update
            _server.ConnectedClientsChanged += OnConnectedClientsChanged;
        }

        private void OnConnectedClientsChanged(int count)
        {
            if (_trayIcon.ContextMenuStrip?.InvokeRequired == true)
            {
                _trayIcon.ContextMenuStrip.BeginInvoke(new Action(() => OnConnectedClientsChanged(count)));
                return;
            }

            _clientsItem.Text = $"📱 Connected: {count}";
            _trayIcon.Text = $"Mousely Remote - {count} device{(count == 1 ? "" : "s")}";
        }

        private void OnPairClicked(object? sender, EventArgs e)
        {
            ShowPairingDialog();
        }

        private void ShowPairingDialog()
        {
            if (_pairingForm == null || _pairingForm.IsDisposed)
            {
                _pairingForm = new PairingForm(_server.Port);
            }
            _pairingForm.Show();
            _pairingForm.BringToFront();
        }

        private void OnOpenBrowserClicked(object? sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = $"http://localhost:{_server.Port}",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void OnGamepadInfoClicked(object? sender, EventArgs e)
        {
            if (GamepadManager.IsViGEmActive)
            {
                MessageBox.Show(
                    "ViGEmBus driver is ACTIVE!\n\nMousely is providing a full virtual Xbox 360 controller natively recognized by Steam, Xbox App, Forza, GTA, EA Sports, and all PC games.",
                    "Mousely - Gamepad Emulation",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                var result = MessageBox.Show(
                    "Mousely Gamepad is operating in DirectInput Hardware mode (SendInput WASD + Space/Shift/Esc/Arrows/PWM Steering) for 100% out-of-the-box compatibility with all games.\n\nWould you like to install the free ViGEmBus driver to enable native virtual Xbox 360 controller emulation for Steam and Xbox games?",
                    "Mousely - Gamepad Emulation",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "https://github.com/nefarius/ViGEmBus/releases",
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
            }
        }

        private void OnToggleAutostartClicked(object? sender, EventArgs e)
        {
            bool current = IsAutostartEnabled();
            SetAutostart(!current);
            _autostartItem.Checked = !current;
        }

        private static bool IsAutostartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
                return key?.GetValue(AppRegistryName) != null;
            }
            catch
            {
                return false;
            }
        }

        private static void SetAutostart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                if (key == null) return;

                if (enable)
                {
                    string exePath = Application.ExecutablePath;
                    key.SetValue(AppRegistryName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(AppRegistryName, false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not update startup registry: {ex.Message}", "Mousely", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnExitClicked(object? sender, EventArgs e)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();

            _udpInputServer.Dispose();
            _beacon.Dispose();
            _server.Dispose();
            _mediaManager.Dispose();
            _audioVolume.Dispose();
            GamepadManager.Shutdown();

            ExitThread();
        }

        // Dynamically creates a crisp, stylish system tray icon
        private static Icon CreateTrayIcon()
        {
            string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            if (File.Exists(icoPath))
            {
                try { return new Icon(icoPath); } catch { }
            }

            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // Gradient background circle
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(2, 2, 28, 28);
                    using var brush = new LinearGradientBrush(new Point(0, 0), new Point(32, 32),
                        Color.FromArgb(99, 102, 241), Color.FromArgb(79, 70, 229));
                    g.FillPath(brush, path);
                }

                // Inner white music note
                using (var pen = new Pen(Color.White, 2.5f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;

                    // Stem
                    g.DrawLine(pen, 20, 8, 20, 20);
                    // Beam
                    g.DrawLine(pen, 13, 11, 20, 8);
                    // Second stem
                    g.DrawLine(pen, 13, 11, 13, 22);
                }

                using (var brush = new SolidBrush(Color.White))
                {
                    g.FillEllipse(brush, 9, 19, 6, 5);
                    g.FillEllipse(brush, 16, 17, 6, 5);
                }
            }

            IntPtr hIcon = bmp.GetHicon();
            return Icon.FromHandle(hIcon);
        }
    }
}
