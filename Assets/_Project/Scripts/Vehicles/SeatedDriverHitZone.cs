using Invector;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// One hit collider (head, torso) of a seated driver. Invector's projectiles and melee call
    /// <see cref="vIDamageReceiver.TakeDamage"/> on the collider they hit; the damage is scaled for this zone and passed to the
    /// driver's <see cref="SeatedDriverHealth"/>. The collider is a trigger on the kinematic bullet hitbox (VehicleHitbox layer),
    /// so it has no physics cost and never affects the vehicle's mass. Bullets only reach it through the window glass: the
    /// metal body hitbox stops them.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class SeatedDriverHitZone : MonoBehaviour, vIDamageReceiver
    {
        [SerializeField] private SeatedDriverHealth health;
        [Tooltip("Damage multiplier for this zone (e.g. 4 for the head).")]
        [SerializeField, Min(0f)] private float damageMultiplier = 1f;

        [SerializeField] private OnReceiveDamage _onStartReceiveDamage = new OnReceiveDamage();
        [SerializeField] private OnReceiveDamage _onReceiveDamage = new OnReceiveDamage();

        public OnReceiveDamage onStartReceiveDamage => _onStartReceiveDamage;
        public OnReceiveDamage onReceiveDamage => _onReceiveDamage;

        private void Awake()
        {
            if (health == null) health = GetComponentInParent<SeatedDriverHealth>();
            if (health == null)
            {
                Debug.LogWarning($"[{nameof(SeatedDriverHitZone)}] No SeatedDriverHealth for '{name}'; hits on this driver are ignored.", this);
                enabled = false;
            }
        }

        public void TakeDamage(vDamage damage)
        {
            if (damage != null) ApplyDamage(damage, 1f, damage.hitPosition);
        }

        /// <summary>
        /// Applies a hit to this zone, scaled by the zone and <paramref name="multiplier"/> (e.g. the share kept after window glass).
        /// The same bullet (same <see cref="vDamage"/> instance) is only counted once, however it arrives.
        /// </summary>
        public void ApplyDamage(vDamage source, float multiplier, Vector3 hitPoint)
        {
            if (!enabled || source == null || health == null) return;
            var scaled = new vDamage(source)
            {
                damageValue = source.damageValue * damageMultiplier * multiplier,
                hitPosition = hitPoint,
                receiver = transform
            };
            _onStartReceiveDamage.Invoke(scaled);
            health.ReceiveDamage(scaled, source);
            _onReceiveDamage.Invoke(scaled);
        }
    }
}
