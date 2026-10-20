using System.Collections.Generic;
using OpenworldCrime.Vehicles;
using UnityEngine;

/// <summary>
/// Keeps traffic cars simulating everywhere (so iTS lane logic never has gaps),
/// but only renders/plays audio for the ones within a radius of the player's camera.
/// This makes cars "appear" only once the player is close enough for it to look and
/// sound natural, instead of relying on iTS's own (unreliable in this project) far
/// car culling, and instead of ever spawning a car directly on top of the player.
/// Hidden cars: renderers use forceRenderingOff (renderer.enabled stays free for other systems), engine/collision audio is
/// paused and muted, and rigidbody interpolation is switched off (nobody sees it). The show radius grows with the camera's
/// speed (Speed Margin) so cars ahead of a fast vehicle appear early enough. Cars are tracked through
/// <see cref="TrafficVehicleAdapter.OnCarEnabled"/> (checked the moment iTS spawns or reuses them); a slow rescan picks up
/// TSTrafficAI cars without an adapter. The radius is set by the graphics profiles (<see cref="SetVisibleRadius"/>).
/// </summary>
public class TrafficViewRadius : MonoBehaviour
{
    [Tooltip("Cars fully inside this distance from the camera are shown.")]
    public float visibleRadius = 160f;

    [Tooltip("Cars beyond visibleRadius + this margin are hidden; the gap avoids constant flicker at the edge.")]
    public float hideMargin = 20f;

    [Tooltip("Camera to measure distance from. Defaults to Camera.main if left empty.")]
    public Camera targetCamera;

    [Tooltip("How many cars to re-check per frame (spreads the cost out instead of checking all cars every frame).")]
    public int carsCheckedPerFrame = 20;

    [Header("Speed Margin")]
    [Tooltip("The show radius grows by the camera speed times this many seconds (cars ahead of a fast vehicle appear earlier).")]
    [SerializeField, Min(0f)] private float speedLookaheadSeconds = 1.5f;
    [Tooltip("Upper limit (m) of the speed margin.")]
    [SerializeField, Min(0f)] private float maxSpeedMargin = 60f;

    [Header("Fallback Scan")]
    [Tooltip("Seconds between scans for traffic cars that are not reported by TrafficVehicleAdapter (taxi, chase cars).")]
    [SerializeField, Min(0.5f)] private float rescanInterval = 2f;

    private readonly List<TSTrafficAI> _tracked = new List<TSTrafficAI>();
    private readonly HashSet<TSTrafficAI> _trackedSet = new HashSet<TSTrafficAI>();
    private readonly Dictionary<TSTrafficAI, Renderer[]> _renderers = new Dictionary<TSTrafficAI, Renderer[]>();
    private readonly Dictionary<TSTrafficAI, AudioSource[]> _audioSources = new Dictionary<TSTrafficAI, AudioSource[]>();
    private readonly Dictionary<TSTrafficAI, Rigidbody> _bodies = new Dictionary<TSTrafficAI, Rigidbody>();
    private readonly Dictionary<TSTrafficAI, RigidbodyInterpolation> _interpolation = new Dictionary<TSTrafficAI, RigidbodyInterpolation>();
    private int _cursor;
    private float _rescanTimer;
    private Vector3 _lastCamPos;
    private float _camSpeed;
    private bool _hasLastCamPos;

    /// <summary>Current show radius including the speed margin.</summary>
    public float EffectiveVisibleRadius => visibleRadius + Mathf.Min(maxSpeedMargin, _camSpeed * speedLookaheadSeconds);

    /// <summary>Sets the base show radius and hide margin (graphics profiles).</summary>
    public void SetVisibleRadius(float radius, float margin)
    {
        visibleRadius = Mathf.Max(10f, radius);
        hideMargin = Mathf.Max(1f, margin);
    }

