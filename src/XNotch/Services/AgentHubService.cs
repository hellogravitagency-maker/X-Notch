using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace XNotch.Services;

public enum AgentKind { Claude, Codex, Custom }

public enum AgentState { Idle, Working, Waiting, Done, Error }

public sealed class AgentSession
{
    public required string Id { get; init; }
    public AgentKind Kind { get; init; }
    public string Name { get; set; } = "";
    public AgentState State { get; set; } = AgentState.Idle;
    public string Activity { get; set; } = "";
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public string KindLabel => Kind switch
    {
        AgentKind.Claude => "Claude",
        AgentKind.Codex => "Codex",
        _ => string.IsNullOrEmpty(Name) ? "Agent" : Name,
    };
}

/// <summary>
/// Live status of coding agents (Claude Code, Codex, custom scripts).
/// Agents report through their own hook systems by POSTing JSON to
/// http://localhost:47830/agent — X-Notch never polls them.
/// </summary>
public sealed class AgentHubService : IDisposable
{
    public const int Port = 47830;
    public static readonly string Endpoint = $"http://localhost:{Port}/agent";

    private readonly Dictionary<string, AgentSession> _sessions = [];
    private readonly object _lock = new();
    private HttpListener? _listener;

    /// <summary>Fired (on a worker thread) whenever any session changes.</summary>
    public event Action? SessionsChanged;

    /// <summary>Fired when a session starts waiting for the user or finishes.</summary>
    public event Action<AgentSession>? Attention;

    public bool IsRunning => _listener?.IsListening == true;

    public IReadOnlyList<AgentSession> Sessions
    {
        get { lock (_lock) return _sessions.Values.OrderByDescending(s => s.UpdatedAt).ToList(); }
    }

    public void Start()
    {
        if (IsRunning) return;
        try
        {
            // ponytail: "localhost" prefix needs no URL ACL / admin; 127.0.0.1 would.
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AgentHub: cannot listen on {Port}: {ex.Message}");
            _listener = null;
        }
    }

    public void Stop()
    {
        try { _listener?.Close(); } catch { }
        _listener = null;
    }

