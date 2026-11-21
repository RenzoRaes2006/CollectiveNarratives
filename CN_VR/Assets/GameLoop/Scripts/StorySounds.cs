using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Alle geluiden van Home Together. Zolang er geen echte opnames zijn, maakt deze klasse
/// zelf eenvoudige geluiden (rinkelen, kraken, krabben, blaffen, sirene...).
/// Een echte opname zet je in de Inspector van HomeTogetherGame bij "Sound Overrides":
/// vul de naam in (bv. "marley") en sleep je AudioClip erin.
///
/// Namen: ring, tick, hum, engine, slam, clunk, step, snowstep, creak, roll, scratch, whine,
/// bark, growl, whisper, wind, siren, jingle, breath, marley, mum, magnet, gate, snowslide, glass.
/// </summary>
public static class StorySounds
{
    [Serializable]
    public class Override
    {
        public string sound;
        public AudioClip clip;
    }

    const int Rate = 22050;
    const float Tau = Mathf.PI * 2f;

    static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
    static readonly Dictionary<string, AudioClip> overrides = new Dictionary<string, AudioClip>();

    public static void SetOverrides(List<Override> list)
    {
        overrides.Clear();
        if (list == null) return;
        foreach (var o in list)
            if (o != null && o.clip != null && !string.IsNullOrEmpty(o.sound))
                overrides[o.sound.Trim().ToLowerInvariant()] = o.clip;
    }

    public static AudioClip Get(string name)
    {
        name = name.ToLowerInvariant();
        if (overrides.TryGetValue(name, out var clip)) return clip;
        if (!cache.TryGetValue(name, out clip) || clip == null)
        {
            // Echte opname: Assets/GameLoop/Resources/HomeTogetherSounds/<naam>.wav (of .mp3/.ogg). Anders plaatshouder.
            clip = Resources.Load<AudioClip>("HomeTogetherSounds/" + name);
            if (clip == null) clip = Make(name);
            cache[name] = clip;
        }
        return clip;
    }

