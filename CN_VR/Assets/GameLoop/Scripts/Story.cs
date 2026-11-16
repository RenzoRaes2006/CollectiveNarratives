using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>Kleine hulpfuncties voor de game loop: zoeken, afstanden, lichtjes, blokjes.</summary>
public static class Story
{
    public static Transform Head
    {
        get
        {
            var cam = Camera.main;
            return cam != null ? cam.transform : null;
        }
    }

    public static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    public static float FlatDist(Vector3 a, Vector3 b) => Flat(a - b).magnitude;

    public static Vector3 FlatDir(Vector3 from, Vector3 to, Vector3 fallback)
    {
        var d = Flat(to - from);
        return d.sqrMagnitude < 0.0001f ? fallback : d.normalized;
    }

    /// <summary>Hoogte van de vloer onder de speler (XR Origin), anders hoofd min 1,6 m.</summary>
    public static float FloorY(Transform head)
    {
        var origin = Object.FindFirstObjectByType<XROrigin>();
        if (origin != null) return origin.transform.position.y;
        return head != null ? head.position.y - 1.6f : 0f;
    }

    static IEnumerable<Transform> All() =>
        Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);

    /// <summary>Object met precies deze naam (hoofdletters maken niet uit).</summary>
    public static GameObject FindExact(params string[] names)
    {
        var all = new List<Transform>(All());
        foreach (var n in names)
            foreach (var t in all)
                if (string.Equals(t.name.Trim(), n.Trim(), System.StringComparison.OrdinalIgnoreCase) && !IsStory(t))
                    return t.gameObject;
        return null;
    }

    /// <summary>Object met precies deze naam. Zijn er meerdere, dan het exemplaar het dichtst bij 'near'.</summary>
    public static GameObject FindExactNear(string name, Vector3 near)
    {
        GameObject best = null;
        float bestD = float.MaxValue;
        string n = name.Trim();
        foreach (var t in All())
        {
            if (IsStory(t) || !string.Equals(t.name.Trim(), n, System.StringComparison.OrdinalIgnoreCase)) continue;
            float d = Vector3.Distance(t.position, near);
            if (d < bestD) { bestD = d; best = t.gameObject; }
        }
        return best;
    }

    static bool IsHandle(string n) =>
        n.IndexOf("Pommel", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("handle", System.StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    /// Maakt van een object een VR-deur (zoals VRDoorSetup), als het er nog geen is.
    /// Het deurblad is het object (of kind) met een klink ("Pommel"/"handle") eronder.
    /// </summary>
    public static VRDoor MakeDoor(GameObject go)
    {
        if (go == null) return null;
        var door = go.GetComponentInChildren<VRDoor>(true);
        if (door == null) door = go.GetComponentInParent<VRDoor>();
        if (door != null) return door;

        Transform leaf = null;
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
        {
            foreach (Transform c in t)
                if (IsHandle(c.name)) { leaf = t; break; }
            if (leaf != null) break;
        }
        if (leaf == null) leaf = go.transform;

        var handles = new List<XRSimpleInteractable>();
        foreach (Transform c in leaf)
        {
            if (!IsHandle(c.name)) continue;
            EnsureCollider(c.gameObject, 0.12f);
            var h = c.GetComponent<XRSimpleInteractable>();
            if (h == null) h = c.gameObject.AddComponent<XRSimpleInteractable>();
            handles.Add(h);
        }
        var rb = leaf.GetComponent<Rigidbody>();
        if (rb == null) rb = leaf.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        door = leaf.gameObject.AddComponent<VRDoor>();
        door.SetHandles(handles);
        if (handles.Count == 0) Debug.LogWarning($"[Home Together] Deur '{go.name}' heeft geen klink: hij gaat alleen vanzelf open/dicht.", go);
        return door;
    }

    /// <summary>Eerste object waarvan de naam dit stuk tekst bevat (en geen van de uitzonderingen).</summary>
    public static GameObject FindContains(string part, params string[] exclude)
    {
        foreach (var t in All())
        {
            if (IsStory(t)) continue;
            if (t.name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            bool skip = false;
            foreach (var e in exclude)
                if (t.name.IndexOf(e, System.StringComparison.OrdinalIgnoreCase) >= 0) skip = true;
            if (!skip) return t.gameObject;
        }
        return null;
    }

    /// <summary>Het object met dit stuk naam dat het dichtst bij een punt ligt.</summary>
    public static GameObject FindNearest(string part, Vector3 near, float maxDistance)
    {
        GameObject best = null;
        float bestD = maxDistance;
        foreach (var t in All())
        {
            if (IsStory(t) || t.name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            float d = Vector3.Distance(Center(t.gameObject), near);
            if (d < bestD) { bestD = d; best = t.gameObject; }
        }
        return best;
    }

    /// <summary>Objecten die de game loop zelf maakte, of de markers, tellen niet mee bij het zoeken.</summary>
    static bool IsStory(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
            if (p.GetComponent<HomeTogetherGame>() != null) return true;
        return false;
    }

    /// <summary>Marker "Story_..." als die bestaat, anders de standaardplek.</summary>
    public static Vector3 Marker(string name, Vector3 fallback)
    {
        var t = MarkerTransform(name);
        return t != null ? t.position : fallback;
    }

    public static Transform MarkerTransform(string name)
    {
        foreach (var m in Object.FindObjectsByType<StoryMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (m.name == name) return m.transform;
        var go = GameObject.Find(name);
        return go != null ? go.transform : null;
    }

    public static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
        var b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    public static Vector3 Center(GameObject go) => Bounds(go).center;

    /// <summary>Zet een BoxCollider rond alle renderers (lokaal), als er nog geen collider is.</summary>
    public static void EnsureCollider(GameObject go, float minSize = 0.1f)
    {
        if (go.GetComponentInChildren<Collider>() != null) return;
        var b = Bounds(go);
        var box = go.AddComponent<BoxCollider>();
        box.center = go.transform.InverseTransformPoint(b.center);
        Vector3 s = go.transform.lossyScale;
        Vector3 size = b.size;
        for (int i = 0; i < 3; i++)
            size[i] = Mathf.Max(size[i], minSize) / Mathf.Max(Mathf.Abs(s[i]), 0.0001f);
        box.size = size;
    }

    public static T AddInteractable<T>(GameObject go) where T : XRBaseInteractable
    {
        var existing = go.GetComponentInChildren<T>(true);
        if (existing != null) return existing;
        EnsureCollider(go);
        return go.AddComponent<T>();
    }

    public static GameObject Block(string name, PrimitiveType type, Vector3 position, Vector3 size, Color color,
        Transform parent, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.localScale = size;
        if (!collider) Object.Destroy(go.GetComponent<Collider>());
        Paint(go, color);
        return go;
    }

    public static void Paint(GameObject go, Color color)
    {
        var r = go.GetComponent<Renderer>();
        if (r == null) return;
        var m = r.material; // standaardmateriaal van de render pipeline
        m.color = color;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
    }

    public static Light MakeLight(string name, Vector3 position, LightType type, Color color, float intensity,
        float range, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        var l = go.AddComponent<Light>();
        l.type = type;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        return l;
    }

    public static void SetActive(GameObject go, bool on)
    {
        if (go != null) go.SetActive(on);
    }

    /// <summary>Laat een personage (hond, inbreker) naar een plek lopen. Draait mee, animatie loopt alleen tijdens het stappen.</summary>
    public static IEnumerator Walk(Transform who, Vector3 target, float speed, Animator anim = null)
    {
        if (who == null) yield break;
        if (anim != null) anim.speed = 1f;
        target.y = who.position.y;
        while ((who.position - target).sqrMagnitude > 0.0025f)
        {
            Vector3 dir = target - who.position;
            if (dir.sqrMagnitude > 0.0001f)
                who.rotation = Quaternion.Slerp(who.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 8f);
            who.position = Vector3.MoveTowards(who.position, target, speed * Time.deltaTime);
            yield return null;
        }
        if (anim != null) anim.speed = 0f;
    }

    public static void Face(Transform who, Vector3 point)
    {
        if (who == null) return;
        var d = Flat(point - who.position);
        if (d.sqrMagnitude > 0.0001f) who.rotation = Quaternion.LookRotation(d);
    }
}
