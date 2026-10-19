using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

// Test-scene voor de koppeling webapp <-> Unity.
// 1. Wachtkamer: QR-code, "x kijkers verbonden", knop Starten.
// 2. De telefoon gaat en het publiek stemt (keuze "telefoon": opnemen / rinkelen).
// 3. De uitslag komt binnen en de telefoon doet wat het publiek koos.
// Het scherm-UI wordt in code gebouwd, zodat de scene zelf simpel blijft.
// Het is een Screen Space-overlay: zichtbaar op het grote scherm, niet in de headset. Kevin ziet de zaal-info dus niet.
public class ApplicationConnection : MonoBehaviour
{
    public VoteClient voteClient;
    public PhoneRinger phone;
    [Tooltip("Stemtijd in seconden (game flow: 12)")]
    public int voteSeconds = 12;
    [Tooltip("Hoe lang de telefoon na de stemming nog rinkelt bij 'laten rinkelen'")]
    public float keepRingingSeconds = 4f;

    enum Phase { WaitingRoom, Voting, KevinDecides, Result }
    Phase phase;

    Font font;
    GameObject waitingPanel, votePanel, kevinPanel, resultPanel;
    RawImage qrImage;
    Text qrPlaceholder, joinUrlText, viewersText, serverText;
    Text voteTitle, voteCounts, voteTimer;
    Text resultTitle, resultDetail;
    string kevinChoice;

    void Awake()
    {
        if (voteClient == null) voteClient = Find<VoteClient>();
        if (phone == null) phone = Find<PhoneRinger>();
#if UNITY_2022_2_OR_NEWER
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
        font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
        BuildUi();
    }

    void Start() => ShowWaitingRoom();

    static T Find<T>() where T : Object
    {
#if UNITY_2023_1_OR_NEWER
        return FindFirstObjectByType<T>();
#else
        return FindObjectOfType<T>();
#endif
    }

    // ---------- Verhaal ----------

    void ShowWaitingRoom()
    {
        phase = Phase.WaitingRoom;
        if (phone != null) phone.ResetPhone();
        Show(waitingPanel);
    }

    void OnStartPressed()
    {
        if (phase != Phase.WaitingRoom) return;
        StartCoroutine(PhoneChapter());
    }

    IEnumerator PhoneChapter()
    {
        phase = Phase.Voting;
        Show(votePanel);
        voteTitle.text = "De telefoon gaat...";
        voteCounts.text = "";
        voteTimer.text = "";

        yield return voteClient.ResetRound(); // nieuwe speelronde: oude stemmen weg
        if (phone != null) phone.Ring();      // dit is de trigger: Kevin hoort de telefoon, het publiek krijgt de vraag

        VoteResult result = null;
        yield return voteClient.RunVote("telefoon", voteSeconds, r => result = r);

        string choice = result != null ? result.winner : "";
        string decidedBy = "publiek";
        if (string.IsNullOrEmpty(choice))
        {
            // Gelijkspel, niemand stemde of server weg: Kevin beslist.
            phase = Phase.KevinDecides;
            kevinChoice = null;
            Show(kevinPanel);
            yield return new WaitUntil(() => kevinChoice != null);
            choice = kevinChoice;
            decidedBy = "kevin";
            if (result != null) yield return voteClient.Decide(result.voteId, choice);
        }

        phase = Phase.Result;
        Show(resultPanel);
        resultDetail.text = DescribeResult(result, decidedBy);

        if (choice == "opnemen")
        {
            resultTitle.text = "OPNEMEN\nKevin neemt op: \"Hallo...?\"";
            resultTitle.color = new Color(0.4f, 1f, 0.5f);
            if (phone != null) phone.PickUp();
        }
        else
        {
            resultTitle.text = "LATEN RINKELEN\nDe telefoon blijft gaan...";
            resultTitle.color = new Color(1f, 0.45f, 0.45f);
            yield return new WaitForSeconds(keepRingingSeconds);
            if (phone != null) phone.StopRinging();
            resultTitle.text = "LATEN RINKELEN\n...en stopt. Iemand weet nu dat Kevin niet opneemt.";
        }
    }

    string DescribeResult(VoteResult r, string decidedBy)
    {
        if (r == null) return "Server niet bereikbaar: Kevin besliste zelf.";
        string counts = "";
        if (r.options != null)
            foreach (var o in r.options) counts += $"{o.label}: {o.count}   ";
        string who = decidedBy == "kevin" ? "Gelijkspel of geen stemmen: Kevin besliste." : "Gekozen door het publiek.";
        return $"{who}\n{counts}({r.total} stemmen)";
    }

    // ---------- Elke frame: live info tonen ----------

