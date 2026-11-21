using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// De volledige game loop van Home Together (plan: game-flow-full-en.md, 10 stappen, 3 eindes).
///
/// Regels uit het plan:
/// - Kevin loopt vrij rond. Deze code verplaatst, teleporteert of fadet de speler NOOIT.
/// - Het verhaal wacht op Kevin: een stap start pas als hij ergens aankomt. Geluid en licht lokken hem.
/// - Kevin ziet nooit tekst, knoppen of uitslagen. Hij merkt de keuze van het publiek alleen aan wat er gebeurt.
/// - Stemmingen lopen terwijl Kevin met iets anders bezig is.
///
/// Gebruik: zit automatisch in de scene "demo_city_night" (wordt bij Play zelf aangemaakt als hij ontbreekt).
/// Met Tools > Home Together > Game loop in deze scene zetten krijg je markers ("Story_...") die je naar de
/// juiste plek in het huis sleept.
///
/// Testen in de Editor zonder telefoons: 1 / 2 = optie A / B tijdens een stemming, J = samen-actie gelukt,
/// N = huidige wachtstap overslaan. Zonder server beslist Kevin zelf bij elke keuze (wat hij eerst doet).
/// Zonder VR-bril start automatisch de testbesturing (DesktopTestRig): WASD + muis, E = pakken, F = deur, H = hulp.
/// </summary>
[DisallowMultipleComponent]
public class HomeTogetherGame : MonoBehaviour
{
    public const string GameScene = "demo_city_night";

    /// <summary>Exacte namen van de objecten in de Hierarchy (aangeleverd door Renzo, 16/11).</summary>
    [Serializable]
    public class ObjectNames
    {
        public string tree = "ChristmasTree (2)";
        public string phone = "Phone prefab";
        public string note = "StickyNote_Fridge";
        public string flashlight = "Flashlight Grabbable";
        public string backDoor = "Exterior_Door (1)";
        public string cellarDoor = "Door_3B";
        public string fuseBox = "Shield Metall";
        public string attic = "RoofFloor (2)";
        public string gardenWindow = "Exterior_WindowB";
        public string closet = "Closet";
        public string dog = "dog-animated";
        public string burglar = "rob";
        public string marleyHouse = "small_house_1 (2)";
        public string[] presents = { "present_01", "present_05" };
    }

    [Header("Objecten in de scene (exacte naam uit de Hierarchy)")]
    public ObjectNames objects = new ObjectNames();

    /// <summary>De namen die ComputePoints gebruikt (ook vanuit het Tools-menu).</summary>
    public static ObjectNames Names = new ObjectNames();

    /// <summary>Object met deze naam; bij dubbele namen het exemplaar het dichtst bij 'near'.</summary>
    public static GameObject Named(string name, Vector3 near) =>
        string.IsNullOrWhiteSpace(name) ? null : Story.FindExactNear(name, near);

    public enum TestMode { Auto, Altijd, Nooit }

    [Header("Testen zonder VR-bril")]
    [Tooltip("Auto: toetsenbord/muis-besturing als er geen bril actief is. Altijd: ook met bril. Nooit: uit.")]
    public TestMode desktopTest = TestMode.Auto;

    [Header("Algemeen")]
    public bool autoStart = true;
    [Tooltip("Seconden voor een stemming.")] public int voteSeconds = 12;
    [Tooltip("Sneeuw rond het huis (laag om op te lopen, hoopjes en vallende sneeuw).")] public bool snowAroundHouse = true;
    [Tooltip("Een stemming duurt minstens zo lang, ook als Kevin het briefje meteen neerlegt.")] public int minVoteSeconds = 5;
    [Tooltip("Hoe dicht (meter) Kevin bij het midden van de kast moet staan om 'in de kast' te zijn.")] public float closetRadius = 0.75f;
    [Tooltip("Seconden voor 'inbreker' als de telefoon niet opgenomen werd.")] public int fastVoteSeconds = 8;
    [Tooltip("Seconden voor de samen-actie.")] public int togetherSeconds = 5;
    [Tooltip("Even wachten na het laden voor het verhaal start.")] public float startDelay = 2f;

    [Header("Deuren (graden; zet een minteken als een deur de verkeerde kant op zwaait)")]
    public float backDoorOpenAngle = 95f;
    public float cellarDoorOpenAngle = 90f;

    [Header("Eigen geluiden (leeg = automatisch gemaakt plaatshouder-geluid)")]
    public List<StorySounds.Override> soundOverrides = new List<StorySounds.Override>();

    [Header("Status (alleen lezen)")]
    public string step = "";
    public bool marleyKnows;
    public bool burglarsFast;
    public bool dogInside;
    public bool lightsOn = true;
    public bool hidden;
    public bool escaped;
    public string ending = "";
    public List<string> choices = new List<string>();

    // ---------- Automatisch in de hoofdscene ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryCreate(SceneManager.GetActiveScene());
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryCreate(scene);

    static void TryCreate(Scene scene)
    {
        if (scene.name != GameScene) return;
        if (FindFirstObjectByType<HomeTogetherGame>(FindObjectsInactive.Include) != null) return;
        new GameObject("HomeTogether (game loop)").AddComponent<HomeTogetherGame>();
    }

    // ---------- Plekken in het huis ----------

    public static readonly string[] MarkerNames =
    {
        "Story_Kerstboom", "Story_Telefoon", "Story_Briefje", "Story_Zaklamp", "Story_Achterdeur", "Story_Zolderluik",
        "Story_Kelderdeur", "Story_Zekeringkast", "Story_Tuinraam", "Story_Kast", "Story_Marley", "Story_Straat",
    };

    public class Points
    {
        public Vector3 start, forward, right;
        public float floorY;
        public Vector3 tree, phone, note, flashlight, backDoor, attic, cellarDoor, fuseBox, gardenWindow, closet, marley, street;
        /// <summary>Markers waarvan de plek uit een echt object in de scene komt.</summary>
        public readonly HashSet<string> fromObject = new HashSet<string>();

        public Vector3 Get(string marker)
        {
            switch (marker)
            {
                case "Story_Kerstboom": return tree;
                case "Story_Telefoon": return phone;
                case "Story_Briefje": return note;
                case "Story_Zaklamp": return flashlight;
                case "Story_Achterdeur": return backDoor;
                case "Story_Zolderluik": return attic;
                case "Story_Kelderdeur": return cellarDoor;
                case "Story_Zekeringkast": return fuseBox;
                case "Story_Tuinraam": return gardenWindow;
                case "Story_Kast": return closet;
                case "Story_Marley": return marley;
                case "Story_Straat": return street;
            }
            return start;
        }
    }

    public static string MarkerHelp(string marker)
    {
        switch (marker)
        {
            case "Story_Kerstboom": return "Midden van de kerstboom (woonkamer).";
            case "Story_Telefoon": return "Waar de telefoon hangt. Staat er geen object met 'Phone' in de naam, dan komt hier een eenvoudige telefoon.";
            case "Story_Briefje": return "De post-it op de koelkast (StickyNote).";
            case "Story_Zaklamp": return "Zaklamp op het aanrecht. Staat er geen object met 'Flashlight' in de naam, dan komt hier een eenvoudige zaklamp.";
            case "Story_Achterdeur": return "Net BUITEN de achterdeur, waar de hond en de inbreker staan. De dichtstbijzijnde VR-deur wordt de achterdeur.";
            case "Story_Zolderluik": return "Zolderluik in het plafond van de gang.";
            case "Story_Kelderdeur": return "Bij de kelderdeur. De dichtstbijzijnde VR-deur wordt de kelderdeur (op slot tot 'kelder').";
            case "Story_Zekeringkast": return "Zekeringkast met hendel (in de kelder), op ooghoogte.";
            case "Story_Tuinraam": return "Midden van het tuinraam waar Kevin door ontsnapt.";
            case "Story_Kast": return "Midden van de lattenkast (Closet) waar Kevin zich verstopt.";
            case "Story_Marley": return "Voordeur van Marleys huis (buren).";
            case "Story_Straat": return "Op straat, waar het busje stopt.";
        }
        return "";
    }

