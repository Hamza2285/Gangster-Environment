using System.Collections.Generic;
using UnityEngine;

// Safety net for the road surfaces: at high speed (a hard landing off a bump or ramp) a car could still slip through a thin
// one-sided road mesh and fall into the void under it. Every driven car (RCC) is watched; one that is dropping with nothing
// under it while a road surface is just above it is put back on that road, keeping its speed along the ground.
// (Cars legitimately under a bridge always have ground right below them, so they are never moved.) Starts by itself.
public class VehicleFallRescue : MonoBehaviour
{
    const float ScanInterval = 1f;      // how often the list of cars is refreshed
    const float GroundBelow = 3f;       // a car with ground this close underneath is fine
    const float RoadAbove = 8f;         // a road surface within this height above a falling car is where it came through
    const float FallSpeed = -3f;        // only cars actually dropping

    readonly List<RCC_CarControllerV3> cars = new List<RCC_CarControllerV3>();
    readonly RaycastHit[] hits = new RaycastHit[48];   // roomy: a car brings many colliders of its own
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<VehicleFallRescue>() != null) return;
        var go = new GameObject("Vehicle Fall Rescue");
        DontDestroyOnLoad(go);
        go.AddComponent<VehicleFallRescue>();
    }

    void FixedUpdate()
    {
        if (Time.time >= nextScan)
        {
            nextScan = Time.time + ScanInterval;
            cars.Clear();
            cars.AddRange(FindObjectsByType<RCC_CarControllerV3>(FindObjectsSortMode.None));
        }
        foreach (var car in cars)
        {
            if (car == null || !car.isActiveAndEnabled) continue;
            var body = car.GetComponent<Rigidbody>();
            if (body == null || body.isKinematic || body.linearVelocity.y > FallSpeed) continue;
            Check(car.transform, body);
        }
    }

    void Check(Transform car, Rigidbody body)
    {
        Vector3 centre = body.worldCenterOfMass;
        // Ground right under the car: all good.
        int n = Physics.RaycastNonAlloc(centre, Vector3.down, hits, GroundBelow, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++) if (!hits[i].collider.transform.IsChildOf(car)) return;

        // A surface just above it, seen from above: the road it fell through.
        Vector3 from = centre + Vector3.up * RoadAbove;
        n = Physics.RaycastNonAlloc(from, Vector3.down, hits, RoadAbove, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        RaycastHit road = default;
        for (int i = 0; i < n; i++)
        {
            if (hits[i].collider.transform.IsChildOf(car) || hits[i].normal.y < 0.6f) continue;
            if (hits[i].distance < best) { best = hits[i].distance; road = hits[i]; }
        }
        if (best == float.MaxValue) return;

        // The centre of mass a metre over the road: wheels just above the surface, so the car drops back onto it.
        Vector3 lift = road.point + Vector3.up * 1f - (centre - body.position);
        Vector3 v = body.linearVelocity;
        body.position = lift;
        car.position = lift;
        body.linearVelocity = new Vector3(v.x, 0f, v.z);
        body.angularVelocity = Vector3.zero;
        Debug.Log($"[VehicleFallRescue] {car.name} fell through the road at {road.point:F0}; put back on it.", car);
    }
}
