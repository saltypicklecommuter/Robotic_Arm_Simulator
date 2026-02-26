using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(ArticulationBody))]
public class HullMover : MonoBehaviour
{
    [Header("Movement")]
    public float maxSpeed = 2f;            // slow heavy tank
    public float moveAcceleration = 2f;    // very sluggish ramp up

    [Header("Rotation")]
    public float maxTurnSpeed = 69f;       // slow deliberate turns
    public float turnAcceleration = 6f;    // sluggish turn ramp
    [Tooltip("Hard cap on wheel rotation speed (deg/s) to prevent excessive spin")]
    public float maxWheelRotationSpeed = 500f;

    [Header("Stability")]
    [Tooltip("How low to place the center of mass (negative = lower)")]
    public float centerOfMassY = -1f;

    [Header("Treads")]
    public TreadSystem leftTread;
    public TreadSystem rightTread;
    [Tooltip("How fast the tread links scroll relative to hull speed")]
    public float treadSpeedScale = 0.02f;
    [Tooltip("How fast treads scroll when turning in place (no forward input)")]
    public float treadTurnScale = 0.5f;

    [Header("Wheels")]
    public WheelSpinner leftWheels;
    public WheelSpinner rightWheels;
    [Tooltip("Degrees per second per unit of tread speed")]
    public float wheelDegreesPerSpeed = 360f;

    private ArticulationBody hullBody;
    private PlayerInput playerInput;
    private InputAction moveAction;

    private float currentSpeed = 0f;
    private float currentTurnSpeed = 0f;

    void Awake()
    {
        hullBody = GetComponent<ArticulationBody>();
        playerInput = GetComponent<PlayerInput>();
        moveAction = playerInput.actions["Move"];

        // ── Stability settings ──────────────────────────────────────
        // Low center of mass prevents tipping
        hullBody.automaticCenterOfMass = false;
        hullBody.centerOfMass = new Vector3(0f, centerOfMassY, 0f);

        // Lock inertia so child articulation bodies don't destabilize
        hullBody.automaticInertiaTensor = false;

        // High mass and drag prevent jitter and flying off
        hullBody.mass = 200f;
        hullBody.linearDamping = 20f;
        hullBody.angularDamping = 50f;
    }

    void FixedUpdate()
    {
        if (moveAction == null) return;

        Vector2 moveInput = moveAction.ReadValue<Vector2>();
        float dt = Time.fixedDeltaTime;

        // ── Forward / Back ──────────────────────────────────────────
        float targetSpeed = moveInput.y * maxSpeed;
        if (Mathf.Abs(moveInput.y) > 0.01f)
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, moveAcceleration * dt);
        else
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, 15f * dt); // Quick but not instant

        // ── Yaw (A / D) ────────────────────────────────────────────
        float targetTurn = moveInput.x * maxTurnSpeed;
        if (Mathf.Abs(moveInput.x) > 0.01f)
            currentTurnSpeed = Mathf.MoveTowards(currentTurnSpeed, targetTurn, turnAcceleration * dt);
        else
            currentTurnSpeed = Mathf.MoveTowards(currentTurnSpeed, 0f, 200f * dt); // Stops in ~0.15s max

        // ── Move via TeleportRoot ───────────────────────────────────
        // Using TeleportRoot avoids fighting articulation child joints.
        // This moves the ENTIRE articulation chain together.
        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 moveStep = flatForward * currentSpeed * dt;

        // Yaw rotation step
        float yawStep = currentTurnSpeed * dt;
        Quaternion yawRot = Quaternion.Euler(0f, yawStep, 0f);

        // Force pitch and roll to zero (keep only current yaw + yaw step)
        Vector3 currentEuler = transform.rotation.eulerAngles;
        Quaternion stableRot = Quaternion.Euler(0f, currentEuler.y + yawStep, 0f);

        // Teleport the entire articulation hierarchy — no reaction forces
        hullBody.TeleportRoot(transform.position + moveStep, stableRot);

        // Kill any residual velocity that physics might accumulate
        hullBody.linearVelocity = Vector3.zero;
        hullBody.angularVelocity = Vector3.zero;

        // ── Differential tread & wheel drive ────────────────────────
        float forwardComponent = currentSpeed * treadSpeedScale;
        float turnComponent    = (currentTurnSpeed / maxTurnSpeed) * treadTurnScale;

        float leftTreadSpeed  = forwardComponent + turnComponent;
        float rightTreadSpeed = forwardComponent - turnComponent;

        if (leftTread  != null) leftTread.SetSpeed(leftTreadSpeed);
        if (rightTread != null) rightTread.SetSpeed(rightTreadSpeed);

        float leftWheelSpeed  = Mathf.Clamp(leftTreadSpeed * wheelDegreesPerSpeed, -maxWheelRotationSpeed, maxWheelRotationSpeed);
        float rightWheelSpeed = Mathf.Clamp(rightTreadSpeed * wheelDegreesPerSpeed, -maxWheelRotationSpeed, maxWheelRotationSpeed);

        if (leftWheels  != null) leftWheels.SetSpeed(leftWheelSpeed);
        if (rightWheels != null) rightWheels.SetSpeed(rightWheelSpeed);
    }
}