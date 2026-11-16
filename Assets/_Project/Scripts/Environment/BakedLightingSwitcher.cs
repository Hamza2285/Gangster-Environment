using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// Switches the city between its two baked lighting results (day / night: lightmaps + light probes) following
    /// <see cref="CityDayNightController"/> (Hamza's, untouched; it keeps owning sun, sky, ambient and the night shader globals).
    ///
    /// Bake mode is Baked Indirect: the sun / moon stays a realtime light for direct light and shadows (it moves during the
    /// 40 s day/night transition), the bake adds sky light, bounce, ambient occlusion and, at night, the light pools of every
    /// street lamp (baked-only lights, zero runtime cost). The sets are exchanged at the middle of a transition, where the
    /// sun is low and the difference between the two bounce bakes is smallest.
    ///
    /// Additive interiors: Unity appends an interior's lightmaps behind the city's and merges its probes, and re-merges the
    /// probes (re-tetrahedralization) whenever a scene is added or removed, which restores the scene's own bake. So the
    /// switcher only replaces the city's own block in both arrays and re-applies after every re-tetrahedralization.
    /// The controller has no change event, so its night amount is read on a slow coroutine; nothing happens unless it changed.
    /// Lives under ///Manager/// (not a Singleton). Filled by Tools > OpenworldCrime > Lighting > Bake Day And Night.
    /// </summary>
    [DisallowMultipleComponent]
    public class BakedLightingSwitcher : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CityDayNightController dayNight;
        [SerializeField] private BakedLightingSet daySet;
        [SerializeField] private BakedLightingSet nightSet;
        [Tooltip("Only used when the day and night bakes packed the renderers differently (see the sets' Renderer Layout).")]
        [SerializeField] private Renderer[] layoutRenderers = new Renderer[0];

        [Header("Switching")]
        [Tooltip("Night amount (0 day - 1 night) at which the night bake replaces the day bake.")]
        [SerializeField, Range(0.05f, 0.95f)] private float swapAtNightAmount = 0.5f;
        [Tooltip("Seconds between reads of the day/night amount.")]
        [SerializeField, Min(0.05f)] private float checkInterval = 0.25f;

        [Header("Realtime Street Lights")]
        [Tooltip("The controller's nearby realtime street lights. Their light is baked into the night lightmaps and probes, so they are switched off while baked lighting is active (saves up to 4 per-pixel lights at night).")]
        [SerializeField] private bool disableRealtimeStreetLights = true;
        [SerializeField] private GameObject[] realtimeStreetLights = new GameObject[0];

        /// <summary>True while the night bake is shown.</summary>
        public bool IsShowingNight { get; private set; }

        private SphericalHarmonicsL2[] dayProbes;
        private SphericalHarmonicsL2[] nightProbes;
        private bool applied;
        private bool valid;
        private bool warnedBlock;
        private bool warnedProbes;

        private void Awake()
        {
            valid = Validate();
        }

        private void OnEnable()
        {
            if (!valid) return;
            if (dayProbes == null) dayProbes = daySet.DecodeProbes();
            if (nightProbes == null) nightProbes = nightSet.DecodeProbes();
            LightProbes.tetrahedralizationCompleted += HandleProbesRebuilt;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
            SetRealtimeStreetLightsActive(!disableRealtimeStreetLights);
            applied = false;
            Apply(dayNight != null && dayNight.NightBlend >= swapAtNightAmount);
            if (dayNight != null) StartCoroutine(Watch());
        }

        private void OnDisable()
        {
            if (!valid) return;
            LightProbes.tetrahedralizationCompleted -= HandleProbesRebuilt;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
            SetRealtimeStreetLightsActive(true);
        }

        private IEnumerator Watch()
        {
            var wait = new WaitForSeconds(checkInterval);
            while (true)
            {
                yield return wait;
                if (dayNight != null) Apply(dayNight.NightBlend >= swapAtNightAmount);
            }
        }

        /// <summary>Shows the night or the day bake (no-op if it is already shown).</summary>
        public void Apply(bool night)
        {
            if (!valid || (applied && night == IsShowingNight)) return;
            BakedLightingSet set = night ? nightSet : daySet;
            if (!ApplyLightmaps(set)) return;
            ApplyProbes(night ? nightProbes : dayProbes, set);
            IsShowingNight = night;
            applied = true;
        }

        private bool ApplyLightmaps(BakedLightingSet set)
        {
            LightmapData[] maps = LightmapSettings.lightmaps;
            int start = FindCityBlock(maps);
            if (start < 0)
            {
                if (!warnedBlock) Debug.LogWarning($"[{nameof(BakedLightingSwitcher)}] The city's baked lightmaps are not loaded (scene lighting cleared?). Re-run the bake.", this);
                warnedBlock = true;
                return false;
            }
            for (int i = 0; i < set.LightmapCount; i++)
            {
                maps[start + i].lightmapColor = set.GetLightmapColor(i);
                maps[start + i].lightmapDir = set.GetLightmapDirection(i);
            }
            LightmapSettings.lightmaps = maps;

            if (set.HasOwnLayout)
            {
                int[] index = set.RendererLightmapIndex;
                Vector4[] scaleOffset = set.RendererScaleOffset;
                int count = Mathf.Min(layoutRenderers.Length, Mathf.Min(index.Length, scaleOffset.Length));
                for (int i = 0; i < count; i++)
                {
                    Renderer r = layoutRenderers[i];
                    if (r == null) continue;
                    r.lightmapIndex = index[i] < 0 ? index[i] : index[i] + start;
                    r.lightmapScaleOffset = scaleOffset[i];
                }
            }
            return true;
        }

        private int FindCityBlock(LightmapData[] maps)
        {
            Texture2D day0 = daySet.GetLightmapColor(0);
            Texture2D night0 = nightSet.GetLightmapColor(0);
            for (int i = 0; i + daySet.LightmapCount <= maps.Length; i++)
            {
                Texture2D color = maps[i].lightmapColor;
                if (color == day0 || color == night0) return i;
            }
            return -1;
        }

        private void ApplyProbes(SphericalHarmonicsL2[] probes, BakedLightingSet set)
        {
            LightProbes loaded = LightmapSettings.lightProbes;
            if (probes == null || loaded == null || loaded.count < probes.Length) return;
            if (loaded.count == probes.Length)
            {
                loaded.bakedProbes = probes;
                return;
            }
            // An interior's probes are merged in: find the city's block (first or last) by its first probe position.
            Vector3[] positions = loaded.positions;
            int start = -1;
            if ((positions[0] - set.FirstProbePosition).sqrMagnitude < 0.0001f) start = 0;
            else if ((positions[loaded.count - probes.Length] - set.FirstProbePosition).sqrMagnitude < 0.0001f) start = loaded.count - probes.Length;
            if (start < 0)
            {
                if (!warnedProbes) Debug.LogWarning($"[{nameof(BakedLightingSwitcher)}] Could not find the city's probes in the merged probe set; probes keep the scene's own bake.", this);
                warnedProbes = true;
                return;
            }
            SphericalHarmonicsL2[] all = loaded.bakedProbes;
            System.Array.Copy(probes, 0, all, start, probes.Length);
            loaded.bakedProbes = all;
        }

        private void HandleProbesRebuilt()
        {
            if (applied) ApplyProbes(IsShowingNight ? nightProbes : dayProbes, IsShowingNight ? nightSet : daySet);
        }

        private void HandleSceneUnloaded(Scene scene)
        {
            // Removing a scene can rebuild the lightmap array from the remaining scenes' own bakes.
            if (!applied) return;
            applied = false;
            Apply(IsShowingNight);
        }

        private void SetRealtimeStreetLightsActive(bool active)
        {
            for (int i = 0; i < realtimeStreetLights.Length; i++)
            {
                if (realtimeStreetLights[i] != null) realtimeStreetLights[i].SetActive(active);
            }
        }

        private bool Validate()
        {
            if (daySet == null || nightSet == null || daySet.LightmapCount == 0)
            {
                Debug.LogError($"[{nameof(BakedLightingSwitcher)}] '{name}' has no day/night lighting sets. Run Tools > OpenworldCrime > Lighting > Bake Day And Night.", this);
                return false;
            }
            if (daySet.LightmapCount != nightSet.LightmapCount || daySet.ProbeCount != nightSet.ProbeCount)
            {
                Debug.LogError($"[{nameof(BakedLightingSwitcher)}] Day and night sets do not match (lightmaps {daySet.LightmapCount}/{nightSet.LightmapCount}, probes {daySet.ProbeCount}/{nightSet.ProbeCount}). Re-run the bake.", this);
                return false;
            }
            if (dayNight == null) Debug.LogWarning($"[{nameof(BakedLightingSwitcher)}] No CityDayNightController on '{name}'; the day bake stays.", this);
            return true;
        }

#if UNITY_EDITOR
        public void EditorSetup(CityDayNightController controller, BakedLightingSet day, BakedLightingSet night, Renderer[] renderers, GameObject[] streetLights)
        {
            dayNight = controller;
            daySet = day;
            nightSet = night;
            layoutRenderers = renderers ?? new Renderer[0];
            realtimeStreetLights = streetLights ?? new GameObject[0];
        }
#endif
    }
}