    private void OnEnable()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
        TrafficVehicleAdapter.OnCarEnabled += HandleCarEnabled;
        RescanCars();
    }

    private void OnDisable()
    {
        TrafficVehicleAdapter.OnCarEnabled -= HandleCarEnabled;
    }

    private void Update()
    {
        if (targetCamera == null || !targetCamera.isActiveAndEnabled)
        {
            targetCamera = Camera.main;
            if (targetCamera == null) return;
        }

        _rescanTimer -= Time.deltaTime;
        if (_rescanTimer <= 0f)
        {
            RescanCars();
            _rescanTimer = rescanInterval;
        }

        Vector3 camPos = targetCamera.transform.position;
        UpdateCameraSpeed(camPos);
        if (_tracked.Count == 0) return;

        float show = EffectiveVisibleRadius;
        float showSqr = show * show;
        float hideSqr = (show + hideMargin) * (show + hideMargin);

        int checkedCount = 0;
        int n = _tracked.Count;
        while (checkedCount < carsCheckedPerFrame && checkedCount < n)
        {
            if (_cursor >= n) _cursor = 0;
            var car = _tracked[_cursor];
            _cursor++;
            checkedCount++;

            if (car == null || !car.gameObject.activeInHierarchy) continue;
            float distSqr = (car.transform.position - camPos).sqrMagnitude;

            Renderer[] renderers;
            if (!_renderers.TryGetValue(car, out renderers) || renderers.Length == 0) continue;
            // The renderer's own state is the source of truth (a car fresh from the prefab renders by default).
            bool currentlyVisible = renderers[0] != null && !renderers[0].forceRenderingOff;

            if (currentlyVisible && distSqr >= hideSqr)
            {
                SetCarVisible(car, false);
            }
            else if (!currentlyVisible && distSqr <= showSqr)
            {
                SetCarVisible(car, true);
            }
        }
    }

    private void UpdateCameraSpeed(Vector3 camPos)
    {
        float dt = Time.deltaTime;
        if (_hasLastCamPos && dt > 0f)
        {
            float speed = (camPos - _lastCamPos).magnitude / dt;
            // A camera cut (respawn, teleport) is not speed.
            if (speed > 150f) speed = _camSpeed;
            _camSpeed = Mathf.Lerp(_camSpeed, speed, Mathf.Clamp01(dt * 4f));
        }
        _lastCamPos = camPos;
        _hasLastCamPos = true;
    }

    public int CountCarsWithin(Vector3 center, float radius)
    {
        float radiusSqr = radius * radius;
        int count = 0;
        for (int i = 0; i < _tracked.Count; i++)
        {
            if (_tracked[i] == null || !_tracked[i].gameObject.activeInHierarchy) continue;
            Vector3 d = _tracked[i].transform.position - center;
            d.y = 0f;
            if (d.sqrMagnitude <= radiusSqr) count++;
        }
        return count;
    }

    // A car spawned or reused by iTS: tracked and put in the right state at once, before it renders a frame.
    private void HandleCarEnabled(TrafficVehicleAdapter adapter)
    {
        if (adapter == null || !adapter.TryGetComponent(out TSTrafficAI car)) return;
        Track(car);
        if (targetCamera == null) return;
        float show = EffectiveVisibleRadius;
        SetCarVisible(car, (car.transform.position - targetCamera.transform.position).sqrMagnitude <= show * show);
    }

    private void RescanCars()
    {
        // Cars iTS has parked in its pool (inactive) drop out; they come back through OnCarEnabled or the scan below.
        for (int i = _tracked.Count - 1; i >= 0; i--)
        {
            TSTrafficAI car = _tracked[i];
            if (car != null && car.gameObject.activeInHierarchy) continue;
            _tracked.RemoveAt(i);
            _trackedSet.Remove(car);
        }

        var all = FindObjectsByType<TSTrafficAI>(FindObjectsSortMode.None);
        foreach (var car in all)
            if (car != null) Track(car);
    }

    private void Track(TSTrafficAI car)
    {
        if (!_trackedSet.Add(car)) return;
        _tracked.Add(car);
        if (_renderers.ContainsKey(car)) return;
        _renderers[car] = car.GetComponentsInChildren<Renderer>(true);
        _audioSources[car] = car.GetComponentsInChildren<AudioSource>(true);
        if (car.TryGetComponent(out Rigidbody body))
        {
            _bodies[car] = body;
            _interpolation[car] = body.interpolation;
        }
    }

    private void SetCarVisible(TSTrafficAI car, bool visible)
    {
        Renderer[] renderers;
        if (_renderers.TryGetValue(car, out renderers))
        {
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].forceRenderingOff = !visible;
        }

        AudioSource[] sources;
        if (_audioSources.TryGetValue(car, out sources))
        {
            for (int i = 0; i < sources.Length; i++)
            {
                AudioSource source = sources[i];
                if (source == null) continue;
                source.mute = !visible;
                // Paused voices cost nothing in the mixer; muted playing ones still do.
                if (visible) source.UnPause();
                else if (source.isPlaying) source.Pause();
            }
        }

        if (_bodies.TryGetValue(car, out Rigidbody body) && body != null)
            body.interpolation = visible ? _interpolation[car] : RigidbodyInterpolation.None;
    }
}
