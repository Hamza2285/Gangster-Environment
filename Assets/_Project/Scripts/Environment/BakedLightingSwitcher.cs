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
    /// street lamp (baked-only lights, zero runtime cost).
    /// During a transition the two bakes are crossfaded with the controller's night blend: each lightmap pair is blended on the
    /// GPU (RGBM decoded, blended, re-encoded; Hidden/OpenworldCrime/LightmapBlend) into a temporary texture shown in place of
    /// the bake, and the probes are blended on the CPU a few times a second. The temporaries exist only while the blend is
    /// strictly between day and night; at either end the original compressed bake is shown again and they are released. So
    /// the street lamp pools, sky light and bounce fade instead of swapping in one frame. Devices that cannot copy a render
    /// texture into a texture fall back to the old exchange at Swap At Night Amount.
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

        [Header("Crossfade")]
        [Tooltip("Blend the bakes during a transition (off: exchange them at Swap At Night Amount).")]
        [SerializeField] private bool crossfade = true;
        [SerializeField] private Shader blendShader;
        [Tooltip("Size of the blended lightmaps as a share of the bake (0.5 = a quarter of the memory; lightmaps are soft).")]
        [SerializeField, Range(0.25f, 1f)] private float blendResolution = 0.5f;
        [Tooltip("Blend change that triggers a new lightmap blend (GPU) / probe blend (CPU).")]
        [SerializeField, Range(0.001f, 0.05f)] private float lightmapStep = 0.004f;
        [SerializeField, Range(0.005f, 0.1f)] private float probeStep = 0.02f;
        [Tooltip("Probes blended per frame (the blend is spread over a few frames, then applied at once: no hitch).")]
        [SerializeField, Min(100)] private int probesPerFrame = 1000;

        [Header("Realtime Street Lights")]
        [Tooltip("The controller's nearby realtime street lights. Their light is baked into the night lightmaps and probes, so they are switched off while baked lighting is active (saves up to 4 per-pixel lights at night).")]
        [SerializeField] private bool disableRealtimeStreetLights = true;
        [SerializeField] private GameObject[] realtimeStreetLights = new GameObject[0];

        /// <summary>True while the night bake is shown (or the blend is past half way).</summary>
        public bool IsShowingNight { get; private set; }
        /// <summary>True while blended lightmaps are shown.</summary>
        public bool IsBlending => blending;

        private SphericalHarmonicsL2[] dayProbes;
        private SphericalHarmonicsL2[] nightProbes;
        private bool applied;
        private bool valid;
        private bool warnedBlock;
        private bool warnedProbes;
        // Crossfade state (temporaries live only while blending).
        private bool blending;
        private Texture2D[] blendMaps;
        private RenderTexture blendTarget;
        private Material blendMaterial;
        private SphericalHarmonicsL2[] blendProbes;
        private float blendedLightmaps = -1f, blendedProbes = -1f;
        private int probeCursor = -1;      // >= 0 while a probe blend is being built over several frames
        private float probeTarget;

        private void Awake()
        {
            // Switched off by the realtime lighting (its sets are unassigned then, on purpose).
            if (enabled) valid = Validate();
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
            if (dayNight != null) Follow(dayNight.NightBlend);
            else Apply(false);
            if (dayNight != null) StartCoroutine(Watch());
        }

        private void OnDisable()
        {
            if (!valid) return;
            LightProbes.tetrahedralizationCompleted -= HandleProbesRebuilt;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
            SetRealtimeStreetLightsActive(true);
            if (blending) { applied = false; Apply(IsShowingNight); }
            ReleaseBlend();
        }

        private void OnDestroy()
        {
            ReleaseBlend();
            if (blendMaterial != null) Destroy(blendMaterial);
        }

        private IEnumerator Watch()
        {
            var wait = new WaitForSeconds(checkInterval);
            while (true)
            {
                // Every frame while a transition moves (the blend steps are thresholded), slowly otherwise.
                if (dayNight != null) Follow(dayNight.NightBlend);
                if (blending) yield return null;
                else yield return wait;
            }
        }

        /// <summary>Shows the bake for a night amount: day, night, or (crossfade on) a blend of both.</summary>
        public void Follow(float night)
        {
            if (!valid) return;
            if (!crossfade || night <= 0.001f || night >= 0.999f || !CanBlend())
            {
                if (blending) { applied = false; blending = false; }
                Apply(night >= (crossfade && CanBlend() ? 0.5f : swapAtNightAmount));
                ReleaseBlend();
                return;
            }
            Blend(night);
        }

        private bool CanBlend()
        {
            if (blendShader == null || !blendShader.isSupported || daySet.HasOwnLayout || nightSet.HasOwnLayout) return false;
            return (SystemInfo.copyTextureSupport & CopyTextureSupport.RTToTexture) != 0;
        }

        private void Blend(float night)
        {
            LightmapData[] maps = LightmapSettings.lightmaps;
            int start = FindCityBlock(maps);
            if (start < 0 && blendMaps != null) start = FindBlendBlock(maps);
            if (start < 0) return;
            if (blendMaps == null) CreateBlend();
            if (!blending || Mathf.Abs(night - blendedLightmaps) >= lightmapStep)
            {
                blendMaterial.SetFloat(BlendId, night);
                for (int i = 0; i < blendMaps.Length; i++)
                {
                    blendMaterial.SetTexture(DayTexId, daySet.GetLightmapColor(i));
                    blendMaterial.SetTexture(NightTexId, nightSet.GetLightmapColor(i));
                    Graphics.Blit(null, blendTarget, blendMaterial);
                    Graphics.CopyTexture(blendTarget, blendMaps[i]);
                }
                blendedLightmaps = night;
            }
            if (!blending)
            {
                // Shown once; the textures' content changes in place afterwards.
                for (int i = 0; i < blendMaps.Length; i++)
                {
                    maps[start + i].lightmapColor = blendMaps[i];
                    maps[start + i].lightmapDir = null;
                }
                LightmapSettings.lightmaps = maps;
                blending = true;
                applied = false;
            }
            // Probes: a new blend is built a slice per frame into the buffer, then applied whole.
            if (probeCursor < 0 && (Mathf.Abs(night - blendedProbes) >= probeStep || blendedProbes < 0f))
            {
                probeCursor = 0;
                probeTarget = night;
            }
            if (probeCursor >= 0)
            {
                int end = Mathf.Min(blendProbes.Length, probeCursor + probesPerFrame);
                float w = probeTarget, d = 1f - probeTarget;
                for (int i = probeCursor; i < end; i++) blendProbes[i] = dayProbes[i] * d + nightProbes[i] * w;
                probeCursor = end;
                if (probeCursor >= blendProbes.Length)
                {
                    ApplyProbes(blendProbes, daySet);
                    blendedProbes = probeTarget;
                    probeCursor = -1;
                }
            }
            IsShowingNight = night >= 0.5f;
        }

        private int FindBlendBlock(LightmapData[] maps)
        {
            for (int i = 0; i + blendMaps.Length <= maps.Length; i++)
                if (maps[i].lightmapColor == blendMaps[0]) return i;
            return -1;
        }

        private void CreateBlend()
        {
            if (blendMaterial == null) blendMaterial = new Material(blendShader) { hideFlags = HideFlags.HideAndDontSave };
            Texture2D source = daySet.GetLightmapColor(0);
            int w = Mathf.Max(64, Mathf.RoundToInt(source.width * blendResolution));
            int h = Mathf.Max(64, Mathf.RoundToInt(source.height * blendResolution));
            blendTarget = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "Lightmap Blend Target" };
            blendTarget.Create();
            blendMaps = new Texture2D[daySet.LightmapCount];
            for (int i = 0; i < blendMaps.Length; i++)
            {
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false, true) { name = "Lightmap Blend " + i, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                t.Apply(false, true); // GPU only: no CPU copy kept
                blendMaps[i] = t;
            }
            blendProbes = new SphericalHarmonicsL2[dayProbes.Length];
            blendedLightmaps = blendedProbes = -1f;
            probeCursor = -1;
        }

        private void ReleaseBlend()
        {
            if (blendMaps != null) foreach (Texture2D t in blendMaps) if (t != null) Destroy(t);
            blendMaps = null;
            if (blendTarget != null) { blendTarget.Release(); Destroy(blendTarget); blendTarget = null; }
            blendProbes = null;
            blending = false;
            probeCursor = -1;
            blendedLightmaps = blendedProbes = -1f;
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

        private static readonly int DayTexId = Shader.PropertyToID("_DayTex");
        private static readonly int NightTexId = Shader.PropertyToID("_NightTex");
        private static readonly int BlendId = Shader.PropertyToID("_Blend");

        private int FindCityBlock(LightmapData[] maps)
        {
            Texture2D day0 = daySet.GetLightmapColor(0);
            Texture2D night0 = nightSet.GetLightmapColor(0);
            for (int i = 0; i + daySet.LightmapCount <= maps.Length; i++)
            {
                Texture2D color = maps[i].lightmapColor;
                if (color == day0 || color == night0) return i;
                if (blendMaps != null && blendMaps.Length > 0 && color == blendMaps[0]) return i; // blended maps shown
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
            if (blending && blendProbes != null) ApplyProbes(blendProbes, daySet);
            else if (applied) ApplyProbes(IsShowingNight ? nightProbes : dayProbes, IsShowingNight ? nightSet : daySet);
        }

        private void HandleSceneUnloaded(Scene scene)
        {
            // Removing a scene can rebuild the lightmap array from the remaining scenes' own bakes.
            if (!applied && !blending) return;
            applied = false;
            if (blending) { blending = false; if (dayNight != null) Follow(dayNight.NightBlend); }
            else Apply(IsShowingNight);
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
