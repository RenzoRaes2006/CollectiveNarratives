using System.Security.Cryptography;
using System.Text;

namespace CollectiveNarratives.Web.Sessions;

// Wat een sessie naar buiten stuurt. In productie gaat dat via SignalR (HubSessionEvents), in de tests naar een lijstje.
public interface ISessionEvents
{
    // Naar de telefoons en het grote scherm.
    Task State(string code, SessionState state);

    // Naar de game: ScreenPaired, ViewersChanged, VoteTally, VoteEnded, TogetherEnded.
    Task ToGame(string code, string method, object payload);

    // Naar iedereen in de sessie.
    Task Ended(string code);

    // Dezelfde game startte een nieuwe sessie: naar de schermen, en naar de telefoons alleen als die mee mogen.
    Task Moved(string code, string nextCode, bool phonesToo);
}

// Fout die de game of een browser te zien krijgt (de hub maakt er een HubException van).
public sealed class SessionException(string message) : Exception(message);

// Eén speelsessie: één game, groot scherm(en) en telefoons, met een code van 6 cijfers.
// Alle staat zit achter één lock. Berichten worden in de lock klaargezet en pas daarna verstuurd.
public sealed class Session
{
    public const int DefaultVoteSeconds = 12;
    public const int DefaultTogetherSeconds = 5;

    readonly Lock _lock = new();
    readonly TimeProvider _time;
    readonly ISessionEvents _events;
    readonly ILogger _log;
    readonly string _baseUrl;

    readonly Dictionary<string, string> _phones = []; // connectionId -> clientId (één telefoon kan even 2 verbindingen hebben)
    readonly HashSet<string> _everJoined = []; // clientIds van alle telefoons die ooit meededen
    readonly HashSet<string> _screens = [];
    string? _gameConnection;
    DateTimeOffset? _gameLostAt;

    SessionPhase _phase = SessionPhase.Pairing;

    ActiveVote? _vote;
    VoteResult? _last;
    readonly List<VoteResult> _history = [];
    int _voteCounter;

    ActiveTogether? _together;
    TogetherResult _lastTogether = new(0, false, 0, 0);
    int _togetherCounter;

    string _ending = "";

    sealed class ActiveVote
    {
        public required int VoteId;
        public required string Id;
        public required string Question;
        public required string Message;
        public required IReadOnlyList<OptionDef> Options;
        public required int Seconds;
        public required DateTimeOffset EndsAt;
        public required ITimer Timer;
        public readonly Dictionary<string, string> Votes = []; // clientId -> optie-id (laatste druk telt)
    }

    sealed class ActiveTogether
    {
        public required int Number;
        public required string Label;
        public required int Seconds;
        public required DateTimeOffset EndsAt;
        public required int Needed;
        public required ITimer Timer;
        public readonly HashSet<string> Presses = []; // clientIds
    }

    public Session(string code, string gameToken, string baseUrl, string gameConnectionId, TimeProvider time, ISessionEvents events, ILogger log)
    {
        Code = code;
        GameToken = gameToken;
        _baseUrl = baseUrl.TrimEnd('/');
        _gameConnection = gameConnectionId;
        _time = time;
        _events = events;
        _log = log;
        CreatedAt = time.GetUtcNow();
    }

    public string Code { get; }
    public string GameToken { get; }

    // Geheim dat alleen een gekoppeld groot scherm krijgt. Daarmee mag het na een herstart van de game de nieuwe code opvragen.
    public string ScreenKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_');
    public DateTimeOffset CreatedAt { get; }
    public string JoinUrl => $"{_baseUrl}/poll/{Code}";
    public string ScreenUrl => $"{_baseUrl}/screen/{Code}";

    public SessionPhase Phase
    {
        get { lock (_lock) return _phase; }
    }

    // Afgelopen met een einde (held, weg, cadeau): een volgende sessie is een nieuwe ronde, niet hetzelfde spel.
    public bool Finished
    {
        get { lock (_lock) return _ending != ""; }
    }

    public IReadOnlySet<string> PhonesEverJoined()
    {
        lock (_lock) return _everJoined.ToHashSet();
    }

    public SessionState GetState()
    {
        lock (_lock) return BuildState();
    }

    // ---------- Verbindingen ----------

