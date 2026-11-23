using UnityEngine;

/// <summary>
/// Moves Rob along the waypoints (children of the "Waypoints" object), or straight
/// forward if none are set, until he enters the assigned trigger box (where he stops).
/// The surprised animation (Animator trigger "Check") plays when the flashlight
/// beam shines on his face.
/// </summary>
public class RobWalkToTrigger : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Collider destinationTrigger;
    [SerializeField, Min(0f)] private float moveSpeed = 1f;
    [SerializeField] private string surprisedTriggerName = "Check";

    public enum PathMode { StopAtEnd, Loop, PingPong }

    [Header("Waypoints")]
    [Tooltip("Empty object whose children are the waypoints, visited in Hierarchy order. " +
             "Leave empty to just walk forward like before.")]
    [SerializeField] private Transform waypoints;
    [SerializeField] private PathMode pathMode = PathMode.StopAtEnd;
    [Tooltip("Seconds Rob waits at each waypoint.")]
    [SerializeField, Min(0f)] private float waitAtWaypoint = 0f;
    [Tooltip("How fast Rob turns towards the next waypoint (degrees per second).")]
    [SerializeField, Min(0f)] private float turnSpeed = 360f;
    [Tooltip("Distance at which a waypoint counts as reached.")]
    [SerializeField, Min(0.01f)] private float reachDistance = 0.1f;

    [Header("Ground / Stairs")]
    [Tooltip("Keep Rob's feet on the ground so he can walk up and down stairs and slopes.")]
    [SerializeField] private bool followGround = true;
    [Tooltip("Highest step Rob can walk up (metres).")]
    [SerializeField, Min(0f)] private float stepHeight = 0.4f;
    [Tooltip("How far down Rob looks for ground below his feet (metres).")]
    [SerializeField, Min(0f)] private float maxDrop = 1f;
    [Tooltip("How smoothly Rob moves up/down a step. Higher = snappier.")]
    [SerializeField, Min(1f)] private float stepSmoothing = 15f;
    [Tooltip("Layers that count as ground. Everything by default.")]
    [SerializeField] private LayerMask groundLayers = ~0;

    private int waypointIndex;
    private int direction = 1;
    private float waitTimer;
    private bool pathFinished;

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
        {
            if (waypoints != null && waypoints.childCount > 0)
                FollowWaypoints();
            else
                transform.position += transform.forward * (moveSpeed * Time.deltaTime);

            if (followGround)
                StickToGround();
        }

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

    private void FollowWaypoints()
    {
        if (pathFinished)
            return;

        if (waitTimer > 0f)
        {
            waitTimer -= Time.deltaTime;
            return;
        }

        Vector3 target = waypoints.GetChild(waypointIndex).position;
        target.y = transform.position.y; // stay on the ground, ignore waypoint height

        Vector3 toTarget = target - transform.position;
        if (toTarget.magnitude <= reachDistance)
        {
            NextWaypoint();
            return;
        }

        Quaternion look = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
        transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
    }

    private void NextWaypoint()
    {
        waitTimer = waitAtWaypoint;
        int count = waypoints.childCount;
        int next = waypointIndex + direction;

        if (next >= 0 && next < count)
        {
            waypointIndex = next;
            return;
        }

        switch (pathMode)
        {
            case PathMode.Loop:
                waypointIndex = 0;
                break;
            case PathMode.PingPong:
                direction = -direction;
                waypointIndex = Mathf.Clamp(waypointIndex + direction, 0, count - 1);
                break;
            default:
                pathFinished = true;
                break;
        }
    }

    private void StickToGround()
    {
        Vector3 origin = transform.position + Vector3.up * (stepHeight + 0.05f);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, stepHeight + 0.05f + maxDrop,
            groundLayers, QueryTriggerInteraction.Ignore);

        float bestDistance = float.MaxValue;
        float groundY = 0f;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform))
                continue; // ignore Rob's own collider
            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                groundY = hit.point.y;
            }
        }

        if (bestDistance == float.MaxValue)
            return; // no ground found: keep current height

        Vector3 position = transform.position;
        position.y = Mathf.Lerp(position.y, groundY, 1f - Mathf.Exp(-stepSmoothing * Time.deltaTime));
        transform.position = position;
    }

    // Draws the path in the Scene view so you can see the route while placing waypoints.
    private void OnDrawGizmos()
    {
        if (waypoints == null || waypoints.childCount == 0)
            return;

        Gizmos.color = Color.yellow;
        Vector3 previous = transform.position;
        for (int i = 0; i < waypoints.childCount; i++)
        {
            Vector3 point = waypoints.GetChild(i).position;
            Gizmos.DrawSphere(point, 0.15f);
            Gizmos.DrawLine(previous, point);
            previous = point;
        }
        if (pathMode == PathMode.Loop)
            Gizmos.DrawLine(previous, waypoints.GetChild(0).position);
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
