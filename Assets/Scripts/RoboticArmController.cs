using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controls the robotic arm joints.
///
/// Two input modes — toggle with TAB:
///   DRIVE MODE (default): WASD controls the hull (handled by HullMover).
///   ARM MODE:             WASD controls the arm joints.
///
/// In ARM MODE, Left-Ctrl toggles between two sub-modes:
///   BASE  (default) :  W/S = shoulder pitch     A/D = base rotation (shoulder_joint)
///   WRIST (Ctrl)    :  W/S = arm (elbow) pitch   A/D = wrist rotation
///
/// Gripper: Q / E  open/close (works in arm mode only)
///
/// The old RoboticArmInput bindings still work in parallel (R/F, T/G, etc.)
/// so you can use either control scheme.
///
/// HullMover checks RoboticArmController.IsArmMode to know when to
/// ignore WASD for driving.
/// </summary>
public class RoboticArmController : MonoBehaviour
{
    private RoboticArmInput input;

    [Header("Articulation Bodies")]
    public ArticulationBody shoulder_joint;
    public ArticulationBody shoulder;
    public ArticulationBody arm;
    public ArticulationBody wrist;
    public ArticulationBody gripper1;
    public ArticulationBody gripper2;
    public ArticulationBody gripper3;
    public ArticulationBody gripper4;

    [Header("Rotation Speed (degrees/sec)")]
    public float rotationSpeed = 50f;

    [Header("Joint Limits")]
    public float shoulderJointMin = -90f;
    public float shoulderJointMax = 90f;
    public float shoulderMin = -45f;
    public float shoulderMax = 90f;
    public float armMin = -30f;
    public float armMax = 120f;
    public float wristMin = -60f;
    public float wristMax = 60f;
    public float gripperMin = -80f;
    public float gripperMax = 0f;

    // Current joint angles
    private float shoulderJointAngle;
    private float shoulderAngle;
    private float armAngle;
    private float wristAngle;
    private float gripperAngle;

    // ── Toggle state ──
    /// <summary>True when WASD controls the arm instead of the hull.</summary>
    public static bool IsArmMode { get; private set; }

    /// <summary>True when Ctrl sub-mode active (wrist/elbow instead of base/shoulder).</summary>
    private bool wristSubMode;

    [Header("Debug")]
    public bool showControlHUD = true;

    // Enum for axis selection
    private enum Axis { X, Y, Z }

    private void Awake()
    {
        input = new RoboticArmInput();
    }

    private void OnEnable()
    {
        input.Enable();
        IsArmMode = false;
    }

    private void OnDisable()
    {
        input.Disable();
        IsArmMode = false;
    }

    private void Start()
    {
        // Configure all drives with high stiffness/damping/force for smooth movement
        ConfigureDrive(shoulder_joint, Axis.X);
        ConfigureDrive(shoulder, Axis.X);
        ConfigureDrive(arm, Axis.X);
        ConfigureDrive(wrist, Axis.X);
        ConfigureDrive(gripper1, Axis.X);
        ConfigureDrive(gripper2, Axis.X);
        ConfigureDrive(gripper3, Axis.X);
        ConfigureDrive(gripper4, Axis.X);
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        var mouse = Mouse.current;

        // ── Tab toggles arm mode ──
        if (kb.tabKey.wasPressedThisFrame)
            IsArmMode = !IsArmMode;

        // ── Ctrl toggles wrist sub-mode (while in arm mode) ──
        if (kb.leftCtrlKey.wasPressedThisFrame)
            wristSubMode = !wristSubMode;

        // ── Mouse gripper: LMB = close, RMB = open (always works) ──
        float mouseGrip = 0f;
        if (mouse != null)
        {
            if (mouse.leftButton.isPressed)  mouseGrip += 1f;  // open
            if (mouse.rightButton.isPressed) mouseGrip -= 1f;  // close
        }
        if (Mathf.Abs(mouseGrip) > 0.01f)
            UpdateGripper(mouseGrip);

        // ── WASD arm control (only in arm mode) ──
        if (IsArmMode)
        {
            float vertical   = 0f;  // W/S
            float horizontal = 0f;  // A/D
            if (kb.wKey.isPressed) vertical   += 1f;
            if (kb.sKey.isPressed) vertical   -= 1f;
            if (kb.dKey.isPressed) horizontal += 1f;
            if (kb.aKey.isPressed) horizontal -= 1f;

            if (!wristSubMode)
            {
                // BASE sub-mode: W/S = shoulder, A/D = shoulder_joint
                UpdateJoint(shoulder_joint, ref shoulderJointAngle,
                    horizontal, shoulderJointMin, shoulderJointMax, 0.5f);
                UpdateJoint(shoulder, ref shoulderAngle,
                    vertical, shoulderMin, shoulderMax, 0.5f);
            }
            else
            {
                // WRIST sub-mode: W/S = arm (elbow), A/D = wrist
                UpdateJoint(arm, ref armAngle,
                    vertical, armMin, armMax, 0.5f);
                UpdateJoint(wrist, ref wristAngle,
                    horizontal, wristMin, wristMax, 0.5f);
            }

            // Gripper: Q = close, E = open (in arm mode)
            float gripInput = 0f;
            if (kb.qKey.isPressed) gripInput -= 1f;
            if (kb.eKey.isPressed) gripInput += 1f;
            if (Mathf.Abs(gripInput) > 0.01f)
                UpdateGripper(gripInput);
        }

        // ── Original RoboticArmInput bindings always work too ──
        float sjInput = input.Robot.ShoulderJoint.ReadValue<float>();
        float shInput = input.Robot.Shoulder.ReadValue<float>();
        float arInput = input.Robot.Arm.ReadValue<float>();
        float wrInput = input.Robot.Wrist.ReadValue<float>();
        float grInput = input.Robot.Gripper.ReadValue<float>();

        if (Mathf.Abs(sjInput) > 0.01f)
            UpdateJoint(shoulder_joint, ref shoulderJointAngle, sjInput, shoulderJointMin, shoulderJointMax, 0.5f);
        if (Mathf.Abs(shInput) > 0.01f)
            UpdateJoint(shoulder, ref shoulderAngle, shInput, shoulderMin, shoulderMax, 0.5f);
        if (Mathf.Abs(arInput) > 0.01f)
            UpdateJoint(arm, ref armAngle, arInput, armMin, armMax, 0.5f);
        if (Mathf.Abs(wrInput) > 0.01f)
            UpdateJoint(wrist, ref wristAngle, wrInput, wristMin, wristMax, 0.5f);
        if (Mathf.Abs(grInput) > 0.01f)
            UpdateGripper(grInput);
    }

