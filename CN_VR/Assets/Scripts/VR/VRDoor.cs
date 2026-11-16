using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Deur die je in VR bij de klink vastpakt (grip) en open/dicht trekt of duwt.
/// Het pivot van dit object moet op het scharnier liggen; de deur draait rond de lokale Y-as
/// van de parent. Geen physics nodig: de hoek volgt gewoon je hand.
/// </summary>
public class VRDoor : MonoBehaviour
{
    [Tooltip("Maximale openingshoek in graden, naar beide kanten.")]
    [SerializeField, Range(10f, 180f)] private float maxOpenAngle = 100f;

    [Tooltip("De klinken waaraan je de deur vastpakt (elk met een XRSimpleInteractable).")]
    [SerializeField] private List<XRSimpleInteractable> handles = new List<XRSimpleInteractable>();

    private XRSimpleInteractable activeHandle;
    private IXRSelectInteractor grabbingInteractor;
    private float closedYaw;
    private float yawAtGrab;
    private float handAngleAtGrab;
    private float currentOffset;
    private bool subscribed;

    private void Awake()
    {
        closedYaw = transform.localEulerAngles.y;
    }

    /// <summary>Stel de klinken in (gebruikt door VRDoorSetup bij het starten).</summary>
    public void SetHandles(IEnumerable<XRSimpleInteractable> newHandles)
    {
        Unsubscribe();
        handles = new List<XRSimpleInteractable>(newHandles);
        if (isActiveAndEnabled)
            Subscribe();
    }

    private void OnEnable() => Subscribe();

    private void OnDisable()
    {
        Unsubscribe();
        grabbingInteractor = null;
        activeHandle = null;
    }

    private void Subscribe()
    {
        if (subscribed) return;
        foreach (var h in handles)
        {
            if (h == null) continue;
            h.selectEntered.AddListener(OnGrab);
            h.selectExited.AddListener(OnRelease);
        }
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        foreach (var h in handles)
        {
            if (h == null) continue;
            h.selectEntered.RemoveListener(OnGrab);
            h.selectExited.RemoveListener(OnRelease);
        }
        subscribed = false;
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        grabbingInteractor = args.interactorObject;
        activeHandle = args.interactableObject as XRSimpleInteractable;
        yawAtGrab = currentOffset;
        handAngleAtGrab = HandAngle();
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        if (args.interactorObject == grabbingInteractor)
        {
            grabbingInteractor = null;
            activeHandle = null;
        }
    }

    private void Update()
    {
        if (grabbingInteractor == null || activeHandle == null)
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
        Vector3 handWorld = grabbingInteractor.GetAttachTransform(activeHandle).position;
        Vector3 hand = transform.parent != null ? transform.parent.InverseTransformPoint(handWorld) : handWorld;
        Vector3 fromHinge = hand - transform.localPosition;
        return Mathf.Atan2(fromHinge.x, fromHinge.z) * Mathf.Rad2Deg;
    }
}
