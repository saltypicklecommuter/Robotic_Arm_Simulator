using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Splines;

/// <summary>
/// Spawns tread-link prefabs evenly along a closed Spline and moves them each frame.
/// 
/// SETUP:
///   1. Create an empty child of the hull, add a SplineContainer, 
///      draw a closed loop around the wheels, and assign it here.
///   2. Create a prefab from ONE tread link mesh. 
///      The link's local Z+ should point "forward" (direction of travel).
///   3. Drag the prefab into "Tread Link Prefab".
///   4. Set Link Count so the links sit snug with no gaps.
///      You can also use "Auto Fit" in the context menu to let the script guess.
/// </summary>
public class TreadSystem : MonoBehaviour
{
    [Header("Spline")]
    [Tooltip("The SplineContainer with a closed loop around the wheels")]
    public SplineContainer splineContainer;

    [Header("Tread Links")]
    [Tooltip("Prefab of a single tread link")]
    public GameObject treadLinkPrefab;

    [Tooltip("How many links to place around the spline")]
    public int linkCount = 40;

    [Header("Offset Tuning")]
    [Tooltip("Local offset applied to each link (use Y to push links outward from spline)")]
    public Vector3 localOffset = Vector3.zero;

    [Tooltip("Extra rotation applied to every link (Euler degrees). " +
             "Use this if the link faces the wrong way.")]
    public Vector3 rotationFix = Vector3.zero;

    [Header("Direction")]
    [Tooltip("Tick this if the tread moves backwards relative to the hull")]
    public bool reverseDirection = false;

    [Header("Motion")]
    [Tooltip("Current speed — driven from HullMover or TankDriveController")]
    public float speed = 0f;

    // runtime data
    private Transform[] links;
    private float[] tValues;

    void Start()
    {
        if (splineContainer == null || treadLinkPrefab == null)
        {
            Debug.LogError($"[TreadSystem] Missing references on {name}!", this);
            enabled = false;
            return;
        }

        if (splineContainer.Spline == null || splineContainer.Spline.Count < 2)
        {
            Debug.LogError($"[TreadSystem] Spline on {splineContainer.name} has no knots or too few knots!", this);
            enabled = false;
            return;
        }

        links  = new Transform[linkCount];
        tValues = new float[linkCount];

        for (int i = 0; i < linkCount; i++)
        {
            tValues[i] = (float)i / linkCount;

            GameObject go = Instantiate(treadLinkPrefab, transform);
            go.name = $"TreadLink_{i}";
            go.SetActive(true);
            links[i] = go.transform;

            PositionLink(i);
        }
    }

    void Update()
    {
        if (links == null) return;

        float delta = speed * Time.deltaTime;
        if (reverseDirection) delta = -delta;

        for (int i = 0; i < linkCount; i++)
        {
            tValues[i] = (tValues[i] + delta) % 1f;
            if (tValues[i] < 0f) tValues[i] += 1f;

            PositionLink(i);
        }
    }

    private void PositionLink(int i)
    {
        Spline spline = splineContainer.Spline;
        spline.Evaluate(tValues[i], out float3 localPos, out float3 tangent, out float3 up);

        Transform st = splineContainer.transform;

        Vector3 worldPos     = st.TransformPoint(localPos);
        Vector3 worldTangent = st.TransformDirection(math.normalize(tangent));
        Vector3 worldUp      = st.TransformDirection(math.normalize(up));

        // Base rotation: Z+ along tangent, Y+ along up
        Quaternion baseRot = Quaternion.LookRotation(worldTangent, worldUp);

        // Apply user rotation fix
        Quaternion finalRot = baseRot * Quaternion.Euler(rotationFix);

        links[i].position = worldPos + finalRot * localOffset;
        links[i].rotation = finalRot;
    }

    /// <summary>
    /// Call from your vehicle controller to animate the treads.
    /// Positive = forward, negative = reverse.
    /// </summary>
    public void SetSpeed(float newSpeed)
    {
        speed = newSpeed;
    }
}
