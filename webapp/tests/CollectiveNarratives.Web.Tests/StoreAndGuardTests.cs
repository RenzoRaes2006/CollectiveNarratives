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
