using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

// Test-scene voor de koppeling website <-> Unity. Nog geen verhaal, alleen de koppeling:
// 1. Verbinden en een sessie maken: de code van 6 cijfers staat in de bril.
// 2. Wachten tot het grote scherm de code invult op <site>/screen (ScreenPaired).
// 3. Tutorial (nu leeg) en minstens 60 s wachten, zodat het publiek kan joinen. Dan StartGame.
//    De wachttijd kan overgeslagen worden met de knop "Skip countdown" op het grote scherm (LobbySkipped).
// 4. Teststemming: de telefoon gaat, het publiek stemt, de uitslag komt hier binnen.
// In de bril: één paneel in de wereld dat je blik volgt (code, wachten, uitslag). Op de pc: een debug-overlay.
// Het zicht van de speler gaat naar het grote scherm (PlayerViewStreamer). In de Quest zorgt XRRigSetup voor hoofdtracking.
// Bediening:            bril (controllers)                          Editor (toetsen)
//   zonder groot scherm  A/X, zichtbaar na 2 minuten op het codescherm   S
//   wachttijd overslaan  A/X tijdens het wachten (of knop op /screen)    K
//   Kevin beslist        naar de telefoon kijken + trigger = opnemen,    1 = opnemen, 2 = laten rinkelen
//                        8 s niets doen = laten rinkelen
//   opnieuw (nieuwe code) A/X op het uitslagscherm                       R
public class ApplicationConnection : MonoBehaviour
{
    [FormerlySerializedAs("voteClient")] public SessionClient sessionClient;
    public PhoneRinger phone;
    public Tutorial tutorial;
    public PlayerViewStreamer playerView;
    [Tooltip("Minimale wachttijd na het koppelen, zodat het publiek kan joinen (s)")]
    public int lobbySeconds = 60;
    [Tooltip("Stemtijd in seconden (game flow: 12)")]
    public int voteSeconds = 12;
    [Tooltip("Hoe lang de telefoon na de stemming nog rinkelt bij 'laten rinkelen'")]
    public float keepRingingSeconds = 4f;
    [Tooltip("Na zoveel seconden op het codescherm mag de speler kiezen om zonder groot scherm te spelen")]
    public float offlineOptionAfter = 120f;
    [Tooltip("Zo lang heeft Kevin om de telefoon op te nemen bij een gelijkspel; daarna laat hij hem rinkelen")]
    public float kevinDecideSeconds = 8f;
    [Tooltip("Afstand van het paneel in de bril (m)")]
    public float panelDistance = 2.2f;

    enum Phase { Connecting, ShowCode, Tutorial, WaitingForAudience, Voting, KevinDecides, Result }
    Phase phase;
    bool offline; // zonder groot scherm verder
    float lobbyStart;
    float codeShownAt;
    float kevinDeadline;
    string kevinChoice;

    Font font;
    GameObject vrPanel;
    Text vrTitle, vrBig, vrSmall;
    bool panelPlaced;
    Text debugText;
    GameObject kevinButtons;

#if ENABLE_INPUT_SYSTEM
    InputAction triggerAction; // trigger links of rechts
    InputAction buttonAction; // A of X
#endif

    void Awake()
    {
        if (Find<XRRigSetup>() == null) gameObject.AddComponent<XRRigSetup>(); // hoofdtracking in de Quest
        if (sessionClient == null) sessionClient = Find<SessionClient>() ?? gameObject.AddComponent<SessionClient>();
        if (phone == null) phone = Find<PhoneRinger>();
        if (tutorial == null) tutorial = Find<Tutorial>() ?? gameObject.AddComponent<Tutorial>();
        if (playerView == null) playerView = Find<PlayerViewStreamer>() ?? gameObject.AddComponent<PlayerViewStreamer>();
        playerView.sessionClient = sessionClient;
#if UNITY_2022_2_OR_NEWER
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
        font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
        BuildVrPanel();
        BuildDebugOverlay();
    }

    void OnEnable()
    {
#if ENABLE_INPUT_SYSTEM
        triggerAction = new InputAction("Trigger", InputActionType.Button);
        triggerAction.AddBinding("<XRController>{RightHand}/triggerPressed");
        triggerAction.AddBinding("<XRController>{LeftHand}/triggerPressed");
        buttonAction = new InputAction("A of X", InputActionType.Button);
        buttonAction.AddBinding("<XRController>{RightHand}/primaryButton");
        buttonAction.AddBinding("<XRController>{LeftHand}/primaryButton");
        triggerAction.Enable();
        buttonAction.Enable();
#endif
    }

    void OnDisable()
    {
#if ENABLE_INPUT_SYSTEM
        triggerAction?.Dispose();
        buttonAction?.Dispose();
#endif
    }

    void Start() => StartCoroutine(RunSession());

