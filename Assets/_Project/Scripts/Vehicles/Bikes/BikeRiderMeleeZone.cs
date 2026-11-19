using Invector;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// The part of a traffic bike rider that fists and melee weapons reach. Invector's melee hitboxes only hurt colliders tagged
    /// Pedestrian / Enemy and ignore the rider's bullet zones (VehicleHitbox layer), so a punch went straight through the rider.
    /// This trigger (Ignore Raycast layer: bullets never hit it, the shot rider keeps his own zones) knocks him off the bike
    /// through <see cref="TrafficBikeAdapter.KnockRiderOffNextFrame"/>. Created by the adapter on its rider; no Update.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class BikeRiderMeleeZone : MonoBehaviour, vIDamageReceiver
    {
        [SerializeField] private TrafficBikeAdapter adapter;
        [SerializeField] private OnReceiveDamage _onStartReceiveDamage = new OnReceiveDamage();
        [SerializeField] private OnReceiveDamage _onReceiveDamage = new OnReceiveDamage();

        public OnReceiveDamage onStartReceiveDamage => _onStartReceiveDamage;
        public OnReceiveDamage onReceiveDamage => _onReceiveDamage;

        public void Setup(TrafficBikeAdapter owner) => adapter = owner;

        public void TakeDamage(vDamage damage)
        {
            if (adapter == null || damage == null) return;
            _onStartReceiveDamage.Invoke(damage);
            adapter.KnockRiderOffNextFrame(damage.sender); // not inside the physics callback this arrives in
            _onReceiveDamage.Invoke(damage);
        }
    }
}
