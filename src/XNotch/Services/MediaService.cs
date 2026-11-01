using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;
using XNotch.Models;

namespace XNotch.Services;

/// <summary>
/// Controls Windows SMTC media playback with multi-session player switching,
/// iTunes artwork fallback, and synchronized lyrics coordination.
/// </summary>
public class MediaService : IDisposable
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private readonly List<GlobalSystemMediaTransportControlsSession> _sessions = [];
    private string? _pinnedAppUserModelId;
    private int _currentSessionIndex;

    private readonly DispatcherTimer _positionTimer;
    private DateTime _lastTimelineUpdate = DateTime.MinValue;
    private TimeSpan _basePosition = TimeSpan.Zero;
    private CancellationTokenSource? _artworkCts;

    public MediaInfo MediaInfo { get; } = new();
    public LyricsService? Lyrics { get; set; }

    public event Action? SessionChanged;

    public MediaService(LyricsService? lyrics = null)
    {
        Lyrics = lyrics;
        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _positionTimer.Tick += OnPositionTimerTick;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            _manager.SessionsChanged += OnSessionsChanged;

            RefreshSessions();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MediaService init failed: {ex.Message}");
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            RefreshSessions();
            SessionChanged?.Invoke();
        });
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            RefreshSessions();
            SessionChanged?.Invoke();
        });
    }

    public void RefreshSessions()
    {
        if (_manager == null) return;

        try
        {
            var rawSessions = _manager.GetSessions();
            _sessions.Clear();

            // Deduplicate by SourceAppUserModelId
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in rawSessions)
            {
                string id = s.SourceAppUserModelId ?? "";
                if (!string.IsNullOrEmpty(id) && seen.Add(id))
                {
                    _sessions.Add(s);
                }
            }

            MediaInfo.SessionCount = Math.Max(1, _sessions.Count);

            // Select active session: pinned, or current playing, or manager current
            GlobalSystemMediaTransportControlsSession? target = null;
            if (!string.IsNullOrEmpty(_pinnedAppUserModelId))
            {
                target = _sessions.FirstOrDefault(s => string.Equals(s.SourceAppUserModelId, _pinnedAppUserModelId, StringComparison.OrdinalIgnoreCase));
            }

            if (target == null)
            {
                target = _sessions.FirstOrDefault(s => s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                         ?? _manager.GetCurrentSession()
                         ?? _sessions.FirstOrDefault();
            }

            _currentSessionIndex = target != null ? _sessions.IndexOf(target) : 0;
            if (_currentSessionIndex < 0) _currentSessionIndex = 0;
            MediaInfo.SessionIndex = _currentSessionIndex;

            AttachSession(target);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RefreshSessions failed: {ex.Message}");
        }
    }

    public void NextSession()
    {
        if (_sessions.Count <= 1) return;
        _currentSessionIndex = (_currentSessionIndex + 1) % _sessions.Count;
        var next = _sessions[_currentSessionIndex];
        _pinnedAppUserModelId = next.SourceAppUserModelId;
        MediaInfo.SessionIndex = _currentSessionIndex;
        AttachSession(next);
        SessionChanged?.Invoke();
    }

    public void PreviousSession()
    {
        if (_sessions.Count <= 1) return;
        _currentSessionIndex = (_currentSessionIndex - 1 + _sessions.Count) % _sessions.Count;
        var prev = _sessions[_currentSessionIndex];
        _pinnedAppUserModelId = prev.SourceAppUserModelId;
        MediaInfo.SessionIndex = _currentSessionIndex;
        AttachSession(prev);
        SessionChanged?.Invoke();
    }

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelineChanged;
        }

        _session = session;

        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged += OnTimelineChanged;

            MediaInfo.HasMedia = true;
            MediaInfo.SourceAppTitle = FormatAppTitle(_session.SourceAppUserModelId);
            _ = UpdateMediaPropertiesAsync();
            UpdatePlaybackInfo();
            UpdateTimeline();
            _positionTimer.Start();
        }
        else
        {
            MediaInfo.HasMedia = false;
            MediaInfo.IsPlaying = false;
            MediaInfo.Title = string.Empty;
            MediaInfo.Artist = string.Empty;
            MediaInfo.AlbumTitle = string.Empty;
            MediaInfo.AlbumArt = null;
            MediaInfo.Position = TimeSpan.Zero;
            MediaInfo.Duration = TimeSpan.Zero;
            MediaInfo.LyricsText = string.Empty;
            MediaInfo.HasLyrics = false;
            MediaInfo.IsInstrumental = false;
            MediaInfo.SourceAppTitle = string.Empty;
            _positionTimer.Stop();
            Lyrics?.Clear();
        }
    }

    private static string FormatAppTitle(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return "Media";
        string lower = aumid.ToLowerInvariant();
        if (lower.Contains("spotify")) return "Spotify";
        if (lower.Contains("chrome")) return "Chrome";
        if (lower.Contains("edge")) return "Edge";
        if (lower.Contains("firefox")) return "Firefox";
        if (lower.Contains("vlc")) return "VLC";
        if (lower.Contains("apple")) return "Apple Music";
        return aumid.Split(['.', '\\', '_']).LastOrDefault() ?? "Media";
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        Application.Current?.Dispatcher.Invoke(() => _ = UpdateMediaPropertiesAsync());
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        Application.Current?.Dispatcher.Invoke(UpdatePlaybackInfo);
    }

    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        Application.Current?.Dispatcher.Invoke(UpdateTimeline);
    }

    private async Task UpdateMediaPropertiesAsync()
    {
        if (_session == null) return;

        try
        {
            var props = await _session.TryGetMediaPropertiesAsync();
            if (props == null) return;

            string title = props.Title ?? string.Empty;
            string artist = props.Artist ?? string.Empty;
            string album = props.AlbumTitle ?? string.Empty;

            MediaInfo.Title = title;
            MediaInfo.Artist = artist;
            MediaInfo.AlbumTitle = album;

            // Trigger Lyrics Fetch
            if (Lyrics != null && !string.IsNullOrWhiteSpace(title))
            {
                _ = Lyrics.LoadTrackLyricsAsync(title, artist, album, MediaInfo.Duration.TotalSeconds);
            }

            // Load album art
            if (props.Thumbnail != null)
            {
                try
                {
                    using var stream = await props.Thumbnail.OpenReadAsync();
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream.AsStreamForRead();
                    bitmap.DecodePixelWidth = 300;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    MediaInfo.AlbumArt = bitmap;
                    MediaInfo.DominantColor = ColorExtractionService.GetDominantColor(bitmap);
                }
                catch
                {
                    _ = FallbackItunesArtworkAsync(title, artist);
                }
            }
            else
            {
                _ = FallbackItunesArtworkAsync(title, artist);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UpdateMediaProperties failed: {ex.Message}");
        }
    }

    private async Task FallbackItunesArtworkAsync(string title, string artist)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            MediaInfo.AlbumArt = null;
            return;
        }

        _artworkCts?.Cancel();
        _artworkCts?.Dispose();
        _artworkCts = new CancellationTokenSource();
        var token = _artworkCts.Token;

        try
        {
            string cleanTitle = LyricsService.CleanTrackTitle(title);
            string cleanArtist = LyricsService.CleanArtistName(artist);
            string url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(cleanTitle + " " + cleanArtist)}&entity=song&limit=1";

            using var resp = await HttpClient.GetAsync(url, token);
            if (!resp.IsSuccessStatusCode) return;

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(token), cancellationToken: token);
            if (doc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
            {
                var first = results[0];
                if (first.TryGetProperty("artworkUrl100", out var artProp))
                {
                    string artUrl = artProp.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(artUrl))
                    {
                        // Request high-res 600x600 cover
                        string highResUrl = artUrl.Replace("100x100bb", "600x600bb");
                        var imgBytes = await HttpClient.GetByteArrayAsync(highResUrl, token);

                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (token.IsCancellationRequested) return;
                            using var ms = new MemoryStream(imgBytes);
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = ms;
                            bitmap.DecodePixelWidth = 300;
                            bitmap.EndInit();
                            bitmap.Freeze();

                            MediaInfo.AlbumArt = bitmap;
                            MediaInfo.DominantColor = ColorExtractionService.GetDominantColor(bitmap);
                        });
                    }
                }
            }
        }
        catch { }
    }

    private void UpdatePlaybackInfo()
    {
        if (_session == null) return;

        try
        {
            var info = _session.GetPlaybackInfo();
            if (info == null) return;

            bool wasPlaying = MediaInfo.IsPlaying;
            MediaInfo.IsPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            if (wasPlaying != MediaInfo.IsPlaying)
            {
                _lastTimelineUpdate = DateTime.MinValue;
                UpdateTimeline();
            }

            MediaInfo.IsShuffle = info.IsShuffleActive ?? false;
            MediaInfo.RepeatMode = info.AutoRepeatMode switch
            {
                Windows.Media.MediaPlaybackAutoRepeatMode.List => 1,
                Windows.Media.MediaPlaybackAutoRepeatMode.Track => 2,
                _ => 0
            };
        }
        catch { }
    }

    private void UpdateTimeline()
    {
        if (_session == null) return;

        try
        {
            var timeline = _session.GetTimelineProperties();
            if (timeline == null) return;

            _basePosition = timeline.Position;
            MediaInfo.Position = _basePosition;
            MediaInfo.Duration = timeline.EndTime;
            _lastTimelineUpdate = DateTime.UtcNow;
        }
        catch { }
    }

    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        if (_session == null || !MediaInfo.IsPlaying) return;

        if (_lastTimelineUpdate != DateTime.MinValue && MediaInfo.Duration.TotalSeconds > 0)
        {
            double elapsed = (DateTime.UtcNow - _lastTimelineUpdate).TotalSeconds;
            var interpolated = _basePosition + TimeSpan.FromSeconds(elapsed);
            MediaInfo.Position = interpolated <= MediaInfo.Duration ? interpolated : MediaInfo.Duration;

            // Sync with Lyrics
            if (Lyrics != null)
            {
                string line = Lyrics.UpdatePlayhead(MediaInfo.Position);
                MediaInfo.LyricsText = line;
                MediaInfo.HasLyrics = Lyrics.HasSyncedLyrics;
                MediaInfo.IsInstrumental = Lyrics.IsInstrumental;
            }
        }

        if ((DateTime.UtcNow - _lastTimelineUpdate).TotalSeconds >= 1.0)
        {
            UpdateTimeline();
        }
    }

    // Transport controls
    public async Task PlayPauseAsync()
    {
        if (_session == null) return;
        try { await _session.TryTogglePlayPauseAsync(); } catch { }
    }

    public async Task NextAsync()
    {
        if (_session == null) return;
        try { await _session.TrySkipNextAsync(); } catch { }
    }

    public async Task PreviousAsync()
    {
        if (_session == null) return;
        try { await _session.TrySkipPreviousAsync(); } catch { }
    }

    public async Task ToggleShuffleAsync()
    {
        if (_session == null) return;
        try { await _session.TryChangeShuffleActiveAsync(!MediaInfo.IsShuffle); } catch { }
    }

    public async Task CycleRepeatAsync()
    {
        if (_session == null) return;
        try
        {
            var next = MediaInfo.RepeatMode switch
            {
                0 => Windows.Media.MediaPlaybackAutoRepeatMode.List,
                1 => Windows.Media.MediaPlaybackAutoRepeatMode.Track,
                _ => Windows.Media.MediaPlaybackAutoRepeatMode.None
            };
            await _session.TryChangeAutoRepeatModeAsync(next);
        }
        catch { }
    }

    public async Task SeekToAsync(double percent)
    {
        if (_session == null || MediaInfo.Duration.TotalSeconds <= 0) return;
        try
        {
            var targetTicks = (long)(percent * MediaInfo.Duration.Ticks);
            await _session.TryChangePlaybackPositionAsync(targetTicks);
            _lastTimelineUpdate = DateTime.MinValue;
        }
        catch { }
    }

    public void Dispose()
    {
        _positionTimer.Stop();
        _artworkCts?.Cancel();
        _artworkCts?.Dispose();

        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelineChanged;
        }
        if (_manager != null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager.SessionsChanged -= OnSessionsChanged;
        }
    }
}