    static T Find<T>() where T : Object
    {
#if UNITY_2023_1_OR_NEWER
        return FindFirstObjectByType<T>();
#else
        return FindObjectOfType<T>();
#endif
    }

    // ---------- Verloop ----------

    IEnumerator RunSession()
    {
        if (phone != null) phone.ResetPhone();
        kevinButtons.SetActive(false);
        offline = false;

        // 1. Verbinden en sessie maken (blijft proberen tot het lukt, of zonder groot scherm verder).
        phase = Phase.Connecting;
        codeShownAt = Time.unscaledTime;
        while (string.IsNullOrEmpty(sessionClient.Code) && !offline)
        {
            ShowVr("Connecting...", "", "Hang on, looking for the server." + OfflineHint());
            yield return sessionClient.StartSession();
            if (!string.IsNullOrEmpty(sessionClient.Code)) break;
            ShowVr("No connection", "", "Can't reach the server. Retrying..." + OfflineHint());
            yield return WaitUnscaled(3f);
        }

        // 2. Code tonen tot het grote scherm gekoppeld is.
        if (!offline)
        {
            phase = Phase.ShowCode;
            codeShownAt = Time.unscaledTime;
            string site = HostOf(sessionClient.ServerUrl);
            while (!sessionClient.ScreenPaired && !sessionClient.SessionLost && !offline)
            {
                ShowVr("Pair the big screen", PrettyCode(sessionClient.Code), $"On the laptop, go to {site}/screen\nand type this code." + OfflineHint());
                yield return null;
            }
            if (sessionClient.SessionLost)
            {
                StartCoroutine(RunSession()); // sessie vervallen: nieuwe code
                yield break;
            }
        }

        // 3. Tutorial + wachtkamer: verder als de tutorial klaar is EN er minstens lobbySeconds voorbij zijn.
        lobbyStart = Time.unscaledTime;
        if (!offline) yield return sessionClient.StartTutorial(lobbySeconds);
        phase = Phase.Tutorial;
        ShowVr("Tutorial", "", "(coming soon)");
        tutorial.Begin();
        yield return new WaitUntil(() => tutorial.IsDone);

        phase = Phase.WaitingForAudience;
        while (!offline && LobbyLeft() > 0 && !sessionClient.LobbySkipped)
        {
            ShowVr("The audience is joining", $"{Mathf.CeilToInt(LobbyLeft())} s", "The game starts soon.\n\nA = skip the countdown");
            yield return null;
        }
        if (!offline) yield return sessionClient.StartGame();

        // 4. Teststemming.
        yield return PhoneVote();
    }

    float LobbyLeft() => lobbySeconds - (Time.unscaledTime - lobbyStart);

    bool OfflineOptionVisible => (phase == Phase.Connecting || phase == Phase.ShowCode) && Time.unscaledTime - codeShownAt >= offlineOptionAfter;

    string OfflineHint() => OfflineOptionVisible ? "\n\nNo big screen? Press A to play without one." : "";

    IEnumerator PhoneVote()
    {
        phase = Phase.Voting;
        HideVr(); // de speler hoort de telefoon, het publiek stemt
        if (phone != null) phone.Ring();

        VoteResult result = null;
        if (!offline) yield return sessionClient.RunVote("telefoon", voteSeconds, r => result = r);

        string choice = result != null ? result.winner : "";
        if (string.IsNullOrEmpty(choice))
        {
            // Gelijkspel, niemand stemde of zonder server: Kevin beslist.
            phase = Phase.KevinDecides;
            kevinChoice = null;
            kevinDeadline = Time.unscaledTime + kevinDecideSeconds;
            kevinButtons.SetActive(true);
            while (kevinChoice == null)
            {
                float left = kevinDeadline - Time.unscaledTime;
                if (left <= 0) kevinChoice = "rinkelen";
                else ShowVr("You decide!", $"{Mathf.CeilToInt(left)} s", "Look at the phone and pull the trigger to answer.\nDo nothing = let it ring.");
                yield return null;
            }
            kevinButtons.SetActive(false);
            choice = kevinChoice;
            if (result != null) yield return sessionClient.Decide(result.voteId, choice);
        }

        phase = Phase.Result;
        if (choice == "opnemen")
        {
            if (phone != null) phone.PickUp();
            ShowVr("Answer it", "", "\"Hello...?\"\n\nA = restart");
        }
        else
        {
            ShowVr("Let it ring", "", "The phone keeps ringing...\n\nA = restart");
            yield return WaitUnscaled(keepRingingSeconds);
            if (phone != null) phone.StopRinging();
        }
    }

    // Sessie netjes afsluiten (code vervalt) en opnieuw beginnen met een nieuwe code.
    IEnumerator Restart()
    {
        yield return sessionClient.EndSession();
        yield return RunSession();
    }