    private async Task AcceptLoopAsync()
    {
        while (_listener?.IsListening == true)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { return; }

            try
            {
                var req = ctx.Request;
                // Browsers always send Origin on cross-site POSTs; real hooks (curl) never do.
                if (req.Headers["Origin"] != null || req.HttpMethod != "POST" || req.Url?.AbsolutePath != "/agent")
                {
                    ctx.Response.StatusCode = 403;
                }
                else
                {
                    using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                    string body = await reader.ReadToEndAsync();
                    Ingest(req.QueryString["kind"] ?? "custom", req.QueryString["name"], body);
                    ctx.Response.StatusCode = 204; // empty body: Claude adds hook stdout to context
                }
            }
            catch { ctx.Response.StatusCode = 400; }
            finally { ctx.Response.Close(); }
        }
    }

    /// <summary>Applies one hook payload. Public so tests can drive it without HTTP.</summary>
    public void Ingest(string kind, string? name, string body)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = doc.RootElement;
        AgentSession? attention = null;

        lock (_lock)
        {
            var agentKind = kind.ToLowerInvariant() switch
            {
                "claude" => AgentKind.Claude,
                "codex" => AgentKind.Codex,
                _ => AgentKind.Custom,
            };

            string id = Str(root, "session_id") ?? Str(root, "thread-id") ?? $"{agentKind}:{name}";
            string evt = Str(root, "hook_event_name") ?? Str(root, "type") ?? Str(root, "event") ?? "";

            if (evt == "SessionEnd")
            {
                _sessions.Remove(id);
            }
            else
            {
                if (!_sessions.TryGetValue(id, out var s))
                {
                    s = new AgentSession { Id = id, Kind = agentKind };
                    _sessions[id] = s;
                }

                var cwd = Str(root, "cwd");
                if (!string.IsNullOrEmpty(name)) s.Name = name;
                else if (!string.IsNullOrEmpty(cwd)) s.Name = Path.GetFileName(cwd.TrimEnd('\\', '/'));

                var before = s.State;
                Apply(s, evt, root);
                s.UpdatedAt = DateTime.Now;

                if (s.State != before && s.State is AgentState.Waiting or AgentState.Done or AgentState.Error)
                    attention = s;
            }

            // ponytail: prune on write instead of a timer; idle sessions vanish after 30 min.
            foreach (var stale in _sessions.Values.Where(x => DateTime.Now - x.UpdatedAt > TimeSpan.FromMinutes(30)).ToList())
                _sessions.Remove(stale.Id);
        }

        SessionsChanged?.Invoke();
        if (attention != null) Attention?.Invoke(attention);
    }

    /// <summary>Maps a Claude Code hook event / Codex notify / custom event onto a session.</summary>
    public static void Apply(AgentSession s, string evt, JsonElement root)
    {
        switch (evt)
        {
            case "SessionStart":
                s.State = AgentState.Idle;
                s.Activity = "Ready";
                s.StartedAt = DateTime.Now;
                break;
            case "UserPromptSubmit":
                s.State = AgentState.Working;
                s.Activity = "Thinking";
                s.StartedAt = DateTime.Now;
                break;
            case "PreToolUse":
                s.State = AgentState.Working;
                s.Activity = DescribeTool(Str(root, "tool_name") ?? "", root);
                break;
            case "PostToolUse":
                s.State = AgentState.Working;
                break;
            case "Notification":
                s.State = AgentState.Waiting;
                s.Activity = Str(root, "message") ?? "Needs your input";
                break;
            case "Stop":
            case "agent-turn-complete":
                s.State = AgentState.Done;
                s.Activity = Trim(Str(root, "last-assistant-message") ?? "Finished");
                break;
            // Custom agents: {"event":"working|waiting|done|error","message":"..."}
            case "working": s.State = AgentState.Working; s.Activity = Str(root, "message") ?? "Working"; break;
            case "waiting": s.State = AgentState.Waiting; s.Activity = Str(root, "message") ?? "Needs you"; break;
            case "done": s.State = AgentState.Done; s.Activity = Str(root, "message") ?? "Finished"; break;
            case "error": s.State = AgentState.Error; s.Activity = Str(root, "message") ?? "Failed"; break;
        }
    }

    private static string DescribeTool(string tool, JsonElement root)
    {
        var input = root.TryGetProperty("tool_input", out var ti) && ti.ValueKind == JsonValueKind.Object ? ti : default;
        string? file = input.ValueKind == JsonValueKind.Object ? Str(input, "file_path") ?? Str(input, "path") : null;
        string fileName = file != null ? Path.GetFileName(file) : "";

        return tool switch
        {
            "Edit" or "MultiEdit" or "Write" or "NotebookEdit" => $"Editing {fileName}".TrimEnd(),
            "Read" => $"Reading {fileName}".TrimEnd(),
            "Grep" or "Glob" => "Searching code",
            "Bash" or "PowerShell" => "Running " + Trim(input.ValueKind == JsonValueKind.Object ? Str(input, "command") ?? "" : "", 40),
            "WebFetch" or "WebSearch" => "Browsing the web",
            "Task" or "Agent" => "Running a subagent",
            "TodoWrite" or "ExitPlanMode" => "Planning",
            "AskUserQuestion" => "Asking you",
            _ => tool.StartsWith("mcp__") ? "Using " + tool.Split("__").ElementAtOrDefault(1) : $"Using {tool}",
        };
    }

    private static string? Str(JsonElement e, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Trim(string s, int max = 80)
    {
        s = s.ReplaceLineEndings(" ").Trim();
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }

    public void Dispose() => Stop();
}

