using Invector;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Police
{
    /// <summary>
    /// One police shot at the player (shared by officers on foot and police cars). Hitscan with a hit chance (distance, target
    /// speed) instead of physical bullets: one linecast per shot, no projectile objects. A hit goes through Invector's damage
    /// (the player's vHealthController) or, while he drives, mostly into his car's <see cref="VehicleHealth"/>.
    /// </summary>
    public static class PoliceGunfire
    {
        private const float FastTargetSpeed = 5f;
        private const float MissSpread = 1.6f;

        /// <summary>True when the officer at <paramref name="from"/> has a clear line to the player (or his car).</summary>
        public static bool HasLineOfFire(Vector3 from, WantedManager wanted, PoliceSettings settings, out Vector3 aimPoint)
        {
            aimPoint = AimPoint(wanted);
            return !Physics.Linecast(from, aimPoint, settings.sightBlockers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Fires one shot from <paramref name="gun"/> at the player. Returns true when it hit.</summary>
        public static bool ShootPlayer(PoliceSidearm gun, Transform shooter, WantedManager wanted, PoliceSettings settings)
        {
            Vector3 from = gun.Muzzle.position;
            Vector3 aimPoint = AimPoint(wanted);
            float distance = Vector3.Distance(from, aimPoint);
            float chance = Mathf.Lerp(settings.hitChanceNear, settings.hitChanceFar, distance / Mathf.Max(1f, settings.fireRange));
            if (wanted.PlayerSpeed > FastTargetSpeed) chance *= settings.movingTargetFactor;
            bool hit = Random.value < chance;

            Vector3 impact = aimPoint;
            if (!hit)
            {
                // A near miss beside the target. The tracer ends there rather than flying on past him, where it would cross the
                // third-person camera behind the player as a huge streak.
                Vector3 offset = Random.insideUnitSphere * MissSpread;
                offset.y = Mathf.Abs(offset.y) * 0.5f;
                impact = aimPoint + offset;
            }
            gun.Fire(impact);
            if (hit) ApplyHit(shooter, wanted, settings, impact, (aimPoint - from).normalized);
            return hit;
        }

        private static Vector3 AimPoint(WantedManager wanted)
        {
            return wanted.PlayerTarget.position + Vector3.up * (wanted.PlayerInVehicle ? 1f : 1.2f);
        }

        private static void ApplyHit(Transform shooter, WantedManager wanted, PoliceSettings settings, Vector3 impact, Vector3 direction)
        {
            float playerDamage = settings.shotDamage;
            IVehicleController vehicle = wanted.PlayerVehicle;
            if (vehicle != null)
            {
                VehicleHealth carHealth = vehicle.transform.GetComponent<VehicleHealth>();
                if (carHealth != null) carHealth.ApplyDamage(settings.vehicleShotDamage, shooter);
                playerDamage *= settings.playerInsideDamageShare;
            }
            int damage = Mathf.RoundToInt(playerDamage);
            if (damage <= 0 || wanted.PlayerManager == null || wanted.PlayerManager.Controller == null) return;
            var controller = wanted.PlayerManager.Controller;
            controller.TakeDamage(new vDamage
            {
                damageValue = damage,
                sender = shooter,
                hitPosition = impact,
                force = direction * 2f,
                damageType = "PoliceShot",
                hitReaction = false
            });
            // Just the damage: Invector answers a hit without a hit reaction with its recoil (the arms-up block pose), and that
            // pose on every police bullet looks like he is shielding himself. Its triggers were set just now, so clear them.
            Animator animator = controller.GetComponent<Animator>();
            if (animator != null && controller.currentHealth > 0f)
            {
                animator.ResetTrigger(TriggerRecoil);
                animator.ResetTrigger(ResetState);
            }
        }

        private static readonly int TriggerRecoil = Animator.StringToHash("TriggerRecoil");
        private static readonly int ResetState = Animator.StringToHash("ResetState");
    }
}
