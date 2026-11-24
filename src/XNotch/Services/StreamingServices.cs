using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace XNotch.Services;

public enum ChatPlatform { Twitch, YouTube }

public sealed record ChatMessage(ChatPlatform Platform, string User, string Text, string? Color, DateTime Time);

/// <summary>
/// Live chat. Twitch: anonymous read-only IRC over WebSocket (no account, no token).
/// YouTube: Data API v3 with the user's own API key (opt-in, uses their quota).
/// </summary>
public sealed class ChatService : IDisposable
{
    private const int Keep = 100;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly List<ChatMessage> _messages = [];
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;

    public string? TwitchChannel { get; private set; }
    public bool TwitchConnected { get; private set; }
    public bool YouTubeConnected { get; private set; }

    public IReadOnlyList<ChatMessage> Messages { get { lock (_lock) return _messages.ToList(); } }

    /// <summary>Fired on a worker thread.</summary>
    public event Action<ChatMessage>? MessageReceived;
    public event Action? StateChanged;

    public void Configure(string? twitchChannel, string? youTubeApiKey, string? youTubeVideoId)
    {
        Stop();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        TwitchChannel = string.IsNullOrWhiteSpace(twitchChannel) ? null : twitchChannel.Trim().TrimStart('#').ToLowerInvariant();
        if (TwitchChannel != null) _ = Task.Run(() => TwitchLoopAsync(TwitchChannel, ct));
        if (!string.IsNullOrWhiteSpace(youTubeApiKey) && !string.IsNullOrWhiteSpace(youTubeVideoId))
            _ = Task.Run(() => YouTubeLoopAsync(youTubeApiKey.Trim(), youTubeVideoId.Trim(), ct));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        TwitchConnected = YouTubeConnected = false;
        StateChanged?.Invoke();
    }

    private void Add(ChatMessage m)
    {
        lock (_lock)
        {
            _messages.Add(m);
            if (_messages.Count > Keep) _messages.RemoveAt(0);
        }
        MessageReceived?.Invoke(m);
    }

    // ── Twitch ──
    private async Task TwitchLoopAsync(string channel, CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(2);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri("wss://irc-ws.chat.twitch.tv:443"), ct);
                await Send(ws, "CAP REQ :twitch.tv/tags twitch.tv/commands", ct);
                await Send(ws, "PASS SCHMOOPIIE", ct); // Twitch's documented anonymous login
                await Send(ws, $"NICK justinfan{Random.Shared.Next(10000, 99999)}", ct);
                await Send(ws, $"JOIN #{channel}", ct);
                TwitchConnected = true;
                StateChanged?.Invoke();
                backoff = TimeSpan.FromSeconds(2);

