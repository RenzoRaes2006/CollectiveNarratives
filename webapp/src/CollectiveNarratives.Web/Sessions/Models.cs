namespace CollectiveNarratives.Web.Sessions;

// Alles wat over de hub gaat. SignalR maakt er camelCase-JSON van.
// Unity leest dezelfde velden met JsonUtility: dus geen dictionaries, alleen gewone velden en arrays.

public enum SessionPhase
{
    Pairing, // code staat in de bril, nog geen groot scherm
    Lobby, // groot scherm gekoppeld, tutorial + publiek joint
    Playing,
    Ended,
}

public sealed record OptionDef(string Id, string Label);

public sealed record OptionCount(string Id, string Label, int Count);

public sealed record VoteView(
    int VoteId,
    string Id,
    string Question,
    string Message,
    int Seconds,
    int Remaining,
    int Total,
    IReadOnlyList<OptionCount> Options);

// Klasse en geen record: Decide past Winner en DecidedBy achteraf aan.
public sealed class VoteResult
{
    public int VoteId { get; init; }
    public string Id { get; init; } = "";
    public string Question { get; init; } = "";
    public string Winner { get; set; } = "";
    public string DecidedBy { get; set; } = "";
    public int Total { get; init; }
    public IReadOnlyList<OptionCount> Options { get; init; } = [];
}

public sealed record TogetherView(bool Active, string Label, int Seconds, int Remaining, int Presses, int Needed)
{
    public static readonly TogetherView Inactive = new(false, "", 0, 0, 0, 0);
}

public sealed record TogetherResult(int Attempt, bool Success, int Presses, int Needed);

// Zelfde vorm als /api/state van de oude Node-server, plus code, joinUrl, phase, lobbyRemaining en screens.
public sealed record SessionState(
    string Code,
    string JoinUrl,
    string Phase,
    double? LobbyRemaining,
    int Screens,
    int Viewers,
    bool Active,
    VoteView? Vote,
    VoteResult? Last,
    TogetherView Together,
    TogetherResult LastTogether,
    string Ending,
    IReadOnlyList<VoteResult> History,
    bool GameConnected); // false: game gecrasht of verbinding kwijt, het grote scherm toont een storingsmelding

public sealed record CreateSessionResult(string Code, string GameToken, string JoinUrl, string ScreenUrl);

public sealed record JoinResult(bool Ok, string Error)
{
    public static readonly JoinResult Success = new(true, "");
}

// Foutcodes voor JoinResult.Error
public static class JoinErrors
{
    public const string Invalid = "invalid"; // geen 6 cijfers
    public const string Unknown = "unknown";
    public const string Ended = "ended";
    public const string NotReady = "notReady"; // telefoon te vroeg: nog geen groot scherm gekoppeld
    public const string RateLimited = "rateLimited";
}

public sealed record ScreenPairedEvent(int Screens);

public sealed record ViewersChangedEvent(int Viewers);

public sealed record LobbySkippedEvent(int Viewers);
