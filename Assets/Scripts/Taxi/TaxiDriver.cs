using System.Collections.Generic;
using UnityEngine;

// Drives a traffic car (whose iTS AI is paused) along a fixed route and pulls over at the end.
public class TaxiDriver : MonoBehaviour
{
    const float WheelBase = 2.6f;
    const float MaxSteerDegrees = 55f;

    public enum State { Driving, Arrived }

    public State CurrentState { get; private set; }
    public float RemainingDistance { get; private set; }

    public float cruiseSpeed = 11f;
    public float turnSpeed = 5f;
    public float comfortableDecel = 3f;
    public float obstacleDecel = 4.5f;
    [Tooltip("Distance kept to whatever is ahead, in metres.")]
    public float followGap = 6f;
    [Tooltip("Only brake for things that move (cars, people, the player), not fixed scenery such as bollards.")]
    public bool ignoreStaticObstacles = false;
    [Tooltip("Set by the owner to wait where it is (e.g. giving way before joining a road).")]
    public bool hold = false;

    private TSSimpleCar _car;
    private Rigidbody _body;
    private Transform _front;
    private readonly List<Vector3> _route = new List<Vector3>();
    private float[] _along;
    private int _progress;
    private Collider[] _ownColliders;

    // A junction entry on the route: where to stop, and the light that controls it.
    public struct StopLine
    {
        public int routeIndex;
        public TSTrafficLight light;
    }

    private readonly List<float> _stopAlong = new List<float>();
    private readonly List<TSTrafficLight> _stopLights = new List<TSTrafficLight>();

    public void Begin(List<Vector3> route, TSSimpleCar car, Transform frontPoint, List<StopLine> stopLines = null)
    {
        _car = car;
        _body = GetComponent<Rigidbody>();
        _front = frontPoint;
        _ownColliders = GetComponentsInChildren<Collider>(true);
        _route.Clear();
        _route.AddRange(route);
        _along = new float[_route.Count];
        for (int i = 1; i < _route.Count; i++) _along[i] = _along[i - 1] + Vector3.Distance(_route[i - 1], _route[i]);
        _progress = 0;
        CurrentState = State.Driving;
        RemainingDistance = _along[_along.Length - 1];

        _stopAlong.Clear();
        _stopLights.Clear();
        if (stopLines != null)
        {
            foreach (var s in stopLines)
            {
                if (s.light == null || s.routeIndex < 0 || s.routeIndex >= _along.Length) continue;
                _stopAlong.Add(_along[s.routeIndex]);
                _stopLights.Add(s.light);
            }
        }
    }

    // Stop where it is and wait (e.g. the passenger got in before the pickup spot).
    public void StopHere()
    {
        CurrentState = State.Arrived;
        RemainingDistance = 0f;
    }

    // Speed limit that brings the car to rest at the next junction if its light isn't green.
    private float RedLightLimit(float along, float speed)
    {
        for (int i = 0; i < _stopAlong.Count; i++)
        {
            float toLine = _stopAlong[i] - along;
            if (toLine < -0.5f) continue;
            if (toLine > 70f) break;
            TSTrafficLight light = _stopLights[i];
            var type = light.lights[Mathf.Clamp(light.lightToPlay, 0, light.lights.Count - 1)].lightType;
            if (type == TSTrafficLight.LightType.Green) return float.MaxValue;
            // Too close to stop comfortably on yellow: carry on through.
            if (type == TSTrafficLight.LightType.Yellow && toLine < speed * 1.2f) return float.MaxValue;
            return Mathf.Sqrt(2f * obstacleDecel * Mathf.Max(0f, toLine - 1.5f));
        }
        return float.MaxValue;
    }

