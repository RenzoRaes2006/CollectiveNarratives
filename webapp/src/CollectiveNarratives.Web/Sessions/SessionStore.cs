using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace CollectiveNarratives.Web.Sessions;

public enum CodeStatus { Unknown, Active, Ended }

// Alle sessies, in het geheugen. Er draait één server, dus geen database nodig.
public sealed class SessionStore(TimeProvider time, ISessionEvents events, ILoggerFactory loggers)
{
    public static readonly TimeSpan GameGrace = TimeSpan.FromMinutes(2); // zo lang mag de game weg zijn
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(2);
    public static readonly TimeSpan CodeCooldown = TimeSpan.FromHours(24); // pas dan mag een code terugkomen

    readonly ConcurrentDictionary<string, Session> _active = new();
    readonly ConcurrentDictionary<string, DateTimeOffset> _retired = new(); // code -> wanneer vervallen
    readonly Lock _createLock = new();
    readonly ILogger _log = loggers.CreateLogger<SessionStore>();

    public int ActiveCount => _active.Count;

    public Session Create(string baseUrl, string gameConnectionId)
    {
        lock (_createLock)
        {
            string code;
            do code = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();
            while (_active.ContainsKey(code) || _retired.ContainsKey(code));

            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_');
            var session = new Session(code, token, baseUrl, gameConnectionId, time, events, loggers.CreateLogger($"Session.{code}"));
            _active[code] = session;
            _log.LogInformation("Sessie {Code} aangemaakt", code);
            return session;
        }
    }

    public bool TryGet(string code, out Session session) => _active.TryGetValue(code, out session!);

    public CodeStatus Status(string code) =>
        _active.TryGetValue(code, out var s) ? (s.Phase == SessionPhase.Ended ? CodeStatus.Ended : CodeStatus.Active)
        : _retired.ContainsKey(code) ? CodeStatus.Ended
        : CodeStatus.Unknown;

    public async Task End(Session session)
    {
        await session.End();
        Retire(session.Code);
    }

    void Retire(string code)
    {
        if (_active.TryRemove(code, out _)) _retired[code] = time.GetUtcNow();
    }

    // Periodiek opruimen: vervallen sessies beëindigen, oude codes terug in de pot.
    public async Task Cleanup()
    {
        var now = time.GetUtcNow();
        foreach (var session in _active.Values)
        {
            if (session.Phase == SessionPhase.Ended) Retire(session.Code);
            else if (session.IsExpired(now, GameGrace, MaxAge))
            {
                _log.LogInformation("Sessie {Code} vervalt", session.Code);
                await End(session);
            }
        }
        foreach (var (code, at) in _retired)
            if (now - at > CodeCooldown) _retired.TryRemove(code, out _);
    }
}

public sealed class SessionCleanupService(SessionStore store, TimeProvider time) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), time);
        while (await timer.WaitForNextTickAsync(stoppingToken)) await store.Cleanup();
    }
}
