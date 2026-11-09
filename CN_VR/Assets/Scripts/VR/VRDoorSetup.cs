using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Zoekt bij het starten alle deuren in de scène (op naam) en maakt ze grabbaar in VR.
/// Zo hoeft de grote House-prefab zelf niet aangepast te worden.
/// </summary>
public class VRDoorSetup : MonoBehaviour
{
    [Tooltip("Namen van de deurbladen. Hun pivot moet op het scharnier liggen.")]
    [SerializeField] private string[] doorNames = { "Interior_Door", "Exterior_Door" };

    private void Awake()
    {
        var all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int count = 0;
        foreach (var t in all)
        {
            if (System.Array.IndexOf(doorNames, t.name) < 0 || t.GetComponent<VRDoor>() != null)
                continue;
            if (t.GetComponentInChildren<Collider>() == null)
            {
                Debug.LogWarning($"VRDoorSetup: '{t.name}' heeft geen collider, overgeslagen.", t);
                continue;
            }

            // Kinematic rigidbody: de deur beweegt via script, maar blijft de speler tegenhouden.
            var rb = t.GetComponent<Rigidbody>();
            if (rb == null)
                rb = t.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            if (t.GetComponent<XRSimpleInteractable>() == null)
                t.gameObject.AddComponent<XRSimpleInteractable>();
            t.gameObject.AddComponent<VRDoor>();
            count++;
        }
        Debug.Log($"VRDoorSetup: {count} deuren grabbaar gemaakt.");
    }
}
