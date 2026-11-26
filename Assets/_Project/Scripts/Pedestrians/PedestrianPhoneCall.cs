using System;
using UnityEngine;

namespace OpenworldCrime.Pedestrians
{
    /// <summary>
    /// A street pedestrian phoning the police (one per person, added the first time he is picked as a caller; pooled with him).
    /// Armed by <see cref="WitnessCallManager"/> when the player throws him out of his car; once back on his feet
    /// (<see cref="PedestrianDamageBridge"/> asks <see cref="TryStartAfterGettingUp"/>) he backs off a step, turns to the player,
    /// reaches into his pocket, holds a phone to his ear and talks for a few seconds, then puts it away and runs (or walks) off.
    /// When the talk ends <see cref="OnAnyCallCompleted"/> is raised (the manager reports it to the police).
    /// The call is dropped when he is hit, knocked down or killed, or the player gets far away.
    /// The "animation" is fake: the ped's own idle / talk clip plays and the right arm is bent onto the pocket / ear by a small
    /// two-bone IK after the Animator (works on the generic Bip01 ped rigs and on humanoids). The phone is a small box on the
    /// right hand, shown only during the call, made once per person.
    /// Runs Update / LateUpdate only during a call (disabled otherwise); no allocations per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public class PedestrianPhoneCall : MonoBehaviour
    {
        private enum Phase { Idle, StepBack, Reach, Raise, Talk, Lower, PutAway }

        /// <summary>A call got through (he talked to the end). Arg: the caller.</summary>
        public static event Action<PedestrianPhoneCall> OnAnyCallCompleted;

        /// <summary>True from getting up until he leaves.</summary>
        public bool IsCalling => phase != Phase.Idle;

        private PedestrianDamage damage;
        private PedestrianWalker walker;
        private WitnessCallSettings settings;
        private WitnessCallManager manager;

        private bool armed;
        private float armedUntil;
        private Phase phase;
        private float phaseTime, phaseLength;
        private float ikWeight;
        private Vector3 stepFrom, stepTo;

        private Transform upperArm, foreArm, hand, head, pelvis;
        private bool bonesSearched;
        private GameObject phone;

        private void Awake()
        {
            damage = GetComponent<PedestrianDamage>();
            walker = GetComponent<PedestrianWalker>();
            if (damage == null || walker == null)
            {
                Debug.LogError($"[{nameof(PedestrianPhoneCall)}] Needs PedestrianDamage and PedestrianWalker on '{name}'.", this);
                Destroy(this);
                return;
            }
            damage.OnKnockedDown += HandleKnockedDown;
            damage.OnDied += HandleKnockedDown;
            damage.OnHurt += HandleHurt;
            damage.OnStaggered += HandleHurt;
            damage.OnMeleeHit += HandleMeleeHit;
            enabled = false;
        }

        private void OnDestroy()
        {
            if (damage == null) return;
            damage.OnKnockedDown -= HandleKnockedDown;
            damage.OnDied -= HandleKnockedDown;
            damage.OnHurt -= HandleHurt;
            damage.OnStaggered -= HandleHurt;
            damage.OnMeleeHit -= HandleMeleeHit;
        }

        /// <summary>Will phone the police after getting up (within the settings' Armed Seconds).</summary>
        public void Arm(WitnessCallSettings callSettings, WitnessCallManager owner)
        {
            settings = callSettings;
            manager = owner;
            armed = settings != null && manager != null;
            armedUntil = Time.time + (settings != null ? settings.armedSeconds : 0f);
        }

        /// <summary>Called when he is back on his feet: starts the call (true) or lets him run as usual (false).</summary>
        public bool TryStartAfterGettingUp(Vector3 threat)
        {
            if (!armed) return false;
            armed = false;
            if (Time.time > armedUntil || damage.IsDead || walker.State != PedestrianWalker.Mode.Down) return false;
            if (!FindBones() || !manager.CanCall(transform.position)) return false;

            Transform player = WitnessCallManager.PlayerTransform;
            Vector3 away = transform.position - (player != null ? player.position : threat);
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : -transform.forward;
            stepFrom = transform.position;
            stepTo = stepFrom + away * settings.stepBackDistance;
            walker.PlayStand("idle1");
            ikWeight = 0f;
            Begin(Phase.StepBack, settings.stepBackSeconds);
            enabled = true;
            return true;
        }

        /// <summary>Back to the pool: forget everything.</summary>
        public void ResetForReuse()
        {
            armed = false;
            Stop();
        }

        private void Update()
        {
            if (phase == Phase.Idle) { enabled = false; return; }
            Transform player = WitnessCallManager.PlayerTransform;
            if (player == null || (player.position - transform.position).sqrMagnitude > settings.cancelDistance * settings.cancelDistance)
            {
                Leave(false);
                return;
            }

            phaseTime += Time.deltaTime;
            float k = Mathf.Clamp01(phaseTime / phaseLength);
            switch (phase)
            {
                case Phase.StepBack:
                    transform.position = Vector3.Lerp(stepFrom, stepTo, Mathf.SmoothStep(0f, 1f, k));
                    break;
                case Phase.Reach:
                    ikWeight = Mathf.SmoothStep(0f, 1f, k);
                    break;
                case Phase.PutAway:
                    ikWeight = 1f - Mathf.SmoothStep(0f, 1f, k);
                    break;
            }
            Face(player.position);
            if (k < 1f) return;

            switch (phase)
            {
                case Phase.StepBack: Begin(Phase.Reach, settings.reachSeconds); break;
                case Phase.Reach:
                    ShowPhone(true);
                    Begin(Phase.Raise, settings.raiseSeconds);
                    break;
                case Phase.Raise:
                    if (walker.CanTalk) walker.PlayStand("talk1");
                    Begin(Phase.Talk, UnityEngine.Random.Range(settings.talkSeconds.x, Mathf.Max(settings.talkSeconds.x, settings.talkSeconds.y)));
                    break;
                case Phase.Talk:
                    OnAnyCallCompleted?.Invoke(this);
                    walker.PlayStand("idle1");
                    Begin(Phase.Lower, settings.raiseSeconds);
                    break;
                case Phase.Lower:
                    ShowPhone(false);
                    Begin(Phase.PutAway, settings.reachSeconds);
                    break;
                case Phase.PutAway:
                    Leave(settings.fleeAfterCall);
                    break;
            }
        }

        // After the Animator: the right arm bent onto the pocket / ear target, blended by the IK weight.
        private void LateUpdate()
        {
            if (phase == Phase.Idle || upperArm == null || ikWeight <= 0f) return;
            Vector3 right = transform.right, up = transform.up, forward = transform.forward;
            Vector3 pocket = pelvis.position + right * settings.pocketOffset.x + up * settings.pocketOffset.y + forward * settings.pocketOffset.z;
            Vector3 ear = head.position + right * settings.earOffset.x + up * settings.earOffset.y + forward * settings.earOffset.z;
            Vector3 pocketHint = right * settings.pocketElbowHint.x + up * settings.pocketElbowHint.y + forward * settings.pocketElbowHint.z;
            Vector3 earHint = right * settings.earElbowHint.x + up * settings.earElbowHint.y + forward * settings.earElbowHint.z;

            float toEar;
            switch (phase)
            {
                case Phase.Raise: toEar = Mathf.SmoothStep(0f, 1f, phaseTime / phaseLength); break;
                case Phase.Talk: toEar = 1f; break;
                case Phase.Lower: toEar = 1f - Mathf.SmoothStep(0f, 1f, phaseTime / phaseLength); break;
                default: toEar = 0f; break;
            }
            Vector3 target = Vector3.Lerp(pocket, ear, toEar);
            Vector3 hint = Vector3.Lerp(pocketHint, earHint, toEar);
            SolveArm(target, upperArm.position + hint, ikWeight);

            if (phone != null && phone.activeSelf)
            {
                Vector3 along = hand.position - foreArm.position;
                Vector3 fingers = along.sqrMagnitude > 1e-6f ? along.normalized : up;
                // Screen to the cheek, length along the hand; at the ear it sits between the palm and the ear.
                phone.transform.SetPositionAndRotation(Vector3.Lerp(hand.position + fingers * 0.05f, ear, toEar * 0.2f),
                    Quaternion.LookRotation(right, Vector3.Lerp(fingers, up, toEar * 0.6f)));
            }
        }

        // Two-bone IK: elbow placed by the law of cosines toward the hint, then the forearm aimed at the target.
        private void SolveArm(Vector3 target, Vector3 elbowHint, float weight)
        {
            Quaternion upperAnimated = upperArm.rotation, foreAnimated = foreArm.rotation;
            Vector3 a = upperArm.position, b = foreArm.position, c = hand.position;
            float upperLength = (b - a).magnitude, foreLength = (c - b).magnitude;
            Vector3 toTarget = target - a;
            float distance = Mathf.Clamp(toTarget.magnitude, 0.01f, (upperLength + foreLength) * 0.999f);
            Vector3 direction = toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : transform.forward;
            Vector3 bend = Vector3.ProjectOnPlane(elbowHint - a, direction);
            bend = bend.sqrMagnitude > 1e-6f ? bend.normalized : -transform.up;
            float cos = Mathf.Clamp((upperLength * upperLength + distance * distance - foreLength * foreLength) / (2f * upperLength * distance), -1f, 1f);
            Vector3 elbow = a + direction * (upperLength * cos) + bend * (upperLength * Mathf.Sqrt(1f - cos * cos));

            upperArm.rotation = Quaternion.FromToRotation(b - a, elbow - a) * upperAnimated;
            Vector3 newB = foreArm.position, newC = hand.position;
            foreArm.rotation = Quaternion.FromToRotation(newC - newB, a + direction * distance - newB) * foreArm.rotation;

            if (weight >= 1f) return;
            Quaternion upperSolved = upperArm.rotation, foreSolved = foreArm.rotation;
            upperArm.rotation = Quaternion.Slerp(upperAnimated, upperSolved, weight);
            foreArm.rotation = Quaternion.Slerp(foreAnimated, foreSolved, weight);
        }

        private void Face(Vector3 point)
        {
            Vector3 to = point - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), settings.turnSpeed * Time.deltaTime);
        }

        private void Begin(Phase next, float seconds)
        {
            phase = next;
            phaseTime = 0f;
            phaseLength = Mathf.Max(0.05f, seconds);
        }

        // Call over (or dropped while he is unhurt): off he goes, running or walking back into the crowd.
        private void Leave(bool flee)
        {
            Stop();
            if (damage.IsDead || walker.State != PedestrianWalker.Mode.Down || walker.Owner == null) return;
            Transform player = WitnessCallManager.PlayerTransform;
            walker.Owner.RecoverWalker(walker, player != null ? player.position : transform.position - transform.forward);
            if (!flee) walker.ForceCalm();
        }

        private void Stop()
        {
            phase = Phase.Idle;
            ikWeight = 0f;
            ShowPhone(false);
            enabled = false;
        }

        // Knocked down or killed: the call ends; the ragdoll / bridge take over.
        private void HandleKnockedDown(Vector3 threat)
        {
            armed = false;
            if (IsCalling) Stop();
        }

        // Hurt without going down (a bump, a shove): the call is dropped and he runs.
        private void HandleHurt(Vector3 threat)
        {
            armed = false;
            if (!IsCalling) return;
            Leave(true);
        }

        // Punched: the brawler drives his reaction from here.
        private void HandleMeleeHit(Vector3 direction, int hitNumber, bool heavy)
        {
            armed = false;
            if (IsCalling) Stop();
        }

        private void ShowPhone(bool show)
        {
            if (show && phone == null) phone = CreatePhone();
            if (phone != null && phone.activeSelf != show) phone.SetActive(show);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => OnAnyCallCompleted = null;

        private GameObject CreatePhone()
        {
            if (hand == null || settings == null || settings.phoneMesh == null) return null;
            var go = new GameObject("Witness Phone");
            go.transform.SetParent(hand, false);
            go.transform.localScale = settings.phoneSize;
            go.AddComponent<MeshFilter>().sharedMesh = settings.phoneMesh;
            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = settings.phoneMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            go.layer = hand.gameObject.layer;
            go.SetActive(false);
            return go;
        }

        // Generic Bip01 ped rigs by bone name, humanoids through the Animator. Searched once per person.
        private bool FindBones()
        {
            if (bonesSearched) return upperArm != null;
            bonesSearched = true;
            Animator animator = GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                upperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                foreArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                head = animator.GetBoneTransform(HumanBodyBones.Head);
                pelvis = animator.GetBoneTransform(HumanBodyBones.Hips);
            }
            else
            {
                foreach (Transform t in GetComponentsInChildren<Transform>(true))
                {
                    switch (t.name)
                    {
                        case "Bip01 R UpperArm": upperArm = t; break;
                        case "Bip01 R Forearm": foreArm = t; break;
                        case "Bip01 R Hand": hand = t; break;
                        case "Bip01 Head": head = t; break;
                        case "Bip01 Pelvis": pelvis = t; break;
                    }
                }
            }
            if (upperArm == null || foreArm == null || hand == null || head == null || pelvis == null)
            {
                upperArm = null;
                Debug.LogWarning($"[{nameof(PedestrianPhoneCall)}] '{name}' has no right arm / head / pelvis bones found: no phone call.", this);
                return false;
            }
            return true;
        }
    }
}
