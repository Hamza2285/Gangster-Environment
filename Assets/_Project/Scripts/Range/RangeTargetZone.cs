using Invector;
using UnityEngine;

namespace OpenworldCrime.Range
{
    /// <summary>
    /// Head / torso hit box of a <see cref="RangeTarget"/>. Invector projectiles and melee call
    /// <see cref="vIDamageReceiver.TakeDamage"/> on the collider they hit; the zone forwards the hit to its target.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class RangeTargetZone : MonoBehaviour, vIDamageReceiver
    {
        [SerializeField] private RangeTarget target;
        [SerializeField] private RangeHitZone zone = RangeHitZone.Torso;

        [SerializeField] private OnReceiveDamage _onStartReceiveDamage = new OnReceiveDamage();
        [SerializeField] private OnReceiveDamage _onReceiveDamage = new OnReceiveDamage();

        public OnReceiveDamage onStartReceiveDamage => _onStartReceiveDamage;
        public OnReceiveDamage onReceiveDamage => _onReceiveDamage;

        private void Awake()
        {
            if (target == null) target = GetComponentInParent<RangeTarget>();
            if (target == null) Debug.LogWarning($"[{nameof(RangeTargetZone)}] No RangeTarget above '{name}'.", this);
        }

        public void TakeDamage(vDamage damage)
        {
            if (damage == null || target == null) return;
            _onStartReceiveDamage.Invoke(damage);
            target.ReportHit(zone, damage.hitPosition);
            _onReceiveDamage.Invoke(damage);
        }
    }
}
