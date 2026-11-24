using System.Reflection;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Steady engine sound for RCC cars at speed (RCC itself is not edited).
    /// Cause of the "flicker like fast gear changing" on fast cars: their top gear runs into RCC's rev limiter below their top
    /// speed. The limiter cuts the throttle to 0 and back several times a second, and in RCC a closed throttle also opens the
    /// automatic clutch, so RCC's engine RPM - which drives the sound pitch - drops by ~1000 RPM at every cut and climbs back
    /// (measured, 310 km/h car at full throttle: 54-126 limiter cuts per 30-40 s; pitch saw-tooth), and RCC switches between
    /// its on- and off-throttle engine clips at every cut (110-250 swaps).
    /// Here, after RCC has written its audio (LateUpdate):
    /// - pitch: once rolling, it follows the revs implied by the driven wheels and the gear (wheel RPM x final drive x gear ratio,
    ///   as RCC itself computes the in-gear part), which does not drop when the limiter opens the clutch, then is lightly
    ///   smoothed. Gear changes still drop it, since the ratio changes. At walking speed RCC's own RPM is used (clutch slipping).
    /// - clip mix: the on/off-throttle split is moved toward the throttle at a limited rate instead of jumping.
    /// The car's physics, gearbox and limiter are untouched. Only while the car is driven. Added at runtime by
    /// <see cref="RCCVehicleAdapter"/> to every drivable car.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public class VehicleEngineAudioSmoother : MonoBehaviour
    {
        [Tooltip("Pitch smoothing time constant (s). Higher = steadier, but gear changes sound softer.")]
        [SerializeField, Range(0.01f, 0.3f)] private float pitchSmoothing = 0.08f;
        [Tooltip("Speed range (km/h) over which the pitch moves from RCC's engine RPM to the wheel-linked revs.")]
        [SerializeField] private Vector2 wheelRevsFromKmh = new Vector2(12f, 30f);
        [Tooltip("How fast (per s) the sound goes from the off-throttle to the on-throttle clip, and back.")]
        [SerializeField, Min(0.5f)] private float throttleRise = 5f, throttleFall = 3f;

        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static FieldInfo highField, lowField, highOffField, lowOffField, idleField;

        private RCC_CarControllerV3 car;
        private AudioSource high, low, highOff, lowOff, idle;
        private float pitch = -1f, throttle;
        private float nextLookup;

        /// <summary>Pitch written now (debug / tests).</summary>
        public float SmoothedPitch => pitch;
        /// <summary>On-throttle clip share written now (debug / tests).</summary>
        public float SmoothedThrottle => throttle;

        private void Awake()
        {
            car = GetComponent<RCC_CarControllerV3>();
            if (car == null) { enabled = false; return; }
            if (highField == null)
            {
                System.Type t = typeof(RCC_CarControllerV3);
                highField = t.GetField("engineSoundHigh", Flags);
                lowField = t.GetField("engineSoundLow", Flags);
                highOffField = t.GetField("engineSoundHighOff", Flags);
                lowOffField = t.GetField("engineSoundLowOff", Flags);
                idleField = t.GetField("engineSoundIdle", Flags);
            }
        }

        private void LateUpdate()
        {
            if (!car.canControl || !car.engineRunning) { pitch = -1f; return; }
            if (high == null)
            {
                // RCC creates its audio sources in its Start; look them up once they exist (a field read, not a scene search).
                if (Time.unscaledTime < nextLookup) return;
                nextLookup = Time.unscaledTime + 0.5f;
                high = Get(highField); low = Get(lowField); highOff = Get(highOffField); lowOff = Get(lowOffField); idle = Get(idleField);
                if (high == null) return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            float rpm = car.engineRPM;
            if (car.direction == 1 && !car.NGear)
            {
                float rolling = Mathf.InverseLerp(wheelRevsFromKmh.x, wheelRevsFromKmh.y, car.speed);
                if (rolling > 0f)
                {
                    float inGear = Mathf.Clamp(WheelRevs(), car.minEngineRPM, car.maxEngineRPM);
                    rpm = Mathf.Lerp(rpm, inGear, rolling);
                }
            }
            float target = Mathf.Lerp(car.minEngineSoundPitch, car.maxEngineSoundPitch, rpm / car.maxEngineRPM);
            if (pitch < 0f) { pitch = target; throttle = Mathf.Clamp01(car.throttleInput); }
            pitch += (target - pitch) * (1f - Mathf.Exp(-dt / pitchSmoothing));
            float rawThrottle = Mathf.Clamp01(car.throttleInput);
            throttle = Mathf.MoveTowards(throttle, rawThrottle, (rawThrottle > throttle ? throttleRise : throttleFall) * dt);

            Split(high, highOff);
            Split(low, lowOff);
            if (idle != null) idle.pitch = pitch;
        }

        // As RCC computes the in-gear part of its engine RPM: mean driven-wheel RPM x final drive x gear ratio.
        private float WheelRevs()
        {
            RCC_WheelCollider[] wheels = car.allWheelColliders;
            if (wheels == null || car.gears == null || car.gears.Length == 0) return car.engineRPM;
            float sum = 0f;
            for (int i = 0; i < wheels.Length; i++)
                if (wheels[i] != null && wheels[i].canPower) sum += wheels[i].wheelCollider.rpm;
            int gear = Mathf.Clamp(car.currentGear, 0, car.gears.Length - 1);
            return Mathf.Abs(sum / Mathf.Max(1, car.poweredWheels)) * car.finalRatio * car.gears[gear].maxRatio;
        }

        // RCC wrote on = level * throttle and off = level * (1 - throttle); rewrite both with the smoothed throttle.
        private void Split(AudioSource on, AudioSource off)
        {
            if (on == null) return;
            on.pitch = pitch;
            if (off == null) return;
            float level = on.volume + off.volume;
            on.volume = level * throttle;
            off.volume = level * (1f - throttle);
            off.pitch = pitch;
        }

        private AudioSource Get(FieldInfo f) => f != null ? f.GetValue(car) as AudioSource : null;
    }
}
