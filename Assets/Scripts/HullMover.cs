using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Physics-based tank movement for an ArticulationBody hull.
///
/// WHY TeleportRoot:
///   ArticulationBody roots with child joint chains (like a robotic arm) cannot
///   be reliably moved with AddForce — the articulation solver eats the forces
///   to maintain joint constraints, causing the arm to slide off and the hull to
///   ignore input. TeleportRoot moves the ENTIRE chain as one rigid unit.
///
///   To still get physics-like behavior, we simulate our own velocity, gravity,
///   momentum, and ground contact via raycasts. The result looks and feels like
///   real physics: the tank accelerates, decelerates, falls off cliffs, follows
///   slopes, and responds to terrain — but the arm stays rock-solid on the hull.
///
/// SETUP:
///   1. Put the hull and all children on a "Vehicle" layer.
///   2. In the Inspector, set Ground Layers to EXCLUDE "Vehicle".
///   3. Adjust Tread Length / Tread Width to match your model.
///   4. Adjust Ride Height so the hull sits correctly above the ground.
///   5. Assign tread/wheel visual references if you have them.
/// </summary>
[RequireComponent(typeof(ArticulationBody))]
public class HullMover : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════
    //  MOVEMENT
    // ═══════════════════════════════════════════════════════════════

    [Header("Speed")]
    public float maxForwardSpeed = 4f;
    public float maxReverseSpeed = 2f;
    public float forwardAccel    = 5f;
    public float reverseAccel    = 3f;
    public float brakeStrength   = 10f;

    [Header("Turning")]
    public float maxTurnSpeed       = 60f;   // deg/s
    public float turnAccel          = 120f;  // deg/s²
    public float turnBrake          = 200f;  // deg/s²

    // ═══════════════════════════════════════════════════════════════
    //  PHYSICS SIMULATION
    // ═══════════════════════════════════════════════════════════════

    [Header("Gravity & Air")]
    public float gravityAccel     = 9.81f;
    public float terminalVelocity = 40f;

    [Header("Ground Detection")]
    [Tooltip("Layers the raycasts can hit. MUST exclude the vehicle layer.")]
    public LayerMask groundLayers = ~0;

    [Header("Tread Raycast Layout")]
    [Tooltip("Half the tread footprint length (front to center).")]
    public float treadHalfLength = 2f;
    [Tooltip("Lateral distance from hull center to each tread.")]
    public float treadHalfWidth  = 1.2f;
    [Tooltip("Rays per tread side (spread front-to-back).")]
    [Range(2, 10)]
    public int raysPerSide = 4;

    [Header("Suspension")]
    [Tooltip("Target height of the hull pivot above detected ground.")]
    public float rideHeight    = 0.6f;
    [Tooltip("Spring stiffness — pulls hull toward ride height.")]
    public float spring        = 60f;
    [Tooltip("Damper — prevents bouncing. Higher = stiffer ride.")]
    public float damper        = 25f;
    [Tooltip("Max vertical correction speed from spring (prevents launch on slopes).")]
    public float maxSpringVel  = 3f;
    [Tooltip("Ray origin height above hull pivot.")]
    public float rayStartAbove = 2f;
    [Tooltip("Total downward ray length.")]
    public float rayLength     = 5f;

    [Header("Slope Behavior")]
    [Tooltip("How fast hull tilt blends toward terrain angle.")]
    public float tiltSmoothing = 8f;
    [Tooltip("Max slope in degrees the treads can climb.")]
    public float maxClimbAngle = 45f;
    [Tooltip("Max hull rotation speed in deg/s (limits how fast it can tilt). Lower = smoother arm.")]
    public float maxTiltRate = 90f;

    // ═══════════════════════════════════════════════════════════════
    //  VISUAL ANIMATION
    // ═══════════════════════════════════════════════════════════════

    [Header("Tread Visuals")]
    public TreadSystem leftTread;
    public TreadSystem rightTread;
    public float treadScrollScale = 0.02f;
    public float treadTurnScale   = 0.005f;

    [Header("Wheel Visuals")]
    public WheelSpinner leftWheels;
    public WheelSpinner rightWheels;
    public float wheelDegPerSpeed    = 360f;
    public float maxWheelDegPerSec   = 500f;

    [Header("Debug")]
    [Tooltip("Show on-screen debug overlay while playing.")]
    public bool showDebugHUD = true;

    // ═══════════════════════════════════════════════════════════════
    //  PRIVATE STATE
    // ═══════════════════════════════════════════════════════════════

    private ArticulationBody body;

    // Simulated velocities
    private float speed;           // m/s along hull forward
    private float yawSpeed;        // deg/s
    private float leftVertVel;     // per-tread vertical velocity
    private float rightVertVel;
    private float yaw;             // tracked independently — NEVER from eulerAngles

    // Terrain state
    private bool  leftGrounded, rightGrounded;
    private float leftHeight,   rightHeight;
    private Vector3 leftNormal, rightNormal;
    private float leftY, rightY;   // current Y of each tread contact point
    private float smoothedRollDeg; // smoothed roll for arm stability
    private Quaternion smoothedRot; // final smoothed rotation

    // Input
    private float inDrive, inTurn;

    // Spawn state (for reset)
    private Vector3    spawnPos;
    private Quaternion spawnRot;

    // ═══════════════════════════════════════════════════════════════

    void Awake()
    {
        body = GetComponent<ArticulationBody>();

        // Make sure body is movable
        body.immovable = false;

        // Disable built-in physics — we do everything manually
        body.useGravity             = false;
        body.linearDamping          = 0f;
        body.angularDamping         = 0f;
        body.automaticCenterOfMass  = false;
        body.centerOfMass           = Vector3.zero;
        body.automaticInertiaTensor = false;

        // ── Auto-exclude our own layer from raycasts ──
        int myLayer = gameObject.layer;
        int myLayerBit = 1 << myLayer;
        if ((groundLayers.value & myLayerBit) != 0)
        {
            groundLayers &= ~myLayerBit;
            Debug.Log($"[HullMover] Auto-excluded layer {myLayer} ({LayerMask.LayerToName(myLayer)}) from ground raycasts.");
        }

        // Save spawn state for reset
        spawnPos = transform.position;
        spawnRot = transform.rotation;

        // Initialise yaw from current rotation
        yaw = transform.eulerAngles.y;

        // Initialise per-tread Y to current position
        leftY  = transform.position.y;
        rightY = transform.position.y;
        smoothedRot = transform.rotation;
        smoothedRollDeg = 0f;

        Debug.Log($"[HullMover] Awake OK. Layer={myLayer} ({LayerMask.LayerToName(myLayer)}), GroundMask={groundLayers.value}, Pos={transform.position}");
    }

    void Update()
    {
        // Poll keyboard every render frame for responsive input
        var kb = Keyboard.current;
        if (kb == null) { inDrive = inTurn = 0f; return; }

        // When arm mode is active, WASD goes to the arm — hull gets nothing
        if (RoboticArmController.IsArmMode)
        {
            inDrive = 0f;
            inTurn  = 0f;
            return;
        }

        inDrive = 0f;
        inTurn  = 0f;
        if (kb.wKey.isPressed) inDrive += 1f;
        if (kb.sKey.isPressed) inDrive -= 1f;
        if (kb.dKey.isPressed) inTurn  += 1f;
        if (kb.aKey.isPressed) inTurn  -= 1f;

        // ── Backspace = reload the entire scene ──
        if (kb.backspaceKey.wasPressedThisFrame)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // ── 1. RAYCAST BOTH TREADS ──────────────────────────────────
        CastTreadSide(-1f, out leftGrounded,  out leftHeight,  out leftNormal);
        CastTreadSide(+1f, out rightGrounded, out rightHeight, out rightNormal);

        bool grounded = leftGrounded || rightGrounded;
        bool both     = leftGrounded && rightGrounded;

        // ── 2. DRIVE ────────────────────────────────────────────────
        if (grounded)
        {
            bool wantForward  = inDrive > 0.05f;
            bool wantReverse  = inDrive < -0.05f;

            // Check if slope is too steep
            Vector3 avgNorm = both ? ((leftNormal + rightNormal) * 0.5f).normalized
                            : leftGrounded ? leftNormal : rightNormal;
            float slopeAngle = Vector3.Angle(avgNorm, Vector3.up);
            bool  tooSteep   = slopeAngle > maxClimbAngle && inDrive > 0f;

            if (tooSteep)
            {
                // Can't climb — slide back
                speed = Mathf.MoveTowards(speed, -1f, brakeStrength * dt);
            }
            else if (wantForward)
            {
                float traction = both ? 1f : 0.5f;
                speed = Mathf.MoveTowards(speed, maxForwardSpeed,
                                          forwardAccel * traction * dt);
            }
            else if (wantReverse)
            {
                float traction = both ? 1f : 0.5f;
                speed = Mathf.MoveTowards(speed, -maxReverseSpeed,
                                          reverseAccel * traction * dt);
            }
            else
            {
                // No input — brake to zero
                speed = Mathf.MoveTowards(speed, 0f, brakeStrength * dt);
            }

            // Yaw
            if (Mathf.Abs(inTurn) > 0.05f)
            {
                float traction = both ? 1f : 0.4f;
                yawSpeed = Mathf.MoveTowards(yawSpeed, inTurn * maxTurnSpeed,
                                             turnAccel * traction * dt);
            }
            else
            {
                yawSpeed = Mathf.MoveTowards(yawSpeed, 0f, turnBrake * dt);
            }
        }
        // Airborne: keep momentum (speed & yawSpeed unchanged), no input

        // ── 3. PER-TREAD SUSPENSION ──────────────────────────────
        // Each tread has its own spring-damper and freefall.
        // This gives realistic roll from terrain: if one side is higher,
        // that side's Y is higher → hull tilts naturally.
        leftY  = SimulateTreadY(leftGrounded,  leftHeight,  leftY,  ref leftVertVel,  dt);
        rightY = SimulateTreadY(rightGrounded, rightHeight, rightY, ref rightVertVel, dt);

        // ── 4. TERRAIN TILT (pitch from normals) ─────────────────
        // Pitch comes from averaged surface normals.
        // Roll comes purely from the height difference between the two treads.
        Vector3 targetNormal;
        if (both)
            targetNormal = ((leftNormal + rightNormal) * 0.5f).normalized;
        else if (leftGrounded)
            targetNormal = leftNormal;
        else if (rightGrounded)
            targetNormal = rightNormal;
        else
            targetNormal = Vector3.up;

        // Only use the pitch component of the terrain normal; roll is geometric
        Vector3 flatRight = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
        // Remove lateral tilt from terrain normal (we compute roll ourselves)
        Vector3 pitchNormal = (targetNormal - flatRight * Vector3.Dot(targetNormal, flatRight)).normalized;
        if (pitchNormal.sqrMagnitude < 0.01f) pitchNormal = Vector3.up;

        // Blend for smooth pitch transitions
        Vector3 blendedPitch = Vector3.Slerp(
            Vector3.ProjectOnPlane(transform.up, flatRight).normalized,
            pitchNormal,
            tiltSmoothing * dt);
        if (blendedPitch.sqrMagnitude < 0.01f) blendedPitch = Vector3.up;

        // ── 5. POSITION ─────────────────────────────────────────────
        // Drive along the slope but ONLY pitch-adjusted (no lateral drift).
        // We compute the slope ratio in the forward direction only, so
        // cross-slopes don't deflect the heading sideways.
        // Now compute the slope-following drive direction using pitchNormal
        Vector3 flatFwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        float nDotF = Vector3.Dot(pitchNormal, flatFwd);
        float nDotU = Vector3.Dot(pitchNormal, Vector3.up);
        float slopeRatio = nDotU > 0.01f ? -nDotF / nDotU : 0f;
        Vector3 driveDir = (flatFwd + Vector3.up * slopeRatio).normalized;

        // Hull center Y = average of both tread Y positions
        float centerY = (leftY + rightY) * 0.5f;

        Vector3 pos = transform.position;
        pos += driveDir * speed * dt;            // drives along slope, no swerve
        pos.y = centerY;                         // height from per-tread springs

        // ── 6. ROTATION ─────────────────────────────────────────────
        // Yaw is tracked as a standalone float.
        yaw += yawSpeed * dt;

        // Roll from tread height difference (geometric — pure terrain shape)
        float heightDiff = rightY - leftY;
        float targetRollDeg = Mathf.Atan2(heightDiff, treadHalfWidth * 2f) * Mathf.Rad2Deg;
        // Smooth the roll to prevent sudden angular jumps that whip the arm
        smoothedRollDeg = Mathf.MoveTowards(smoothedRollDeg, targetRollDeg,
                                            maxTiltRate * dt);

        // Pitch from terrain normal
        Vector3 desiredFwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 projFwd    = Vector3.ProjectOnPlane(desiredFwd, blendedPitch);
        if (projFwd.sqrMagnitude < 0.001f) projFwd = desiredFwd;
        projFwd.Normalize();

        Quaternion pitchYawRot = Quaternion.LookRotation(projFwd, blendedPitch);
        Quaternion targetRot = pitchYawRot * Quaternion.Euler(0f, 0f, smoothedRollDeg);

        // Rate-limit the final rotation so the arm never gets whipped
        smoothedRot = Quaternion.RotateTowards(smoothedRot, targetRot, maxTiltRate * dt);

        // ── 7. APPLY ────────────────────────────────────────────────
        body.TeleportRoot(pos, smoothedRot);
        body.linearVelocity  = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        // ── 8. ANIMATE TREADS & WHEELS ──────────────────────────────
        float fwd  = speed    * treadScrollScale;
        float turn = yawSpeed * treadTurnScale;

        float lSpd = fwd + turn;
        float rSpd = fwd - turn;

        if (leftTread  != null) leftTread.SetSpeed(lSpd);
        if (rightTread != null) rightTread.SetSpeed(rSpd);

        if (leftWheels  != null) leftWheels.SetSpeed(
            Mathf.Clamp(lSpd * wheelDegPerSpeed, -maxWheelDegPerSec, maxWheelDegPerSec));
        if (rightWheels != null) rightWheels.SetSpeed(
            Mathf.Clamp(rSpd * wheelDegPerSpeed, -maxWheelDegPerSec, maxWheelDegPerSec));
    }

    // ═══════════════════════════════════════════════════════════════
    //  RESET
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Teleport the hull back to its spawn position and zero all velocities.</summary>
    public void ResetToStart()
    {
        speed           = 0f;
        yawSpeed        = 0f;
        leftVertVel     = 0f;
        rightVertVel    = 0f;
        yaw             = spawnRot.eulerAngles.y;
        leftY           = spawnPos.y;
        rightY          = spawnPos.y;
        smoothedRollDeg = 0f;
        smoothedRot     = spawnRot;

        body.TeleportRoot(spawnPos, spawnRot);
        body.linearVelocity  = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        Debug.Log("[HullMover] Reset to spawn.");
    }

    // ═══════════════════════════════════════════════════════════════
    //  PER-TREAD SPRING
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Spring-damper for one tread side. If grounded, spring toward
    /// groundHeight + rideHeight. If airborne, freefall.
    /// Returns the new Y for this tread's contact point.
    /// </summary>
    private float SimulateTreadY(bool isGrounded, float groundHeight,
        float currentY, ref float vel, float dt)
    {
        if (isGrounded)
        {
            float target = groundHeight + rideHeight;
            float error  = target - currentY;
            float accel  = error * spring - vel * damper;
            vel += accel * dt;
            vel  = Mathf.Clamp(vel, -maxSpringVel, maxSpringVel);
        }
        else
        {
            // Freefall
            vel -= gravityAccel * dt;
            vel  = Mathf.Max(vel, -terminalVelocity);
        }
        return currentY + vel * dt;
    }

    // ═══════════════════════════════════════════════════════════════
    //  TREAD RAYCASTING
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Casts a row of downward rays along one tread.
    /// side: -1 = left, +1 = right
    /// </summary>
    private void CastTreadSide(float side,
        out bool grounded, out float avgHeight, out Vector3 avgNormal)
    {
        Vector3 center = transform.position + transform.right * (side * treadHalfWidth);
        Vector3 fwd    = transform.forward;
        Vector3 up     = Vector3.up;
        float   dist   = rayStartAbove + rayLength;

        Vector3 nSum = Vector3.zero;
        float   hSum = 0f;
        int     hits = 0;

        for (int i = 0; i < raysPerSide; i++)
        {
            float t = raysPerSide > 1
                ? ((float)i / (raysPerSide - 1)) * 2f - 1f   // -1 … +1
                : 0f;

            Vector3 origin = center + fwd * (t * treadHalfLength)
                           + up * rayStartAbove;

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                                dist, groundLayers, QueryTriggerInteraction.Ignore))
            {
                nSum += hit.normal;
                hSum += hit.point.y;
                hits++;
                Debug.DrawLine(origin, hit.point, side < 0 ? Color.cyan : Color.yellow);
            }
            else
            {
                Debug.DrawRay(origin, Vector3.down * dist, Color.red);
            }
        }

        // Store hit counts for debug HUD
        if (side < 0f) debugHitCountL = hits; else debugHitCountR = hits;

        grounded  = hits > 0;
        avgHeight = hits > 0 ? hSum / hits : 0f;
        avgNormal = hits > 0 ? (nSum / hits).normalized : Vector3.up;
    }

    // ═══════════════════════════════════════════════════════════════
    //  EDITOR GIZMOS
    // ═══════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════
    //  DEBUG HUD  (on-screen overlay — toggle with showDebugHUD)
    // ═══════════════════════════════════════════════════════════════

    private int debugHitCountL, debugHitCountR;

    void OnGUI()
    {
        if (!showDebugHUD) return;

        GUILayout.BeginArea(new Rect(10, 10, 350, 260));
        GUILayout.Label("<b><color=white>── HullMover Debug ──</color></b>",
            new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14 });

        var s = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12 };

        string clr(bool ok) => ok ? "lime" : "red";

        GUILayout.Label($"<color=white>Input  →  drive={inDrive:+0.0;-0.0}  turn={inTurn:+0.0;-0.0}</color>", s);
        float rollDeg = Mathf.Atan2(rightY - leftY, treadHalfWidth * 2f) * Mathf.Rad2Deg;
        GUILayout.Label($"<color=white>Speed  →  {speed:F2} m/s   yaw={yawSpeed:F1} °/s</color>", s);
        GUILayout.Label($"<color=white>Treads →  L={leftY:F2}  R={rightY:F2}  roll={rollDeg:F1}°</color>", s);
        GUILayout.Label($"<color={clr(leftGrounded)}>Left  grounded={leftGrounded}  hits={debugHitCountL}  ground={leftHeight:F2}</color>", s);
        GUILayout.Label($"<color={clr(rightGrounded)}>Right grounded={rightGrounded}  hits={debugHitCountR}  ground={rightHeight:F2}</color>", s);
        GUILayout.Label($"<color=white>Pos={transform.position}  Layer={gameObject.layer}</color>", s);
        GUILayout.Label($"<color=white>GroundMask={groundLayers.value}  RayDist={rayStartAbove + rayLength:F1}</color>", s);

        if (!leftGrounded && !rightGrounded)
            GUILayout.Label("<color=red><b>AIRBORNE — rays not hitting anything!</b></color>", s);
        if (Mathf.Approximately(inDrive, 0f) && Mathf.Approximately(inTurn, 0f))
            GUILayout.Label("<color=yellow>No input detected (WASD)</color>", s);

        GUILayout.EndArea();
    }

    // ═══════════════════════════════════════════════════════════════
    //  EDITOR GIZMOS
    // ═══════════════════════════════════════════════════════════════

    void OnDrawGizmosSelected()
    {
        Vector3 f = transform.forward, r = transform.right;
        Vector3 lf = transform.position - r * treadHalfWidth + f * treadHalfLength;
        Vector3 lb = transform.position - r * treadHalfWidth - f * treadHalfLength;
        Vector3 rf = transform.position + r * treadHalfWidth + f * treadHalfLength;
        Vector3 rb = transform.position + r * treadHalfWidth - f * treadHalfLength;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(lf, lb); Gizmos.DrawLine(rf, rb);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(lf, rf); Gizmos.DrawLine(lb, rb);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position - Vector3.up * rideHeight, 0.08f);
    }
}