    // Smoothly update a single joint
    private void UpdateJoint(ArticulationBody joint, ref float currentAngle,
        float inputValue, float min, float max, float speedFactor)
    {
        float targetAngle = currentAngle + inputValue * rotationSpeed * Time.deltaTime;
        targetAngle = Mathf.Clamp(targetAngle, min, max);

        float maxDelta = rotationSpeed * Time.deltaTime * speedFactor;

        if (Mathf.Abs(targetAngle - currentAngle) < 0.01f)
            currentAngle = targetAngle;
        else
            currentAngle = Mathf.MoveTowards(currentAngle, targetAngle, maxDelta);

        ApplyDrive(joint, currentAngle);
    }

    private void UpdateGripper(float inputValue)
    {
        float targetAngle = gripperAngle + inputValue * rotationSpeed * Time.deltaTime;
        targetAngle = Mathf.Clamp(targetAngle, gripperMin, gripperMax);

        float maxDelta = rotationSpeed * Time.deltaTime;

        if (Mathf.Abs(targetAngle - gripperAngle) < 0.01f)
            gripperAngle = targetAngle;
        else
            gripperAngle = Mathf.MoveTowards(gripperAngle, targetAngle, maxDelta);

        ApplyDrive(gripper1, gripperAngle);
        ApplyDrive(gripper2, -gripperAngle);
        ApplyDrive(gripper3, -gripperAngle);
        ApplyDrive(gripper4, gripperAngle);
    }

    // Apply angle to X axis drive
    private void ApplyDrive(ArticulationBody joint, float target)
    {
        ArticulationDrive drive = joint.xDrive;
        drive.target = target;
        joint.xDrive = drive;
    }

    /// <summary>Reset all joint angles to zero.</summary>
    public void ResetToStart()
    {
        shoulderJointAngle = 0f;
        shoulderAngle      = 0f;
        armAngle           = 0f;
        wristAngle         = 0f;
        gripperAngle       = 0f;

        ApplyDrive(shoulder_joint, 0f);
        ApplyDrive(shoulder,       0f);
        ApplyDrive(arm,            0f);
        ApplyDrive(wrist,          0f);
        ApplyDrive(gripper1,       0f);
        ApplyDrive(gripper2,       0f);
        ApplyDrive(gripper3,       0f);
        ApplyDrive(gripper4,       0f);

        Debug.Log("[RoboticArmController] Reset joints to zero.");
    }

    // Configure drive with high stiffness/damping/force
    private void ConfigureDrive(ArticulationBody joint, Axis axis)
    {
        ArticulationDrive drive;
        switch (axis)
        {
            case Axis.X:
                drive = joint.xDrive;
                drive.stiffness = 10000f;
                drive.damping = 1000f;
                drive.forceLimit = 10000f;
                joint.xDrive = drive;
                break;
            case Axis.Y:
                drive = joint.yDrive;
                drive.stiffness = 10000f;
                drive.damping = 1000f;
                drive.forceLimit = 10000f;
                joint.yDrive = drive;
                break;
            case Axis.Z:
                drive = joint.zDrive;
                drive.stiffness = 10000f;
                drive.damping = 1000f;
                drive.forceLimit = 10000f;
                joint.zDrive = drive;
                break;
        }
    }

    // ── Debug HUD ──
    void OnGUI()
    {
        if (!showControlHUD) return;

        float x = 10f, y = Screen.height - 50f;
        var s = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14 };

        string mode = IsArmMode
            ? (wristSubMode ? "<color=orange>ARM MODE: Wrist/Elbow</color>  <color=grey>[Tab→Drive | Ctrl→Base | Bksp→Reset]</color>"
                            : "<color=lime>ARM MODE: Base/Shoulder</color>  <color=grey>[Tab→Drive | Ctrl→Wrist | Bksp→Reset]</color>")
            : "<color=white>DRIVE MODE</color>  <color=grey>[Tab→Arm | Bksp→Reset]</color>";

        GUI.Label(new Rect(x, y, 600, 30), mode, s);
    }
}