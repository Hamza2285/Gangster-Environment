using Invector.vCharacterController;
using Invector.vShooter;
using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Makes pedestrians panic when the player threatens them with a gun:
    /// - aiming: only the first pedestrian on the actual aim line, within Aim Threat Distance and in line of sight
    ///   (walls / buildings on the Line Of Sight Blockers layers stop it) panics;
    /// - every shot panics people around the player (gunshot hearing), along the bullet path and around the impact point,
    ///   independent of the aim distance.
    /// Once panicked, a pedestrian calms down through the walker's own release rules (panic duration and the spawner's
    /// Calm Distance, which is much larger than the aim distance), so there is no calm/flee flip-flop at the aim boundary.
    /// Driven by <see cref="PlayerCombatController"/>'s aim / shot events; the aim check only runs while aiming.
    /// </summary>
    [DisallowMultipleComponent]
    public class PedestrianThreatSource : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerCombatController combat;
        [Tooltip("Supplies the aim point (AimPosition) and the active weapon's aim reference.")]
        [SerializeField] private vShooterMeleeInput shooterInput;
        [SerializeField] private PedestrianSpawner pedestrians;

        [Header("Aimed At")]
        [Tooltip("Aiming alone only scares pedestrians within this distance (m) of the player. Gunshots, damage and nearby " +
                 "violence are separate and still reach further.")]
        [SerializeField, Min(1f)] private float aimThreatDistance = 10f;
        [Tooltip("How close (metres) the aim line has to pass to a pedestrian's body centre.")]
        [SerializeField, Min(0.1f)] private float aimTolerance = 0.9f;
        [Tooltip("Seconds between aim checks while aiming.")]
        [SerializeField, Min(0f)] private float aimCheckInterval = 0.1f;
        [Tooltip("Layers that block aim-only detection (solid walls and buildings). Gunshots ignore this.")]
        [SerializeField] private LayerMask lineOfSightBlockers = 1; // Default (buildings, walls, props)

        [Header("Gunshot")]
        [Tooltip("Everyone within this distance of the player runs when a shot is fired.")]
        [SerializeField, Min(0f)] private float gunshotNoiseRadius = 25f;
        [Tooltip("Pedestrians within this distance of the bullet path run.")]
        [SerializeField, Min(0f)] private float bulletPathRadius = 2.5f;
        [Tooltip("Pedestrians within this distance of the bullet impact point run.")]
        [SerializeField, Min(0f)] private float impactRadius = 8f;
        [Tooltip("The bullet path is only checked up to this distance from the muzzle.")]
        [SerializeField, Min(1f)] private float maxShotDistance = 150f;

        private bool isAiming;
        private float nextAimCheckTime;

        private void Awake()
        {
            if (combat == null || shooterInput == null || pedestrians == null)
            {
                Debug.LogError($"[{nameof(PedestrianThreatSource)}] Missing references on '{name}' " +
                               "(PlayerCombatController, vShooterMeleeInput, PedestrianSpawner). Pedestrians will not react to guns.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (combat == null) return;
            combat.OnAimChanged += HandleAimChanged;
            combat.OnShot += HandleShot;
        }

        private void OnDisable()
        {
            if (combat == null) return;
            combat.OnAimChanged -= HandleAimChanged;
            combat.OnShot -= HandleShot;
            isAiming = false;
        }

        // Runs the (allocation-free) aim check only while aiming, at most every aimCheckInterval seconds.
        private void Update()
        {
            if (!isAiming || Time.time < nextAimCheckTime) return;
            nextAimCheckTime = Time.time + aimCheckInterval;
            CheckAimedAt();
        }

        /// <summary>Panics the first pedestrian on the current aim line. Returns true if someone panicked.</summary>
        public bool CheckAimedAt()
        {
            Vector3 from, to;
            if (!TryGetAimLine(aimThreatDistance, out from, out to)) return false;
            // Small slack so a pedestrian whose collider stopped the aim point still counts (aim point is on its surface).
            to += (to - from).normalized * aimTolerance;
            PedestrianWalker target = pedestrians.FindFirstAlongLine(from, to, aimTolerance);
            if (target == null) return false;

            // Measured from the player, so a long weapon or the camera offset never extends the reach.
            Vector3 body = target.transform.position + Vector3.up * PedestrianSpawner.BodyCenterHeight;
            Vector3 toTarget = body - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > aimThreatDistance * aimThreatDistance) return false;
            if (Physics.Linecast(from, body, lineOfSightBlockers, QueryTriggerInteraction.Ignore)) return false;
            return pedestrians.TriggerPanicFor(target, transform.position);
        }

        /// <summary>Reacts to a gunshot fired from the player along the current aim line.</summary>
        public void ReportGunshot()
        {
            Vector3 threat = transform.position;
            if (gunshotNoiseRadius > 0f) pedestrians.TriggerPanic(threat, gunshotNoiseRadius);

            Vector3 from, to;
            if (!TryGetAimLine(maxShotDistance, out from, out to)) return;
            if (bulletPathRadius > 0f) pedestrians.TriggerPanicAlongLine(from, to, bulletPathRadius, threat, false);
            if (impactRadius > 0f) pedestrians.TriggerPanic(to, impactRadius);
        }

        private void HandleAimChanged(bool aiming)
        {
            isAiming = aiming;
            nextAimCheckTime = 0f;
        }

        private void HandleShot(vShooterWeapon weapon) => ReportGunshot();

        // The line Invector itself aims along: weapon aim reference -> AimPosition (already stopped by blockAimLayer, e.g. walls).
        private bool TryGetAimLine(float maxDistance, out Vector3 from, out Vector3 to)
        {
            vShooterWeapon weapon = shooterInput.CurrentActiveWeapon;
            Transform origin = weapon != null && weapon.aimReference != null ? weapon.aimReference : null;
            from = origin != null ? origin.position : transform.position + Vector3.up * 1.4f;
            to = shooterInput.AimPosition;

            Vector3 line = to - from;
            float length = line.magnitude;
            if (length < 0.01f) return false;
            if (length > maxDistance) to = from + line / length * maxDistance;
            return true;
        }
    }
}
