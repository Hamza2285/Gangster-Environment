using System.Collections;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Pedestrians
{
    /// <summary>
    /// Connects <see cref="PedestrianDamage"/> (on the pedestrian prefab variants) to the street pedestrian system
    /// (PedestrianWalker / PedestrianSpawner, which add themselves at runtime): a knocked-down or dead person leaves its lane,
    /// a survivor gets up and runs, a shot one panics, and a body is cleared after Corpse Lifetime once it is off camera, which
    /// hands the person back to the spawner's pool. A carjacked driver comes through here too.
    /// Awake runs when the pooled person is first placed (the pool's root is inactive), after the walker was set up.
    /// </summary>
    [DisallowMultipleComponent]
    public class PedestrianDamageBridge : MonoBehaviour
    {
        [SerializeField] private PedestrianDamage damage;
        [Tooltip("Seconds a body stays before it is cleared, as soon as no camera sees it.")]
        [SerializeField, Min(0f)] private float corpseLifetime = 20f;
        [Tooltip("A body closer than this (m) to the camera is kept, so it never vanishes right next to the player.")]
        [SerializeField, Min(0f)] private float corpseKeepDistance = 12f;

        private PedestrianWalker walker;
        private Coroutine corpseRoutine;

        private void Awake()
        {
            walker = GetComponent<PedestrianWalker>();
            if (damage == null || walker == null)
            {
                Debug.LogError($"[{nameof(PedestrianDamageBridge)}] Missing PedestrianDamage or PedestrianWalker on '{name}'. It will walk through hits.", this);
                enabled = false;
                return;
            }
            damage.OnKnockedDown += HandleKnockedDown;
            damage.OnDied += HandleDied;
            damage.OnRecovered += HandleRecovered;
            damage.OnHurt += HandleHurt;
            walker.Despawned += HandleDespawned;
        }

        private void OnDestroy()
        {
            if (damage == null || walker == null) return;
            damage.OnKnockedDown -= HandleKnockedDown;
            damage.OnDied -= HandleDied;
            damage.OnRecovered -= HandleRecovered;
            damage.OnHurt -= HandleHurt;
            walker.Despawned -= HandleDespawned;
        }

        private void HandleKnockedDown(Vector3 threat) => walker.EnterDown();

        private void HandleDied(Vector3 threat)
        {
            walker.EnterDown();
            if (corpseRoutine != null) StopCoroutine(corpseRoutine);
            corpseRoutine = StartCoroutine(CorpseRoutine());
        }

        private void HandleRecovered(Vector3 threat)
        {
            if (walker.Owner != null) walker.Owner.RecoverWalker(walker, threat);
        }

        private void HandleHurt(Vector3 threat)
        {
            if (walker.Owner != null && walker.State != PedestrianWalker.Mode.Down) walker.Owner.TriggerPanicFor(walker, threat);
        }

        private void HandleDespawned()
        {
            if (corpseRoutine != null)
            {
                StopCoroutine(corpseRoutine);
                corpseRoutine = null;
            }
            damage.ResetForReuse();
        }

        private IEnumerator CorpseRoutine()
        {
            yield return new WaitForSeconds(corpseLifetime);
            var wait = new WaitForSeconds(1f);
            while (!VehicleHealth.IsOutOfSight(damage.BodyPosition, corpseKeepDistance)) yield return wait;
            corpseRoutine = null;
            if (walker.Owner != null) walker.Owner.ReleaseDown(walker);
        }
    }
}
