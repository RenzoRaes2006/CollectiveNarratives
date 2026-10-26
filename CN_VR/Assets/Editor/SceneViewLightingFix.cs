using UnityEditor;

// Turns Scene view lighting and skybox on, so light changes are visible while editing.
// Runs once automatically after this script is added; also available via Tools menu.
[InitializeOnLoad]
public static class SceneViewLightingFix
{
    const string RanKey = "CN_SceneViewLightingFix_Ran";

    static SceneViewLightingFix()
    {
        if (EditorPrefs.GetBool(RanKey, false)) return;
        EditorApplication.delayCall += () =>
        {
            EnableLighting();
            EditorPrefs.SetBool(RanKey, true);
        };
    }

    [MenuItem("Tools/Scene View/Turn Lighting + Skybox On")]
    public static void EnableLighting()
    {
        foreach (SceneView view in SceneView.sceneViews)
        {
            view.sceneLighting = true;
            view.sceneViewState.showSkybox = true;
            view.sceneViewState.alwaysRefresh = true;
            view.Repaint();
        }
    }
}