    IEnumerator WaitUnscaled(float seconds)
    {
        float until = Time.unscaledTime + seconds;
        while (Time.unscaledTime < until && !offline) yield return null;
    }

    // ---------- Elke frame ----------

    void Update()
    {
        bool button = ButtonPressed();
        if ((phase == Phase.Connecting || phase == Phase.ShowCode) && (KeyDown(Key.S) || (button && OfflineOptionVisible)))
        {
            // Zonder groot scherm spelen: Kevin beslist alles zelf.
            offline = true;
            sessionClient.GoOffline();
        }
        if (phase == Phase.WaitingForAudience && (KeyDown(Key.K) || button))
        {
            // Wachttijd overslaan, net als de knop op het grote scherm.
            if (!offline && !sessionClient.LobbySkipped) StartCoroutine(sessionClient.SkipLobby());
        }
        if (phase == Phase.KevinDecides)
        {
            if (KeyDown(Key.One) || (TriggerPressed() && LookingAtPhone())) kevinChoice = "opnemen";
            if (KeyDown(Key.Two)) kevinChoice = "rinkelen";
        }
        if (phase == Phase.Result && (KeyDown(Key.R) || button))
        {
            StopAllCoroutines();
            phase = Phase.Connecting;
            StartCoroutine(Restart());
        }

        UpdateDebugOverlay();
    }

    void LateUpdate() => FollowHead();

    // Kijkt de speler (ongeveer) naar de telefoon?
    bool LookingAtPhone()
    {
        var cam = Camera.main;
        if (cam == null || phone == null) return true; // niets om naar te kijken: elke trigger telt
        var toPhone = phone.transform.position - cam.transform.position;
        return Vector3.Angle(cam.transform.forward, toPhone) < 20f;
    }

    void UpdateDebugOverlay()
    {
        var c = sessionClient;
        string text =
            $"Server: {c.ServerUrl} ({(offline ? "offline by choice" : c.ConnectionStatus.ToString())})\n" +
            $"Code: {(string.IsNullOrEmpty(c.Code) ? "-" : PrettyCode(c.Code))}   Big screen: {(c.ScreenPaired ? "paired" : "not yet")}   Viewers: {c.Viewers}\n" +
            $"Phase: {phase}";
        if (phase == Phase.WaitingForAudience) text += c.LobbySkipped ? " (skipped)" : $" ({Mathf.CeilToInt(Mathf.Max(0, LobbyLeft()))} s left)";
        if (playerView != null)
            text += $"\nPlayer view: {(playerView.IsStreaming ? $"on ({playerView.framesPerSecond} per s)" : "off")}"
                    + (string.IsNullOrEmpty(playerView.LastError) ? "" : $" <color=#ff8080>{playerView.LastError}</color>");
        var vote = c.LiveVote;
        if (vote != null && vote.options != null)
        {
            text += $"\nVote: {vote.question}";
            foreach (var o in vote.options) text += $"\n   {o.label}: {o.count}";
        }
        if (!string.IsNullOrEmpty(c.LastError)) text += $"\n<color=#ff8080>{c.LastError}</color>";
        if (phase == Phase.Connecting || phase == Phase.ShowCode) text += "\n[S] play without a big screen";
        if (phase == Phase.WaitingForAudience) text += "\n[K] skip the countdown";
        debugText.text = text;
    }

    // ---------- Invoer ----------

    bool TriggerPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return triggerAction != null && triggerAction.WasPressedThisFrame();
#else
        return false;
#endif
    }

    bool ButtonPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return buttonAction != null && buttonAction.WasPressedThisFrame();
#else
        return false;
#endif
    }

    enum Key { S, R, K, One, Two }

    static bool KeyDown(Key key)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return false;
        switch (key)
        {
            case Key.S: return kb.sKey.wasPressedThisFrame;
            case Key.R: return kb.rKey.wasPressedThisFrame;
            case Key.K: return kb.kKey.wasPressedThisFrame;
            case Key.One: return kb.digit1Key.wasPressedThisFrame;
            case Key.Two: return kb.digit2Key.wasPressedThisFrame;
            default: return false;
        }
#else
        switch (key)
        {
            case Key.S: return Input.GetKeyDown(KeyCode.S);
            case Key.R: return Input.GetKeyDown(KeyCode.R);
            case Key.K: return Input.GetKeyDown(KeyCode.K);
            case Key.One: return Input.GetKeyDown(KeyCode.Alpha1);
            case Key.Two: return Input.GetKeyDown(KeyCode.Alpha2);
            default: return false;
        }
