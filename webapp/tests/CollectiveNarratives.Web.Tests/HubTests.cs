using System.Net;
using System.Net.Http.Json;
using CollectiveNarratives.Web.Sessions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace CollectiveNarratives.Web.Tests;

// Tegen een echte server: game, groot scherm en telefoon via SignalR, zoals in de zaal.
public class HubTests : IAsyncLifetime
{
    TestServer _server = null!;

    public async Task InitializeAsync() => _server = await TestServer.Start();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Without_game_key_no_session_can_be_created()
    {
        await using var noKey = await _server.Connect();
        await using var wrongKey = await _server.Connect("verkeerd");

        await Assert.ThrowsAsync<HubException>(() => noKey.InvokeAsync<CreateSessionResult>("CreateSession"));
        await Assert.ThrowsAsync<HubException>(() => wrongKey.InvokeAsync<CreateSessionResult>("CreateSession"));
    }

    [Fact]
    public async Task Full_flow_pair_lobby_vote()
    {
        // Game maakt een sessie, de code staat "in de bril".
        await using var game = await _server.Connect(TestServer.GameKey);
        var paired = new TaskCompletionSource<ScreenPairedEvent>();
        var ended = new TaskCompletionSource<VoteResult>();
        var viewers = new TaskCompletionSource<ViewersChangedEvent>();
        game.On<ScreenPairedEvent>("ScreenPaired", e => paired.TrySetResult(e));
        game.On<VoteResult>("VoteEnded", r => ended.TrySetResult(r));
        game.On<ViewersChangedEvent>("ViewersChanged", e => viewers.TrySetResult(e));
        var session = await game.InvokeAsync<CreateSessionResult>("CreateSession");
        Assert.Equal($"https://collective-narratives.app/poll/{session.Code}", session.JoinUrl);

        // Telefoon te vroeg: nog geen groot scherm.
        await using var phone = await _server.Connect();
        var phoneStates = new List<SessionState>();
        phone.On<SessionState>("State", s => { lock (phoneStates) phoneStates.Add(s); });
        Assert.Equal(JoinErrors.NotReady, (await phone.InvokeAsync<JoinResult>("JoinAsPhone", session.Code, "phone-1")).Error);

        // Groot scherm typt de code: gekoppeld.
        await using var screen = await _server.Connect();
        var screenState = new TaskCompletionSource<SessionState>();
        screen.On<SessionState>("State", s => { if (s.LobbyRemaining is not null) screenState.TrySetResult(s); });
        Assert.True((await screen.InvokeAsync<JoinResult>("JoinAsScreen", session.Code)).Ok);
        Assert.Equal(1, (await paired.Task.Within()).Screens);

        // Tutorial + wachtkamer: het scherm krijgt de aftelling.
        await game.InvokeAsync("StartTutorial", 60);
        var lobby = await screenState.Task.Within();
        Assert.Equal("lobby", lobby.Phase);
        Assert.InRange(lobby.LobbyRemaining!.Value, 55, 60);

        // Telefoon joint nu wel.
        Assert.True((await phone.InvokeAsync<JoinResult>("JoinAsPhone", session.Code, "phone-1")).Ok);
        Assert.Equal(1, (await viewers.Task.Within()).Viewers);

        // Spel start, stemming van 1 seconde.
        await game.InvokeAsync("StartGame");
        int voteId = await game.InvokeAsync<int>("StartVote", "telefoon", null, null, 1, "");
        await phone.InvokeAsync("CastVote", voteId, "rinkelen");

        var result = await ended.Task.Within();
        Assert.Equal(voteId, result.VoteId);
        Assert.Equal("rinkelen", result.Winner);
        Assert.Equal("publiek", result.DecidedBy);
        lock (phoneStates) Assert.Contains(phoneStates, s => s.Active && s.Vote!.Question.StartsWith("The phone is ringing"));
    }

    [Fact]
    public async Task Big_screen_can_skip_the_lobby_and_screen_sees_when_the_game_drops()
    {
        var game = await _server.Connect(TestServer.GameKey);
        var skipped = new TaskCompletionSource<LobbySkippedEvent>();
        game.On<LobbySkippedEvent>("LobbySkipped", e => skipped.TrySetResult(e));
        var session = await game.InvokeAsync<CreateSessionResult>("CreateSession");

        await using var screen = await _server.Connect();
        var gameGone = new TaskCompletionSource<SessionState>();
        screen.On<SessionState>("State", s => { if (!s.GameConnected) gameGone.TrySetResult(s); });
        await screen.InvokeAsync<JoinResult>("JoinAsScreen", session.Code);
        await game.InvokeAsync("StartTutorial", 60);

        // Een telefoon mag niet overslaan, het grote scherm wel.
        await using var phone = await _server.Connect();
        await phone.InvokeAsync<JoinResult>("JoinAsPhone", session.Code, "phone-1");
        await phone.InvokeAsync("SkipLobby");
        Assert.False(skipped.Task.IsCompleted);
        await screen.InvokeAsync("SkipLobby");
        Assert.Equal(1, (await skipped.Task.Within()).Viewers);

        // Game crasht: het scherm krijgt een state met GameConnected = false.
        await game.DisposeAsync();
        Assert.Equal("lobby", (await gameGone.Task.Within()).Phase);
    }

