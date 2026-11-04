using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Keeps iTS traffic moving, which it can't always do on its own in this city:
// - The cars' engines can't pull away from rest on the bridge ramps, so hill assist cancels the slope for them.
// - There are no traffic lights, so a busy road can hold a junction forever. A car that has waited too long gets
//   priority for a few seconds, one direction at a time.
// - A car that still can't move while its engine is pulling gets a shove.
public class TrafficFlowKeeper : MonoBehaviour
{
    const float CheckSeconds = 0.5f;
    const float StoppedSpeed = 0.5f;
    const float QueueLookAhead = 12f;
    const float QueueHalfWidth = 2.2f;
    const float JunctionReach = 16f;
    const int PriorityBoost = 100;
    const float MinSlopeDegrees = 2f;
    const float EngineOnThrottle = 0.1f;

    [Tooltip("How long a car waits at a junction before it is given a turn.")]
    public float patienceSeconds = 10f;
    [Tooltip("How long that turn lasts.")]
    public float turnSeconds = 7f;
    [Tooltip("How long a road waits before it can be given another turn.")]
    public float turnCooldownSeconds = 5f;
    [Tooltip("How long a car can pull without moving before it gets a shove.")]
    public float stuckSeconds = 12f;
    public float shoveSpeed = 3f;

    static readonly FieldInfo ThrottleField = typeof(TSTrafficAI).GetField("_throttle", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo BrakeField = typeof(TSTrafficAI).GetField("_brake", BindingFlags.Instance | BindingFlags.NonPublic);

    sealed class Car
    {
        public TSTrafficAI ai;
        public Rigidbody body;
        public float stoppedSince = -1f;
        public float pullingSince = -1f;
    }

    sealed class Turn
    {
        public int lane;
        public float until;
        public readonly Dictionary<TSLaneConnector, int> originalPriority = new Dictionary<TSLaneConnector, int>();
    }

    readonly List<Car> cars = new List<Car>();
    readonly List<Car> waiting = new List<Car>();
    readonly List<Turn> turns = new List<Turn>();
    readonly Dictionary<int, float> laneCooldown = new Dictionary<int, float>();
    readonly HashSet<TSLaneConnector> boosted = new HashSet<TSLaneConnector>();
    TSLaneInfo[] lanes;
    float nextCheck;
    float nextCarRefresh;

    public int TurnsGiven { get; private set; }
    public int Shoves { get; private set; }

    void Start()
    {
        var manager = FindFirstObjectByType<TSMainManager>();
        if (manager != null) lanes = manager.lanes;
    }

    void OnDisable()
    {
        for (int i = turns.Count - 1; i >= 0; i--) EndTurn(i);
    }

    void FixedUpdate()
    {
        for (int i = 0; i < cars.Count; i++)
        {
            Car car = cars[i];
            if (car.ai == null || !car.ai.isActiveAndEnabled || car.body == null) continue;
            Vector3 forward = car.ai.transform.forward;
            // Only uphill: downhill the car has gravity on its side already.
            float slope = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (slope < MinSlopeDegrees || !EnginePulling(car.ai)) continue;
            car.body.AddForce(forward * (Physics.gravity.magnitude * forward.y), ForceMode.Acceleration);
        }
    }

    static bool EnginePulling(TSTrafficAI ai)
    {
        return (float)ThrottleField.GetValue(ai) > EngineOnThrottle && (float)BrakeField.GetValue(ai) < EngineOnThrottle;
    }

    void Update()
    {
        if (lanes == null || Time.time < nextCheck) return;
        nextCheck = Time.time + CheckSeconds;

        if (Time.time >= nextCarRefresh)
        {
            nextCarRefresh = Time.time + 5f;
            RefreshCars();
        }

        waiting.Clear();
        for (int i = 0; i < cars.Count; i++)
        {
            Car car = cars[i];
            if (car.ai == null || !car.ai.isActiveAndEnabled || car.body == null) continue;
            if (car.body.linearVelocity.magnitude > StoppedSpeed)
            {
                car.stoppedSince = -1f;
                car.pullingSince = -1f;
                continue;
            }
            if (car.stoppedSince < 0f) car.stoppedSince = Time.time;

            if (EnginePulling(car.ai))
            {
                if (car.pullingSince < 0f) car.pullingSince = Time.time;
                if (Time.time - car.pullingSince > stuckSeconds)
                {
                    car.body.linearVelocity = car.ai.transform.forward * shoveSpeed;
                    car.pullingSince = -1f;
                    Shoves++;
                }
            }
            else
            {
                car.pullingSince = -1f;
            }

            if (Time.time - car.stoppedSince > patienceSeconds && !CarAhead(car)) waiting.Add(car);
        }

        for (int i = turns.Count - 1; i >= 0; i--)
        {
            if (Time.time >= turns[i].until) EndTurn(i);
        }

        // Longest wait goes first.
        waiting.Sort((a, b) => a.stoppedSince.CompareTo(b.stoppedSince));
        for (int i = 0; i < waiting.Count; i++) TryGiveTurn(waiting[i]);
    }

    void RefreshCars()
    {
        var found = FindObjectsByType<TSTrafficAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = cars.Count - 1; i >= 0; i--)
        {
            if (cars[i].ai == null) cars.RemoveAt(i);
        }
        for (int i = 0; i < found.Length; i++)
        {
            bool known = false;
            for (int k = 0; k < cars.Count && !known; k++) known = cars[k].ai == found[i];
            if (!known) cars.Add(new Car { ai = found[i], body = found[i].GetComponent<Rigidbody>() });
        }
    }