/// <summary>
/// Adds/removes X-Notch reporting hooks in Claude Code (~/.claude/settings.json)
/// and Codex (~/.codex/config.toml). Every file is backed up once before the first edit.
/// Our entries are recognised by the endpoint URL, so removal never touches user hooks.
/// </summary>
public static class AgentHookInstaller
{
    private static readonly string[] ClaudeEvents =
        ["SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "Notification", "Stop", "SessionEnd"];

    public static string ClaudeSettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public static string CodexConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml");

    public static string ClaudeCommand =>
        $"curl -s --max-time 2 -H \"Content-Type: application/json\" --data-binary @- \"{AgentHubService.Endpoint}?kind=claude\"";

    public static string CodexNotifyLine =>
        $"notify = [\"curl\", \"-s\", \"--max-time\", \"2\", \"-H\", \"Content-Type: application/json\", \"{AgentHubService.Endpoint}?kind=codex\", \"--data\"]";

    /// <summary>Installs or removes hooks for every agent CLI that is present. Returns a short status.</summary>
    public static string Apply(bool enable)
    {
        var parts = new List<string>();
        if (Directory.Exists(Path.GetDirectoryName(ClaudeSettingsPath)))
            parts.Add("Claude " + (enable ? InstallClaude(ClaudeSettingsPath) : RemoveClaude(ClaudeSettingsPath)));
        if (Directory.Exists(Path.GetDirectoryName(CodexConfigPath)))
            parts.Add("Codex " + (enable ? InstallCodex(CodexConfigPath) : RemoveCodex(CodexConfigPath)));
        return parts.Count == 0 ? "No Claude Code or Codex found" : string.Join(" · ", parts);
    }

    public static string InstallClaude(string path)
    {
        var root = ReadJson(path);
        if (root == null) return "settings unreadable, left alone";

        int added = 0;
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        root["hooks"] = hooks;
        foreach (var evt in ClaudeEvents)
        {
            var groups = hooks[evt] as JsonArray ?? new JsonArray();
            hooks[evt] = groups;
            bool present = groups.OfType<JsonObject>()
                .SelectMany(g => g["hooks"] as JsonArray ?? [])
                .Any(IsOurs);
            if (present) continue;

            var group = new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = ClaudeCommand, ["timeout"] = 5 }),
            };
            if (evt is "PreToolUse" or "PostToolUse") group["matcher"] = "*";
            groups.Add(group);
            added++;
        }

        if (added == 0) return "connected";
        Backup(path);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return "connected";
    }

    public static string RemoveClaude(string path)
    {
        if (!File.Exists(path)) return "not connected";
        var root = ReadJson(path);
        if (root?["hooks"] is not JsonObject hooks) return "not connected";

        foreach (var evt in hooks.Select(kv => kv.Key).ToList())
        {
            if (hooks[evt] is not JsonArray groups) continue;
            foreach (var g in groups.OfType<JsonObject>().ToList())
            {
                if (g["hooks"] is not JsonArray inner) continue;
                foreach (var h in inner.Where(IsOurs).ToList()) inner.Remove(h);
                if (inner.Count == 0) groups.Remove(g);
            }
            if (groups.Count == 0) hooks.Remove(evt);
        }
        if (hooks.Count == 0) root.AsObject().Remove("hooks");

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return "disconnected";
    }

    public static string InstallCodex(string path)
    {
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        if (lines.Any(l => l.Contains(AgentHubService.Endpoint))) return "connected";
        // TOML: a top-level key must precede every [table]; Codex allows only one notify.
        if (lines.Any(l => l.TrimStart().StartsWith("notify") && l.Contains('=')))
            return "already has its own notify, left alone";

        Backup(path);
        lines.Insert(0, CodexNotifyLine);
        File.WriteAllLines(path, lines);
        return "connected";
    }

    public static string RemoveCodex(string path)
    {
        if (!File.Exists(path)) return "not connected";
        var lines = File.ReadAllLines(path).ToList();
        if (lines.RemoveAll(l => l.Contains(AgentHubService.Endpoint)) == 0) return "not connected";
        File.WriteAllLines(path, lines);
        return "disconnected";
    }

    private static bool IsOurs(JsonNode? hook) =>
        hook?["command"]?.GetValue<string>()?.Contains(AgentHubService.Endpoint) == true;

    private static JsonNode? ReadJson(string path)
    {
        if (!File.Exists(path)) return new JsonObject();
        try
        {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
            return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) as JsonObject;
        }
        catch { return null; } // never overwrite a file we failed to parse
    }

    private static void Backup(string path)
    {
        var backup = path + ".xnotch-backup";
        if (File.Exists(path) && !File.Exists(backup)) File.Copy(path, backup);
    }
}
