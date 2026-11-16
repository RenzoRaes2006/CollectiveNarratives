using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Zoekt bij het starten alle deuren in de scène (op naam) en maakt ze in VR bedienbaar
/// via hun klink. Zo hoeft de grote House-prefab zelf niet aangepast te worden.
/// </summary>
public class VRDoorSetup : MonoBehaviour
{
    [Tooltip("Namen van de deurbladen. Hun pivot moet op het scharnier liggen.")]
    [SerializeField] private string[] doorNames = { "Interior_Door", "Exterior_Door" };

    [Tooltip("Kinderen van de deur waarvan de naam een van deze woorden bevat, worden de klink.")]
    [SerializeField] private string[] handleKeywords = { "Pommel", "handle" };

    [Tooltip("Minimale grootte (meter) van de grijpzone rond de klink, zodat je hem makkelijk raakt.")]
    [SerializeField, Min(0.02f)] private float minHandleSize = 0.12f;

    private void Awake()
    {
        var all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int doors = 0;
        foreach (var t in all)
        {
            if (System.Array.IndexOf(doorNames, t.name) < 0 || t.GetComponent<VRDoor>() != null)
                continue;

            var handles = new List<XRSimpleInteractable>();
            foreach (Transform child in t)
            {
                if (IsHandle(child.name))
                    handles.Add(MakeHandle(child));
            }
            if (handles.Count == 0)
            {
                Debug.LogWarning($"VRDoorSetup: '{t.name}' heeft geen klink gevonden, overgeslagen.", t);
                continue;
            }

            // Kinematic rigidbody: de deur beweegt via script, maar blijft de speler tegenhouden.
            var rb = t.GetComponent<Rigidbody>();
            if (rb == null)
                rb = t.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var door = t.gameObject.AddComponent<VRDoor>();
            door.SetHandles(handles);
            doors++;
        }
        Debug.Log($"VRDoorSetup: {doors} deuren bedienbaar gemaakt via de klink.");
    }

    private bool IsHandle(string objectName)
    {
        foreach (var k in handleKeywords)
            if (objectName.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private XRSimpleInteractable MakeHandle(Transform handle)
    {
        // Grijpzone rond het klink-mesh (lokale bounds), met een minimale grootte.
        var box = handle.GetComponent<BoxCollider>();
        if (box == null)
            box = handle.gameObject.AddComponent<BoxCollider>();

        var filter = handle.GetComponent<MeshFilter>();
        Bounds b = filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.zero);
        Vector3 scale = handle.lossyScale;
        Vector3 size = b.size;
        for (int i = 0; i < 3; i++)
        {
            float s = Mathf.Max(Mathf.Abs(scale[i]), 0.0001f);
            size[i] = Mathf.Max(size[i], minHandleSize / s);
        }
        box.center = b.center;
        box.size = size;
        // Geen trigger: de interactors van de XR rig negeren triggers (Physics Trigger Interaction = Ignore).
        box.isTrigger = false;

        var interactable = handle.GetComponent<XRSimpleInteractable>();
        if (interactable == null)
            interactable = handle.gameObject.AddComponent<XRSimpleInteractable>();
        return interactable;
    }
}
