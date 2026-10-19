using CollectiveNarratives.Net;
using CollectiveNarratives.Web.Sessions;
using Microsoft.AspNetCore.SignalR.Client;

namespace CollectiveNarratives.Web.Tests;

// De zelfgeschreven SignalR-client uit Unity (CN_VR/Assets/ApplicationConnection/Scripts/Net) tegen de echte server.
public class UnityClientTests : IAsyncLifetime
{
    TestServer _server = null!;

    public async Task InitializeAsync() => _server = await TestServer.Start();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Unity_client_creates_a_session_and_receives_events()
    {
        using var unity = new SignalRConnection();
        var events = new List<(string Target, string Arg)>();
        var gotPaired = new TaskCompletionSource();
        var gotVoteEnded = new TaskCompletionSource<string>();
        unity.Received += (target, arg) =>
        {
            lock (events) events.Add((target, arg));
            if (target == "ScreenPaired") gotPaired.TrySetResult();
            if (target == "VoteEnded") gotVoteEnded.TrySetResult(arg);
        };
        await unity.ConnectAsync(_server.HubUrl, TestServer.GameKey);

        var created = MiniJson.ParseObject(await unity.InvokeAsync("CreateSession"));
        string code = MiniJson.ReadString(created["code"]);
        Assert.True(JoinGuard.IsValidCode(code));
        Assert.EndsWith("/poll/" + code, MiniJson.ReadString(created["joinUrl"]));

        // Groot scherm (gewone .NET-client) koppelt.
        await using var screen = await _server.Connect();
        await screen.InvokeAsync<JoinResult>("JoinAsScreen", code);
        await gotPaired.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await unity.InvokeAsync("StartTutorial", MiniJson.Number(60));
        await unity.InvokeAsync("StartGame");

        // Eigen vraag met tekens die ge-escaped moeten worden.
        string options = "[{\"id\":\"a\",\"label\":\"Optie \\\"A\\\"\",\"count\":0},{\"id\":\"b\",\"label\":\"Optie B\",\"count\":0}]";
        string voteIdRaw = await unity.InvokeAsync("StartVote", MiniJson.Quote("test"), MiniJson.Quote("Wat \"nu\"?\nkies"), options, MiniJson.Number(1), MiniJson.Quote(""));
        int voteId = MiniJson.ReadInt(voteIdRaw);

        await using var phone = await _server.Connect();
        Assert.True((await phone.InvokeAsync<JoinResult>("JoinAsPhone", code, "p1")).Ok);
        await phone.InvokeAsync("CastVote", voteId, "a");

        var result = MiniJson.ParseObject(await gotVoteEnded.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(voteId, MiniJson.ReadInt(result["voteId"]));
        Assert.Equal("a", MiniJson.ReadString(result["winner"]));
        Assert.Equal("Wat \"nu\"?\nkies", MiniJson.ReadString(result["question"]));
        lock (events) Assert.Contains(events, e => e.Target == "ViewersChanged");
    }

    [Fact]
    public async Task Server_errors_come_back_as_SignalRException()
    {
        using var unity = new SignalRConnection();
        await unity.ConnectAsync(_server.HubUrl, "verkeerde-sleutel");

        var e = await Assert.ThrowsAsync<SignalRException>(() => unity.InvokeAsync("CreateSession"));
        Assert.Contains("game key", e.Message);
        Assert.True(unity.IsConnected); // een fout sluit de verbinding niet
    }

    [Fact]
    public async Task Closed_fires_when_the_server_stops()
    {
        using var unity = new SignalRConnection();
        var closed = new TaskCompletionSource<string?>();
        unity.Closed += error => closed.TrySetResult(error);
        await unity.ConnectAsync(_server.HubUrl, TestServer.GameKey);

        await _server.App.StopAsync();

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(unity.IsConnected);
        await Assert.ThrowsAsync<SignalRException>(() => unity.InvokeAsync("CreateSession"));
    }
}

public class MiniJsonTests
{
    [Fact]
    public void Splits_a_SignalR_message_into_raw_parts()
    {
        var msg = MiniJson.ParseObject("{\"type\":1,\"target\":\"VoteEnded\",\"arguments\":[{\"voteId\":3,\"options\":[{\"id\":\"a\"}],\"winner\":\"\"}]}");

        Assert.Equal(1, MiniJson.ReadInt(msg["type"]));
        Assert.Equal("VoteEnded", MiniJson.ReadString(msg["target"]));
        var args = MiniJson.ParseArray(msg["arguments"]);
        Assert.Equal("{\"voteId\":3,\"options\":[{\"id\":\"a\"}],\"winner\":\"\"}", Assert.Single(args));
    }

    [Theory]
    [InlineData("gewoon")]
    [InlineData("met \"aanhalingstekens\" en \\ backslash")]
    [InlineData("nieuwe\nregel\ten tab")]
    [InlineData("ünïcödé ★ 🎁")]
    [InlineData("")]
    public void Quote_and_ReadString_round_trip(string text) => Assert.Equal(text, MiniJson.ReadString(MiniJson.Quote(text)));

    [Fact]
    public void Handles_whitespace_nulls_and_braces_inside_strings()
    {
        var msg = MiniJson.ParseObject(" { \"a\" : null , \"b\" : \"}{][\" , \"c\" : [ 1 , 2 ] , \"d\":true } ");

        Assert.Null(MiniJson.ReadString(msg["a"]));
        Assert.Equal("}{][", MiniJson.ReadString(msg["b"]));
        Assert.Equal(["1", "2"], MiniJson.ParseArray(msg["c"]));
        Assert.True(MiniJson.ReadBool(msg["d"]));
        Assert.Empty(MiniJson.ParseObject("{}"));
        Assert.Equal("\u00e9", MiniJson.ReadString("\"\\u00e9\""));
    }
}
