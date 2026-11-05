using Invector;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Metal bullet hitbox of a vehicle (trigger on the VehicleHitbox layer, or the solid body boxes of traffic cars). Invector's
    /// projectiles and melee call <see cref="vIDamageReceiver.TakeDamage"/> on the collider they hit; it is passed to the
    /// vehicle's <see cref="VehicleHealth"/>. No Update, no physics cost of its own.
    /// </summary>
    [DisallowMultipleComponent]
    public class VehicleHitZone : MonoBehaviour, vIDamageReceiver
    {
        [SerializeField] private VehicleHealth health;
        [Tooltip("Damage multiplier of this zone.")]
        [SerializeField, Min(0f)] private float damageMultiplier = 1f;

        [SerializeField] private OnReceiveDamage _onStartReceiveDamage = new OnReceiveDamage();
        [SerializeField] private OnReceiveDamage _onReceiveDamage = new OnReceiveDamage();

        public OnReceiveDamage onStartReceiveDamage => _onStartReceiveDamage;
        public OnReceiveDamage onReceiveDamage => _onReceiveDamage;

        private void Awake()
        {
            if (health == null) health = GetComponentInParent<VehicleHealth>();
            if (health == null) Debug.LogWarning($"[{nameof(VehicleHitZone)}] No VehicleHealth above '{name}'; hits on it are ignored.", this);
        }

        public void TakeDamage(vDamage damage)
        {
            if (damage == null || health == null) return;
            _onStartReceiveDamage.Invoke(damage);
            health.ApplyDamage(damage.damageValue * damageMultiplier, damage.sender);
            _onReceiveDamage.Invoke(damage);
        }
    }
}
