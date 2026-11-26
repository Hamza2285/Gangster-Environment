using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Speed-sensitive steering and controllable slides for the player's RCC car, on top of RCC's Semi Arcade behaviour (RCC itself
    /// is not edited). That behaviour gives every tyre constant grip and a strong steer helper, so the car turned at full strength at
    /// any speed and never let go. Here:
    /// - the steering lock narrows with speed (RCC's own high-speed lock is set every step, as the bikes do);
    /// - the rear loses grip when the player holds a hard turn at high speed, or at any speed on the handbrake;
    /// - the steer helper (which drags the velocity back in line) relaxes during a slide;
    /// - past the maximum slide angle grip comes back and yaw is damped, so the car drifts instead of spinning round.
    /// Values are in <see cref="VehicleHandlingProfile"/>. Only while the player drives; added by <see cref="RCCVehicleAdapter"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)] // before RCC's FixedUpdate, which reads these values
    public class VehicleDriftAssist : MonoBehaviour
    {
        private RCC_CarControllerV3 car;
        private Rigidbody body;
        private VehicleHandlingProfile profile;
        private float baseHighSteer, baseHighSteerAt, baseLinear, baseAngular;
        private bool ready, applied;

        /// <summary>0 = full grip, 1 = full slide.</summary>
        public float Slide { get; private set; }
        /// <summary>Angle (deg) between where the car points and where it moves.</summary>
        public float SlideAngle { get; private set; }

        /// <summary>Also active while a script drives the car (handling tests).</summary>
        public bool ActiveWhenScripted { get; set; }

        private void Start() // after RCC's Awake, which applies the Semi Arcade limits
        {
            car = GetComponent<RCC_CarControllerV3>();
            body = GetComponent<Rigidbody>();
            RCCVehicleAdapter adapter = GetComponent<RCCVehicleAdapter>();
            profile = adapter != null ? adapter.HandlingProfile : null;
            ready = car != null && body != null && profile != null && car.RearLeftWheelCollider != null && car.RearRightWheelCollider != null;
            if (!ready)
            {
                enabled = false;
                return;
            }
            baseHighSteer = car.highspeedsteerAngle;
            baseHighSteerAt = car.highspeedsteerAngleAtspeed;
            baseLinear = car.steerHelperLinearVelStrength;
            baseAngular = car.steerHelperAngularVelStrength;
        }

        private void OnDisable() => Restore();

        private void FixedUpdate()
        {
            if (!ready) return;
            if (!car.canControl || (car.externalController && !ActiveWhenScripted))
            {
                Restore();
                return;
            }
            applied = true;

            float dt = Time.fixedDeltaTime;
            float kmh = body.linearVelocity.magnitude * 3.6f;
            Vector3 local = transform.InverseTransformDirection(body.linearVelocity);
            bool forward = local.z > 1f;
            SlideAngle = forward && kmh > 5f ? Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg : 0f;
            float steer = car.steerInput;

            // How much the rear should slide.
            bool handbrake = car.handbrakeInput > 0.5f && kmh > profile.HandbrakeSlideMinKmh;
            float turnSlide = Mathf.InverseLerp(profile.DriftFromKmh, profile.DriftFullKmh, kmh) * Mathf.InverseLerp(profile.DriftSteerInput, 1f, Mathf.Abs(steer));
            float target = !forward ? 0f : handbrake ? 1f : turnSlide;
            float rate = target < Slide ? profile.SlideRecoverRate : handbrake ? profile.HandbrakeSlideRate : profile.TurnSlideRate;
            Slide = Mathf.MoveTowards(Slide, target, rate * dt);

            // Toward the maximum angle the rear grips again (not fully: a sudden full bite tips tall cars over).
            float tooFar = Mathf.InverseLerp(profile.MaxSlideAngle * 0.4f, profile.MaxSlideAngle, Mathf.Abs(SlideAngle));
            float slidingGrip = handbrake ? profile.HandbrakeRearGrip : profile.TurnRearGrip;
            float grip = Mathf.Lerp(Mathf.Lerp(1f, slidingGrip, Slide), 1f, tooFar * 0.6f);
            car.RearLeftWheelCollider.tractionHelpedSidewaysStiffness = grip;
            car.RearRightWheelCollider.tractionHelpedSidewaysStiffness = grip;

            float helper = Mathf.Lerp(1f, profile.SteerHelperWhileSliding, Slide);
            car.steerHelperLinearVelStrength = baseLinear * helper;
            car.steerHelperAngularVelStrength = baseAngular * helper;

            // Steering lock for this speed; steering into the slide (counter-steer) gets the slide angle on top.
            float limit = profile.SteerLockAt(kmh);
            if (steer * SlideAngle > 0f) limit = Mathf.Min(profile.LowSpeedSteerAngle, limit + Mathf.Abs(SlideAngle) * profile.CounterSteerLockShare);
            car.highspeedsteerAngleAtspeed = 0.5f; // RCC's steering limit = the angle set below at any real speed
            car.highspeedsteerAngle = limit;

            if (!forward || !AnyWheelGrounded()) return;
            // A slide must not roll a tall car over: lean beyond the free angle is pushed back, and the roll damped.
            float lean = Vector3.SignedAngle(Vector3.ProjectOnPlane(Vector3.up, transform.forward), transform.up, transform.forward);
            float tooMuch = Mathf.Abs(lean) - profile.FreeRollAngle;
            if (Slide + Mathf.Abs(SlideAngle) * 0.1f > 0.2f && tooMuch > 0f && Mathf.Abs(lean) < 60f)
            {
                float roll = Vector3.Dot(body.angularVelocity, transform.forward);
                body.AddRelativeTorque(Vector3.forward * (-Mathf.Sign(lean) * tooMuch * profile.RollGuard - roll * 4f), ForceMode.Acceleration);
            }

            float yaw = Vector3.Dot(body.angularVelocity, transform.up);
            if (handbrake && Mathf.Abs(steer) > 0.2f && tooFar <= 0f && yaw * Mathf.Sign(steer) < profile.HandbrakeMaxYawRate)
                body.AddRelativeTorque(Vector3.up * (steer * profile.HandbrakeYawKick * Mathf.Clamp01(kmh / 60f)), ForceMode.Acceleration);
            // Yaw that opens the slide further (nose turning away from the movement) is capped, down to almost none at the limit.
            if (Slide > 0.05f && yaw * SlideAngle < 0f)
            {
                float cap = Mathf.Lerp(handbrake ? profile.HandbrakeMaxYawRate : profile.TurnMaxYawRate, 0.1f, tooFar);
                float over = Mathf.Abs(yaw) - cap;
                if (over > 0f) body.AddRelativeTorque(Vector3.up * (-Mathf.Sign(yaw) * over * Mathf.Min(profile.SpinDamping, 0.5f / dt)), ForceMode.Acceleration);
            }
        }

        // Not RCC's own flag, which drops when one wheel lifts (tall cars in a hard slide, exactly when the guard is needed).
        private bool AnyWheelGrounded()
        {
            RCC_WheelCollider[] wheels = car.allWheelColliders;
            for (int i = 0; i < wheels.Length; i++)
                if (wheels[i] != null && wheels[i].isGrounded) return true;
            return false;
        }

        private void Restore()
        {
            if (!applied || car == null) return;
            applied = false;
            Slide = 0f;
            SlideAngle = 0f;
            car.highspeedsteerAngle = baseHighSteer;
            car.highspeedsteerAngleAtspeed = baseHighSteerAt;
            car.steerHelperLinearVelStrength = baseLinear;
            car.steerHelperAngularVelStrength = baseAngular;
            if (car.RearLeftWheelCollider != null) car.RearLeftWheelCollider.tractionHelpedSidewaysStiffness = 1f;
            if (car.RearRightWheelCollider != null) car.RearRightWheelCollider.tractionHelpedSidewaysStiffness = 1f;
        }
    }
}
