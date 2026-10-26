using System.Collections.Concurrent;

namespace CollectiveNarratives.Web.Sessions;

// Tegen het raden van codes: telt per IP alleen de MISLUKTE pogingen (onbekende of verlopen code).
// Geslaagde joins tellen niet mee, want op het wifi van de zaal zitten veel telefoons achter hetzelfde IP.
public sealed class JoinGuard(TimeProvider time, IConfiguration config)
{
    readonly int _limit = config.GetValue("JOIN_FAILURES_PER_MINUTE", 20);
    readonly ConcurrentDictionary<string, Window> _windows = new();

    sealed record Window(DateTimeOffset Start, int Failures);

    public bool IsBlocked(string ip) =>
        _windows.TryGetValue(ip, out var w) && time.GetUtcNow() - w.Start < TimeSpan.FromMinutes(1) && w.Failures >= _limit;

    public void RecordFailure(string ip)
    {
        var now = time.GetUtcNow();
        _windows.AddOrUpdate(ip, _ => new Window(now, 1),
            (_, w) => now - w.Start >= TimeSpan.FromMinutes(1) ? new Window(now, 1) : w with { Failures = w.Failures + 1 });

        if (_windows.Count > 10_000) // af en toe opruimen
            foreach (var (key, w) in _windows)
                if (now - w.Start >= TimeSpan.FromMinutes(1)) _windows.TryRemove(key, out _);
    }

    public static bool IsValidCode(string? code) => code is { Length: 6 } && code.All(char.IsAsciiDigit) && code[0] != '0';
}
