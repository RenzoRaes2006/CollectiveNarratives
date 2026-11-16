using TMPro;
using UnityEngine;

/// <summary>
/// Post-it in de wereld (geen UI). Pas de tekst aan in de Inspector of via code:
///   GetComponent&lt;StickyNote&gt;().Text = "Nieuwe tekst";
/// De tekst staat op een 3D TextMeshPro-object (child "Text") op het gele papiertje (child "Paper").
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class StickyNote : MonoBehaviour
{
    [TextArea(3, 8)]
    [SerializeField] private string text = "Kevin,\nniet vergeten:\nalles op slot!\n- mama x";

    [SerializeField] private Color inkColor = new Color(0.10f, 0.16f, 0.45f, 1f);

    [Tooltip("De TextMeshPro (3D) op het papiertje. Wordt automatisch gezocht als dit leeg is.")]
    [SerializeField] private TextMeshPro label;

    /// <summary>Tekst op de post-it. Kan ook tijdens het spelen veranderd worden.</summary>
    public string Text
    {
        get => text;
        set { text = value; Apply(); }
    }

    public Color InkColor
    {
        get => inkColor;
        set { inkColor = value; Apply(); }
    }

    private void OnEnable() => Apply();

#if UNITY_EDITOR
    private void OnValidate()
    {
        // TextMeshPro mag niet direct in OnValidate aangepast worden, dus één frame later.
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null) Apply();
        };
    }
#endif

    private void Apply()
    {
        if (label == null) label = GetComponentInChildren<TextMeshPro>(true);
        if (label == null) return;
        label.text = text;
        label.color = inkColor;
    }
}