    [Fact]
    public async Task Restarted_game_moves_the_screen_but_the_old_code_reveals_nothing()
    {
        async Task<HubConnection> Game() =>
            await ConnectTo(_server.HubUrl + "?device=quest-a", TestServer.GameKey);

        var game = await Game();
        var old = await game.InvokeAsync<CreateSessionResult>("CreateSession");
        await using var screen = await _server.Connect();
        var moved = new TaskCompletionSource<SessionMovedEvent>();
        screen.On<SessionMovedEvent>("SessionMoved", e => moved.TrySetResult(e));
        var joined = await screen.InvokeAsync<JoinResult>("JoinAsScreen", old.Code);
        Assert.False(string.IsNullOrEmpty(joined.Key));

        await game.DisposeAsync(); // crash
        await using var restarted = await Game();
        var fresh = await restarted.InvokeAsync<CreateSessionResult>("CreateSession");

        Assert.Equal(fresh.Code, (await moved.Task.Within()).Code);
        // Publiek: alleen "ended", geen nieuwe code. Met de schermsleutel wel.
        var status = await _server.Http.GetStringAsync($"/api/sessions/{old.Code}").ContinueWith(t => t.IsFaulted ? "" : t.Result);
        Assert.DoesNotContain(fresh.Code, status);
        Assert.Equal(HttpStatusCode.NoContent, (await _server.Http.GetAsync($"/api/sessions/{old.Code}/next?screenKey=raden")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _server.Http.GetAsync($"/api/sessions/{old.Code}/next?clientId=onbekend")).StatusCode);
        var next = await _server.Http.GetFromJsonAsync<NextResponse>($"/api/sessions/{old.Code}/next?screenKey={Uri.EscapeDataString(joined.Key)}");
        Assert.Equal(fresh.Code, next!.Next);
    }

    sealed record NextResponse(string Next);

    static async Task<HubConnection> ConnectTo(string url, string accessToken)
    {
        var conn = new HubConnectionBuilder().WithUrl(url, o => o.AccessTokenProvider = () => Task.FromResult<string?>(accessToken)).Build();
        await conn.StartAsync();
        return conn;
    }

    [Fact]
    public async Task Ended_session_tells_everyone_and_refuses_new_joins()
    {
        await using var game = await _server.Connect(TestServer.GameKey);
        var session = await game.InvokeAsync<CreateSessionResult>("CreateSession");
        await using var screen = await _server.Connect();
        var endedOnScreen = new TaskCompletionSource();
        screen.On("SessionEnded", () => endedOnScreen.TrySetResult());
        await screen.InvokeAsync<JoinResult>("JoinAsScreen", session.Code);

        await game.InvokeAsync("EndSession");

        await endedOnScreen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using var late = await _server.Connect();
        Assert.Equal(JoinErrors.Ended, (await late.InvokeAsync<JoinResult>("JoinAsPhone", session.Code, "x")).Error);
        await Assert.ThrowsAsync<HubException>(() => game.InvokeAsync("StartGame"));
    }

    [Fact]
    public async Task Game_resumes_after_reconnecting()
    {
        var game = await _server.Connect(TestServer.GameKey);
        var session = await game.InvokeAsync<CreateSessionResult>("CreateSession");
        await game.DisposeAsync(); // wifi even weg

        await using var again = await _server.Connect(TestServer.GameKey);
        Assert.False(await again.InvokeAsync<bool>("ResumeSession", session.Code, "verkeerd-token"));
        Assert.True(await again.InvokeAsync<bool>("ResumeSession", session.Code, session.GameToken));

        await using var screen = await _server.Connect();
        var paired = new TaskCompletionSource<ScreenPairedEvent>();
        again.On<ScreenPairedEvent>("ScreenPaired", e => paired.TrySetResult(e));
        await screen.InvokeAsync<JoinResult>("JoinAsScreen", session.Code);
        await paired.Task.Within();
    }

