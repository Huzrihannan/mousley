using System;
using System.Collections.Generic;
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

                Console.WriteLine($"[Discovery] UDP beacon active on port {DiscoveryPort}");

                // Broadcaster loop (every 1.5 seconds for instant detection)
                Task.Run(() => BroadcastLoopAsync(_cts.Token));

                // Receiver/responder loop (instant replies to MOUSELY_DISCOVER queries)
                Task.Run(() => ReceiveLoopAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Discovery] Failed to bind UDP port {DiscoveryPort}: {ex.Message}");
            }
        }

        private async Task BroadcastLoopAsync(CancellationToken token)
        {
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
                        udpPort = 58922,
                        version = "1.1.5"
                    };

                    byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
                    var targets = GetBroadcastAddresses();

                    foreach (var target in targets)
                    {
                        try
                        {
                            var endpoint = new IPEndPoint(target, DiscoveryPort);
                            await _udpClient.SendAsync(bytes, bytes.Length, endpoint);
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        Console.WriteLine($"[Discovery] Broadcast note: {ex.Message}");
                    }
                }

                try
                {
                    await Task.Delay(1500, token);
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
                            udpPort = 58922,
                            version = "1.1.5"
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

        public static List<IPAddress> GetBroadcastAddresses()
        {
            var list = new List<IPAddress> { IPAddress.Broadcast };
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
                    foreach (var u in ipProps.UnicastAddresses)
                    {
                        if (u.Address.AddressFamily == AddressFamily.InterNetwork && u.IPv4Mask != null)
                        {
                            byte[] ipBytes = u.Address.GetAddressBytes();
                            byte[] maskBytes = u.IPv4Mask.GetAddressBytes();
                            byte[] broadcastBytes = new byte[ipBytes.Length];
                            for (int i = 0; i < ipBytes.Length; i++)
                            {
                                broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                            }
                            list.Add(new IPAddress(broadcastBytes));
                        }
                    }
                }
            }
            catch { }

            return list.Distinct().ToList();
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
