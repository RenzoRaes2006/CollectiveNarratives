using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Sneeuw rond het huis van Kevin: een witte sneeuwlaag (met collider, je kunt erop lopen en valt niet meer
/// door de map), sneeuwhoopjes tegen de muren en vallende sneeuw die niet door het dak komt.
/// Wordt bij Play vanzelf gemaakt door HomeTogetherGame (instelling "Sneeuw rond het huis").
/// </summary>
public class HomeSnow : MonoBehaviour
{
    [Tooltip("Hoe ver (m) de sneeuw rond het huis ligt.")] public float margin = 14f;
    [Tooltip("Vallende sneeuw.")] public bool falling = true;
    [Tooltip("Aantal sneeuwvlokken tegelijk (lager = sneller op de Quest).")] public int flakes = 2500;
    [Tooltip("Aantal sneeuwhoopjes in de tuin.")] public int mounds = 30;

    static readonly Color SnowColor = new Color(0.9f, 0.93f, 1f);

    public static HomeSnow Create(Transform parent, Transform houseRoot, Vector3 fallbackCenter)
    {
        var go = new GameObject("Sneeuw rond het huis");
        go.transform.SetParent(parent, false);
        var snow = go.AddComponent<HomeSnow>();
        snow.Build(houseRoot, fallbackCenter);
        return snow;
    }

    void Build(Transform houseRoot, Vector3 fallbackCenter)
    {
        Bounds house = houseRoot != null ? Story.Bounds(houseRoot.gameObject) : new Bounds(fallbackCenter, new Vector3(12f, 6f, 12f));
        float groundY = GroundHeight(house);
        var ground = SnowMaterial();

        // Sneeuwlaag: vier stroken rond het huis (niet eronder, anders ligt er sneeuw in de kamers).
        float inMinX = house.min.x - 0.02f, inMaxX = house.max.x + 0.02f, inMinZ = house.min.z - 0.02f, inMaxZ = house.max.z + 0.02f;
        float outMinX = inMinX - margin, outMaxX = inMaxX + margin, outMinZ = inMinZ - margin, outMaxZ = inMaxZ + margin;
        Strip("Sneeuw noord", outMinX, outMaxX, inMaxZ, outMaxZ, groundY, ground);
        Strip("Sneeuw zuid", outMinX, outMaxX, outMinZ, inMinZ, groundY, ground);
        Strip("Sneeuw west", outMinX, inMinX, inMinZ, inMaxZ, groundY, ground);
        Strip("Sneeuw oost", inMaxX, outMaxX, inMinZ, inMaxZ, groundY, ground);

        // Sneeuwhoopjes tegen de muren, niet voor de deuren.
        var doors = new List<Vector3>();
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name.IndexOf("Door", System.StringComparison.OrdinalIgnoreCase) >= 0) doors.Add(t.position);
        var rnd = new System.Random(1225);
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        bool NearDoor(Vector3 p) { foreach (var d in doors) if (Story.FlatDist(d, p) < 1.6f) return true; return false; }
        void Mound(Vector3 p, float size)
        {
            if (NearDoor(p)) return;
            var m = Story.Block("Sneeuwhoop", PrimitiveType.Sphere, new Vector3(p.x, groundY, p.z),
                new Vector3(size * R(1f, 1.8f), size * R(0.25f, 0.45f), size * R(0.8f, 1.3f)), SnowColor, transform, false);
            m.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
            m.GetComponent<Renderer>().sharedMaterial = ground;
        }
        for (float x = inMinX; x <= inMaxX; x += 1.4f)
        {
            Mound(new Vector3(x + R(-0.3f, 0.3f), 0f, inMinZ - 0.3f), R(0.6f, 1.1f));
            Mound(new Vector3(x + R(-0.3f, 0.3f), 0f, inMaxZ + 0.3f), R(0.6f, 1.1f));
        }
        for (float z = inMinZ; z <= inMaxZ; z += 1.4f)
        {
            Mound(new Vector3(inMinX - 0.3f, 0f, z + R(-0.3f, 0.3f)), R(0.6f, 1.1f));
            Mound(new Vector3(inMaxX + 0.3f, 0f, z + R(-0.3f, 0.3f)), R(0.6f, 1.1f));
        }
        for (int i = 0; i < mounds; i++)
        {
            var p = new Vector3(R(outMinX, outMaxX), 0f, R(outMinZ, outMaxZ));
            if (p.x > inMinX - 1f && p.x < inMaxX + 1f && p.z > inMinZ - 1f && p.z < inMaxZ + 1f) continue;
            Mound(p, R(0.8f, 2f));
        }

        if (falling) Flakes(new Vector3((outMinX + outMaxX) / 2f, house.max.y + 5f, (outMinZ + outMaxZ) / 2f),
            new Vector3(outMaxX - outMinX, 0.1f, outMaxZ - outMinZ), groundY);
    }

    /// <summary>Hoogte van de grond buiten: mediaan van een paar stralen naar beneden net naast het huis.</summary>
    static float GroundHeight(Bounds house)
    {
        var ys = new List<float>();
        Vector3 c = house.center, e = house.extents + new Vector3(1.5f, 0f, 1.5f);
        var pts = new[]
        {
            new Vector3(c.x + e.x, 0f, c.z), new Vector3(c.x - e.x, 0f, c.z), new Vector3(c.x, 0f, c.z + e.z), new Vector3(c.x, 0f, c.z - e.z),
            new Vector3(c.x + e.x, 0f, c.z + e.z), new Vector3(c.x - e.x, 0f, c.z - e.z), new Vector3(c.x + e.x, 0f, c.z - e.z), new Vector3(c.x - e.x, 0f, c.z + e.z),
        };
        foreach (var p in pts)
            if (Physics.Raycast(new Vector3(p.x, house.max.y + 2f, p.z), Vector3.down, out var hit, house.size.y + 10f, ~0, QueryTriggerInteraction.Ignore))
                ys.Add(hit.point.y);
        if (ys.Count == 0) return house.min.y;
        ys.Sort();
        return ys[ys.Count / 2];
    }

    void Strip(string name, float minX, float maxX, float minZ, float maxZ, float groundY, Material mat)
    {
        if (maxX - minX < 0.05f || maxZ - minZ < 0.05f) return;
        // 0,3 m dik, met de bovenkant net boven de grond: je loopt erop en zakt er niet doorheen.
        var s = Story.Block(name, PrimitiveType.Cube, new Vector3((minX + maxX) / 2f, groundY + 0.03f - 0.15f, (minZ + maxZ) / 2f),
            new Vector3(maxX - minX, 0.3f, maxZ - minZ), SnowColor, transform);
        s.GetComponent<Renderer>().sharedMaterial = mat;
        s.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    static Material SnowMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(shader) { name = "Sneeuw" };
        m.color = SnowColor;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", SnowColor);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.15f);
        return m;
    }

    void Flakes(Vector3 top, Vector3 area, float groundY)
    {
        var go = new GameObject("Vallende sneeuw");
        go.transform.SetParent(transform, false);
        go.transform.position = top;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        float fallSpeed = 0.7f;
        float life = Mathf.Max(4f, (top.y - groundY) / fallSpeed + 1f);
        var main = ps.main;
        main.duration = 10f;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = life;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
        main.startColor = new Color(1f, 1f, 1f, 0.9f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = flakes;

        var emission = ps.emission;
        emission.rateOverTime = flakes / life;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = area;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        vel.y = new ParticleSystem.MinMaxCurve(-fallSpeed * 1.2f, -fallSpeed * 0.8f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.25f;
        noise.frequency = 0.3f;
        noise.quality = ParticleSystemNoiseQuality.Low;

        // Vlokken verdwijnen op het dak en op de grond: binnen sneeuwt het niet.
        var col = ps.collision;
        col.enabled = true;
        col.type = ParticleSystemCollisionType.World;
        col.mode = ParticleSystemCollisionMode.Collision3D;
        col.quality = ParticleSystemCollisionQuality.Low;
        col.lifetimeLoss = 1f;
        col.collidesWith = ~0;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = FlakeMaterial();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
    }

    static Material FlakeMaterial()
    {
        // Rond, zacht vlokje.
        var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 15.5f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d * d)));
            }
        tex.Apply();

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
        var m = new Material(shader) { name = "Sneeuwvlok" };
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        m.mainTexture = tex;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Surface"))
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        return m;
    }
}
