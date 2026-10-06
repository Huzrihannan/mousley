using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Mousely.Tray.Media;

namespace Mousely.Tray.Server
{
    public sealed class UdpInputServer : IDisposable
    {
        public const int DefaultPort = 58922;
        private readonly int _port;
        private Socket? _socket;
        private Thread? _workerThread;
        private volatile bool _isRunning;

        public UdpInputServer(int port = DefaultPort)
        {
            _port = port;
        }

        public void Start()
        {
            if (_isRunning) return;

            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _socket.ReceiveBufferSize = 65536;
                _socket.Bind(new IPEndPoint(IPAddress.Any, _port));

                _isRunning = true;

                _workerThread = new Thread(ListenLoop)
                {
                    Name = "Mousely_UdpInputListener",
                    IsBackground = true,
                    Priority = ThreadPriority.Highest
                };
                _workerThread.Start();

                Console.WriteLine($"[UdpInputServer] Real-time input listener active on UDP port {_port}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UdpInputServer] Failed to start on port {_port}: {ex.Message}");
            }
        }

        private void ListenLoop()
        {
            byte[] buffer = new byte[64];
            EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

            while (_isRunning && _socket != null)
            {
                try
                {
                    int bytesReceived = _socket.ReceiveFrom(buffer, ref remoteEp);
                    if (bytesReceived < 5) continue;

                    byte packetId = buffer[0];
                    if (packetId == 0x01) // High-frequency Mouse Move: [0x01, dx (int16 BE), dy (int16 BE)]
                    {
                        short dx = BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(1, 2));
                        short dy = BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(3, 2));
                        MouseInputManager.Move(dx, dy);
                    }
                    else if (packetId == 0x02) // Real-time Scroll: [0x02, deltaY (int16 BE), deltaX (int16 BE)]
                    {
                        short deltaY = BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(1, 2));
                        short deltaX = BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(3, 2));
                        MouseInputManager.Scroll(deltaY, deltaX);
                    }
                    else if (packetId == 0x03) // Ping probe -> Pong
                    {
                        _socket.SendTo(new byte[] { 0x03 }, remoteEp);
                    }
                }
                catch (SocketException) when (!_isRunning)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        Console.WriteLine($"[UdpInputServer] Receive error: {ex.Message}");
                    }
                }
            }
        }

        public void Dispose()
        {
            _isRunning = false;
            try
            {
                _socket?.Close();
                _socket?.Dispose();
            }
            catch { }
            _socket = null;
        }
    }
}
