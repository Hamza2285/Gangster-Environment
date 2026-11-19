using System;
using System.Collections;
using System.Collections.Generic;
using OpenworldCrime.Pedestrians;
using OpenworldCrime.Police;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Economy
{
    /// <summary>
    /// Cash from dead NPCs (global manager under ///Manager///, not a Singleton). When a pedestrian, police officer, or a driver
    /// or rider killed in his seat dies, a cash bundle drops by the body now and then (once it has fallen). Walking over it (or
    /// rolling over it slowly in a car / on a bike) adds the money through <see cref="MoneyManager"/>, plays the pickup sound and
    /// raises <see cref="OnCashCollected"/> for the "+$X" popup. Bundles come from a pool, vanish after a while, and the oldest
    /// one is reused when the cap is reached.
    /// </summary>
    [DisallowMultipleComponent]
    public class CashDropManager : MonoBehaviour
    {
        [Header("Pool")]
        [SerializeField] private CashPickup pickupPrefab;
        [SerializeField, Range(1, 40)] private int maxPickups = 12;
        [SerializeField, Min(5f)] private float lifetime = 60f;

        [Header("Drops")]
        [SerializeField, Range(0f, 1f)] private float dropChance = 0.7f;
        [SerializeField] private Vector2Int pedestrianAmount = new Vector2Int(5, 50);
        [SerializeField] private Vector2Int policeAmount = new Vector2Int(20, 90);
        [SerializeField] private Vector2Int driverAmount = new Vector2Int(10, 60);
        [Tooltip("Seconds after the death before the cash appears (the body has fallen by then).")]
        [SerializeField, Min(0f)] private float dropDelay = 1.2f;
        [Tooltip("Layers the cash is placed on.")]
        [SerializeField] private LayerMask groundLayers = 1;

        [Header("Feedback")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip pickupSound;

        /// <summary>Cash taken. Args: amount, where.</summary>
        public event Action<int, Vector3> OnCashCollected;

        private readonly List<CashPickup> pool = new List<CashPickup>();
        private readonly List<CashPickup> active = new List<CashPickup>();
        private Action<CashPickup> collectHandler;
        private WaitForSeconds delay;

        private void Awake()
        {
            if (pickupPrefab == null) { Debug.LogError($"[{nameof(CashDropManager)}] No cash pickup prefab on '{name}'.", this); enabled = false; return; }
            collectHandler = Collect;
            delay = new WaitForSeconds(dropDelay);
            for (int i = 0; i < maxPickups; i++)
            {
                CashPickup p = Instantiate(pickupPrefab, transform);
                p.name = pickupPrefab.name + " " + i;
                p.Hide();
                pool.Add(p);
            }
        }

        private void OnEnable()
        {
            PedestrianDamage.OnAnyDied += HandlePedestrianDied;
            SeatedDriverHealth.OnAnyKilled += HandleDriverKilled;
        }

        private void OnDisable()
        {
            PedestrianDamage.OnAnyDied -= HandlePedestrianDied;
            SeatedDriverHealth.OnAnyKilled -= HandleDriverKilled;
        }

        private void HandlePedestrianDied(PedestrianDamage person)
        {
            if (person == null || UnityEngine.Random.value > dropChance) return;
            bool police = PoliceOfficerUnit.IsOfficer(person);
            Vector2Int range = police ? policeAmount : pedestrianAmount;
            var ragdoll = person.GetComponent<PedestrianRagdoll>();
            StartCoroutine(DropLater(() => ragdoll != null ? ragdoll.HipsPosition : person.transform.position, range));
        }

        // A driver slumps in his seat: the cash lands by the vehicle's side. A rider's body is flung, the bike lies near.
        private void HandleDriverKilled(SeatedDriverHealth driver)
        {
            if (driver == null || UnityEngine.Random.value > dropChance) return;
            Transform seat = driver.transform;
            Rigidbody vehicle = driver.GetComponentInParent<Rigidbody>();
            Vector3 side = vehicle != null ? -vehicle.transform.right * 1.6f : Vector3.zero;
            Vector3 fallback = seat.position;
            StartCoroutine(DropLater(() => (seat != null ? seat.position : fallback) + side, driverAmount));
        }

        private IEnumerator DropLater(Func<Vector3> where, Vector2Int range)
        {
            yield return delay;
            // Beside the body rather than under it.
            Vector2 side = UnityEngine.Random.insideUnitCircle.normalized * 0.6f;
            Vector3 point = where() + new Vector3(side.x, 0f, side.y);
            if (Physics.Raycast(point + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 6f, groundLayers, QueryTriggerInteraction.Ignore))
                point = hit.point;
            Spawn(point + Vector3.up * 0.15f, UnityEngine.Random.Range(range.x, range.y + 1));
        }

        private void Spawn(Vector3 position, int amount)
        {
            CashPickup pickup;
            if (pool.Count > 0)
            {
                pickup = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);
            }
            else
            {
                // At the cap: the oldest bundle moves here.
                pickup = active[0];
                active.RemoveAt(0);
                pickup.Hide();
            }
            pickup.Show(position, amount, collectHandler);
            active.Add(pickup);
            if (active.Count == 1) StartCoroutine(ExpireRoutine());
        }

        private void Collect(CashPickup pickup)
        {
            int amount = pickup.Amount;
            Vector3 where = pickup.transform.position;
            Release(pickup);
            if (MoneyManager.Instance != null) MoneyManager.Instance.Add(amount);
            if (audioSource != null && pickupSound != null)
            {
                audioSource.transform.position = where;
                audioSource.PlayOneShot(pickupSound);
            }
            OnCashCollected?.Invoke(amount, where);
        }

        private void Release(CashPickup pickup)
        {
            if (!active.Remove(pickup)) return;
            pickup.Hide();
            pool.Add(pickup);
        }

        // Runs only while bundles are lying around: takes away those left longer than the lifetime.
        private IEnumerator ExpireRoutine()
        {
            var wait = new WaitForSeconds(1f);
            while (active.Count > 0)
            {
                yield return wait;
                for (int i = active.Count - 1; i >= 0; i--)
                    if (Time.time - active[i].SpawnTime > lifetime) Release(active[i]);
            }
        }
    }
}
