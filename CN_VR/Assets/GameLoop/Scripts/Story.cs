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
        // Heeft een kind dezelfde naam als zijn parent (bv. Closet/Closet), neem dan de parent: daar zit alles onder.
        while (best != null && best.transform.parent != null && string.Equals(best.transform.parent.name.Trim(), n, System.StringComparison.OrdinalIgnoreCase))
            best = best.transform.parent.gameObject;
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
        if (!collider) { if (Application.isPlaying) Object.Destroy(go.GetComponent<Collider>()); else Object.DestroyImmediate(go.GetComponent<Collider>()); }
        Paint(go, color);
        return go;
    }

    public static void Paint(GameObject go, Color color)
    {
        var r = go.GetComponent<Renderer>();
        if (r == null) return;
        // standaardmateriaal van de render pipeline (in de Editor buiten Play een eigen kopie, zonder lek-waarschuwing)
        var m = Application.isPlaying ? r.material : (r.sharedMaterial = new Material(r.sharedMaterial));
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

    /// <summary>
    /// Laat een personage (hond, inbreker) naar een plek lopen, om muren en meubels heen (er is geen NavMesh,
    /// dus de route wordt op een raster gezocht met de colliders in de scene). Lukt dat niet, dan rechtdoor.
    /// Draait mee, animatie loopt alleen tijdens het stappen.
    /// </summary>
    /// <param name="maxDetour">Is de route om de muren heen langer dan zoveel keer de rechte afstand (of is er geen route),
    /// dan blijft het personage staan in plaats van een rare omweg (bv. terug naar buiten) of door een muur.
    /// 0 = altijd lopen (zonder route: rechtdoor).</param>
    public static IEnumerator Walk(Transform who, Vector3 target, float speed, Animator anim = null, float radius = 0.2f, float maxDetour = 0f)
    {
        if (who == null) yield break;
        target.y = who.position.y;
        var path = radius < 0f ? new List<Vector3> { target } : FindPath(who, who.position, target, radius); // radius < 0 = rechtdoor
        if (maxDetour > 0f)
        {
            float straight = FlatDist(who.position, target);
            float len = 0f;
            Vector3 at = who.position;
            if (path != null) foreach (var p in path) { len += FlatDist(at, p); at = p; }
            if (path == null || (straight > 0.5f && len > straight * maxDetour))
            {
                Debug.Log($"[Home Together] {who.name}: geen goede route naar {target} (omweg {len:0.0} m voor {straight:0.0} m), blijft staan.");
                yield break;
            }
        }
        if (path == null) path = new List<Vector3> { target };
        if (anim != null) { anim.speed = 1f; anim.applyRootMotion = false; } // root motion zou het personage terugduwen
        // Tijdens het lopen botst het personage niet met de speler (de speler duwt hem anders terug).
        var cols = new List<Collider>();
        foreach (var c in who.GetComponentsInChildren<Collider>()) if (c.enabled && !c.isTrigger) { c.enabled = false; cols.Add(c); }
        foreach (var point in path)
        {
            var p = new Vector3(point.x, who.position.y, point.z);
            while (who != null && (who.position - p).sqrMagnitude > 0.0025f)
            {
                Vector3 dir = p - who.position;
                if (dir.sqrMagnitude > 0.0001f)
                    who.rotation = Quaternion.Slerp(who.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 8f);
                who.position = Vector3.MoveTowards(who.position, p, speed * Time.deltaTime);
                yield return null;
            }
        }
        foreach (var c in cols) if (c != null) c.enabled = true;
        if (anim != null) anim.speed = 0f;
    }

    const float Cell = 0.25f;

    /// <summary>Staat er iets in de weg op deze plek (een staande capsule tussen 0,25 en 0,9 m boven de vloer)?</summary>
    static bool Blocked(Transform who, Vector3 p, float radius)
    {
        var hits = Physics.OverlapCapsule(p + Vector3.up * (0.25f + radius), p + Vector3.up * 0.9f, radius, ~0, QueryTriggerInteraction.Ignore);
        foreach (var c in hits)
        {
            if (c.transform.IsChildOf(who) || c is CharacterController) continue;
            if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue; // losse spullen duwen we weg
            if (c.GetComponentInParent<XROrigin>() != null) continue; // de speler zelf
            return true;
        }
        return false;
    }

    static bool ClearLine(Transform who, Vector3 a, Vector3 b, float radius)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 0.01f) return true;
        int steps = Mathf.CeilToInt(len / (Cell * 0.5f));
        for (int i = 1; i <= steps; i++)
            if (Blocked(who, Vector3.Lerp(a, b, i / (float)steps), radius)) return false;
        return true;
    }

    /// <summary>Route om obstakels heen (A* op een raster van 25 cm). Null als er geen route is.</summary>
    public static List<Vector3> FindPath(Transform who, Vector3 from, Vector3 to, float radius)
    {
        to.y = from.y;
        if (ClearLine(who, from, to, radius)) return new List<Vector3> { to };

        float margin = 8f;
        float minX = Mathf.Min(from.x, to.x) - margin, minZ = Mathf.Min(from.z, to.z) - margin;
        int w = Mathf.CeilToInt((Mathf.Abs(from.x - to.x) + 2 * margin) / Cell) + 1;
        int h = Mathf.CeilToInt((Mathf.Abs(from.z - to.z) + 2 * margin) / Cell) + 1;
        if (w * h > 120000) return null;
        Vector3 Pos(int x, int z) => new Vector3(minX + x * Cell, from.y, minZ + z * Cell);
        int Ix(Vector3 p) => Mathf.Clamp(Mathf.RoundToInt((p.x - minX) / Cell), 0, w - 1) + Mathf.Clamp(Mathf.RoundToInt((p.z - minZ) / Cell), 0, h - 1) * w;

        var blocked = new sbyte[w * h]; // 0 = onbekend, 1 = vrij, 2 = bezet
        bool Free(int i)
        {
            if (blocked[i] == 0) blocked[i] = (sbyte)(Blocked(who, Pos(i % w, i / w), radius) ? 2 : 1);
            return blocked[i] == 1;
        }

        int start = Ix(from), goal = Ix(to);
        if (!Free(goal))
        {
            // Doel zit in een meubel (bv. onder de kerstboom): neem de dichtstbijzijnde vrije plek.
            int best = -1;
            float bestD = float.MaxValue;
            int gx = goal % w, gz = goal / w, r = Mathf.CeilToInt(1.5f / Cell);
            for (int z = Mathf.Max(0, gz - r); z <= Mathf.Min(h - 1, gz + r); z++)
                for (int x = Mathf.Max(0, gx - r); x <= Mathf.Min(w - 1, gx + r); x++)
                {
                    float d = (x - gx) * (x - gx) + (z - gz) * (z - gz);
                    if (d < bestD && Free(x + z * w)) { bestD = d; best = x + z * w; }
                }
            if (best < 0) return null;
            goal = best;
            to = Pos(goal % w, goal / w);
        }
        blocked[start] = 1; // het personage staat er al
        {
            // Staat het personage half in een meubel of muur (bv. net binnen de deur), laat hem er dan uit stappen:
            // de cellen vlak rond het startpunt tellen als vrij.
            int sx = start % w, sz = start / w, er = Mathf.CeilToInt(0.6f / Cell);
            for (int z = Mathf.Max(0, sz - er); z <= Mathf.Min(h - 1, sz + er); z++)
                for (int x = Mathf.Max(0, sx - er); x <= Mathf.Min(w - 1, sx + er); x++)
                    if ((x - sx) * (x - sx) + (z - sz) * (z - sz) <= er * er) blocked[x + z * w] = 1;
        }

        var cost = new Dictionary<int, float> { [start] = 0f };
        var came = new Dictionary<int, int>();
        var open = new List<int> { start };
        float H(int i) => Vector2.Distance(new Vector2(i % w, i / w), new Vector2(goal % w, goal / w));
        int guard = 0;
        while (open.Count > 0 && guard++ < 40000)
        {
            int bi = 0;
            for (int k = 1; k < open.Count; k++)
                if (cost[open[k]] + H(open[k]) < cost[open[bi]] + H(open[bi])) bi = k;
            int cur = open[bi];
            open.RemoveAt(bi);
            if (cur == goal) break;
            int cx = cur % w, cz = cur / w;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = cx + dx, nz = cz + dz;
                    if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                    int n = nx + nz * w;
                    if (!Free(n)) continue;
                    if (dx != 0 && dz != 0 && (!Free(cx + dx + cz * w) || !Free(cx + (cz + dz) * w))) continue; // geen hoeken afsnijden
                    float c = cost[cur] + (dx != 0 && dz != 0 ? 1.414f : 1f);
                    if (cost.TryGetValue(n, out var old) && old <= c) continue;
                    cost[n] = c;
                    came[n] = cur;
                    if (!open.Contains(n)) open.Add(n);
                }
        }
        if (!came.ContainsKey(goal)) return null;

        var cells = new List<Vector3>();
        for (int i = goal; i != start; i = came[i]) cells.Add(Pos(i % w, i / w));
        cells.Reverse();
        cells[cells.Count - 1] = to;

        // Rechte stukken samennemen, zodat hij niet zigzagt.
        var path = new List<Vector3>();
        Vector3 at = from;
        int idx = 0;
        while (idx < cells.Count)
        {
            int far = idx;
            for (int j = cells.Count - 1; j > idx; j--)
                if (ClearLine(who, at, cells[j], radius)) { far = j; break; }
            path.Add(cells[far]);
            at = cells[far];
            idx = far + 1;
        }
        return path;
    }

    public static void Face(Transform who, Vector3 point)
    {
        if (who == null) return;
        var d = Flat(point - who.position);
        if (d.sqrMagnitude > 0.0001f) who.rotation = Quaternion.LookRotation(d);
    }
}
