using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Home Together > Game loop in deze scene zetten
/// Maakt het object "HomeTogether (game loop)" met markers ("Story_...") voor alle plekken uit het plan.
/// Sleep daarna elke marker naar de juiste plek in het huis (oranje bolletjes in de Scene view).
/// Zonder dit menu werkt de game loop ook: dan gokt hij de plekken bij het starten.
/// </summary>
public static class HomeTogetherMenu
{
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

        HomeTogetherGame.Names = game.objects;
        Vector3 start = Vector3.zero, forward = Vector3.forward;
        float floor = 0f;
        var origin = Object.FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
        var cam = origin != null && origin.Camera != null ? origin.Camera.transform : (Camera.main != null ? Camera.main.transform : null);
        if (origin != null) { floor = origin.transform.position.y; start = origin.transform.position + Vector3.up * 1.6f; forward = origin.transform.forward; }
        if (cam != null) { start = cam.position; forward = cam.forward; if (origin == null) floor = cam.position.y - 1.6f; }
        if (origin == null && cam == null)
            Debug.LogWarning("[Home Together] Geen XR Origin of Main Camera in de scene: markers staan rond (0,0,0). Sleep ze naar de juiste plek.");

        var points = HomeTogetherGame.ComputePoints(start, forward, floor);
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
        Debug.Log($"[Home Together] Game loop staat in de scene met {made} nieuwe markers. Sleep elke 'Story_...' marker naar de juiste plek en sla de scene op.");
    }

    [MenuItem("Tools/Home Together/Toon plekken in de Console")]
    static void LogPoints()
    {
        var g = Object.FindFirstObjectByType<HomeTogetherGame>(FindObjectsInactive.Include);
        if (g != null) HomeTogetherGame.Names = g.objects;
        var cam = Camera.main != null ? Camera.main.transform : null;
        var p = HomeTogetherGame.ComputePoints(cam != null ? cam.position : Vector3.zero, cam != null ? cam.forward : Vector3.forward, cam != null ? cam.position.y - 1.6f : 0f);
        foreach (var name in HomeTogetherGame.MarkerNames)
            Debug.Log($"{name}: {p.Get(name)} {(Story.MarkerTransform(name) != null ? "(marker)" : "(gegokt)")}");
    }
}