    /// <summary>
    /// Berekent alle plekken. Per plek: een marker die je met de hand verplaatste wint, anders het echte object
    /// uit de scene (zie Objecten), anders de marker zoals hij staat, anders een gok.
    /// </summary>
    public static Points ComputePoints(Vector3 startPos, Vector3 startForward, float floorY)
    {
        var p = new Points { start = startPos, floorY = floorY };
        p.forward = Story.Flat(startForward).sqrMagnitude > 0.001f ? Story.Flat(startForward).normalized : Vector3.forward;
        p.right = Vector3.Cross(Vector3.up, p.forward);
        Vector3 At(Vector3 v, float h) => new Vector3(v.x, floorY + h, v.z);
        Vector3 Place(string marker, bool hasObject, Func<Vector3> fromObject, Vector3 guess)
        {
            var m = Story.MarkerTransform(marker);
            var sm = m != null ? m.GetComponent<StoryMarker>() : null;
            if (m != null && sm != null && !sm.volgObject) return m.position; // met de hand gezet
            if (hasObject) { p.fromObject.Add(marker); return fromObject(); }
            return m != null ? m.position : guess;
        }

        var N = Names ?? new ObjectNames();
        var treeObj = Named(N.tree, startPos) ?? Story.FindExact("Christmas Tree", "ChristmasTree", "Kerstboom", "Ornamentals");
        p.tree = Place("Story_Kerstboom", treeObj != null, () => Story.Center(treeObj), At(startPos + p.forward * 3f, 1f));

        var noteObj = Named(N.note, p.tree);
        if (noteObj == null) { var sn = FindFirstObjectByType<StickyNote>(FindObjectsInactive.Include); if (sn != null) noteObj = sn.gameObject; }
        p.note = Place("Story_Briefje", noteObj != null, () => Story.Center(noteObj), At(startPos + p.right * 4f, 1.5f));

        var phoneObj = Named(N.phone, p.tree) ?? FindPhone();
        p.phone = Place("Story_Telefoon", phoneObj != null, () => Story.Center(phoneObj), At(startPos - p.forward * 2.5f, 1.4f));

        var flashObj = Named(N.flashlight, p.note) ?? FindFlashlight();
        Vector3 towardStart = Story.FlatDir(p.note, startPos, -p.right);
        p.flashlight = Place("Story_Zaklamp", flashObj != null, () => flashObj.transform.position,
            At(p.note + towardStart * 0.7f + Vector3.Cross(Vector3.up, towardStart) * 0.6f, 0.95f));

        var doorObj = Named(N.backDoor, p.tree);
        var rob = Named(N.burglar, p.tree);
        p.backDoor = Place("Story_Achterdeur", doorObj != null || rob != null,
            () => doorObj != null ? At(Story.Center(doorObj) + Story.FlatDir(p.tree, Story.Center(doorObj), -p.right) * 1.2f, 0f) : rob.transform.position,
            At(startPos - p.right * 5f, 0f));

        var atticObj = Named(N.attic, p.tree);
        p.attic = Place("Story_Zolderluik", atticObj != null,
            () => new Vector3(Story.Center(atticObj).x, Story.Bounds(atticObj).min.y, Story.Center(atticObj).z),
            At(Vector3.Lerp(startPos, p.backDoor, 0.6f), 2.6f));

        var cellarObj = Named(N.cellarDoor, p.tree);
        var stairs = cellarObj != null ? null : Story.FindExact("Stairs");
        p.cellarDoor = Place("Story_Kelderdeur", cellarObj != null || stairs != null,
            () => At(Story.Center(cellarObj != null ? cellarObj : stairs), 1f),
            At(Vector3.Lerp(startPos, p.backDoor, 0.5f) - p.forward * 1.5f, 1f));

        var fuseObj = Named(N.fuseBox, p.cellarDoor);
        p.fuseBox = Place("Story_Zekeringkast", fuseObj != null, () => Story.Center(fuseObj), At(p.cellarDoor - p.forward * 1.5f, 1.4f));

        var win = Named(N.gardenWindow, p.tree) ?? Story.FindNearest("Exterior_Window", p.tree, 8f);
        p.gardenWindow = Place("Story_Tuinraam", win != null, () => Story.Center(win), At(p.tree + p.forward * 1.5f, 1.2f));

        var closetObj = Named(N.closet, p.tree);
        p.closet = Place("Story_Kast", closetObj != null, () => Story.Center(closetObj), At(Vector3.Lerp(startPos, p.backDoor, 0.7f) + p.forward, 1f));

        var marleyObj = Named(N.marleyHouse, p.note);
        Vector3 kitchenSide = Story.FlatDir(p.tree, p.note, p.right);
        p.marley = Place("Story_Marley", marleyObj != null, () => At(Story.Bounds(marleyObj).ClosestPoint(p.note), 0f), At(p.note + kitchenSide * 18f, 0f));
        p.street = Place("Story_Straat", false, null, At(startPos - p.forward * 14f, 0f));
        return p;
    }

    static GameObject FindPhone()
    {
        var go = Story.FindExact("Phone");
        return go != null ? go : Story.FindContains("phone", "headphone", "microphone", "smartphone");
    }

    static GameObject FindFlashlight() => Story.FindContains("flashlight");

    // ---------- Runtime ----------

    class VoteRun
    {
        public string id;
        public bool done, stop, online;
        public int voteId;
        public string winner = "";
        public string forced = "";
    }

    static readonly Dictionary<string, string[]> Options = new Dictionary<string, string[]>
    {
        { "telefoon", new[] { "opnemen", "rinkelen" } },
        { "hond", new[] { "binnen", "buiten" } },
        { "stroom", new[] { "kelder", "zaklamp" } },
        { "inbreker", new[] { "kast", "raam" } },
    };

    Transform head;
    Points P;
    SessionClient session;
    VoteRun activeVote;
    bool debugSkip, debugTogether;
    readonly Dictionary<string, float> dwell = new Dictionary<string, float>();

    VRDoor backDoor, cellarDoor;
    Vector3 doorInside, doorOutside, doorOut;
    GameObject rob, dog, phone, note, flashlight, fuseBox, closet, parcel;
    Animator robAnim, dogAnim;
    XRSimpleInteractable phoneTouch, leverTouch;
    XRGrabInteractable noteGrab, flashGrab;
    Light phoneLight, noteGlow, flashBeam, marleyLight, cellarStrip, closetLight, robTorch;
    Transform lever;
    readonly List<Light> houseLights = new List<Light>();
    readonly List<Light> treeLights = new List<Light>();
    readonly List<GameObject> windowParts = new List<GameObject>();
    AudioSource clock, fridgeHum, ringing, fuseHum;
    Rigidbody noteBody;

    bool Online => session != null && session.Connected && !string.IsNullOrEmpty(session.Code);

    /// <summary>Tekst over de lopende stemming, voor de testbesturing in de Editor (nooit in de bril).</summary>
    public string ActiveVoteText
    {
        get
        {
            var v = activeVote;
            if (v == null || !Options.TryGetValue(v.id, out var o)) return "";
            return $"{v.id}  (1 = {o[0]}, 2 = {o[1]}){(v.forced != "" ? "  gekozen: " + v.forced : "")}";
        }
    }

    IEnumerator Start()
    {
        if (!autoStart) yield break;
        StorySounds.SetOverrides(soundOverrides);
        Names = objects;
        yield return null; // eerst VRDoorSetup en de XR-rig
        DesktopTestRig.Ensure(this, desktopTest);
        if (snowAroundHouse)
        {
            var cam = Story.Head;
            HomeSnow.Create(transform, FindHouseRoot(Named(Names.tree, cam != null ? cam.position : Vector3.zero)), cam != null ? cam.position : Vector3.zero);
        }
        float t0 = Time.time;
        while ((head = Story.Head) == null && Time.time - t0 < 10f) yield return null;
        if (head == null)
        {
            Debug.LogError("[Home Together] Geen Main Camera gevonden: de game loop start niet.");
            yield break;
        }
        yield return new WaitForSeconds(startDelay);
        Setup();
        yield return RunStory();
    }

    string loggedStep = "";
    Transform originT;
    Vector3 lastSafe;
    bool guardReady;

    /// <summary>
    /// Rand van de wereld: staat er geen grond (collider) meer onder de speler, dan blijft hij op de laatste plek
    /// met grond staan, alsof er een onzichtbare rand is. Zo valt niemand door de map. Dit is geen teleport:
    /// de speler wordt alleen tegengehouden, zoals een muur doet.
    /// </summary>
    void LateUpdate()
    {
        if (!guardReady || originT == null || DesktopTestRig.Noclip) return;
        bool ground = Physics.Raycast(originT.position + Vector3.up * 0.5f, Vector3.down, 2.5f, ~0, QueryTriggerInteraction.Ignore);
        if (ground) { lastSafe = originT.position; return; }
        var cc = originT.GetComponent<CharacterController>();
        bool was = cc != null && cc.enabled;
        if (was) cc.enabled = false;
        originT.position = lastSafe;
        if (was) cc.enabled = true;
    }

    void Update()
    {
        // De stap staat alleen in de Console (en de Inspector), nooit in beeld.
        if (step != loggedStep) { loggedStep = step; if (step != "") Debug.Log("[Home Together] Stap: " + step); }
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return;
        if (activeVote != null && Options.TryGetValue(activeVote.id, out var opts))
        {
            if (kb.digit1Key.wasPressedThisFrame) activeVote.forced = opts[0];
            if (kb.digit2Key.wasPressedThisFrame) activeVote.forced = opts[1];
        }
        if (kb.nKey.wasPressedThisFrame) debugSkip = true;
        if (kb.jKey.wasPressedThisFrame) debugTogether = true;
#endif
    }

    // ---------- Opbouw ----------

