using System;
using System.Collections.Generic;
using UnityEngine;

// Holds the destination marker, keeps a GPS route to it and draws the guide line on the road.
public class WaypointSystem : MonoBehaviour
{
    public static WaypointSystem Instance { get; private set; }

    [Header("Road guide line")]
    public Material routeMaterial;
    public float lineWidth = 1.4f;
    public float lineLift = 0.22f;
    public int maxVisiblePoints = 90;

    [Header("Marker beam")]
    public Material beamMaterial;
    public float beamHeight = 90f;

    [Header("Routing")]
    public float rerouteDistance = 35f;
    public float arriveDistance = 14f;
    public float updateInterval = 0.25f;

    public bool HasMarker { get; private set; }
    public Vector3 MarkerPosition { get; private set; }
    public IReadOnlyList<Vector3> Route => route;
    public int RouteIndex { get; private set; }
    public event Action RouteChanged;

    readonly List<Vector3> route = new List<Vector3>();
    LineRenderer line;
    GameObject beam;
    float nextUpdate;
    Vector3[] linePoints = new Vector3[0];

    void Awake()
    {
        Instance = this;

        var lineObject = new GameObject("Road Guide Line");
        lineObject.transform.SetParent(transform, false);
        lineObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        line = lineObject.AddComponent<LineRenderer>();
        line.alignment = LineAlignment.TransformZ;
        line.useWorldSpace = true;
        line.widthMultiplier = lineWidth;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = routeMaterial;
        line.positionCount = 0;

        beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beam.name = "Marker Beam";
        Destroy(beam.GetComponent<Collider>());
        beam.transform.SetParent(transform, false);
        beam.transform.localScale = new Vector3(2.2f, beamHeight * 0.5f, 2.2f);
        var beamRenderer = beam.GetComponent<MeshRenderer>();
        beamRenderer.sharedMaterial = beamMaterial;
        beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beam.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Snaps to the nearest road, like a GPS destination.
    public bool SetMarker(Vector3 world)
    {
        var network = RoadNetwork.Instance;
        if (network == null || !network.TryFindNearest(new Vector3(world.x, 28f, world.z), out RoadNetwork.RoadPoint point)) return false;
        MarkerPosition = point.position;
        HasMarker = true;
        beam.transform.position = point.position + Vector3.up * beamHeight * 0.5f;
        beam.SetActive(true);
        Recalculate();
        return true;
    }

    public void ClearMarker()
    {
        HasMarker = false;
        route.Clear();
        RouteIndex = 0;
        line.positionCount = 0;
        beam.SetActive(false);
        RouteChanged?.Invoke();
    }

    // Shows a route somebody else worked out (the taxi's trip).
    public void UseRoute(List<Vector3> newRoute)
    {
        route.Clear();
        route.AddRange(newRoute);
        RouteIndex = 0;
        RefreshLine();
        RouteChanged?.Invoke();
    }

    void Recalculate()
    {
        var player = FirstPersonPlayer.Instance;
        var network = RoadNetwork.Instance;
        if (player == null || network == null || !HasMarker) return;
        if (!network.TryFindRoute(player.transform.position, MarkerPosition, route))
        {
            route.Clear();
        }
        RouteIndex = 0;
        RefreshLine();
        RouteChanged?.Invoke();
    }

    void Update()
    {
        if (!HasMarker || Time.time < nextUpdate) return;
        nextUpdate = Time.time + updateInterval;

        var player = FirstPersonPlayer.Instance;
        if (player == null) return;
        Vector3 position = player.transform.position;
        // In the taxi the trip owns the route: only follow along it.
        bool riding = !player.ControlEnabled;

        if (!riding && Horizontal(position - MarkerPosition) < arriveDistance)
        {
            ClearMarker();
            GameMapUI.Instance?.ShowMessage("You have reached your destination");
            return;
        }
        if (route.Count < 2)
        {
            if (!riding) Recalculate();
            return;
        }

        // Move along the route as the player does; look only a short way ahead.
        float best = float.MaxValue;
        int bestIndex = RouteIndex;
        int end = Mathf.Min(route.Count, RouteIndex + 60);
        for (int i = RouteIndex; i < end; i++)
        {
            float d = Horizontal(route[i] - position);
            if (d < best) { best = d; bestIndex = i; }
        }
        if (best > rerouteDistance)
        {
            if (!riding) Recalculate();
            return;
        }
        if (bestIndex != RouteIndex)
        {
            RouteIndex = bestIndex;
            RefreshLine();
            RouteChanged?.Invoke();
        }
    }

    static float Horizontal(Vector3 v)
    {
        v.y = 0f;
        return v.magnitude;
    }

    void RefreshLine()
    {
        int count = Mathf.Min(route.Count - RouteIndex, maxVisiblePoints);
        if (count < 2)
        {
            line.positionCount = 0;
            return;
        }
        if (linePoints.Length != count) linePoints = new Vector3[count];
        for (int i = 0; i < count; i++) linePoints[i] = route[RouteIndex + i] + Vector3.up * lineLift;
        line.positionCount = count;
        line.SetPositions(linePoints);
    }
}
