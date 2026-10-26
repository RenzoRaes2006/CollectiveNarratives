using CollectiveNarratives.Web.Sessions;
using Microsoft.Extensions.Time.Testing;

namespace CollectiveNarratives.Web.Tests;

public class SessionTests
{
    readonly FakeTimeProvider _time = new();
    readonly RecordingEvents _events = new();

    // ---------- Koppelen en wachtkamer ----------

    [Fact]
    public async Task Phone_cannot_join_before_the_big_screen_is_paired()
    {
        var s = Make.Session(_time, _events);

        var result = await s.JoinPhone("p1", "client-1");

        Assert.False(result.Ok);
        Assert.Equal(JoinErrors.NotReady, result.Error);
        Assert.Equal(SessionPhase.Pairing, s.Phase);
    }

    [Fact]
    public async Task First_screen_pairs_the_session_and_tells_the_game()
    {
        var s = Make.Session(_time, _events);

        var result = await s.JoinScreen("screen-1");

        Assert.True(result.Ok);
        Assert.Equal(SessionPhase.Lobby, s.Phase);
        Assert.Equal(1, Assert.Single(_events.Game<ScreenPairedEvent>("ScreenPaired")).Screens);
        Assert.Equal("lobby", _events.LastState.Phase);
        Assert.True((await s.JoinPhone("p1", "client-1")).Ok);
    }

    [Fact]
    public async Task StartTutorial_needs_a_paired_screen()
    {
        var s = Make.Session(_time, _events);

        await Assert.ThrowsAsync<SessionException>(() => s.StartTutorial(60));
    }

    [Fact]
    public async Task Lobby_counts_down_from_StartTutorial()
    {
        var s = await Make.PairedSession(_time, _events);
        Assert.Null(s.GetState().LobbyRemaining); // speler maakt zich klaar

        await s.StartTutorial(60);
        _time.Advance(TimeSpan.FromSeconds(20));

        Assert.Equal(40, s.GetState().LobbyRemaining!.Value, precision: 3);
        _time.Advance(TimeSpan.FromSeconds(50));
        Assert.Equal(0, s.GetState().LobbyRemaining); // nooit negatief: "de speler is bijna klaar"
    }

    [Fact]
    public async Task StartGame_ends_the_lobby()
    {
        var s = await Make.PairedSession(_time, _events);
        await s.StartTutorial(60);

        await s.StartGame();

        var state = s.GetState();
        Assert.Equal("playing", state.Phase);
        Assert.Null(state.LobbyRemaining);
    }

    [Fact]
    public async Task SkipLobby_ends_the_countdown_and_tells_the_game_once()
    {
        var s = await Make.PairedSession(_time, _events);
        await s.StartTutorial(60);

        await s.SkipLobby();
        await s.SkipLobby();

        Assert.Equal(0, s.GetState().LobbyRemaining);
        Assert.Single(_events.Game<LobbySkippedEvent>("LobbySkipped"));
    }

    [Fact]
    public async Task SkipLobby_before_the_tutorial_still_skips_the_wait()
    {
        var s = await Make.PairedSession(_time, _events);

        await s.SkipLobby();
        await s.StartTutorial(60);

        Assert.Equal(0, s.GetState().LobbyRemaining);
    }

    [Fact]
    public async Task SkipLobby_does_nothing_once_the_game_runs()
    {
        var s = await Make.PairedSession(_time, _events);
        await s.StartGame();

        await s.SkipLobby();

        Assert.Empty(_events.Game<LobbySkippedEvent>("LobbySkipped"));
    }

    // ---------- Kijkers ----------

    [Fact]
    public async Task Same_phone_with_two_connections_is_one_viewer()
    {
        var s = await Make.PairedSession(_time, _events);

        await s.JoinPhone("conn-a", "phone-1");
        await s.JoinPhone("conn-b", "phone-1");
        await s.JoinPhone("conn-c", "phone-2");

        Assert.Equal(2, s.GetState().Viewers);
        Assert.Equal([1, 2], _events.Game<ViewersChangedEvent>("ViewersChanged").Select(e => e.Viewers));

        await s.Leave("conn-c");
        Assert.Equal(1, s.GetState().Viewers);
    }

    // ---------- Stemmen ----------

