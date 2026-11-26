using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Upshifts an RCC car (automatic gearbox) before it reaches the rev limiter (RCC itself is not edited).
    /// RCC only shifts up once the car has reached a fixed speed for the gear, spaced evenly up to the top speed, while its gear
    /// ratios are not even: the middle gears run out of revs well before that speed, so the car sat on the rev limiter, which cuts
    /// the throttle off and on several times a second - the engine sound fluttered like rapid gear changes (measured on the
    /// 310 km/h car: 126 limiter cuts in 30 s of full throttle). Here, when the revs reach Upshift Rev Share of the limit under
    /// throttle in any gear but the top one, the next gear is engaged through RCC's own ChangeGear. RCC's downshift rule (speed
    /// below the lower gear's shift speed AND revs under its downshift RPM) cannot fire right after, as the revs after an upshift
    /// stay well above the downshift RPM. (The top gear is made long enough for the car's top speed by
    /// <see cref="VehicleHandlingProfile"/>, so it does not meet the limiter either.) Only while the car is driven by the player;
    /// a few field compares per physics step.
    /// Added at runtime by <see cref="RCCVehicleAdapter"/> to every drivable car.
    /// </summary>
    [DisallowMultipleComponent]
    public class VehicleShiftAssist : MonoBehaviour
    {
        [SerializeField, Range(0.8f, 0.99f)] private float upshiftRevShare = 0.94f;
        [Tooltip("Minimum time (s) between two assisted shifts (lets the revs settle in the new gear).")]
        [SerializeField, Min(0f)] private float minSecondsBetweenShifts = 0.5f;
        [Tooltip("No assisted upshift below this speed (km/h).")]
        [SerializeField, Min(0f)] private float minUpshiftKmh = 15f;

        private RCC_CarControllerV3 car;
        private float lastShift = -10f;

        private void Awake()
        {
            car = GetComponent<RCC_CarControllerV3>();
            if (car == null) enabled = false;
        }

        private void FixedUpdate()
        {
            if (!car.canControl || !car.engineRunning || !car.automaticGear || car.semiAutomaticGear || car.changingGear || car.NGear) return;
            if (car.direction != 1) return;
            bool topGear = car.currentGear >= car.gears.Length - 1;
            float rpm = WheelRevs();
            if (topGear) return;
            if (car.throttleInput < 0.5f && !car.cutGas) return;
            // Revs from the wheels only: pushing against a wall slips the clutch and spins the engine up while the car stands,
            // which climbed the gears at a standstill.
            if (car.speed < minUpshiftKmh || rpm < car.maxEngineRPM * upshiftRevShare) return;
            if (Time.time - lastShift < minSecondsBetweenShifts) return;
            lastShift = Time.time;
            car.StartCoroutine(car.ChangeGear(car.currentGear + 1));
        }

        // As RCC computes the wheel part of its engine RPM: mean driven-wheel RPM x final drive x gear ratio.
        private float WheelRevs()
        {
            RCC_WheelCollider[] wheels = car.allWheelColliders;
            if (wheels == null) return car.engineRPM;
            float sum = 0f;
            for (int i = 0; i < wheels.Length; i++)
                if (wheels[i] != null && wheels[i].canPower) sum += wheels[i].wheelCollider.rpm;
            return Mathf.Abs(sum / Mathf.Max(1, car.poweredWheels)) * car.finalRatio * car.gears[car.currentGear].maxRatio;
        }
    }
}