    void Setup()
    {
        session = FindFirstObjectByType<SessionClient>();
        Debug.Log("[Home Together] Start game loop. " + (Online ? "Online, sessie " + session.Code : "Offline: Kevin beslist zelf (1/2 = keuze forceren in de Editor)."));
        P = ComputePoints(head.position, head.forward, Story.FloorY(head));
        var xrOrigin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        originT = xrOrigin != null ? xrOrigin.transform : null;
        if (originT != null) { lastSafe = originT.position; guardReady = true; }

        // Achterdeur en kelderdeur: de VR-deur het dichtst bij de marker.
        var backDoorObj = Named(Names.backDoor, P.tree);
        backDoor = backDoorObj != null ? Story.MakeDoor(backDoorObj) : NearestDoor(P.backDoor, 6f, null);
        Vector3 hinge = backDoorObj != null ? Story.Center(backDoorObj) : backDoor != null ? backDoor.transform.position : P.backDoor;
        doorOut = Story.FlatDir(hinge, P.backDoor, Story.FlatDir(P.tree, hinge, -P.right));
        if (backDoor == null) doorOut = Story.FlatDir(P.tree, P.backDoor, -P.right);
        doorInside = Floor(hinge - doorOut * 1.0f, 0f);
        doorOutside = Floor(hinge + doorOut * 1.3f, 0f);
        if (backDoor != null) backDoor.Locked = true;

        var cellarObj = Named(Names.cellarDoor, P.tree);
        cellarDoor = cellarObj != null ? Story.MakeDoor(cellarObj) : NearestDoor(P.cellarDoor, 2.5f, backDoor);
        if (cellarDoor == backDoor) cellarDoor = null;
        if (cellarDoor != null) cellarDoor.Locked = true;

        // De inbreker staat nog niet klaar: zijn eigen loopscript gaat uit, wij sturen hem.
        rob = Named(Names.burglar, P.tree);
        if (rob != null)
        {
            var walker = rob.GetComponent<RobWalkToTrigger>();
            if (walker != null) walker.enabled = false;
            robAnim = rob.GetComponentInChildren<Animator>();
            rob.SetActive(false);
        }

        dog = Named(Names.dog, P.tree);
        if (dog == null) dog = Story.FindContains("dog", "hotdog");
        if (dog != null)
        {
            dogAnim = dog.GetComponentInChildren<Animator>();
            dog.SetActive(false);
        }

        SetupNote();
        SetupPhone();
        SetupFlashlight();
        SetupFuseBox();

        closet = Named(Names.closet, P.tree);
        SetupClosetDoors();
        closetLight = Story.MakeLight("Kastlampje", P.closet + Vector3.up * 0.6f, LightType.Point, new Color(1f, 0.75f, 0.45f), 0f, 2.5f, transform);
        marleyLight = Story.MakeLight("Licht bij Marley", P.marley + Vector3.up * 3f, LightType.Point, new Color(1f, 0.85f, 0.6f), 0f, 25f, transform);
        cellarStrip = Story.MakeLight("Lichtstreep kelder", Floor(P.cellarDoor, 0.05f), LightType.Point, new Color(1f, 0.55f, 0.2f), 0f, 1.5f, transform);

        // Tuinraam: de delen in de buurt van de marker.
        var namedWin = Named(Names.gardenWindow, P.gardenWindow);
        if (namedWin != null)
        {
            foreach (var t in namedWin.GetComponentsInChildren<Transform>(true)) windowParts.Add(t.gameObject);
        }
        var win = namedWin != null ? null : Story.FindNearest("Window", P.gardenWindow, 2.5f);
        if (win != null)
        {
            Transform root = win.transform;
            while (root.parent != null && Vector3.Distance(Story.Center(root.parent.gameObject), P.gardenWindow) < 1.2f
                   && Story.Bounds(root.parent.gameObject).size.magnitude < 4f)
                root = root.parent;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0 || t.name.IndexOf("WindowA", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.name.IndexOf("WindowB", StringComparison.OrdinalIgnoreCase) >= 0)
                    windowParts.Add(t.gameObject);
        }

        // Lichten in huis (de boom gaat als laatste uit). Alleen lampen van het huis zelf, niet de straatlantaarns.
        Transform houseRoot = FindHouseRoot(Named(Names.tree, P.tree));
        Bounds houseBounds = houseRoot != null ? Story.Bounds(houseRoot.gameObject) : new Bounds(P.tree, Vector3.zero);
        houseBounds.Expand(0.5f);
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (!l.enabled || l.type == LightType.Directional || l.transform.IsChildOf(transform) || l == flashBeam) continue;
            if (houseRoot != null ? !(l.transform.IsChildOf(houseRoot) || houseBounds.Contains(l.transform.position))
                                  : Story.FlatDist(l.transform.position, P.tree) > 18f) continue;
            if (flashlight != null && l.transform.IsChildOf(flashlight.transform)) continue;
            if (Story.FlatDist(l.transform.position, P.tree) < 1.5f) treeLights.Add(l); else houseLights.Add(l);
        }

