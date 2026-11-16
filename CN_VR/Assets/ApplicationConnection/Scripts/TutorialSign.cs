using UnityEngine;
using UnityEngine.UI;

// Bord dat vast in de straat staat (volgt het hoofd niet). Witte, half doorzichtige achtergrond met donkere tekst.
// Zelfde indeling als het paneel in de bril: titel, grote tekst (code) en kleine uitleg.
// 1600x900 pixels = 1,6 x 0,9 m; het midden van het bord ligt op de positie van dit object.
public class TutorialSign : MonoBehaviour
{
    static readonly Color Background = new Color(1f, 1f, 1f, 0.6f);
    static readonly Color Ink = new Color(0.1f, 0.1f, 0.12f);

    Text title, big, small;

    void Awake() => Build();

    public void Show(string titleText, string bigText, string smallText)
    {
        if (title == null) Build();
        title.text = titleText;
        big.text = bigText;
        small.text = smallText;
    }

    void Build()
    {
        if (title != null) return;
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var go = new GameObject("Bord", typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4; // scherpere tekst
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(1600, 900);
        rt.localScale = Vector3.one * 0.001f;

        var bg = new GameObject("Achtergrond", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        bg.transform.SetParent(go.transform, false);
        bg.color = Background;
        bg.raycastTarget = false;
        Place(bg.rectTransform, Vector2.zero, new Vector2(1600, 900));

        title = Label(go.transform, font, 90, new Vector2(0, 320), new Vector2(1500, 130));
        title.fontStyle = FontStyle.Bold;
        big = Label(go.transform, font, 230, new Vector2(0, 70), new Vector2(1550, 300));
        small = Label(go.transform, font, 60, new Vector2(0, -250), new Vector2(1500, 320));
    }

    static Text Label(Transform parent, Font font, int size, Vector2 pos, Vector2 box)
    {
        var t = new GameObject("Tekst", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = font;
        t.fontSize = size;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Ink;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        Place(t.rectTransform, pos, box);
        return t;
    }

    static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }
}
