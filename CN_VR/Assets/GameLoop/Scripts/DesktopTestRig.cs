using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Testen zonder VR-bril: toetsenbord en muis besturen de XR Origin.
/// Staat alleen aan als er geen bril actief is (of als HomeTogetherGame.desktopTest op "Altijd" staat).
/// In de bril bestaat dit object niet, dus Kevin ziet de hulptekst nooit.
///
/// WASD / pijltjes = lopen, Shift = sneller, rechtermuisknop ingedrukt = rondkijken (of pijltjes links/rechts = draaien),
/// E of linkermuisknop = pakken / loslaten (briefje, zaklamp) of vasthouden (telefoon, hendel),
/// F = deur open/dicht (alleen als hij niet op slot is), V = door muren lopen aan/uit, H = hulp verbergen.
/// De testtoetsen van de game loop blijven: 1 / 2 = keuze A / B, J = samen-actie gelukt, N = stap overslaan.
/// </summary>
[DisallowMultipleComponent]
public class DesktopTestRig : MonoBehaviour
{
    public float eyeHeight = 1.6f;
    public float walkSpeed = 1.5f;
    public float runSpeed = 3f;
    public float turnSpeed = 90f;
    public float mouseSensitivity = 0.15f;
    public float reach = 2.2f;
    public bool noclip;

    HomeTogetherGame.TestMode mode;
    HomeTogetherGame game;
    XROrigin origin;
    Transform body, cam;
    CharacterController cc;
    DesktopInteractor hand;
    float pitch, fallSpeed;
    bool showHelp;
    float helpUntil;
    string feedback = "";
    float feedbackUntil;
    readonly List<Behaviour> poseDrivers = new List<Behaviour>();

    public static bool Active { get; private set; }

    /// <summary>Door muren lopen staat aan (dan mag je ook boven de grond zweven).</summary>
    public static bool Noclip { get; private set; }

    static bool HeadsetActive => XRSettings.isDeviceActive;

    /// <summary>Maakt de testbesturing als er geen bril is. Geeft null terug in de bril.</summary>
    public static DesktopTestRig Ensure(HomeTogetherGame game, HomeTogetherGame.TestMode mode)
    {
        if (mode == HomeTogetherGame.TestMode.Nooit) return null;
        if (mode == HomeTogetherGame.TestMode.Auto && HeadsetActive) return null;
        var existing = FindFirstObjectByType<DesktopTestRig>();
        if (existing != null) return existing;
        var go = new GameObject("Testbesturing zonder bril (alleen Editor/PC)");
        var rig = go.AddComponent<DesktopTestRig>();
        rig.mode = mode;
        return rig;
    }

    void Start()
    {
        origin = FindFirstObjectByType<XROrigin>();
        var camera = origin != null && origin.Camera != null ? origin.Camera : Camera.main;
        if (camera == null)
        {
            Debug.LogWarning("[Home Together] Testbesturing: geen camera gevonden.");
            enabled = false;
            return;
        }
        cam = camera.transform;
        body = origin != null ? origin.transform : cam;
        cc = body.GetComponent<CharacterController>();
        game = FindFirstObjectByType<HomeTogetherGame>();

        // Zonder bril schrijven de TrackedPoseDrivers niets, maar zet ze uit zodat ze onze muis niet overschrijven.
        foreach (var b in cam.GetComponents<Behaviour>())
            if (b != null && b.enabled && b.GetType().Name == "TrackedPoseDriver") { b.enabled = false; poseDrivers.Add(b); }

        pitch = 0f;
        cam.localRotation = Quaternion.identity;

        var handGo = new GameObject("Testhand");
        handGo.transform.SetParent(cam, false);
        handGo.transform.localPosition = new Vector3(0.12f, -0.18f, 0.45f);
        hand = handGo.AddComponent<DesktopInteractor>();

        Active = true;
        helpUntil = Time.time + 12f;
        Debug.Log("[Home Together] Geen VR-bril: testbesturing met toetsenbord en muis staat aan (H = hulp).");
    }

    void OnDestroy()
    {
        Active = false;
        Noclip = false;
        foreach (var b in poseDrivers) if (b != null) b.enabled = true;
    }

    void Update()
    {
        if (cam == null) return;
        // Komt er toch een bril bij (Auto), dan geeft de testbesturing alles terug.
        if (mode == HomeTogetherGame.TestMode.Auto && HeadsetActive)
        {
            Debug.Log("[Home Together] VR-bril gevonden: testbesturing gaat uit.");
            if (hand != null) Destroy(hand.gameObject);
            Destroy(gameObject);
            return;
        }
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null) return;
        float dt = Time.deltaTime;

        // Rondkijken
        float yaw = 0f;
        if (mouse != null && mouse.rightButton.isPressed)
        {
            Vector2 d = mouse.delta.ReadValue() * mouseSensitivity;
            yaw += d.x;
            pitch = Mathf.Clamp(pitch - d.y, -80f, 80f);
        }
        if (kb.leftArrowKey.isPressed) yaw -= turnSpeed * dt;
        if (kb.rightArrowKey.isPressed) yaw += turnSpeed * dt;
        if (Mathf.Abs(yaw) > 0f) body.RotateAround(cam.position, Vector3.up, yaw);

