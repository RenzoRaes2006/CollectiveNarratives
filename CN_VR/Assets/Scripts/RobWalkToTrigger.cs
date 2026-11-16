using UnityEngine;

/// <summary>
/// Moves Rob forward until he enters the assigned trigger box (where he stops).
/// The surprised animation (Animator trigger "Check") plays when the flashlight
/// beam shines on his face.
/// </summary>
public class RobWalkToTrigger : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Collider destinationTrigger;
    [SerializeField, Min(0f)] private float moveSpeed = 1f;
    [SerializeField] private string surprisedTriggerName = "Check";

    [Header("Flashlight")]
    [Tooltip("The flashlight's Spot Light. Found automatically if left empty.")]
    [SerializeField] private Light flashlight;
    [Tooltip("Rob's face/head. Uses the Animator's head bone if left empty.")]
    [SerializeField] private Transform face;
    [Tooltip("Face height above Rob's feet, used when no head bone is found.")]
    [SerializeField] private float faceHeight = 1.6f;
    [Tooltip("Narrows the beam: 1 = full spot angle, 0.5 = only the centre half.")]
    [SerializeField, Range(0.1f, 1f)] private float beamWidth = 0.8f;

    private bool hasReachedTrigger;
    private bool hasBeenLit;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (face == null && animator != null && animator.isHuman)
            face = animator.GetBoneTransform(HumanBodyBones.Head);

        if (flashlight == null)
            flashlight = FindFlashlight();

        Collider npcCollider = GetComponent<Collider>();
        if (npcCollider != null && npcCollider.isTrigger)
            Debug.LogWarning("Rob needs a non-trigger Collider so he can enter the destination trigger.", this);
    }

    private void Update()
    {
        if (!hasReachedTrigger && !hasBeenLit)
            transform.position += transform.forward * (moveSpeed * Time.deltaTime);

        if (!hasBeenLit && IsFaceLit())
        {
            hasBeenLit = true;

            if (animator == null)
            {
                Debug.LogError("No Animator found on Rob or his children.", this);
                return;
            }

            animator.SetTrigger(surprisedTriggerName);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (destinationTrigger != null && other == destinationTrigger)
            hasReachedTrigger = true;
    }

    private Vector3 FacePosition =>
        face != null ? face.position : transform.position + Vector3.up * faceHeight;

    private bool IsFaceLit()
    {
        if (flashlight == null)
            flashlight = FindFlashlight();
        if (flashlight == null || !flashlight.enabled || !flashlight.gameObject.activeInHierarchy)
            return false;

        Transform lightTransform = flashlight.transform;
        Vector3 toFace = FacePosition - lightTransform.position;
        float distance = toFace.magnitude;
        if (distance > flashlight.range || distance < 0.001f)
            return false;

        float halfAngle = flashlight.spotAngle * 0.5f * beamWidth;
        if (Vector3.Angle(lightTransform.forward, toFace) > halfAngle)
            return false;

        // Make sure nothing (a wall, a door...) blocks the beam.
        RaycastHit[] hits = Physics.RaycastAll(lightTransform.position, toFace / distance, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            Transform t = hit.collider.transform;
            if (t.IsChildOf(transform) || IsPartOfFlashlight(t))
                continue;
            return false;
        }
        return true;
    }

    private bool IsPartOfFlashlight(Transform t)
    {
        Rigidbody body = flashlight.GetComponentInParent<Rigidbody>();
        return body != null && t.IsChildOf(body.transform);
    }

    private static Light FindFlashlight()
    {
        foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l.type != LightType.Spot)
                continue;
            for (Transform t = l.transform; t != null; t = t.parent)
                if (t.name.ToLower().Contains("flashlight"))
                    return l;
        }
        return null;
    }
}
