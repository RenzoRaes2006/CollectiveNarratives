using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Menu: Collective Narratives > Build tutorial street here
// Zet de tutorial in de open scene (tutorial_scene): XR-rig, twee borden, box, Begin-knop, loopzone,
// onzichtbare muren, GRIP-aanduiding op de controllers, daglicht en de build settings.
// Alles staat onder "Tutorial (built)" en vertrekt vanaf het midden van de Scene view (kijkrichting = straatrichting).
// Opnieuw uitvoeren vervangt de vorige versie. Daarna kan je alles nog met de hand verschuiven.
public static class TutorialSceneBuilder
{
    const string RootName = "Tutorial (built)";
    const string RigPath = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
    const string NextScene = "demo_city_night";

    [MenuItem("Collective Narratives/Build tutorial street here")]
    public static void Build()
    {
        var view = SceneView.lastActiveSceneView;
        if (view == null) { Debug.LogError("[Tutorial] Open een Scene view en kijk naar de plek op straat waar de speler moet starten."); return; }

        var old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);

        // Startplek: midden van de Scene view, op de grond; richting: waar de Scene view naartoe kijkt.
        Physics.SyncTransforms();
        var pivot = view.pivot;
        var ground = Physics.Raycast(pivot + Vector3.up * 50f, Vector3.down, out var hit, 200f) ? hit.point : pivot;
        var forward = Vector3.ProjectOnPlane(view.rotation * Vector3.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;

        var root = new GameObject(RootName);
        root.transform.SetPositionAndRotation(ground, Quaternion.LookRotation(forward.normalized, Vector3.up));

        var rig = BuildRig(root.transform);
        var tutorial = BuildTutorial(root.transform, rig);
        BuildWalls(root.transform);
        SetDaylight();
        AddToBuildSettings();

        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Selection.activeGameObject = tutorial.gameObject;
        Debug.Log("[Tutorial] Klaar: tutorial gebouwd op " + ground + ". Scene opgeslagen.");
    }

    // Voor de scene na de tutorial (demo_city_night): alleen een rig als startplek, midden in de Scene view.
    // Geen SessionClient: die komt mee uit de tutorial.
    [MenuItem("Collective Narratives/Put player start here")]
    public static void PutPlayerStart()
    {
        var view = SceneView.lastActiveSceneView;
        if (view == null) { Debug.LogError("[Tutorial] Open een Scene view en kijk naar de startplek."); return; }
        var old = GameObject.Find("Player start");
        if (old != null) Object.DestroyImmediate(old);
        Physics.SyncTransforms();
        var pivot = view.pivot;
        var ground = Physics.Raycast(pivot + Vector3.up * 1.5f, Vector3.down, out var hit, 20f) ? hit.point : pivot; // binnen: niet van boven het dak
        var forward = Vector3.ProjectOnPlane(view.rotation * Vector3.forward, Vector3.up);
        var root = new GameObject("Player start");
        root.transform.SetPositionAndRotation(ground, Quaternion.LookRotation(forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized, Vector3.up));
        BuildRig(root.transform);
        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[Tutorial] Startplek gezet op " + ground + ". Scene opgeslagen.");
    }

    // ---------- Rig ----------

    static GameObject BuildRig(Transform root)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
        rig.transform.localPosition = Vector3.zero;
        rig.transform.localRotation = Quaternion.identity;