                var buf = new byte[16 * 1024];
                var pending = new StringBuilder();
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var r = await ws.ReceiveAsync(buf, ct);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    pending.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                    if (!r.EndOfMessage) continue;
                    foreach (var line in pending.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.StartsWith("PING")) { await Send(ws, "PONG" + line[4..], ct); continue; }
                        if (line.Contains(" RECONNECT")) throw new WebSocketException("reconnect requested");
                        if (ParseTwitch(line) is { } msg) Add(msg);
                    }
                    pending.Clear();
                }
            }
            catch (OperationCanceledException) { break; }
            catch { }
            TwitchConnected = false;
            StateChanged?.Invoke();
            try { await Task.Delay(backoff, ct); } catch { break; }
            backoff = TimeSpan.FromSeconds(Math.Min(60, backoff.TotalSeconds * 2));
        }
    }

    private static Task Send(ClientWebSocket ws, string line, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(line + "\r\n"), WebSocketMessageType.Text, true, ct);

    /// <summary>Parses one IRCv3 line; returns null for anything that isn't a chat message.</summary>
    public static ChatMessage? ParseTwitch(string line)
    {
        // @tags :nick!nick@nick.tmi.twitch.tv PRIVMSG #channel :text
        string tags = "";
        if (line.StartsWith('@'))
        {
            int sp = line.IndexOf(' ');
            if (sp < 0) return null;
            tags = line[1..sp];
            line = line[(sp + 1)..];
        }
        int priv = line.IndexOf(" PRIVMSG #", StringComparison.Ordinal);
        if (!line.StartsWith(':') || priv < 0) return null;
        int textStart = line.IndexOf(" :", priv, StringComparison.Ordinal);
        if (textStart < 0) return null;

        int bang = line.IndexOf('!');
        string nick = line[1..(bang > 0 && bang < priv ? bang : priv)];
        var tagMap = tags.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Split('=', 2)).Where(kv => kv.Length == 2)
            .ToDictionary(kv => kv[0], kv => kv[1]);
        string user = tagMap.TryGetValue("display-name", out var dn) && dn.Length > 0 ? dn.Replace("\\s", " ") : nick;
        string? color = tagMap.TryGetValue("color", out var c) && c.StartsWith('#') ? c : null;
        string text = line[(textStart + 2)..];
        if (text.StartsWith("\u0001ACTION ") && text.EndsWith('\u0001')) text = text[8..^1]; // /me
        return new ChatMessage(ChatPlatform.Twitch, user, text, color, DateTime.Now);
    }

    // ── YouTube ──
    private async Task YouTubeLoopAsync(string key, string videoId, CancellationToken ct)
    {
        try
        {
            var video = await GetJson($"https://www.googleapis.com/youtube/v3/videos?part=liveStreamingDetails&id={Uri.EscapeDataString(videoId)}&key={Uri.EscapeDataString(key)}", ct);
            var chatId = video["items"]?[0]?["liveStreamingDetails"]?["activeLiveChatId"]?.GetValue<string>();
            if (chatId == null) return; // not live
            YouTubeConnected = true;
            StateChanged?.Invoke();

            string? page = null;
            bool first = true;
            while (!ct.IsCancellationRequested)
            {
                var url = $"https://www.googleapis.com/youtube/v3/liveChat/messages?part=snippet,authorDetails&liveChatId={Uri.EscapeDataString(chatId)}&key={Uri.EscapeDataString(key)}"
                          + (page != null ? $"&pageToken={Uri.EscapeDataString(page)}" : "");
                var res = await GetJson(url, ct);
                page = res["nextPageToken"]?.GetValue<string>();
                if (!first) // the first page is history; only show what arrives live
                {
                    foreach (var item in res["items"]?.AsArray() ?? [])
                    {
                        var text = item?["snippet"]?["displayMessage"]?.GetValue<string>();
                        var user = item?["authorDetails"]?["displayName"]?.GetValue<string>();
                        if (text != null && user != null) Add(new ChatMessage(ChatPlatform.YouTube, user, text, null, DateTime.Now));
                    }
                }
                first = false;
                int wait = res["pollingIntervalMillis"]?.GetValue<int>() ?? 5000;
                await Task.Delay(Math.Max(wait, 3000), ct); // ponytail: ≥3 s keeps a day of streaming inside the free quota
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        YouTubeConnected = false;
        StateChanged?.Invoke();
    }

    private static async Task<JsonNode> GetJson(string url, CancellationToken ct) =>
        JsonNode.Parse(await Http.GetStringAsync(url, ct)) ?? new JsonObject();

    public void Dispose() => Stop();
}

/// <summary>
/// OBS Studio control over obs-websocket v5 (built into OBS 28+, default ws://127.0.0.1:4455).
/// Stream/record state, timecode, scenes, start/stop.
/// </summary>
public sealed class ObsService : IDisposable
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private readonly Dictionary<string, TaskCompletionSource<JsonNode?>> _pending = [];
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public bool Connected { get; private set; }
    public bool AuthFailed { get; private set; }
    public bool Streaming { get; private set; }
    public bool Recording { get; private set; }
    public DateTime? LiveSince { get; private set; }
    public string? CurrentScene { get; private set; }
    public IReadOnlyList<string> Scenes { get; private set; } = [];

    public event Action? StateChanged;

    public void Configure(bool enabled, int port, string? password)
    {
        Stop();
        if (!enabled) return;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(port, password, _cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        try { _ws?.Abort(); } catch { }
        _ws = null;
        Connected = Streaming = Recording = false;
        LiveSince = null;
        StateChanged?.Invoke();
    }

    private async Task LoopAsync(int port, string? password, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                _ws = ws;
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), ct);
                var hello = await Receive(ws, ct);
                var identify = new JsonObject { ["rpcVersion"] = 1, ["eventSubscriptions"] = 1 | 4 | 64 }; // General, Scenes, Outputs
                if (hello?["d"]?["authentication"] is JsonNode auth)
                {
                    if (string.IsNullOrEmpty(password)) { AuthFailed = true; StateChanged?.Invoke(); return; }
                    identify["authentication"] = AuthString(password, auth["salt"]!.GetValue<string>(), auth["challenge"]!.GetValue<string>());
                }
                await SendRaw(ws, new JsonObject { ["op"] = 1, ["d"] = identify }, ct);
                var identified = await Receive(ws, ct);
                if (identified?["op"]?.GetValue<int>() != 2) { AuthFailed = true; StateChanged?.Invoke(); return; } // wrong password closes the socket

                AuthFailed = false;
                Connected = true;
                _ = Task.Run(() => RefreshAsync(ct), ct);
                StateChanged?.Invoke();

                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var msg = await Receive(ws, ct);
                    if (msg == null) break;
                    Handle(msg);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { }
            Connected = Streaming = Recording = false;
            LiveSince = null;
            StateChanged?.Invoke();
            try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch { break; } // OBS not running yet: retry quietly
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var stream = await Request("GetStreamStatus");
        var record = await Request("GetRecordStatus");
        var scenes = await Request("GetSceneList");
        Streaming = stream?["outputActive"]?.GetValue<bool>() == true;
        Recording = record?["outputActive"]?.GetValue<bool>() == true;
        if ((Streaming || Recording) && LiveSince == null)
        {
            var tc = (Streaming ? stream : record)?["outputDuration"]?.GetValue<double>() ?? 0;
            LiveSince = DateTime.Now - TimeSpan.FromMilliseconds(tc);
        }
        CurrentScene = scenes?["currentProgramSceneName"]?.GetValue<string>();
        Scenes = scenes?["scenes"]?.AsArray().Select(s => s?["sceneName"]?.GetValue<string>() ?? "").Where(n => n.Length > 0).Reverse().ToList() ?? [];
        StateChanged?.Invoke();
    }

    private void Handle(JsonNode msg)
    {
        int op = msg["op"]?.GetValue<int>() ?? -1;
        var d = msg["d"];
        if (op == 7) // RequestResponse
        {
            var id = d?["requestId"]?.GetValue<string>();
            TaskCompletionSource<JsonNode?>? tcs = null;
            lock (_pending) if (id != null && _pending.Remove(id, out var t)) tcs = t;
            tcs?.TrySetResult(d?["responseData"]?.DeepClone());
        }
        else if (op == 5) // Event
        {
            var type = d?["eventType"]?.GetValue<string>();
            var data = d?["eventData"];
            switch (type)
            {
                case "StreamStateChanged": Streaming = data?["outputActive"]?.GetValue<bool>() == true; UpdateLive(); break;
                case "RecordStateChanged": Recording = data?["outputActive"]?.GetValue<bool>() == true; UpdateLive(); break;
                case "CurrentProgramSceneChanged": CurrentScene = data?["sceneName"]?.GetValue<string>(); break;
                case "SceneCreated" or "SceneRemoved" or "SceneNameChanged": _ = RefreshAsync(CancellationToken.None); return;
                default: return;
            }
            StateChanged?.Invoke();
        }
    }

    private void UpdateLive()
    {
        if (Streaming || Recording) LiveSince ??= DateTime.Now;
        else LiveSince = null;
    }

    public Task ToggleStream() => Request("ToggleStream");
    public Task ToggleRecord() => Request("ToggleRecord");
    public Task SetScene(string name) => Request("SetCurrentProgramScene", new JsonObject { ["sceneName"] = name });

    private async Task<JsonNode?> Request(string type, JsonObject? data = null)
    {
        var ws = _ws;
        if (ws == null || ws.State != WebSocketState.Open) return null;
        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending) _pending[id] = tcs;
        var d = new JsonObject { ["requestType"] = type, ["requestId"] = id };
        if (data != null) d["requestData"] = data;
        try { await SendRaw(ws, new JsonObject { ["op"] = 6, ["d"] = d }, CancellationToken.None); }
        catch { lock (_pending) _pending.Remove(id); return null; }
        var done = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        lock (_pending) _pending.Remove(id);
        return done == tcs.Task ? tcs.Task.Result : null;
    }

    private async Task SendRaw(ClientWebSocket ws, JsonNode msg, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try { await ws.SendAsync(Encoding.UTF8.GetBytes(msg.ToJsonString()), WebSocketMessageType.Text, true, ct); }
        finally { _sendLock.Release(); }
    }

    private static async Task<JsonNode?> Receive(ClientWebSocket ws, CancellationToken ct)
    {
        var buf = new byte[64 * 1024];
        using var ms = new System.IO.MemoryStream();
        WebSocketReceiveResult r;
        do
        {
            r = await ws.ReceiveAsync(buf, ct);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buf, 0, r.Count);
        } while (!r.EndOfMessage);
        return JsonNode.Parse(ms.ToArray());
    }

    /// <summary>obs-websocket v5 auth: base64(sha256(base64(sha256(password + salt)) + challenge)).</summary>
    public static string AuthString(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    public void Dispose() => Stop();
}
