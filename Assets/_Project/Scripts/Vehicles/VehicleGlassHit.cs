using Invector;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Window glass collider of a vehicle's bullet hitbox (the cabin). Invector's projectile stops here, so this continues the shot
    /// at that moment through the cabin along the crosshair's line: if it reaches a seated driver's hit zone before a body panel,
    /// the zone takes the hit and the car takes none. No per-frame cost: it only runs when the glass is hit.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class VehicleGlassHit : MonoBehaviour, vIDamageReceiver
    {
        private const int MaxHits = 8;

        [Tooltip("Share of the bullet's damage left after passing the glass.")]
        [SerializeField, Range(0f, 1f)] private float damageKeptThroughGlass = 0.85f;
        [Tooltip("How far (m) behind the glass a driver can be hit.")]
        [SerializeField, Min(0.1f)] private float reachBehindGlass = 2.5f;
        [Tooltip("Layers of the bullet hitbox (metal body, glass and driver zones).")]
        [SerializeField] private LayerMask hitboxLayers = 1 << 21;
        [Tooltip("The car's health: shots on the cabin (roof, pillars, windows) also wear the car down. Optional.")]
        [SerializeField] private VehicleHealth health;
        [Tooltip("Share of the bullet's damage the car takes.")]
        [SerializeField, Range(0f, 1f)] private float hullDamageShare = 0.5f;

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
            bool reachedDriver = false;

            Vector3 direction = ShotLine(damage);
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
                    // Other panes are crossed as well; metal stops the shot, except the top of the lower body box: inside the cabin
                    // that is the door sills' height, and the driver's lap and chest sit below it.
                    if (hit.GetComponent<VehicleGlassHit>() != null) continue;
                    if (hit.GetComponent<VehicleHitZone>() != null && hits[i].normal.y > 0.7f) continue;
                    nearest = hits[i].distance;
                    zone = hit.GetComponent<SeatedDriverHitZone>();
                    zonePoint = hits[i].point;
                }
                if (zone != null)
                {
                    zone.ApplyDamage(damage, damageKeptThroughGlass, zonePoint);
                    reachedDriver = true;
                }
            }

            if (!reachedDriver && health != null) health.ApplyDamage(damage.damageValue * hullDamageShare, damage.sender);
            _onReceiveDamage.Invoke(damage);
        }

        // The player's bullet flies from the muzzle to where the crosshair's ray met this box; beyond it the shot continues along
        // the crosshair's ray, not the muzzle's line, so what is under the crosshair is what gets hit, however close he stands.
        private static Vector3 ShotLine(vDamage damage)
        {
            if (damage.sender != null && damage.sender.CompareTag("Player"))
            {
                Camera view = Camera.main;
                if (view != null) return (damage.hitPosition - view.transform.position).normalized;
            }
            if (damage.force.sqrMagnitude > 0.0001f) return damage.force.normalized;
            return damage.sender != null ? (damage.hitPosition - damage.sender.position).normalized : Vector3.zero;
        }
    }
}
