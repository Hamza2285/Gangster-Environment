using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OpenworldCrime.Settings
{
    /// <summary>
    /// One graphics / performance preset (Low, Medium, High). Pure configuration data: <see cref="GraphicsSettingsManager"/>
    /// reads it and pushes the values into the engine and the game systems; nothing here is changed at runtime.
    /// Rendering values that live on the URP asset (shadow distance / resolution / cascades, render scale, HDR, SSAO through
    /// the renderer) come from the Pipeline Asset assigned here, so no shared URP asset is ever modified while playing.
    /// </summary>
    [CreateAssetMenu(menuName = "OpenworldCrime/Settings/Graphics Profile", fileName = "GraphicsProfile")]
    public class GraphicsProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string displayName = "Medium";
        [Tooltip("Picked automatically when the device has at least this much system memory (MB) and no choice is saved.")]
        [SerializeField, Min(0)] private int minSystemMemoryMB = 6000;
        [Tooltip("... and at least this much graphics memory (MB).")]
        [SerializeField, Min(0)] private int minGraphicsMemoryMB = 2000;

        [Header("Render Pipeline")]
        [Tooltip("QualitySettings level applied first (built-in quality values such as soft particles, skin weights).")]
        [SerializeField, Min(0)] private int qualityLevel = 3;
        [Tooltip("URP asset for this profile: shadow distance, shadow resolution and cascades, render scale, HDR, MSAA, " +
                 "additional lights; its renderer decides SSAO.")]
        [SerializeField] private UniversalRenderPipelineAsset pipelineAsset;

        [Header("Frame Rate")]
        [Tooltip("Frame rate cap (Application.targetFrameRate). -1 = platform default.")]
        [SerializeField] private int targetFrameRate = 60;
        [Tooltip("0 = vSync off (the target frame rate caps), 1 = sync to every display refresh.")]
        [SerializeField, Range(0, 2)] private int vSyncCount;

        [Header("Camera")]
        [Tooltip("Far clip plane (m) of the gameplay cameras.")]
        [SerializeField, Min(50f)] private float cameraFarDistance = 750f;
        [SerializeField] private bool postProcessing = true;
        [SerializeField] private AntialiasingMode antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        [SerializeField] private AntialiasingQuality antialiasingQuality = AntialiasingQuality.Medium;

        [Header("Quality Settings")]
        [SerializeField, Min(0.1f)] private float lodBias = 1f;
        [Tooltip("0 = full resolution textures, 1 = half, 2 = quarter.")]
        [SerializeField, Range(0, 3)] private int textureMipmapLimit;
        [SerializeField] private AnisotropicFiltering anisotropicFiltering = AnisotropicFiltering.Enable;
        [SerializeField] private bool realtimeReflectionProbes;
        [Tooltip("Particle collision raycasts per frame.")]
        [SerializeField, Min(0)] private int particleRaycastBudget = 256;
        [Tooltip("Longest frame time (s) the simulation advances by; lower stops slow frames from running many physics steps.")]
        [SerializeField, Range(0.05f, 0.34f)] private float maximumDeltaTime = 0.15f;

        [Header("Traffic (iTS)")]
        [Tooltip("Traffic cars. Also the iTS pool size when applied at scene start.")]
        [SerializeField, Min(0)] private int trafficAmount = 80;
        [Tooltip("iTS spawn / despawn distance (m) around the player.")]
        [SerializeField, Min(20f)] private float trafficMaxDistance = 125f;
        [Tooltip("Traffic cars are rendered (and heard) within this distance (m).")]
        [SerializeField, Min(20f)] private float trafficVisibleRadius = 140f;
        [SerializeField, Min(1f)] private float trafficHideMargin = 20f;
        [Tooltip("Traffic cars closer than this (m) to the camera run full WheelCollider physics; farther cars move kinematically " +
                 "along their lane (TrafficSimulationLOD).")]
        [SerializeField, Min(10f)] private float trafficPhysicsDistance = 70f;
        [Tooltip("Same for cars behind the camera (m).")]
        [SerializeField, Min(10f)] private float trafficBehindPhysicsDistance = 40f;

        [Header("Pedestrians")]
        [SerializeField, Min(0f)] private float peoplePerCar = 1.6f;
        [SerializeField, Min(0)] private int maxPedestrians = 150;
        [Tooltip("Only pedestrians this close (m) cast shadows. 0 = none.")]
        [SerializeField, Min(0f)] private float pedestrianShadowDistance = 35f;
        [Tooltip("Pedestrians this close (m) have per-bone bullet hitboxes (head shots) and live corpse physics; farther ones " +
                 "use one hit capsule and frozen corpses.")]
        [SerializeField, Min(0f)] private float pedestrianHitboxDistance = 25f;
        [Tooltip("Most pedestrians placed per second (gradual fill instead of a burst). 0 = no limit.")]
        [SerializeField, Min(0f)] private float pedestrianSpawnsPerSecond = 6f;
        [Tooltip("Create the whole pedestrian pool during the first seconds (no activation hitches later, more memory and a " +
                 "longer start-up cost).")]
        [SerializeField] private bool pedestrianPrewarmPool = true;

        [Header("Environment Visibility")]
        [Tooltip("Small props (signs, bollards, hydrants...) are hidden beyond this distance (m).")]
        [SerializeField, Min(10f)] private float smallPropDistance = 100f;
        [Tooltip("Medium objects (billboards, kiosks, shelters...) are hidden beyond this distance (m).")]
        [SerializeField, Min(10f)] private float mediumPropDistance = 200f;
        [Tooltip("Trees and palms are hidden beyond this distance (m).")]
        [SerializeField, Min(10f)] private float vegetationDistance = 240f;

        [Header("Vehicles and Objects")]
        [Tooltip("Real-time headlights of the player's vehicle within this distance (m) of the camera. 0 = off.")]
        [SerializeField, Min(0f)] private float realtimeLightDistance = 40f;
        [Tooltip("Unused drivable vehicles farther than this (m) go to sleep (RCC, wheels, audio and physics off).")]
        [SerializeField, Min(5f)] private float vehicleSleepDistance = 60f;
        [Tooltip("Broken props farther than this (m) are put back.")]
        [SerializeField, Min(1f)] private float breakableResetRadius = 60f;
        [SerializeField, Min(1)] private int maxBrokenProps = 16;

        public string DisplayName => displayName;
        public int MinSystemMemoryMB => minSystemMemoryMB;
        public int MinGraphicsMemoryMB => minGraphicsMemoryMB;
        public int QualityLevel => qualityLevel;
        public UniversalRenderPipelineAsset PipelineAsset => pipelineAsset;
        public int TargetFrameRate => targetFrameRate;
        public int VSyncCount => vSyncCount;
        public float CameraFarDistance => cameraFarDistance;
        public bool PostProcessing => postProcessing;
        public AntialiasingMode Antialiasing => antialiasing;
        public AntialiasingQuality AntialiasingQuality => antialiasingQuality;
        public float LodBias => lodBias;
        public int TextureMipmapLimit => textureMipmapLimit;
        public AnisotropicFiltering AnisotropicFiltering => anisotropicFiltering;
        public bool RealtimeReflectionProbes => realtimeReflectionProbes;
        public int ParticleRaycastBudget => particleRaycastBudget;
        public float MaximumDeltaTime => maximumDeltaTime;
        public int TrafficAmount => trafficAmount;
        public float TrafficMaxDistance => trafficMaxDistance;
        public float TrafficVisibleRadius => trafficVisibleRadius;
        public float TrafficHideMargin => trafficHideMargin;
        public float TrafficPhysicsDistance => trafficPhysicsDistance;
        public float TrafficBehindPhysicsDistance => trafficBehindPhysicsDistance;
        public float PeoplePerCar => peoplePerCar;
        public int MaxPedestrians => maxPedestrians;
        public float PedestrianShadowDistance => pedestrianShadowDistance;
        public float PedestrianHitboxDistance => pedestrianHitboxDistance;
        public float PedestrianSpawnsPerSecond => pedestrianSpawnsPerSecond;
        public bool PedestrianPrewarmPool => pedestrianPrewarmPool;
        public float SmallPropDistance => smallPropDistance;
        public float MediumPropDistance => mediumPropDistance;
        public float VegetationDistance => vegetationDistance;
        public float RealtimeLightDistance => realtimeLightDistance;
        public float VehicleSleepDistance => vehicleSleepDistance;
        public float BreakableResetRadius => breakableResetRadius;
        public int MaxBrokenProps => maxBrokenProps;
    }
}