    /// <summary>Speelt een 3D-geluid op een plek in de wereld (Spatial Blend 1).</summary>
    public static AudioSource Play(string name, Vector3 position, float volume = 1f, bool loop = false,
        Transform parent = null, float pitch = 1f)
    {
        var clip = Get(name);
        if (clip == null) return null;
        var go = new GameObject("Sound " + name);
        if (parent != null) go.transform.SetParent(parent, true);
        go.transform.position = position;
        var s = go.AddComponent<AudioSource>();
        s.clip = clip;
        s.volume = volume;
        s.loop = loop;
        s.pitch = pitch;
        s.spatialBlend = 1f;
        s.rolloffMode = AudioRolloffMode.Linear;
        s.minDistance = 1f;
        s.maxDistance = 40f;
        s.dopplerLevel = 0f;
        s.playOnAwake = false;
        s.Play();
        if (!loop) Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.25f);
        return s;
    }

    public static void Stop(AudioSource s)
    {
        if (s != null) Object.Destroy(s.gameObject);
    }

    // ---------- Synthese ----------

    static float[] Buf(float seconds) => new float[Mathf.CeilToInt(seconds * Rate)];

    static AudioClip Make(string name)
    {
        var r = new System.Random(name.GetHashCode());
        float N() => (float)(r.NextDouble() * 2.0 - 1.0);
        float[] d;
        float lp = 0f, lp2 = 0f, prev = 0f, phase = 0f;

        switch (name)
        {
            case "ring": // oude wandtelefoon: twee belletjes, dan stilte
                d = Buf(3f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    bool on = t < 0.4f || (t > 0.6f && t < 1.0f);
                    float am = Mathf.Sin(Tau * 20f * t) > 0f ? 1f : 0.35f;
                    d[i] = on ? am * 0.3f * (Mathf.Sin(Tau * 1000f * t) + Mathf.Sin(Tau * 1270f * t)) : 0f;
                }
                break;

            case "tick": // tik-tak van de klok (lus van 1 s)
                d = Buf(1f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    float a = t < 0.5f ? t : t - 0.5f;
                    float vol = t < 0.5f ? 0.8f : 0.5f;
                    d[i] = vol * (N() * Mathf.Exp(-a * 700f) + 0.6f * Mathf.Sin(Tau * 2200f * a) * Mathf.Exp(-a * 300f));
                }
                break;

            case "hum": // koelkast
                d = Buf(1f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    d[i] = 0.25f * Mathf.Sin(Tau * 50f * t) + 0.15f * Mathf.Sin(Tau * 100f * t) + 0.05f * Mathf.Sin(Tau * 150f * t);
                }
                break;

            case "engine": // busje (lus van 2 s)
                d = Buf(2f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    float saw = ((t * 35f) % 1f) * 2f - 1f;
                    lp += 0.06f * (saw + 0.4f * N() - lp);
                    d[i] = 0.7f * lp;
                }
                break;

            case "slam": // deur die dichtslaat / openvliegt
                d = Buf(0.8f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    lp += 0.3f * (N() - lp);
                    d[i] = Mathf.Sin(Tau * 55f * t) * Mathf.Exp(-t * 8f) + 0.8f * lp * Mathf.Exp(-t * 25f);
                }
                break;

            case "clunk": // stroom valt uit / hendel
                d = Buf(0.6f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    d[i] = Mathf.Sin(Tau * 90f * t) * Mathf.Exp(-t * 12f) + 0.6f * N() * Mathf.Exp(-t * 60f);
                }
                break;

            case "step": // voetstap op hout
                d = Buf(0.3f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    lp += 0.1f * (N() - lp);
                    d[i] = 0.9f * Mathf.Sin(Tau * 70f * t) * Mathf.Exp(-t * 25f) + 0.6f * lp * Mathf.Exp(-t * 30f);
                }
                break;

            case "snowstep": // voetstap in de sneeuw
                d = Buf(0.35f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    float n = r.NextDouble() < 0.25 ? N() : 0f;
                    lp += 0.4f * (n - lp);
                    d[i] = 0.9f * lp * Mathf.Sin(Mathf.PI * t / 0.35f);
                }
                break;

            case "creak": // plafond / vloer kraakt
            case "gate": // tuinhekje
            {
                float len = name == "gate" ? 1.8f : 1.0f;
                float f0 = name == "gate" ? 300f : 150f;
                d = Buf(len);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    float f = f0 - 60f * t / len + 30f * Mathf.Sin(Tau * 1.3f * t) + 15f * N();
                    phase = (phase + f / Rate) % 1f;
                    float saw = phase * 2f - 1f;
                    float am = 0.6f + 0.4f * Mathf.Sin(Tau * 23f * t);
                    lp += 0.25f * (saw - lp);
                    d[i] = 0.6f * lp * am * Mathf.Sin(Mathf.PI * t / len);
                }
                break;
            }

            case "roll": // kerstbal valt en rolt
                d = Buf(1.6f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    lp += 0.05f * (N() - lp);
                    float tink = 0.5f * Mathf.Sin(Tau * 3100f * t) * Mathf.Exp(-t * 40f);
                    d[i] = tink + 1.6f * lp * (0.6f + 0.4f * Mathf.Sin(Tau * 14f * t)) * (1f - t / 1.6f);
                }
                break;

            case "scratch": // hond krabt aan de deur (lus)
                d = Buf(1.5f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    float p = (t * 6f) % 1f;
                    float g = p < 0.5f ? Mathf.Sin(Mathf.PI * p / 0.5f) : 0f;
                    float n = N();
                    d[i] = 0.5f * (n - prev) * g;
                    prev = n;
                }
                break;

            case "whine": // hond jankt
                d = Buf(1.2f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    float f = 800f + 300f * Mathf.Sin(Mathf.PI * t / 1.2f) + 20f * Mathf.Sin(Tau * 6f * t);
                    phase = (phase + f / Rate) % 1f;
                    d[i] = 0.35f * Mathf.Sin(Tau * phase) * Mathf.Sin(Mathf.PI * t / 1.2f);
                }
                break;

            case "bark": // blaf
                d = Buf(0.4f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    float f = Mathf.Max(150f, 480f - 600f * t);
                    phase = (phase + f / Rate) % 1f;
                    float env = t < 0.02f ? t / 0.02f : Mathf.Exp(-(t - 0.02f) * 9f);
                    lp += 0.35f * ((phase * 2f - 1f) * 0.7f + 0.3f * N() - lp);
                    d[i] = 0.9f * lp * env;
                }
                break;

            case "growl":
                d = Buf(1.0f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    phase = (phase + 90f / Rate) % 1f;
                    lp += 0.15f * ((phase * 2f - 1f) * (0.6f + 0.4f * Mathf.Sin(Tau * 30f * t)) - lp);
                    d[i] = 0.8f * lp * Mathf.Sin(Mathf.PI * t);
                }
                break;

            case "whisper": // "Empty. Nobody's home."
                d = Buf(1.8f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    float n = N();
                    float s = Mathf.Max(0f, Mathf.Sin(Tau * 4.5f * t));
                    d[i] = 0.35f * (n - prev) * s * s * Mathf.Sin(Mathf.PI * t / 1.8f);
                    prev = n;
                }
                break;

            case "wind": // koude wind (lus)
                d = Buf(4f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    float a = 0.02f + 0.015f * Mathf.Sin(Tau * 0.5f * t);
                    lp += a * (N() - lp);
                    lp2 += a * (lp - lp2);
                    d[i] = 2.5f * lp2;
                }
                break;

            case "siren": // politie in de verte (lus)
                d = Buf(4f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    float f = 750f + 350f * Mathf.Sin(Tau * 0.25f * t);
                    phase = (phase + f / Rate) % 1f;
                    d[i] = 0.3f * Mathf.Sin(Tau * phase) + 0.1f * Mathf.Sin(Tau * 2f * phase);
                }
                break;

            case "jingle": // zacht kerstdeuntje uit de boom
            {
                float[] notes = { 659.25f, 659.25f, 659.25f, 0f, 659.25f, 659.25f, 659.25f, 0f, 659.25f, 783.99f, 523.25f, 587.33f, 659.25f, 0f, 0f, 0f };
                float beat = 0.25f;
                d = Buf(notes.Length * beat);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    int k = Mathf.Min(notes.Length - 1, (int)(t / beat));
                    float tn = t - k * beat;
                    float f = notes[k];
                    d[i] = f <= 0f ? 0f : 0.3f * (Mathf.Sin(Tau * f * tn) + 0.4f * Mathf.Sin(Tau * 2f * f * tn)) * Mathf.Exp(-tn * 6f);
                }
                break;
            }

            case "breath": // inbreker ademt voor de kast (lus)
                d = Buf(3f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    lp += 0.08f * (N() - lp);
                    float e = Mathf.Sin(Mathf.PI * (t % 1.5f) / 1.5f);
                    d[i] = 1.2f * lp * e * e;
                }
                break;

            case "marley": // plaatshouder voor Marleys stem door de hoorn (neem een echte zin op!)
                d = Buf(4f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    float s = Mathf.Max(0f, Mathf.Sin(Tau * 3.3f * t));
                    phase = (phase + (130f + 25f * Mathf.Sin(Tau * 0.7f * t)) / Rate) % 1f;
                    lp += 0.2f * ((phase * 2f - 1f) - lp);
                    d[i] = 0.45f * (lp - prev) * 4f * s;
                    prev = lp;
                }
                break;

            case "mum": // plaatshouder voor "KEVIN!"
                d = Buf(1.0f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    float f = t < 0.35f ? 260f : 330f;
                    bool on = t < 0.3f || (t > 0.4f && t < 0.95f);
                    phase = (phase + f / Rate) % 1f;
                    lp += 0.25f * ((phase * 2f - 1f) - lp);
                    d[i] = on ? 0.8f * lp : 0f;
                }
                break;

            case "magnet": // magneetje valt van de koelkast
                d = Buf(0.2f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    d[i] = 0.5f * Mathf.Sin(Tau * 2500f * t) * Mathf.Exp(-t * 50f) + 0.4f * N() * Mathf.Exp(-t * 300f);
                }
                break;

            case "snowslide": // sneeuw schuift van de dakgoot
                d = Buf(1.6f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    lp += 0.04f * (N() - lp);
                    float env = t < 0.3f ? t / 0.3f : Mathf.Max(0f, 1f - (t - 0.3f) / 1.1f);
                    float thump = t > 1.3f ? Mathf.Sin(Tau * 60f * (t - 1.3f)) * Mathf.Exp(-(t - 1.3f) * 15f) : 0f;
                    d[i] = 2f * lp * env + 0.7f * thump;
                }
                break;

            case "glass": // raam knalt open
                d = Buf(1.0f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    lp += 0.3f * (N() - lp);
                    d[i] = 0.8f * Mathf.Sin(Tau * 60f * t) * Mathf.Exp(-t * 10f) + 0.6f * lp * Mathf.Exp(-t * 20f)
                         + 0.2f * Mathf.Sin(Tau * 3300f * t) * Mathf.Exp(-t * 6f);
                }
                break;

            default:
                Debug.LogWarning("[Home Together] Onbekend geluid: " + name);
                return null;
        }

        var clip = AudioClip.Create("story_" + name, d.Length, 1, Rate, false);
        clip.SetData(d, 0);
        return clip;
    }
}
