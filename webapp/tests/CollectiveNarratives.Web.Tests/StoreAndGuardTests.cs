using CollectiveNarratives.Web.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace CollectiveNarratives.Web.Tests;

public class SessionStoreTests
{
    readonly FakeTimeProvider _time = new();
    readonly RecordingEvents _events = new();
    readonly SessionStore _store;

    public SessionStoreTests() => _store = new SessionStore(_time, _events, NullLoggerFactory.Instance);

    [Fact]
    public void Codes_are_six_digits_and_unique()
    {
        var codes = Enumerable.Range(0, 500).Select(i => _store.Create("https://x", $"g{i}").Code).ToList();

        Assert.All(codes, c => Assert.True(JoinGuard.IsValidCode(c), c));
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void Links_point_to_the_public_site()
    {
        var s = _store.Create("https://collective-narratives.app/", "g");

        Assert.Equal($"https://collective-narratives.app/poll/{s.Code}", s.JoinUrl);
        Assert.Equal($"https://collective-narratives.app/screen/{s.Code}", s.ScreenUrl);
    }

    [Fact]
    public async Task Ended_code_stops_working_but_is_remembered()
    {
        var s = _store.Create("https://x", "g");
        Assert.Equal(CodeStatus.Active, _store.Status(s.Code));

        await _store.End(s);

        Assert.Equal(CodeStatus.Ended, _store.Status(s.Code));
        Assert.False(_store.TryGet(s.Code, out _));
        string other = s.Code == "999999" ? "999998" : "999999";
        Assert.Equal(CodeStatus.Unknown, _store.Status(other));
    }

    [Fact]
    public async Task Cleanup_ends_sessions_whose_game_is_gone_and_frees_codes_after_a_day()
    {
        var s = _store.Create("https://x", "game-conn");
        await s.Leave("game-conn");

        _time.Advance(TimeSpan.FromMinutes(3));
        await _store.Cleanup();
        Assert.Equal(CodeStatus.Ended, _store.Status(s.Code));
        Assert.Equal(1, _events.EndedCount);

        _time.Advance(TimeSpan.FromHours(25));
        await _store.Cleanup();
        Assert.Equal(CodeStatus.Unknown, _store.Status(s.Code));
    }

    [Fact]
    public async Task Cleanup_ends_sessions_older_than_two_hours()
    {
        var s = _store.Create("https://x", "game-conn");

        _time.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));
        await _store.Cleanup();

        Assert.Equal(CodeStatus.Ended, _store.Status(s.Code));
    }

    [Fact]
    public async Task Crashed_game_restart_moves_screen_and_players_but_tells_no_one_else()
    {
        var old = await _store.Start("https://x", "game-1", "quest-a");
        var screen = await old.JoinScreen("screen-1");
        await old.JoinPhone("phone-conn", "phone-1");
        await old.Leave("game-1"); // crash midden in het spel

        var fresh = await _store.Start("https://x", "game-2", "quest-a");

        Assert.Equal(CodeStatus.Ended, _store.Status(old.Code));
        Assert.Equal((old.Code, fresh.Code, true), Assert.Single(_events.Moves));
        Assert.Equal(fresh.Code, _store.NextCodeForScreen(old.Code, screen.Key));
        Assert.Equal(fresh.Code, _store.NextCodeForPhone(old.Code, "phone-1"));
        // Wie alleen de oude code kent (of een verkeerde sleutel heeft), krijgt niets.
        Assert.Null(_store.NextCodeForScreen(old.Code, "raden"));
        Assert.Null(_store.NextCodeForPhone(old.Code, "iemand-anders"));
    }

    [Fact]
    public async Task After_a_finished_game_only_the_big_screen_follows_to_the_next_round()
    {
        var old = await _store.Start("https://x", "game-1", "quest-a");
        var screen = await old.JoinScreen("screen-1");
        await old.JoinPhone("phone-conn", "phone-1");
        await old.SetEnding("held");

        var fresh = await _store.Start("https://x", "game-2", "quest-a");

        Assert.False(Assert.Single(_events.Moves).PhonesToo);
        Assert.Equal(fresh.Code, _store.NextCodeForScreen(old.Code, screen.Key));
        Assert.Null(_store.NextCodeForPhone(old.Code, "phone-1"));
    }

    [Fact]
    public async Task Phones_only_follow_a_quick_restart_and_screens_follow_several_restarts()
    {
        var a = await _store.Start("https://x", "g1", "quest-a");
        var screen = await a.JoinScreen("screen-1");
        await a.JoinPhone("phone-conn", "phone-1");
        await _store.End(a); // game netjes gestopt

        _time.Advance(SessionStore.PhoneFollowWindow + TimeSpan.FromMinutes(1));
        var b = await _store.Start("https://x", "g2", "quest-a");
        var c = await _store.Start("https://x", "g3", "quest-a");
        var other = await _store.Start("https://x", "g4", "quest-b");

        Assert.Null(_store.NextCodeForPhone(a.Code, "phone-1")); // te laat: misschien al een nieuwe groep
        Assert.Equal(c.Code, _store.NextCodeForScreen(a.Code, screen.Key));
        Assert.NotEqual(other.Code, c.Code);
    }

    [Fact]
    public async Task Long_stopped_game_does_not_pull_old_screens_along()
    {
        var a = await _store.Start("https://x", "g1", "quest-a");
        var screen = await a.JoinScreen("screen-1");
        await _store.End(a);

        _time.Advance(SessionStore.FollowWindow + TimeSpan.FromMinutes(1));
        await _store.Cleanup();
        await _store.Start("https://x", "g2", "quest-a");

        Assert.Null(_store.NextCodeForScreen(a.Code, screen.Key));
    }
}

public class JoinGuardTests
{
    readonly FakeTimeProvider _time = new();

    JoinGuard Guard(int limit = 20) =>
        new(_time, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["JOIN_FAILURES_PER_MINUTE"] = limit.ToString() }).Build());

    [Fact]
    public void Blocks_an_ip_after_too_many_wrong_codes_for_a_minute()
    {
        var guard = Guard(3);
        for (int i = 0; i < 3; i++)
        {
            Assert.False(guard.IsBlocked("1.2.3.4"));
            guard.RecordFailure("1.2.3.4");
        }

        Assert.True(guard.IsBlocked("1.2.3.4"));
        Assert.False(guard.IsBlocked("5.6.7.8")); // ander IP

        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.False(guard.IsBlocked("1.2.3.4"));
    }

    [Theory]
    [InlineData("365895", true)]
    [InlineData("100000", true)]
    [InlineData("012345", false)]
    [InlineData("12345", false)]
    [InlineData("1234567", false)]
    [InlineData("12a456", false)]
    [InlineData(null, false)]
    public void Valid_codes_have_six_digits(string? code, bool valid) => Assert.Equal(valid, JoinGuard.IsValidCode(code));
}
