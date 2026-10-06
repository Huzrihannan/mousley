using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Mousely.Tray.Media
{
    public class MediaState
    {
        public string Title { get; set; } = "No Media Playing";
        public string Artist { get; set; } = "Windows";
        public string Album { get; set; } = "";
        public string Source { get; set; } = "Windows";
        public bool IsPlaying { get; set; } = false;
        public double Position { get; set; } = 0;
        public double Duration { get; set; } = 0;
        public string? Artwork { get; set; } = null;
        public bool HasArtwork { get; set; } = false;
    }

    public class WindowsMediaManager : IDisposable
    {
        private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private GlobalSystemMediaTransportControlsSession? _currentSession;
        private readonly object _lock = new();
        private byte[]? _cachedArtworkBytes;
        private System.Threading.Timer? _pollTimer;

        public event Action<MediaState>? MediaStateChanged;

        public MediaState CurrentState { get; private set; } = new();
        public byte[]? CachedArtworkBytes => _cachedArtworkBytes;

        public async Task InitializeAsync()
        {
            try
            {
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_sessionManager != null)
                {
                    _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
                    AttachToCurrentSession();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WindowsMediaManager] Initialization failed: {ex.Message}");
            }

            _pollTimer = new System.Threading.Timer(_ => _ = RefreshMediaStateAsync(), null, 1500, 1500);
        }

        private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            AttachToCurrentSession();
        }

        private void AttachToCurrentSession()
        {
            lock (_lock)
            {
                DetachFromCurrentSession();

                if (_sessionManager == null) return;
                _currentSession = _sessionManager.GetCurrentSession();

                if (_currentSession != null)
                {
                    _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                    _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                    _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
                }
            }

            _ = RefreshMediaStateAsync();
        }

        private void DetachFromCurrentSession()
        {
            if (_currentSession != null)
            {
                try
                {
                    _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                    _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                    _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
                }
                catch { }
                _currentSession = null;
            }
        }

        private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            _ = RefreshMediaStateAsync();
        }

        private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            _ = RefreshMediaStateAsync();
        }

        private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            _ = RefreshMediaStateAsync();
        }

        public async Task<MediaState> RefreshMediaStateAsync()
        {
            var state = new MediaState();
            GlobalSystemMediaTransportControlsSession? session = null;

            if (_sessionManager != null)
            {
                try
                {
                    var sessions = _sessionManager.GetSessions();
                    if (sessions != null && sessions.Count > 0)
                    {
                        // Prioritize any session that is actively playing
                        foreach (var s in sessions)
                        {
                            try
                            {
                                var info = s.GetPlaybackInfo();
                                if (info != null && info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                                {
                                    session = s;
                                    break;
                                }
                            }
                            catch { }
                        }

                        if (session == null)
                        {
                            session = _sessionManager.GetCurrentSession() ?? sessions[0];
                        }
                    }
                    else
                    {
                        session = _sessionManager.GetCurrentSession();
                    }
                }
                catch
                {
                    session = _sessionManager.GetCurrentSession();
                }
            }

            lock (_lock)
            {
                if (session != null && session != _currentSession)
                {
                    DetachFromCurrentSession();
                    _currentSession = session;
                    try
                    {
                        _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                        _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                        _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
                    }
                    catch { }
                }
            }

            if (session != null)
            {
                try
                {
                    // Source application
                    state.Source = CleanAppId(session.SourceAppUserModelId);

                    // Media properties (Title, Artist, Album, Art)
                    var mediaProps = await session.TryGetMediaPropertiesAsync();
                    if (mediaProps != null)
                    {
                        state.Title = string.IsNullOrWhiteSpace(mediaProps.Title) ? "Unknown Track" : mediaProps.Title;
                        state.Artist = string.IsNullOrWhiteSpace(mediaProps.Artist) ? "Unknown Artist" : mediaProps.Artist;
                        state.Album = mediaProps.AlbumTitle ?? "";

                        // Extract Thumbnail Artwork
                        if (mediaProps.Thumbnail != null)
                        {
                            try
                            {
                                using var stream = await mediaProps.Thumbnail.OpenReadAsync();
                                if (stream != null && stream.Size > 0)
                                {
                                    using var memoryStream = new MemoryStream();
                                    using var netStream = stream.AsStreamForRead();
                                    await netStream.CopyToAsync(memoryStream);
                                    byte[] bytes = memoryStream.ToArray();
                                    _cachedArtworkBytes = bytes;
                                    state.Artwork = $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}";
                                    state.HasArtwork = true;
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[WindowsMediaManager] Artwork read note: {ex.Message}");
                            }
                        }
                    }

                    // Playback Info
                    var playbackInfo = session.GetPlaybackInfo();
                    if (playbackInfo != null)
                    {
                        state.IsPlaying = playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    }

                    // Timeline
                    var timeline = session.GetTimelineProperties();
                    if (timeline != null)
                    {
                        state.Position = timeline.Position.TotalSeconds;
                        state.Duration = timeline.EndTime.TotalSeconds;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WindowsMediaManager] Refresh error: {ex.Message}");
                }
            }
            else
            {
                state.Title = "No Active Media";
                state.Artist = "Ready for playback";
                state.Source = "Windows";
                state.IsPlaying = false;
                state.Position = 0;
                state.Duration = 0;
            }

            CurrentState = state;
            MediaStateChanged?.Invoke(state);
            return state;
        }

        private static string CleanAppId(string? appId)
        {
            if (string.IsNullOrEmpty(appId)) return "Windows";
            string lower = appId.ToLowerInvariant();
            if (lower.Contains("spotify")) return "Spotify";
            if (lower.Contains("chrome")) return "Google Chrome";
            if (lower.Contains("msedge")) return "Microsoft Edge";
            if (lower.Contains("firefox")) return "Firefox";
            if (lower.Contains("applemusic")) return "Apple Music";
            if (lower.Contains("itunes")) return "iTunes";
            if (lower.Contains("vlc")) return "VLC Media Player";
            if (lower.Contains("tidal")) return "TIDAL";
            return Path.GetFileNameWithoutExtension(appId);
        }

        public async Task TogglePlayPauseAsync()
        {
            bool handled = false;
            if (_currentSession != null)
            {
                try
                {
                    handled = await _currentSession.TryTogglePlayPauseAsync();
                }
                catch { }
            }

            if (!handled)
            {
                FallbackMediaKeys.PlayPause();
            }

            _ = Task.Delay(150).ContinueWith(_ => RefreshMediaStateAsync());
        }

        public async Task NextTrackAsync()
        {
            bool handled = false;
            if (_currentSession != null)
            {
                try
                {
                    handled = await _currentSession.TrySkipNextAsync();
                }
                catch { }
            }

            if (!handled)
            {
                FallbackMediaKeys.NextTrack();
            }

            _ = Task.Delay(250).ContinueWith(_ => RefreshMediaStateAsync());
        }

        public async Task PreviousTrackAsync()
        {
            bool handled = false;
            if (_currentSession != null)
            {
                try
                {
                    handled = await _currentSession.TrySkipPreviousAsync();
                }
                catch { }
            }

            if (!handled)
            {
                FallbackMediaKeys.PreviousTrack();
            }

            _ = Task.Delay(250).ContinueWith(_ => RefreshMediaStateAsync());
        }

        public async Task SeekAsync(double positionSeconds)
        {
            if (_currentSession != null)
            {
                try
                {
                    long ticks = (long)(positionSeconds * TimeSpan.TicksPerSecond);
                    await _currentSession.TryChangePlaybackPositionAsync(ticks);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WindowsMediaManager] Seek error: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
            DetachFromCurrentSession();
            if (_sessionManager != null)
            {
                try { _sessionManager.CurrentSessionChanged -= OnCurrentSessionChanged; } catch { }
            }
        }
    }
}
