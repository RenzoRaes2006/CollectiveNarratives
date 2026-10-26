using CollectiveNarratives.Web.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace CollectiveNarratives.Web.Hubs;

// De SignalR-groepen per sessiecode.
public static class HubGroups
{
    public static string Game(string code) => $"{code}:game";
    public static string Phones(string code) => $"{code}:phones";
    public static string Screens(string code) => $"{code}:screens";
}

public sealed class HubSessionEvents(IHubContext<SessionHub> hub) : ISessionEvents
{
    public Task State(string code, SessionState state) =>
        hub.Clients.Groups(HubGroups.Phones(code), HubGroups.Screens(code)).SendAsync("State", state);

    public Task ToGame(string code, string method, object payload) =>
        hub.Clients.Group(HubGroups.Game(code)).SendAsync(method, payload);

    public Task Ended(string code) =>
        hub.Clients.Groups(HubGroups.Game(code), HubGroups.Phones(code), HubGroups.Screens(code)).SendAsync("SessionEnded");
}

// Fouten uit een sessie (bv. "Onbekende keuze") gaan als nette foutmelding naar de aanroeper.
public sealed class SessionExceptionFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(context);
        }
        catch (SessionException e)
        {
            throw new HubException(e.Message);
        }
    }
}

// Eén hub voor iedereen. De rol volgt uit wat een verbinding aanroept: CreateSession/ResumeSession (game,
// alleen met de game key), JoinAsScreen (groot scherm) of JoinAsPhone (telefoon). Contract: webapp/UNITY-KOPPELING.md
public sealed class SessionHub(SessionStore store, JoinGuard guard, PublicUrl publicUrl, GameKey gameKey) : Hub
{
    const string IsGameKey = "isGame";
    const string RoleKey = "role";
    const string CodeKey = "code";

    public override Task OnConnectedAsync()
    {
        // De game stuurt de game key mee als access_token (query bij WebSockets, anders de Authorization-header).
        var request = Context.GetHttpContext()?.Request;
        string? token = request?.Query["access_token"];
        if (string.IsNullOrEmpty(token) && request?.Headers.Authorization.ToString() is { } auth && auth.StartsWith("Bearer "))
            token = auth["Bearer ".Length..];
        if (!string.IsNullOrEmpty(token) && gameKey.Matches(token)) Context.Items[IsGameKey] = true;
        return base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Current() is { } session) await session.Leave(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    // ---------- Game ----------

    public async Task<CreateSessionResult> CreateSession()
    {
        RequireGameKey();
        await LeaveCurrent();
        var session = store.Create(publicUrl.For(Context.GetHttpContext()), Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Game(session.Code));
        SetRole("game", session.Code);
        return new CreateSessionResult(session.Code, session.GameToken, session.JoinUrl, session.ScreenUrl);
    }

    // Na een verbroken verbinding (binnen 2 minuten).
    public async Task<bool> ResumeSession(string code, string gameToken)
    {
        RequireGameKey();
        if (!store.TryGet(code ?? "", out var session) || !session.ResumeGame(Context.ConnectionId, gameToken)) return false;
        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Game(code!));
        SetRole("game", code!);
        return true;
    }

    public Task StartTutorial(int seconds) => GameSession().StartTutorial(seconds);

    public Task StartGame() => GameSession().StartGame();

    public Task<int> StartVote(string id, string? question, OptionDef[]? options, int seconds, string? message) =>
        GameSession().StartVote(id, question, options, seconds, message);

    public Task<VoteResult?> StopVote() => GameSession().StopVote();

    public async Task Decide(int voteId, string option) => await GameSession().Decide(voteId, option);

    public Task<TogetherView> StartTogether(string? label, int seconds) => GameSession().StartTogether(label, seconds);

    public Task SetEnding(string ending) => GameSession().SetEnding(ending);

    public async Task EndSession()
    {
        await store.End(GameSession());
        Context.Items.Remove(CodeKey);
    }

    // ---------- Groot scherm ----------

    public async Task<JoinResult> JoinAsScreen(string code)
    {
        if (CheckJoin(code, out var session) is { } error) return error;
        await LeaveCurrent();
        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Screens(code));
        var result = await session.JoinScreen(Context.ConnectionId);
        if (!result.Ok)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.Screens(code));
            return result;
        }
        SetRole("screen", code);
        return result;
    }

    // ---------- Telefoon ----------

    public async Task<JoinResult> JoinAsPhone(string code, string clientId)
    {
        if (CheckJoin(code, out var session) is { } error) return error;
        // Vaste id per telefoon (localStorage): opnieuw verbinden geeft geen extra kijker of dubbele stem.
        if (string.IsNullOrWhiteSpace(clientId) || clientId.Length > 64) clientId = Context.ConnectionId;
        await LeaveCurrent();
        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Phones(code));
        var result = await session.JoinPhone(Context.ConnectionId, clientId);
        if (!result.Ok)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, HubGroups.Phones(code));
            return result;
        }
        SetRole("phone", code);
        return result;
    }

    public async Task CastVote(int voteId, string option)
    {
        if (Current("phone") is { } session) await session.CastVote(Context.ConnectionId, voteId, option);
    }

    public async Task Press()
    {
        if (Current("phone") is { } session) await session.Press(Context.ConnectionId);
    }

    // ---------- Hulp ----------

    JoinResult? CheckJoin(string code, out Session session)
    {
        session = null!;
        string ip = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "?";
        if (guard.IsBlocked(ip)) return new(false, JoinErrors.RateLimited);
        if (!JoinGuard.IsValidCode(code))
        {
            guard.RecordFailure(ip);
            return new(false, JoinErrors.Invalid);
        }
        switch (store.Status(code))
        {
            case CodeStatus.Unknown:
                guard.RecordFailure(ip);
                return new(false, JoinErrors.Unknown);
            case CodeStatus.Ended:
                return new(false, JoinErrors.Ended);
        }
        return store.TryGet(code, out session) ? null : new(false, JoinErrors.Ended);
    }

    void RequireGameKey()
    {
        if (!Context.Items.ContainsKey(IsGameKey)) throw new HubException("Geen geldige game key.");
    }

    Session GameSession() => Current("game") ?? throw new HubException("Deze verbinding heeft geen actieve sessie. Eerst CreateSession of ResumeSession.");

    Session? Current(string? role = null)
    {
        if (role is not null && (Context.Items[RoleKey] as string) != role) return null;
        return Context.Items[CodeKey] is string code && store.TryGet(code, out var session) ? session : null;
    }

    async Task LeaveCurrent()
    {
        if (Current() is { } session && Context.Items[RoleKey] is string role)
        {
            await session.Leave(Context.ConnectionId);
            string group = role switch { "game" => HubGroups.Game(session.Code), "screen" => HubGroups.Screens(session.Code), _ => HubGroups.Phones(session.Code) };
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        }
        Context.Items.Remove(CodeKey);
        Context.Items.Remove(RoleKey);
    }

    void SetRole(string role, string code)
    {
        Context.Items[RoleKey] = role;
        Context.Items[CodeKey] = code;
    }
}
