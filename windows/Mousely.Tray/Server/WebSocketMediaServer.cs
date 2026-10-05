using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
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
        
        private HttpListener? _httpListener;
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

            // Wire up callbacks for instant broadcasting
            _audioVolume.VolumeChanged += OnVolumeChanged;
            _mediaManager.MediaStateChanged += OnMediaStateChanged;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _httpListener = new HttpListener();

            try
            {
                // Try listening on all interfaces
                _httpListener.Prefixes.Add($"http://*:{_port}/");
                _httpListener.Start();
                Console.WriteLine($"[Server] Listening on http://*:{_port}/");
            }
            catch (HttpListenerException)
            {
                // Fallback to localhost and plus if admin permissions not elevated
                _httpListener = new HttpListener();
                try
                {
                    _httpListener.Prefixes.Add($"http://+:{_port}/");
                    _httpListener.Start();
                    Console.WriteLine($"[Server] Listening on http://+:{_port}/");
                }
                catch
                {
                    _httpListener = new HttpListener();
                    _httpListener.Prefixes.Add($"http://localhost:{_port}/");
                    _httpListener.Start();
                    Console.WriteLine($"[Server] Fallback listening on http://localhost:{_port}/");
                }
            }

            Task.Run(() => AcceptRequestsLoopAsync(_cts.Token));
        }

        private async Task AcceptRequestsLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _httpListener != null && _httpListener.IsListening)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();
                    _ = ProcessRequestContextAsync(context, token);
                }
                catch (HttpListenerException) when (token.IsCancellationRequested)
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

        private async Task ProcessRequestContextAsync(HttpListenerContext context, CancellationToken token)
        {
            try
            {
                var path = context.Request.Url?.AbsolutePath ?? "/";

                // WebSocket endpoint: /ws
                if (context.Request.IsWebSocketRequest && path == "/ws")
                {
                    var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
                    _ = HandleWebSocketConnectionAsync(wsContext.WebSocket, token);
                    return;
                }

                // API: Artwork
                if (path == "/api/artwork")
                {
                    var artBytes = _mediaManager.CachedArtworkBytes;
                    if (artBytes != null && artBytes.Length > 0)
                    {
                        context.Response.ContentType = "image/jpeg";
                        context.Response.ContentLength64 = artBytes.Length;
                        context.Response.Headers.Add("Cache-Control", "no-cache");
                        await context.Response.OutputStream.WriteAsync(artBytes, token);
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                    }
                    context.Response.Close();
                    return;
                }

                // API: Status JSON
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
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = jsonBytes.Length;
                    await context.Response.OutputStream.WriteAsync(jsonBytes, token);
                    context.Response.Close();
                    return;
                }

                // Serve Static Files from wwwroot
                await ServeStaticFileAsync(context, path);
            }
            catch (Exception ex)
            {
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch { }
                Console.WriteLine($"[Server] ProcessRequest error: {ex.Message}");
            }
        }

        private async Task ServeStaticFileAsync(HttpListenerContext context, string path)
        {
            if (path == "/" || string.IsNullOrEmpty(path))
            {
                path = "/index.html";
            }

            string relativePath = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.Combine(_wwwRootPath, relativePath);

            if (!File.Exists(fullPath))
            {
                // Fallback to index.html for SPA routing
                fullPath = Path.Combine(_wwwRootPath, "index.html");
            }

            if (File.Exists(fullPath))
            {
                context.Response.ContentType = GetContentType(fullPath);
                byte[] content = await File.ReadAllBytesAsync(fullPath);
                context.Response.ContentLength64 = content.Length;
                await context.Response.OutputStream.WriteAsync(content);
            }
            else
            {
                context.Response.StatusCode = 404;
            }

            context.Response.Close();
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
                ".gif" => "image/gif",
                ".woff2" => "font/woff2",
                _ => "application/octet-stream"
            };
        }

        // --- WebSocket Communication ---
        private async Task HandleWebSocketConnectionAsync(WebSocket ws, CancellationToken token)
        {
            var id = Guid.NewGuid();
            _clients.TryAdd(id, ws);
            ConnectedClientsChanged?.Invoke(_clients.Count);

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
                Console.WriteLine($"[Server] Client error: {ex.Message}");
            }
            finally
            {
                _clients.TryRemove(id, out _);
                ConnectedClientsChanged?.Invoke(_clients.Count);
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
                        ShortcutManager.LaunchSlot(slot, cmd);
                        break;

                    case "clipboard_action":
                        string clipType = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "" : "";
                        if (clipType == "copy") ShortcutManager.SendCopy();
                        else if (clipType == "paste") ShortcutManager.SendPaste();
                        else if (clipType == "history") ShortcutManager.SendClipboardHistory();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server] Command parsing error: {ex.Message}");
            }
        }

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
            string json = JsonSerializer.Serialize(obj);
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
            string json = JsonSerializer.Serialize(obj);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _httpListener?.Stop(); } catch { }
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
            try { _httpListener?.Close(); } catch { }
        }
    }
}
