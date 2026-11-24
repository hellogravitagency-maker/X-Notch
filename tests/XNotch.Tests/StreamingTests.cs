using System;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using XNotch.Models;
using XNotch.Services;
using Xunit;

namespace XNotch.Tests;

public class StreamingTests
{
    [Fact]
    public void Twitch_ParsesTaggedMessage_Action_AndIgnoresOthers()
    {
        var m = ChatService.ParseTwitch("@badge-info=;color=#1E90FF;display-name=Cool\\sGuy;emotes= :coolguy!coolguy@coolguy.tmi.twitch.tv PRIVMSG #chan :hello there :)");
        Assert.NotNull(m);
        Assert.Equal("Cool Guy", m!.User);
        Assert.Equal("#1E90FF", m.Color);
        Assert.Equal("hello there :)", m.Text);

        var me = ChatService.ParseTwitch(":bob!bob@bob.tmi.twitch.tv PRIVMSG #chan :\u0001ACTION waves\u0001");
        Assert.Equal("bob", me!.User);
        Assert.Equal("waves", me.Text);

        Assert.Null(ChatService.ParseTwitch(":tmi.twitch.tv 001 justinfan123 :Welcome, GLHF!"));
        Assert.Null(ChatService.ParseTwitch("PING :tmi.twitch.tv"));
    }

    [Fact]
    public void Obs_Auth_MatchesProtocolDocsExample() =>
        Assert.Equal("1Ct943GAT+6YQUUX47Ia/ncufilbe6+oD6lY+5kaCu4=",
            ObsService.AuthString("supersecretpassword", "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=", "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY="));

    [Fact]
    public void Secrets_RoundTrip_AndAreNotPlaintext()
    {
        var stored = AppSettings.Protect("hunter2-obs");
        Assert.DoesNotContain("hunter2", stored);
        Assert.Equal("hunter2-obs", AppSettings.Unprotect(stored));
        Assert.Null(AppSettings.Unprotect("not-base64!"));
        Assert.Null(AppSettings.Protect(""));
    }

    [Theory]
    [InlineData("pw", true)]
    [InlineData("wrong", false)]
    public async Task Obs_ConnectsToFakeServer_ReadsState(string password, bool shouldConnect)
    {
        int port = 48000 + Random.Shared.Next(0, 500);
        await using var fake = await FakeObs.StartAsync(port, "pw");
        using var obs = new ObsService();
        obs.Configure(true, port, password);

        for (int i = 0; i < 50 && !(obs.Connected && obs.CurrentScene != null) && !obs.AuthFailed; i++) await Task.Delay(100);

        if (!shouldConnect) { Assert.True(obs.AuthFailed); Assert.False(obs.Connected); return; }
        Assert.True(obs.Connected);
        Assert.True(obs.Streaming);
        Assert.False(obs.Recording);
        Assert.Equal("Game", obs.CurrentScene);
        Assert.Equal(new[] { "Game", "Chat" }, obs.Scenes.ToArray()); // OBS lists bottom-up; we show top-down
        Assert.NotNull(obs.LiveSince);
        Assert.InRange((DateTime.Now - obs.LiveSince!.Value).TotalSeconds, 60, 75);
    }

    /// <summary>Minimal obs-websocket v5 server: Hello(auth) → Identify → Identified → requests.</summary>
    private sealed class FakeObs : IAsyncDisposable
    {
        private WebApplication _app = null!;

        public static async Task<FakeObs> StartAsync(int port, string password)
        {
            var f = new FakeObs();
            var b = WebApplication.CreateSlimBuilder();
            b.Logging.ClearProviders();
            b.WebHost.UseKestrel(o => o.Listen(IPAddress.Loopback, port));
            f._app = b.Build();
            f._app.UseWebSockets();
            f._app.Map("/", async (HttpContext ctx) =>
            {
                using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                const string salt = "c2FsdA==", challenge = "Y2hhbGxlbmdl";
                await Send(ws, new JsonObject { ["op"] = 0, ["d"] = new JsonObject { ["rpcVersion"] = 1, ["authentication"] = new JsonObject { ["salt"] = salt, ["challenge"] = challenge } } });
                var identify = await Receive(ws);
                if (identify?["d"]?["authentication"]?.GetValue<string>() != ObsService.AuthString(password, salt, challenge))
                {
                    await ws.CloseAsync((WebSocketCloseStatus)4009, "auth failed", CancellationToken.None);
                    return;
                }
                await Send(ws, new JsonObject { ["op"] = 2, ["d"] = new JsonObject { ["negotiatedRpcVersion"] = 1 } });
                while (ws.State == WebSocketState.Open)
                {
                    var req = await Receive(ws);
                    if (req == null) break;
                    var type = req["d"]!["requestType"]!.GetValue<string>();
                    JsonObject data = type switch
                    {
                        "GetStreamStatus" => new JsonObject { ["outputActive"] = true, ["outputDuration"] = 65000 },
                        "GetRecordStatus" => new JsonObject { ["outputActive"] = false, ["outputDuration"] = 0 },
                        "GetSceneList" => new JsonObject { ["currentProgramSceneName"] = "Game", ["scenes"] = new JsonArray(new JsonObject { ["sceneName"] = "Chat" }, new JsonObject { ["sceneName"] = "Game" }) },
                        _ => new JsonObject(),
                    };
                    await Send(ws, new JsonObject { ["op"] = 7, ["d"] = new JsonObject { ["requestType"] = type, ["requestId"] = req["d"]!["requestId"]!.GetValue<string>(), ["requestStatus"] = new JsonObject { ["result"] = true, ["code"] = 100 }, ["responseData"] = data } });
                }
            });
            await f._app.StartAsync();
            return f;
        }

        private static Task Send(WebSocket ws, JsonNode n) => ws.SendAsync(Encoding.UTF8.GetBytes(n.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None);

        private static async Task<JsonNode?> Receive(WebSocket ws)
        {
            var buf = new byte[8192];
            var r = await ws.ReceiveAsync(buf, CancellationToken.None);
            return r.MessageType == WebSocketMessageType.Close ? null : JsonNode.Parse(Encoding.UTF8.GetString(buf, 0, r.Count));
        }

        public async ValueTask DisposeAsync() { await _app.StopAsync(); await _app.DisposeAsync(); }
    }
}
