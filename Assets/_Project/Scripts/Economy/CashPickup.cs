using System;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Economy
{
    /// <summary>
    /// A cash bundle dropped by a dead NPC (pooled by <see cref="CashDropManager"/>). The player collects it by walking over it,
    /// or driving / riding over it slowly. Its trigger sits on the Ignore Raycast layer so bullets and aiming pass through.
    /// Bobs and turns slowly while it lies there (only the few active bundles update).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class CashPickup : MonoBehaviour
    {
        [SerializeField] private Transform model;
        [SerializeField, Min(0f)] private float bobHeight = 0.08f;
        [SerializeField, Min(0f)] private float bobSpeed = 2.2f;
        [SerializeField] private float spinSpeed = 70f;
        [Tooltip("Fastest a vehicle may go (m/s) to pick the cash up.")]
        [SerializeField, Min(0f)] private float maxVehicleSpeed = 9f;

        public int Amount { get; private set; }
        public float SpawnTime { get; private set; }

        private Action<CashPickup> collected;
        private Vector3 modelRest;
        private float phase;

        private void Awake()
        {
            if (model != null) modelRest = model.localPosition;
        }

        /// <summary>Places the bundle (on the ground) with an amount; <paramref name="onCollected"/> is called once when taken.</summary>
        public void Show(Vector3 position, int amount, Action<CashPickup> onCollected)
        {
            Amount = amount;
            collected = onCollected;
            SpawnTime = Time.time;
            phase = UnityEngine.Random.value * 6.28f;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            gameObject.SetActive(true);
        }

        /// <summary>Back to the pool, fully reset.</summary>
        public void Hide()
        {
            collected = null;
            Amount = 0;
            if (model != null) model.localPosition = modelRest;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (model == null) return;
            model.localPosition = modelRest + Vector3.up * (Mathf.Sin(Time.time * bobSpeed + phase) * bobHeight);
            model.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        }

        private void OnTriggerEnter(Collider other) => TryCollect(other);
        private void OnTriggerStay(Collider other) => TryCollect(other); // a car that was too fast can slow down on it

        private void TryCollect(Collider other)
        {
            if (collected == null) return;
            Rigidbody body = other.attachedRigidbody;
            if (body == null) return;
            bool player = body.TryGetComponent(out OpenworldCrime.Player.PlayerManager _); // qualified: SharedAssets has a global PlayerManager
            if (!player && body.TryGetComponent(out IVehicleController vehicle))
                player = vehicle.IsControlled && vehicle.CurrentSpeed <= maxVehicleSpeed;
            if (!player) return;
            Action<CashPickup> callback = collected;
            collected = null;
            callback(this);
        }
    }
}
