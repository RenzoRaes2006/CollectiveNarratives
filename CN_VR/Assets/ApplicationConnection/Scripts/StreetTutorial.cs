using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Tutorial op straat (tutorial_scene). ApplicationConnection zet eerst de code op bord 1 en roept Begin() aan
// zodra het grote scherm gekoppeld is. Elke stap is pas klaar als de speler de actie echt doet:
//   1. rondkijken (links en rechts)  2. naar bord 2 lopen  3. naar de box wijzen (blauw)
//   4. grip indrukken (groen)        5. Begin indrukken: IsDone, de game start.
// Editor zonder bril: N = volgende stap.
public class StreetTutorial : Tutorial
{
    enum Step { Idle, Look, Walk, Point, Grip, Begin, Done }

    public TutorialSign sign1;
    [Tooltip("Bord 2 met box en Begin-knop; staat uit tot de speler moet lopen")]
    public GameObject station2;
    public TutorialSign sign2;
    public ColorOnInteract box;
    public ColorOnInteract beginButton;
    [Tooltip("Zone bij bord 2: de stap 'lopen' is klaar zodra het hoofd erin staat")]
    public Collider walkZone;
    public GripHint[] gripHints;
    [Tooltip("Zo ver (graden) moet de speler naar links en naar rechts kijken")]
    public float lookThreshold = 70f;

    Step step;
    float startYaw, minYaw, maxYaw;
    SessionClient session;

    public override TutorialSign Sign => sign1;
    // Voor de debug-overlay: stap en plek van het hoofd t.o.v. de start (x = zijwaarts, z = de straat in).
    public string StepName => Camera.main == null ? step.ToString()
        : $"{step}, head {transform.parent.InverseTransformPoint(Camera.main.transform.position):F1}";

    public static bool LookedAround(float minYaw, float maxYaw, float threshold = 70f) =>
        minYaw <= -threshold && maxYaw >= threshold;

    void Awake() => ResetTutorial();

    public override void Begin()
    {
        IsDone = false;
        var cam = Camera.main;
        startYaw = cam != null ? cam.transform.eulerAngles.y : 0f;
        minYaw = maxYaw = 0f;
        Go(Step.Look);
    }

    public override void ResetTutorial()
    {
        IsDone = false;
        step = Step.Idle;
        if (station2 != null) station2.SetActive(false);
        if (box != null) box.ResetBox();
        if (beginButton != null) { beginButton.ResetBox(); beginButton.gameObject.SetActive(false); }
        ShowGripHints(false);
    }

    void Go(Step next)
    {
        step = next;
        switch (next)
        {
            case Step.Look:
                sign1.Show("Look around", "", "Turn your head to the left and to the right.");
                break;
            case Step.Walk:
                station2.SetActive(true);
                sign1.Show("Walk", "", "Push the left stick to walk to the next sign.");
                sign2.Show("Over here!", "", "Walk up to this sign.");
                break;
            case Step.Point:
                sign1.Show("", "", "");
                sign1.gameObject.SetActive(false); // de speler staat nu bij bord 2
                sign2.Show("Point at the box", "", "Aim your hand at the box.");
                break;
            case Step.Grip:
                ShowGripHints(true);
                sign2.Show("Press GRIP", "", "Squeeze the button under your middle finger, on the inside of the controller. Look at your hand: it's the glowing yellow one.");
                break;
            case Step.Begin:
                beginButton.gameObject.SetActive(true);
                break;
            case Step.Done:
                ShowGripHints(false);
                sign2.Show("Here we go!", "", "");
                IsDone = true;
                break;
        }
    }

    void Update()
    {
        if (step == Step.Idle || step == Step.Done) return;
        if (EditorNextPressed()) { SkipStep(); return; }

        var cam = Camera.main;
        switch (step)
        {
            case Step.Look:
                if (cam == null) return;
                float yaw = Mathf.DeltaAngle(startYaw, cam.transform.eulerAngles.y);
                minYaw = Mathf.Min(minYaw, yaw);
                maxYaw = Mathf.Max(maxYaw, yaw);
                if (LookedAround(minYaw, maxYaw, lookThreshold)) Go(Step.Walk);
                break;
            case Step.Walk:
                if (cam != null && walkZone.bounds.Contains(cam.transform.position)) Go(Step.Point);
                break;
            case Step.Point:
                if (box.WasHovered) Go(Step.Grip);
                break;
            case Step.Grip:
                if (box.WasSelected) Go(Step.Begin);
                break;
            case Step.Begin:
                if (session == null) session = FindFirstObjectByType<SessionClient>();
                int viewers = session != null ? session.Viewers : 0;
                sign2.Show("You're ready", "", $"{viewers} {(viewers == 1 ? "viewer has" : "viewers have")} joined.\nPoint at Begin and press GRIP to start the story.");
                if (beginButton.WasSelected) Go(Step.Done);
                break;
        }
    }

    // Volgende stap zonder de actie (Editor: N). Ook voor testen.
    public void SkipStep()
    {
        if (step != Step.Idle && step != Step.Done) Go(step + 1);
    }

    void ShowGripHints(bool on)
    {
        if (gripHints == null) return;
        foreach (var hint in gripHints) if (hint != null) hint.SetVisible(on);
    }

    static bool EditorNextPressed()
    {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.nKey.wasPressedThisFrame;
#else
        return false;
#endif
    }
}
