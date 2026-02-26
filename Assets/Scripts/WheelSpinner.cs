using UnityEngine;

/// <summary>
/// Spins an array of wheel transforms around a local axis.
/// Driven from HullMover with differential speed per side.
/// Works with both regular transforms AND ArticulationBody wheels.
///
/// SETUP:
///   1. Create an empty GameObject under hull, name it "WheelSpinner_Left" / "WheelSpinner_Right"
///   2. Add this component
///   3. Drag all left-side Pwheel transforms into the Wheels array (or right-side for the other)
///   4. Set Spin Axis to match the wheel's local rotation axis (usually X or Z)
///   5. Drag into HullMover's Left/Right Wheels slots
/// </summary>
public class WheelSpinner : MonoBehaviour
{
    [Tooltip("All wheel transforms on this side")]
    public Transform[] wheels;

    [Tooltip("Local axis the wheels spin around. " +
             "For Blender exports this is usually X (1,0,0) or Z (0,0,1). " +
             "Try different axes if wheels spin the wrong way.")]
    public Vector3 spinAxis = Vector3.right; // (1, 0, 0)

    [Tooltip("Flip spin direction if wheels rotate backwards")]
    public bool reverseDirection = false;

    [Tooltip("Current rotation speed in degrees/sec — set by HullMover")]
    public float rotationSpeed = 0f;

    private ArticulationBody[] artBodies;
    private bool useArticulation = false;

    void Start()
    {
        if (wheels == null || wheels.Length == 0)
        {
            Debug.LogWarning($"[WheelSpinner] {name}: No wheels assigned!", this);
            return;
        }

        // Check if wheels use ArticulationBody (physics-driven)
        artBodies = new ArticulationBody[wheels.Length];
        for (int i = 0; i < wheels.Length; i++)
        {
            if (wheels[i] != null)
                artBodies[i] = wheels[i].GetComponent<ArticulationBody>();
        }
        // If the first wheel has an ArticulationBody, assume they all do
        useArticulation = artBodies[0] != null;

        Debug.Log($"[WheelSpinner] {name}: {wheels.Length} wheels, " +
                  $"ArticulationBody mode = {useArticulation}");
    }

    void Update()
    {
        if (wheels == null || wheels.Length == 0) return;

        if (Mathf.Abs(rotationSpeed) > 0.01f)
            Debug.Log($"[WheelSpinner] {name}: rotationSpeed = {rotationSpeed}");

        float angle = rotationSpeed * Time.deltaTime;
        if (reverseDirection) angle = -angle;

        for (int i = 0; i < wheels.Length; i++)
        {
            if (wheels[i] == null) continue;

            if (useArticulation && artBodies[i] != null)
            {
                // For ArticulationBody wheels: rotate visually by modifying
                // the anchor rotation, or just force the transform
                // (ArticulationBody in reduced coordinates may fight this,
                //  so we use localRotation directly which works for
                //  kinematic/locked articulation joints)
                wheels[i].localRotation *= Quaternion.AngleAxis(angle, spinAxis);
            }
            else
            {
                wheels[i].Rotate(spinAxis, angle, Space.Self);
            }
        }
    }

    /// <summary>
    /// Called by HullMover to set rotation speed.
    /// Positive = forward roll, negative = reverse.
    /// </summary>
    public void SetSpeed(float degreesPerSecond)
    {
        rotationSpeed = degreesPerSecond;
    }
}