#endif
    }

    static string PrettyCode(string code) => code.Length == 6 ? code.Substring(0, 3) + " " + code.Substring(3) : code;

    static string HostOf(string url) => System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri) ? uri.Authority : url;

    // ---------- Paneel in de bril ----------

    void ShowVr(string title, string big, string small)
    {
        vrPanel.SetActive(true);
        vrTitle.text = title;
        vrBig.text = big;
        vrSmall.text = small;
    }

    void HideVr() => vrPanel.SetActive(false);

    // Het paneel blijft vindbaar: kijkt de speler er ver naast, dan schuift het rustig mee naar voren.
    // De eerste keer wordt het pas geplaatst als de hoofdtracking een geldige hoogte geeft.
    void FollowHead()
    {
        var cam = Camera.main;
        if (cam == null || !vrPanel.activeSelf) return;
        var head = cam.transform;
        if (head.position.y < 0.5f && Time.timeSinceLevelLoad < 2f) return; // tracking nog niet klaar

        var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f) return;
        forward.Normalize();
        var target = head.position + forward * panelDistance + Vector3.up * 0.15f;
        var rt = vrPanel.transform;

        if (!panelPlaced)
        {
            rt.SetPositionAndRotation(target, Quaternion.LookRotation(forward, Vector3.up));
            panelPlaced = true;
            return;
        }
        var toPanel = Vector3.ProjectOnPlane(rt.position - head.position, Vector3.up);
        if (Vector3.Angle(forward, toPanel) > 40f || Mathf.Abs(toPanel.magnitude - panelDistance) > 1f)
        {
            float t = 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime);
            rt.position = Vector3.Lerp(rt.position, target, t);
            rt.rotation = Quaternion.Slerp(rt.rotation, Quaternion.LookRotation(forward, Vector3.up), t);
        }
    }

    // World-space canvas. 1600x900 pixels = ongeveer 1,9 x 1,1 m.
    void BuildVrPanel()
    {
        vrPanel = new GameObject("Paneel in de bril", typeof(Canvas), typeof(CanvasScaler));
        vrPanel.transform.SetParent(transform, false);
        var canvas = vrPanel.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        vrPanel.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4; // scherpere tekst
        var rt = (RectTransform)vrPanel.transform;
        rt.sizeDelta = new Vector2(1600, 900);
        rt.localScale = Vector3.one * 0.0012f;

        Box(vrPanel.transform, "Achtergrond", new Color(0.05f, 0.07f, 0.12f, 0.88f), Vector2.zero, new Vector2(1600, 900));
        vrTitle = Label(vrPanel.transform, "", 80, new Vector2(0, 300), new Vector2(1500, 120));
        vrTitle.color = new Color(0.96f, 0.77f, 0.26f); // goud, zoals de website
        vrBig = Label(vrPanel.transform, "", 230, new Vector2(0, 60), new Vector2(1550, 300));
        vrSmall = Label(vrPanel.transform, "", 56, new Vector2(0, -260), new Vector2(1500, 300));
    }

    // ---------- Debug-overlay op de pc ----------

    void BuildDebugOverlay()
    {
        var canvasGo = new GameObject("Debug (pc)", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        if (Find<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        var bg = Box(canvasGo.transform, "Debug achtergrond", new Color(0, 0, 0, 0.55f), Vector2.zero, new Vector2(900, 300));
        var bgRt = bg.rectTransform;
        bgRt.anchorMin = bgRt.anchorMax = bgRt.pivot = new Vector2(0, 1);
        bgRt.anchoredPosition = new Vector2(20, -20);
        debugText = Label(bg.transform, "", 24, Vector2.zero, new Vector2(860, 280));
        debugText.alignment = TextAnchor.UpperLeft;
        debugText.supportRichText = true;

        kevinButtons = new GameObject("Kevin beslist", typeof(RectTransform));
        kevinButtons.transform.SetParent(canvasGo.transform, false);
        MakeButton(kevinButtons.transform, "1  Answer it", new Vector2(-300, -380), () => kevinChoice = "opnemen");
        MakeButton(kevinButtons.transform, "2  Let it ring", new Vector2(300, -380), () => kevinChoice = "rinkelen");
        kevinButtons.SetActive(false);
    }

    // ---------- UI-hulp ----------

    Image Box(Transform parent, string name, Color color, Vector2 pos, Vector2 size)
    {
        var img = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.color = color;
        img.raycastTarget = false;
        Place(img.rectTransform, pos, size);
        return img;
    }

    Text Label(Transform parent, string text, int size, Vector2 pos, Vector2 box)
    {
        var t = new GameObject("Tekst", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = font;
        t.text = text;
        t.fontSize = size;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        Place(t.rectTransform, pos, box);
        return t;
    }

    void MakeButton(Transform parent, string text, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        var img = Box(parent, "Knop " + text, new Color(0.78f, 0.06f, 0.18f), pos, new Vector2(460, 110));
        img.raycastTarget = true;
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        Label(img.transform, text, 44, Vector2.zero, new Vector2(440, 100));
    }

    static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }
}