    // Someone in a queue isn't waiting for the junction, only for the car in front.
    bool CarAhead(Car car)
    {
        Transform t = car.ai.transform;
        Vector3 forward = t.forward;
        for (int i = 0; i < cars.Count; i++)
        {
            if (cars[i] == car || cars[i].ai == null) continue;
            Vector3 toOther = cars[i].ai.transform.position - t.position;
            float ahead = Vector3.Dot(toOther, forward);
            if (ahead < 0f || ahead > QueueLookAhead) continue;
            if ((toOther - forward * ahead).sqrMagnitude < QueueHalfWidth * QueueHalfWidth) return true;
        }
        return false;
    }

    void TryGiveTurn(Car car)
    {
        int lane = LaneWaitingAtJunction(car);
        if (lane < 0) return;
        if (laneCooldown.TryGetValue(lane, out float until) && Time.time < until) return;
        for (int i = 0; i < turns.Count; i++)
        {
            if (turns[i].lane == lane) return;
        }

        // Two roads that cross can't both have the turn.
        TSLaneConnector[] connectors = lanes[lane].connectors;
        for (int c = 0; c < connectors.Length; c++)
        {
            if (connectors[c].OtherConnectors == null) continue;
            foreach (var other in connectors[c].OtherConnectors)
            {
                if (boosted.Contains(other)) return;
            }
        }

        var turn = new Turn { lane = lane, until = Time.time + turnSeconds };
        for (int c = 0; c < connectors.Length; c++)
        {
            turn.originalPriority[connectors[c]] = connectors[c].priority;
            connectors[c].priority += PriorityBoost;
            boosted.Add(connectors[c]);
        }
        turns.Add(turn);
        TurnsGiven++;
    }

    int LaneWaitingAtJunction(Car car)
    {
        Transform t = car.ai.transform;
        int best = -1;
        float bestDistance = JunctionReach * JunctionReach;
        for (int l = 0; l < lanes.Length; l++)
        {
            TSPoints[] points = lanes[l].points;
            if (points.Length < 2 || lanes[l].connectors.Length == 0) continue;
            Vector3 end = points[points.Length - 1].point;
            Vector3 toEnd = end - t.position;
            float distance = toEnd.sqrMagnitude;
            if (distance >= bestDistance) continue;
            if (Vector3.Dot(toEnd, t.forward) < 0f) continue;
            if (Vector3.Dot(end - points[points.Length - 2].point, t.forward) < 0f) continue;
            // It has to be on this lane, not the one beside it.
            float sideways = (toEnd - t.forward * Vector3.Dot(toEnd, t.forward)).magnitude;
            if (sideways > QueueHalfWidth) continue;
            best = l;
            bestDistance = distance;
        }
        return best;
    }

    void EndTurn(int index)
    {
        Turn turn = turns[index];
        foreach (var pair in turn.originalPriority)
        {
            pair.Key.priority = pair.Value;
            boosted.Remove(pair.Key);
        }
        laneCooldown[turn.lane] = Time.time + turnCooldownSeconds;
        turns.RemoveAt(index);
    }
}
