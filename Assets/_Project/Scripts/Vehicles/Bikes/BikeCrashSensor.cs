using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Crash judgement for a drivable motorbike (on the bike prefab next to <see cref="RCCVehicleAdapter"/>; per-instance). While the
    /// player rides it, every solid hit is measured (<see cref="BikeImpact"/>) and judged by <see cref="BikeCrashSettings"/>:
    /// a nudge wobbles the bike, a medium hit is a stumble (a bigger wobble and lost speed), a hard or badly placed hit throws the
    /// rider off through <see cref="PlayerVehicleInteraction.TryCrashThrow"/>, flying on with the speed he had (GTA style).
    /// Idle bikes cost one FixedUpdate copy of the velocity; nothing is judged while nobody rides.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class BikeCrashSensor : MonoBehaviour
    {
        [SerializeField] private BikeCrashSettings settings;
        [SerializeField] private RCCVehicleAdapter vehicle;
        [SerializeField] private Rigidbody body;
        [Tooltip("Share of the speed (along the hit) a stumble takes off the bike.")]
        [SerializeField, Range(0f, 1f)] private float stumbleSpeedLoss = 0.35f;
        [Tooltip("Logs every judged hit (impact speed, angle, verdict) for tuning.")]
        [SerializeField] private bool logHits;

        private Vector3 velocityBefore;
        private float nextHitTime;

        private void Awake()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            if (vehicle == null) vehicle = GetComponent<RCCVehicleAdapter>();
            if (settings == null || vehicle == null)
            {
                Debug.LogError($"[{nameof(BikeCrashSensor)}] Missing crash settings or RCCVehicleAdapter on '{name}'; crashes never unseat the rider.", this);
                enabled = false;
            }
        }

        private void FixedUpdate()
        {
            velocityBefore = body.linearVelocity;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!enabled || !vehicle.IsControlled || Time.time < nextHitTime) return;
            if (!BikeImpact.Measure(transform, velocityBefore, collision, settings, out BikeImpact.Result hit)) return;
            BikeCrashSettings.Outcome outcome = settings.Judge(hit.ImpactKmh, hit.AngleDegrees);
            if (outcome == BikeCrashSettings.Outcome.None) return;
            nextHitTime = Time.time + settings.hitCooldown;
            if (logHits) Debug.Log($"[{nameof(BikeCrashSensor)}] '{name}' hit '{collision.collider.name}': {hit.ImpactKmh:F0} km/h at {hit.AngleDegrees:F0} deg -> {outcome}", this);

            if (outcome == BikeCrashSettings.Outcome.Thrown)
            {
                // The rider sits under the seat while riding; found once per crash, never per frame.
                PlayerVehicleInteraction rider = GetComponentInChildren<PlayerVehicleInteraction>(true);
                float upShare = Mathf.Clamp01(hit.ImpactKmh / Mathf.Max(1f, settings.alwaysThrowKmh));
                Vector3 launch = velocityBefore * settings.carriedVelocity + hit.OtherVelocity * settings.pushShare
                                 + Vector3.up * (settings.upSpeed * upShare);
                if (rider != null && rider.TryCrashThrow(launch)) return;
            }

            float kick = outcome == BikeCrashSettings.Outcome.Stumble
                ? settings.stumbleRollSpeed
                : settings.wobbleRollSpeed * Mathf.Clamp01(hit.ImpactKmh / Mathf.Max(1f, settings.stayOnBelowKmh));
            BikeImpact.Wobble(body, transform, hit.Normal, kick);
            if (outcome == BikeCrashSettings.Outcome.Stumble && !body.isKinematic)
            {
                Vector3 along = Vector3.Project(body.linearVelocity, hit.Normal);
                body.linearVelocity -= along * stumbleSpeedLoss;
            }
        }
    }
}
