using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using TrackedPoseDriver = UnityEngine.InputSystem.XR.TrackedPoseDriver;

// Zorgt dat een eenvoudige scene (gewone camera, geen XR-rig) ook in de Quest werkt: het hoofd wordt gevolgd.
// Alleen als er echt een bril actief is én er nog geen XR Origin in de scene staat. In de Editor zonder bril
// verandert er niets: dan blijft de gewone camera staan.
// De rig komt op de vloer onder de camera, met dezelfde kijkrichting. Kan de bril geen vloerhoogte geven,
// dan staat de camera op standingHeight.
public class XRRigSetup : MonoBehaviour
{
    public float standingHeight = 1.6f;

    void Awake()
    {
        if (!XRSettings.isDeviceActive) return;
        if (FindFirstObjectByType<XROrigin>() != null) return;
        var cam = Camera.main;
        if (cam == null) return;

        var rig = new GameObject("XR Origin (automatisch)");
        rig.SetActive(false); // eerst alles instellen, dan pas aanzetten
        var start = cam.transform;
        var forward = Vector3.ProjectOnPlane(start.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        rig.transform.SetPositionAndRotation(new Vector3(start.position.x, 0f, start.position.z), Quaternion.LookRotation(forward.normalized, Vector3.up));

        var offset = new GameObject("Camera Offset");
        offset.transform.SetParent(rig.transform, false);

        cam.transform.SetParent(offset.transform, false);
        cam.transform.localPosition = Vector3.zero;
        cam.transform.localRotation = Quaternion.identity;
        if (cam.GetComponent<TrackedPoseDriver>() == null) AddHeadTracking(cam.gameObject);

        var origin = rig.AddComponent<XROrigin>();
        origin.Origin = rig;
        origin.CameraFloorOffsetObject = offset;
        origin.Camera = cam;
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        origin.CameraYOffset = standingHeight;

        rig.SetActive(true);
        Debug.Log("[XRRigSetup] Geen XR Origin in de scene: er is er automatisch een gemaakt voor de hoofdtracking.");
    }

    static void AddHeadTracking(GameObject cameraObject)
    {
        // Component toevoegen terwijl het object uit staat, zodat de acties er al zijn als hij aangaat.
        bool wasActive = cameraObject.activeSelf;
        cameraObject.SetActive(false);
        var driver = cameraObject.AddComponent<TrackedPoseDriver>();
        driver.positionInput = new InputActionProperty(new InputAction("Hoofd positie", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
        driver.rotationInput = new InputActionProperty(new InputAction("Hoofd rotatie", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
        driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        cameraObject.SetActive(wasActive);
    }
}
