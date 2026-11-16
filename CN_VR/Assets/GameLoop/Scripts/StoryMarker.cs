using UnityEngine;

/// <summary>
/// Plek in het huis die de game loop gebruikt (telefoon, achterdeur, zolderluik...).
/// Sleep de marker in de Scene view naar de juiste plek. Alleen zichtbaar in de Editor, nooit in de bril.
/// </summary>
public class StoryMarker : MonoBehaviour
{
    [TextArea] public string uitleg = "";
    public Color color = new Color(1f, 0.6f, 0.1f);
    public float radius = 0.25f;

    void OnDrawGizmos()
    {
        Gizmos.color = color;
        Gizmos.DrawWireSphere(transform.position, radius);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.down * 2f);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * (radius + 0.1f), name.Replace("Story_", ""));
#endif
    }
}
