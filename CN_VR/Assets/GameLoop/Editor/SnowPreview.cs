using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Toont de sneeuw rond het huis ook buiten Play, in de Scene view van demo_city_night.
/// Het is alleen een voorbeeld: niets ervan wordt in de scene opgeslagen (bij Play maakt HomeTogetherGame de echte sneeuw).
/// Aan/uit: Tools > Home Together > Sneeuw tonen in de Editor.
/// </summary>
[InitializeOnLoad]
public static class SnowPreview
{
    const string Pref = "HomeTogether.SnowPreview";
    const string MenuPath = "Tools/Home Together/Sneeuw tonen in de Editor";
    const string RootName = "Sneeuw (voorbeeld in de Editor, wordt niet opgeslagen)";
    static GameObject root;

    static bool On
    {
        get => EditorPrefs.GetBool(Pref, true);
        set => EditorPrefs.SetBool(Pref, value);
    }

    static SnowPreview()
    {
        EditorSceneManager.sceneOpened += (s, m) => Rebuild();
        EditorSceneManager.sceneClosing += (s, removing) => Clear();
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) Clear();
            if (state == PlayModeStateChange.EnteredEditMode) Rebuild();
        };
        EditorApplication.delayCall += Rebuild;
    }

    [MenuItem(MenuPath)]
    static void Toggle()
    {
        On = !On;
        Rebuild();
    }

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, On);
        return true;
    }

    static void Clear()
    {
        if (root != null) Object.DestroyImmediate(root);
        var old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);
        root = null;
    }

    static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Clear();
        if (!On) return;
        var game = Object.FindFirstObjectByType<HomeTogetherGame>(FindObjectsInactive.Include);
        if (game == null || !game.snowAroundHouse) return;

        HomeTogetherGame.Names = game.objects;
        var cam = Camera.main != null ? Camera.main.transform.position : game.transform.position;
        var house = HomeTogetherGame.FindHouseRoot(HomeTogetherGame.Named(game.objects.tree, cam));
        root = new GameObject(RootName);
        HomeSnow.Create(root.transform, house, cam);
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            t.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
    }
}
