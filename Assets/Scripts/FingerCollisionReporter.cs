using UnityEngine;

public class FingerCollisionReporter : MonoBehaviour
{
    [HideInInspector] public RealisticFingerGrabber grabber;
    [HideInInspector] public ArticulationBody segment;

    void OnCollisionEnter(Collision col)
    {
        Debug.Log($"[FingerCollision] {name} ENTER with {col.gameObject.name}");
        grabber?.OnSegmentCollisionEnter(segment, col);
    }

    void OnCollisionStay(Collision col)
    {
        grabber?.OnSegmentCollisionStay(segment, col);
    }

    void OnCollisionExit(Collision col)
    {
        Debug.Log($"[FingerCollision] {name} EXIT with {col.gameObject.name}");
        grabber?.OnSegmentCollisionExit(segment, col);
    }

    // Fallback: if colliders are set as triggers
    void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[FingerCollision] {name} TRIGGER ENTER with {other.gameObject.name} — collider is a trigger!");
    }
}