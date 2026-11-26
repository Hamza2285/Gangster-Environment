using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Engine character of one car model (configuration only, never changed at runtime): which RCC engine clips it uses and the
    /// pitch / volume range they are played at. <see cref="RCCVehicleAdapter"/> writes it onto the car's RCC_CarControllerV3 in
    /// its Awake, before RCC creates its engine AudioSources, so RCC and <see cref="VehicleEngineAudioSmoother"/> simply play these
    /// values. Empty clip fields keep the prefab's own clip. Heavier cars: lower pitch range; sports cars: higher and raspier clips.
    /// Assets live in Assets/_Project/Data/Vehicles/EngineSounds (created and assigned by Tools > OpenworldCrime > Vehicles >
    /// Car Polish > Apply Engine Sounds; re-running it never overwrites values tuned here).
    /// </summary>
    [CreateAssetMenu(menuName = "OpenworldCrime/Vehicles/Vehicle Engine Sound", fileName = "EngineSound")]
    public class VehicleEngineSound : ScriptableObject
    {
        [Header("RCC Clips (empty = keep the prefab's)")]
        [Tooltip("On-throttle loop for low revs.")]
        public AudioClip low;
        [Tooltip("On-throttle loop for high revs.")]
        public AudioClip high;
        [Tooltip("Off-throttle loops. Empty with 'Generate Off Sounds' = the on clips through a low-pass filter (RCC's own option).")]
        public AudioClip lowOff;
        public AudioClip highOff;
        public AudioClip idle;
        [Tooltip("Let RCC build the off-throttle sound from the on clips (low-pass filtered). Use when the set has no off clips.")]
        public bool generateOffSounds;

        [Header("Pitch / Volume")]
        [Tooltip("Pitch at idle revs. Lower = heavier engine.")]
        [Range(0.3f, 1f)] public float minPitch = 0.75f;
        [Tooltip("Pitch at the rev limit. Higher = more screaming engine.")]
        [Range(1f, 2f)] public float maxPitch = 1.75f;
        [Tooltip("Loudness of the engine loops at full revs.")]
        [Range(0f, 1f)] public float maxVolume = 0.85f;
        [Range(0f, 1f)] public float idleVolume = 0.85f;

        [Header("Traffic")]
        [Tooltip("Single loop used by the iTS traffic twin of this model (iTS drives its pitch). Empty = keep the traffic prefab's.")]
        public AudioClip trafficLoop;

        /// <summary>Writes the sound onto an RCC car. Call before RCC's Awake (it creates its AudioSources there).</summary>
        public void ApplyTo(RCC_CarControllerV3 car)
        {
            if (car == null) return;
            if (low != null) car.engineClipLow = low;
            if (high != null) car.engineClipHigh = high;
            if (lowOff != null) car.engineClipLowOff = lowOff;
            if (highOff != null) car.engineClipHighOff = highOff;
            if (idle != null) car.engineClipIdle = idle;
            // A new clip set without its own off clips must not be paired with the prefab's old off clips.
            if (low != null || high != null) car.autoCreateEngineOffSounds = generateOffSounds || lowOff == null || highOff == null;
            car.minEngineSoundPitch = minPitch;
            car.maxEngineSoundPitch = Mathf.Max(minPitch + 0.1f, maxPitch);
            car.maxEngineSoundVolume = maxVolume;
            car.idleEngineSoundVolume = idleVolume;
        }

        private void OnValidate()
        {
            if (maxPitch < minPitch + 0.1f) maxPitch = minPitch + 0.1f;
        }
    }
}
