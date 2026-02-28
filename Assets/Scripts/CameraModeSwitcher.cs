using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// Automatically switches between two Cinemachine cameras based on arm mode:
///   • Third-person cam  — follows the hull (for driving)
///   • Shoulder/aim cam  — over-the-shoulder view (for arm control)
///
/// The camera follows RoboticArmController.IsArmMode, so pressing Tab to
/// enter/exit arm mode also switches the camera — no extra key needed.
///
/// SETUP (in the Unity Editor):
///
///   0. Make sure the Main Camera has a CinemachineBrain component.
///      Set Default Blend → 0.75 s for smooth transitions.
///
///   1. Create TWO CinemachineCamera GameObjects in the scene.
///
///   ── THIRD-PERSON FREELOOK CAM (for driving) ──────────────────
///
///   2. Select the first CinemachineCamera ("ThirdPersonCam"):
///        a. Set  Tracking Target → Follow = hull, LookAt = hull.
///        b. Add component: "Cinemachine Orbital Follow"
///             - Orbit Style  → Sphere
///             - Radius       → 8  (how far back)
///             - Top / Middle / Bottom ring radii can stay default
///        c. Add component: "Cinemachine Rotation Composer"
///             - This keeps the hull centred on screen while you orbit.
///             - Tracked Object Offset → (0, 1.5, 0)  (look slightly above hull centre)
///             - Damping → (0.5, 0.5)
///        d. Add component: "Cinemachine Input Axis Controller"
///             - This reads the mouse to drive the orbit.
///             - It auto-detects Mouse X → Horizontal Axis, Mouse Y → Vertical Axis.
///             - Make sure "Enabled" is checked.
///
///   ── SHOULDER / AIM CAM (for arm control) ─────────────────────
///
///   3. Select the second CinemachineCamera ("ShoulderCam"):
///        a. Set  Tracking Target → Follow = shoulder joint, LookAt = wrist or gripper tip.
///        b. Add component: "Cinemachine Third Person Follow"
///             - Shoulder Offset  → (0.6, 0.4, 0)  — right-shoulder ride.
///             - Camera Distance  → 1.5             — tight over-the-shoulder.
///             - Camera Side      → 1               — camera on the right.
///             - Damping           → (0.1, 0.3, 0.1) — snappier tracking.
///
///   4. Drag both CinemachineCamera objects into this component's inspector fields.
///
///   RESULT:
///     • Drive mode — mouse orbits freely around the hull (freelook).
///     • Tab → arm mode — camera snaps to a tight over-the-shoulder aim view.
/// </summary>
public class CameraModeSwitcher : MonoBehaviour
{
    [Header("Cinemachine Cameras")]
    [Tooltip("The wide third-person camera that follows the hull (driving).")]
    public CinemachineCamera thirdPersonCam;

    [Tooltip("The close over-the-shoulder camera for arm manipulation.")]
    public CinemachineCamera shoulderCam;

    [Header("Priority Values")]
    [Tooltip("Priority assigned to the currently active camera.")]
    public int activePriority   = 20;
    [Tooltip("Priority assigned to the inactive camera.")]
    public int inactivePriority = 0;

    /// <summary>Tracks the last known arm-mode state so we only switch on change.</summary>
    private bool wasArmMode;

    void OnEnable()
    {
        // Start in third-person (drive) mode
        wasArmMode = false;
        ApplyDriveView();
    }

    void LateUpdate()
    {
        bool armNow = RoboticArmController.IsArmMode;

        if (armNow != wasArmMode)
        {
            wasArmMode = armNow;

            if (armNow)
                ApplyShoulderView();
            else
                ApplyDriveView();
        }
    }

    // ── Camera helpers ──────────────────────────────────────────────

    void ApplyDriveView()
    {
        if (thirdPersonCam != null) thirdPersonCam.Priority = activePriority;
        if (shoulderCam    != null) shoulderCam.Priority    = inactivePriority;
    }

    void ApplyShoulderView()
    {
        if (shoulderCam    != null) shoulderCam.Priority    = activePriority;
        if (thirdPersonCam != null) thirdPersonCam.Priority = inactivePriority;
    }
}