    [Fact]
    public async Task Majority_wins_and_the_last_press_counts()
    {
        var s = await Make.PairedSession(_time, _events);
        await s.JoinPhone("c1", "phone-1");
        await s.JoinPhone("c2", "phone-2");
        await s.JoinPhone("c3", "phone-3");

        int voteId = await s.StartVote("telefoon", null, null, 12, "");
        await s.CastVote("c1", voteId, "rinkelen");
        await s.CastVote("c1", voteId, "opnemen"); // van mening veranderd
        await s.CastVote("c2", voteId, "opnemen");
        await s.CastVote("c3", voteId, "rinkelen");
        Assert.True(s.GetState().Active);

        _time.Advance(TimeSpan.FromSeconds(12));

        var result = Assert.Single(_events.Game<VoteResult>("VoteEnded"));
        Assert.Equal(voteId, result.VoteId);
        Assert.Equal("opnemen", result.Winner);
        Assert.Equal("publiek", result.DecidedBy);
        Assert.Equal(3, result.Total);
        Assert.Equal([2, 1], result.Options.Select(o => o.Count));
        Assert.False(s.GetState().Active);
        Assert.Same(result, s.GetState().Last);
    }

    [Fact]
    public async Task Tie_or_no_votes_means_Kevin_decides()
    {
        var s = await Make.PairedSession(_time, _events);
        await s.JoinPhone("c1", "phone-1");
        await s.JoinPhone("c2", "phone-2");

        int tie = await s.StartVote("hond", null, null, 5, "");
        await s.CastVote("c1", tie, "binnen");
        await s.CastVote("c2", tie, "buiten");
        _time.Advance(TimeSpan.FromSeconds(5));

        int none = await s.StartVote("stroom", null, null, 5, "");
        _time.Advance(TimeSpan.FromSeconds(5));

        var results = _events.Game<VoteResult>("VoteEnded").ToList();
        Assert.All(results, r => Assert.Equal("", r.Winner));
        Assert.All(results, r => Assert.Equal("kevin", r.DecidedBy));

        await s.Decide(tie, "binnen");
        Assert.Equal("binnen", s.GetState().History[0].Winner);
        await Assert.ThrowsAsync<SessionException>(() => s.Decide(none, "nope"));
    }

    [Fact]
    public async Task Game_can_send_its_own_question_and_options()
    {
        var s = await Make.PairedSession(_time, _events);

        await s.StartVote("deur", "Welke deur?", [new("links", "Links"), new("rechts", "Rechts")], 8, "Snel!");

        var vote = s.GetState().Vote!;
        Assert.Equal("Welke deur?", vote.Question);
        Assert.Equal("Snel!", vote.Message);
        Assert.Equal(8, vote.Seconds);
        Assert.Equal(["links", "rechts"], vote.Options.Select(o => o.Id));
    }

    [Fact]
    public async Task Unknown_choice_or_bad_options_are_refused()
    {
        var s = await Make.PairedSession(_time, _events);

        await Assert.ThrowsAsync<SessionException>(() => s.StartVote("bestaat-niet", null, null, 12, ""));
        await Assert.ThrowsAsync<SessionException>(() => s.StartVote("x", "?", [new("a", "A")], 12, ""));
        await Assert.ThrowsAsync<SessionException>(() => s.StartVote("x", "?", [new("a", "A"), new("a", "B")], 12, ""));
    }

    [Fact]
    public async Task Votes_from_non_phones_or_for_old_votes_are_ignored()
    {
        var s = await Make.PairedSession(_time, _events);
        await s.JoinPhone("c1", "phone-1");
        int voteId = await s.StartVote("telefoon", null, null, 12, "");

        await s.CastVote("screen-1", voteId, "opnemen"); // geen telefoon
        await s.CastVote("c1", voteId + 1, "opnemen"); // verkeerde stemming
        await s.CastVote("c1", voteId, "bestaat-niet");

        Assert.Equal(0, s.GetState().Vote!.Total);
    }

