using System;
using System.Collections.Generic;
using OpenworldCrime.Environment;
using OpenworldCrime.Vehicles;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenworldCrime.Settings
{
    /// <summary>
    /// Owner of the graphics / performance profile (lives under ///Manager///, one per scene, not a Singleton).
    /// - On Awake (before the traffic and pedestrian spawners initialise) it picks the saved profile, or the recommended one
    ///   for the device's system and graphics memory, and applies it.
    /// - Applying pushes every value of the <see cref="GraphicsProfile"/> into the engine (quality level, URP asset, LOD bias,
    ///   texture mip limit, cameras) and into the game systems through their setters. No system reads the profile itself.
    /// - <see cref="SetProfile(GraphicsProfile)"/> switches at runtime and saves the choice (one PlayerPrefs key, owned here).
    ///   The iTS traffic pool size is fixed once the spawner has started: a higher traffic amount than the pool takes full
    ///   effect after the scene is loaded again.
    /// - In the editor, project-wide quality values changed in play mode are put back when play mode ends, so profiles never
    ///   leak into ProjectSettings.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public class GraphicsSettingsManager : MonoBehaviour
    {
        private const string ProfilePrefsKey = "Settings.GraphicsProfile";
#if UNITY_EDITOR
        /// <summary>Editor-only SessionState key: profile name (or display name) that overrides the startup profile.</summary>
        public const string EditorProfileOverrideKey = "OWC.EditorProfileOverride";
#endif

        [Header("Profiles (lowest to highest)")]
        [SerializeField] private GraphicsProfile[] profiles = new GraphicsProfile[0];
        [Tooltip("Used instead of the memory-based pick on phones and tablets (Application.isMobilePlatform), tuned for a " +
                 "mid-range 6 GB phone. A saved player choice still wins.")]
        [SerializeField] private GraphicsProfile mobileProfile;
        [Tooltip("Editor only: always start with this profile (ignores the saved choice and the auto pick). For testing.")]
        [SerializeField] private GraphicsProfile editorTestProfile;

        [Header("Cameras")]
        [Tooltip("Gameplay cameras (third-person and vehicle): far plane, post-processing and anti-aliasing.")]
        [SerializeField] private Camera[] gameplayCameras = new Camera[0];

        [Header("Systems")]
        [SerializeField] private TSTrafficSpawner trafficSpawner;
        [SerializeField] private TrafficViewRadius trafficViewRadius;
        [SerializeField] private PedestrianSpawner pedestrianSpawner;
        [SerializeField] private BreakablePropManager breakablePropManager;
        [SerializeField] private EnvironmentVisibilityManager environmentVisibility;
        [SerializeField] private TrafficTakeoverManager trafficTakeoverManager;
        [SerializeField] private TrafficSimulationLOD trafficSimulationLOD;

        /// <summary>Raised after a profile has been applied.</summary>
        public static event Action<GraphicsProfile> OnProfileApplied;

        public GraphicsProfile CurrentProfile { get; private set; }
        public IReadOnlyList<GraphicsProfile> Profiles => profiles;

        private bool trafficStarted;

#if UNITY_EDITOR
        private struct LevelState
        {
            public RenderPipelineAsset Pipeline;
            public float LodBias;
            public int MipLimit;
            public AnisotropicFiltering Aniso;
            public bool RealtimeProbes;
            public int ParticleBudget;
            public int VSync;
        }

        private readonly Dictionary<int, LevelState> editorLevelStates = new Dictionary<int, LevelState>();
        private int originalQualityLevel;
        private float originalMaxDeltaTime;
#endif

        private void Awake()
        {
            if (profiles.Length == 0)
            {
                Debug.LogError($"[{nameof(GraphicsSettingsManager)}] No graphics profiles assigned on '{name}'.", this);
                return;
            }
            if (trafficSpawner == null || trafficViewRadius == null || pedestrianSpawner == null || breakablePropManager == null || environmentVisibility == null)
                Debug.LogWarning($"[{nameof(GraphicsSettingsManager)}] Some system references are missing on '{name}'; those systems keep their own values.", this);

#if UNITY_EDITOR
            originalQualityLevel = QualitySettings.GetQualityLevel();
            originalMaxDeltaTime = Time.maximumDeltaTime;
#endif
            Apply(GetStartupProfile());
        }

        private void Start()
        {
            // From here on the iTS pool exists; later profile switches only change the active amount.
            trafficStarted = true;
        }

#if UNITY_EDITOR
        // Quality values belong to the active quality level and would otherwise stay changed in ProjectSettings after play mode.
        private void CaptureEditorLevelState()
        {
            int level = QualitySettings.GetQualityLevel();
            if (editorLevelStates.ContainsKey(level)) return;
            editorLevelStates[level] = new LevelState
            {
                Pipeline = QualitySettings.renderPipeline,
                LodBias = QualitySettings.lodBias,
                MipLimit = QualitySettings.globalTextureMipmapLimit,
                Aniso = QualitySettings.anisotropicFiltering,
                RealtimeProbes = QualitySettings.realtimeReflectionProbes,
                ParticleBudget = QualitySettings.particleRaycastBudget,
                VSync = QualitySettings.vSyncCount
            };
        }

        private void OnDestroy()
        {
            if (editorLevelStates.Count == 0) return;
            foreach (KeyValuePair<int, LevelState> pair in editorLevelStates)
            {
                QualitySettings.SetQualityLevel(pair.Key, false);
                LevelState state = pair.Value;
                QualitySettings.renderPipeline = state.Pipeline;
                QualitySettings.lodBias = state.LodBias;
                QualitySettings.globalTextureMipmapLimit = state.MipLimit;
                QualitySettings.anisotropicFiltering = state.Aniso;
                QualitySettings.realtimeReflectionProbes = state.RealtimeProbes;
                QualitySettings.particleRaycastBudget = state.ParticleBudget;
                QualitySettings.vSyncCount = state.VSync;
            }
            editorLevelStates.Clear();
            QualitySettings.SetQualityLevel(originalQualityLevel, true);
            Time.maximumDeltaTime = originalMaxDeltaTime;
        }
#endif

        /// <summary>Applies a profile now and saves it as the player's choice.</summary>
        public void SetProfile(GraphicsProfile profile)
        {
            if (profile == null) return;
            Apply(profile);
            PlayerPrefs.SetString(ProfilePrefsKey, profile.name);
            PlayerPrefs.Save();
        }

        /// <summary>Applies the profile at this index of <see cref="Profiles"/> (0 = lowest) and saves the choice.</summary>
        public void SetProfile(int index)
        {
            if (index < 0 || index >= profiles.Length) return;
            SetProfile(profiles[index]);
        }

        /// <summary>Forgets the saved choice and applies the profile recommended for this device.</summary>
        public void ResetToRecommended()
        {
            PlayerPrefs.DeleteKey(ProfilePrefsKey);
            Apply(GetRecommendedProfile());
        }

        /// <summary>Highest profile whose memory requirements this device meets (system RAM and graphics memory).</summary>
        public GraphicsProfile GetRecommendedProfile()
        {
            // Phones report shared memory as graphics memory, so the desktop memory thresholds don't apply to them.
            if (Application.isMobilePlatform && mobileProfile != null) return mobileProfile;
            int systemMemory = SystemInfo.systemMemorySize;
            int graphicsMemory = SystemInfo.graphicsMemorySize;
            for (int i = profiles.Length - 1; i >= 0; i--)
            {
                GraphicsProfile p = profiles[i];
                if (p != null && systemMemory >= p.MinSystemMemoryMB && graphicsMemory >= p.MinGraphicsMemoryMB) return p;
            }
            return profiles[0];
        }

        private GraphicsProfile GetStartupProfile()
        {
#if UNITY_EDITOR
            // Editor tools (PerfCaptureRunner) can force a profile for one play session without touching the scene.
            string forced = UnityEditor.SessionState.GetString(EditorProfileOverrideKey, string.Empty);
            if (!string.IsNullOrEmpty(forced))
            {
                for (int i = 0; i < profiles.Length; i++)
                    if (profiles[i] != null && (profiles[i].name == forced || profiles[i].DisplayName == forced)) return profiles[i];
            }
            if (editorTestProfile != null) return editorTestProfile;
#endif
            string saved = PlayerPrefs.GetString(ProfilePrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(saved))
            {
                for (int i = 0; i < profiles.Length; i++)
                    if (profiles[i] != null && profiles[i].name == saved) return profiles[i];
                if (mobileProfile != null && mobileProfile.name == saved) return mobileProfile;
            }
            return GetRecommendedProfile();
        }

        private void Apply(GraphicsProfile p)
        {
            if (p == null) return;
            CurrentProfile = p;

            if (p.QualityLevel < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != p.QualityLevel)
                QualitySettings.SetQualityLevel(p.QualityLevel, true);
#if UNITY_EDITOR
            CaptureEditorLevelState();
#endif
            if (p.PipelineAsset != null) QualitySettings.renderPipeline = p.PipelineAsset;
            QualitySettings.lodBias = p.LodBias;
            QualitySettings.globalTextureMipmapLimit = p.TextureMipmapLimit;
            QualitySettings.anisotropicFiltering = p.AnisotropicFiltering;
            QualitySettings.realtimeReflectionProbes = p.RealtimeReflectionProbes;
            QualitySettings.particleRaycastBudget = p.ParticleRaycastBudget;
            Time.maximumDeltaTime = p.MaximumDeltaTime;
            QualitySettings.vSyncCount = p.VSyncCount;
            Application.targetFrameRate = p.TargetFrameRate;

            for (int i = 0; i < gameplayCameras.Length; i++)
            {
                Camera cam = gameplayCameras[i];
                if (cam == null) continue;
                cam.farClipPlane = p.CameraFarDistance;
                UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = p.PostProcessing;
                data.antialiasing = p.Antialiasing;
                data.antialiasingQuality = p.AntialiasingQuality;
            }

            ApplyTraffic(p);
            if (trafficViewRadius != null) trafficViewRadius.SetVisibleRadius(p.TrafficVisibleRadius, p.TrafficHideMargin);
            if (trafficSimulationLOD != null) trafficSimulationLOD.SetDistances(p.TrafficPhysicsDistance, p.TrafficBehindPhysicsDistance);
            if (pedestrianSpawner != null)
            {
                pedestrianSpawner.SetDensity(p.PeoplePerCar, p.MaxPedestrians);
                pedestrianSpawner.SetShadowDistance(p.PedestrianShadowDistance);
                pedestrianSpawner.SetDetail(p.PedestrianHitboxDistance, p.PedestrianSpawnsPerSecond, p.PedestrianPrewarmPool);
            }
            if (breakablePropManager != null)
            {
                breakablePropManager.SetResetRadius(p.BreakableResetRadius);
                breakablePropManager.SetMaxBrokenProps(p.MaxBrokenProps);
            }
            if (environmentVisibility != null) environmentVisibility.SetDistances(p.SmallPropDistance, p.MediumPropDistance, p.VegetationDistance);
            if (trafficTakeoverManager != null) trafficTakeoverManager.SetDormantDistance(p.VehicleSleepDistance);
            VehicleLights.SetGlobalRealtimeLightDistance(p.RealtimeLightDistance);

            Debug.Log($"[{nameof(GraphicsSettingsManager)}] Applied graphics profile '{p.DisplayName}' (RAM {SystemInfo.systemMemorySize} MB, VRAM {SystemInfo.graphicsMemorySize} MB).", this);
            OnProfileApplied?.Invoke(p);
        }

        private void ApplyTraffic(GraphicsProfile p)
        {
            if (trafficSpawner == null) return;
            if (!trafficStarted)
            {
                // Before iTS initialises: the pool itself is sized to the profile (fewer cars in memory on Low).
                trafficSpawner.totalAmountOfCars = p.TrafficAmount;
                trafficSpawner.amount = p.TrafficAmount;
                trafficSpawner.maxDistance = p.TrafficMaxDistance;
                return;
            }
            trafficSpawner.MaxDistance = p.TrafficMaxDistance;
            trafficSpawner.Amount = Mathf.Min(p.TrafficAmount, trafficSpawner.totalAmountOfCars);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => OnProfileApplied = null;
    }
}