    [Fact]
    public async Task Unknown_codes_are_refused_and_guessing_gets_blocked()
    {
        await using var phone = await _server.Connect();

        Assert.Equal(JoinErrors.Invalid, (await phone.InvokeAsync<JoinResult>("JoinAsPhone", "12ab56", "x")).Error);
        string error = "";
        for (int i = 0; i < 25; i++) error = (await phone.InvokeAsync<JoinResult>("JoinAsPhone", (100000 + i).ToString(), "x")).Error;

        Assert.Equal(JoinErrors.RateLimited, error);
    }

    // ---------- HTTP ----------

    [Fact]
    public async Task Healthz_says_OK()
    {
        Assert.Equal("OK", await _server.Http.GetStringAsync("/healthz"));
    }

    [Fact]
    public async Task Session_check_endpoint_knows_active_and_unknown_codes()
    {
        await using var game = await _server.Connect(TestServer.GameKey);
        var session = await game.InvokeAsync<CreateSessionResult>("CreateSession");

        var active = await _server.Http.GetAsync($"/api/sessions/{session.Code}");
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        Assert.Equal("active", (await active.Content.ReadFromJsonAsync<StatusBody>())!.Status);

        string unknownCode = session.Code == "999999" ? "999998" : "999999";
        Assert.Equal(HttpStatusCode.NotFound, (await _server.Http.GetAsync($"/api/sessions/{unknownCode}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _server.Http.GetAsync("/api/sessions/abc")).StatusCode);
    }

    [Fact]
    public async Task Qr_code_is_a_png()
    {
        var res = await _server.Http.GetAsync("/api/qr/365895.png");

        Assert.Equal("image/png", res.Content.Headers.ContentType!.MediaType);
        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], bytes[..4]);
    }

    [Theory]
    [InlineData("/", "Enter the code")]
    [InlineData("/poll/365895", "You are fate")]
    [InlineData("/screen", "Pair the big screen")]
    [InlineData("/screen/365895", "Pair the big screen")]
    [InlineData("/host", "FAKE GAME")]
    public async Task Pages_are_served(string path, string text)
    {
        var html = await _server.Http.GetStringAsync(path);

        Assert.Contains(text, html);
    }

    // ---------- Zicht van de speler ----------

    static readonly byte[] FakeJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 0xFF, 0xD9];

    static HttpRequestMessage FramePost(string code, string? token, byte[] body)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{code}/frame") { Content = new ByteArrayContent(body) };
        msg.Content.Headers.ContentType = new("image/jpeg");
        if (token is not null) msg.Headers.Add("X-Game-Token", token);
        return msg;
    }

    [Fact]
    public async Task Game_frames_reach_the_big_screen()
    {
        await using var game = await _server.Connect(TestServer.GameKey);
        var session = await game.InvokeAsync<CreateSessionResult>("CreateSession");
        Assert.Equal(HttpStatusCode.NoContent, (await _server.Http.GetAsync($"/api/sessions/{session.Code}/frame.jpg")).StatusCode);

        var posted = await _server.Http.SendAsync(FramePost(session.Code, session.GameToken, FakeJpeg));

        Assert.Equal(HttpStatusCode.NoContent, posted.StatusCode);
        var frame = await _server.Http.GetAsync($"/api/sessions/{session.Code}/frame.jpg");
        Assert.Equal("image/jpeg", frame.Content.Headers.ContentType!.MediaType);
        Assert.Equal(FakeJpeg, await frame.Content.ReadAsByteArrayAsync());
        Assert.True(frame.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task Only_the_game_can_send_frames_and_only_jpegs()
    {
        await using var game = await _server.Connect(TestServer.GameKey);
        var session = await game.InvokeAsync<CreateSessionResult>("CreateSession");

        Assert.Equal(HttpStatusCode.Unauthorized, (await _server.Http.SendAsync(FramePost(session.Code, null, FakeJpeg))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _server.Http.SendAsync(FramePost(session.Code, "verkeerd", FakeJpeg))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _server.Http.SendAsync(FramePost(session.Code, session.GameToken, [1, 2, 3, 4]))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await _server.Http.SendAsync(FramePost(session.Code, session.GameToken, new byte[WebApp.MaxFrameBytes + 1]))).StatusCode);
        string other = session.Code == "999999" ? "999998" : "999999";
        Assert.Equal(HttpStatusCode.NotFound, (await _server.Http.SendAsync(FramePost(other, session.GameToken, FakeJpeg))).StatusCode);
    }

    sealed record StatusBody(string Status);
}
