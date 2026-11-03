using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps traffic cars simulating everywhere (so iTS lane logic never has gaps),
/// but only renders/plays audio for the ones within a radius of the player's camera.
/// This makes cars "appear" only once the player is close enough for it to look and
/// sound natural, instead of relying on iTS's own (unreliable in this project) far
/// car culling, and instead of ever spawning a car directly on top of the player.
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

    private readonly List<TSTrafficAI> _tracked = new List<TSTrafficAI>();
    private readonly Dictionary<TSTrafficAI, Renderer[]> _renderers = new Dictionary<TSTrafficAI, Renderer[]>();
    private readonly Dictionary<TSTrafficAI, AudioSource[]> _audioSources = new Dictionary<TSTrafficAI, AudioSource[]>();
    private int _cursor;
    private float _rescanTimer;

    private void OnEnable()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
        RescanCars();
    }

    private void Update()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
            if (targetCamera == null) return;
        }

        _rescanTimer -= Time.deltaTime;
        if (_rescanTimer <= 0f)
        {
            RescanCars();
            // Short interval so a freshly (re)spawned car gets its distance checked almost
            // immediately, instead of sitting fully visible-by-default for a couple of seconds.
            _rescanTimer = 0.3f;
        }

        if (_tracked.Count == 0) return;

        Vector3 camPos = targetCamera.transform.position;
        float showSqr = visibleRadius * visibleRadius;
        float hideSqr = (visibleRadius + hideMargin) * (visibleRadius + hideMargin);

        int checkedCount = 0;
        int n = _tracked.Count;
        while (checkedCount < carsCheckedPerFrame && checkedCount < n)
        {
            if (_cursor >= n) _cursor = 0;
            var car = _tracked[_cursor];
            _cursor++;
            checkedCount++;

            if (car == null) continue;
            float distSqr = (car.transform.position - camPos).sqrMagnitude;

            Renderer[] renderers;
            if (!_renderers.TryGetValue(car, out renderers) || renderers.Length == 0) continue;
            // Use the renderer's own actual state as the source of truth (a freshly spawned
            // car has its renderers enabled by default from the prefab), not a separate flag
            // that could start out of sync with reality.
            bool currentlyVisible = renderers[0] != null && renderers[0].enabled;

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

    public int CountCarsWithin(Vector3 center, float radius)
    {
        float radiusSqr = radius * radius;
        int count = 0;
        for (int i = 0; i < _tracked.Count; i++)
        {
            if (_tracked[i] == null) continue;
            Vector3 d = _tracked[i].transform.position - center;
            d.y = 0f;
            if (d.sqrMagnitude <= radiusSqr) count++;
        }
        return count;
    }

    private void RescanCars()
    {
        _tracked.Clear();
        var all = FindObjectsOfType<TSTrafficAI>();
        foreach (var car in all)
        {
            if (car == null) continue;
            _tracked.Add(car);
            if (!_renderers.ContainsKey(car))
            {
                _renderers[car] = car.GetComponentsInChildren<Renderer>(true);
                _audioSources[car] = car.GetComponentsInChildren<AudioSource>(true);
            }
        }
    }

    private void SetCarVisible(TSTrafficAI car, bool visible)
    {
        Renderer[] renderers;
        if (_renderers.TryGetValue(car, out renderers))
        {
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = visible;
        }

        AudioSource[] sources;
        if (_audioSources.TryGetValue(car, out sources))
        {
            for (int i = 0; i < sources.Length; i++)
                if (sources[i] != null) sources[i].mute = !visible;
        }
    }
}