        // Lopen (zoals de joystick: de speler beweegt zichzelf, de game loop nooit)
        Vector2 move = Vector2.zero;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
        if (kb.dKey.isPressed) move.x += 1f;
        if (kb.aKey.isPressed) move.x -= 1f;
        Vector3 fwd = Story.FlatDir(Vector3.zero, cam.forward, Vector3.forward);
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        float speed = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? runSpeed : walkSpeed;
        Vector3 delta = (fwd * move.y + right * move.x).normalized * speed * dt;

        if (kb.vKey.wasPressedThisFrame) { noclip = !noclip; Noclip = noclip; Say(noclip ? "Door muren lopen: AAN" : "Door muren lopen: UIT"); }
        if (kb.hKey.wasPressedThisFrame) { showHelp = !(showHelp || Time.time < helpUntil); helpUntil = 0f; }

        if (cc != null && cc.enabled && !noclip)
        {
            fallSpeed = cc.isGrounded ? -1f : Mathf.Max(fallSpeed - 9.81f * dt, -20f);
            cc.Move(delta + Vector3.up * fallSpeed * dt);
        }
        else
        {
            if (kb.spaceKey.isPressed) delta += Vector3.up * speed * dt;
            if (kb.leftCtrlKey.isPressed) delta -= Vector3.up * speed * dt;
            body.position += delta;
        }

        // Kijkdoel
        var target = Aim(out var door, out _);

        // Pakken / vasthouden
        bool press = kb.eKey.wasPressedThisFrame || (mouse != null && mouse.leftButton.wasPressedThisFrame);
        bool held = kb.eKey.isPressed || (mouse != null && mouse.leftButton.isPressed);
        // Testbesturing: E terwijl je naar de open kast kijkt = erin stappen (in de bril loopt Kevin er zelf in).
        if (press && !gliding && game != null && game.ClosetEnterable)
        {
            Vector3 inside = game.ClosetInside;
            Vector3 to = inside - cam.position; to.y = 0f;
            if (to.magnitude < 3.5f && Vector3.Angle(Story.FlatDir(Vector3.zero, cam.forward, Vector3.forward), to) < 35f)
            {
                StartCoroutine(GlideInto(inside, game.ClosetFront));
                press = false;
            }
        }
        if (hand.Holding != null)
        {
            bool isGrab = hand.Holding is XRGrabInteractable;
            if ((isGrab && press) || (!isGrab && !held) || !(hand.Holding as Behaviour).isActiveAndEnabled) hand.Release();
        }
        else if (press)
        {
            if (target != null)
            {
                if (!target.isActiveAndEnabled) Say(NameOf(target) + " kan nu niet (nog op slot in het verhaal)");
                else if (!hand.Grab(target)) Say(NameOf(target) + " kan niet vastgepakt worden");
            }
            else if (door != null) ToggleDoor(door);
        }

