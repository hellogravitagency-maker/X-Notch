using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Threading;
using XNotch.Models;

namespace XNotch.Services;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PetKind { Cat, Dog, Bunny }

public enum PetAction { Sit, Walk, Look, Sleep }

public enum PingQuality { Good, Fair, Bad, Lost }

/// <summary>
/// Data behind the Widgets card. Network data is cached with a TTL and fetched only
/// while the card is visible (the view calls <see cref="RefreshAsync"/>). Screen time
/// accumulates in the background from input idleness.
/// </summary>
public sealed class WidgetsService : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly DispatcherTimer _screenTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private AppSettings _settings = new();
    private DateTime _weatherAt, _fxAt, _pingAt;

    public double? TemperatureC { get; private set; }
    public int? WeatherCode { get; private set; }
    public int? UsAqi { get; private set; }
    public double? FxRate { get; private set; }
    public long? PingMs { get; private set; }
    public PingQuality Ping { get; private set; } = PingQuality.Lost;

    public event Action? Changed;

    public void Start(AppSettings settings)
    {
        _settings = settings;
        _screenTimer.Tick -= OnScreenTick;
        _screenTimer.Tick += OnScreenTick;
        _screenTimer.Start();
    }

    public void UpdateSettings(AppSettings settings)
    {
        bool cityChanged = !string.Equals(settings.WeatherCity, _settings.WeatherCity, StringComparison.OrdinalIgnoreCase);
        bool fxChanged = settings.CurrencyPair != _settings.CurrencyPair;
        _settings = settings;
        if (cityChanged) { settings.WeatherLatitude = settings.WeatherLongitude = null; _weatherAt = default; }
        if (fxChanged) _fxAt = default;
    }

    // ── Screen time: counts 30 s slices in which you touched the keyboard or mouse ──
    private void OnScreenTick(object? sender, EventArgs e)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (_settings.ScreenTimeDate != today) { _settings.ScreenTimeDate = today; _settings.ScreenTimeSeconds = 0; }
        if (IdleTime() < TimeSpan.FromSeconds(60)) _settings.ScreenTimeSeconds += 30;
        _settings.Save(); // debounced by AppSettings
    }

    public TimeSpan ScreenTimeToday =>
        _settings.ScreenTimeDate == DateTime.Today.ToString("yyyy-MM-dd") ? TimeSpan.FromSeconds(_settings.ScreenTimeSeconds) : TimeSpan.Zero;

    public static TimeSpan IdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime)) : TimeSpan.Zero;
    }

    // ── Pet: reacts to how long you've been away ──
    public static PetAction PetFor(TimeSpan idle, DateTime now) =>
        idle > TimeSpan.FromMinutes(5) ? PetAction.Sleep
        : idle > TimeSpan.FromMinutes(1) ? PetAction.Sit
        : now.Second % 20 < 3 ? PetAction.Look : PetAction.Walk;

    // ── Countdown & world clock (pure, no network) ──
    public static string? CountdownText(string? isoDate, DateTime now)
    {
        if (!DateTime.TryParse(isoDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var target)) return null;
        var days = (target.Date - now.Date).TotalDays;
        return days switch { 0 => "today", 1 => "tomorrow", < 0 => $"{-days:0} days ago", _ => $"{days:0} days" };
    }

    /// <summary>Finds a Windows time zone by id or by a city in its display name ("Tokyo").</summary>
    public static TimeZoneInfo? FindZone(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        try { return TimeZoneInfo.FindSystemTimeZoneById(query); } catch { }
        return TimeZoneInfo.GetSystemTimeZones().FirstOrDefault(z =>
            z.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) || z.StandardName.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    // ── Network-backed tiles ──
    public async Task RefreshAsync()
    {
        var now = DateTime.Now;
        var jobs = new System.Collections.Generic.List<Task>();
        if (now - _pingAt > TimeSpan.FromSeconds(10)) { _pingAt = now; jobs.Add(PingAsync()); }
        if (now - _weatherAt > TimeSpan.FromMinutes(30) && !string.IsNullOrWhiteSpace(_settings.WeatherCity)) { _weatherAt = now; jobs.Add(WeatherAsync()); }
        if (now - _fxAt > TimeSpan.FromHours(1) && ParsePair(_settings.CurrencyPair) is not null) { _fxAt = now; jobs.Add(FxAsync()); }
        if (jobs.Count == 0) return;
        await Task.WhenAll(jobs);
        Changed?.Invoke();
    }

    private async Task PingAsync()
    {
        try
        {
            using var p = new Ping();
            var reply = await p.SendPingAsync(string.IsNullOrWhiteSpace(_settings.PingHost) ? "1.1.1.1" : _settings.PingHost, 2000);
            PingMs = reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch { PingMs = null; }
        Ping = PingMs switch { null => PingQuality.Lost, < 60 => PingQuality.Good, < 150 => PingQuality.Fair, _ => PingQuality.Bad };
    }

    private async Task WeatherAsync()
    {
        try
        {
            if (_settings.WeatherLatitude is null || _settings.WeatherLongitude is null)
            {
                var geo = await GetJson($"https://geocoding-api.open-meteo.com/v1/search?count=1&name={Uri.EscapeDataString(_settings.WeatherCity!)}");
                if (!geo.TryGetProperty("results", out var res) || res.GetArrayLength() == 0) return;
                _settings.WeatherLatitude = res[0].GetProperty("latitude").GetDouble();
                _settings.WeatherLongitude = res[0].GetProperty("longitude").GetDouble();
                _settings.Save();
            }
            string ll = $"latitude={_settings.WeatherLatitude!.Value.ToString(CultureInfo.InvariantCulture)}&longitude={_settings.WeatherLongitude!.Value.ToString(CultureInfo.InvariantCulture)}";
            var wx = await GetJson($"https://api.open-meteo.com/v1/forecast?{ll}&current=temperature_2m,weather_code");
            var cur = wx.GetProperty("current");
            TemperatureC = cur.GetProperty("temperature_2m").GetDouble();
            WeatherCode = cur.GetProperty("weather_code").GetInt32();
            var aq = await GetJson($"https://air-quality-api.open-meteo.com/v1/air-quality?{ll}&current=us_aqi");
            UsAqi = aq.GetProperty("current").TryGetProperty("us_aqi", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetInt32() : null;
        }
        catch { _weatherAt = DateTime.Now - TimeSpan.FromMinutes(25); } // retry in ~5 min
    }

    private async Task FxAsync()
    {
        try
        {
            var (from, to) = ParsePair(_settings.CurrencyPair)!.Value;
            var fx = await GetJson($"https://api.frankfurter.app/latest?from={from}&to={to}");
            FxRate = fx.GetProperty("rates").GetProperty(to).GetDouble();
        }
        catch { FxRate = null; _fxAt = DateTime.Now - TimeSpan.FromMinutes(55); }
    }

    /// <summary>"USD/EUR", "usd eur" or "USD→EUR" → ("USD","EUR").</summary>
    public static (string From, string To)? ParsePair(string? pair)
    {
        if (string.IsNullOrWhiteSpace(pair)) return null;
        var parts = pair.ToUpperInvariant().Split(['/', ' ', '-', '>', '→', ','], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts.All(p => p.Length == 3 && p.All(char.IsLetter)) ? (parts[0], parts[1]) : null;
    }

    private static async Task<JsonElement> GetJson(string url)
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        return doc.RootElement.Clone();
    }

    /// <summary>WMO weather code → glyph + word (Open-Meteo uses WMO 4677 codes).</summary>
    public static (string Glyph, string Text) DescribeWeather(int code) => code switch
    {
        0 => ("☀", "Clear"),
        1 or 2 => ("⛅", "Partly cloudy"),
        3 => ("☁", "Cloudy"),
        45 or 48 => ("🌫", "Fog"),
        >= 51 and <= 67 or >= 80 and <= 82 => ("🌧", "Rain"),
        >= 71 and <= 77 or 85 or 86 => ("❄", "Snow"),
        >= 95 => ("⛈", "Storm"),
        _ => ("☁", "Cloudy"),
    };

    public static string AqiBand(int aqi) => aqi switch
    {
        <= 50 => "Good", <= 100 => "Moderate", <= 150 => "Sensitive", <= 200 => "Unhealthy", <= 300 => "Very unhealthy", _ => "Hazardous",
    };

    public void Dispose() => _screenTimer.Stop();

    [StructLayout(LayoutKind.Sequential)] private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
}
