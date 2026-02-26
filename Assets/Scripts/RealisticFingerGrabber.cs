using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RealisticFingerGrabber : MonoBehaviour
{
    [Header("Finger Auto-Detection")]
    [Tooltip("Parent transforms containing finger chains. If empty, searches this GameObject's children.")]
    public Transform[] fingerRoots;

    [Header("Finger Rotation")]
    public float rotationSpeed = 50f;
    public float maxCurlAngle = -80f;

    [Header("Drive Tuning")]
    public float driveStiffness = 800f;
    public float driveDamping = 80f;
    public float driveForceLimit = 5000f;

    [Header("Hold Tuning (while grabbed)")]
    public float holdStiffness = 200f;
    public float holdDamping = 60f;
    public float holdForceLimit = 500f;

    [Header("Stall Detection")]
    [Tooltip("If the joint error exceeds this AND the joint isn't moving, the finger is stalled")]
    public float stallErrorThreshold = 1f;
    [Tooltip("Movement per frame below this means the joint is stuck")]
    public float stallMoveThreshold = 0.15f;
    [Tooltip("How many consecutive stalled frames before we block the segment")]
    public int stallFrameCount = 3;

    [Header("Grab Settings")]
    public int requiredSegmentsToGrab = 3;
    public Rigidbody grabAnchor;
    public int grabSettleFrames = 4;

    [HideInInspector] public ArticulationBody[] fingerSegments;

    // Per-segment arrays
    private float[] fingerTargets;
    private float[] driveTargets;
    private float[] frozenAngles;
    private float[] lastAngles;
    private int[] stallCounters;
    private bool[] isStalled;

    // Collision-based tracking (for grab detection)
    private readonly Dictionary<Rigidbody, HashSet<ArticulationBody>> touchingSegments = new();
    private readonly Dictionary<Rigidbody, FixedJoint> grabbedObjects = new();

    private bool isGrabbing;
    private bool anglesFrozen;
    private Coroutine grabCoroutine;

    void Awake()
    {
        // ── Auto-detect finger segments ──
        List<ArticulationBody> found = new();
        Transform[] roots = (fingerRoots != null && fingerRoots.Length > 0)
            ? fingerRoots
            : new Transform[] { transform };

        foreach (var root in roots)
        {
            foreach (var ab in root.GetComponentsInChildren<ArticulationBody>())
            {
                if (ab.gameObject == gameObject) continue;
                found.Add(ab);
            }
        }

        fingerSegments = found.ToArray();
        int count = fingerSegments.Length;
        fingerTargets = new float[count];
        driveTargets = new float[count];
        frozenAngles = new float[count];
        lastAngles = new float[count];
        stallCounters = new int[count];
        isStalled = new bool[count];

        for (int i = 0; i < count; i++)
        {
            var seg = fingerSegments[i];
            float deg = seg.jointPosition[0] * Mathf.Rad2Deg;
            fingerTargets[i] = deg;
            driveTargets[i] = deg;
            frozenAngles[i] = deg;
            lastAngles[i] = deg;
            stallCounters[i] = 0;
            isStalled[i] = false;

            if (seg.isRoot)
            {
                seg.solverIterations = 20;
                seg.solverVelocityIterations = 10;
            }

            // Attach collision reporter (still needed for grab detection)
            var reporter = seg.gameObject.GetComponent<FingerCollisionReporter>();
            if (reporter == null)
                reporter = seg.gameObject.AddComponent<FingerCollisionReporter>();
            reporter.grabber = this;
            reporter.segment = seg;
        }

        Debug.Log($"[FingerGrabber] Found {count} finger segments.");
    }

    void FixedUpdate()
    {
        for (int i = 0; i < fingerSegments.Length; i++)
        {
            UpdateStallDetection(i);
            RotateSegment(i);
        }
    }

    // ───────── Stall Detection (replaces collision-based blocking) ─────────

    private void UpdateStallDetection(int i)
    {
        if (isGrabbing && anglesFrozen)
        {
            // Don't update stall while holding — everything is frozen
            return;
        }

        ArticulationBody seg = fingerSegments[i];
        float currentAngle = seg.jointPosition[0] * Mathf.Rad2Deg;
        float error = Mathf.Abs(driveTargets[i] - currentAngle);
        float delta = Mathf.Abs(currentAngle - lastAngles[i]);

        if (error > stallErrorThreshold && delta < stallMoveThreshold)
        {
            stallCounters[i]++;
            if (stallCounters[i] >= stallFrameCount)
            {
                if (!isStalled[i])
                {
                    isStalled[i] = true;
                    // Snap targets to where the finger actually is
                    fingerTargets[i] = currentAngle;
                    driveTargets[i] = currentAngle;
                    Debug.Log($"[FingerGrabber] Segment {i} ({seg.name}) STALLED at {currentAngle:F1}°");
                }
            }
        }
        else
        {
            // If the finger is moving or the error is small, clear stall
            if (stallCounters[i] > 0 || isStalled[i])
            {
                stallCounters[i] = 0;
                isStalled[i] = false;
            }
        }

        lastAngles[i] = currentAngle;
    }

    // ───────── Public API ─────────

    public void CurlFinger(int index, float input)
    {
        if (index < 0 || index >= fingerSegments.Length) return;
        if (isGrabbing) return;
        if (isStalled[index] && input < 0) return; // block further curling when stalled (but allow extend)

        fingerTargets[index] += input * rotationSpeed * Time.fixedDeltaTime;
        fingerTargets[index] = Mathf.Clamp(fingerTargets[index], maxCurlAngle, 0f);
    }

    public void CurlAllFingers(float input)
    {
        for (int i = 0; i < fingerSegments.Length; i++)
            CurlFinger(i, input);
    }

    // ───────── Internal rotation ─────────

    private void RotateSegment(int i)
    {
        ArticulationBody segment = fingerSegments[i];
        var drive = segment.xDrive;

        if (isGrabbing && anglesFrozen)
        {
            // Holding an object — constant frozen angles, low force
            drive.target = frozenAngles[i];
            drive.stiffness = holdStiffness;
            drive.damping = holdDamping;
            drive.forceLimit = holdForceLimit;
        }
        else if (isStalled[i])
        {
            // Segment is stalled against something — hold at current position with low force
            drive.target = driveTargets[i];
            drive.stiffness = holdStiffness;
            drive.damping = holdDamping;
            drive.forceLimit = holdForceLimit;
        }
        else
        {
            // Normal free movement
            driveTargets[i] = Mathf.MoveTowards(
                driveTargets[i],
                fingerTargets[i],
                rotationSpeed * Time.fixedDeltaTime
            );
            drive.target = driveTargets[i];
            drive.stiffness = driveStiffness;
            drive.damping = driveDamping;
            drive.forceLimit = driveForceLimit;
        }

        drive.driveType = ArticulationDriveType.Target;
        segment.xDrive = drive;
    }

    // ───────── Collision callbacks (for grab joint only) ─────────

    public void OnSegmentCollisionEnter(ArticulationBody segment, Collision col)
    {
        Rigidbody rb = col.rigidbody;
        if (rb == null) return;

        // Accept either tag or layer — more forgiving
        if (!IsGrabbable(col.gameObject)) return;

        if (!touchingSegments.ContainsKey(rb))
            touchingSegments[rb] = new HashSet<ArticulationBody>();
        touchingSegments[rb].Add(segment);

        Debug.Log($"[FingerGrabber] Contact: {segment.name} -> {col.gameObject.name} (total contacts: {touchingSegments[rb].Count})");

        if (!grabbedObjects.ContainsKey(rb) &&
            touchingSegments[rb].Count >= requiredSegmentsToGrab)
        {
            if (grabCoroutine != null) StopCoroutine(grabCoroutine);
            grabCoroutine = StartCoroutine(DelayedGrab(rb));
        }
    }

    public void OnSegmentCollisionStay(ArticulationBody segment, Collision col)
    {
        // Keep the segment registered as touching
        Rigidbody rb = col.rigidbody;
        if (rb == null) return;
        if (!IsGrabbable(col.gameObject)) return;

        if (!touchingSegments.ContainsKey(rb))
            touchingSegments[rb] = new HashSet<ArticulationBody>();
        touchingSegments[rb].Add(segment);
    }

    public void OnSegmentCollisionExit(ArticulationBody segment, Collision col)
    {
        Rigidbody rb = col.rigidbody;
        if (rb == null) return;
        if (!IsGrabbable(col.gameObject)) return;

        // Don't lose contacts during an active grab
        if (isGrabbing && grabbedObjects.ContainsKey(rb)) return;

        if (touchingSegments.ContainsKey(rb))
        {
            touchingSegments[rb].Remove(segment);
            if (touchingSegments[rb].Count < requiredSegmentsToGrab)
                Release(rb);
            if (touchingSegments[rb].Count == 0)
                touchingSegments.Remove(rb);
        }
    }

    // ───────── Grab / Release ─────────

    private IEnumerator DelayedGrab(Rigidbody rb)
    {
        isGrabbing = true;
        SnapshotAllFingers();

        for (int i = 0; i < grabSettleFrames; i++)
            yield return new WaitForFixedUpdate();

        if (rb == null || grabbedObjects.ContainsKey(rb))
        {
            if (grabbedObjects.Count == 0) { isGrabbing = false; anglesFrozen = false; }
            yield break;
        }
        if (!touchingSegments.ContainsKey(rb) ||
             touchingSegments[rb].Count < requiredSegmentsToGrab)
        {
            if (grabbedObjects.Count == 0) { isGrabbing = false; anglesFrozen = false; }
            yield break;
        }

        rb.linearVelocity = grabAnchor.linearVelocity;
        rb.angularVelocity = grabAnchor.angularVelocity;

        FixedJoint joint = rb.gameObject.AddComponent<FixedJoint>();
        joint.connectedBody = grabAnchor;
        joint.breakForce = 8000f;
        joint.breakTorque = 8000f;
        joint.enablePreprocessing = false;

        grabbedObjects[rb] = joint;
        Debug.Log($"[FingerGrabber] GRABBED {rb.name}");
    }

    private void Release(Rigidbody rb)
    {
        if (!grabbedObjects.ContainsKey(rb)) return;

        if (grabbedObjects[rb] != null)
            Destroy(grabbedObjects[rb]);
        grabbedObjects.Remove(rb);
        Debug.Log($"[FingerGrabber] RELEASED {rb.name}");

        if (grabbedObjects.Count == 0)
        {
            isGrabbing = false;
            anglesFrozen = false;
            // Clear stall so fingers can move freely after release
            for (int i = 0; i < fingerSegments.Length; i++)
            {
                stallCounters[i] = 0;
                isStalled[i] = false;
            }
        }
    }

    private void SnapshotAllFingers()
    {
        for (int i = 0; i < fingerSegments.Length; i++)
        {
            float deg = fingerSegments[i].jointPosition[0] * Mathf.Rad2Deg;
            frozenAngles[i] = deg;
            fingerTargets[i] = deg;
            driveTargets[i] = deg;

            var drive = fingerSegments[i].xDrive;
            drive.target = deg;
            drive.stiffness = holdStiffness;
            drive.damping = holdDamping;
            drive.forceLimit = holdForceLimit;
            fingerSegments[i].xDrive = drive;
        }
        anglesFrozen = true;
    }

    // ───────── Helpers ─────────

    private bool IsGrabbable(GameObject obj)
    {
        // Try tag first, fall back to name contains "grab" for easier setup
        if (obj.CompareTag("Grabbable")) return true;
        if (obj.name.ToLower().Contains("grab")) return true;
        // Also accept if a Rigidbody exists and isn't kinematic (generic fallback)
        // Remove this line if you only want tagged objects:
        // var rb = obj.GetComponent<Rigidbody>();
        // if (rb != null && !rb.isKinematic) return true;
        return false;
    }
}