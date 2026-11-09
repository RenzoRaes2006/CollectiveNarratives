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
    public static readonly TimeSpan FollowWindow = TimeSpan.FromMinutes(30); // zo lang na het einde volgt het grote scherm nog mee
    public static readonly TimeSpan PhoneFollowWindow = TimeSpan.FromMinutes(5); // telefoons: alleen bij een snelle herstart

    readonly ConcurrentDictionary<string, Session> _active = new();
    readonly ConcurrentDictionary<string, DateTimeOffset> _retired = new(); // code -> wanneer vervallen
    readonly ConcurrentDictionary<string, Session> _recentlyEnded = new(); // nog FollowWindow lang, voor de overdracht
    readonly ConcurrentDictionary<string, string> _deviceSessions = new(); // toestel-id van de game -> laatste code
    readonly ConcurrentDictionary<string, Link> _links = new(); // oude code -> nieuwe sessie van dezelfde game

    // Wie mag de nieuwe code kennen: het grote scherm (met zijn geheime sleutel) en, als het spel midden in liep,
    // de telefoons die echt in de oude sessie zaten. Zo komt niemand met alleen een oude code in een latere sessie.
    sealed record Link(string Next, string ScreenKey, IReadOnlySet<string> Phones, bool PhonesFollow);
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

    // Nieuwe sessie voor een game. Had hetzelfde toestel (bv. na een crash of herstart) nog een sessie, dan wordt die
    // beëindigd en gelinkt aan de nieuwe: het grote scherm springt mee, telefoons alleen als het spel midden in liep.
    public async Task<Session> Start(string baseUrl, string gameConnectionId, string? deviceId)
    {
        var session = Create(baseUrl, gameConnectionId);
        if (string.IsNullOrEmpty(deviceId)) return session;

        if (_deviceSessions.TryGetValue(deviceId, out var previous) && previous != session.Code)
        {
            var now = time.GetUtcNow();
            if (_active.TryGetValue(previous, out var old))
            {
                var link = LinkTo(old, session, phonesInTime: true);
                // Eerst verhuizen, dan pas beëindigen: wie SessionEnded krijgt, verbreekt de verbinding.
                await events.Moved(old.Code, session.Code, link.PhonesFollow);
                await End(old);
            }
            else if (_recentlyEnded.TryGetValue(previous, out var ended) && _retired.TryGetValue(previous, out var endedAt)
                     && now - endedAt <= FollowWindow)
            {
                LinkTo(ended, session, phonesInTime: now - endedAt <= PhoneFollowWindow);
            }
        }
        _deviceSessions[deviceId] = session.Code;
        return session;
    }

    Link LinkTo(Session old, Session next, bool phonesInTime)
    {
        var link = new Link(next.Code, old.ScreenKey, old.PhonesEverJoined(), phonesInTime && !old.Finished);
        _links[old.Code] = link;
        _log.LogInformation("Sessie {Old} gaat verder als {New} (zelfde game, telefoons mee: {Phones})", old.Code, next.Code, link.PhonesFollow);
        return link;
    }

    // Groot scherm: met de sleutel van de oude sessie de nieuwste code van dezelfde game (ook na meerdere herstarts).
    public string? NextCodeForScreen(string code, string screenKey)
    {
        if (!_links.TryGetValue(code, out var link) || !SameSecret(link.ScreenKey, screenKey)) return null;
        string next = link.Next;
        for (int i = 0; i < 20 && _links.TryGetValue(next, out var further); i++) next = further.Next;
        return next;
    }

    // Telefoon: alleen als hij in de oude sessie zat en het spel midden in liep. Eén stap; daarna geldt hetzelfde opnieuw.
    public string? NextCodeForPhone(string code, string clientId) =>
        _links.TryGetValue(code, out var link) && link.PhonesFollow && link.Phones.Contains(clientId ?? "") ? link.Next : null;

    static bool SameSecret(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b ?? ""));

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
        if (!_active.TryRemove(code, out var session)) return;
        _retired[code] = time.GetUtcNow();
        _recentlyEnded[code] = session;
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
        {
            if (now - at > FollowWindow) _recentlyEnded.TryRemove(code, out _);
            if (now - at > CodeCooldown)
            {
                _retired.TryRemove(code, out _);
                _links.TryRemove(code, out _);
            }
        }
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
