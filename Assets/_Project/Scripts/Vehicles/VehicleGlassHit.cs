using Invector;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Window glass collider of a vehicle's bullet hitbox. Invector's projectile crosses glass (bullet life settings: VehicleHitbox
    /// layer, Glass tag) but continues only on its next frame, from where the glass was; on a fast car the window frame has moved
    /// under that point by then. So when a bullet hits the glass, this continues the shot at that exact moment through the
    /// cabin: if it reaches a seated driver's hit zone before any metal, the zone takes the hit (the zone ignores the same
    /// bullet arriving again a frame later). No per-frame cost: it only runs when the glass is hit.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class VehicleGlassHit : MonoBehaviour, vIDamageReceiver
    {
        private const int MaxHits = 8;

        [Tooltip("Share of the bullet's damage left after passing the glass (keep in step with the bullet life settings).")]
        [SerializeField, Range(0f, 1f)] private float damageKeptThroughGlass = 0.85f;
        [Tooltip("How far (m) behind the glass a driver can be hit.")]
        [SerializeField, Min(0.1f)] private float reachBehindGlass = 2.5f;
        [Tooltip("Layers of the bullet hitbox (metal body, glass and driver zones).")]
        [SerializeField] private LayerMask hitboxLayers = 1 << 21;

        [SerializeField] private OnReceiveDamage _onStartReceiveDamage = new OnReceiveDamage();
        [SerializeField] private OnReceiveDamage _onReceiveDamage = new OnReceiveDamage();

        public OnReceiveDamage onStartReceiveDamage => _onStartReceiveDamage;
        public OnReceiveDamage onReceiveDamage => _onReceiveDamage;

        private static readonly RaycastHit[] hits = new RaycastHit[MaxHits];
        private Collider glassCollider;

        private void Awake()
        {
            glassCollider = GetComponent<Collider>();
        }

        public void TakeDamage(vDamage damage)
        {
            if (damage == null || damage.damageValue <= 0f) return;
            _onStartReceiveDamage.Invoke(damage);

            Vector3 direction = damage.force.sqrMagnitude > 0.0001f ? damage.force.normalized
                : damage.sender != null ? (damage.hitPosition - damage.sender.position).normalized : Vector3.zero;
            if (direction.sqrMagnitude > 0f)
            {
                int count = Physics.RaycastNonAlloc(damage.hitPosition + direction * 0.005f, direction, hits, reachBehindGlass, hitboxLayers, QueryTriggerInteraction.Collide);
                SeatedDriverHitZone zone = null;
                Vector3 zonePoint = Vector3.zero;
                float nearest = float.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    Collider hit = hits[i].collider;
                    if (hit == glassCollider || hits[i].distance >= nearest) continue;
                    // Other panes are crossed as well; metal stops the shot.
                    if (hit.GetComponent<VehicleGlassHit>() != null) continue;
                    nearest = hits[i].distance;
                    zone = hit.GetComponent<SeatedDriverHitZone>();
                    zonePoint = hits[i].point;
                }
                if (zone != null) zone.ApplyDamage(damage, damageKeptThroughGlass, zonePoint);
            }

            _onReceiveDamage.Invoke(damage);
        }
    }
}
