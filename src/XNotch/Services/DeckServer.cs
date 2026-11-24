using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace XNotch.Services;

public sealed class DeckDevice
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Phone";
    public string TokenHash { get; set; } = "";
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public DateTime LastSeen { get; set; } = DateTime.Now;
}

public enum DeckState { Off, On, NoNetwork, PublicNetwork, Failed }

/// <summary>
/// Pairing and authentication for the Phone Deck. Pairing is only possible inside a short
/// window the user opens on the PC; the 6-digit code is shown only on the PC screen.
/// Tokens are 256-bit random, stored as SHA-256, compared in constant time.
/// </summary>
public sealed class DeckPairing
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(2);
    public const int MaxFailures = 5;

    private readonly List<DeckDevice> _devices;
    private readonly Action _save;
    private readonly object _lock = new();
    private DateTime _openedAt;
    private int _failures;

    public string? Code { get; private set; }
    public bool IsOpen => Code != null && DateTime.Now - _openedAt < Window;
    public IReadOnlyList<DeckDevice> Devices { get { lock (_lock) return _devices.ToList(); } }

    public event Action? Changed;
    public event Action<DeckDevice>? Paired;

    public DeckPairing(List<DeckDevice> devices, Action save)
    {
        _devices = devices;
        _save = save;
    }

    /// <summary>Opens the pairing window (user pressed "Pair phone") with a fresh code.</summary>
    public string Open()
    {
        lock (_lock)
        {
            Code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            _openedAt = DateTime.Now;
            _failures = 0;
        }
        Changed?.Invoke();
        return Code;
    }

    public void Close()
    {
        lock (_lock) Code = null;
        Changed?.Invoke();
    }

    /// <summary>Returns a new token, or null. Wrong codes count; too many closes the window.</summary>
    public string? Pair(string? code, string? name)
    {
        DeckDevice device;
        string token;
        lock (_lock)
        {
            if (!IsOpen || code == null) return null;
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(code.Trim()), Encoding.ASCII.GetBytes(Code!)))
            {
                if (++_failures >= MaxFailures) Code = null;
                return null;
            }
            token = Base64Url(RandomNumberGenerator.GetBytes(32));
            device = new DeckDevice
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Phone" : name.Trim()[..Math.Min(40, name.Trim().Length)],
                TokenHash = Hash(token),
            };
            _devices.Add(device);
            Code = null; // one phone per window
        }
        _save();
        Changed?.Invoke();
        Paired?.Invoke(device);
        return token;
    }

    public DeckDevice? Authenticate(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128) return null;
        var hash = Convert.FromHexString(Hash(token));
        lock (_lock)
        {
            DeckDevice? match = null;
            foreach (var d in _devices) // no early exit: constant time over the list
                if (CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(d.TokenHash))) match = d;
            if (match != null && DateTime.Now - match.LastSeen > TimeSpan.FromMinutes(1))
            {
                match.LastSeen = DateTime.Now;
                _save();
            }
            return match;
        }
    }

    public void Revoke(string id)
    {
        lock (_lock) _devices.RemoveAll(d => d.Id == id);
        _save();
        Changed?.Invoke();
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>What the phone can see and do. The app fills these in; tests fill only what they need.</summary>
public sealed class DeckHost
{
    public Func<object> Status { get; set; } = () => new { };
    public Func<byte[]?> ArtPng { get; set; } = () => null;
    public Func<string, double, Task> Media { get; set; } = (_, _) => Task.CompletedTask;
    public Action<double?, bool> Volume { get; set; } = (_, _) => { };
    public Func<IReadOnlyList<string>> Apps { get; set; } = () => [];
    public Action<int> LaunchApp { get; set; } = _ => { };
    public Action<string> RunAction { get; set; } = _ => { };
}

/// <summary>
/// The Phone Deck LAN server (Kestrel). Off by default, refuses public networks,
/// every /api and /ws call needs a paired token.
/// </summary>
public sealed class DeckServer : IAsyncDisposable
{
    public const int DefaultPort = 47831;

    private readonly DeckPairing _pairing;
    private readonly DeckHost _host;
    private WebApplication? _app;
    private DateTime _lastPairAttempt;

    public DeckState State { get; private set; } = DeckState.Off;
    public string? Url { get; private set; }
    public event Action? StateChanged;

    public DeckServer(DeckPairing pairing, DeckHost host)
    {
        _pairing = pairing;
        _host = host;
    }

    public async Task StartAsync(int port = DefaultPort, bool allowPublicNetwork = false, bool loopbackOnly = false)
    {
        if (_app != null) return;
        var ip = loopbackOnly ? IPAddress.Loopback : LanAddress();
        if (ip == null) { Set(DeckState.NoNetwork); return; }
        if (!loopbackOnly && !allowPublicNetwork && IsOnPublicNetwork()) { Set(DeckState.PublicNetwork); return; }

        try
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(o =>
            {
                if (loopbackOnly) o.Listen(IPAddress.Loopback, port); else o.ListenAnyIP(port);
                o.Limits.MaxRequestBodySize = 16 * 1024;
            });
            var app = builder.Build();
            app.UseWebSockets();
            Map(app);
            await app.StartAsync();
            _app = app;
            Url = $"http://{ip}:{port}/";
            Set(DeckState.On);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Deck: {ex.Message}");
            Set(DeckState.Failed);
        }
    }

    public async Task StopAsync()
    {
        var app = _app;
        _app = null;
        Url = null;
        if (app != null) { try { await app.StopAsync(); await app.DisposeAsync(); } catch { } }
        Set(DeckState.Off);
    }

    private void Set(DeckState s) { State = s; StateChanged?.Invoke(); }

    private void Map(WebApplication app)
    {
        app.MapGet("/", () => Results.Content(IndexHtml(), "text/html; charset=utf-8"));

        app.MapPost("/api/pair", async (HttpContext ctx) =>
        {
            // ponytail: one global attempt per second is enough throttle for a 2-minute window.
            if (DateTime.Now - _lastPairAttempt < TimeSpan.FromSeconds(1)) return Results.StatusCode(429);
            _lastPairAttempt = DateTime.Now;
            var body = await Body(ctx);
            var token = _pairing.Pair(Str(body, "code"), Str(body, "name"));
            return token == null ? Results.StatusCode(403) : Results.Json(new { token });
        });

        var api = app.MapGroup("/api").AddEndpointFilter(async (ctx, next) =>
            Authorized(ctx.HttpContext) ? await next(ctx) : Results.StatusCode(401));

        api.MapGet("/status", () => Results.Json(_host.Status()));
        api.MapGet("/art", () => _host.ArtPng() is { } png ? Results.Bytes(png, "image/png") : Results.NotFound());
        api.MapPost("/media", async (HttpContext ctx) =>
        {
            var b = await Body(ctx);
            var cmd = Str(b, "cmd");
            if (cmd is not ("play" or "next" or "prev" or "seek")) return Results.BadRequest();
            await _host.Media(cmd, Num(b, "value") ?? 0);
            return Results.NoContent();
        });
        api.MapPost("/volume", async (HttpContext ctx) =>
        {
            var b = await Body(ctx);
            _host.Volume(Num(b, "value") is double v ? Math.Clamp(v, 0, 1) : null, b.TryGetProperty("mute", out _));
            return Results.NoContent();
        });
        api.MapPost("/type", async (HttpContext ctx) =>
        {
            var text = Str(await Body(ctx), "text");
            if (string.IsNullOrEmpty(text) || text.Length > 500) return Results.BadRequest();
            DeckInput.Type(text);
            return Results.NoContent();
        });
        api.MapGet("/apps", () => Results.Json(_host.Apps()));
        api.MapPost("/apps/launch", async (HttpContext ctx) =>
        {
            if (Num(await Body(ctx), "index") is not double i || i < 0 || i >= _host.Apps().Count) return Results.BadRequest();
            _host.LaunchApp((int)i);
            return Results.NoContent();
        });
        api.MapPost("/action", async (HttpContext ctx) =>
        {
            var id = Str(await Body(ctx), "id");
            if (id is not ("lock" or "mic" or "mute" or "snip" or "desktop")) return Results.BadRequest();
            _host.RunAction(id);
            return Results.NoContent();
        });

        // Trackpad: browsers cannot set headers on WebSockets, so the first message is the token.
        app.Map("/ws", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            var buf = new byte[256];
            WebSocketReceiveResult first;
            using (var hello = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted))
            {
                hello.CancelAfter(TimeSpan.FromSeconds(5)); // unauthenticated sockets don't get to linger
                try { first = await ws.ReceiveAsync(buf, hello.Token); }
                catch (OperationCanceledException) { return; }
            }
            if (_pairing.Authenticate(Encoding.UTF8.GetString(buf, 0, first.Count)) == null)
            {
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "unauthorized", CancellationToken.None);
                return;
            }
            while (ws.State == WebSocketState.Open)
            {
                var r = await ws.ReceiveAsync(buf, ctx.RequestAborted);
                if (r.MessageType == WebSocketMessageType.Close) break;
                DeckInput.Handle(Encoding.ASCII.GetString(buf, 0, r.Count));
            }
        });
    }

    private bool Authorized(HttpContext ctx)
    {
        var h = ctx.Request.Headers.Authorization.ToString();
        return h.StartsWith("Bearer ", StringComparison.Ordinal) && _pairing.Authenticate(h[7..]) != null;
    }

    private static async Task<JsonElement> Body(HttpContext ctx)
    {
        try { return (await JsonDocument.ParseAsync(ctx.Request.Body)).RootElement.Clone(); }
        catch { return default; }
    }

    private static string? Str(JsonElement e, string p) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement e, string p) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static string? _html;
    private static string IndexHtml()
    {
        if (_html != null) return _html;
        using var s = typeof(DeckServer).Assembly.GetManifestResourceStream("XNotch.Deck.index.html");
        return _html = s == null ? "<h1>X-Notch Deck</h1>" : new StreamReader(s).ReadToEnd();
    }

    /// <summary>The PC's address on the local network (the interface with a default gateway).</summary>
    public static IPAddress? LanAddress() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n => n.GetIPProperties())
            .Where(p => p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .SelectMany(p => p.UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);

    /// <summary>True if any connected network is set to Public (Windows Network List Manager).</summary>
    public static bool IsOnPublicNetwork()
    {
        try { return NetworkCategories().Contains(0 /* PUBLIC */); }
        catch { return true; } // can't tell: be safe
    }

    /// <summary>Category of each connected network: 0 Public, 1 Private, 2 Domain. Throws if NLM is unavailable.</summary>
    public static List<int> NetworkCategories()
    {
        dynamic nlm = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"))!)!;
        var result = new List<int>();
        foreach (dynamic net in nlm.GetNetworks(1 /* CONNECTED */))
            result.Add((int)net.GetCategory());
        return result;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}

/// <summary>Remote mouse and keyboard via SendInput.</summary>
public static class DeckInput
{
    private const uint MOUSE_MOVE = 0x1, LEFTDOWN = 0x2, LEFTUP = 0x4, RIGHTDOWN = 0x8, RIGHTUP = 0x10, WHEEL = 0x800;
    private const uint KEY_UNICODE = 0x4, KEY_UP = 0x2;

    /// <summary>"m dx dy" move · "c l|r" click · "d"/"u" left down/up (drag) · "s dy" scroll.</summary>
    public static void Handle(string msg)
    {
        var p = msg.Split(' ');
        switch (p[0])
        {
            case "m" when p.Length == 3 && double.TryParse(p[1], System.Globalization.CultureInfo.InvariantCulture, out var dx)
                                        && double.TryParse(p[2], System.Globalization.CultureInfo.InvariantCulture, out var dy):
                Mouse(MOUSE_MOVE, (int)Math.Clamp(dx * 2, -500, 500), (int)Math.Clamp(dy * 2, -500, 500), 0);
                break;
            case "c" when p.Length == 2:
                if (p[1] == "r") { Mouse(RIGHTDOWN, 0, 0, 0); Mouse(RIGHTUP, 0, 0, 0); }
                else { Mouse(LEFTDOWN, 0, 0, 0); Mouse(LEFTUP, 0, 0, 0); }
                break;
            case "d": Mouse(LEFTDOWN, 0, 0, 0); break;
            case "u": Mouse(LEFTUP, 0, 0, 0); break;
            case "s" when p.Length == 2 && double.TryParse(p[1], System.Globalization.CultureInfo.InvariantCulture, out var s):
                Mouse(WHEEL, 0, 0, (int)Math.Clamp(-s * 4, -1200, 1200));
                break;
        }
    }

    public static void Type(string text)
    {
        var inputs = new List<INPUT>();
        foreach (char ch in text)
        {
            inputs.Add(Key(ch, 0));
            inputs.Add(Key(ch, KEY_UP));
        }
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static INPUT Key(char ch, uint flags) => new()
    {
        type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEY_UNICODE | flags } },
    };

    private static void Mouse(uint flags, int dx, int dy, int data)
    {
        var i = new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags } } };
        SendInput(1, [i], Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy, mouseData; public uint dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
}
