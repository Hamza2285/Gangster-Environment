using UnityEngine;

/// <summary>
/// Temporary add-on for the traffic car that answers a taxi call (one instance, reused by <see cref="TaxiService"/>):
/// - a roof sign so the player can tell which car is the taxi;
/// - a trigger on the Player layer with the Player tag, the same "TrafficSensorTarget" the drivable cars carry, so iTS
///   traffic brakes for the taxi while it is driven outside the traffic AI (it holds no lane reservations then).
/// </summary>
[DisallowMultipleComponent]
public class TaxiMarker : MonoBehaviour
{
    [SerializeField] private Transform roofSign;
    [Tooltip("Trigger on the Player layer, tagged Player (iTS player sensor target).")]
    [SerializeField] private BoxCollider trafficSensorTarget;
    [SerializeField, Min(0f)] private float roofClearance = 0.1f;
    [Tooltip("Used when the car has no solid box collider to measure.")]
    [SerializeField] private Vector3 fallbackCarSize = new Vector3(2f, 1.5f, 4.6f);

    private void Awake()
    {
        if (roofSign == null || trafficSensorTarget == null)
            Debug.LogWarning($"[{nameof(TaxiMarker)}] Missing roof sign or traffic sensor target on '{name}'.", this);
    }

    /// <summary>Parents the marker to the car and fits the sign and sensor to the car's body.</summary>
    public void AttachTo(Transform car)
    {
        transform.SetParent(car, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        Bounds local = MeasureCar(car);
        if (roofSign != null)
            roofSign.localPosition = new Vector3(local.center.x, local.max.y + roofClearance, local.center.z);
        if (trafficSensorTarget != null)
        {
            trafficSensorTarget.center = local.center;
            trafficSensorTarget.size = local.size;
        }
        gameObject.SetActive(true);
    }

    /// <summary>Takes the marker off the car and parks it (inactive) under the given parent.</summary>
    public void Detach(Transform parent)
    {
        gameObject.SetActive(false);
        transform.SetParent(parent, false);
    }

    // The car's physical body: its largest solid box collider, in the car's own space (once per call, never per frame).
    private Bounds MeasureCar(Transform car)
    {
        BoxCollider body = null;
        float bodyVolume = 0f;
        foreach (BoxCollider box in car.GetComponentsInChildren<BoxCollider>())
        {
            if (box.isTrigger || box.transform.IsChildOf(transform)) continue;
            Vector3 size = Vector3.Scale(box.size, box.transform.lossyScale);
            float volume = Mathf.Abs(size.x * size.y * size.z);
            if (volume > bodyVolume) { bodyVolume = volume; body = box; }
        }
        if (body == null) return new Bounds(new Vector3(0f, fallbackCarSize.y * 0.5f, 0f), fallbackCarSize);

        Vector3 center = car.InverseTransformPoint(body.transform.TransformPoint(body.center));
        Vector3 worldSize = Vector3.Scale(body.size, body.transform.lossyScale);
        Vector3 carScale = car.lossyScale;
        Vector3 localSize = new Vector3(Mathf.Abs(worldSize.x / carScale.x), Mathf.Abs(worldSize.y / carScale.y), Mathf.Abs(worldSize.z / carScale.z));
        return new Bounds(center, localSize);
    }
}
