using CollectiveNarratives.Web.Sessions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace CollectiveNarratives.Web.Tests;

// Onthoudt alles wat een sessie verstuurt.
public sealed class RecordingEvents : ISessionEvents
{
    public List<SessionState> States { get; } = [];
    public List<(string Method, object Payload)> ToGameCalls { get; } = [];
    public int EndedCount { get; private set; }

    public SessionState LastState => States[^1];

    public IEnumerable<T> Game<T>(string method) => ToGameCalls.Where(c => c.Method == method).Select(c => (T)c.Payload);

    public Task State(string code, SessionState state)
    {
        lock (States) States.Add(state);
        return Task.CompletedTask;
    }

    public Task ToGame(string code, string method, object payload)
    {
        lock (ToGameCalls) ToGameCalls.Add((method, payload));
        return Task.CompletedTask;
    }

    public Task Ended(string code)
    {
        EndedCount++;
        return Task.CompletedTask;
    }
}

public static class Make
{
    public static Session Session(FakeTimeProvider time, RecordingEvents events) =>
        new("365895", "token", "https://collective-narratives.app", "game-conn", time, events, NullLogger.Instance);

    // Sessie met een gekoppeld scherm, zodat telefoons mogen joinen.
    public static async Task<Session> PairedSession(FakeTimeProvider time, RecordingEvents events)
    {
        var s = Session(time, events);
        await s.JoinScreen("screen-1");
        return s;
    }
}

// Echte server op een vrije poort, voor de hub- en HTTP-tests.
public sealed class TestServer : IAsyncDisposable
{
    public const string GameKey = "test-game-key-123";

    TestServer(WebApplication app, string url)
    {
        App = app;
        Url = url;
        Http = new HttpClient { BaseAddress = new Uri(url) };
    }

    public WebApplication App { get; }
    public string Url { get; }
    public HttpClient Http { get; }
    public string HubUrl => Url + "/hubs/session";

    public static async Task<TestServer> Start(Dictionary<string, string?>? settings = null)
    {
        var app = WebApp.Create(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory, EnvironmentName = "Testing" }, b =>
        {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GAME_KEY"] = GameKey,
                ["PUBLIC_URL"] = "https://collective-narratives.app",
            });
            if (settings is not null) b.Configuration.AddInMemoryCollection(settings);
            b.WebHost.UseUrls("http://127.0.0.1:0");
            b.Logging.ClearProviders();
        });
        await app.StartAsync();
        string url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new TestServer(app, url.TrimEnd('/'));
    }

    public async Task<HubConnection> Connect(string? accessToken = null)
    {
        var conn = new HubConnectionBuilder()
            .WithUrl(HubUrl, o =>
            {
                if (accessToken is not null) o.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
            })
            .Build();
        await conn.StartAsync();
        return conn;
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
    }
}

public static class TaskExtensions
{
    // Wacht maximaal een paar seconden op iets dat de server stuurt.
    public static async Task<T> Within<T>(this Task<T> task, int seconds = 5)
    {
        var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds)));
        if (done != task) throw new TimeoutException("Niets ontvangen binnen de tijd");
        return await task;
    }
}
