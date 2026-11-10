using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Shared driving feel for the drivable (RCC) cars of one class: GTA V-like handling (weight, visible roll and pitch, finite
    /// grip that lets go progressively, light assists) layered on top of each car's own
    /// engine torque, mass, top speed and brakes (set per model by the builder, so sports cars stay faster and SUVs heavier).
    /// Configuration only, never changed at runtime. <see cref="RCCVehicleAdapter"/> applies it when the car is created, before
    /// RCC initialises, so editing an asset changes every car that uses it on the next play, without rebuilding prefabs.
    /// </summary>
    [CreateAssetMenu(menuName = "OpenworldCrime/Vehicles/Vehicle Handling Profile", fileName = "VehicleHandling")]
    public class VehicleHandlingProfile : ScriptableObject
    {
        [Header("Engine & Gearbox")]
        [Tooltip("Multiplies the car's own engine torque (its model value from the builder).")]
        [SerializeField, Range(0.5f, 3f)] private float torqueMultiplier = 1.4f;
        [Tooltip("RPM at which the engine gives its peak torque. Lower = stronger pull off the line.")]
        [SerializeField, Range(1500f, 6000f)] private float peakTorqueRpm = 3500f;
        [Tooltip("How fast the revs follow the throttle (s). Lower = snappier.")]
        [SerializeField, Range(0.02f, 0.4f)] private float engineInertia = 0.06f;
        [Tooltip("Time (s) the throttle is cut for a gear change. Short = no hesitation when accelerating.")]
        [SerializeField, Range(0f, 0.5f)] private float gearShiftingDelay = 0.08f;
        [Tooltip("Shift-up points as a share of top speed (higher = longer gears, later shifts).")]
        [SerializeField, Range(0.25f, 1f)] private float gearShiftingThreshold = 0.85f;
        [Tooltip("Clutch engagement smoothing (lower = bites faster).")]
        [SerializeField, Range(0.1f, 0.9f)] private float clutchInertia = 0.12f;
        [Tooltip("Multiplies the final drive ratio (higher = quicker acceleration, revs out sooner).")]
        [SerializeField, Range(0.6f, 1.6f)] private float finalDriveMultiplier = 1f;

        [Header("Steering (speed sensitive)")]
        [Tooltip("Front wheel angle (deg) when slow: sharp turns in town.")]
        [SerializeField, Range(20f, 50f)] private float lowSpeedSteerAngle = 38f;
        [Tooltip("Front wheel angle (deg) at the speed below: calm, precise steering on the highway.")]
        [SerializeField, Range(3f, 25f)] private float highSpeedSteerAngle = 9f;
        [Tooltip("Speed (km/h) at which the steering has narrowed to the high-speed angle.")]
        [SerializeField, Range(60f, 300f)] private float highSpeedSteerAtKmh = 150f;
        [Tooltip("Automatic counter-steer while sliding (0 = none). Makes slides easy to catch.")]
        [SerializeField, Range(0f, 1f)] private float counterSteering = 0.6f;

        [Header("Driving Assists")]
        [Tooltip("Turns the velocity toward where the car points (stops the car drifting wide).")]
        [SerializeField, Range(0f, 1f)] private float steerHelperLinear = 0.3f;
        [Tooltip("Damps unwanted yaw (spins) toward the steering direction.")]
        [SerializeField, Range(0f, 1f)] private float steerHelperAngular = 0.25f;
        [Tooltip("Reduces front grip loss when the rear steps out (0 = off).")]
        [SerializeField, Range(0f, 1f)] private float tractionHelper = 0.3f;
        [SerializeField] private bool abs = true;
        [Tooltip("Traction control: limits wheelspin so full throttle gives the best launch.")]
        [SerializeField] private bool tcs = true;
        [SerializeField, Range(0.05f, 1f)] private float tcsStrength = 0.6f;
        [SerializeField] private bool esp = true;
        [SerializeField, Range(0.05f, 1f)] private float espStrength = 0.3f;

        [Header("Stability")]
        [Tooltip("Moves the centre of mass (local m; y negative = lower). Lower = less body roll and no tipping over.")]
        [SerializeField] private Vector3 centerOfMassOffset = new Vector3(0f, -0.2f, 0.05f);
        [Tooltip("Anti-roll bar force (N) front / rear. Higher = flatter in corners; more rear than front = more agile.")]
        [SerializeField, Min(0f)] private float antiRollFront = 9000f;
        [SerializeField, Min(0f)] private float antiRollRear = 8000f;
        [Tooltip("Multiplies the car's downforce (grip at speed).")]
        [SerializeField, Range(0f, 4f)] private float downForceMultiplier = 1.5f;
        [Tooltip("Rigidbody angular damping: higher = steadier, less wobble and fewer spins; too high kills handbrake turns.")]
        [SerializeField, Range(0f, 2f)] private float angularDamping = 0.3f;

        [Header("Suspension (per kg of car mass)")]
        [Tooltip("Spring stiffness per kg of car mass (N/m per kg).")]
        [SerializeField, Range(10f, 80f)] private float springPerKg = 34f;
        [Tooltip("Damper per kg of car mass. Higher = settles at once, no floaty bouncing.")]
        [SerializeField, Range(0.5f, 8f)] private float damperPerKg = 3.2f;

        [Header("Tyres (WheelCollider friction curves)")]
        [SerializeField] private float forwardExtremumSlip = 0.35f;
        [SerializeField] private float forwardExtremumValue = 1.3f;
        [SerializeField] private float forwardAsymptoteSlip = 0.8f;
        [SerializeField] private float forwardAsymptoteValue = 0.9f;
        [SerializeField] private float sidewaysExtremumSlip = 0.25f;
        [SerializeField] private float sidewaysExtremumValue = 1.3f;
        [SerializeField] private float sidewaysAsymptoteSlip = 0.6f;
        [Tooltip("Grip left once the tyre slides. Higher = slides recover easily; lower = drifty.")]
        [SerializeField] private float sidewaysAsymptoteValue = 1.0f;

        [Header("Brakes")]
        [Tooltip("Multiplies the car's own brake torque.")]
        [SerializeField, Range(0.5f, 3f)] private float brakeMultiplier = 1.3f;
        [Tooltip("Handbrake torque on the rear wheels, as a multiple of the brake torque.")]
        [SerializeField, Range(0.5f, 4f)] private float handbrakeMultiplier = 1.5f;

        /// <summary>
        /// Applies the profile to an RCC car that has not initialised yet (called from Awake before RCC's own Awake), so RCC
        /// builds its torque curve, gears, steering range and tyre curves from these values.
        /// </summary>
        public void ApplyTo(RCC_CarControllerV3 car, Rigidbody body)
        {
            if (car == null) return;
            car.maxEngineTorque *= torqueMultiplier;
            car.maxEngineTorqueAtRPM = Mathf.Clamp(peakTorqueRpm, car.minEngineRPM + 100f, car.maxEngineRPM - 100f);
            car.engineInertia = engineInertia;
            car.gearShiftingDelay = gearShiftingDelay;
            car.gearShiftingThreshold = gearShiftingThreshold;
            car.clutchInertia = clutchInertia;
            car.finalRatio *= finalDriveMultiplier;
            // The prefabs carry gear speeds built for another top speed; rebuild them for this car.
            car.InitGears();

            car.steerAngle = lowSpeedSteerAngle;
            car.highspeedsteerAngle = highSpeedSteerAngle;
            car.highspeedsteerAngleAtspeed = highSpeedSteerAtKmh;
            car.useCounterSteering = counterSteering > 0f;
            car.counterSteeringFactor = counterSteering;

            car.steeringHelper = steerHelperLinear > 0f || steerHelperAngular > 0f;
            car.steerHelperLinearVelStrength = steerHelperLinear;
            car.steerHelperAngularVelStrength = steerHelperAngular;
            car.tractionHelper = tractionHelper > 0f;
            car.tractionHelperStrength = tractionHelper;
            car.angularDragHelper = false;
            car.ABS = abs;
            car.TCS = tcs;
            car.TCSStrength = tcsStrength;
            car.ESP = esp;
            car.ESPStrength = espStrength;

            car.antiRollFrontHorizontal = antiRollFront;
            car.antiRollRearHorizontal = antiRollRear;
            car.downForce *= downForceMultiplier;
            car.brakeTorque *= brakeMultiplier;
            if (car.COM != null) car.COM.localPosition += centerOfMassOffset;

            float mass = body != null ? body.mass : 1500f;
            if (body != null) body.angularDamping = angularDamping;
            foreach (RCC_WheelCollider wheel in car.GetComponentsInChildren<RCC_WheelCollider>(true))
            {
                wheel.handbrakeMultiplier = handbrakeMultiplier;
                WheelCollider collider = wheel.GetComponent<WheelCollider>();
                if (collider == null) continue;
                JointSpring spring = collider.suspensionSpring;
                spring.spring = mass * springPerKg;
                spring.damper = mass * damperPerKg;
                collider.suspensionSpring = spring;
                collider.forwardFriction = Curve(collider.forwardFriction, forwardExtremumSlip, forwardExtremumValue, forwardAsymptoteSlip, forwardAsymptoteValue);
                collider.sidewaysFriction = Curve(collider.sidewaysFriction, sidewaysExtremumSlip, sidewaysExtremumValue, sidewaysAsymptoteSlip, sidewaysAsymptoteValue);
            }
        }

        private static WheelFrictionCurve Curve(WheelFrictionCurve curve, float extremumSlip, float extremumValue, float asymptoteSlip, float asymptoteValue)
        {
            curve.extremumSlip = extremumSlip;
            curve.extremumValue = extremumValue;
            curve.asymptoteSlip = asymptoteSlip;
            curve.asymptoteValue = asymptoteValue;
            return curve;
        }
    }
}
