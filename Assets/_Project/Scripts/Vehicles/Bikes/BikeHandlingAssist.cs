using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Arcade motorbike handling on top of RCC (our drivable bikes are RCC cars with narrow wheel sets, see DrivableBikeBuilder):
    /// - Speed-sensitive steering: the front wheel angle at full lock is the one that drives the bike round the tightest circle
    ///   the tyres can hold (radius = speed^2 / lateral grip, never below the low-speed minimum). RCC's own linear
    ///   "high speed steer angle" let a 1.4 m wheelbase bike turn far tighter than its grip at speed (measured 1.8 g at 83 km/h),
    ///   so it snapped into turns and slid.
    /// - Yaw damping: the turn rate is pulled towards the one the steering asks for, which takes out the snap and the overshoot
    ///   after quick keyboard / touch steering (measured 2.4x the steady turn rate) without slowing a deliberate turn.
    /// Writes RCC's public steering fields each physics step (before RCC reads them); RCC's source is not changed.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    [DisallowMultipleComponent]
    public class BikeHandlingAssist : MonoBehaviour
    {
        [SerializeField] private RCC_CarControllerV3 car;
        [SerializeField] private Rigidbody body;
        [Tooltip("Front to rear axle (m). 0 = measured from the wheel colliders.")]
        [SerializeField, Min(0f)] private float wheelBase;

        [Header("Steering")]
        [Tooltip("Full-lock front wheel angle when walking pace.")]
        [SerializeField, Range(5f, 45f)] private float lowSpeedSteer = 26f;
        [Tooltip("Tightest turning circle radius (m) at low speed.")]
        [SerializeField, Min(1f)] private float minTurnRadius = 3.2f;
        [Tooltip("Sideways acceleration (m/s²) the full-lock wheel angle is shaped for. The tyres' slip roughly doubles the real " +
                 "circle at these small angles, so about 22 gives close to 1 g in practice.")]
        [SerializeField, Min(2f)] private float lateralGrip = 22f;
        [Tooltip("Never below this lock (degrees), so the bike still steers at top speed.")]
        [SerializeField, Range(0.2f, 5f)] private float minSteer = 1f;

        [Header("Yaw damping")]
        [Tooltip("How strongly the turn rate is pulled to the rate the steering asks for (1/s).")]
        [SerializeField, Min(0f)] private float yawAssist = 5f;
        [Tooltip("Only above this speed (m/s); slow manoeuvres are left alone.")]
        [SerializeField, Min(0f)] private float yawAssistMinSpeed = 3f;

        public float CurrentSteerLimit { get; private set; }

        private void Awake()
        {
            if (car == null) car = GetComponent<RCC_CarControllerV3>();
            if (body == null) body = GetComponent<Rigidbody>();
            if (car == null || body == null)
            {
                Debug.LogError($"[{nameof(BikeHandlingAssist)}] '{name}' needs an RCC_CarControllerV3 and a Rigidbody.", this);
                enabled = false;
                return;
            }
            if (wheelBase <= 0f) wheelBase = MeasureWheelBase();
            // RCC lerps from its own steer angle to 'high speed steer angle' by speed / 'at speed': with 'at speed' near zero it
            // simply uses the angle written below.
            car.highspeedsteerAngleAtspeed = 0.5f;
        }

        private float MeasureWheelBase()
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (WheelCollider w in GetComponentsInChildren<WheelCollider>(true))
            {
                float z = transform.InverseTransformPoint(w.transform.position).z;
                min = Mathf.Min(min, z);
                max = Mathf.Max(max, z);
            }
            return max > min ? max - min : 1.4f;
        }

        private void FixedUpdate()
        {
            Vector3 velocity = body.linearVelocity;
            float speed = Mathf.Abs(Vector3.Dot(velocity, transform.forward));
            float radius = Mathf.Max(minTurnRadius, speed * speed / lateralGrip);
            float limit = Mathf.Clamp(Mathf.Atan(wheelBase / radius) * Mathf.Rad2Deg, minSteer, lowSpeedSteer);
            CurrentSteerLimit = limit;
            car.highspeedsteerAngle = limit;

            if (yawAssist <= 0f || speed < yawAssistMinSpeed || !car.canControl) return;
            // The turn rate the steering asks for: speed / radius at full lock, scaled by the input.
            float wanted = car.steerInput * speed / radius * Mathf.Sign(Vector3.Dot(velocity, transform.forward));
            float yawRate = Vector3.Dot(body.angularVelocity, transform.up);
            float error = wanted - yawRate;
            // Only take out the excess (snap / overshoot / slide); never add turn the tyres are not giving.
            if (Mathf.Sign(error) != Mathf.Sign(yawRate) || Mathf.Abs(wanted) < 0.01f)
                body.AddTorque(transform.up * error * yawAssist, ForceMode.Acceleration);
        }
    }
}
