using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Menu: Collective Narratives > Maak scene 'Application Connection'
// (of zonder Unity-venster: zie BuildFromCommandLine)
// Maakt Assets/Scenes/ApplicationConnection.unity met camera, licht, vloer, telefoon en de koppeling.
// Zo hoeft niemand scene-bestanden met de hand te maken; daarna gewoon de scene + .meta committen.
public static class ApplicationConnectionSceneBuilder
{
    const string ScenePath = "Assets/Scenes/ApplicationConnection.unity";

    [MenuItem("Collective Narratives/Maak scene 'Application Connection'")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog("Scene bestaat al", ScenePath + " overschrijven?", "Overschrijven", "Annuleren"))
            return;
        CreateScene();
        AddToBuildSettings();
        AllowHttp();
        AssetDatabase.SaveAssets();
    }

    // Zonder vensters, voor de command line:
    // Unity -batchmode -quit -projectPath CN_VR -executeMethod ApplicationConnectionSceneBuilder.BuildFromCommandLine
    public static void BuildFromCommandLine()
    {
        CreateScene();
        AddToBuildSettings();
        AllowHttp();
        AssetDatabase.SaveAssets();
    }

    static void CreateScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var cam = Camera.main;
        if (cam != null) { cam.transform.position = new Vector3(0f, 1.6f, -2f); cam.transform.rotation = Quaternion.Euler(10f, 0f, 0f); }

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Vloer";

        var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        table.name = "Tafeltje";
        table.transform.position = new Vector3(0f, 0.4f, 0f);
        table.transform.localScale = new Vector3(0.8f, 0.8f, 0.6f);

        // Placeholder-telefoon: een blokje op het tafeltje.
        var phone = GameObject.CreatePrimitive(PrimitiveType.Cube);
        phone.name = "Telefoon (placeholder)";
        phone.transform.position = new Vector3(0f, 0.88f, 0f);
        phone.transform.localScale = new Vector3(0.3f, 0.15f, 0.2f);
        phone.AddComponent<AudioSource>();
        var ringer = phone.AddComponent<PhoneRinger>();

        var connection = new GameObject("Application Connection");
        var client = connection.AddComponent<VoteClient>();
        var flow = connection.AddComponent<ApplicationConnection>();
        flow.voteClient = client;
        flow.phone = ringer;

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = connection;
        Debug.Log("[Application Connection] Scene gemaakt: " + ScenePath + ". Zet het server-adres op 'Application Connection' > VoteClient > Server Url.");
    }

    static void AddToBuildSettings()
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Exists(s => s.path == ScenePath)) return;
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // Unity blokkeert standaard http://. De webapp draait op http, dus dit moet aan.
    static void AllowHttp()
    {
#if UNITY_2022_1_OR_NEWER
        if (PlayerSettings.insecureHttpOption != InsecureHttpOption.AlwaysAllowed)
        {
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            Debug.Log("[Application Connection] Allow downloads over HTTP staat nu op Always allowed (ProjectSettings.asset mee committen).");
        }
#endif
    }
}
