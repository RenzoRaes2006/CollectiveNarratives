using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Menu: Collective Narratives > Maak scene 'Application Connection'
// (of zonder Unity-venster: zie BuildFromCommandLine)
// Maakt Assets/Scenes/ApplicationConnection.unity met camera, licht, vloer, telefoon en de koppeling,
// plus Assets/ApplicationConnection/Resources/ServerConfig.asset als dat er nog niet is.
// Zo hoeft niemand scene-bestanden met de hand te maken; daarna gewoon de scene + .meta committen.
public static class ApplicationConnectionSceneBuilder
{
    const string ScenePath = "Assets/Scenes/ApplicationConnection.unity";
    const string ConfigPath = "Assets/ApplicationConnection/Resources/ServerConfig.asset";

    [MenuItem("Collective Narratives/Create scene 'Application Connection'")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog("Scene already exists", "Overwrite " + ScenePath + "?", "Overwrite", "Cancel"))
            return;
        BuildFromCommandLine();
    }

    // Zonder vensters, voor de command line:
    // Unity -batchmode -quit -projectPath CN_VR -executeMethod ApplicationConnectionSceneBuilder.BuildFromCommandLine
    public static void BuildFromCommandLine()
    {
        var config = CreateConfig();
        CreateScene(config);
        AddToBuildSettings();
        AssetDatabase.SaveAssets();
    }

    static ServerConfig CreateConfig()
    {
        var config = AssetDatabase.LoadAssetAtPath<ServerConfig>(ConfigPath);
        if (config != null) return config;
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
        config = ScriptableObject.CreateInstance<ServerConfig>();
        AssetDatabase.CreateAsset(config, ConfigPath);
        Debug.Log("[Application Connection] " + ConfigPath + " gemaakt.");
        return config;
    }

    static void CreateScene(ServerConfig config)
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
        var client = connection.AddComponent<SessionClient>();
        client.config = config;
        var tutorial = connection.AddComponent<Tutorial>();
        var flow = connection.AddComponent<ApplicationConnection>();
        flow.sessionClient = client;
        flow.phone = ringer;
        flow.tutorial = tutorial;

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = connection;
        Debug.Log("[Application Connection] Scene gemaakt: " + ScenePath + ". Server-adres en game key staan in " + ConfigPath + ".");
    }

    static void AddToBuildSettings()
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Exists(s => s.path == ScenePath)) return;
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