    void Update()
    {
        var s = voteClient != null ? voteClient.State : null;
        bool online = voteClient != null && voteClient.Connected;

        if (phase == Phase.WaitingRoom)
        {
            serverText.text = online ? $"Server verbonden ({voteClient.ActiveUrl})" : $"Server niet bereikbaar op {voteClient?.ActiveUrl}. Draait 'npm start' in webapp?";
            serverText.color = online ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.5f, 0.5f);
            viewersText.text = s != null ? $"{s.viewers} kijker{(s.viewers == 1 ? "" : "s")} verbonden" : "0 kijkers verbonden";
            if (voteClient != null && voteClient.QrTexture != null && qrImage.texture != voteClient.QrTexture)
            {
                qrImage.texture = voteClient.QrTexture;
                qrImage.color = Color.white;
                qrPlaceholder.text = "";
            }
            joinUrlText.text = voteClient != null ? voteClient.JoinUrl : "";
            if (KeyDown(KeyCode.Space)) OnStartPressed();
        }
        else if (phase == Phase.Voting && s != null && s.active && s.vote != null && s.vote.id == "telefoon")
        {
            voteTitle.text = "De telefoon gaat! Het publiek stemt...";
            voteTimer.text = $"Nog {s.vote.remaining} s";
            string counts = "";
            if (s.vote.options != null)
                foreach (var o in s.vote.options) counts += $"{o.label}: {o.count}\n";
            voteCounts.text = counts + $"({s.vote.total} van {s.viewers} gestemd)";
        }
        else if (phase == Phase.KevinDecides)
        {
            // Placeholder: later beslist Kevin door iets aan te raken in VR.
            if (KeyDown(KeyCode.Alpha1)) kevinChoice = "opnemen";
            if (KeyDown(KeyCode.Alpha2)) kevinChoice = "rinkelen";
        }
    }

    static bool KeyDown(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return false;
        switch (key)
        {
            case KeyCode.Space: return kb.spaceKey.wasPressedThisFrame;
            case KeyCode.Alpha1: return kb.digit1Key.wasPressedThisFrame;
            case KeyCode.Alpha2: return kb.digit2Key.wasPressedThisFrame;
            default: return false;
        }
#else
        return Input.GetKeyDown(key);
#endif
    }

    // ---------- UI bouwen ----------

    void Show(GameObject panel)
    {
        waitingPanel.SetActive(panel == waitingPanel);
        votePanel.SetActive(panel == votePanel);
        kevinPanel.SetActive(panel == kevinPanel);
        resultPanel.SetActive(panel == resultPanel);
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("Groot scherm (UI)", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var root = canvasGo.transform;

        if (Find<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        // Wachtkamer
        waitingPanel = Panel(root, "Wachtkamer");
        Label(waitingPanel.transform, "Home Together", 72, new Vector2(0, 420), new Vector2(1600, 100));
        Label(waitingPanel.transform, "Scan de QR-code en doe mee", 40, new Vector2(0, 340), new Vector2(1600, 60));
        var qrBg = Box(waitingPanel.transform, "QR achtergrond", Color.white, new Vector2(0, 40), new Vector2(480, 480));
        qrImage = new GameObject("QR", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        qrImage.transform.SetParent(qrBg.transform, false);
        Place(qrImage.rectTransform, Vector2.zero, new Vector2(440, 440));
        qrImage.color = new Color(1, 1, 1, 0);
        qrPlaceholder = Label(qrBg.transform, "QR-code laden...\n(start de webapp-server)", 28, Vector2.zero, new Vector2(440, 200));
        qrPlaceholder.color = Color.black;
        joinUrlText = Label(waitingPanel.transform, "", 30, new Vector2(0, -230), new Vector2(1600, 50));
        viewersText = Label(waitingPanel.transform, "0 kijkers verbonden", 44, new Vector2(0, -300), new Vector2(1600, 60));
        MakeButton(waitingPanel.transform, "Starten", new Vector2(0, -400), OnStartPressed);
        serverText = Label(waitingPanel.transform, "", 24, new Vector2(0, -500), new Vector2(1800, 40));

        // Stemming
        votePanel = Panel(root, "Stemming", 0.35f);
        voteTitle = Label(votePanel.transform, "", 64, new Vector2(0, 300), new Vector2(1800, 100));
        voteTimer = Label(votePanel.transform, "", 56, new Vector2(0, 180), new Vector2(1000, 80));
        voteCounts = Label(votePanel.transform, "", 48, new Vector2(0, -60), new Vector2(1400, 300));

        // Kevin beslist (gelijkspel)
        kevinPanel = Panel(root, "Kevin beslist", 0.35f);
        Label(kevinPanel.transform, "Gelijkspel: Kevin beslist!", 64, new Vector2(0, 250), new Vector2(1800, 100));
        Label(kevinPanel.transform, "(placeholder: klik of druk 1 / 2)", 32, new Vector2(0, 160), new Vector2(1800, 50));
        MakeButton(kevinPanel.transform, "1  Opnemen", new Vector2(-300, -50), () => kevinChoice = "opnemen");
        MakeButton(kevinPanel.transform, "2  Laten rinkelen", new Vector2(300, -50), () => kevinChoice = "rinkelen");

        // Uitslag
        resultPanel = Panel(root, "Uitslag", 0.35f);
        Label(resultPanel.transform, "De keuze is binnen:", 40, new Vector2(0, 380), new Vector2(1600, 60));
        resultTitle = Label(resultPanel.transform, "", 64, new Vector2(0, 240), new Vector2(1800, 200));
        resultDetail = Label(resultPanel.transform, "", 36, new Vector2(0, 40), new Vector2(1800, 140));
        MakeButton(resultPanel.transform, "Opnieuw", new Vector2(0, -400), () => { StopAllCoroutines(); ShowWaitingRoom(); });
    }

    GameObject Panel(Transform parent, string name, float alpha = 0.85f)
    {
        var go = Box(parent, name, new Color(0.05f, 0.07f, 0.12f, alpha), Vector2.zero, Vector2.zero).gameObject;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return go;
    }

    Image Box(Transform parent, string name, Color color, Vector2 pos, Vector2 size)
    {
        var img = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.color = color;
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
        var img = Box(parent, "Knop " + text, new Color(0.9f, 0.25f, 0.2f), pos, new Vector2(460, 110));
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
