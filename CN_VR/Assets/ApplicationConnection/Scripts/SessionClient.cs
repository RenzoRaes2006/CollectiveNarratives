using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CollectiveNarratives.Net;
using UnityEngine;

// Datavormen uit webapp/UNITY-KOPPELING.md. Leesbaar met JsonUtility (geen dictionaries).
[Serializable] public class VoteOption { public string id; public string label; public int count; }
[Serializable] public class Vote { public int voteId; public string id; public string question; public string message; public int seconds; public int remaining; public int total; public VoteOption[] options; }
[Serializable] public class VoteResult { public int voteId; public string id; public string question; public string winner; public string decidedBy; public int total; public VoteOption[] options; }
[Serializable] public class TogetherResult { public int attempt; public bool success; public int presses; public int needed; }
[Serializable] public class SessionInfo { public string code; public string gameToken; public string joinUrl; public string screenUrl; }
[Serializable] public class ScreenPairedEvent { public int screens; }
[Serializable] public class ViewersChangedEvent { public int viewers; }

// Praat via SignalR met de website (webapp/). Vervangt de oude VoteClient (HTTP-polling).
// - StartSession() maakt een sessie: Code staat dan klaar om in de bril te tonen.
// - ScreenPaired wordt true zodra het grote scherm de code heeft ingevuld.
// - Events van de server komen binnen op een achtergrond-thread en worden in Update() op de main thread afgehandeld.
// - Valt de verbinding weg, dan verbindt hij opnieuw en hervat hij de sessie (kan tot 2 minuten).
public class SessionClient : MonoBehaviour
{
    [Tooltip("Leeg = Resources/ServerConfig")]
    public ServerConfig config;
    public bool logEvents = true;

    public enum Status { Offline, Connecting, Connected }

    public Status ConnectionStatus { get; private set; }
    public bool Connected => ConnectionStatus == Status.Connected;
    public string LastError { get; private set; } = "";
    public SessionInfo Session { get; private set; } // null = geen sessie
    public string Code => Session != null ? Session.code : "";
    public bool ScreenPaired { get; private set; }
    public int Screens { get; private set; }
    public int Viewers { get; private set; }
    public Vote LiveVote { get; private set; } // tellingen tijdens een stemming, anders null
    public TogetherResult LastTogether { get; private set; }
    public bool SessionLost { get; private set; } // sessie vervallen of door de server beëindigd: opnieuw beginnen
    public string ServerUrl => config.ActiveServerUrl;

