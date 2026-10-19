using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Datavormen uit UNITY-KOPPELING.md. Leesbaar met JsonUtility (geen dictionaries).
[Serializable] public class VoteOption { public string id; public string label; public int count; }
[Serializable] public class Vote { public int voteId; public string id; public string question; public string message; public int seconds; public int remaining; public int total; public VoteOption[] options; }
[Serializable] public class VoteResult { public int voteId; public string id; public string winner; public string decidedBy; public int total; public VoteOption[] options; }
[Serializable] public class Together { public bool active; public string label; public int seconds; public int remaining; public int presses; public int needed; }
[Serializable] public class TogetherResult { public int attempt; public bool success; public int presses; public int needed; }
[Serializable] public class VoteState { public int viewers; public bool active; public Vote vote; public VoteResult last; public Together together; public TogetherResult lastTogether; public string ending; public VoteResult[] history; }
[Serializable] public class ServerInfo { public string joinUrl; }

[Serializable] class StartBody { public string id; public int seconds; public string message; }
[Serializable] class DecideBody { public int voteId; public string option; }

// Praat via gewone HTTP met de stem-server (webapp/). Pollt /api/state 4x per seconde.
// Vergeet niet: Project Settings > Player > Other Settings > Allow downloads over HTTP = Always allowed.
public class VoteClient : MonoBehaviour
{
    [Tooltip("Adres van de laptop met de webapp, bv. http://192.168.1.10:3000 (verandert per netwerk)")]
    public string serverUrl = "http://192.168.1.10:3000";
    public float pollInterval = 0.25f;

    public VoteState State { get; private set; }
    public bool Connected { get; private set; }
    public string JoinUrl { get; private set; } = "";
    public Texture2D QrTexture { get; private set; }

    string Url(string path) => serverUrl.TrimEnd('/') + path;

    IEnumerator Start()
    {
        while (true)
        {
            yield return Poll();
            // QR en adres blijven proberen tot het lukt (server kan later starten).
            if (Connected && QrTexture == null) { yield return LoadInfo(); yield return LoadQr(); }
            yield return new WaitForSeconds(pollInterval);
        }
    }

    IEnumerator Poll()
    {
        using var req = UnityWebRequest.Get(Url("/api/state"));
        req.timeout = 2;
        yield return req.SendWebRequest();
        Connected = req.result == UnityWebRequest.Result.Success;
        if (Connected) State = JsonUtility.FromJson<VoteState>(req.downloadHandler.text);
    }

    IEnumerator LoadInfo()
    {
        using var req = UnityWebRequest.Get(Url("/api/info"));
        req.timeout = 2;
        yield return req.SendWebRequest();
        if (req.result == UnityWebRequest.Result.Success)
            JoinUrl = JsonUtility.FromJson<ServerInfo>(req.downloadHandler.text).joinUrl;
    }

    IEnumerator LoadQr()
    {
        using var req = UnityWebRequestTexture.GetTexture(Url("/api/qr.png"));
        req.timeout = 3;
        yield return req.SendWebRequest();
        if (req.result == UnityWebRequest.Result.Success)
        {
            QrTexture = DownloadHandlerTexture.GetContent(req);
            QrTexture.filterMode = FilterMode.Point; // scherpe blokjes, beter scanbaar
        }
    }

    // Zelf opgebouwd i.p.v. UnityWebRequest.Post(url, json, type): werkt zo ook in oudere Unity-versies.
    IEnumerator PostJson(string path, string json, Action<string> onDone = null)
    {
        using var req = new UnityWebRequest(Url(path), "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 2;
        yield return req.SendWebRequest();
        if (req.result == UnityWebRequest.Result.Success) onDone?.Invoke(req.downloadHandler.text);
        else Debug.LogWarning($"[VoteClient] {path} mislukt: {req.error}");
    }

    public IEnumerator ResetRound() => PostJson("/api/reset", "{}");

    // Start een keuze en geeft de uitslag terug (null = server niet bereikbaar).
    // uitslag.winner == "" betekent: Kevin beslist.
    public IEnumerator RunVote(string id, int seconds, Action<VoteResult> onResult, string message = "")
    {
        int voteId = 0;
        var body = JsonUtility.ToJson(new StartBody { id = id, seconds = seconds, message = message });
        yield return PostJson("/api/start", body, text => voteId = JsonUtility.FromJson<Vote>(text).voteId);
        if (voteId == 0) { onResult(null); yield break; }

        // Wachten op de uitslag, met een veiligheidsmarge voor als de server wegvalt.
        float deadline = Time.time + seconds + 5f;
        yield return new WaitUntil(() =>
            (State != null && State.last != null && State.last.voteId == voteId) || Time.time > deadline);

        onResult(State != null && State.last != null && State.last.voteId == voteId ? State.last : null);
    }

    public IEnumerator Decide(int voteId, string option) =>
        PostJson("/api/decide", JsonUtility.ToJson(new DecideBody { voteId = voteId, option = option }));
}
