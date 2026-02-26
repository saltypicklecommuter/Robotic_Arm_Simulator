using UnityEngine;
using UnityEngine.InputSystem;

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

    // Enum for axis selection
    private enum Axis { X, Y, Z }

    private void Awake()
    {
        input = new RoboticArmInput();
    }

    private void OnEnable() => input.Enable();
    private void OnDisable() => input.Disable();

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
        // Update joints with smooth interpolation
        UpdateJoint(shoulder_joint, ref shoulderJointAngle,
            input.Robot.ShoulderJoint.ReadValue<float>(), shoulderJointMin, shoulderJointMax, 0.5f);

        UpdateJoint(shoulder, ref shoulderAngle,
            input.Robot.Shoulder.ReadValue<float>(), shoulderMin, shoulderMax, 0.5f);

        UpdateJoint(arm, ref armAngle,
            input.Robot.Arm.ReadValue<float>(), armMin, armMax, 0.5f);

        UpdateJoint(wrist, ref wristAngle,
            input.Robot.Wrist.ReadValue<float>(), wristMin, wristMax, 0.5f);

        UpdateGripper();
    }

    // Smoothly update a single joint
    private void UpdateJoint(ArticulationBody joint, ref float currentAngle,
        float inputValue, float min, float max, float speedFactor)
    {
        float targetAngle = currentAngle + inputValue * rotationSpeed * Time.deltaTime;
        targetAngle = Mathf.Clamp(targetAngle, min, max);

        float maxDelta = rotationSpeed * Time.deltaTime * speedFactor;

        // Snap if very close to prevent micro back-and-forth
        if (Mathf.Abs(targetAngle - currentAngle) < 0.01f)
            currentAngle = targetAngle;
        else
            currentAngle = Mathf.MoveTowards(currentAngle, targetAngle, maxDelta);

        ApplyDrive(joint, currentAngle);
    }

    private void UpdateGripper()
    {
        float inputValue = input.Robot.Gripper.ReadValue<float>();
        float targetAngle = gripperAngle + inputValue * rotationSpeed * Time.deltaTime;
        targetAngle = Mathf.Clamp(targetAngle, gripperMin, gripperMax);

        float maxDelta = rotationSpeed * Time.deltaTime;

        if (Mathf.Abs(targetAngle - gripperAngle) < 0.01f)
            gripperAngle = targetAngle;
        else
            gripperAngle = Mathf.MoveTowards(gripperAngle, targetAngle, maxDelta);

        // Apply to all fingers
        ApplyDrive(gripper1, gripperAngle);
        ApplyDrive(gripper2, -gripperAngle);
        ApplyDrive(gripper3, -gripperAngle);
        ApplyDrive(gripper4, gripperAngle);
    }

    // Apply angle to X axis drive (modify if your joint uses Y or Z)
    private void ApplyDrive(ArticulationBody joint, float target)
    {
        ArticulationDrive drive = joint.xDrive;
        drive.target = target;
        joint.xDrive = drive;
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
}