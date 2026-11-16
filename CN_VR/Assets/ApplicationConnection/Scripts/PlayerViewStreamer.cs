using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Fase 5a: het zicht van de speler op het grote scherm.
// Een extra camera volgt het hoofd (iets afgevlakt, minder schokkerig voor het publiek) en rendert op 720p met 4x MSAA,
// alleen op de momenten dat er een beeld verstuurd wordt (standaard 15 per seconde). Dat beeld gaat als JPEG naar
// POST <server>/api/sessions/{code}/frame; /screen haalt telkens het nieuwste op.
// Kost het te veel op de Quest (framedrops)? Zet streamEnabled uit, of verlaag fps of resolutie.
public class PlayerViewStreamer : MonoBehaviour
{
    public SessionClient sessionClient;
    [Tooltip("Uit = geen extra camera en geen netwerkverkeer")]
    public bool streamEnabled = true;
    [Range(1, 15)] public int framesPerSecond = 15;
    public int width = 1280;
    public int height = 720;
    [Range(10, 95)] public int jpegQuality = 75;
    [Tooltip("Anti-aliasing tegen kartelrandjes (1 = uit)")]
    public int msaaSamples = 4;
    [Tooltip("Hoe snel de camera de hoofdbeweging volgt. Lager = rustiger beeld")]
    public float followSpeed = 12f;
    public float fieldOfView = 75f;

    public bool IsStreaming { get; private set; }
    public string LastError { get; private set; } = "";

    Camera _cam;
    RenderTexture _rt;       // de camera rendert hierin (met MSAA)
    RenderTexture _resolved; // zonder MSAA, om uit te lezen
    Texture2D _cpuTexture; // alleen als het toestel geen AsyncGPUReadback kan
    float _next;
    bool _busy;

    void Awake()
    {
        if (sessionClient == null) sessionClient = FindFirstObjectByType<SessionClient>();
    }

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    // Na een scenewissel (de sessie gaat mee) de instellingen van de nieuwe hoofdcamera overnemen, zoals skybox en culling.
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var main = Camera.main;
        if (_cam == null || main == null) return;
        _cam.CopyFrom(main);
        ApplyStreamSettings();
    }

    void OnDestroy()
    {
        if (_rt != null) _rt.Release();
        if (_resolved != null) _resolved.Release();
        if (_cpuTexture != null) Destroy(_cpuTexture);
    }

    void LateUpdate()
    {
        var head = Camera.main != null ? Camera.main.transform : null;
        bool want = streamEnabled && head != null && sessionClient != null && sessionClient.Connected
                    && sessionClient.Session != null && sessionClient.ScreenPaired;
        IsStreaming = want;
        if (!want)
        {
            if (_cam != null && !_busy) _cam.enabled = false;
            return;
        }
        if (_cam == null) CreateCamera(head.GetComponent<Camera>());

        // Volgen: positie meteen, draaien afgevlakt.
        _cam.transform.position = head.position;
        _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, head.rotation, 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime));

        if (!_busy && Time.unscaledTime >= _next)
        {
            _next = Time.unscaledTime + 1f / framesPerSecond;
            StartCoroutine(CaptureAndSend());
        }
    }

    void CreateCamera(Camera main)
    {
        _rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            name = "Spelerzicht",
            antiAliasing = Mathf.Max(1, msaaSamples)
        };
        _resolved = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Spelerzicht-uitlezen" };
        var go = new GameObject("Spelerzicht-camera");
        go.transform.SetParent(transform, false);
        _cam = go.AddComponent<Camera>();
        _cam.CopyFrom(main);
        ApplyStreamSettings();
    }

    void ApplyStreamSettings()
    {
        _cam.stereoTargetEye = StereoTargetEyeMask.None; // gewone camera, niet voor de bril
        _cam.targetTexture = _rt;
        _cam.fieldOfView = fieldOfView;
        _cam.enabled = false;
    }

    IEnumerator CaptureAndSend()
    {
        _busy = true;

        // De camera rendert alleen in deze ene frame.
        _cam.enabled = true;
        yield return new WaitForEndOfFrame();
        _cam.enabled = false;
        // MSAA-beeld eerst platmaken, uitlezen kan alleen zonder MSAA.
        Graphics.Blit(_rt, _resolved);

        byte[] pixels = null;
        if (SystemInfo.supportsAsyncGPUReadback)
        {
            var request = AsyncGPUReadback.Request(_resolved, 0, TextureFormat.RGBA32);
            yield return new WaitUntil(() => request.done);
            if (!request.hasError) pixels = request.GetData<byte>().ToArray();
        }
        else
        {
            if (_cpuTexture == null) _cpuTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            RenderTexture.active = _resolved;
            _cpuTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            RenderTexture.active = previous;
            pixels = _cpuTexture.GetRawTextureData();
        }
        if (pixels == null)
        {
            _busy = false;
            yield break;
        }

        // JPEG maken op een achtergrond-thread (EncodeArrayToJPG mag dat), zodat de bril niet hapert.
        int w = width, h = height, quality = jpegQuality;
        var encode = Task.Run(() => ImageConversion.EncodeArrayToJPG(pixels, GraphicsFormat.R8G8B8A8_SRGB, (uint)w, (uint)h, 0, quality));
        yield return new WaitUntil(() => encode.IsCompleted);
        var session = sessionClient.Session;
        if (encode.IsFaulted || session == null)
        {
            _busy = false;
            yield break;
        }

        using (var req = new UnityWebRequest($"{sessionClient.ServerUrl}/api/sessions/{session.code}/frame", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(encode.Result) { contentType = "image/jpeg" };
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("X-Game-Token", session.gameToken);
            req.timeout = 2;
            yield return req.SendWebRequest();
            // 429 = net te snel na het vorige beeld: geen probleem.
            if (req.result != UnityWebRequest.Result.Success && req.responseCode != 429)
            {
                if (LastError != req.error) Debug.LogWarning("[PlayerViewStreamer] Failed to send frame: " + req.error);
                LastError = req.error;
            }
            else LastError = "";
        }
        _busy = false;
    }
}
