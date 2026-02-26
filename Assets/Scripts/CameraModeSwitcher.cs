using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

/// <summary>
/// Toggles between two Cinemachine cameras with the T key:
///   • Third-person cam  — follows the hull (for driving)
///   • Shoulder cam       — looks over the shoulder joint (for arm control)
///
/// SETUP (in the Unity Editor):
///   1. Create two CinemachineCamera objects in the scene.
///   2. Third-person cam:
///        - Add CinemachineFollow (or CinemachineThirdPersonFollow) component
///        - Set Follow = hull GameObject, LookAt = hull GameObject
///        - Tweak follow offset to taste (e.g. 0, 4, -6)
///   3. Shoulder cam:
///        - Add CinemachineFollow component
///        - Set Follow = shoulder joint GameObject, LookAt = wrist or gripper
///        - Tweak follow offset for a close over-the-shoulder view (e.g. 0.3, 0.5, -1)
///   4. Drag both cameras into this component's inspector fields.
///   5. Make sure a PlayerInput component exists on the same GameObject
///      with the InputSystem_Actions asset assigned (it already has the
///      "ToggleCamera" action bound to T).
/// </summary>
public class CameraModeSwitcher : MonoBehaviour
{
    [Header("Cinemachine Cameras")]
    [Tooltip("The wide third-person camera that follows the hull")]
    public CinemachineCamera thirdPersonCam;

    [Tooltip("The close shoulder camera for arm manipulation")]
    public CinemachineCamera shoulderCam;

    [Header("Priority values")]
    public int activePriority = 20;
    public int inactivePriority = 0;

    private PlayerInput playerInput;
    private InputAction toggleAction;
    private bool isShoulderView = false;

    void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        if (playerInput != null)
            toggleAction = playerInput.actions["ToggleCamera"];
    }

    void OnEnable()
    {
        if (toggleAction != null)
            toggleAction.performed += OnToggle;

        // Start in third-person mode
        SetThirdPerson();
    }

    void OnDisable()
    {
        if (toggleAction != null)
            toggleAction.performed -= OnToggle;
    }

    void OnToggle(InputAction.CallbackContext ctx)
    {
        isShoulderView = !isShoulderView;

        if (isShoulderView)
            SetShoulder();
        else
            SetThirdPerson();
    }

    void SetThirdPerson()
    {
        if (thirdPersonCam != null)
            thirdPersonCam.Priority = activePriority;
        if (shoulderCam != null)
            shoulderCam.Priority = inactivePriority;
    }

    void SetShoulder()
    {
        if (shoulderCam != null)
            shoulderCam.Priority = activePriority;
        if (thirdPersonCam != null)
            thirdPersonCam.Priority = inactivePriority;
    }
}
