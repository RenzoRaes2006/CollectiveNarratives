using UnityEngine;

/// <summary>
/// Moves Rob forward until he enters the assigned trigger box, then plays
/// the existing surprised animation through the Animator trigger "Check".
/// </summary>
public class RobWalkToTrigger : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Collider destinationTrigger;
    [SerializeField, Min(0f)] private float moveSpeed = 1f;
    [SerializeField] private string surprisedTriggerName = "Check";

    private bool hasReachedTrigger;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        Collider npcCollider = GetComponent<Collider>();
        if (npcCollider != null && npcCollider.isTrigger)
            Debug.LogWarning("Rob needs a non-trigger Collider so he can enter the destination trigger.", this);
    }

    private void Update()
    {
        if (!hasReachedTrigger)
            transform.position += transform.forward * (moveSpeed * Time.deltaTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasReachedTrigger || destinationTrigger == null || other != destinationTrigger)
            return;

        hasReachedTrigger = true;

        if (animator == null)
        {
            Debug.LogError("No Animator found on Rob or his children.", this);
            return;
        }

        animator.SetTrigger(surprisedTriggerName);
    }
}

