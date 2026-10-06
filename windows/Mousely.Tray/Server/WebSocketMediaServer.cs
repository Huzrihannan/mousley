using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Mousely.Tray.Audio;
using Mousely.Tray.Media;

namespace Mousely.Tray.Server
{
    public class WebSocketMediaServer : IDisposable
    {
        private readonly int _port;
        private readonly string _wwwRootPath;
        private readonly CoreAudioVolume _audioVolume;
        private readonly WindowsMediaManager _mediaManager;

        private TcpListener? _tcpListener;
        private CancellationTokenSource? _cts;
        private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();

        public event Action<int>? ConnectedClientsChanged;

        public int ConnectedClientsCount => _clients.Count;
        public int Port => _port;

        public WebSocketMediaServer(int port, string wwwRootPath, CoreAudioVolume audioVolume, WindowsMediaManager mediaManager)
        {
            _port = port;
            _wwwRootPath = wwwRootPath;
            _audioVolume = audioVolume;
            _mediaManager = mediaManager;

            _audioVolume.VolumeChanged += OnVolumeChanged;
            _mediaManager.MediaStateChanged += OnMediaStateChanged;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();

            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, _port);
                _tcpListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _tcpListener.Start();
                Console.WriteLine($"[Server] TcpListener active on 0.0.0.0:{_port} (Non-Admin / Direct Sockets)");

