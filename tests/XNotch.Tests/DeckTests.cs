using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using XNotch.Services;
using Xunit;

namespace XNotch.Tests;

public class DeckTests
{
    [Fact]
    public void Pairing_OnlyInsideWindow_OneShot_LockoutAndRevoke()
    {
        var devices = new List<DeckDevice>();
        int saves = 0;
        var p = new DeckPairing(devices, () => saves++);

        Assert.Null(p.Pair("123456", "x")); // window closed

        var code = p.Open();
        string wrong = code == "000000" ? "111111" : "000000";
        for (int i = 0; i < DeckPairing.MaxFailures; i++) Assert.Null(p.Pair(wrong, "x"));
        Assert.False(p.IsOpen);               // too many wrong codes closes pairing
        Assert.Null(p.Pair(code, "x"));       // even the right code is dead now

        code = p.Open();
        var token = p.Pair(code, "Pixel 9");
        Assert.NotNull(token);
        Assert.Null(p.Pair(code, "again"));   // one phone per window
        Assert.Single(devices);
        Assert.DoesNotContain(token!, JsonSerializer.Serialize(devices)); // only the hash is stored
        Assert.Equal("Pixel 9", p.Authenticate(token)!.Name);
        Assert.Null(p.Authenticate(token + "x"));
        Assert.Null(p.Authenticate(null));

        p.Revoke(devices[0].Id);
        Assert.Null(p.Authenticate(token));
        Assert.True(saves >= 2);
    }

    [Fact]
    public async Task Server_ServesPage_RequiresToken_Pairs_AndThrottles()
    {
        var p = new DeckPairing([], () => { });
        await using var server = new DeckServer(p, new DeckHost { Status = () => new { title = "Song" } });
        int port = 47900 + Random.Shared.Next(0, 90);
        await server.StartAsync(port, loopbackOnly: true);
        Assert.Equal(DeckState.On, server.State);

        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
        Assert.Contains("X-Notch Deck", await http.GetStringAsync("/"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/status")).StatusCode);

        var code = p.Open();
        var pair = await http.PostAsync("/api/pair", Json(new { code, name = "Test phone" }));
        Assert.Equal(HttpStatusCode.OK, pair.StatusCode);
        var token = JsonDocument.Parse(await pair.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString();

        // second attempt inside 1 s is throttled
        Assert.Equal((HttpStatusCode)429, (await http.PostAsync("/api/pair", Json(new { code = "000000" }))).StatusCode);

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/status");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var status = await http.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Contains("Song", await status.Content.ReadAsStringAsync());

        var bad = new HttpRequestMessage(HttpMethod.Get, "/api/status");
        bad.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(bad)).StatusCode);

        await server.StopAsync();
        Assert.Equal(DeckState.Off, server.State);
    }

    private static StringContent Json(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");
}

public class DeckNetworkTests
{
    [Fact]
    public void NetworkListManager_IsReadable()
    {
        var cats = DeckServer.NetworkCategories(); // must not throw, or the Deck would never start
        Assert.All(cats, c => Assert.InRange(c, 0, 2));
    }
}
