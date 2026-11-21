using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Play in de Editor start altijd in tutorial_scene, net zoals de build (scene 0 in Build Settings).
/// Is de tutorial klaar, dan laadt ApplicationConnection (nextScene = demo_city_night) vanzelf de game.
/// Uitzetten: Tools > Home Together > Play start bij de tutorial (dan start Play in de scene die open staat).
/// Testtoetsen in de tutorial: S = zonder groot scherm spelen, N = volgende tutorialstap.
/// </summary>
[InitializeOnLoad]
public static class PlayFromTutorial
{
    const string TutorialScene = "Assets/Scenes/tutorial_scene.unity";
    const string Pref = "HomeTogether.PlayFromTutorial";
    const string MenuPath = "Tools/Home Together/Play start bij de tutorial";

    static bool On
    {
        get => EditorPrefs.GetBool(Pref, true);
        set => EditorPrefs.SetBool(Pref, value);
    }

    static PlayFromTutorial() => EditorApplication.delayCall += Apply;

    static void Apply()
    {
        EditorSceneManager.playModeStartScene = On ? AssetDatabase.LoadAssetAtPath<SceneAsset>(TutorialScene) : null;
        Menu.SetChecked(MenuPath, On);
    }

    [MenuItem(MenuPath)]
    static void Toggle()
    {
        On = !On;
        Apply();
    }
}
