using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace XNotch.Services;

public sealed record TimedLyricLine(TimeSpan Timestamp, string Text);

/// <summary>
/// High-performance synchronized lyrics engine connecting to LRCLIB with
/// fuzzy track title normalization, disk caching, and real-time playhead sync.
/// </summary>
public sealed class LyricsService : IDisposable
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XNotch", "LyricsCache");

    private static readonly Regex LrcTimeRegex = new(
        @"^\[(?<min>\d{1,2}):(?<sec>\d{2})(?:\.(?<ms>\d{2,3}))?\](?<text>.*)$",
        RegexOptions.Compiled);

    private static readonly Regex TitleCleanerRegex = new(
        @"\s*[-–]\s*(?:\d{4}\s+)?(?:Remaster(?:ed)?|Live|Deluxe|Radio Edit|Mono|Stereo|Anniversary|Edition).*|\s*\(feat\..*?\)|\s*\[.*?\]|\s*\(with.*?\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ConcurrentDictionary<string, List<TimedLyricLine>> _memoryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _instrumentalCache = new(StringComparer.OrdinalIgnoreCase);

    private string _currentTrackKey = string.Empty;
    private List<TimedLyricLine> _currentLines = [];
    private int _lastIndex = -1;
    private CancellationTokenSource? _activeCts;
    private bool _disposed;

    public bool HasSyncedLyrics => _currentLines.Count > 0;
    public IReadOnlyList<TimedLyricLine> CurrentLines => _currentLines;
    public bool IsInstrumental { get; private set; }
    public bool IsLoading { get; private set; }
    public string CurrentLine { get; private set; } = string.Empty;

    public event Action<string>? LineChanged;
    public event Action<bool>? LyricsLoaded;

    public LyricsService()
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("X-Notch/2.0 (Windows; Desktop Overlay)");
        }
        catch { }
    }

    /// <summary>
    /// Loads lyrics for a given track, checking memory cache, then local disk cache, then LRCLIB.
    /// </summary>
    public async Task LoadTrackLyricsAsync(string title, string artist, string album = "", double durationSeconds = 0)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            Clear();
            return;
        }

        string cleanTitle = CleanTrackTitle(title);
        string cleanArtist = CleanArtistName(artist);
        string trackKey = $"{cleanTitle}\x1f{cleanArtist}".ToLowerInvariant();

        if (trackKey == _currentTrackKey)
            return;

        _currentTrackKey = trackKey;
        _lastIndex = -1;
        CurrentLine = string.Empty;

        // Check memory cache first
        if (_instrumentalCache.TryGetValue(trackKey, out bool isInst) && isInst)
        {
            _currentLines = [];
            IsInstrumental = true;
            IsLoading = false;
            LyricsLoaded?.Invoke(false);
            return;
        }

        if (_memoryCache.TryGetValue(trackKey, out var cachedLines))
        {
            _currentLines = cachedLines;
            IsInstrumental = false;
            IsLoading = false;
            LyricsLoaded?.Invoke(true);
            return;
        }

        // Check local disk cache
        string hash = ComputeHash(trackKey);
        string diskPath = Path.Combine(CacheDir, $"{hash}.lrc");
        if (File.Exists(diskPath))
        {
            try
            {
                string rawLrc = await File.ReadAllTextAsync(diskPath);
                var parsed = ParseLrc(rawLrc);
                if (parsed.Count > 0)
                {
                    _currentLines = parsed;
                    _memoryCache[trackKey] = parsed;
                    IsInstrumental = false;
                    IsLoading = false;
                    LyricsLoaded?.Invoke(true);
                    return;
                }
            }
            catch { }
        }

        // Cancel previous pending fetch
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _activeCts = new CancellationTokenSource();
        var token = _activeCts.Token;

        IsLoading = true;
        _currentLines = [];
        IsInstrumental = false;

        _ = Task.Run(async () =>
        {
            try
            {
                var (syncedLyrics, instrumental) = await FetchFromLrclibAsync(cleanTitle, cleanArtist, album, durationSeconds, token);
                if (token.IsCancellationRequested) return;

                if (instrumental)
                {
                    IsInstrumental = true;
                    _instrumentalCache[trackKey] = true;
                    IsLoading = false;
                    LyricsLoaded?.Invoke(false);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(syncedLyrics))
                {
                    var lines = ParseLrc(syncedLyrics);
                    if (lines.Count > 0)
                    {
                        _currentLines = lines;
                        _memoryCache[trackKey] = lines;
                        IsInstrumental = false;
                        IsLoading = false;

                        // Save to disk cache
                        try { await File.WriteAllTextAsync(diskPath, syncedLyrics, CancellationToken.None); } catch { }

                        LyricsLoaded?.Invoke(true);
                        return;
                    }
                }

                // If not found or plain lyrics only
                IsLoading = false;
                LyricsLoaded?.Invoke(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Lyrics fetch error: {ex.Message}");
                IsLoading = false;
                LyricsLoaded?.Invoke(false);
            }
        }, token);
    }

    /// <summary>
    /// Evaluates current playhead and returns the active line.
    /// </summary>
    public string UpdatePlayhead(TimeSpan position, double offsetSeconds = 0.0)
    {
        if (_currentLines.Count == 0)
        {
            if (CurrentLine != string.Empty)
            {
                CurrentLine = string.Empty;
                LineChanged?.Invoke(string.Empty);
            }
            return string.Empty;
        }

        var targetTime = position.Add(TimeSpan.FromSeconds(offsetSeconds));
        int activeIndex = -1;

        // Binary search or sequential scan
        for (int i = 0; i < _currentLines.Count; i++)
        {
            if (_currentLines[i].Timestamp <= targetTime)
                activeIndex = i;
            else
                break;
        }

        if (activeIndex != _lastIndex)
        {
            _lastIndex = activeIndex;
            string text = activeIndex >= 0 ? _currentLines[activeIndex].Text.Trim() : string.Empty;
            CurrentLine = text;
            LineChanged?.Invoke(text);
        }

        return CurrentLine;
    }

    public void Clear()
    {
        _activeCts?.Cancel();
        _currentTrackKey = string.Empty;
        _currentLines = [];
        _lastIndex = -1;
        IsInstrumental = false;
        IsLoading = false;
        CurrentLine = string.Empty;
        LineChanged?.Invoke(string.Empty);
        LyricsLoaded?.Invoke(false);
    }

    private static async Task<(string? Synced, bool Instrumental)> FetchFromLrclibAsync(
        string title, string artist, string album, double duration, CancellationToken ct)
    {
        // 1. Direct GET
        string getUrl = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        if (!string.IsNullOrWhiteSpace(album)) getUrl += $"&album_name={Uri.EscapeDataString(album)}";
        if (duration > 0) getUrl += $"&duration={(int)Math.Round(duration)}";

        try
        {
            using var response = await HttpClient.GetAsync(getUrl, ct);
            if (response.IsSuccessStatusCode)
            {
                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                var root = doc.RootElement;
                bool isInst = root.TryGetProperty("instrumental", out var instProp) && instProp.GetBoolean();
                if (isInst) return (null, true);

                if (root.TryGetProperty("syncedLyrics", out var syncedProp) && syncedProp.ValueKind == JsonValueKind.String)
                {
                    string text = syncedProp.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(text))
                        return (text, false);
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }

        // 2. Loose search fallback
        string searchUrl = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(title + " " + artist)}";
        try
        {
            using var searchResp = await HttpClient.GetAsync(searchUrl, ct);
            if (searchResp.IsSuccessStatusCode)
            {
                using var doc = await JsonDocument.ParseAsync(await searchResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        bool isInst = item.TryGetProperty("instrumental", out var instProp) && instProp.GetBoolean();
                        if (isInst) return (null, true);

                        if (item.TryGetProperty("syncedLyrics", out var sProp) && sProp.ValueKind == JsonValueKind.String)
                        {
                            string sText = sProp.GetString() ?? "";
                            if (!string.IsNullOrWhiteSpace(sText))
                                return (sText, false);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }

        return (null, false);
    }

    public static List<TimedLyricLine> ParseLrc(string lrcContent)
    {
        var lines = new List<TimedLyricLine>();
        using var reader = new StringReader(lrcContent);
        string? rawLine;

        while ((rawLine = reader.ReadLine()) != null)
        {
            rawLine = rawLine.Trim();
            if (string.IsNullOrEmpty(rawLine)) continue;

            var match = LrcTimeRegex.Match(rawLine);
            if (match.Success)
            {
                int min = int.Parse(match.Groups["min"].Value);
                int sec = int.Parse(match.Groups["sec"].Value);
                int ms = 0;
                if (match.Groups["ms"].Success)
                {
                    string msStr = match.Groups["ms"].Value;
                    ms = msStr.Length == 2 ? int.Parse(msStr) * 10 : int.Parse(msStr);
                }

                var timestamp = new TimeSpan(0, 0, min, sec, ms);
                string text = match.Groups["text"].Value.Trim();
                lines.Add(new TimedLyricLine(timestamp, text));
            }
        }

        lines.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return lines;
    }

    public static string CleanTrackTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        string cleaned = TitleCleanerRegex.Replace(title, "").Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? title : cleaned;
    }

    public static string CleanArtistName(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return string.Empty;
        int commaIdx = artist.IndexOfAny([',', '&', '/']);
        if (commaIdx > 0)
            return artist[..commaIdx].Trim();
        return artist.Trim();
    }

    private static string ComputeHash(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _activeCts?.Cancel();
        _activeCts?.Dispose();
    }
}
