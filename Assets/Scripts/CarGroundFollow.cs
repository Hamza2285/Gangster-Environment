using UnityEngine;

// Keeps a transform-driven car on the road surface (bridges, ramps, underpasses).
// Runs after CarController.Update so it corrects the height of the frame's movement.
[DefaultExecutionOrder(100)]
public class CarGroundFollow : MonoBehaviour
{
    [Tooltip("How far above the car the ground check starts. Must be lower than bridge clearance.")]
    public float probeHeight = 3f;
    [Tooltip("How far below the car the ground check reaches.")]
    public float probeDepth = 6f;
    [Tooltip("How quickly the car tilts to match slopes.")]
    public float alignSpeed = 10f;
    [Tooltip("Small lift so wheels sit on the surface instead of clipping it.")]
    public float heightOffset = 0f;

    readonly RaycastHit[] hits = new RaycastHit[16];

    void Update()
    {
        Vector3 origin = transform.position + Vector3.up * probeHeight;
        int count = Physics.RaycastNonAlloc(
            origin, Vector3.down, hits, probeHeight + probeDepth,
            ~0, QueryTriggerInteraction.Ignore
        );

        float best = float.MaxValue;
        RaycastHit ground = default;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.transform.IsChildOf(transform)) continue;
            if (hit.collider.attachedRigidbody != null) continue; // ignore traffic cars
            if (hit.normal.y < 0.5f) continue;                    // ignore walls and barriers
            if (hit.distance < best)
            {
                best = hit.distance;
                ground = hit;
                found = true;
            }
        }
        if (!found) return;

        Vector3 pos = transform.position;
        pos.y = ground.point.y + heightOffset;
        transform.position = pos;

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, ground.normal);
        if (forward.sqrMagnitude > 0.0001f)
        {
            Quaternion target = Quaternion.LookRotation(forward.normalized, ground.normal);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, target, alignSpeed * Time.deltaTime
            );
        }
    }
}
