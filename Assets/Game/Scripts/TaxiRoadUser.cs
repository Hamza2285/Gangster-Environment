using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Makes the taxi a real road user in the iTS system: it reserves the lane points it occupies, so traffic
// queues behind it and it queues behind traffic, and it asks for each junction connector before entering.
public sealed class TaxiRoadUser
{
    const int ReservationId = 987654;
    const float CarOccupation = 6f;
    const float BumperToCentre = 3f;
    const float RequestDistance = 45f;
    const float ReserveBehind = 4f;
    const float ReserveAhead = 8f;
    const float QueueGap = 3f;
    // Cars on the priority road can hold a request for a long time. After this wait the taxi may
    // take its turn, but only when nobody is actually inside the junction.
    const float JunctionPatience = 15f;
    // Traffic can lock up on its own. Only after this long does the taxi stop waiting for anything.
    const float GridlockPatience = 45f;

    // iTS keeps these two private; they are what its own cars use to claim a junction without a TSTrafficAI.
    static readonly MethodInfo ReserveMethod = typeof(TSLaneConnector).GetMethod("ReserveNearConnectorPoints",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(int), typeof(float), typeof(float) }, null);
    static readonly MethodInfo RequestSetter = typeof(TSLaneConnector).GetProperty("IsRequested",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetSetMethod(true);

    static void SetRequested(TSLaneConnector connector, bool value)
    {
        RequestSetter.Invoke(connector, new object[] { value });
    }

    static bool TryReserve(TSLaneConnector connector, float speed)
    {
        return (bool)ReserveMethod.Invoke(connector, new object[] { ReservationId, speed, CarOccupation });
    }

    sealed class Gate
    {
        public TSLaneConnector connector;
        public float stopAt;
        public float exitAt;
        public bool requested;
        public bool granted;
        public bool released;
    }

    readonly List<Gate> gates = new List<Gate>();
    readonly List<TSPoints> points = new List<TSPoints>();
    readonly List<float> pointAt = new List<float>();
    readonly HashSet<int> held = new HashSet<int>();
    float waited;

    public int JunctionsGranted { get; private set; }
    public int JunctionsForced { get; private set; }
    public float SecondsWaited { get; private set; }
    public int GridlocksBroken { get; private set; }
    public bool GaveUpWaiting => waited > GridlockPatience;

    public TaxiRoadUser(List<Vector3> path, float[] distance, RoadNetwork network, int firstLane, List<RoadNetwork.Link> links)
    {
        AddLanePoints(path, distance, network.lanes[firstLane].source);
        int cursor = 0;
        foreach (var link in links)
        {
            float start = Project(path, distance, link.points[0], ref cursor);
            int after = cursor;
            float end = Project(path, distance, link.points[link.points.Length - 1], ref after);
            gates.Add(new Gate
            {
                connector = link.source,
                stopAt = start - BumperToCentre - 1f,
                exitAt = end + BumperToCentre,
            });
            AddLanePoints(path, distance, network.lanes[link.to].source);
        }
    }

    void AddLanePoints(List<Vector3> path, float[] distance, TSLaneInfo lane)
    {
        int cursor = 0;
        foreach (var point in lane.points)
        {
            float along = Project(path, distance, point.point, ref cursor, 12f);
            if (along < 0f) continue;
            points.Add(point);
            pointAt.Add(along);
        }
    }

    // Distance along the path nearest the point, or -1 when requireWithin is set and the path doesn't pass close by.
    static float Project(List<Vector3> path, float[] distance, Vector3 point, ref int cursor, float requireWithin = 0f)
    {
        float best = float.MaxValue;
        int bestIndex = cursor;
        for (int i = cursor; i < path.Count; i++)
        {
            float d = (path[i] - point).sqrMagnitude;
            if (d < best)
            {
                best = d;
                bestIndex = i;
            }
        }
        if (requireWithin > 0f && best > requireWithin * requireWithin) return -1f;
        cursor = bestIndex;
        return distance[bestIndex];
    }

    // Call once per frame. Returns the fastest the car may go right now, given its braking ability.
    public float Tick(float travelled, float speed, float braking, float deltaTime)
    {
        float limit = float.MaxValue;
        bool gaveUp = GaveUpWaiting;

        foreach (var gate in gates)
        {
            if (gate.released) continue;
            if (travelled > gate.exitAt)
            {
                Release(gate);
                continue;
            }
            float toLine = gate.stopAt - travelled;
            if (!gate.granted)
            {
                if (toLine < RequestDistance && !gate.requested)
                {
                    SetRequested(gate.connector, true);
                    gate.requested = true;
                }
                // Claim it only when close, so a far-away taxi doesn't hold up a junction.
                if (toLine < Mathf.Max(8f, speed * 1.5f + 6f))
                {
                    if (TryReserve(gate.connector, speed))
                    {
                        gate.granted = true;
                        JunctionsGranted++;
                    }
                    else if (gaveUp || (waited > JunctionPatience && JunctionIsClear(gate.connector)))
                    {
                        gate.granted = true;
                        JunctionsForced++;
                        if (gaveUp) GridlocksBroken++;
                        waited = 0f;
                    }
                }
                if (!gate.granted) limit = Mathf.Min(limit, Mathf.Sqrt(2f * braking * Mathf.Max(0f, toLine)));
            }
            // Later junctions don't matter until this one is dealt with.
            if (toLine > RequestDistance) break;
        }

        limit = Mathf.Min(limit, UpdateLaneReservations(travelled, speed, braking, gaveUp));

        if (speed < 0.2f)
        {
            waited += deltaTime;
            SecondsWaited += deltaTime;
        }
        else if (speed > 1f)
        {
            waited = 0f;
        }
        return limit;
    }

    static bool JunctionIsClear(TSLaneConnector connector)
    {
        if (connector.OtherConnectors == null) return true;
        foreach (var other in connector.OtherConnectors)
        {
            if (other != null && other.IsReserved) return false;
        }
        return true;
    }

    float UpdateLaneReservations(float travelled, float speed, float braking, bool gaveUp)
    {
        float from = travelled - ReserveBehind;
        float to = travelled + ReserveAhead + speed;
        float limit = float.MaxValue;
        for (int i = 0; i < points.Count; i++)
        {
            float at = pointAt[i];
            if (at < from || at > to)
            {
                if (held.Remove(i)) points[i].TryUnReservePoint(ReservationId);
                continue;
            }
            if (held.Contains(i)) continue;
            if (points[i].TryReservePoint(ReservationId, null, false))
            {
                held.Add(i);
            }
            else if (!gaveUp && at > travelled)
            {
                // Someone is on that stretch: stop behind them.
                limit = Mathf.Min(limit, Mathf.Sqrt(2f * braking * Mathf.Max(0f, at - travelled - BumperToCentre - QueueGap)));
            }
        }
        return limit;
    }

    void Release(Gate gate)
    {
        gate.released = true;
        if (gate.requested) SetRequested(gate.connector, false);
        if (gate.granted) gate.connector.UnReserveLaneIdOnNearConnectorPoints(ReservationId, CarOccupation);
    }

    // Parked or removed: traffic may use everything again.
    public void ReleaseAll()
    {
        foreach (var gate in gates)
        {
            if (!gate.released) Release(gate);
        }
        foreach (int i in held) points[i].TryUnReservePoint(ReservationId);
        held.Clear();
    }
}