    private void FixedUpdate()
    {
        if (_car == null || _route.Count < 2) return;

        if (CurrentState == State.Arrived)
        {
            _car.OnAIUpdate(0f, 1f, 0f, false);
            return;
        }

        Vector3 pos = transform.position;
        AdvanceProgress(pos);
        float total = _along[_along.Length - 1];
        Vector3 end = _route[_route.Count - 1];
        RemainingDistance = Mathf.Max(0f, total - AlongAt(pos));
        float speed = Vector3.Dot(_body.linearVelocity, transform.forward);

        if (Flat(end - pos).magnitude < 1.5f || (RemainingDistance < 1f && speed < 1f))
        {
            CurrentState = State.Arrived;
            _car.OnAIUpdate(0f, 1f, 0f, false);
            return;
        }

        // Pure pursuit toward a point further along the route.
        float lookAhead = Mathf.Clamp(Mathf.Abs(speed) * 0.9f, 5f, 12f);
        Vector3 target = PointAtDistance(AlongAt(pos) + lookAhead);
        Vector3 local = transform.InverseTransformPoint(target);
        float alpha = Mathf.Atan2(local.x, Mathf.Max(0.1f, local.z));
        float steerDegrees = Mathf.Atan2(2f * WheelBase * Mathf.Sin(alpha), lookAhead) * Mathf.Rad2Deg;
        float steer = Mathf.Clamp(steerDegrees / MaxSteerDegrees, -1f, 1f);

        // Speed: slow for sharp turns ahead, brake smoothly into the stop, and yield to anything in front.
        float desired = cruiseSpeed;
        float turn = TurnAngleAhead(AlongAt(pos), 18f);
        if (turn > 25f) desired = Mathf.Lerp(cruiseSpeed, turnSpeed, Mathf.InverseLerp(25f, 70f, turn));
        desired = Mathf.Min(desired, Mathf.Sqrt(2f * comfortableDecel * Mathf.Max(0f, RemainingDistance - 1f)) + 0.5f);
        desired = Mathf.Min(desired, RedLightLimit(AlongAt(pos), speed));
        // Keep a real gap: other traffic's sensors react to the taxi if it presses up against them.
        float clearance = ObstacleDistance(Mathf.Max(10f, speed * 2.2f + 10f));
        if (clearance < float.MaxValue)
            desired = Mathf.Min(desired, Mathf.Sqrt(2f * obstacleDecel * Mathf.Max(0f, clearance - followGap)));

        float throttle = 0f, brake = 0f;
        if (speed < desired - 0.3f) throttle = Mathf.Clamp01((desired - speed) * 0.35f);
        else if (speed > desired + 0.3f) brake = Mathf.Clamp01((speed - desired) * 0.6f + 0.2f);
        if (hold) desired = 0f;
        if (desired < 0.2f) { throttle = 0f; brake = 1f; }

        _car.OnAIUpdate(steer, brake, throttle, false);
    }

    private void AdvanceProgress(Vector3 pos)
    {
        float best = float.MaxValue;
        int bestIndex = _progress;
        int limit = Mathf.Min(_route.Count - 1, _progress + 30);
        for (int i = _progress; i <= limit; i++)
        {
            float d = Flat(_route[i] - pos).sqrMagnitude;
            if (d < best) { best = d; bestIndex = i; }
        }
        _progress = bestIndex;
    }

    private float AlongAt(Vector3 pos)
    {
        int i = Mathf.Clamp(_progress, 0, _route.Count - 2);
        Vector3 a = _route[i], b = _route[i + 1];
        Vector3 ab = Flat(b - a);
        float t = ab.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector3.Dot(Flat(pos - a), ab) / ab.sqrMagnitude) : 0f;
        return _along[i] + t * ab.magnitude;
    }

    private Vector3 PointAtDistance(float d)
    {
        if (d >= _along[_along.Length - 1]) return _route[_route.Count - 1];
        for (int i = Mathf.Max(1, _progress); i < _along.Length; i++)
        {
            if (_along[i] >= d)
            {
                float seg = _along[i] - _along[i - 1];
                return Vector3.Lerp(_route[i - 1], _route[i], seg > 1e-4f ? (d - _along[i - 1]) / seg : 0f);
            }
        }
        return _route[_route.Count - 1];
    }

    private float TurnAngleAhead(float from, float distance)
    {
        Vector3 a = PointAtDistance(from), b = PointAtDistance(from + distance * 0.5f), c = PointAtDistance(from + distance);
        Vector3 d1 = Flat(b - a), d2 = Flat(c - b);
        if (d1.sqrMagnitude < 0.01f || d2.sqrMagnitude < 0.01f) return 0f;
        return Vector3.Angle(d1, d2);
    }

    // Distance to the nearest thing ahead in the lane (cars, people, the player, poles), or MaxValue.
    private float ObstacleDistance(float range)
    {
        Vector3 origin = (_front != null ? _front.position : transform.position + transform.forward * 2f) + Vector3.up * 0.4f;
        float best = float.MaxValue;
        foreach (var hit in Physics.SphereCastAll(origin, 0.9f, transform.forward, range, ~0, QueryTriggerInteraction.Ignore))
        {
            if (IsOwn(hit.collider) || hit.normal.y > 0.7f) continue;
            if (ignoreStaticObstacles && hit.collider.attachedRigidbody == null && hit.collider.GetComponentInParent<PedestrianWalker>() == null
                && !hit.collider.CompareTag("Player")) continue;
            if (hit.distance < best) best = hit.distance;
        }
        return best;
    }

    private bool IsOwn(Collider c)
    {
        for (int i = 0; i < _ownColliders.Length; i++) if (_ownColliders[i] == c) return true;
        return false;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