        clock = StorySounds.Play("tick", Floor(P.start - P.right * 2f, 1.8f), 0.35f, true);
        fridgeHum = StorySounds.Play("hum", P.note, 0.3f, true);
    }

    Vector3 Floor(Vector3 v, float h) => new Vector3(v.x, P.floorY + h, v.z);

    /// <summary>Het huis van Kevin: de bovenste parent van de kerstboom met "House" in de naam.</summary>
    public static Transform FindHouseRoot(GameObject treeObj)
    {
        Transform root = null;
        for (var t = treeObj != null ? treeObj.transform : null; t != null; t = t.parent)
            if (t.name.IndexOf("House", StringComparison.OrdinalIgnoreCase) >= 0) root = t;
        return root;
    }

    VRDoor NearestDoor(Vector3 near, float max, VRDoor except)
    {
        VRDoor best = null;
        float bestD = max;
        foreach (var d in FindObjectsByType<VRDoor>(FindObjectsSortMode.None))
        {
            if (d == except) continue;
            float dist = Vector3.Distance(d.transform.position, near);
            if (dist < bestD) { bestD = dist; best = d; }
        }
        return best;
    }

    void SetupNote()
    {
        note = Named(Names.note, P.note);
        if (note == null)
        {
            var sticky = FindFirstObjectByType<StickyNote>(FindObjectsInactive.Include);
            note = sticky != null ? sticky.gameObject : null;
        }
        noteGlow = Story.MakeLight("Gloed briefje", P.note + Story.FlatDir(P.note, P.start, Vector3.zero) * 0.25f, LightType.Point, new Color(1f, 0.9f, 0.4f), 0f, 1.2f, transform);
        if (note == null)
        {
            Debug.LogWarning("[Home Together] Geen StickyNote gevonden: Kevin 'leest' het briefje door ernaar te kijken op Story_Briefje.");
            return;
        }
        noteBody = note.GetComponent<Rigidbody>();
        if (noteBody == null) noteBody = note.AddComponent<Rigidbody>();
        noteBody.isKinematic = true;
        noteBody.useGravity = false;
        noteGrab = Story.AddInteractable<XRGrabInteractable>(note);
        noteGrab.throwOnDetach = false;
        // Losgelaten = neergelegd: het briefje valt op het aanrecht of de vloer.
        noteGrab.selectExited.AddListener(_ => StartCoroutine(DropNote()));
    }

    IEnumerator DropNote()
    {
        yield return null;
        if (noteBody != null && noteGrab != null && !noteGrab.isSelected)
        {
            noteBody.isKinematic = false;
            noteBody.useGravity = true;
        }
    }

    void SetupPhone()
    {
        phone = Named(Names.phone, P.phone) ?? FindPhone();
        if (phone == null)
        {
            phone = Story.Block("Telefoon (story)", PrimitiveType.Cube, P.phone, new Vector3(0.18f, 0.26f, 0.09f), new Color(0.2f, 0.05f, 0.05f), transform);
            Story.Block("Hoorn", PrimitiveType.Capsule, P.phone + Vector3.up * 0.05f + Story.FlatDir(P.phone, P.start, Vector3.forward) * 0.06f,
                new Vector3(0.05f, 0.11f, 0.05f), new Color(0.1f, 0.1f, 0.1f), phone.transform, false);
        }
        phoneTouch = Story.AddInteractable<XRSimpleInteractable>(phone);
        phoneLight = Story.MakeLight("Rood lampje telefoon", P.phone + Story.FlatDir(P.phone, P.start, Vector3.zero) * 0.15f, LightType.Point, Color.red, 0f, 1.5f, transform);
    }

    void SetupFlashlight()
    {
        flashlight = Named(Names.flashlight, P.flashlight) ?? FindFlashlight();
        if (flashlight == null)
        {
            flashlight = Story.Block("Flashlight (story)", PrimitiveType.Cylinder, P.flashlight, new Vector3(0.05f, 0.1f, 0.05f), new Color(0.15f, 0.15f, 0.2f), transform);
            flashlight.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var rb = flashlight.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var beam = Story.MakeLight("Spot Light", flashlight.transform.position, LightType.Spot, new Color(1f, 0.95f, 0.8f), 4f, 15f, flashlight.transform);
            beam.transform.rotation = Quaternion.LookRotation(flashlight.transform.up);
            beam.spotAngle = 40f;
        }
        flashGrab = Story.AddInteractable<XRGrabInteractable>(flashlight);
        flashBeam = flashlight.GetComponentInChildren<Light>(true);
        if (flashBeam != null) flashBeam.enabled = false;
        if (flashBeam != null)
        {
            // Greep zo leggen dat de straal naar voren wijst: in de hand (en met de testhand) schijnt hij waar je naar wijst.
            Vector3 beamFwd = flashBeam.transform.forward;
            Vector3 up = Mathf.Abs(Vector3.Dot(beamFwd, flashlight.transform.up)) > 0.9f ? flashlight.transform.forward : flashlight.transform.up;
            var grip = new GameObject("Greep (straal vooruit)").transform;
            grip.SetParent(flashlight.transform, false);
            grip.SetPositionAndRotation(flashlight.transform.position, Quaternion.LookRotation(beamFwd, up));
            flashGrab.attachTransform = grip;
            flashGrab.useDynamicAttach = false;
        }
        flashGrab.enabled = false; // op slot tot het publiek "zaklamp" kiest
    }

    void SetupFuseBox()
    {
        Vector3 facing = Story.FlatDir(P.fuseBox, P.cellarDoor, P.forward);
        fuseBox = Named(Names.fuseBox, P.fuseBox);
        float depth = 0.1f;
        if (fuseBox != null)
        {
            // Bestaande kast (Shield Metall): de hendel komt op de voorkant, aan de kant van de kelderdeur.
            var b = Story.Bounds(fuseBox);
            depth = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(facing.x), 0f, Mathf.Abs(facing.z)))) + 0.03f;
        }
        else
        {
            fuseBox = Story.Block("Zekeringkast (story)", PrimitiveType.Cube, P.fuseBox, new Vector3(0.4f, 0.5f, 0.12f), new Color(0.45f, 0.47f, 0.5f), transform);
            fuseBox.transform.rotation = Quaternion.LookRotation(facing);
        }
        var leverGo = Story.Block("Hendel", PrimitiveType.Cube, P.fuseBox + facing * depth + Vector3.up * 0.1f, new Vector3(0.04f, 0.2f, 0.04f), new Color(0.8f, 0.1f, 0.1f), transform);
        lever = leverGo.transform;
        leverTouch = Story.AddInteractable<XRSimpleInteractable>(leverGo);
        leverTouch.enabled = false;
    }

    // ---------- Hulpjes voor wachten en kijken ----------

    bool Near(Vector3 p, float r) => Story.FlatDist(head.position, p) < r;
    bool Looking(Vector3 p, float angle) => Vector3.Angle(head.forward, p - head.position) < angle;
    bool InKitchen() => Near(P.note, 2.3f);
    bool InHall() => Near(doorInside, 3f) || Near(P.attic, 2f);
    bool InLiving() => Near(P.tree, 3.5f);

    bool Dwell(string key, bool condition, float seconds)
    {
        float v = condition ? (dwell.TryGetValue(key, out var x) ? x : 0f) + Time.deltaTime : 0f;
        dwell[key] = v;
        return v >= seconds;
    }

    bool Skip()
    {
        if (!debugSkip) return false;
        debugSkip = false;
        return true;
    }

    IEnumerator WaitFor(Func<bool> condition, float timeout = float.PositiveInfinity)
    {
        float end = Time.time + timeout;
        while (!condition() && Time.time < end && !Skip()) yield return null;
    }

    bool PhonePicked() => (phoneTouch != null && phoneTouch.isSelected) || Dwell("phone", Near(P.phone, 0.9f) && Looking(P.phone, 30f), 1f);
    bool FlashTaken() => (flashGrab != null && flashGrab.isSelected) || Dwell("flash", Near(P.flashlight, 0.8f) && Looking(P.flashlight, 30f), 1.2f);
    bool LeverPulled() => (leverTouch != null && leverTouch.isSelected) || Dwell("lever", Near(P.fuseBox, 0.9f) && Looking(P.fuseBox, 30f), 1.2f);
    bool InCloset()
    {
        if (Near(P.closet, closetRadius)) return true;
        if (closet == null) return false;
        var b = Story.Bounds(closet);
        b.Expand(new Vector3(0.3f, 10f, 0.3f));
        return b.Contains(head.position);
    }

    /// <summary>Een deur zonder klink kan Kevin niet vastpakken: die gaat open zodra hij ervoor staat (als hij niet op slot is).</summary>
    void OpenIfNoHandle(VRDoor door, Vector3 at, float angle)
    {
        if (door != null && !door.Locked && !door.HasHandles && Mathf.Abs(door.OpenAngle) < 5f && Near(at, 1.2f))
            door.SwingTo(angle, 90f);
    }

    bool OutsideWindow()
    {
        Vector3 outDir = Story.FlatDir(P.tree, P.gardenWindow, P.forward);
        return Vector3.Dot(Story.Flat(head.position - P.gardenWindow), outDir) > 0.4f;
    }

    // ---------- Stemmen ----------

    IEnumerator Vote(VoteRun run, int seconds, string message = "")
    {
        activeVote = run;
        if (Online)
        {
            VoteResult res = null;
            bool finished = false, stopSent = false;
            StartCoroutine(session.RunVote(run.id, seconds, r => { res = r; finished = true; }, message));
            while (!finished)
            {
                if ((run.stop || run.forced != "") && !stopSent)
                {
                    stopSent = true;
                    StartCoroutine(session.StopVote());
                }
                yield return null;
            }
            if (res != null)
            {
                run.online = true;
                run.voteId = res.voteId;
                run.winner = res.winner ?? "";
            }
        }
        else
        {
            float end = Time.time + seconds;
            while (Time.time < end && !run.stop && run.forced == "") yield return null;
        }
        if (run.forced != "") run.winner = run.forced;
        Debug.Log($"[Home Together] Stemming {run.id}: {(run.winner == "" ? "gelijkspel / geen stemmen, Kevin beslist" : run.winner)}");
        run.done = true;
        if (activeVote == run) activeVote = null;
    }

    /// <summary>Bij gelijkspel telt wat Kevin doet. Zo kloppen de keuzes op de telefoons aan het einde.</summary>
    void KevinDecides(VoteRun run, string option)
    {
        if (run.winner == "" && run.online && run.voteId > 0) StartCoroutine(session.Decide(run.voteId, option));
        run.winner = option;
    }

    IEnumerator Together(string label, Action<bool> done)
    {
        bool ok = false;
        for (int attempt = 0; attempt < 2 && !ok; attempt++)
        {
            debugTogether = false;
            if (Online)
            {
                var before = session.LastTogether;
                yield return session.StartTogether(label, togetherSeconds);
                float end = Time.time + togetherSeconds + 4f;
                while (session.LastTogether == before && Time.time < end && !debugTogether) yield return null;
                ok = debugTogether || (session.LastTogether != null && session.LastTogether != before && session.LastTogether.success);
            }
            else
            {
                float end = Time.time + togetherSeconds;
                while (Time.time < end && !debugTogether) yield return null;
                ok = debugTogether;
            }
            Debug.Log($"[Home Together] Samen-actie poging {attempt + 1}: {(ok ? "gelukt" : "mislukt")}");
            if (!ok && attempt == 0) yield return new WaitForSeconds(1.5f);
        }
        done(ok);
    }

    // ---------- Het verhaal ----------

    IEnumerator RunStory()
    {
        yield return Step1_HomeAlone();
        yield return Step2_TheNote();
        yield return Step3_ThePhone();
        yield return Step4And5_AtticAndDog();
        var power = new VoteRun { id = "stroom" };
        yield return Step6_PowerOut(power);
        yield return Step7_CellarOrFlashlight(power);
        yield return Step8_SomeoneComesIn();
        bool success = false;
        if (hidden) yield return Step9a_Closet(ok => success = ok);
        else yield return Step9b_Snow(ok => success = ok);
        yield return Step10_Ending(success);
    }

    // 1. Alleen thuis: na 15 s rijdt het busje voorbij, koplampen over het plafond.
    IEnumerator Step1_HomeAlone()
    {
        step = "1 Alleen thuis";
        yield return WaitFor(() => false, 15f);
        Vector3 a = P.street - P.right * 20f, b = P.street;
        var engine = StorySounds.Play("engine", a, 0.9f, true);
        var beam = Story.MakeLight("Koplampen busje", a + Vector3.up * 1f, LightType.Spot, new Color(1f, 0.95f, 0.8f), 30f, 40f, transform);
        beam.spotAngle = 50f;
        for (float t = 0f; t < 6f; t += Time.deltaTime)
        {
            Vector3 pos = Vector3.Lerp(a, b, Mathf.SmoothStep(0f, 1f, t / 6f)) + Vector3.up * 1f;
            engine.transform.position = pos;
            beam.transform.position = pos;
            beam.transform.rotation = Quaternion.LookRotation(Floor(P.start, 2.6f) - pos);
            yield return null;
        }
        StorySounds.Stop(engine);
        beam.enabled = false;
        yield return new WaitForSeconds(0.8f);
        StorySounds.Play("slam", P.street, 0.7f, false, null, 1.3f); // portier
        StartCoroutine(Pulse(noteGlow, 0.2f, 1.0f, () => step.StartsWith("1") || step.StartsWith("2")));
    }

    // 2. Het briefje: de keuken lokt (gloed, gezoem, magneetje). Kerstbal rolt naar de telefoon.
    IEnumerator Step2_TheNote()
    {
        step = "2 Het briefje";
        float next = Time.time + 20f;
        while (!InKitchen() && !Skip())
        {
            if (Time.time > next) { StorySounds.Play("magnet", P.note, 0.8f); next = Time.time + 20f; }
            yield return null;
        }
        yield return new WaitForSeconds(2f);
        StartCoroutine(RollBauble());
    }

    IEnumerator RollBauble()
    {
        Vector3 from = Floor(P.tree, 0.06f), to = Floor(P.phone, 0.06f);
        var ball = Story.Block("Kerstbal", PrimitiveType.Sphere, from, Vector3.one * 0.08f, new Color(0.8f, 0.05f, 0.05f), transform, false);
        var snd = StorySounds.Play("roll", from, 0.8f);
        for (float t = 0f; t < 1.6f; t += Time.deltaTime)
        {
            ball.transform.position = Vector3.Lerp(from, to, 1f - Mathf.Pow(1f - t / 1.6f, 2f));
            if (snd != null) snd.transform.position = ball.transform.position;
            yield return null;
        }
    }

    // 3. De telefoon: stemming loopt zolang Kevin het briefje leest. Legt hij het neer, dan rinkelt de telefoon.
    IEnumerator Step3_ThePhone()
    {
        step = "3 De telefoon (Kevin leest)";
        // Wachten tot Kevin het briefje oppakt of ervoor staat te lezen. Na 40 s start de stemming toch.
        float giveUp = Time.time + 40f;
        yield return WaitFor(() => (noteGrab != null && noteGrab.isSelected)
                                   || Dwell("read", Near(P.note, 1.4f) && Looking(P.note, 30f), 0.8f)
                                   || Time.time > giveUp);

        var vote = new VoteRun { id = "telefoon" };
        StartCoroutine(Vote(vote, voteSeconds));
        bool everGrabbed = false, everRead = false;
        float lookAway = 0f, readStart = Time.time;
        while (!vote.done && Time.time - readStart < voteSeconds)
        {
            bool grabbed = noteGrab != null && noteGrab.isSelected;
            everGrabbed |= grabbed;
            bool reading = grabbed || (Near(P.note, 1.8f) && Looking(P.note, 40f));
            everRead |= reading;
            lookAway = reading ? 0f : lookAway + Time.deltaTime;
            // Briefje neergelegd (of, zonder vastpakken, weggekeken). Het publiek krijgt altijd minstens minVoteSeconds.
            // Las Kevin het briefje nooit (na 40 s gestart), dan loopt de stemming gewoon helemaal.
            bool putDown = everGrabbed ? !grabbed : everRead && lookAway > 1.5f;
            if (putDown && Time.time - readStart >= minVoteSeconds) break;
            yield return null;
        }
        vote.stop = true;
        yield return new WaitUntil(() => vote.done);

        step = "3 De telefoon rinkelt";
        StartRinging();
        if (vote.winner == "rinkelen")
        {
            // Net voor Kevin de hoorn kan pakken stopt het rinkelen. Gefluister buiten bij de garage.
            yield return WaitFor(() => Near(P.phone, 1.2f));
            StopRinging();
            yield return new WaitForSeconds(1.2f);
            StorySounds.Play("whisper", doorOutside + Vector3.up * 1.6f, 0.9f);
            burglarsFast = true;
        }
        else
        {
            // Opnemen (of gelijkspel: blijft rinkelen, Kevin beslist). Neemt hij na 25 s bij de telefoon niet op: laten rinkelen.
            float atPhoneSince = -1f;
            bool picked = false;
            while (!Skip())
            {
                if (PhonePicked()) { picked = true; break; }
                if (Near(P.phone, 1.5f)) { if (atPhoneSince < 0f) atPhoneSince = Time.time; }
                if (vote.winner == "" && atPhoneSince > 0f && Time.time - atPhoneSince > 25f) break;
                yield return null;
            }
            if (vote.winner == "") KevinDecides(vote, picked || vote.winner == "opnemen" ? "opnemen" : "rinkelen");
            StopRinging();
            // Kevin nam niet op (gelijkspel): het antwoordapparaat speelt Marleys bericht, als die opname er is.
            if (!picked && Resources.Load<AudioClip>("HomeTogetherSounds/voicemail") != null)
                StorySounds.Play("voicemail", P.phone, 0.9f);
            if (vote.winner == "opnemen" || picked)
            {
                // Marley aan de lijn, dicht bij zijn linkeroor. Achter hem flitst Marleys licht aan en uit.
                StorySounds.Play("marley", head.position - head.right * 0.15f, 1f, false, head);
                marleyKnows = true;
                // Het licht flitst bij "I'll turn my light on", aan het einde van de zin.
                var marleyClip = StorySounds.Get("marley");
                yield return new WaitForSeconds(marleyClip != null ? Mathf.Max(2.5f, marleyClip.length - 1.5f) : 2.5f);
                for (int i = 0; i < 3; i++)
                {
                    marleyLight.intensity = 6f; yield return new WaitForSeconds(0.4f);
                    marleyLight.intensity = 0f; yield return new WaitForSeconds(0.4f);
                }
                marleyLight.intensity = 2f; // blijft zacht aan
            }
            else burglarsFast = true;
        }
        choices.Add("telefoon=" + vote.winner);
    }

    void StartRinging()
    {
        ringing = StorySounds.Play("ring", P.phone, 0.4f, true);
        StartCoroutine(RingLouder());
    }

    IEnumerator RingLouder()
    {
        float t0 = Time.time;
        while (ringing != null)
        {
            float t = Time.time - t0;
            ringing.volume = Mathf.Min(1f, 0.4f + t * 0.04f);
            phoneLight.intensity = (t % 3f) < 1f ? 3f : 0f; // knippert bij elke rinkel
            yield return null;
        }
        phoneLight.intensity = 0f;
    }

    void StopRinging()
    {
        StorySounds.Stop(ringing);
        ringing = null;
    }

    // 4. Iets op zolder (stemming hond loopt mee) en 5. De hond aan de achterdeur.
    IEnumerator Step4And5_AtticAndDog()
    {
        step = "4 Iets op zolder";
        yield return WaitFor(InHall, 15f);
        var vote = new VoteRun { id = "hond" };
        StartCoroutine(Vote(vote, voteSeconds));
        for (int i = 0; i < 3; i++) // drie trage stappen boven zijn hoofd, van voor naar achter
        {
            StorySounds.Play("creak", head.position + Vector3.up * 1.3f + head.forward * (1.5f - i * 1.5f), 0.9f);
            yield return new WaitForSeconds(1.1f);
        }
        StartCoroutine(Dust(P.attic));
        yield return new WaitForSeconds(1.5f);
        StorySounds.Play("roll", P.attic + Vector3.up * 0.3f, 0.7f, false, null, 0.6f);
        yield return new WaitUntil(() => vote.done);

        step = "5 De hond";
        if (dog != null)
        {
            dog.SetActive(true);
            dog.transform.position = Floor(doorOutside, 0f);
            Story.Face(dog.transform, doorInside);
            if (dogAnim != null) dogAnim.speed = 0f;
        }
        var scratch = StorySounds.Play("scratch", Floor(doorOutside, 0.4f), 0.4f, true);
        float t0 = Time.time, nextWhine = Time.time + 6f, nextBark = Time.time + 18f;
        yield return WaitFor(() =>
        {
            float t = Time.time - t0;
            if (scratch != null) scratch.volume = Mathf.Min(1f, 0.4f + t * 0.03f);
            if (Time.time > nextWhine) { StorySounds.Play("whine", Floor(doorOutside, 0.5f), 0.8f); nextWhine = Time.time + 8f; }
            if (Time.time > nextBark) { StorySounds.Play("bark", Floor(doorOutside, 0.5f), 1f); nextBark = Time.time + 5f; }
            return Near(doorInside, 1.4f);
        });

        if (vote.winner == "binnen")
        {
            // De klink draait vanzelf, de deur zwaait open, de hond rent tussen zijn benen door naar de boom.
            StorySounds.Stop(scratch);
            OpenBackDoor(120f);
            var wind = StorySounds.Play("wind", Floor(doorOutside, 1.2f), 0.8f, true);
            yield return new WaitForSeconds(0.8f);
            if (dog != null)
            {
                // Rent tussen Kevins benen door: naar net voorbij Kevin (niet recht op hem in), dan naar de boom.
                Vector3 past = Floor(doorInside - doorOut * 1.2f, 0f); // recht door de open deur, langs Kevin
                yield return Story.Walk(dog.transform, past, 3.5f, dogAnim, -1f);
                yield return Story.Walk(dog.transform, Floor(P.tree - Story.FlatDir(P.start, P.tree, P.forward) * 0.6f, 0f), 3f, dogAnim, 0.2f, 2.5f);
            }
            dogInside = true;
            yield return new WaitForSeconds(2f);
            CloseBackDoor();
            StorySounds.Stop(wind);
        }
        else
        {
            // Laten (of gelijkspel): Kevin opent zelf. De hond gromt en rent weg. Daarna zware stappen in de sneeuw.
            if (backDoor != null) backDoor.Locked = false;
            yield return WaitFor(() =>
            {
                OpenIfNoHandle(backDoor, doorInside, backDoorOpenAngle);
                return backDoor == null ? Near(doorInside, 0.7f) : Mathf.Abs(backDoor.OpenAngle) > 25f;
            }, 25f);
            if (vote.winner == "") KevinDecides(vote, "buiten");
            StorySounds.Stop(scratch);
            StorySounds.Play("growl", Floor(doorOutside, 0.4f), 0.9f);
            yield return new WaitForSeconds(1f);
            if (dog != null)
            {
                Vector3 away = doorOutside + doorOut * 15f + Vector3.Cross(Vector3.up, doorOut) * 6f;
                yield return Story.Walk(dog.transform, away, 4f, dogAnim);
                dog.SetActive(false);
            }
            yield return WaitFor(() => backDoor == null || Mathf.Abs(backDoor.OpenAngle) < 10f, 8f);
            CloseBackDoor();
            yield return new WaitForSeconds(3f);
            StartCoroutine(Footsteps("snowstep", doorOutside + Vector3.Cross(Vector3.up, doorOut) * 4f,
                doorOutside - Vector3.Cross(Vector3.up, doorOut) * 4f, 8, 0.55f, 1f));
        }
        choices.Add("hond=" + vote.winner);
    }

    void OpenBackDoor(float speed)
    {
        if (backDoor == null) return;
        backDoor.Locked = true; // gaat vanzelf open, Kevin hoeft niets te doen
        backDoor.SwingTo(backDoorOpenAngle, speed);
    }

    void CloseBackDoor()
    {
        if (backDoor == null) return;
        backDoor.SwingTo(0f, 90f);
        backDoor.Locked = true;
    }

    IEnumerator Dust(Vector3 from)
    {
        for (int i = 0; i < 14; i++)
        {
            var speck = Story.Block("Stof", PrimitiveType.Cube, from + new Vector3(UnityEngine.Random.Range(-0.25f, 0.25f), 0f, UnityEngine.Random.Range(-0.25f, 0.25f)),
                Vector3.one * 0.015f, new Color(0.8f, 0.78f, 0.7f), transform, false);
            StartCoroutine(Fall(speck.transform, UnityEngine.Random.Range(0.3f, 0.6f)));
            yield return new WaitForSeconds(0.12f);
        }
    }

    IEnumerator Fall(Transform t, float speed)
    {
        while (t != null && t.position.y > P.floorY)
        {
            t.position += Vector3.down * speed * Time.deltaTime;
            yield return null;
        }
        if (t != null) Destroy(t.gameObject);
    }

    Light engineRoomLight;

    /// <summary>Licht dat zacht flakkert zolang de voorwaarde geldt, daarna uit.</summary>
    IEnumerator Flicker(Light l, float intensity, Func<bool> whileTrue)
    {
        while (l != null && whileTrue())
        {
            l.intensity = intensity * (0.8f + 0.2f * Mathf.PerlinNoise(Time.time * 3f, 0.5f)) * (UnityEngine.Random.value < 0.01f ? 0.3f : 1f);
            yield return null;
        }
        if (l != null) l.intensity = 0f;
    }

    /// <summary>Is de opname een hele reeks stappen (langer dan 1,5 s) in plaats van één stap?</summary>
    static bool IsLongRecording(string sound)
    {
        var clip = StorySounds.Get(sound);
        return clip != null && clip.length > 1.5f;
    }

    IEnumerator Footsteps(string sound, Vector3 from, Vector3 to, int steps, float interval, float volume)
    {
        if (IsLongRecording(sound))
        {
            // Echte opname met een reeks stappen: één geluid dat meeschuift van 'from' naar 'to'.
            float dur = Mathf.Max(1f, steps * interval);
            var src = StorySounds.Play(sound, from, volume, true);
            for (float t = 0f; t < dur && src != null; t += Time.deltaTime)
            {
                src.transform.position = Vector3.Lerp(from, to, t / dur);
                yield return null;
            }
            StorySounds.Stop(src);
            yield break;
        }
        for (int i = 0; i < steps; i++)
        {
            StorySounds.Play(sound, Vector3.Lerp(from, to, (float)i / Mathf.Max(1, steps - 1)), volume);
            yield return new WaitForSeconds(interval);
        }
    }

    // 6. De stroom valt uit.
    IEnumerator Step6_PowerOut(VoteRun vote)
    {
        step = "6 De stroom valt uit";
        if (dogInside) StorySounds.Play("jingle", P.tree, 0.5f);
        yield return WaitFor(InLiving, 20f);
        StorySounds.Play("clunk", P.fuseBox, 1f);
        StartCoroutine(Vote(vote, voteSeconds));
        yield return Blackout();
        if (dogInside && dog != null) StorySounds.Play("whine", dog.transform.position + Vector3.up * 0.4f, 0.7f);
        yield return new WaitUntil(() => vote.done);
    }

    // Wat de stroomuitval uitzet, om het bij de hendel terug te zetten.
    readonly List<Light> darkened = new List<Light>();
    LightmapData[] savedLightmaps;
    UnityEngine.Rendering.AmbientMode savedAmbientMode;
    Color savedAmbient;
    float savedAmbientIntensity, savedReflection;

    bool IsStoryLight(Light l) =>
        l.transform.IsChildOf(transform) || (flashlight != null && l.transform.IsChildOf(flashlight.transform)) || l == flashBeam;

    IEnumerator Blackout()
    {
        lightsOn = false;
        darkened.Clear();
        // ALLE lampen gaan uit (huis, straat, maan), behalve die van het verhaal zelf (zaklamp, lichtstreep, kastlampje).
        var tree = new List<Light>();
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (!l.enabled || IsStoryLight(l)) continue;
            if (treeLights.Contains(l)) { tree.Add(l); continue; }
            l.enabled = false;
            darkened.Add(l);
        }
        // Gebakken licht (lightmaps) en omgevingslicht ook weg, anders blijft alles verlicht.
        savedLightmaps = LightmapSettings.lightmaps;
        LightmapSettings.lightmaps = new LightmapData[0];
        savedAmbientMode = RenderSettings.ambientMode;
        savedAmbient = RenderSettings.ambientLight;
        savedAmbientIntensity = RenderSettings.ambientIntensity;
        savedReflection = RenderSettings.reflectionIntensity;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.015f, 0.02f, 0.04f); // heel zwak blauw: net je handen zien
        RenderSettings.reflectionIntensity = 0.05f;
        if (fridgeHum != null) fridgeHum.Pause();
        yield return new WaitForSeconds(0.6f);
        // De boom gaat als laatste uit, lichtje per lichtje van boven naar beneden.
        tree.Sort((a, b) => b.transform.position.y.CompareTo(a.transform.position.y));
        foreach (var l in tree)
        {
            if (l != null) { l.enabled = false; darkened.Add(l); }
            yield return new WaitForSeconds(0.35f);
        }
    }

    void LightsBack()
    {
        lightsOn = true;
        foreach (var l in darkened) if (l != null) l.enabled = true;
        darkened.Clear();
        if (savedLightmaps != null) LightmapSettings.lightmaps = savedLightmaps;
        RenderSettings.ambientMode = savedAmbientMode;
        RenderSettings.ambientLight = savedAmbient;
        RenderSettings.ambientIntensity = savedAmbientIntensity;
        RenderSettings.reflectionIntensity = savedReflection;
        if (fridgeHum != null) fridgeHum.UnPause();
    }

    // 7a. De kelder of 7b. De zaklamp.
    IEnumerator Step7_CellarOrFlashlight(VoteRun vote)
    {
        bool cellarOpen = vote.winner != "zaklamp";
        bool flashOpen = vote.winner != "kelder";
        step = cellarOpen && flashOpen ? "7 Kelder of zaklamp (Kevin kiest)" : cellarOpen ? "7a De kelder" : "7b De zaklamp";

        if (cellarOpen)
        {
            cellarStrip.intensity = 3f;
            cellarStrip.range = 2.5f;
            // Klein warm licht in de kelderkamer bij de zekeringkast: het enige licht in het donkere huis, lokt Kevin naar beneden.
            if (engineRoomLight == null)
                engineRoomLight = Story.MakeLight("Licht kelderkamer", P.fuseBox + Vector3.up * 0.4f, LightType.Point, new Color(1f, 0.6f, 0.25f), 0f, 5f, transform);
            StartCoroutine(Flicker(engineRoomLight, 2.5f, () => !lightsOn));
            fuseHum = StorySounds.Play("hum", P.fuseBox, 0.5f, true, null, 1.5f);
            if (cellarDoor != null) cellarDoor.Locked = false;
            leverTouch.enabled = true;
        }
        if (flashOpen) flashGrab.enabled = true;

        string chosen = "";
        yield return WaitFor(() =>
        {
            if (cellarOpen) OpenIfNoHandle(cellarDoor, P.cellarDoor, cellarDoorOpenAngle);
            if (cellarOpen && LeverPulled()) chosen = "kelder";
            else if (flashOpen && FlashTaken()) chosen = "zaklamp";
            return chosen != "";
        });
        if (chosen == "") chosen = cellarOpen ? "kelder" : "zaklamp"; // overgeslagen met N
        if (vote.winner == "") KevinDecides(vote, chosen);
        choices.Add("stroom=" + chosen);

        if (chosen == "kelder")
        {
            // Kevin trekt zelf aan de hendel: licht aan. Boven is de kelderdeur ineens dicht.
            flashGrab.enabled = false;
            lever.localRotation = Quaternion.Euler(60f, 0f, 0f);
            StorySounds.Play("clunk", P.fuseBox, 1f);
            StorySounds.Stop(fuseHum);
            cellarStrip.intensity = 0f;
            LightsBack();
            // Dicht, maar NIET op slot: Kevin staat in de kelder en moet er zelf weer uit kunnen.
            if (cellarDoor != null) { cellarDoor.SwingTo(0f, 400f); cellarDoor.Locked = false; StorySounds.Play("slam", P.cellarDoor, 0.4f); }
            yield return new WaitForSeconds(2f);
            if (cellarDoor != null && !cellarDoor.HasHandles) StartCoroutine(KeepCellarPassable());
            if (dogInside) StorySounds.Play("bark", P.tree, 0.8f); else StorySounds.Play("creak", Floor(head.position - head.forward * 1.5f, 0f), 0.8f);
        }
        else
        {
            // Zaklamp: alleen zien waar hij op schijnt. Natte voetstappen in de gang.
            StorySounds.Stop(fuseHum);
            cellarStrip.intensity = 0f;
            if (engineRoomLight != null) engineRoomLight.enabled = false;
            leverTouch.enabled = false;
            if (flashBeam != null) flashBeam.enabled = true;
            if (!(flashGrab != null && flashGrab.isSelected))
            {
                // Geen hand (Kevin keek er alleen naar): de zaklamp schijnt mee met het hoofd.
                // Rigidbody kinematic, anders valt hij met de zwaartekracht van het hoofd weg.
                var flashBody = flashlight.GetComponent<Rigidbody>();
                if (flashBody != null) { flashBody.isKinematic = true; flashBody.useGravity = false; }
                foreach (var c in flashlight.GetComponentsInChildren<Collider>()) c.enabled = false;
                flashGrab.enabled = false;
                flashlight.transform.SetParent(head, false);
                flashlight.transform.localPosition = new Vector3(0.2f, -0.25f, 0.3f);
                flashlight.transform.localRotation = Quaternion.identity;
                if (flashBeam != null)
                    flashlight.transform.rotation = Quaternion.FromToRotation(flashBeam.transform.forward, head.forward) * flashlight.transform.rotation;
            }
            for (int i = 0; i < 6; i++)
            {
                Vector3 p = Vector3.Lerp(doorInside, P.tree, i / 8f) + Vector3.Cross(Vector3.up, doorOut) * (i % 2 == 0 ? 0.12f : -0.12f);
                Story.Block("Natte voetstap", PrimitiveType.Cube, Floor(p, 0.005f), new Vector3(0.12f, 0.005f, 0.28f), new Color(0.15f, 0.17f, 0.2f), transform, false);
            }
            yield return new WaitForSeconds(3f);
            StorySounds.Play("creak", Floor(P.tree, 0f), 0.8f);
            if (dogInside) StorySounds.Play("bark", P.tree, 0.8f);
        }
    }

    IEnumerator KeepCellarPassable()
    {
        while (!step.StartsWith("10") && step != "Einde")
        {
            OpenIfNoHandle(cellarDoor, P.cellarDoor, cellarDoorOpenAngle);
            yield return null;
        }
    }

    // 8. Iemand komt binnen.
    IEnumerator Step8_SomeoneComesIn()
    {
        step = "8 Iemand komt binnen";
        yield return WaitFor(() => InLiving() || InKitchen(), 20f);

        // BAM: de achterdeur vliegt open en de stemming start meteen.
        OpenBackDoor(500f);
        StorySounds.Play("slam", doorInside + Vector3.up * 1f, 1f);
        var vote = new VoteRun { id = "inbreker" };
        StartCoroutine(Vote(vote, burglarsFast ? fastVoteSeconds : voteSeconds, burglarsFast ? "They're faster than expected." : ""));

        if (rob != null)
        {
            rob.SetActive(true);
            rob.transform.position = Floor(doorOutside, 0f);
            Story.Face(rob.transform, doorInside);
            if (!lightsOn)
            {
                // In het donker: een vreemde lichtbundel over het plafond.
                robTorch = Story.MakeLight("Zaklamp inbreker", rob.transform.position + Vector3.up * 1.5f, LightType.Spot, Color.white, 6f, 15f, rob.transform);
                robTorch.spotAngle = 35f;
                StartCoroutine(SweepTorch());
            }
            StartCoroutine(Footsteps("step", doorOutside, doorInside, 4, 0.6f, 1f));
            yield return Story.Walk(rob.transform, doorInside, 0.9f, robAnim);
        }
        yield return new WaitUntil(() => vote.done);

        bool closetOpen = vote.winner != "raam";
        bool windowOpen = vote.winner != "kast";
        if (closetOpen)
        {
            // Kastje gaat op een kier, warm lampje achter de latten. Staat het licht aan, dan klikt het ganglicht uit.
            closetLight.intensity = 3f;
            closetLight.range = 3.5f;
            StartCoroutine(MoveClosetDoors(1f, 1.4f));
            ClosetEnterable = true;
            StorySounds.Play("creak", P.closet, 0.6f, false, null, 1.4f);
            // Kevin moet er echt in kunnen stappen (ook met de joystick): de kast houdt hem niet meer tegen.
            if (closet != null)
                foreach (var c in closet.GetComponentsInChildren<Collider>()) if (!c.isTrigger) c.enabled = false;
            if (lightsOn)
            {
                Light hallLight = null;
                float best = 5f;
                foreach (var l in houseLights)
                    if (l != null && Vector3.Distance(l.transform.position, doorInside) < best) { best = Vector3.Distance(l.transform.position, doorInside); hallLight = l; }
                if (hallLight != null) { hallLight.enabled = false; StorySounds.Play("magnet", hallLight.transform.position, 0.6f, false, null, 0.5f); }
            }
        }
        if (windowOpen) OpenWindow();

        string chosen = "";
        float nextLure = Time.time + 6f;
        yield return WaitFor(() =>
        {
            // Lokken: de kast kraakt zacht zolang Kevin er niet is (het open raam lokt al met wind).
            if (closetOpen && Time.time > nextLure) { StorySounds.Play("creak", P.closet, 0.5f, false, null, 1.4f); nextLure = Time.time + 6f; }
            if (closetOpen && InCloset()) chosen = "kast";
            else if (windowOpen && OutsideWindow()) chosen = "raam";
            return chosen != "";
        });
        if (chosen == "") chosen = closetOpen ? "kast" : "raam";
        if (vote.winner == "") KevinDecides(vote, chosen);
        ClosetEnterable = false;
        hidden = chosen == "kast";
        escaped = chosen == "raam";
        choices.Add("inbreker=" + chosen);
    }


    // ---------- Kastdeuren ----------

    readonly List<Transform> closetDoors = new List<Transform>();
    readonly List<Quaternion> closetDoorsOpen = new List<Quaternion>();
    readonly List<Quaternion> closetDoorsClosed = new List<Quaternion>();
    float closetDoorsK; // 0 = dicht, 1 = helemaal open
    Vector3 closetFront = Vector3.forward;

    [Tooltip("Hoe ver de kastdeuren openstaan als Kevin erin zit (0 = dicht, 1 = open): een kier om door te kijken.")]
    [Range(0f, 0.5f)] public float closetPeekGap = 0.12f;

    /// <summary>Kan Kevin nu in de kast stappen? (de testbesturing gebruikt dit voor E = in de kast)</summary>
    public bool ClosetEnterable { get; private set; }
    /// <summary>Plek in de kast (op de vloer) en de richting waarin de kast opent.</summary>
    public Vector3 ClosetInside => Floor(P.closet - closetFront * 0.1f, 0f);
    public Vector3 ClosetFront => closetFront;

    /// <summary>
    /// De deuren van de kast ("Left Door" / "Right Door") staan in de prefab open. Bij de start gaan ze dicht;
    /// pas als het publiek "kast" kiest (of bij gelijkspel) zwaaien ze open. Dicht = de draaiing rond hun eigen
    /// scharnieras (lokale Z, zoals in de prefab) eraf halen.
    /// </summary>
    void SetupClosetDoors()
    {
        if (closet == null) return;
        Vector3 openCenters = Vector3.zero;
        foreach (var t in closet.GetComponentsInChildren<Transform>(true))
        {
            if (t == closet.transform) continue;
            // Deur = naam met "Door", of een deel dat in de prefab flink rond zijn eigen Z-as opengedraaid staat.
            float twistDeg = 2f * Mathf.Atan2(Mathf.Abs(t.localRotation.z), Mathf.Abs(t.localRotation.w)) * Mathf.Rad2Deg;
            bool named = t.name.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!named && !(twistDeg > 20f && t.GetComponent<Renderer>() != null)) continue;
            Quaternion open = t.localRotation;
            var twist = new Quaternion(0f, 0f, open.z, open.w);
            float len = Mathf.Sqrt(twist.z * twist.z + twist.w * twist.w);
            Quaternion closed = len < 0.0001f ? open : open * Quaternion.Inverse(new Quaternion(0f, 0f, twist.z / len, twist.w / len));
            openCenters += Story.Center(t.gameObject);
            closetDoors.Add(t);
            closetDoorsOpen.Add(open);
            closetDoorsClosed.Add(closed);
            t.localRotation = closed;
        }
        var cb = Story.Bounds(closet);
        Debug.Log($"[Home Together] Kast '{closet.name}': {closetDoors.Count} deuren, midden {cb.center}, grootte {cb.size}");
        if (closetDoors.Count == 0) { Debug.LogWarning("[Home Together] Geen kastdeuren ('Door' in de naam) gevonden onder " + closet.name); return; }
        // Open deuren steken naar voren uit: daar is de voorkant van de kast.
        closetFront = Story.FlatDir(Story.Center(closet), openCenters / closetDoors.Count, Story.FlatDir(P.closet, doorInside, -P.forward));
    }

    IEnumerator MoveClosetDoors(float to, float seconds)
    {
        float from = closetDoorsK;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            SetClosetDoors(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds)));
            yield return null;
        }
        SetClosetDoors(to);
    }

    void SetClosetDoors(float k)
    {
        closetDoorsK = k;
        for (int i = 0; i < closetDoors.Count; i++)
            if (closetDoors[i] != null) closetDoors[i].localRotation = Quaternion.Slerp(closetDoorsClosed[i], closetDoorsOpen[i], k);
    }

    IEnumerator SweepTorch()
    {
        while (robTorch != null && robTorch.enabled && !step.StartsWith("9a"))
        {
            float a = Mathf.Sin(Time.time * 0.8f) * 50f;
            Vector3 dir = Quaternion.AngleAxis(a, Vector3.up) * (-doorOut) + Vector3.up * 0.6f;
            robTorch.transform.rotation = Quaternion.LookRotation(dir);
            yield return null;
        }
    }

    void OpenWindow()
    {
        // Een windvlaag gooit het tuinraam open: glas weg, wind en sneeuw naar binnen.
        StorySounds.Play("glass", P.gardenWindow, 1f);
        StorySounds.Play("wind", P.gardenWindow, 0.9f, true);
        foreach (var part in windowParts)
        {
            if (part == null) continue;
            foreach (var c in part.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            if (part.name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0) part.SetActive(false);
        }
        if (windowParts.Count == 0) Debug.LogWarning("[Home Together] Geen raam gevonden bij Story_Tuinraam: zet de marker op het tuinraam.");
    }

    // 9a. In de kast: de inbreker loopt langs en blijft staan. Dan de samen-actie.
    IEnumerator Step9a_Closet(Action<bool> done)
    {
        step = "9a In de kast";
        // Kevin staat in de kast: de deuren gaan zacht dicht tot een kier. Hij kijkt door de spleet naar de gang.
        StorySounds.Play("creak", P.closet, 0.4f, false, null, 1.6f);
        yield return MoveClosetDoors(closetPeekGap, 1.2f);
        closetLight.intensity = 0.25f; // bijna donker binnen: alleen strepen licht van buiten
        closetLight.range = 1.2f;

        Vector3 front = Floor(P.closet + closetFront * 1.1f, 0f);
        Vector3 side = Vector3.Cross(Vector3.up, closetFront);
        AudioSource breath = null;
        if (rob != null)
        {
            if (!rob.activeSelf) { rob.SetActive(true); rob.transform.position = Floor(doorInside, 0f); }
            // Zaklamp van de inbreker: die zie je door de kier over de deuren glijden.
            if (robTorch == null)
            {
                robTorch = Story.MakeLight("Zaklamp inbreker", rob.transform.position + Vector3.up * 1.5f, LightType.Spot, Color.white, 6f, 15f, rob.transform);
                robTorch.spotAngle = 35f;
            }
            yield return Story.Walk(rob.transform, front + side * 1.8f, 0.8f, robAnim); // komt van rechts
            StartCoroutine(TorchOnCloset());
            StartCoroutine(Footsteps("step", front + side * 1.8f, front, 3, 0.7f, 0.9f));
            yield return Story.Walk(rob.transform, front, 0.6f, robAnim);
            Story.Face(rob.transform, P.closet);
            breath = StorySounds.Play("breath", rob.transform.position + Vector3.up * 1.6f, 0.8f, true, rob.transform);
        }
        StartCoroutine(CreakWhenMovingTooFast());
        yield return JointAction(done);
        StorySounds.Stop(breath);
    }

    /// <summary>De inbreker schijnt langzaam heen en weer over de kastdeuren, zolang Kevin erin zit.</summary>
    IEnumerator TorchOnCloset()
    {
        Vector3 side = Vector3.Cross(Vector3.up, closetFront);
        while (robTorch != null && robTorch.enabled && step.StartsWith("9a"))
        {
            Vector3 target = P.closet + side * Mathf.Sin(Time.time * 0.9f) * 0.6f + Vector3.up * (0.2f + 0.2f * Mathf.Sin(Time.time * 0.5f));
            robTorch.transform.rotation = Quaternion.Slerp(robTorch.transform.rotation, Quaternion.LookRotation(target - robTorch.transform.position), Time.deltaTime * 4f);
            yield return null;
        }
    }

    IEnumerator CreakWhenMovingTooFast()
    {
        Quaternion last = head.rotation;
        float cooldown = 0f;
        while (step.StartsWith("9a"))
        {
            float speed = Quaternion.Angle(last, head.rotation) / Mathf.Max(Time.deltaTime, 0.001f);
            last = head.rotation;
            cooldown -= Time.deltaTime;
            if (speed > 150f && cooldown <= 0f) { StorySounds.Play("creak", P.closet, 0.5f, false, null, 1.6f); cooldown = 1.5f; }
            yield return null;
        }
    }

    // 9b. Door de sneeuw: hekje kraakt achter hem, voetstappen volgen hem. Dan de samen-actie.
    IEnumerator Step9b_Snow(Action<bool> done)
    {
        step = "9b Door de sneeuw";
        if (rob != null) rob.SetActive(false);
        yield return new WaitForSeconds(1f);
        StorySounds.Play("gate", Floor(head.position - head.forward * 7f, 1f), 1f);
        StartCoroutine(Follower());
        yield return new WaitForSeconds(4f);
        StorySounds.Play("snowslide", Floor(P.gardenWindow, 3f), 0.9f);
        yield return new WaitForSeconds(2f);
        yield return JointAction(done);
    }

    IEnumerator Follower()
    {
        Vector3 lastPos = head.position;
        bool continuous = IsLongRecording("snowstep");
        AudioSource src = continuous ? StorySounds.Play("snowstep", Floor(head.position, 0f), 0.9f, true) : null;
        while (step.StartsWith("9b"))
        {
            float moved = Story.FlatDist(head.position, lastPos);
            lastPos = head.position;
            Vector3 toMarley = Story.FlatDir(head.position, P.marley, P.forward);
            Vector3 behind = Floor(head.position - toMarley * 5f, 0f);
            if (continuous)
            {
                // Eén doorlopende opname achter Kevin; sneller (hogere toon) als hij stilstaat.
                if (src != null) { src.transform.position = behind; src.pitch = moved < 0.1f ? 1.3f : 1f; }
                yield return new WaitForSeconds(0.3f);
                continue;
            }
            StorySounds.Play("snowstep", behind, 0.9f);
            yield return new WaitForSeconds(moved < 0.2f ? 0.35f : 0.6f); // sneller als Kevin stilstaat
        }
        StorySounds.Stop(src);
    }

    IEnumerator JointAction(Action<bool> done)
    {
        string label;
        bool strong;
        if (dogInside) { label = "Make the dog bark"; strong = true; }
        else if (marleyKnows) { label = "Make Marley turn on the light"; strong = true; }
        else { label = "Make the phone ring"; strong = false; }

        bool success = false;
        yield return Together(label, ok => success = ok);
        if (success)
        {
            if (dogInside && dog != null)
            {
                for (int i = 0; i < 4; i++) { StorySounds.Play("bark", dog.transform.position + Vector3.up * 0.4f, 1f); yield return new WaitForSeconds(0.45f); }
                StartCoroutine(Story.Walk(dog.transform, Floor(doorInside, 0f), 4f, dogAnim));
            }
            else if (marleyKnows)
            {
                marleyLight.intensity = 12f;
                Story.MakeLight("Buitenlamp Marley", Floor(Vector3.Lerp(P.marley, P.gardenWindow, 0.5f), 4f), LightType.Point, new Color(1f, 0.9f, 0.7f), 8f, 30f, transform);
            }
            else
            {
                StartRinging();
                yield return new WaitForSeconds(4f);
                StopRinging();
            }
        }
        if (rob != null && rob.activeSelf) Story.Face(rob.transform, dogInside && dog != null ? dog.transform.position : P.marley);
        done(success && strong);
    }

    // 10. Het einde.
    IEnumerator Step10_Ending(bool strongSuccess)
    {
        ending = strongSuccess ? "held" : escaped ? "weg" : "cadeau";
        step = "10 Einde: " + ending;
        Debug.Log("[Home Together] Einde: " + ending + " (" + string.Join(", ", choices) + ")");
        if (Online) StartCoroutine(session.SetEnding(ending));

        if (ending == "held")
        {
            // Inbrekers rennen weg, sirenes rechts, blauw licht over muren en sneeuw, mama's auto achter hem.
            if (rob != null && rob.activeSelf) StartCoroutine(RobLeaves(3f));
            StartCoroutine(Footsteps("snowstep", P.gardenWindow + P.right * 6f, P.street, 10, 0.25f, 1f));
            var siren = StorySounds.Play("siren", head.position + P.right * 25f, 0.5f, true);
            var blue = Story.MakeLight("Zwaailicht", Floor(P.street, 3f), LightType.Point, new Color(0.2f, 0.4f, 1f), 0f, 30f, transform);
            StartCoroutine(Flash(blue, 20f));
            for (float t = 0f; t < 6f; t += Time.deltaTime)
            {
                if (siren != null) siren.transform.position = Vector3.Lerp(head.position + P.right * 25f, P.street + P.right * 4f, t / 6f);
                yield return null;
            }
            var car = Story.MakeLight("Koplampen mama", Floor(head.position - head.forward * 8f, 1f), LightType.Spot, Color.white, 25f, 30f, transform);
            car.spotAngle = 60f;
            car.transform.rotation = Quaternion.LookRotation(head.position - car.transform.position);
            yield return new WaitForSeconds(1.5f);
            StorySounds.Play("mum", car.transform.position + Vector3.up * 0.6f, 1f);
            yield return new WaitForSeconds(6f);
        }
        else if (ending == "weg")
        {
            // Marleys deur zwaait open met warm licht. Achter hem start het busje en rijdt weg.
            Story.MakeLight("Marleys deur", Floor(P.marley, 2f), LightType.Point, new Color(1f, 0.75f, 0.45f), 6f, 10f, transform);
            StorySounds.Play("slam", Floor(P.marley, 1f), 0.4f, false, null, 0.8f);
            yield return new WaitForSeconds(1.5f);
            var engine = StorySounds.Play("engine", P.street, 1f, true);
            for (float t = 0f; t < 7f; t += Time.deltaTime)
            {
                if (engine != null) engine.transform.position = Vector3.Lerp(P.street, P.street + P.right * 30f, t / 7f);
                if (engine != null) engine.volume = 1f - t / 7f;
                yield return null;
            }
            StorySounds.Stop(engine);
            yield return new WaitForSeconds(4f);
        }
        else
        {
            // De inbreker loopt verder. Stilte. Onder de boom is alles weg, op één pakje na.
            if (rob != null && rob.activeSelf) yield return RobLeaves(0.9f);
            closetLight.intensity = 0f;
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if ((t.name.StartsWith("present", StringComparison.OrdinalIgnoreCase) || Array.IndexOf(Names.presents, t.name) >= 0) && !t.IsChildOf(transform)) t.gameObject.SetActive(false);
            parcel = Story.Block("Pakje", PrimitiveType.Cube, Floor(P.tree - Story.FlatDir(P.start, P.tree, P.forward) * 0.5f, 0.08f), Vector3.one * 0.16f, new Color(0.7f, 0.1f, 0.1f), transform);
            var tag = new GameObject("Kaartje", typeof(RectTransform));
            tag.transform.SetParent(parcel.transform, false);
            tag.transform.position = parcel.transform.position + Vector3.up * 0.1f;
            tag.transform.rotation = Quaternion.LookRotation(Story.FlatDir(head.position, parcel.transform.position, P.forward));
            var tmp = tag.AddComponent<TMPro.TextMeshPro>();
            tmp.text = "Merry Christmas, kid.";
            tmp.fontSize = 0.5f;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.color = new Color(0.1f, 0.1f, 0.2f);
            tmp.rectTransform.sizeDelta = new Vector2(0.4f, 0.1f);
            yield return WaitFor(() => Looking(parcel.transform.position, 30f), 30f);
            yield return new WaitForSeconds(5f);
        }

        // Zwart met de titel (zoals in het plan).
        step = "Einde";
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) l.enabled = false;
        var title = new GameObject("Titel Home Together", typeof(RectTransform));
        title.transform.SetParent(transform, false);
        title.transform.position = head.position + Story.Flat(head.forward).normalized * 3f;
        title.transform.rotation = Quaternion.LookRotation(Story.Flat(head.forward).normalized);
        var text = title.AddComponent<TMPro.TextMeshPro>();
        text.text = "Home Together";
        text.fontSize = 3f;
        text.alignment = TMPro.TextAlignmentOptions.Center;
        text.color = Color.white;
        text.rectTransform.sizeDelta = new Vector2(4f, 1f);
        yield return new WaitForSeconds(20f);
        if (Online) yield return session.EndSession();
    }

    IEnumerator RobLeaves(float speed)
    {
        if (robTorch != null) robTorch.enabled = false;
        yield return Story.Walk(rob.transform, doorInside, speed, robAnim);
        yield return Story.Walk(rob.transform, doorOutside + doorOut * 10f, speed, robAnim);
        rob.SetActive(false);
    }

    IEnumerator Flash(Light l, float intensity)
    {
        while (l != null)
        {
            l.intensity = Mathf.PingPong(Time.time * 4f, 1f) > 0.5f ? intensity : 0f;
            yield return null;
        }
    }

    IEnumerator Pulse(Light l, float min, float max, Func<bool> whileTrue)
    {
        while (l != null && whileTrue())
        {
            l.intensity = Mathf.Lerp(min, max, 0.5f + 0.5f * Mathf.Sin(Time.time * 2f));
            yield return null;
        }
        if (l != null) l.intensity = 0f;
    }
}
