using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Deur die je in VR vastpakt (grip) en open/dicht trekt of duwt.
/// Het pivot van dit object moet op het scharnier liggen; de deur draait rond de lokale Y-as
/// van de parent. Geen physics nodig: de hoek volgt gewoon je hand.
/// </summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class VRDoor : MonoBehaviour
{
    [Tooltip("Maximale openingshoek in graden, naar beide kanten.")]
    [SerializeField, Range(10f, 180f)] private float maxOpenAngle = 100f;

    private XRSimpleInteractable interactable;
    private IXRSelectInteractor grabbingInteractor;
    private float closedYaw;
    private float yawAtGrab;
    private float handAngleAtGrab;
    private float currentOffset;

    private void Awake()
    {
        interactable = GetComponent<XRSimpleInteractable>();
        closedYaw = transform.localEulerAngles.y;
    }

    private void OnEnable()
    {
        interactable.selectEntered.AddListener(OnGrab);
        interactable.selectExited.AddListener(OnRelease);
    }

    private void OnDisable()
    {
        interactable.selectEntered.RemoveListener(OnGrab);
        interactable.selectExited.RemoveListener(OnRelease);
        grabbingInteractor = null;
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        grabbingInteractor = args.interactorObject;
        yawAtGrab = currentOffset;
        handAngleAtGrab = HandAngle();
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        if (args.interactorObject == grabbingInteractor)
            grabbingInteractor = null;
    }

    private void Update()
    {
        if (grabbingInteractor == null)
            return;

        float delta = Mathf.DeltaAngle(handAngleAtGrab, HandAngle());
        currentOffset = Mathf.Clamp(yawAtGrab + delta, -maxOpenAngle, maxOpenAngle);

        Vector3 euler = transform.localEulerAngles;
        euler.y = closedYaw + currentOffset;
        transform.localEulerAngles = euler;
    }

    /// <summary>Hoek (graden) van de hand rond het scharnier, gemeten in de ruimte van de parent.</summary>
    private float HandAngle()
    {
        Vector3 handWorld = grabbingInteractor.GetAttachTransform(interactable).position;
        Vector3 hand = transform.parent != null ? transform.parent.InverseTransformPoint(handWorld) : handWorld;
        Vector3 fromHinge = hand - transform.localPosition;
        return Mathf.Atan2(fromHinge.x, fromHinge.z) * Mathf.Rad2Deg;
    }
}
