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

        [Tooltip("Seconds over which a punched person is shoved back (stagger).")]
        [SerializeField, Min(0.01f)] private float staggerSeconds = 0.15f;

        /// <summary>Punch reactions and fighting back (see <see cref="PedestrianBrawler"/>).</summary>
        [System.Serializable]
        public class BrawlSettings
        {
            [Tooltip("Share of these people who fight back after being hit (0 = never, e.g. women; 0.3 for men).")]
            [Range(0f, 1f)] public float fightBackChance;
            [Header("Hit reaction")]
            [Min(0f)] public float smallShove = 0.3f;
            [Min(0f)] public float bigShove = 0.75f;
            [Min(0.01f)] public float shoveSeconds = 0.18f;
            [Min(0.1f)] public float smallReactSeconds = 0.65f;
            [Min(0.1f)] public float bigReactSeconds = 0.95f;
            [Tooltip("People within this radius run off at the first blow (0 = nobody).")]
            [Min(0f)] public float bystanderPanicRadius = 10f;
            [Header("Fighting")]
            [Min(0.5f)] public float punchRange = 1.25f;
            [Min(0f)] public float walkSpeed = 1.6f;
            [Min(0f)] public float runSpeed = 3.6f;
            [Min(0.1f)] public float attackIntervalMin = 0.9f;
            [Min(0.1f)] public float attackIntervalMax = 1.8f;
            [Tooltip("Multiplier on the punch damage dealt to the player (jab 6, cross 7, hook 12).")]
            [Min(0f)] public float damageScale = 1f;
            [Range(0f, 1f)] public float blockChance = 0.25f;
            [Range(0f, 1f)] public float dodgeChance = 0.12f;
            [Min(0.1f)] public float blockSeconds = 0.6f;
            [Min(0f)] public float dodgeSpeed = 2.6f;
            [Tooltip("Gives up and runs below this health (0-1).")]
            [Range(0f, 1f)] public float giveUpHealth = 0.35f;
            [Tooltip("Gives up when the player stays farther than this (m) for 3 s.")]
            [Min(2f)] public float giveUpDistance = 15f;
            public LayerMask groundLayers = 1;
            [Header("Sounds")]
            public AudioClip[] hitSounds = new AudioClip[0];
            public AudioClip[] blockSounds = new AudioClip[0];
            public AudioClip[] swingSounds = new AudioClip[0];
        }

        [SerializeField] private BrawlSettings brawl = new BrawlSettings();

        private PedestrianWalker walker;
        private UnityEngine.AI.NavMeshAgent agent;
        private Coroutine corpseRoutine;
        private Coroutine staggerRoutine;
        private PedestrianBrawler brawler;
        private PedestrianPhoneCall phoneCall; // added by WitnessCallManager when he is picked to phone the police

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
            damage.OnStaggered += HandleStaggered;
            walker.Despawned += HandleDespawned;
            agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            // Punch reactions / fighting back for people whose controller has the hit states (not the police).
            brawler = gameObject.AddComponent<PedestrianBrawler>();
            brawler.Init(damage, walker, brawl);
            if (brawler.HasAnimations)
            {
                damage.OnMeleeHit += HandleMeleeHit;
                damage.OnMeleeBlocked += HandleMeleeBlocked;
            }
        }

        private void OnDestroy()
        {
            if (damage == null || walker == null) return;
            damage.OnKnockedDown -= HandleKnockedDown;
            damage.OnDied -= HandleDied;
            damage.OnRecovered -= HandleRecovered;
            damage.OnHurt -= HandleHurt;
            damage.OnStaggered -= HandleStaggered;
            damage.OnMeleeHit -= HandleMeleeHit;
            damage.OnMeleeBlocked -= HandleMeleeBlocked;
            walker.Despawned -= HandleDespawned;
        }

        private void HandleKnockedDown(Vector3 threat)
        {
            if (brawler != null) brawler.KnockedDown();
            walker.EnterDown();
        }

        private void HandleMeleeHit(Vector3 direction, int hitNumber, bool heavy) =>
            brawler.TakeHit(direction, hitNumber, heavy, LastAttackerPosition());

        private void HandleMeleeBlocked(Vector3 attacker) => brawler.Blocked(attacker);

        private Vector3 LastAttackerPosition()
        {
            var current = PlayableCharacter.Current;
            return current != null ? current.transform.position : transform.position - transform.forward;
        }

        private void HandleDied(Vector3 threat)
        {
            walker.EnterDown();
            if (corpseRoutine != null) StopCoroutine(corpseRoutine);
            corpseRoutine = StartCoroutine(CorpseRoutine());
        }

        private void HandleRecovered(Vector3 threat)
        {
            // A fighter who is still fit gets back into the fight; a phoning witness calls; everyone else runs.
            if (brawler != null && brawler.TryResumeAfterGettingUp()) return;
            // A carjack victim picked to phone the police calls first (he runs once the call is over).
            if (phoneCall == null) TryGetComponent(out phoneCall);
            if (phoneCall != null && phoneCall.TryStartAfterGettingUp(threat)) return;
            if (walker.Owner != null) walker.Owner.RecoverWalker(walker, threat);
        }

        private void HandleHurt(Vector3 threat)
        {
            if (walker.Owner != null && walker.State != PedestrianWalker.Mode.Down) walker.Owner.TriggerPanicFor(walker, threat);
        }

        // A punch that does not knock down: a quick shove back, then the hurt reaction (panic) takes over as before.
        private void HandleStaggered(Vector3 push)
        {
            if (walker.State == PedestrianWalker.Mode.Down || push.sqrMagnitude < 0.0001f) return;
            if (staggerRoutine != null) StopCoroutine(staggerRoutine);
            staggerRoutine = StartCoroutine(StaggerRoutine(push));
        }

        private IEnumerator StaggerRoutine(Vector3 push)
        {
            if (agent == null) agent = GetComponent<UnityEngine.AI.NavMeshAgent>(); // added by the walker when it first panics
            float moved = 0f;
            while (moved < 1f)
            {
                float step = Mathf.Min(1f - moved, Time.deltaTime / Mathf.Max(0.01f, staggerSeconds));
                moved += step;
                Vector3 delta = push * step;
                // A fleeing person is driven by the NavMeshAgent (moving the transform would be undone); a walker in its lane
                // takes the shove through its own Nudge.
                if (agent != null && agent.enabled && agent.isOnNavMesh) agent.Move(delta);
                else walker.Nudge(delta);
                yield return null;
            }
            staggerRoutine = null;
        }

        private void HandleDespawned()
        {
            if (brawler != null) brawler.ResetForReuse();
            if (phoneCall != null) phoneCall.ResetForReuse();
            if (staggerRoutine != null)
            {
                StopCoroutine(staggerRoutine);
                staggerRoutine = null;
            }
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
