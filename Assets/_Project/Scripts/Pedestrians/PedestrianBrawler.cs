using System.Collections.Generic;
using Invector;
using Invector.vCharacterController;
using Invector.vMelee;
using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.Pedestrians
{
    using PlayerManager = OpenworldCrime.Player.PlayerManager; // SharedAssets has a global PlayerManager

    /// <summary>
    /// What a street person does when punched (added at runtime by <see cref="PedestrianDamageBridge"/>):
    /// - Hit reaction: taken out of the crowd flow, shoved back, faces the attacker and plays a flinch (or a big stagger for a
    ///   second / heavy hit), with a hit sound. Further punches during the reaction land (the combo knocks him down).
    /// - Then either runs away (as before) or, for a share of the men, fights back: raises his fists, closes in, throws
    ///   jabs / crosses / hooks that hurt the player through Invector's damage (so the player plays his hit reactions),
    ///   sometimes blocks or steps back, and gives up and runs when badly hurt, when the player draws a gun or gets away.
    /// Only reacting / fighting people run per-frame logic (Update is off otherwise); at most <see cref="MaxFighters"/> fight
    /// at once. Animations are states of our people controllers (PedestrianBrawlAnimBuilder); a person whose controller does
    /// not have them (police) keeps the old stagger + panic.
    /// </summary>
    [DisallowMultipleComponent]
    public class PedestrianBrawler : MonoBehaviour
    {
        public static int MaxFighters = 3;
        private static readonly List<PedestrianBrawler> fighters = new List<PedestrianBrawler>();

        private static readonly int HitFront = Animator.StringToHash("hit_front");
        private static readonly int HitLeft = Animator.StringToHash("hit_left");
        private static readonly int HitRight = Animator.StringToHash("hit_right");
        private static readonly int HitBack = Animator.StringToHash("hit_back");
        private static readonly int HitBig = Animator.StringToHash("hit_big");
        private static readonly int Guard = Animator.StringToHash("brawl_guard");
        private static readonly int Block = Animator.StringToHash("brawl_block");
        private static readonly int Walk = Animator.StringToHash("walk");
        private static readonly int Run = Animator.StringToHash("run");
        private static readonly int[] Punches = { Animator.StringToHash("brawl_jab"), Animator.StringToHash("brawl_cross"), Animator.StringToHash("brawl_hook") };
        private static readonly float[] PunchSeconds = { 0.53f, 0.53f, 0.79f };   // clip length / state speed
        private static readonly float[] PunchImpact = { 0.42f, 0.45f, 0.5f };    // normalized time the fist lands
        private static readonly float[] PunchDamage = { 6f, 7f, 12f };

        private enum Phase { Idle, Reacting, Fighting, Attacking, Blocking, Dodging }

        private PedestrianDamage damage;
        private PedestrianWalker walker;
        private Animator animator;
        private AudioSource audioSource;
        private PedestrianDamageBridge.BrawlSettings settings;

        private Phase phase;
        private float phaseEnd;
        private int punch;
        private bool punchLanded;
        private float nextAttack;
        private Vector3 shove;
        private float shoveLeft;
        private Vector3 threat;
        private bool decided;
        private bool willFight;
        private float farSince;
        private bool playerWasAttacking;
        private readonly RaycastHit[] groundHits = new RaycastHit[6];

        public bool IsFighting => phase == Phase.Fighting || phase == Phase.Attacking || phase == Phase.Blocking || phase == Phase.Dodging;
        public bool IsBusy => phase != Phase.Idle;

        public void Init(PedestrianDamage owner, PedestrianWalker personWalker, PedestrianDamageBridge.BrawlSettings brawlSettings)
        {
            damage = owner;
            walker = personWalker;
            settings = brawlSettings;
            animator = GetComponent<Animator>();
            damage.BlockCheck = IsBlockingAgainst;
            enabled = false;
        }

        /// <summary>True when the person's controller has the hit / fight states.</summary>
        public bool HasAnimations => animator != null && animator.HasState(0, HitFront);
        public bool CanFight => animator != null && animator.HasState(0, Guard);

        private void OnDisable()
        {
            fighters.Remove(this);
        }

        /// <summary>Back to the pool: forget everything.</summary>
        public void ResetForReuse()
        {
            fighters.Remove(this);
            phase = Phase.Idle;
            decided = willFight = false;
            enabled = false;
        }

        // ------------------------------------------------------------------------------------------- hit reaction

        /// <summary>A punch that did not knock the person down (see PedestrianDamage.OnMeleeHit).</summary>
        public void TakeHit(Vector3 direction, int hitNumber, bool heavy, Vector3 attacker)
        {
            threat = attacker;
            bool wasSeated = damage.IsSeated;
            if (walker.State != PedestrianWalker.Mode.Down) walker.EnterDown(); // out of the crowd flow; we drive him now
            if (IsFighting) fighters.Remove(this);

            Vector3 toAttacker = attacker - transform.position;
            toAttacker.y = 0f;
            bool big = heavy || hitNumber >= 2;
            int state = big ? HitBig : HitStateFor(toAttacker);
            if (big && toAttacker.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(toAttacker);
            animator.speed = 1f;
            animator.CrossFadeInFixedTime(state, 0.05f, 0, 0f);
            shove = direction * (big ? settings.bigShove : settings.smallShove);
            shoveLeft = 1f;
            PlaySound(settings.hitSounds, 1f);

            if (!decided)
            {
                decided = true;
                willFight = CanFight && !wasSeated && Random.value < settings.fightBackChance;
            }
            phase = Phase.Reacting;
            phaseEnd = Time.time + (big ? settings.bigReactSeconds : settings.smallReactSeconds);
            enabled = true;
            // People around run off at the first blow.
            if (hitNumber == 1 && walker.Owner != null) walker.Owner.TriggerPanic(transform.position, settings.bystanderPanicRadius);
        }

        /// <summary>A punch taken on the guard.</summary>
        public void Blocked(Vector3 attacker)
        {
            PlaySound(settings.blockSounds, 0.8f);
            shove = (transform.position - attacker).normalized * 0.12f;
            shove.y = 0f;
            shoveLeft = 1f;
        }

        /// <summary>Knocked down (ragdoll) while reacting / fighting: stop driving him.</summary>
        public void KnockedDown()
        {
            fighters.Remove(this);
            phase = Phase.Idle;
            enabled = false;
        }

        /// <summary>Back on his feet after a knock-down: fight on if he is a fighter and fit, otherwise false (run away).</summary>
        public bool TryResumeAfterGettingUp()
        {
            if (!willFight || damage.IsDead || damage.HealthFraction < settings.giveUpHealth || !StartFighting()) return false;
            return true;
        }

        private int HitStateFor(Vector3 toAttacker)
        {
            if (toAttacker.sqrMagnitude < 0.01f) return HitFront;
            float angle = Vector3.SignedAngle(transform.forward, toAttacker, Vector3.up);
            if (Mathf.Abs(angle) < 50f) return HitFront;
            if (Mathf.Abs(angle) > 130f) return HitBack;
            return angle > 0f ? HitRight : HitLeft;
        }

        // ------------------------------------------------------------------------------------------- per frame

        // Per-frame only while reacting or fighting (enabled = false otherwise).
        private void Update()
        {
            if (damage.IsDead || damage.IsDown) { KnockedDown(); return; }
            ApplyShove();
            switch (phase)
            {
                case Phase.Reacting:
                    if (Time.time < phaseEnd) return;
                    if (willFight && StartFighting()) return;
                    RunAway();
                    return;
                case Phase.Fighting:
                case Phase.Attacking:
                case Phase.Blocking:
                case Phase.Dodging:
                    TickFight();
                    return;
                default:
                    enabled = false;
                    return;
            }
        }

        private void ApplyShove()
        {
            if (shoveLeft <= 0f) return;
            float step = Mathf.Min(shoveLeft, Time.deltaTime / settings.shoveSeconds);
            shoveLeft -= step;
            MoveOnGround(shove * step);
        }

        private bool StartFighting()
        {
            fighters.RemoveAll(f => f == null || !f.isActiveAndEnabled || !f.IsFighting);
            if (!fighters.Contains(this) && fighters.Count >= MaxFighters) return false;
            if (!TryGetPlayer(out _, out _)) return false;
            if (!fighters.Contains(this)) fighters.Add(this);
            phase = Phase.Fighting;
            nextAttack = Time.time + Random.Range(0.4f, 0.9f);
            farSince = -1f;
            animator.CrossFadeInFixedTime(Guard, 0.15f, 0, 0f);
            enabled = true;
            return true;
        }

        private void TickFight()
        {
            if (!TryGetPlayer(out Transform player, out PlayerManager manager) || manager.IsInVehicle || manager.CurrentState == PlayerState.Dead)
            {
                RunAway();
                return;
            }
            // Guns out, badly hurt: run.
            var weapons = player.GetComponent<PlayerWeaponController>();
            if ((weapons != null && weapons.HasWeapon) || damage.HealthFraction < settings.giveUpHealth)
            {
                RunAway();
                return;
            }

            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;
            if (distance > settings.giveUpDistance)
            {
                if (farSince < 0f) farSince = Time.time;
                else if (Time.time - farSince > 3f) { RunAway(); return; }
            }
            else farSince = -1f;
            if (distance > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toPlayer), 1f - Mathf.Exp(-10f * Time.deltaTime));

            switch (phase)
            {
                case Phase.Attacking:
                    TickPunch(player, distance);
                    return;
                case Phase.Blocking:
                case Phase.Dodging:
                    if (phase == Phase.Dodging) MoveOnGround(-toPlayer.normalized * settings.dodgeSpeed * Time.deltaTime);
                    if (Time.time >= phaseEnd) { phase = Phase.Fighting; animator.CrossFadeInFixedTime(Guard, 0.15f, 0, 0f); }
                    return;
            }

            // React to the player's swing: block or step back now and then.
            bool playerAttacking = IsPlayerAttacking(player);
            if (playerAttacking && !playerWasAttacking && distance < 2.2f)
            {
                float roll = Random.value;
                if (roll < settings.blockChance) { BeginBlock(); playerWasAttacking = true; return; }
                if (roll < settings.blockChance + settings.dodgeChance) { BeginDodge(); playerWasAttacking = true; return; }
            }
            playerWasAttacking = playerAttacking;

            if (distance > settings.punchRange)
            {
                bool far = distance > 4f;
                SetLocomotion(far ? Run : Walk);
                MoveOnGround(toPlayer / distance * (far ? settings.runSpeed : settings.walkSpeed) * Time.deltaTime);
                return;
            }
            SetLocomotion(Guard);
            if (Time.time >= nextAttack) BeginPunch();
        }

        private int locomotionState;

        private void SetLocomotion(int state)
        {
            if (locomotionState == state && phase == Phase.Fighting) return;
            locomotionState = state;
            animator.CrossFadeInFixedTime(state, 0.15f, 0, 0f);
        }

        private void BeginPunch()
        {
            punch = Random.value < 0.25f ? 2 : Random.Range(0, 2);
            punchLanded = false;
            phase = Phase.Attacking;
            phaseEnd = Time.time + PunchSeconds[punch];
            locomotionState = 0;
            animator.CrossFadeInFixedTime(Punches[punch], 0.08f, 0, 0f);
        }

        private void TickPunch(Transform player, float distance)
        {
            float t = 1f - (phaseEnd - Time.time) / PunchSeconds[punch];
            if (!punchLanded && t >= PunchImpact[punch])
            {
                punchLanded = true;
                Vector3 toPlayer = (player.position - transform.position).normalized;
                if (distance <= settings.punchRange + 0.35f && Vector3.Dot(transform.forward, toPlayer) > 0.6f) HitPlayer(player);
                else PlaySound(settings.swingSounds, 0.6f);
            }
            if (Time.time < phaseEnd) return;
            phase = Phase.Fighting;
            nextAttack = Time.time + Random.Range(settings.attackIntervalMin, settings.attackIntervalMax);
            animator.CrossFadeInFixedTime(Guard, 0.12f, 0, 0f);
        }

        private void HitPlayer(Transform player)
        {
            var health = player.GetComponent<vIHealthController>();
            if (health == null || health.isDead) return;
            var hit = new vDamage((int)PunchDamage[punch])
            {
                sender = transform,
                receiver = player,
                hitPosition = player.position + Vector3.up * 1.4f,
                hitReaction = true,
                reaction_id = 0,
                damageType = "PedPunch",
            };
            hit.damageValue = PunchDamage[punch] * settings.damageScale;
            health.TakeDamage(hit);
            PlaySound(settings.hitSounds, 1f);
        }

        private void BeginBlock()
        {
            phase = Phase.Blocking;
            phaseEnd = Time.time + settings.blockSeconds;
            locomotionState = 0;
            animator.CrossFadeInFixedTime(Block, 0.06f, 0, 0.25f);
        }

        private void BeginDodge()
        {
            phase = Phase.Dodging;
            phaseEnd = Time.time + 0.3f;
            locomotionState = 0;
            animator.CrossFadeInFixedTime(HitBack, 0.06f, 0, 0.1f);
        }

        private bool IsBlockingAgainst(Transform attacker)
        {
            if (phase != Phase.Blocking || attacker == null) return false;
            Vector3 to = attacker.position - transform.position;
            to.y = 0f;
            return Vector3.Dot(transform.forward, to.normalized) > 0.3f;
        }

        private static bool IsPlayerAttacking(Transform player)
        {
            var input = player.GetComponent<vMeleeCombatInput>();
            return input != null && input.isAttacking;
        }

        private void RunAway()
        {
            fighters.Remove(this);
            phase = Phase.Idle;
            enabled = false;
            animator.speed = 1f;
            if (walker.Owner != null) walker.Owner.RecoverWalker(walker, threat);
        }

        // ------------------------------------------------------------------------------------------- helpers

        private static bool TryGetPlayer(out Transform player, out PlayerManager manager)
        {
            PlayableCharacter current = PlayableCharacter.Current;
            player = current != null ? current.transform : null;
            manager = current != null ? current.GetComponent<PlayerManager>() : null;
            return player != null && manager != null;
        }

        // Moves on the ground (the person has no physics body): keeps the feet on whatever is below.
        private void MoveOnGround(Vector3 delta)
        {
            Vector3 position = transform.position + delta;
            int count = Physics.RaycastNonAlloc(position + Vector3.up * 1.2f, Vector3.down, groundHits, 3f, settings.groundLayers, QueryTriggerInteraction.Ignore);
            float best = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                Transform hit = groundHits[i].transform;
                if (hit == null || hit.IsChildOf(transform) || hit.GetComponentInParent<PlayerManager>() != null) continue;
                if (groundHits[i].point.y > best && groundHits[i].point.y < position.y + 0.6f) best = groundHits[i].point.y;
            }
            if (best > float.MinValue) position.y = best;
            transform.position = position;
        }

        private void PlaySound(AudioClip[] clips, float volume)
        {
            if (clips == null || clips.Length == 0) return;
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 1f;
                audioSource.minDistance = 2f;
                audioSource.maxDistance = 30f;
                audioSource.rolloffMode = AudioRolloffMode.Linear;
            }
            AudioClip clip = clips[Random.Range(0, clips.Length)];
            if (clip != null) audioSource.PlayOneShot(clip, volume);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => fighters.Clear();
    }
}
