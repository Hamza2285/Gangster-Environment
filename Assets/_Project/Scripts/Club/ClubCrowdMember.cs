using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Club
{
    /// <summary>
    /// A decorative club character (dancer, patron, DJ). Plays one of its loop states with a random start time and speed so
    /// nobody moves in sync, and optionally changes to another of its states now and then (cross-fade, coroutine; no Update).
    /// Dancers can react to the player (turn toward him and dance faster for a moment, e.g. when cash is thrown).
    /// The state names must exist on the Animator's controller, layer 0.
    /// </summary>
    [DisallowMultipleComponent]
    public class ClubCrowdMember : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [Tooltip("Loop states (layer 0) this character picks from.")]
        [SerializeField] private string[] states = new string[0];
        [Tooltip("Random playback speed range.")]
        [SerializeField] private Vector2 speedRange = new Vector2(0.92f, 1.08f);
        [Tooltip("Seconds between changes to another state (x = min, y = max). 0 keeps the first state.")]
        [SerializeField] private Vector2 switchInterval = Vector2.zero;
        [SerializeField, Min(0f)] private float crossFade = 0.4f;
        [Tooltip("Dancers react to thrown cash.")]
        [SerializeField] private bool isDancer;

        private static readonly List<ClubCrowdMember> activeDancers = new List<ClubCrowdMember>();

        /// <summary>Dancers currently enabled in the loaded club.</summary>
        public static IReadOnlyList<ClubCrowdMember> ActiveDancers => activeDancers;

        public bool IsReacting { get; private set; }

        private int[] stateHashes;
        private int currentIndex = -1;
        private float baseSpeed = 1f;
        private Coroutine reactRoutine;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null || states.Length == 0)
            {
                Debug.LogWarning($"[{nameof(ClubCrowdMember)}] '{name}' has no Animator or no states.", this);
                enabled = false;
                return;
            }
            stateHashes = new int[states.Length];
            for (int i = 0; i < states.Length; i++) stateHashes[i] = Animator.StringToHash(states[i]);
        }

        private void OnEnable()
        {
            if (stateHashes == null) return;
            if (isDancer) activeDancers.Add(this);
            currentIndex = Random.Range(0, stateHashes.Length);
            baseSpeed = Random.Range(speedRange.x, speedRange.y);
            animator.speed = baseSpeed;
            animator.Play(stateHashes[currentIndex], 0, Random.value);
            if (stateHashes.Length > 1 && switchInterval.y > 0f) StartCoroutine(SwitchRoutine());
        }

        private void OnDisable()
        {
            activeDancers.Remove(this);
            StopAllCoroutines();
            reactRoutine = null;
            IsReacting = false;
        }

        /// <summary>
        /// Turns toward <paramref name="target"/>, dances faster (with a fresh move) for <paramref name="duration"/> seconds,
        /// then turns back to the original facing. Calling it again while reacting extends the reaction.
        /// </summary>
        public void React(Vector3 target, float duration, float speedBoost = 1.35f)
        {
            if (!isActiveAndEnabled || stateHashes == null) return;
            if (reactRoutine != null) StopCoroutine(reactRoutine);
            reactRoutine = StartCoroutine(ReactRoutine(target, duration, speedBoost));
        }

        private IEnumerator ReactRoutine(Vector3 target, float duration, float speedBoost)
        {
            if (!IsReacting && stateHashes.Length > 1)
            {
                int next = (currentIndex + 1 + Random.Range(0, stateHashes.Length - 1)) % stateHashes.Length;
                currentIndex = next;
                animator.CrossFadeInFixedTime(stateHashes[currentIndex], crossFade, 0, Random.value);
            }
            IsReacting = true;
            Quaternion original = transform.rotation;
            Vector3 flat = target - transform.position;
            flat.y = 0f;
            Quaternion facing = flat.sqrMagnitude > 0.01f ? Quaternion.LookRotation(flat) : original;
            animator.speed = baseSpeed * speedBoost;

            yield return Turn(original, facing, 0.35f);
            yield return new WaitForSeconds(Mathf.Max(0f, duration - 0.7f));
            animator.speed = baseSpeed;
            yield return Turn(facing, original, 0.35f);
            IsReacting = false;
            reactRoutine = null;
        }

        private IEnumerator Turn(Quaternion from, Quaternion to, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                transform.rotation = Quaternion.Slerp(from, to, t / seconds);
                yield return null;
            }
            transform.rotation = to;
        }

        private IEnumerator SwitchRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(Random.Range(switchInterval.x, switchInterval.y));
                if (IsReacting) continue;
                int next = Random.Range(0, stateHashes.Length - 1);
                if (next >= currentIndex) next++; // never the same state again
                currentIndex = next;
                animator.CrossFadeInFixedTime(stateHashes[currentIndex], crossFade, 0, Random.value);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => activeDancers.Clear();
    }
}
