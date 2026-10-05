using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mousely.Tray.Server
{
    public class UdpDiscoveryBeacon : IDisposable
    {
        public const int DiscoveryPort = 58921;
        private readonly int _serverPort;
        private UdpClient? _udpClient;
        private CancellationTokenSource? _cts;
        private bool _isDisposed;

        public UdpDiscoveryBeacon(int serverPort)
        {
            _serverPort = serverPort;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();

            try
            {
                _udpClient = new UdpClient();
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.EnableBroadcast = true;
                _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

                Console.WriteLine($"[Discovery] UDP beacon listening & broadcasting on port {DiscoveryPort}");

                // Start broadcaster loop
                Task.Run(() => BroadcastLoopAsync(_cts.Token));

                // Start receiver/responder loop
                Task.Run(() => ReceiveLoopAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Discovery] Failed to bind UDP port {DiscoveryPort}: {ex.Message}");
            }
        }

        private async Task BroadcastLoopAsync(CancellationToken token)
        {
            var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);

            while (!token.IsCancellationRequested && _udpClient != null)
            {
                try
                {
                    string primaryIp = GetLocalIpAddress();
                    var payload = new
                    {
                        app = "Mousely",
                        name = Environment.MachineName,
                        ip = primaryIp,
                        port = _serverPort,
                        version = "1.0.0"
                    };

                    byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
                    await _udpClient.SendAsync(bytes, bytes.Length, broadcastEndpoint);
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        Console.WriteLine($"[Discovery] Broadcast error: {ex.Message}");
                    }
                }

                try
                {
                    await Task.Delay(3000, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _udpClient != null)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync(token);
                    string message = Encoding.UTF8.GetString(result.Buffer);

                    if (message.Contains("MOUSELY_DISCOVER") || message.Contains("DISCOVER"))
                    {
                        string primaryIp = GetLocalIpAddress();
                        var response = new
                        {
                            app = "Mousely",
                            name = Environment.MachineName,
                            ip = primaryIp,
                            port = _serverPort,
                            version = "1.0.0"
                        };
                        byte[] responseBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response));
                        await _udpClient.SendAsync(responseBytes, responseBytes.Length, result.RemoteEndPoint);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        Console.WriteLine($"[Discovery] Receive error: {ex.Message}");
                    }
                }
            }
        }

        public static string GetLocalIpAddress()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                 !ni.Description.ToLowerInvariant().Contains("virtual") &&
                                 !ni.Description.ToLowerInvariant().Contains("pseudo"));

                foreach (var ni in interfaces)
                {
                    var ipProps = ni.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string ipStr = addr.Address.ToString();
                            if (!ipStr.StartsWith("127.") && !ipStr.StartsWith("169.254."))
                            {
                                return ipStr;
                            }
                        }
                    }
                }
            }
            catch { }

            return "127.0.0.1";
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _cts?.Cancel();
            try { _udpClient?.Close(); } catch { }
            try { _udpClient?.Dispose(); } catch { }
        }
    }
}