    public bool IsGameToken(string token) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token ?? ""), Encoding.UTF8.GetBytes(GameToken));

    public async Task<bool> ResumeGame(string connectionId, string token)
    {
        if (!IsGameToken(token)) return false;
        var fx = new List<Func<Task>>();
        lock (_lock)
        {
            if (_phase == SessionPhase.Ended) return false;
            _gameConnection = connectionId;
            _gameLostAt = null;
            AddState(fx); // het grote scherm haalt de storingsmelding weg
        }
        _log.LogInformation("Sessie {Code}: game is terug", Code);
        await Run(fx);
        return true;
    }

    // Vervallen: te oud, of de game is al te lang weg.
    public bool IsExpired(DateTimeOffset now, TimeSpan gameGrace, TimeSpan maxAge)
    {
        lock (_lock)
            return now - CreatedAt > maxAge || (_gameConnection is null && _gameLostAt is { } lost && now - lost > gameGrace);
    }

    public async Task<JoinResult> JoinPhone(string connectionId, string clientId)
    {
        var fx = new List<Func<Task>>();
        lock (_lock)
        {
            if (_phase == SessionPhase.Ended) return new(false, JoinErrors.Ended);
            if (_phase == SessionPhase.Pairing) return new(false, JoinErrors.NotReady);
            int before = ViewerCount();
            _phones[connectionId] = clientId;
            _everJoined.Add(clientId);
            if (ViewerCount() != before) AddViewers(fx);
            AddState(fx);
        }
        await Run(fx);
        return JoinResult.Success;
    }

    // Het eerste scherm dat joint, koppelt de sessie: de game mag dan door naar de tutorial.
    public async Task<JoinResult> JoinScreen(string connectionId)
    {
        var fx = new List<Func<Task>>();
        lock (_lock)
        {
            if (_phase == SessionPhase.Ended) return new(false, JoinErrors.Ended);
            _screens.Add(connectionId);
            if (_phase == SessionPhase.Pairing)
            {
                _phase = SessionPhase.Lobby;
                _log.LogInformation("Sessie {Code}: groot scherm gekoppeld", Code);
            }
            var paired = new ScreenPairedEvent(_screens.Count);
            fx.Add(() => _events.ToGame(Code, "ScreenPaired", paired));
            AddState(fx);
        }
        await Run(fx);
        return new JoinResult(true, "", ScreenKey);
    }

    public async Task Leave(string connectionId)
    {
        var fx = new List<Func<Task>>();
        lock (_lock)
        {
            if (_phones.ContainsKey(connectionId))
            {
                int before = ViewerCount();
                _phones.Remove(connectionId);
                if (ViewerCount() != before) AddViewers(fx);
                AddState(fx);
            }
            else if (_screens.Remove(connectionId))
            {
                AddState(fx);
            }
            else if (_gameConnection == connectionId)
            {
                _gameConnection = null;
                _gameLostAt = _time.GetUtcNow();
                _log.LogWarning("Sessie {Code}: verbinding met de game kwijt", Code);
                AddState(fx); // het grote scherm toont een storingsmelding
            }
        }
        await Run(fx);
    }

    // ---------- Wachtkamer ----------

    // De speler start de tutorial. Geen timer: de wachtkamer blijft open tot StartGame (knop Begin in de bril).
    public Task StartTutorial()
    {
        lock (_lock)
        {
            ThrowIfEnded();
            if (_phase == SessionPhase.Pairing) throw new SessionException("No big screen has been paired yet.");
        }
        return Task.CompletedTask;
    }

    public async Task StartGame()
    {
        var fx = new List<Func<Task>>();
        lock (_lock)
        {
            ThrowIfEnded();
            _phase = SessionPhase.Playing;
            AddState(fx);
        }
        _log.LogInformation("Sessie {Code}: spel gestart", Code);
        await Run(fx);
    }

    // ---------- Stemmen ----------

    // Zonder vraag en opties haalt de server ze uit de ingebouwde keuzes (Choices).
    public async Task<int> StartVote(string id, string? question, IReadOnlyList<OptionDef>? options, int seconds, string? message)
    {
        if (options is null || options.Count == 0)
        {
            if (!Choices.All.TryGetValue(id ?? "", out var choice)) throw new SessionException($"Unknown choice: {id}");
            options = choice.Options;
            if (string.IsNullOrWhiteSpace(question)) question = choice.Question;
        }
        if (options.Count is < 2 or > 6) throw new SessionException("A vote needs 2 to 6 options.");
        if (options.Any(o => string.IsNullOrWhiteSpace(o.Id)) || options.Select(o => o.Id).Distinct().Count() != options.Count)
            throw new SessionException("Every option needs a unique id.");
        seconds = seconds > 0 ? Math.Min(seconds, 300) : DefaultVoteSeconds;

        var fx = new List<Func<Task>>();
        int voteId;
        lock (_lock)
        {
            ThrowIfEnded();
            if (_vote is not null) EndVoteLocked(fx); // een nieuwe start rondt de vorige netjes af
            if (_phase != SessionPhase.Playing)
            {
                _phase = SessionPhase.Playing;
                }
            voteId = ++_voteCounter;
            _vote = new ActiveVote
            {
                VoteId = voteId,
                Id = id ?? "",
                Question = question ?? "",
                Message = message ?? "",
                Options = options.ToArray(),
                Seconds = seconds,
                EndsAt = _time.GetUtcNow().AddSeconds(seconds),
                Timer = _time.CreateTimer(_ => _ = EndVoteFromTimer(voteId), null, TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan),
            };
            AddState(fx);
            AddTally(fx);
        }
        _log.LogInformation("Sessie {Code}: stemming {VoteId} gestart ({Id}, {Seconds} s)", Code, voteId, id, seconds);
        await Run(fx);
        return voteId;
    }

    async Task EndVoteFromTimer(int voteId)
    {
        var fx = new List<Func<Task>>();
        lock (_lock)
        {
            if (_vote?.VoteId == voteId) EndVoteLocked(fx);
        }
        await Run(fx);
    }

    public async Task<VoteResult?> StopVote()
    {
        var fx = new List<Func<Task>>();
        VoteResult? result = null;
        lock (_lock)
        {
            if (_vote is not null) result = EndVoteLocked(fx);
        }
        await Run(fx);
        return result;
    }

    public async Task CastVote(string connectionId, int voteId, string option)
    {
        var fx = new List<Func<Task>>();
        lock (_lock)
        {
            if (!_phones.TryGetValue(connectionId, out var clientId)) return;
            if (_vote is null || _vote.VoteId != voteId) return;
            if (!_vote.Options.Any(o => o.Id == option)) return;
            _vote.Votes[clientId] = option;
            AddState(fx);
            AddTally(fx);
        }
        await Run(fx);
    }

    // Kevin koos zelf (gelijkspel of geen stemmen): zo kloppen de keuzes op de telefoons aan het einde.
    public async Task<VoteResult> Decide(int voteId, string option)
    {
        var fx = new List<Func<Task>>();
        VoteResult result;
        lock (_lock)
        {
            result = _history.Find(r => r.VoteId == voteId) ?? throw new SessionException($"Unknown voteId: {voteId}");
            if (!result.Options.Any(o => o.Id == option)) throw new SessionException($"Unknown option: {option}");
            result.Winner = option;
            result.DecidedBy = "kevin";
            AddState(fx);
        }
        await Run(fx);
        return result;
    }

    VoteResult EndVoteLocked(List<Func<Task>> fx)
    {
        var vote = _vote!;
        vote.Timer.Dispose();
        var options = Counts(vote);
        int max = options.Max(o => o.Count);
        var top = options.Where(o => o.Count == max).ToList();
        // Gelijkspel of niemand stemt: winner leeg, Kevin beslist.
        string winner = max > 0 && top.Count == 1 ? top[0].Id : "";

        var result = new VoteResult
        {
            VoteId = vote.VoteId,
            Id = vote.Id,
            Question = vote.Question,
            Winner = winner,
            DecidedBy = winner == "" ? "kevin" : "publiek",
            Total = vote.Votes.Count,
            Options = options,
        };
        _history.Add(result);
        _last = result;
        _vote = null;
        _log.LogInformation("Sessie {Code}: stemming {VoteId} klaar -> {Winner}", Code, result.VoteId, winner == "" ? "Kevin beslist" : winner);
        fx.Add(() => _events.ToGame(Code, "VoteEnded", result));
        AddState(fx);
        return result;
    }

    // ---------- Samen-actie ----------

    public async Task<TogetherView> StartTogether(string? label, int seconds)
    {
        seconds = seconds > 0 ? Math.Min(seconds, 120) : DefaultTogetherSeconds;
        var fx = new List<Func<Task>>();
        TogetherView view;
        lock (_lock)
        {
            ThrowIfEnded();
            _together?.Timer.Dispose();
            int number = ++_togetherCounter;
            _together = new ActiveTogether
            {
                Number = number,
                Label = string.IsNullOrWhiteSpace(label) ? "Press!" : label,
                Seconds = seconds,
                EndsAt = _time.GetUtcNow().AddSeconds(seconds),
                Needed = Math.Max(1, ViewerCount()),
                Timer = _time.CreateTimer(_ => _ = EndTogetherFromTimer(number), null, TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan),
            };
            view = TogetherViewLocked();
            AddState(fx);
        }
        await Run(fx);
        return view;
    }

    async Task EndTogetherFromTimer(int number)
    {
        var fx = new List<Func<Task>>();
        lock (_lock)
        {
            if (_together?.Number == number) EndTogetherLocked(false, fx);
        }
        await Run(fx);
    }

    public async Task Press(string connectionId)
    {
        var fx = new List<Func<Task>>();
        lock (_lock)
        {
            if (_together is null || !_phones.TryGetValue(connectionId, out var clientId)) return;
            _together.Presses.Add(clientId);
            if (_together.Presses.Count >= _together.Needed) EndTogetherLocked(true, fx);
            else AddState(fx);
        }
        await Run(fx);
    }

    void EndTogetherLocked(bool success, List<Func<Task>> fx)
    {
        var t = _together!;
        t.Timer.Dispose();
        _lastTogether = new TogetherResult(_lastTogether.Attempt + 1, success, t.Presses.Count, t.Needed);
        _together = null;
        var result = _lastTogether;
        fx.Add(() => _events.ToGame(Code, "TogetherEnded", result));
        AddState(fx);
    }

    // ---------- Zicht van de speler (fase 5a) ----------

    // De game stuurt een paar keer per seconde een JPEG; het grote scherm haalt telkens het nieuwste op.
    public static readonly TimeSpan MinFrameInterval = TimeSpan.FromMilliseconds(40); // max. 25 beelden per seconde
    public static readonly TimeSpan FrameMaxAge = TimeSpan.FromSeconds(5); // ouder: geen beeld meer tonen

    byte[]? _frame;
    DateTimeOffset _frameAt;

    // false: te snel na het vorige beeld, of de sessie is afgelopen.
    public bool SetFrame(byte[] jpeg)
    {
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            if (_phase == SessionPhase.Ended || now - _frameAt < MinFrameInterval) return false;
            _frame = jpeg;
            _frameAt = now;
            return true;
        }
    }

    // Het nieuwste beeld, of null als er (al een tijdje) geen is.
    public byte[]? GetFrame()
    {
        var now = _time.GetUtcNow();
        lock (_lock)
            return _frame is not null && now - _frameAt <= FrameMaxAge ? _frame : null;
    }

    // ---------- Einde ----------

    public async Task SetEnding(string ending)
    {
        if (!Choices.Endings.Contains(ending)) throw new SessionException($"Unknown ending: {ending}");
        var fx = new List<Func<Task>>();
        lock (_lock)
        {
            ThrowIfEnded();
            _ending = ending;
            AddState(fx);
        }
        await Run(fx);
    }

    // Code vervalt meteen. Telefoons houden hun laatste scherm (bv. het einde).
    public async Task End()
    {
        var fx = new List<Func<Task>>();
        lock (_lock)
        {
            if (_phase == SessionPhase.Ended) return;
            if (_vote is not null) EndVoteLocked(fx);
            _together?.Timer.Dispose();
            _together = null;
            _phase = SessionPhase.Ended;
            AddState(fx);
            fx.Add(() => _events.Ended(Code));
        }
        _log.LogInformation("Sessie {Code}: afgelopen", Code);
        await Run(fx);
    }

    // ---------- Hulp ----------

    void ThrowIfEnded()
    {
        if (_phase == SessionPhase.Ended) throw new SessionException("This session has ended.");
    }

    int ViewerCount() => _phones.Values.Distinct().Count();

    void AddState(List<Func<Task>> fx)
    {
        var state = BuildState();
        fx.Add(() => _events.State(Code, state));
    }

    void AddViewers(List<Func<Task>> fx)
    {
        var e = new ViewersChangedEvent(ViewerCount());
        fx.Add(() => _events.ToGame(Code, "ViewersChanged", e));
    }

    void AddTally(List<Func<Task>> fx)
    {
        var vote = VoteViewLocked();
        if (vote is not null) fx.Add(() => _events.ToGame(Code, "VoteTally", vote));
    }

    async Task Run(List<Func<Task>> fx)
    {
        foreach (var f in fx)
        {
            try
            {
                await f();
            }
            catch (Exception e)
            {
                _log.LogWarning(e, "Sessie {Code}: bericht versturen mislukt", Code);
            }
        }
    }

    static List<OptionCount> Counts(ActiveVote vote) =>
        vote.Options.Select(o => new OptionCount(o.Id, o.Label, vote.Votes.Values.Count(v => v == o.Id))).ToList();

    int Remaining(DateTimeOffset endsAt) => Math.Max(0, (int)Math.Ceiling((endsAt - _time.GetUtcNow()).TotalSeconds));

    VoteView? VoteViewLocked() => _vote is null
        ? null
        : new VoteView(_vote.VoteId, _vote.Id, _vote.Question, _vote.Message, _vote.Seconds, Remaining(_vote.EndsAt), _vote.Votes.Count, Counts(_vote));

    TogetherView TogetherViewLocked() => _together is null
        ? TogetherView.Inactive
        : new TogetherView(true, _together.Label, _together.Seconds, Remaining(_together.EndsAt), _together.Presses.Count, _together.Needed);

    SessionState BuildState()
    {
        return new SessionState(
            Code,
            JoinUrl,
            _phase.ToString().ToLowerInvariant(),
            _screens.Count,
            ViewerCount(),
            _vote is not null,
            VoteViewLocked(),
            _last,
            TogetherViewLocked(),
            _lastTogether,
            _ending,
            _history.ToArray(),
            _gameConnection is not null);
    }
}
