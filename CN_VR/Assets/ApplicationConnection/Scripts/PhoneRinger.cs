using UnityEngine;

// Placeholder-telefoon: trilt, knippert rood en maakt een piepgeluid zolang hij rinkelt.
// Geen audiobestand nodig: de beltoon wordt in code gemaakt. Later vervangen door een echt model en geluid.
[RequireComponent(typeof(AudioSource))]
public class PhoneRinger : MonoBehaviour
{
    public Color idleColor = new Color(0.15f, 0.15f, 0.15f);
    public Color ringColor = new Color(1f, 0.2f, 0.2f);
    public Color pickedUpColor = new Color(0.2f, 0.9f, 0.3f);

    public bool IsRinging { get; private set; }

    AudioSource audioSource;
    Renderer rend;
    Vector3 startPos;
    Quaternion startRot;
    bool pickedUp;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.clip = MakeRingClip();
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f; // 3D-geluid: Kevin hoort waar de telefoon is
        rend = GetComponentInChildren<Renderer>();
        startPos = transform.localPosition;
        startRot = transform.localRotation;
        SetColor(idleColor);
    }

    public void Ring()
    {
        ResetPhone();
        IsRinging = true;
        audioSource.Play();
    }

    public void StopRinging()
    {
        IsRinging = false;
        audioSource.Stop();
        transform.localPosition = startPos;
        transform.localRotation = startRot;
        SetColor(idleColor);
    }

    // Opnemen: stil, groen en de hoorn gaat omhoog.
    public void PickUp()
    {
        StopRinging();
        pickedUp = true;
        SetColor(pickedUpColor);
    }

    public void ResetPhone()
    {
        pickedUp = false;
        StopRinging();
    }

    void Update()
    {
        if (IsRinging)
        {
            float t = Time.time;
            transform.localPosition = startPos + new Vector3(Mathf.Sin(t * 60f) * 0.01f, 0f, 0f);
            transform.localRotation = startRot * Quaternion.Euler(0f, 0f, Mathf.Sin(t * 45f) * 4f);
            SetColor(Mathf.Repeat(t, 0.5f) < 0.25f ? ringColor : idleColor);
        }
        else if (pickedUp)
        {
            transform.localPosition = Vector3.Lerp(transform.localPosition, startPos + Vector3.up * 0.3f, Time.deltaTime * 5f);
        }
    }

    void SetColor(Color c)
    {
        if (rend != null) rend.material.color = c;
    }

    // Klassieke beltoon: 2x kort tuut-tuut, dan stilte. Loopt in een lus.
    static AudioClip MakeRingClip()
    {
        const int rate = 44100;
        const float length = 2f;
        var data = new float[(int)(rate * length)];
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / rate;
            bool on = (t < 0.4f) || (t > 0.6f && t < 1.0f);
            data[i] = on ? 0.25f * Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 440f * t)) * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 20f * t)) : 0f;
        }
        var clip = AudioClip.Create("Beltoon", data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
