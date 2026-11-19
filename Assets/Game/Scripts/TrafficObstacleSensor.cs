using System.Collections.Generic;
using UnityEngine;

// Makes traffic brake for what is in front of it that the traffic system itself does not see: the player's car, the player
// on foot, parked or abandoned cars and wrecks. iTS traffic only queues behind other traffic, and its own player sensor only
// finds colliders on the Player layer that are also tagged Player, so a driven car (Vehicle layer) gets rammed.
//
// Each traffic car near the player sweeps a box the width of the car along its nose, as far as it needs to stop from its
// current speed, and brakes in proportion to how close the obstacle is (full stop near it). Starts by itself in any scene
// with iTS traffic.
public class TrafficObstacleSensor : MonoBehaviour
{
    [Tooltip("Layers that traffic stops for (traffic cars themselves are handled by iTS).")]
    public LayerMask obstacleLayers;
    [Tooltip("Only cars this close to the player look ahead (the others are far from anything that matters).")]
    public float activeRadius = 140f;
    [Tooltip("Shortest and longest look-ahead (metres).")]
    public float minLookAhead = 4f, maxLookAhead = 26f;
    [Tooltip("Metres of look-ahead per m/s of speed (about the stopping distance plus a margin).")]
    public float lookAheadPerSpeed = 1.3f;
    [Tooltip("Gap kept to the obstacle when stopped (metres).")]
    public float stopGap = 2.2f;

    class Car
    {
        public TSTrafficAI ai;
        public Rigidbody body;
        public Transform transform;
        public Collider[] own;
        public float halfLength, halfWidth, height;
        public float braking, target;
    }

    // Each car looks ahead every few physics steps (staggered), keeping its last reading in between.
    const int LookEvery = 3;
    int tick;

    // iTS's own external brake (what its player sensor uses), which is not public in the compiled traffic library.
    static readonly System.Action<TSTrafficAI, float> addExternalBrake = MakeBrake();

    static System.Action<TSTrafficAI, float> MakeBrake()
    {
        var method = typeof(TSTrafficAI).GetMethod("AddExternalBrake",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (method == null) { Debug.LogWarning("[TrafficObstacleSensor] TSTrafficAI.AddExternalBrake not found; traffic will not brake for the player."); return null; }
        return (System.Action<TSTrafficAI, float>)System.Delegate.CreateDelegate(typeof(System.Action<TSTrafficAI, float>), method);
    }

    readonly List<Car> cars = new List<Car>();
    readonly HashSet<TSTrafficAI> known = new HashSet<TSTrafficAI>();
    readonly RaycastHit[] hits = new RaycastHit[12];
    float nextRefresh;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<TrafficObstacleSensor>() != null) return;
        if (FindFirstObjectByType<TSTrafficSpawner>(FindObjectsInactive.Include) == null) return;
        new GameObject("Traffic Obstacle Sensor").AddComponent<TrafficObstacleSensor>();
    }

    void Awake()
    {
        if (obstacleLayers.value == 0)
            obstacleLayers = LayerMask.GetMask("Vehicle", "Player");
    }

    void FixedUpdate()
    {
        if (Time.time >= nextRefresh) Refresh();
        var player = GamePlayer.Current;
        if (player == null) return;
        Vector3 centre = player.transform.position;
        float radiusSqr = activeRadius * activeRadius;
        tick++;

        for (int i = cars.Count - 1; i >= 0; i--)
        {
            var car = cars[i];
            if (car.ai == null) { cars.RemoveAt(i); continue; }
            if (!car.ai.isActiveAndEnabled || car.body == null || car.body.isKinematic) { car.braking = car.target = 0f; continue; }
            if ((car.transform.position - centre).sqrMagnitude > radiusSqr) { car.braking = car.target = 0f; continue; }

            if ((i + tick) % LookEvery == 0) car.target = Look(car);
            float brake = car.target;
            // Ease off rather than snapping off, so a car does not lurch forward the moment the obstacle leaves the box.
            car.braking = brake > car.braking ? brake : Mathf.MoveTowards(car.braking, brake, Time.fixedDeltaTime * 2f);
            if (car.braking > 0.01f && addExternalBrake != null) addExternalBrake(car.ai, car.braking);
        }
    }

    float Look(Car car)
    {
        Vector3 forward = car.transform.forward;
        float speed = Mathf.Max(0f, Vector3.Dot(car.body.linearVelocity, forward));
        float reach = Mathf.Clamp(minLookAhead + speed * lookAheadPerSpeed, minLookAhead, maxLookAhead);
        Vector3 start = car.transform.position + Vector3.up * (car.height * 0.5f) + forward * (car.halfLength - 0.3f);
        Vector3 half = new Vector3(car.halfWidth * 0.9f, car.height * 0.35f, 0.3f);

        int count = Physics.BoxCastNonAlloc(start, half, forward, hits, car.transform.rotation, reach, obstacleLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        for (int h = 0; h < count; h++)
        {
            var collider = hits[h].collider;
            if (collider == null || IsOwn(car, collider)) continue;
            if (collider.attachedRigidbody != null && collider.attachedRigidbody.GetComponent<TSTrafficAI>() != null) continue;   // iTS handles traffic
            if (hits[h].distance <= 0f && hits[h].point == Vector3.zero) continue;                                                  // started inside: ignore
            // Kerbs, low walls and the road itself are not in the way.
            if (collider.bounds.max.y < car.transform.position.y + 0.5f) continue;
            nearest = Mathf.Min(nearest, hits[h].distance);
        }
        if (nearest == float.MaxValue) return 0f;
        if (nearest <= stopGap) return 1f;
        // Harder the closer it is: full brake at the gap, none at the far end of the look-ahead.
        return Mathf.Clamp01(1f - (nearest - stopGap) / Mathf.Max(0.5f, reach - stopGap)) * 0.8f + 0.2f;
    }

    static bool IsOwn(Car car, Collider collider)
    {
        foreach (var c in car.own) if (c == collider) return true;
        return collider.transform.IsChildOf(car.transform);
    }

    void Refresh()
    {
        nextRefresh = Time.time + 4f;
        foreach (var ai in FindObjectsByType<TSTrafficAI>(FindObjectsSortMode.None))
        {
            if (!known.Add(ai)) continue;
            var car = new Car { ai = ai, transform = ai.transform, body = ai.GetComponent<Rigidbody>(), own = ai.GetComponentsInChildren<Collider>(true) };
            var renderers = ai.GetComponentsInChildren<Renderer>();
            Bounds local = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var r in renderers)
            {
                if (r is ParticleSystemRenderer) continue;
                var b = r.bounds;
                // Size in the car's own axes, from the world bounds of each part.
                Vector3 c = ai.transform.InverseTransformPoint(b.center);
                Vector3 e = ai.transform.InverseTransformVector(b.extents);
                var lb = new Bounds(c, new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z)) * 2f);
                if (first) { local = lb; first = false; } else local.Encapsulate(lb);
            }
            car.halfLength = first ? 2.3f : Mathf.Max(Mathf.Abs(local.max.z), Mathf.Abs(local.min.z));
            car.halfWidth = first ? 0.9f : Mathf.Clamp(local.extents.x, 0.35f, 1.3f);
            car.height = first ? 1.4f : Mathf.Clamp(local.max.y, 1f, 3f);
            cars.Add(car);
        }
    }
}