    // Vaste id van dit toestel (blijft bewaard). Start de game opnieuw, dan koppelt de server de nieuwe sessie aan de
    // vorige: het grote scherm en de telefoons springen vanzelf naar de nieuwe code.
    public static string DeviceId
    {
        get
        {
            string id = PlayerPrefs.GetString("cn-device-id", "");
            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString("cn-device-id", id);
                PlayerPrefs.Save();
            }
            return id;
        }
    }

    SignalRConnection _conn;
    readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
    readonly Dictionary<int, VoteResult> _results = new Dictionary<int, VoteResult>();
    bool _wantConnection;
    bool _reconnecting;
    bool _endingOnPurpose;
    float _nextReconnect;

    void Awake()
    {
        if (config == null) config = ServerConfig.Load();
    }

    void Update()
    {
        while (_mainThread.TryDequeue(out var action)) action();

        if (_wantConnection && !_reconnecting && ConnectionStatus == Status.Offline && Time.unscaledTime >= _nextReconnect)
            StartCoroutine(Reconnect());
    }

    // Unity stopt (Play-modus uit, of de app sluit af): sessie meteen beëindigen. Zo stopt alles op het grote scherm
    // en de telefoons, in plaats van door te tellen tot de server merkt dat de game weg is.
    void OnApplicationQuit()
    {
        _wantConnection = false;
        var conn = _conn;
        if (conn == null || !conn.IsConnected || Session == null) return;
        _endingOnPurpose = true;
        try
        {
            // Kort blokkeren mag hier: de game sluit toch af. Op een achtergrond-thread, zodat er niets op de main thread wacht.
            Task.Run(async () =>
            {
                try { await conn.InvokeAsync("EndSession", new string[0]); } catch { }
                await conn.CloseAsync();
            }).Wait(TimeSpan.FromSeconds(2));
        }
        catch { /* afsluiten gaat sowieso door */ }
        Session = null;
    }

    void OnDestroy()
    {
        _wantConnection = false;
        if (_conn != null) _conn.Dispose();
    }

    // ---------- Sessie ----------

    // Verbindt (indien nodig) en maakt een nieuwe sessie. Lukt het niet, dan blijft Code leeg (zie LastError).
    public IEnumerator StartSession()
    {
        _wantConnection = true;
        if (_conn == null || !_conn.IsConnected)
        {
            yield return Connect();
            if (!Connected) yield break;
        }
        ResetSessionState();
        Session = null;
        string raw = null;
        yield return Call("CreateSession", r => raw = r);
        if (raw == null) yield break;
        Session = JsonUtility.FromJson<SessionInfo>(raw);
        Log($"Session {Session.code} created. Big screen: {ServerUrl}/screen");
    }

    // Wachtkamer begint: de speler doet de tutorial, het publiek joint. Geen timer.
    public IEnumerator StartTutorial() => Call("StartTutorial", null);

    // Einde wachtkamer: tutorial klaar en minstens de wachttijd voorbij (of overgeslagen).
    public IEnumerator StartGame() => Call("StartGame", null);

    public IEnumerator EndSession()
    {
        if (Session == null) yield break;
        _endingOnPurpose = true;
        yield return Call("EndSession", null);
        _endingOnPurpose = false;
        Session = null;
        ResetSessionState();
    }

    // Zonder server verder spelen (Kevin beslist alles).
    public void GoOffline()
    {
        _wantConnection = false;
        Session = null;
        ResetSessionState();
        if (_conn != null) _conn.Dispose();
        _conn = null;
        ConnectionStatus = Status.Offline;
    }

    // ---------- Stemmen ----------

    // Keuze uit de ingebouwde lijst van de server (telefoon, hond, stroom, inbreker).
    public IEnumerator RunVote(string id, int seconds, Action<VoteResult> onResult, string message = "") =>
        RunVote(id, null, null, seconds, onResult, message);

    // Eigen vraag en opties. Geeft de uitslag terug, of null als de server niet bereikbaar is.
    // uitslag.winner == "" betekent: gelijkspel of niemand stemde, Kevin beslist.
    public IEnumerator RunVote(string id, string question, VoteOption[] options, int seconds, Action<VoteResult> onResult, string message = "")
    {
        int voteId = 0;
        string optionsJson = options == null || options.Length == 0 ? "null" : "[" + string.Join(",", options.Select(o => JsonUtility.ToJson(o))) + "]";
        yield return Call("StartVote", r => voteId = MiniJson.ReadInt(r),
            MiniJson.Quote(id), MiniJson.Quote(question), optionsJson, MiniJson.Number(seconds), MiniJson.Quote(message ?? ""));
        if (voteId == 0)
        {
            onResult(null);
            yield break;
        }

        // Wachten op VoteEnded, met een veiligheidsmarge voor als de server wegvalt.
        float deadline = Time.unscaledTime + seconds + 5f;
        yield return new WaitUntil(() => _results.ContainsKey(voteId) || Time.unscaledTime > deadline);
        onResult(_results.TryGetValue(voteId, out var result) ? result : null);
    }

    // Stemming meteen afsluiten (bv. Kevin legt het briefje neer). RunVote krijgt dan de uitslag via VoteEnded.
    public IEnumerator StopVote() => Call("StopVote", null);

    // Kevin koos zelf: zo kloppen de keuzes op de telefoons aan het einde.
    public IEnumerator Decide(int voteId, string option) => Call("Decide", null, MiniJson.Number(voteId), MiniJson.Quote(option));

    public IEnumerator StartTogether(string label, int seconds) => Call("StartTogether", null, MiniJson.Quote(label), MiniJson.Number(seconds));

    public IEnumerator SetEnding(string ending) => Call("SetEnding", null, MiniJson.Quote(ending));

    // ---------- Verbinding ----------

    IEnumerator Connect()
    {
        ConnectionStatus = Status.Connecting;
        if (_conn != null) _conn.Dispose();
        var conn = new SignalRConnection();
        conn.Received += (target, arg) => _mainThread.Enqueue(() => OnEvent(target, arg));
        conn.Closed += error => _mainThread.Enqueue(() => OnClosed(conn, error));
        _conn = conn;

        var task = conn.ConnectAsync(ServerUrl + "/hubs/session?device=" + DeviceId, config.ActiveGameKey);
        yield return new WaitUntil(() => task.IsCompleted);
        if (conn != _conn) yield break; // intussen vervangen
        if (task.IsFaulted || task.IsCanceled)
        {
            Fail($"Can't reach the server at {ServerUrl}: {ErrorText(task.Exception)}");
            ConnectionStatus = Status.Offline;
            _nextReconnect = Time.unscaledTime + 3f;
            yield break;
        }
        ConnectionStatus = Status.Connected;
        LastError = "";
    }

    IEnumerator Reconnect()
    {
        _reconnecting = true;
        yield return Connect();
        if (Connected && Session != null)
        {
            string raw = null;
            yield return Call("ResumeSession", r => raw = r, MiniJson.Quote(Session.code), MiniJson.Quote(Session.gameToken));
            if (raw == "true") Log($"Session {Session.code} resumed");
            else if (raw != null)
            {
                Fail($"Session {Session.code} has expired");
                Session = null;
                SessionLost = true;
            }
        }
        _reconnecting = false;
    }

    void OnClosed(SignalRConnection conn, string error)
    {
        if (conn != _conn) return;
        ConnectionStatus = Status.Offline;
        if (error != null) Fail("Connection lost: " + error);
        _nextReconnect = Time.unscaledTime + 2f;
    }

    IEnumerator Call(string method, Action<string> onResult, params string[] args)
    {
        if (_conn == null || !_conn.IsConnected)
        {
            Fail(method + ": not connected");
            yield break;
        }
        var task = _conn.InvokeAsync(method, args);
        yield return new WaitUntil(() => task.IsCompleted);
        if (task.IsFaulted || task.IsCanceled)
        {
            Fail($"{method} failed: {ErrorText(task.Exception)}");
            yield break;
        }
        if (onResult != null) onResult(task.Result);
    }

    void OnEvent(string target, string arg)
    {
        if (logEvents && target != "VoteTally") Log($"← {target} {arg}");
        switch (target)
        {
            case "ScreenPaired":
                ScreenPaired = true;
                Screens = JsonUtility.FromJson<ScreenPairedEvent>(arg).screens;
                break;
            case "ViewersChanged":
                Viewers = JsonUtility.FromJson<ViewersChangedEvent>(arg).viewers;
                break;
            case "VoteTally":
                LiveVote = JsonUtility.FromJson<Vote>(arg);
                break;
            case "VoteEnded":
                var result = JsonUtility.FromJson<VoteResult>(arg);
                _results[result.voteId] = result;
                if (LiveVote != null && LiveVote.voteId == result.voteId) LiveVote = null;
                break;
            case "TogetherEnded":
                LastTogether = JsonUtility.FromJson<TogetherResult>(arg);
                break;
            case "SessionEnded":
                if (!_endingOnPurpose && Session != null)
                {
                    Fail($"Session {Session.code} was ended by the server");
                    Session = null;
                    SessionLost = true;
                }
                break;
        }
    }

    void ResetSessionState()
    {
        ScreenPaired = false;
        Screens = 0;
        Viewers = 0;
        LiveVote = null;
        LastTogether = null;
        SessionLost = false;
        _results.Clear();
    }

    void Log(string message)
    {
        if (logEvents) Debug.Log("[SessionClient] " + message);
    }

    void Fail(string message)
    {
        LastError = message;
        Debug.LogWarning("[SessionClient] " + message);
    }

    static string ErrorText(Exception e) => e == null ? "cancelled" : e.GetBaseException().Message;
}
