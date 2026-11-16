using UnityEngine;
using UnityEngine.UI;

// Gele, kloppende bol op de grip-knop van een controller met het woord "GRIP" ernaast,
// zodat iedereen meteen ziet welke knop bedoeld is. Zet dit object als kind van de controller,
// op de plek van de grip (binnenkant van het handvat, onder de middelvinger).
public class GripHint : MonoBehaviour
{
    public Color color = new Color(1f, 0.85f, 0.1f);
    [Tooltip("Kant waar het label staat, in lokale ruimte van de controller (links: -x, rechts: +x)")]
    public Vector3 labelOffset = new Vector3(0.06f, 0.02f, 0f);

    Transform sphere, label;
    bool visible;

    void Awake()
    {
        Build();
        SetVisible(false);
    }

    public void SetVisible(bool on)
    {
        if (sphere == null) Build();
        visible = on;
        sphere.gameObject.SetActive(on);
        label.gameObject.SetActive(on);
    }

    void Build()
    {
        if (sphere != null) return;
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        s.name = "Grip-bol";
        Destroy(s.GetComponent<Collider>()); // de straal mag er niet op botsen
        s.transform.SetParent(transform, false);
        s.transform.localScale = Vector3.one * 0.03f;
        s.GetComponent<Renderer>().material.color = color;
        sphere = s.transform;

        var go = new GameObject("Grip-label", typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        go.transform.localPosition = labelOffset;
        go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(200, 80);
        rt.localScale = Vector3.one * 0.0004f; // 8 x 3,2 cm

        var bg = new GameObject("Achtergrond", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        bg.transform.SetParent(go.transform, false);
        bg.color = color;
        bg.raycastTarget = false;
        bg.rectTransform.sizeDelta = new Vector2(200, 80);

        var t = new GameObject("Tekst", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        t.transform.SetParent(go.transform, false);
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = "GRIP";
        t.fontSize = 56;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.black;
        t.raycastTarget = false;
        t.rectTransform.sizeDelta = new Vector2(200, 80);
        label = go.transform;
    }

    void LateUpdate()
    {
        if (!visible) return;
        float pulse = 1f + 0.35f * Mathf.Sin(Time.unscaledTime * 8f);
        sphere.localScale = Vector3.one * (0.03f * pulse);
        var cam = Camera.main;
        if (cam != null) label.rotation = Quaternion.LookRotation(label.position - cam.transform.position, cam.transform.up);
    }
}
