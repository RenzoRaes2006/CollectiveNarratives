using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Home Together > Game loop in deze scene zetten
/// Maakt het object "HomeTogether (game loop)" met markers ("Story_...") voor alle plekken uit het plan.
///
/// De markers springen vanzelf naar de echte objecten (kerstboom, telefoon, kast...) als je de scene opent
/// of de scripts opnieuw compileren. Sleep je een marker zelf, dan blijft hij staan waar jij hem zet
/// (volgObject gaat uit). Tools > Home Together > Markers op de objecten zetten zet ze allemaal terug.
/// </summary>
[InitializeOnLoad]
public static class HomeTogetherMenu
{
    static HomeTogetherMenu()
    {
        EditorSceneManager.sceneOpened += (scene, mode) => SnapMarkers(false, false);
        Undo.postprocessModifications += OnModified;
        EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) SnapMarkers(false, false); };
    }

    [MenuItem("Tools/Home Together/Game loop in deze scene zetten")]
    static void AddToScene()
    {
        var game = Object.FindFirstObjectByType<HomeTogetherGame>(FindObjectsInactive.Include);
        if (game == null)
        {
            var go = new GameObject("HomeTogether (game loop)");
            Undo.RegisterCreatedObjectUndo(go, "Home Together game loop");
            game = go.AddComponent<HomeTogetherGame>();
        }

        var points = Compute(game);
        int made = 0;
        foreach (var name in HomeTogetherGame.MarkerNames)
        {
            if (Story.MarkerTransform(name) != null) continue;
            var m = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(m, "Home Together marker");
            m.transform.SetParent(game.transform, false);
            m.transform.position = points.Get(name);
            var marker = m.AddComponent<StoryMarker>();
            marker.uitleg = HomeTogetherGame.MarkerHelp(name);
            made++;
        }
        EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
        Selection.activeGameObject = game.gameObject;
        SnapMarkers(false, true);
        Debug.Log($"[Home Together] Game loop staat in de scene met {made} nieuwe markers.");
    }

    [MenuItem("Tools/Home Together/Markers op de objecten zetten")]
    static void SnapAllMenu() => SnapMarkers(true, true);

    /// <summary>Zet de markers op de plek van de echte objecten. all = ook de markers die je met de hand verplaatste.</summary>
    static void SnapMarkers(bool all, bool log)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var game = Object.FindFirstObjectByType<HomeTogetherGame>(FindObjectsInactive.Include);
        if (game == null) return;
        var markers = Object.FindObjectsByType<StoryMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (markers.Length == 0) return;

        if (all) foreach (var m in markers) { Undo.RecordObject(m, "Markers op de objecten"); m.volgObject = true; }
        var points = Compute(game);
        int moved = 0;
        var report = new System.Text.StringBuilder();
        foreach (var m in markers)
        {
            if (!m.volgObject || !points.fromObject.Contains(m.name)) continue;
            Vector3 target = points.Get(m.name);
            if ((m.transform.position - target).sqrMagnitude < 0.0001f) continue;
            report.AppendLine($"  {m.name}: {Vector3.Distance(m.transform.position, target):0.0} m verschoven");
            Undo.RecordObject(m.transform, "Markers op de objecten");
            m.transform.position = target;
            moved++;
        }
        if (moved > 0)
        {
            EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
            Debug.Log($"[Home Together] {moved} markers op hun object gezet (sla de scene op):\n{report}");
        }
        else if (log) Debug.Log("[Home Together] Alle markers staan al op hun object.");

        if (log)
            foreach (var name in HomeTogetherGame.MarkerNames)
                if (!points.fromObject.Contains(name))
                    Debug.Log($"[Home Together] {name}: geen object gevonden, de marker bepaalt de plek. Sleep hem naar de juiste plek.");
    }

    static HomeTogetherGame.Points Compute(HomeTogetherGame game)
    {
        HomeTogetherGame.Names = game.objects;
        Vector3 start = Vector3.zero, forward = Vector3.forward;
        float floor = 0f;
        var origin = Object.FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
        var cam = origin != null && origin.Camera != null ? origin.Camera.transform : (Camera.main != null ? Camera.main.transform : null);
        if (origin != null) { floor = origin.transform.position.y; start = origin.transform.position + Vector3.up * 1.6f; forward = origin.transform.forward; }
        if (cam != null) { start = new Vector3(cam.position.x, floor + 1.6f, cam.position.z); forward = cam.forward; if (origin == null) floor = cam.position.y - 1.6f; }
        return HomeTogetherGame.ComputePoints(start, forward, floor);
    }

    // Sleep je een marker zelf in de Scene view, dan volgt hij het object niet meer.
    static UndoPropertyModification[] OnModified(UndoPropertyModification[] mods)
    {
        foreach (var mod in mods)
        {
            var t = mod.currentValue?.target as Transform;
            if (t == null || !mod.currentValue.propertyPath.StartsWith("m_LocalPosition")) continue;
            var marker = t.GetComponent<StoryMarker>();
            if (marker != null && marker.volgObject)
            {
                marker.volgObject = false;
                EditorUtility.SetDirty(marker);
            }
        }
        return mods;
    }

    [MenuItem("Tools/Home Together/Toon plekken in de Console")]
    static void LogPoints()
    {
        var g = Object.FindFirstObjectByType<HomeTogetherGame>(FindObjectsInactive.Include);
        if (g == null) return;
        var p = Compute(g);
        foreach (var name in HomeTogetherGame.MarkerNames)
        {
            var m = Story.MarkerTransform(name);
            var sm = m != null ? m.GetComponent<StoryMarker>() : null;
            string bron = sm != null && !sm.volgObject ? "(marker, met de hand)" : p.fromObject.Contains(name) ? "(object)" : m != null ? "(marker)" : "(gegokt)";
            Debug.Log($"{name}: {p.Get(name)} {bron}");
        }
    }
}
