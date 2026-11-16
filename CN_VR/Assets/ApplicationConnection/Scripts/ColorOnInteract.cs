using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Box voor de tutorial: blauw zolang je ernaar wijst, groen (blijvend) zodra je de grip indrukt.
[RequireComponent(typeof(XRSimpleInteractable))]
public class ColorOnInteract : MonoBehaviour
{
    public Color hoverColor = Color.blue;
    public Color selectColor = Color.green;

    public bool IsHovered { get; private set; }
    public bool WasSelected { get; private set; }

    XRSimpleInteractable interactable;
    Renderer rend;
    Color normalColor;

    void Awake()
    {
        interactable = GetComponent<XRSimpleInteractable>();
        rend = GetComponentInChildren<Renderer>();
        normalColor = rend.material.color;
    }

    void OnEnable()
    {
        interactable.hoverEntered.AddListener(OnHoverEntered);
        interactable.hoverExited.AddListener(OnHoverExited);
        interactable.selectEntered.AddListener(OnSelectEntered);
    }

    void OnDisable()
    {
        interactable.hoverEntered.RemoveListener(OnHoverEntered);
        interactable.hoverExited.RemoveListener(OnHoverExited);
        interactable.selectEntered.RemoveListener(OnSelectEntered);
    }

    public void ResetBox()
    {
        WasSelected = false;
        rend.material.color = IsHovered ? hoverColor : normalColor;
    }

    void OnHoverEntered(HoverEnterEventArgs _)
    {
        IsHovered = true;
        if (!WasSelected) rend.material.color = hoverColor;
    }

    void OnHoverExited(HoverExitEventArgs _)
    {
        IsHovered = interactable.isHovered; // de andere hand kan nog wijzen
        if (!WasSelected && !IsHovered) rend.material.color = normalColor;
    }

    void OnSelectEntered(SelectEnterEventArgs _)
    {
        WasSelected = true;
        rend.material.color = selectColor;
    }
}
