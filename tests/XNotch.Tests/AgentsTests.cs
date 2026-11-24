using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using XNotch.Services;
using Xunit;

namespace XNotch.Tests;

public class AgentsTests
{
    [Fact]
    public void ClaudeHookLifecycle_MapsToStates_AndRaisesAttention()
    {
        var hub = new AgentHubService();
        AgentSession? attention = null;
        hub.Attention += s => attention = s;

        hub.Ingest("claude", null, """{"session_id":"a","hook_event_name":"UserPromptSubmit","cwd":"C:\\code\\X-Notch"}""");
        hub.Ingest("claude", null, """{"session_id":"a","hook_event_name":"PreToolUse","tool_name":"Edit","tool_input":{"file_path":"C:\\code\\App.cs"}}""");
        var s = hub.Sessions.Single();
        Assert.Equal(AgentState.Working, s.State);
        Assert.Equal("Editing App.cs", s.Activity);
        Assert.Equal("X-Notch", s.Name);
        Assert.Null(attention);

        hub.Ingest("claude", null, """{"session_id":"a","hook_event_name":"Notification","message":"Claude needs your permission to use Bash"}""");
        Assert.Equal(AgentState.Waiting, attention?.State);

        hub.Ingest("claude", null, """{"session_id":"a","hook_event_name":"Stop"}""");
        Assert.Equal(AgentState.Done, hub.Sessions.Single().State);

        hub.Ingest("claude", null, """{"session_id":"a","hook_event_name":"SessionEnd"}""");
        Assert.Empty(hub.Sessions);
    }

    [Fact]
    public async System.Threading.Tasks.Task HttpEndpoint_Accepts_Hooks_AndRejectsBrowsers()
    {
        using var hub = new AgentHubService();
        hub.Start();
        if (!hub.IsRunning) return; // port taken on this machine; logic is covered above

        using var http = new System.Net.Http.HttpClient();
        var ok = await http.PostAsync(AgentHubService.Endpoint + "?kind=claude",
            new System.Net.Http.StringContent("""{"session_id":"h","hook_event_name":"UserPromptSubmit"}"""));
        Assert.Equal(System.Net.HttpStatusCode.NoContent, ok.StatusCode);
        Assert.Equal("", await ok.Content.ReadAsStringAsync());
        Assert.Equal(AgentState.Working, hub.Sessions.Single().State);

        var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, AgentHubService.Endpoint)
        {
            Content = new System.Net.Http.StringContent("""{"event":"done"}"""),
        };
        req.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await http.SendAsync(req)).StatusCode);
    }

    [Fact]
    public void CodexNotify_And_CustomEvents()
    {
        var hub = new AgentHubService();
        hub.Ingest("codex", null, """{"type":"agent-turn-complete","thread-id":"t1","last-assistant-message":"All tests pass"}""");
        hub.Ingest("custom", "build-bot", """{"event":"error","message":"compile failed"}""");

        var codex = hub.Sessions.Single(x => x.Kind == AgentKind.Codex);
        Assert.Equal(AgentState.Done, codex.State);
        Assert.Equal("All tests pass", codex.Activity);
        var bot = hub.Sessions.Single(x => x.Kind == AgentKind.Custom);
        Assert.Equal(AgentState.Error, bot.State);
        Assert.Equal("build-bot", bot.KindLabel);
    }

    [Fact]
    public void ClaudeInstaller_IsIdempotent_BacksUp_AndKeepsUserHooks()
    {
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "settings.json");
        File.WriteAllText(path, """
            {
              "model": "opus",
              "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "echo mine" } ] } ] }
            }
            """);

        AgentHookInstaller.InstallClaude(path);
        AgentHookInstaller.InstallClaude(path); // second run adds nothing
        Assert.True(File.Exists(path + ".xnotch-backup"));

        var root = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal("opus", root["model"]!.GetValue<string>());
        Assert.Equal(2, root["hooks"]!["Stop"]!.AsArray().Count); // user's + ours
        Assert.Single(root["hooks"]!["PreToolUse"]!.AsArray());

        AgentHookInstaller.RemoveClaude(path);
        root = JsonNode.Parse(File.ReadAllText(path))!;
        var stop = root["hooks"]!["Stop"]!.AsArray();
        Assert.Single(stop);
        Assert.Contains("echo mine", stop.ToJsonString());
        Assert.Null(root["hooks"]!["PreToolUse"]);
        dir.Delete(true);
    }

    [Fact]
    public void ClaudeInstaller_LeavesUnparseableFileAlone()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "{ not json");
        Assert.Contains("left alone", AgentHookInstaller.InstallClaude(path));
        Assert.Equal("{ not json", File.ReadAllText(path));
        File.Delete(path);
    }

    [Fact]
    public void CodexInstaller_PutsNotifyFirst_AndRespectsExistingNotify()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "model = \"gpt-5\"\n[profiles.x]\nmodel = \"y\"\n");
        AgentHookInstaller.InstallCodex(path);
        Assert.StartsWith("notify = [\"curl\"", File.ReadAllLines(path)[0]);
        AgentHookInstaller.RemoveCodex(path);
        Assert.DoesNotContain("notify", File.ReadAllText(path));

        File.WriteAllText(path, "notify = [\"my-script\"]\n");
        Assert.Contains("left alone", AgentHookInstaller.InstallCodex(path));
        File.Delete(path);
    }
}

public class DockTests
{
    [Fact]
    public void RunningApps_And_ShellIcons_Work_WithoutTouchingTheTaskbar()
    {
        var running = XNotch.Views.RunningApps.Enumerate();
        Assert.All(running.Keys, p => Assert.True(Path.IsPathRooted(p)));

        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        Assert.NotNull(XNotch.Views.ShellIcon.Get(explorer));
        Assert.False(XNotch.Views.TaskbarHelper.IsHiddenByUs);
    }
}
