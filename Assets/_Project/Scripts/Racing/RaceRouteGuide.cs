using UnityEngine;

namespace OpenworldCrime.Racing
{
    /// <summary>
    /// GPS-style guide on the road during a race: a short trail of glowing chevrons lying on the road ahead of the player along
    /// the race route, pointing the way. A few pooled quads (one shared additive material, no shadows, no lights) moved a few
    /// times a second; hidden when no race is on.
    /// </summary>
    [DisallowMultipleComponent]
    public class RaceRouteGuide : MonoBehaviour
    {
        [SerializeField] private RaceManager manager;
        [SerializeField] private Mesh arrowMesh;
        [SerializeField] private Material arrowMaterial;
        [SerializeField, Range(2, 16)] private int count = 7;
        [Tooltip("First chevron this far (m) ahead of the player, then one every Spacing metres.")]
        [SerializeField, Min(0f)] private float startAhead = 12f;
        [SerializeField, Min(2f)] private float spacing = 9f;
        [SerializeField, Min(0.5f)] private float size = 4.6f;
        [SerializeField] private float heightAboveRoad = 0.15f;
        [SerializeField, Min(0.02f)] private float refreshInterval = 0.1f;

        private Transform[] arrows;
        private float nextRefresh;

        private void Awake()
        {
            if (manager == null || arrowMesh == null || arrowMaterial == null)
            {
                Debug.LogError($"[{nameof(RaceRouteGuide)}] Missing manager, mesh or material on '{name}'.", this);
                enabled = false;
                return;
            }
            arrows = new Transform[count];
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Guide Chevron " + (i + 1));
                go.transform.SetParent(transform, false);
                go.transform.localScale = Vector3.one * size;
                go.AddComponent<MeshFilter>().sharedMesh = arrowMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = arrowMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                go.SetActive(false);
                arrows[i] = go.transform;
            }
            manager.OnStateChanged += HandleState;
            enabled = false;
        }

        private void OnDestroy()
        {
            if (manager != null) manager.OnStateChanged -= HandleState;
        }

        private void HandleState(RaceState state)
        {
            bool on = state == RaceState.Countdown || state == RaceState.Racing;
            enabled = on;
            if (!on) foreach (Transform a in arrows) a.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + refreshInterval;
            RaceDefinition race = manager.CurrentRace;
            if (race == null) return;
            Vector3[] route = race.RoutePoints;
            int i = Mathf.Clamp(manager.PlayerRouteIndex, 0, route.Length - 1);
            float walked = 0f, next = startAhead;
            int placed = 0;
            int steps = 0;
            while (placed < arrows.Length && steps++ < route.Length)
            {
                int n = i + 1;
                if (n >= route.Length)
                {
                    if (!race.IsCircuit) break;
                    n = 0;
                }
                float segment = Vector3.Distance(route[i], route[n]);
                while (placed < arrows.Length && walked + segment >= next)
                {
                    float t = segment > 0.001f ? (next - walked) / segment : 0f;
                    Vector3 position = Vector3.Lerp(route[i], route[n], t) + Vector3.up * heightAboveRoad;
                    Vector3 dir = route[n] - route[i];
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
                    // Quad lying on the road, its arrow (texture up) along the route.
                    arrows[placed].SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.down, dir.normalized));
                    arrows[placed].gameObject.SetActive(true);
                    placed++;
                    next += spacing;
                }
                walked += segment;
                i = n;
            }
            for (int k = placed; k < arrows.Length; k++) arrows[k].gameObject.SetActive(false);
        }
    }
}