        // Alleen vloeiend lopen (linker stick) en draaien (rechter stick): geen teleport.
        foreach (var mb in rig.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb.GetType().Name != "ControllerInputActionManager") continue;
            var so = new SerializedObject(mb);
            so.FindProperty("m_TeleportInteractor").objectReferenceValue = null;
            so.FindProperty("m_TeleportMode").objectReferenceValue = null;
            so.FindProperty("m_TeleportModeCancel").objectReferenceValue = null;
            so.ApplyModifiedProperties();
        }
        foreach (var t in rig.GetComponentsInChildren<Transform>(true))
            if (t.name.Contains("Teleport")) t.gameObject.SetActive(false);
        return rig;
    }

    // ---------- Borden, box, Begin ----------

    static StreetTutorial BuildTutorial(Transform root, GameObject rig)
    {
        var game = new GameObject("Game");
        game.transform.SetParent(root, false);
        var street = game.AddComponent<StreetTutorial>();
        var flow = game.AddComponent<ApplicationConnection>();
        flow.tutorial = street;
        flow.nextScene = NextScene;

        street.sign1 = Sign(root, "Bord 1", new Vector3(0f, 1.6f, 3f));

        var station = new GameObject("Bord 2 (box en Begin)");
        station.transform.SetParent(root, false);
        station.transform.localPosition = new Vector3(0f, 0f, 13f);
        street.station2 = station;
        street.sign2 = Sign(station.transform, "Bord 2", new Vector3(0f, 1.6f, 0f));

        // Box rechts en Begin-knop links, elk op een sokkel 1,2 m voor bord 2 (de sokkels houden de speler tegen).
        var standColor = new Color(0.35f, 0.35f, 0.38f);
        Cube(station.transform, "Sokkel box", new Vector3(0.6f, 0.45f, -1.2f), new Vector3(0.3f, 0.9f, 0.3f), standColor);
        Cube(station.transform, "Sokkel Begin", new Vector3(-0.6f, 0.45f, -1.2f), new Vector3(0.3f, 0.9f, 0.3f), standColor);
        var box = Cube(station.transform, "Box", new Vector3(0.6f, 1.075f, -1.2f), Vector3.one * 0.35f, new Color(0.85f, 0.85f, 0.85f));
        box.AddComponent<XRSimpleInteractable>();
        street.box = box.AddComponent<ColorOnInteract>();

        var button = Cube(station.transform, "Begin-knop", new Vector3(-0.6f, 1.0f, -1.2f), new Vector3(0.5f, 0.2f, 0.2f), new Color(0.78f, 0.06f, 0.18f));
        button.AddComponent<XRSimpleInteractable>();
        street.beginButton = button.AddComponent<ColorOnInteract>();
        var label = new GameObject("Tekst Begin", typeof(TextMesh));
        label.transform.SetParent(button.transform, false);
        label.transform.localPosition = new Vector3(0f, 0f, -0.6f); // net voor de voorkant
        label.transform.localScale = new Vector3(1f / 0.5f, 1f / 0.2f, 1f / 0.2f) * 0.02f;
        var tm = label.GetComponent<TextMesh>();
        tm.text = "Begin";
        tm.fontSize = 60;
        tm.characterSize = 0.1f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;

        var zone = new GameObject("Loopzone");
        zone.transform.SetParent(station.transform, false);
        zone.transform.localPosition = new Vector3(0f, 1.5f, -1.5f);
        var zoneCollider = zone.AddComponent<BoxCollider>();
        zoneCollider.isTrigger = true;
        zoneCollider.size = new Vector3(12f, 3f, 3f); // hele straatbreedte
        street.walkZone = zoneCollider;

        street.gripHints = new[]
        {
            GripHint(rig, "Left Controller", -1f),
            GripHint(rig, "Right Controller", 1f),
        }.Where(h => h != null).ToArray();
        return street;
    }

    static TutorialSign Sign(Transform parent, string name, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        // Paal tot op de grond: de speler loopt eromheen, niet erdoor.
        var stop = go.AddComponent<BoxCollider>();
        stop.size = new Vector3(1.6f, localPos.y + 0.45f, 0.1f);
        stop.center = new Vector3(0f, (0.45f - localPos.y) / 2f, 0.05f);
        return go.AddComponent<TutorialSign>();
    }

    static GameObject Cube(Transform parent, string name, Vector3 localPos, Vector3 size, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        var renderer = go.GetComponent<Renderer>();
        var mat = new Material(renderer.sharedMaterial) { color = color };
        renderer.sharedMaterial = mat; // shortcut: materiaal zit in de scene, niet als asset; prima voor 2 blokjes
        return go;
    }

    // side: -1 links, +1 rechts. Plek op de grip is een schatting; fijnregelen in de bril.
    static GripHint GripHint(GameObject rig, string controllerName, float side)
    {
        var controller = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == controllerName);
        if (controller == null) { Debug.LogWarning("[Tutorial] " + controllerName + " niet gevonden in de rig."); return null; }
        var go = new GameObject("GRIP-aanduiding");
        go.transform.SetParent(controller, false);
        go.transform.localPosition = new Vector3(-side * 0.012f, -0.03f, -0.035f);
        var hint = go.AddComponent<GripHint>();
        hint.labelOffset = new Vector3(-side * 0.07f, 0.02f, 0f);
        return hint;
    }

    // ---------- Grenzen ----------

    // Onzichtbare muren: achter de start, net achter bord 2 en aan beide kanten (tot de gevels, of 5 m).
    static void BuildWalls(Transform root)
    {
        var walls = new GameObject("Grenzen (onzichtbaar)");
        walls.transform.SetParent(root, false);
        float left = SideDistance(root, -1f), right = SideDistance(root, 1f);
        const float back = -2.5f, front = 13.3f, height = 4f; // voor: net achter bord 2, zodat de speler er niet voorbij loopt
        float length = front - back, mid = (front + back) / 2f, width = left + right;
        Wall(walls.transform, "Achter", new Vector3((right - left) / 2f, height / 2f, back), new Vector3(width, height, 0.3f));
        Wall(walls.transform, "Voor", new Vector3((right - left) / 2f, height / 2f, front), new Vector3(width, height, 0.3f));
        Wall(walls.transform, "Links", new Vector3(-left, height / 2f, mid), new Vector3(0.3f, height, length));
        Wall(walls.transform, "Rechts", new Vector3(right, height / 2f, mid), new Vector3(0.3f, height, length));
        Debug.Log($"[Tutorial] Muren: {left:0.0} m links, {right:0.0} m rechts, {length:0} m lang. Verschuif ze als ze niet op de gevels staan.");
    }

    static float SideDistance(Transform root, float side)
    {
        var origin = root.position + Vector3.up * 1f;
        var dir = root.right * side;
        float best = 5f;
        foreach (float z in new[] { 0f, 6f, 12f })
            if (Physics.Raycast(origin + root.forward * z, dir, out var hit, 15f, ~0, QueryTriggerInteraction.Ignore))
                best = Mathf.Min(best, Mathf.Max(1.5f, hit.distance - 0.3f));
        return best;
    }

    static void Wall(Transform parent, string name, Vector3 localPos, Vector3 size)
    {
        var go = new GameObject("Muur " + name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.AddComponent<BoxCollider>().size = size;
    }

    // ---------- Daglicht ----------

    static void SetDaylight()
    {
        RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.fog = false;

        var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var sun = lights.FirstOrDefault(l => l.type == LightType.Directional);
        if (sun == null) sun = new GameObject("Zon").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.enabled = true;
        sun.gameObject.SetActive(true);
        sun.intensity = 1.3f;
        sun.color = new Color(1f, 0.96f, 0.88f);
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        RenderSettings.sun = sun;

        int off = 0;
        foreach (var l in lights)
        {
            if (l == sun) continue;
            if (l.enabled) { l.enabled = false; off++; }
        }
        DynamicGI.UpdateEnvironment();
        Debug.Log($"[Tutorial] Daglicht: zon aangepast, {off} nachtlampen uit.");
    }

    // ---------- Build settings ----------

    static void AddToBuildSettings()
    {
        var active = EditorSceneManager.GetActiveScene().path;
        var next = AssetDatabase.FindAssets(NextScene + " t:Scene").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => p.EndsWith("/" + NextScene + ".unity"));
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == active || s.path == next);
        if (next != null) scenes.Insert(0, new EditorBuildSettingsScene(next, true));
        scenes.Insert(0, new EditorBuildSettingsScene(active, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
