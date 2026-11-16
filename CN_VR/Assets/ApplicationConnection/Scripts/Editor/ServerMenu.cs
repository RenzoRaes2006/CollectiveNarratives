using UnityEditor;

// Menu: Collective Narratives > Use local server
// Vinkje aan = de Editor praat met http://localhost:5000 (webapp: docker compose up --build).
// Vinkje uit = de live server. Alleen voor deze pc (EditorPrefs), dus geen wijziging in git.
public static class ServerMenu
{
    const string Item = "Collective Narratives/Use local server";

    [MenuItem(Item)]
    static void Toggle()
    {
        bool local = !IsLocal();
        EditorPrefs.SetInt(ServerConfig.LocalServerPref, local ? 1 : 0);
        UnityEngine.Debug.Log("[Server] Editor gebruikt nu " + (local ? "de lokale server" : "de live server") + ".");
    }

    [MenuItem(Item, true)]
    static bool Validate()
    {
        Menu.SetChecked(Item, IsLocal());
        return true;
    }

    static bool IsLocal()
    {
        int choice = EditorPrefs.GetInt(ServerConfig.LocalServerPref, -1);
        if (choice >= 0) return choice == 1;
        var config = ServerConfig.Load();
        return config.useEditorServerInEditor;
    }
}