    [Fact]
    public async Task New_vote_finishes_the_running_one_first()
    {
        var s = await Make.PairedSession(_time, _events);
        int first = await s.StartVote("telefoon", null, null, 12, "");

        int second = await s.StartVote("hond", null, null, 12, "");

        Assert.Equal(first, Assert.Single(_events.Game<VoteResult>("VoteEnded")).VoteId);
        Assert.Equal(second, s.GetState().Vote!.VoteId);
        _time.Advance(TimeSpan.FromSeconds(12)); // de oude timer mag niets meer doen
        Assert.Equal(2, _events.Game<VoteResult>("VoteEnded").Count());
    }

    // ---------- Samen-actie ----------

    [Fact]
    public async Task Together_succeeds_when_everyone_presses()
    {
        var s = await Make.PairedSession(_time, _events);
        await s.JoinPhone("c1", "phone-1");
        await s.JoinPhone("c2", "phone-2");

        var view = await s.StartTogether("Laat de hond blaffen", 5);
        Assert.Equal(2, view.Needed);
        await s.Press("c1");
        await s.Press("c1"); // twee keer drukken telt één keer
        Assert.Empty(_events.Game<TogetherResult>("TogetherEnded"));
        await s.Press("c2");

        var result = Assert.Single(_events.Game<TogetherResult>("TogetherEnded"));
        Assert.True(result.Success);
        Assert.Equal(1, result.Attempt);
        Assert.False(s.GetState().Together.Active);
    }

    [Fact]
    public async Task Together_fails_after_the_time()
    {
        var s = await Make.PairedSession(_time, _events);
        await s.JoinPhone("c1", "phone-1");
        await s.JoinPhone("c2", "phone-2");
        await s.StartTogether("Druk!", 5);
        await s.Press("c1");

        _time.Advance(TimeSpan.FromSeconds(5));

        var result = Assert.Single(_events.Game<TogetherResult>("TogetherEnded"));
        Assert.False(result.Success);
        Assert.Equal(1, result.Presses);
        Assert.Equal(2, result.Needed);
    }

    // ---------- Einde ----------

    [Fact]
    public async Task Ending_is_shown_and_End_closes_the_session()
    {
        var s = await Make.PairedSession(_time, _events);
        await s.SetEnding("held");
        Assert.Equal("held", _events.LastState.Ending);
        await Assert.ThrowsAsync<SessionException>(() => s.SetEnding("onbekend"));

        await s.End();

        Assert.Equal(SessionPhase.Ended, s.Phase);
        Assert.Equal(1, _events.EndedCount);
        Assert.Equal(JoinErrors.Ended, (await s.JoinPhone("c9", "late")).Error);
        Assert.Equal(JoinErrors.Ended, (await s.JoinScreen("screen-2")).Error);
        await Assert.ThrowsAsync<SessionException>(() => s.StartVote("telefoon", null, null, 12, ""));
    }

    [Fact]
    public async Task Frames_are_throttled_and_go_stale()
    {
        var s = await Make.PairedSession(_time, _events);
        byte[] a = [0xFF, 0xD8, 0xFF, 1], b = [0xFF, 0xD8, 0xFF, 2];

        Assert.True(s.SetFrame(a));
        Assert.False(s.SetFrame(b)); // te snel na het vorige
        _time.Advance(TimeSpan.FromMilliseconds(50));
        Assert.True(s.SetFrame(b));
        Assert.Same(b, s.GetFrame());

        _time.Advance(TimeSpan.FromSeconds(6)); // game stuurt niets meer
        Assert.Null(s.GetFrame());

        await s.End();
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(s.SetFrame(a));
    }

    [Fact]
    public async Task Game_can_resume_with_its_token_only()
    {
        var s = Make.Session(_time, _events);
        await s.Leave("game-conn");

        Assert.False(_events.LastState.GameConnected);

        Assert.False(await s.ResumeGame("new-conn", "wrong"));
        Assert.True(await s.ResumeGame("new-conn", "token"));
        Assert.True(_events.LastState.GameConnected);
    }

    [Fact]
    public async Task Session_expires_when_the_game_is_gone_too_long()
    {
        var s = Make.Session(_time, _events);
        var grace = TimeSpan.FromMinutes(2);
        var maxAge = TimeSpan.FromHours(2);

        await s.Leave("game-conn");
        _time.Advance(TimeSpan.FromSeconds(119));
        Assert.False(s.IsExpired(_time.GetUtcNow(), grace, maxAge));
        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(s.IsExpired(_time.GetUtcNow(), grace, maxAge));
    }
}