        if (kb.fKey.wasPressedThisFrame && door != null) ToggleDoor(door);
#endif
    }


    /// <summary>Kijk naar een punt (voor tests): draait het lichaam en kantelt het hoofd.</summary>
    public void LookAt(Vector3 point)
    {
        if (cam == null) return;
        Vector3 d = point - cam.position;
        Vector3 flat = new Vector3(d.x, 0f, d.z);
        if (flat.sqrMagnitude > 0.0001f)
            body.RotateAround(cam.position, Vector3.up, Vector3.SignedAngle(Story.FlatDir(Vector3.zero, cam.forward, Vector3.forward), flat, Vector3.up));
        pitch = Mathf.Clamp(-Mathf.Atan2(d.y, flat.magnitude) * Mathf.Rad2Deg, -80f, 80f);
    }
    void LateUpdate()
    {
        if (cam == null) return;
        cam.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        // Ooghoogte: zonder bril staat de camera anders op de vloer of op de standaardhoogte van de rig.
        var p = cam.position;
        cam.position = new Vector3(p.x, body.position.y + eyeHeight, p.z);
    }

    XRBaseInteractable Aim(out VRDoor door, out string hitName)
    {
        door = null;
        hitName = "";
        var hits = Physics.RaycastAll(cam.position, cam.forward, reach, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            var col = h.collider;
            if (col.transform.IsChildOf(body) || col.transform.IsChildOf(cam)) continue;
            if (hand.Holding != null && col.transform.IsChildOf(hand.Holding.transform)) continue;
            var interactable = col.GetComponentInParent<XRBaseInteractable>(true);
            var d = col.GetComponentInParent<VRDoor>();
            if (d != null && (interactable == null || d.IsHandle(interactable))) { door = d; return null; }
            if (interactable != null) return interactable;
            if (col.isTrigger) continue; // triggerzones (bv. van de inbreker) blokkeren niet
            hitName = col.name;
            return null; // muur of meubel ervoor
        }
        return null;
    }


    bool gliding;

    /// <summary>Alleen voor testen zonder bril: stap rustig de kast in en draai naar de kier.</summary>
    System.Collections.IEnumerator GlideInto(Vector3 inside, Vector3 facing)
    {
        gliding = true;
        bool hadCc = cc != null && cc.enabled;
        if (hadCc) cc.enabled = false;
        Vector3 startCam = new Vector3(cam.position.x, 0f, cam.position.z);
        float startYaw = Mathf.Atan2(cam.forward.x, cam.forward.z) * Mathf.Rad2Deg;
        float endYaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
        for (float t = 0f; t <= 1f; t += Time.deltaTime / 1.2f)
        {
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            Vector3 want = Vector3.Lerp(startCam, new Vector3(inside.x, 0f, inside.z), k);
            Vector3 now = new Vector3(cam.position.x, 0f, cam.position.z);
            body.position += want - now;
            float yawNow = Mathf.Atan2(cam.forward.x, cam.forward.z) * Mathf.Rad2Deg;
            body.RotateAround(cam.position, Vector3.up, Mathf.DeltaAngle(yawNow, Mathf.LerpAngle(startYaw, endYaw, k)));
            yield return null;
        }
        pitch = 0f;
        if (hadCc) cc.enabled = true;
        gliding = false;
    }
    void ToggleDoor(VRDoor door)
    {
        if (door.Locked) { Say(door.name + " zit op slot"); return; }
        if (Mathf.Abs(door.OpenAngle) > 5f) { door.SwingTo(0f, 120f); return; }
        // Open van de speler weg.
        Transform t = door.transform;
        Vector3 hinge = t.position;
        Vector3 center = Story.Center(t.gameObject);
        Vector3 up = t.parent != null ? t.parent.up : Vector3.up;
        Vector3 a = hinge + Quaternion.AngleAxis(90f, up) * (center - hinge);
        Vector3 b = hinge + Quaternion.AngleAxis(-90f, up) * (center - hinge);
        float sign = Vector3.Distance(a, cam.position) > Vector3.Distance(b, cam.position) ? 1f : -1f;
        door.SwingTo(sign * 90f, 120f);
    }

    static string NameOf(XRBaseInteractable i) => i == null ? "" : i.name;

    void Say(string text)
    {
        feedback = text;
        feedbackUntil = Time.time + 2.5f;
        Debug.Log("[Testbesturing] " + text);
    }

    void OnGUI()
    {
        if (cam == null) return;
        // Richtpunt
        var c = new Vector2(Screen.width / 2f, Screen.height / 2f);
        GUI.Label(new Rect(c.x - 5, c.y - 12, 20, 24), "+");

        // Geen stap, stemming of uitleg over het verhaal in beeld: dat moet je uit de wereld zelf halen,
        // net als Kevin in de bril. De stap staat in de Console. Alleen de toetsen, kort bij de start of met H.
        if (Time.time < feedbackUntil)
            GUI.Label(new Rect(c.x - 200, c.y + 30, 400, 24), feedback, new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
        if (!showHelp && Time.time > helpUntil) return;
        string text =
            "Toetsen (H = tonen/verbergen)\n" +
            "WASD lopen, Shift sneller, rechtermuis = kijken, pijltjes = draaien\n" +
            "E/klik = pakken of vasthouden, F = deur, V = door muren\n" +
            "Publiek nadoen: 1/2 = keuze A/B, J = samen-actie, N = stap overslaan";
        var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12, wordWrap = true };
        GUI.Box(new Rect(10, 10, 440, 80), text, style);
    }
}

/// <summary>Een "hand" voor de testbesturing: pakt via de XR Interaction Manager vast wat je aanwijst.</summary>
public class DesktopInteractor : XRBaseInteractor
{
    XRBaseInteractable pending, holding;

    public XRBaseInteractable Holding => holding != null && isSelectActive && IsSelecting(holding) ? holding : null;

    public override bool isSelectActive => base.isSelectActive && holding != null;

    protected override void Awake()
    {
        base.Awake();
        interactionLayers = -1;
    }

    public bool Grab(XRBaseInteractable target)
    {
        holding = target;
        pending = target;
        if (target is IXRSelectInteractable s && interactionManager != null && interactionManager.CanSelect(this, s))
            interactionManager.SelectEnter((IXRSelectInteractor)this, s);
        if (IsSelecting(target)) return true;
        holding = null;
        pending = null;
        return false;
    }

    public void Release()
    {
        holding = null;
        pending = null;
        if (hasSelection && interactionManager != null)
            for (int i = interactablesSelected.Count - 1; i >= 0; i--)
                interactionManager.SelectExit((IXRSelectInteractor)this, interactablesSelected[i]);
    }

    public override void GetValidTargets(List<IXRInteractable> targets)
    {
        targets.Clear();
        if (pending != null && pending.isActiveAndEnabled) targets.Add(pending);
    }
}