                Task.Run(() => AcceptTcpClientsLoopAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server] Failed to bind TcpListener on port {_port}: {ex.Message}");
            }
        }

        private async Task AcceptTcpClientsLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _tcpListener != null)
            {
                try
                {
                    var tcpClient = await _tcpListener.AcceptTcpClientAsync(token);
                    _ = HandleIncomingClientAsync(tcpClient, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        Console.WriteLine($"[Server] Accept error: {ex.Message}");
                    }
                }
            }
        }

        private async Task HandleIncomingClientAsync(TcpClient client, CancellationToken token)
        {
            client.NoDelay = true;
            var stream = client.GetStream();

            try
            {
                // Read HTTP request header
                var headerBuffer = new byte[8192];
                int bytesRead = await stream.ReadAsync(headerBuffer, 0, headerBuffer.Length, token);
                if (bytesRead <= 0)
                {
                    client.Close();
                    return;
                }

                string requestString = Encoding.UTF8.GetString(headerBuffer, 0, bytesRead);
                string[] lines = requestString.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                if (lines.Length == 0)
                {
                    client.Close();
                    return;
                }

                string requestLine = lines[0];
                var parts = requestLine.Split(' ');
                if (parts.Length < 2)
                {
                    client.Close();
                    return;
                }

                string method = parts[0];
                string path = parts[1].Split('?')[0];

                // Check for WebSocket Upgrade
                bool isWebSocket = false;
                string? secKey = null;

                foreach (var line in lines)
                {
                    if (line.StartsWith("Upgrade:", StringComparison.OrdinalIgnoreCase) && line.Contains("websocket", StringComparison.OrdinalIgnoreCase))
                    {
                        isWebSocket = true;
                    }
                    if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                    {
                        secKey = line.Substring("Sec-WebSocket-Key:".Length).Trim();
                    }
                }

                if (isWebSocket && !string.IsNullOrEmpty(secKey))
                {
                    // Compute WebSocket Accept Hash
                    string acceptKey = ComputeWebSocketAcceptKey(secKey);
                    string response = "HTTP/1.1 101 Switching Protocols\r\n" +
                                      "Upgrade: websocket\r\n" +
                                      "Connection: Upgrade\r\n" +
                                      $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";

                    byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                    await stream.WriteAsync(responseBytes, 0, responseBytes.Length, token);
                    await stream.FlushAsync(token);

                    // Upgrade to WebSocket
                    var ws = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromSeconds(30));
                    await HandleWebSocketSessionAsync(ws, token);
                    return;
                }

                // Handle HTTP Requests
                await HandleHttpRequestAsync(stream, method, path, token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server] Client handling note: {ex.Message}");
            }
            finally
            {
                try { client.Dispose(); } catch { }
            }
        }

        private static string ComputeWebSocketAcceptKey(string secKey)
        {
            const string magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(secKey + magic));
            return Convert.ToBase64String(hash);
        }

        private async Task HandleHttpRequestAsync(NetworkStream stream, string method, string path, CancellationToken token)
        {
            try
            {
                if (path == "/api/status")
                {
                    var stateObj = new
                    {
                        type = "state",
                        deviceName = Environment.MachineName,
                        volume = (int)Math.Round(_audioVolume.GetMasterVolume() * 100),
                        muted = _audioVolume.GetMute(),
                        media = _mediaManager.CurrentState
                    };
                    string json = JsonSerializer.Serialize(stateObj);
                    byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
                    await WriteHttpResponseAsync(stream, 200, "OK", "application/json", jsonBytes, token);
                    return;
                }

                if (path == "/api/artwork")
                {
                    var artBytes = _mediaManager.CachedArtworkBytes;
                    if (artBytes != null && artBytes.Length > 0)
                    {
                        await WriteHttpResponseAsync(stream, 200, "OK", "image/jpeg", artBytes, token);
                    }
                    else
                    {
                        await WriteHttpResponseAsync(stream, 404, "Not Found", "text/plain", Array.Empty<byte>(), token);
                    }
                    return;
                }

                // Static File Serving
                if (path == "/" || string.IsNullOrEmpty(path)) path = "/index.html";

                string relativePath = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                string fullPath = Path.Combine(_wwwRootPath, relativePath);

                if (!File.Exists(fullPath))
                {
                    fullPath = Path.Combine(_wwwRootPath, "index.html");
                }

                if (File.Exists(fullPath))
                {
                    byte[] content = await File.ReadAllBytesAsync(fullPath, token);
                    string contentType = GetContentType(fullPath);
                    await WriteHttpResponseAsync(stream, 200, "OK", contentType, content, token);
                }
                else
                {
                    await WriteHttpResponseAsync(stream, 404, "Not Found", "text/plain", Encoding.UTF8.GetBytes("Not Found"), token);
                }
            }
            catch { }
        }

        private static async Task WriteHttpResponseAsync(NetworkStream stream, int statusCode, string statusMsg, string contentType, byte[] body, CancellationToken token)
        {
            string header = $"HTTP/1.1 {statusCode} {statusMsg}\r\n" +
                            $"Content-Type: {contentType}\r\n" +
                            $"Content-Length: {body.Length}\r\n" +
                            "Access-Control-Allow-Origin: *\r\n" +
                            "Connection: close\r\n\r\n";

            byte[] headerBytes = Encoding.UTF8.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, token);
            if (body.Length > 0)
            {
                await stream.WriteAsync(body, 0, body.Length, token);
            }
            await stream.FlushAsync(token);
        }

        private static string GetContentType(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".html" or ".htm" => "text/html; charset=utf-8",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".json" => "application/json",
                ".svg" => "image/svg+xml",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".ico" => "image/x-icon",
                _ => "application/octet-stream"
            };
        }

        // --- WebSocket Session Management ---
        private async Task HandleWebSocketSessionAsync(WebSocket ws, CancellationToken token)
        {
            var id = Guid.NewGuid();
            _clients.TryAdd(id, ws);
            ConnectedClientsChanged?.Invoke(_clients.Count);
            Console.WriteLine($"[Server] iPhone connected! Active clients: {_clients.Count}");

            try
            {
                // Send Initial State Payload
                var initPayload = new
                {
                    type = "init",
                    deviceName = Environment.MachineName,
                    volume = (int)Math.Round(_audioVolume.GetMasterVolume() * 100),
                    muted = _audioVolume.GetMute(),
                    media = _mediaManager.CurrentState
                };
                await SendJsonAsync(ws, initPayload, token);

                var buffer = new byte[4096];
                while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", token);
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        await HandleClientCommandAsync(ws, message, token);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server] Session note: {ex.Message}");
            }
            finally
            {
                _clients.TryRemove(id, out _);
                ConnectedClientsChanged?.Invoke(_clients.Count);
                Console.WriteLine($"[Server] Client disconnected. Active clients: {_clients.Count}");
                try { ws.Dispose(); } catch { }
            }
        }

        private async Task HandleClientCommandAsync(WebSocket ws, string jsonString, CancellationToken token)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonString);
                var root = doc.RootElement;
                if (!root.TryGetProperty("action", out var actionProp)) return;

                string action = actionProp.GetString() ?? "";

                switch (action)
                {
                    case "ping":
                        await SendJsonAsync(ws, new { type = "pong" }, token);
                        break;

                    case "set_volume":
                        if (root.TryGetProperty("value", out var valProp))
                        {
                            int val = valProp.GetInt32();
                            _audioVolume.SetMasterVolume(val / 100.0f);
                        }
                        break;

                    case "toggle_mute":
                        _audioVolume.ToggleMute();
                        break;

                    case "play_pause":
                        await _mediaManager.TogglePlayPauseAsync();
                        break;

                    case "next":
                        await _mediaManager.NextTrackAsync();
                        break;

                    case "prev":
                        await _mediaManager.PreviousTrackAsync();
                        break;

                    case "seek":
                        if (root.TryGetProperty("position", out var posProp))
                        {
                            double pos = posProp.GetDouble();
                            await _mediaManager.SeekAsync(pos);
                        }
                        break;

                    case "launch_app":
                        int slot = root.TryGetProperty("slot", out var slotProp) ? slotProp.GetInt32() : 1;
                        string? cmd = root.TryGetProperty("command", out var cmdProp) ? cmdProp.GetString() : null;
                        bool launched = ShortcutManager.LaunchSlot(slot, cmd);
                        await SendJsonAsync(ws, new { type = "action_ack", action = "launch_app", slot = slot, success = launched }, token);
                        break;

                    case "clipboard_action":
                        string clipType = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "" : "";
                        if (clipType == "copy") ShortcutManager.SendCopy();
                        else if (clipType == "paste") ShortcutManager.SendPaste();
                        else if (clipType == "history") ShortcutManager.SendClipboardHistory();
                        else if (clipType == "screenshot") ShortcutManager.SendScreenshot();
                        await SendJsonAsync(ws, new { type = "action_ack", action = "clipboard_action", clipType = clipType, success = true }, token);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server] Command parsing error: {ex.Message}");
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private void OnVolumeChanged(float volume, bool isMuted)
        {
            var payload = new
            {
                type = "volume",
                value = (int)Math.Round(volume * 100),
                muted = isMuted
            };
            _ = BroadcastJsonAsync(payload);
        }

        private void OnMediaStateChanged(MediaState state)
        {
            var payload = new
            {
                type = "media",
                media = state
            };
            _ = BroadcastJsonAsync(payload);
        }

        private async Task BroadcastJsonAsync(object obj)
        {
            string json = JsonSerializer.Serialize(obj, JsonOptions);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);

            foreach (var kvp in _clients)
            {
                var ws = kvp.Value;
                if (ws.State == WebSocketState.Open)
                {
                    try
                    {
                        await ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch { }
                }
            }
        }

        private static async Task SendJsonAsync(WebSocket ws, object obj, CancellationToken token)
        {
            if (ws.State != WebSocketState.Open) return;
            string json = JsonSerializer.Serialize(obj, JsonOptions);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _tcpListener?.Stop(); } catch { }
            foreach (var kvp in _clients)
            {
                try { kvp.Value.Dispose(); } catch { }
            }
            _clients.Clear();
        }

        public void Dispose()
        {
            Stop();
            _audioVolume.VolumeChanged -= OnVolumeChanged;
            _mediaManager.MediaStateChanged -= OnMediaStateChanged;
        }
    }
}
