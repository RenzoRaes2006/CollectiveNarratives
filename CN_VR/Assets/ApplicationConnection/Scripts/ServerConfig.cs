using UnityEngine;

// Waar de game de website vindt. Het asset staat in Assets/ApplicationConnection/Resources/ServerConfig.asset.
[CreateAssetMenu(menuName = "Collective Narratives/Server Config", fileName = "ServerConfig")]
public class ServerConfig : ScriptableObject
{
    [Tooltip("De online server (Quest-build)")]
    public string serverUrl = "https://collective-narratives.app";

    [Tooltip("In de Unity Editor: de lokale server (webapp/: docker compose up --build, of dotnet run)")]
    public string editorServerUrl = "http://localhost:5000";

    [Tooltip("Standaard voor iedereen. Eigen keuze per pc: menu Collective Narratives > Use local server (overschrijft dit)")]
    public bool useEditorServerInEditor = true;

    // Eigen keuze van deze pc (EditorPrefs, niet in git). -1 = niet gekozen, volg useEditorServerInEditor.
    public const string LocalServerPref = "CN.UseLocalServer";

    [Tooltip("Sleutel waarmee de game sessies mag aanmaken. Lokaal: dev-game-key. " +
             "De echte sleutel NIET hier invullen (dit asset staat in git): zet hem in Resources/GameKey.txt, dat staat in .gitignore.")]
    public string gameKey = "dev-game-key";

    bool UsingEditorServer
    {
        get
        {
#if UNITY_EDITOR
            int choice = UnityEditor.EditorPrefs.GetInt(LocalServerPref, -1);
            return choice < 0 ? useEditorServerInEditor : choice == 1;
#else
            return false;
#endif
        }
    }

    public string ActiveServerUrl => (UsingEditorServer ? editorServerUrl : serverUrl).TrimEnd('/');

    // Tegen de online server: de sleutel uit Resources/GameKey.txt als die er is.
    public string ActiveGameKey
    {
        get
        {
            if (!UsingEditorServer)
            {
                var file = Resources.Load<TextAsset>("GameKey");
                if (file != null && !string.IsNullOrWhiteSpace(file.text)) return file.text.Trim();
            }
            return gameKey;
        }
    }

    public static ServerConfig Load()
    {
        var config = Resources.Load<ServerConfig>("ServerConfig");
        return config != null ? config : CreateInstance<ServerConfig>();
    }
}
