using System;
using System.Collections;
using OpenworldCrime.Economy;
using UnityEngine;

namespace OpenworldCrime.Club
{
    /// <summary>
    /// "Make it rain": while the player is seated, each throw costs <see cref="PricePerThrow"/> from the
    /// <see cref="MoneyManager"/>, plays the arm gesture, sends a burst of banknotes from the player's hand toward the
    /// nearest dancer and makes her react. The notes are particles of one pre-placed system that is re-aimed and emitted
    /// per throw (pooled, no physics, no per-note objects).
    /// </summary>
    [DisallowMultipleComponent]
    public class ClubCashThrower : MonoBehaviour
    {
        [SerializeField] private ClubPlayerSeating seating;
        [Tooltip("World-space banknote particle system (no emission of its own; emitted per throw).")]
        [SerializeField] private ParticleSystem cashBurst;
        [Tooltip("Optional sound per throw.")]
        [SerializeField] private AudioSource throwAudio;

        [Header("Tuning")]
        [SerializeField, Min(1)] private int pricePerThrow = 10;
        [SerializeField, Min(0.1f)] private float cooldown = 0.6f;
        [SerializeField, Min(1)] private int notesPerThrow = 24;
        [Tooltip("Quick flicks of the stack per throw (make it rain): Notes Per Throw leave the hand on each flick.")]
        [SerializeField, Min(1)] private int flicksPerThrow = 3;
        [SerializeField, Min(0f)] private float flickGap = 0.12f;
        [SerializeField, Min(0.5f)] private float throwSpeed = 6.5f;
        [Tooltip("Random spread of the notes around the throw direction (degrees).")]
        [SerializeField, Range(0f, 60f)] private float spread = 30f;
        [Tooltip("Seconds from the start of the gesture to the release of the notes.")]
        [SerializeField, Min(0f)] private float releaseDelay = 0.3f;
        [Tooltip("Dancers farther than this are not targeted (the notes fly forward instead).")]
        [SerializeField, Min(1f)] private float maxDancerDistance = 9f;
        [SerializeField, Min(0.5f)] private float reactSeconds = 3f;

        /// <summary>Short feedback for the HUD (e.g. not enough cash).</summary>
        public event Action<string> OnMessage;
        /// <summary>Raised after a paid throw.</summary>
        public event Action OnThrown;

        public int PricePerThrow => pricePerThrow;

        private float nextThrowTime;
        private string brokeMessage;

        private void Awake()
        {
            if (seating == null || cashBurst == null)
            {
                Debug.LogError($"[{nameof(ClubCashThrower)}] Missing seating or cash particle system on '{name}'.", this);
                enabled = false;
            }
            brokeMessage = "Not enough cash ($" + pricePerThrow + " per throw)";
        }

        public bool CanThrow => enabled && seating.IsSeated && Time.time >= nextThrowTime;

        /// <summary>Pays and throws (HUD button / key). Shows a message instead when the player cannot pay.</summary>
        public void Throw()
        {
            if (!CanThrow) return;
            MoneyManager money = MoneyManager.Instance;
            if (money == null || !money.TrySpend(pricePerThrow))
            {
                nextThrowTime = Time.time + cooldown;
                OnMessage?.Invoke(brokeMessage);
                return;
            }
            nextThrowTime = Time.time + cooldown;
            seating.PlayGesture();
            ClubCrowdMember dancer = FindNearestDancer(seating.SeatedPlayer.transform.position);
            StartCoroutine(Release(dancer));
            OnThrown?.Invoke();
        }

        private IEnumerator Release(ClubCrowdMember dancer)
        {
            yield return new WaitForSeconds(releaseDelay);
            if (!seating.IsSeated) yield break;
            Transform player = seating.SeatedPlayer.transform;
            Vector3 from = seating.RightHand != null ? seating.RightHand.position : player.position + Vector3.up * 1.2f;
            Vector3 target = dancer != null ? dancer.transform.position + Vector3.up * 1.2f : from + player.forward * 3f;
            Vector3 direction = (target - from).normalized;

            if (throwAudio != null && throwAudio.clip != null) throwAudio.PlayOneShot(throwAudio.clip);
            var emit = new ParticleSystem.EmitParams();
            for (int flick = 0; flick < flicksPerThrow; flick++)
            {
                if (flick > 0)
                {
                    yield return new WaitForSeconds(flickGap);
                    if (!seating.IsSeated) yield break;
                    from = seating.RightHand != null ? seating.RightHand.position : player.position + Vector3.up * 1.2f;
                }
                cashBurst.transform.position = from;
                EmitFlick(emit, from, direction);
            }
            if (dancer != null) dancer.React(player.position, reactSeconds);
        }

        private void EmitFlick(ParticleSystem.EmitParams emit, Vector3 from, Vector3 direction)
        {
            for (int i = 0; i < notesPerThrow; i++)
            {
                Quaternion jitter = Quaternion.Euler(UnityEngine.Random.Range(-spread, spread) * 0.6f, UnityEngine.Random.Range(-spread, spread), 0f);
                emit.position = from;
                emit.applyShapeToPosition = false;
                emit.velocity = Quaternion.LookRotation(direction) * jitter * Vector3.forward * (throwSpeed * UnityEngine.Random.Range(0.75f, 1.15f)) + Vector3.up * UnityEngine.Random.Range(1.8f, 3.2f); // up in an arc, so the notes flutter down onto the stage
                emit.rotation3D = new Vector3(UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(0f, 360f));
                cashBurst.Emit(emit, 1);
            }
        }

        private ClubCrowdMember FindNearestDancer(Vector3 from)
        {
            ClubCrowdMember best = null;
            float bestSqr = maxDancerDistance * maxDancerDistance;
            var dancers = ClubCrowdMember.ActiveDancers;
            for (int i = 0; i < dancers.Count; i++)
            {
                float sqr = (dancers[i].transform.position - from).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = dancers[i];
            }
            return best;
        }
    }
}
