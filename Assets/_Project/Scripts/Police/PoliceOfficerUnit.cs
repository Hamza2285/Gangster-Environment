using System.Collections.Generic;
using OpenworldCrime.Pedestrians;
using OpenworldCrime.UI;
using OpenworldCrime.Vehicles;
using UnityEngine;
using UnityEngine.AI;

namespace OpenworldCrime.Police
{
    /// <summary>
    /// Brain of one police officer on foot (pooled by <see cref="PoliceDispatcher"/>; per-instance, never a Singleton).
    /// Patrolling is done by the street officers of the pedestrian system; an officer who witnesses a crime is replaced in place
    /// by one of these, and reinforcements spawn out of sight. State machine, driven by <see cref="WantedManager"/>:
    /// - Pursue (non-lethal): runs to the player on the NavMesh, draws his pistol within Draw Weapon Distance and stands at Arrest
    ///   Stand Distance pointing it (his presence fills the arrest bar). Never shoots.
    /// - Engage (lethal): closes to Fire Range, stops at Fire Stand Distance and shoots (<see cref="PoliceGunfire"/>) whenever he has a
    ///   clear line, after a short reaction time.
    /// - Search: walks to points around the last known position.
    /// - Return: walks away and is recalled once out of sight.
    /// Health, hits, knock-downs and death are the pedestrian damage setup (<see cref="PedestrianDamage"/> with its ragdoll):
    /// a knocked-down officer gets up and carries on; a dead one is cleared out of sight after Corpse Seconds.
    /// Movement is Unity's NavMeshAgent on the pedestrians' panic NavMesh (kept loaded by the dispatcher while officers are out).
    /// </summary>
    [DisallowMultipleComponent]
    public class PoliceOfficerUnit : MonoBehaviour
    {
        public enum UnitState { Inactive, Pursue, Engage, Search, Return, Down, Dead }

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int ShootStateId = Animator.StringToHash("Shoot");

        [Header("References")]
        [SerializeField] private NavMeshAgent agent;
        [SerializeField] private Animator animator;
        [SerializeField] private PedestrianDamage damage;
        [SerializeField] private PoliceSidearm sidearm;
        [Tooltip("Pistol model in the right hand, shown while the gun is drawn.")]
        [SerializeField] private GameObject weaponModel;
        [SerializeField] private MapBlip mapBlip;

        [Header("Animation")]
        [Tooltip("Animator layer with the upper-body pistol aim and shot.")]
        [SerializeField, Min(0)] private int armsLayer = 1;
        [SerializeField, Min(0.1f)] private float armsBlendSpeed = 6f;
        [SerializeField, Min(0.1f)] private float turnSpeed = 10f;
        [Tooltip("While the gun is drawn, IK holds the right hand this far (m) out from the shoulder towards the target, barrel on " +
                 "the target, with the left hand supporting it (the arms layer needs IK Pass).")]
        [SerializeField, Min(0.1f)] private float aimReach = 0.55f;
        [SerializeField] private Vector3 supportHandOffset = new Vector3(-0.05f, -0.06f, -0.04f);
        [SerializeField, Range(0f, 1f)] private float supportHandWeight = 0.85f;

        [Header("Search")]
        [Tooltip("Search points are picked within this share of the search radius around the last known position.")]
        [SerializeField, Range(0.1f, 1f)] private float searchSpread = 0.5f;
        [SerializeField, Min(1f)] private float searchPointSeconds = 8f;

        [Header("Recall")]
        [Tooltip("A returning officer is removed once out of sight and at least this far (m) from the camera.")]
        [SerializeField, Min(5f)] private float recallKeepDistance = 25f;

        private static readonly List<PoliceOfficerUnit> active = new List<PoliceOfficerUnit>();
        /// <summary>Officers on the street right now (alive, standing or knocked down).</summary>
        public static IReadOnlyList<PoliceOfficerUnit> Active => active;

        public UnitState State { get; private set; } = UnitState.Inactive;
        public bool IsAlive => State != UnitState.Inactive && State != UnitState.Dead;
        /// <summary>On his feet (can see, arrest and shoot).</summary>
        public bool IsStanding => IsAlive && State != UnitState.Down;
        public Vector3 EyePosition => transform.position + Vector3.up * (settings != null ? settings.eyeHeight : 1.6f);

        private PoliceDispatcher owner;
        private PoliceSettings settings;
        private WantedManager wanted;
        private float nextRepath;
        private float nextShot;
        private float nextLineCheck;
        private bool hasLineOfFire;
        private float engagedSince;
        private float armsWeight;
        private float armsTarget;
        private float stateSince;
        private float deadSince;
        private float nextCorpseCheck;
        private Vector3 searchPoint;
        private float searchPointUntil;
        private Transform rightShoulder;
        private Transform rightHand;
        private Quaternion weaponLocalRotation = Quaternion.identity;
        // Humanoid IK goals are in the avatar's normalised hand space, not the bone's: measured once at full weight.
        private Quaternion goalToBone = Quaternion.identity;
        private Quaternion lastGoal;
        private bool ikCalibrated;
        private int defaultLayer;
        private string defaultTag;
        private bool valid;

        private void Awake()
        {
            valid = agent != null && animator != null && damage != null && sidearm != null;
            if (!valid)
            {
                Debug.LogError($"[{nameof(PoliceOfficerUnit)}] Missing references on '{name}' (NavMeshAgent, Animator, PedestrianDamage, PoliceSidearm).", this);
                enabled = false;
                return;
            }
            defaultLayer = gameObject.layer;
            defaultTag = gameObject.tag;
            agent.enabled = false;
            rightShoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (weaponModel != null) weaponLocalRotation = weaponModel.transform.localRotation;
            // Subscribed for the whole lifetime: the damage component raises nothing while the officer is pooled.
            damage.OnKnockedDown += HandleKnockedDown;
            damage.OnRecovered += HandleRecovered;
            damage.OnDied += HandleDied;
        }

        private void OnDestroy()
        {
            active.Remove(this);
            if (damage == null) return;
            damage.OnKnockedDown -= HandleKnockedDown;
            damage.OnRecovered -= HandleRecovered;
            damage.OnDied -= HandleDied;
        }

        private void OnDisable()
        {
            active.Remove(this);
        }

        /// <summary>
        /// Puts the officer on the street at the given pose (snapped onto the NavMesh). False when there is no NavMesh nearby; the
        /// officer then stays pooled. <paramref name="sensorLayer"/> is the layer traffic brakes for.
        /// </summary>
        public bool Activate(PoliceDispatcher dispatcher, Vector3 position, Quaternion rotation, int sensorLayer)
        {
            if (!valid) return false;
            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, 6f, NavMesh.AllAreas)) return false;
            owner = dispatcher;
            settings = dispatcher.Settings;
            wanted = WantedManager.Instance;

            transform.SetPositionAndRotation(hit.position, rotation);
            gameObject.SetActive(true);
            damage.ResetForReuse();
            damage.SetHitboxesEnabled(true);
            // Traffic brakes for anything on its player layer with the Player tag (the street pedestrians do the same when they run).
            gameObject.layer = sensorLayer;
            gameObject.tag = "Player";

            agent.enabled = true;
            agent.Warp(hit.position);
            agent.speed = settings.officerRunSpeed;
            agent.acceleration = 14f;
            agent.angularSpeed = 600f;
            agent.autoBraking = true;
            agent.stoppingDistance = 0.3f;
            agent.isStopped = false;

            armsWeight = 0f;
            animator.SetLayerWeight(armsLayer, 0f);
            if (weaponModel != null) weaponModel.SetActive(false);
            if (mapBlip != null) mapBlip.enabled = true;
            nextRepath = 0f;
            nextShot = 0f;
            nextLineCheck = 0f;
            hasLineOfFire = false;
            searchPointUntil = 0f;
            SetState(UnitState.Pursue);
            if (!active.Contains(this)) active.Add(this);
            return true;
        }

        /// <summary>Back to the pool (called by the dispatcher).</summary>
        public void Deactivate()
        {
            active.Remove(this);
            State = UnitState.Inactive;
            if (agent.enabled) agent.enabled = false;
            gameObject.layer = defaultLayer;
            gameObject.tag = defaultTag;
            if (mapBlip != null) mapBlip.enabled = false;
            gameObject.SetActive(false);
        }

        /// <summary>True when this officer can see the point (within Officer Sight Radius, nothing solid in between).</summary>
        public bool CanSee(Vector3 point, float radius)
        {
            if (!IsStanding) return false;
            Vector3 eye = EyePosition;
            Vector3 target = point + Vector3.up * 1.2f;
            float sqr = (target - eye).sqrMagnitude;
            if (sqr <= settings.awarenessRadius * settings.awarenessRadius) return true;
            if (sqr > radius * radius) return false;
            return !Physics.Linecast(eye, target, settings.sightBlockers, QueryTriggerInteraction.Ignore);
        }

        private void Update()
        {
            switch (State)
            {
                case UnitState.Inactive:
                case UnitState.Down:
                    return;
                case UnitState.Dead:
                    UpdateCorpse();
                    return;
            }
            if (wanted == null)
            {
                owner.Release(this);
                return;
            }
            Think();
            Animate(Time.deltaTime);
        }

        private void Think()
        {
            UnitState next;
            if (wanted.Stars == 0) next = UnitState.Return;
            else if (wanted.IsSearching) next = UnitState.Search;
            else next = wanted.IsLethal ? UnitState.Engage : UnitState.Pursue;
            if (next != State) SetState(next);

            switch (State)
            {
                case UnitState.Pursue: Pursue(); break;
                case UnitState.Engage: Engage(); break;
                case UnitState.Search: Search(); break;
                case UnitState.Return: Return(); break;
            }
        }

        private void Pursue()
        {
            Vector3 target = wanted.PlayerTarget.position;
            float distance = FlatDistance(target);
            float stand = wanted.PlayerInVehicle ? settings.bustRadiusInVehicle * 0.75f : settings.arrestStandDistance;
            bool drawn = distance <= settings.drawWeaponDistance;
            SetArms(drawn);
            agent.speed = drawn ? settings.officerWalkSpeed * 1.4f : settings.officerRunSpeed;
            if (distance <= stand)
            {
                Hold(target);
                return;
            }
            MoveTo(target, !drawn);
            if (drawn) Face(target);
        }

        private void Engage()
        {
            Transform targetTransform = wanted.PlayerTarget;
            Vector3 target = targetTransform.position;
            float distance = FlatDistance(target);
            float now = Time.time;
            if (now >= nextLineCheck)
            {
                nextLineCheck = now + 0.25f;
                bool line = distance <= settings.fireRange && PoliceGunfire.HasLineOfFire(EyePosition, wanted, settings, out _);
                if (line && !hasLineOfFire) engagedSince = now;
                hasLineOfFire = line;
            }
            // Gun up only when there is a shot; running to get one, the arms swing freely (a sprint with arms locked out looks wrong).
            SetArms(hasLineOfFire);

            if (!hasLineOfFire)
            {
                agent.speed = settings.officerRunSpeed;
                MoveTo(target, true);
                return;
            }
            Face(target);
            if (distance <= settings.fireStandDistance) Hold(target);
            else
            {
                agent.speed = settings.officerWalkSpeed;
                MoveTo(target, false);
            }
            if (now >= nextShot && now - engagedSince >= settings.fireReactionSeconds && armsWeight > 0.8f)
            {
                nextShot = now + Random.Range(settings.fireInterval.x, settings.fireInterval.y);
                PoliceGunfire.ShootPlayer(sidearm, transform, wanted, settings);
                animator.Play(ShootStateId, armsLayer, 0f);
            }
        }

        private void Search()
        {
            SetArms(false);
            agent.speed = settings.officerWalkSpeed * 1.5f;
            float now = Time.time;
            if (now >= searchPointUntil || (!agent.pathPending && agent.remainingDistance < 1.5f))
            {
                searchPointUntil = now + searchPointSeconds;
                Vector2 offset = Random.insideUnitCircle * settings.SearchRadiusFor(wanted.Stars) * searchSpread;
                searchPoint = wanted.LastKnownPosition + new Vector3(offset.x, 0f, offset.y);
                if (NavMesh.SamplePosition(searchPoint, out NavMeshHit hit, 8f, NavMesh.AllAreas)) searchPoint = hit.position;
                nextRepath = 0f;
            }
            MoveTo(searchPoint, true);
        }

        private void Return()
        {
            SetArms(false);
            agent.speed = settings.officerWalkSpeed;
            if (Time.time >= nextRepath)
            {
                Vector3 away = transform.position - wanted.PlayerPosition;
                away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : transform.forward;
                Vector3 point = transform.position + away * 30f;
                if (NavMesh.SamplePosition(point, out NavMeshHit hit, 10f, NavMesh.AllAreas)) point = hit.position;
                MoveTo(point, true);
            }
            if (Time.time - stateSince > 2f && owner.CanVanish(transform.position, recallKeepDistance)) owner.Release(this);
        }

        private void MoveTo(Vector3 point, bool faceAlongPath)
        {
            agent.updateRotation = faceAlongPath;
            if (agent.isStopped) agent.isStopped = false;
            if (Time.time < nextRepath || !agent.isOnNavMesh) return;
            nextRepath = Time.time + settings.officerRepathInterval;
            agent.SetDestination(point);
        }

        private void Hold(Vector3 faceTarget)
        {
            if (agent.isOnNavMesh && !agent.isStopped)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }
            Face(faceTarget);
        }

        private void Face(Vector3 target)
        {
            agent.updateRotation = false;
            Vector3 direction = target - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) return;
            Quaternion look = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
        }

        private void SetArms(bool drawn) => armsTarget = drawn ? 1f : 0f;

        private void Animate(float deltaTime)
        {
            animator.SetFloat(SpeedId, agent.enabled ? agent.velocity.magnitude : 0f);
            if (Mathf.Approximately(armsWeight, armsTarget)) return;
            armsWeight = Mathf.MoveTowards(armsWeight, armsTarget, armsBlendSpeed * deltaTime);
            animator.SetLayerWeight(armsLayer, armsWeight);
            if (weaponModel != null) weaponModel.SetActive(armsWeight > 0.3f);
        }

        // Points the drawn pistol at the target: the right hand goes out from the shoulder along the line of fire, rotated so the
        // pistol's barrel (+Z of the weapon model) points at the target, and the left hand cups the grip.
        private void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != armsLayer || armsWeight <= 0.01f || wanted == null || rightShoulder == null || !IsStanding) return;
            Vector3 shoulder = rightShoulder.position;
            Vector3 target = wanted.PlayerTarget.position + Vector3.up * (wanted.PlayerInVehicle ? 1f : 1.3f);
            Vector3 direction = target - shoulder;
            // Point blank the line would bend the wrists up or down: keep the arms near shoulder height.
            direction.y = Mathf.Clamp(direction.y, -0.35f * new Vector2(direction.x, direction.z).magnitude, 0.35f * new Vector2(direction.x, direction.z).magnitude);
            if (direction.sqrMagnitude < 0.01f) return;
            direction.Normalize();
            Vector3 hand = shoulder + direction * aimReach;
            Quaternion gun = Quaternion.LookRotation(direction, Vector3.up);
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, armsWeight);
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, armsWeight);
            animator.SetIKPosition(AvatarIKGoal.RightHand, hand);
            Quaternion handBone = gun * Quaternion.Inverse(weaponLocalRotation);
            lastGoal = ikCalibrated ? handBone * Quaternion.Inverse(goalToBone) : handBone;
            animator.SetIKRotation(AvatarIKGoal.RightHand, lastGoal);
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, armsWeight * supportHandWeight);
            animator.SetIKPosition(AvatarIKGoal.LeftHand, hand + gun * supportHandOffset);
        }

        // Runs only until the IK offset of this rig is known (first frame with the gun fully drawn).
        private void LateUpdate()
        {
            if (ikCalibrated || armsWeight < 0.999f || rightHand == null || !IsStanding) return;
            goalToBone = Quaternion.Inverse(lastGoal) * rightHand.rotation;
            ikCalibrated = true;
        }

        private void SetState(UnitState state)
        {
            State = state;
            // Returning officers are no longer police business on the map.
            if (mapBlip != null) mapBlip.enabled = state != UnitState.Return;
            stateSince = Time.time;
            nextRepath = 0f;
            if (state == UnitState.Engage) engagedSince = Time.time;
        }

        private float FlatDistance(Vector3 point)
        {
            Vector3 d = point - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        private void HandleKnockedDown(Vector3 threat)
        {
            if (!IsAlive) return;
            State = UnitState.Down;
            if (agent.enabled) agent.enabled = false;
            armsTarget = 0f;
            armsWeight = 0f;
            if (weaponModel != null) weaponModel.SetActive(false);
        }

        private void HandleRecovered(Vector3 threat)
        {
            if (State != UnitState.Down) return;
            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                owner.Release(this);
                return;
            }
            agent.enabled = true;
            agent.Warp(hit.position);
            SetState(UnitState.Pursue);
        }

        private void HandleDied(Vector3 threat)
        {
            if (State == UnitState.Inactive) return;
            active.Remove(this);
            State = UnitState.Dead;
            deadSince = Time.time;
            if (agent.enabled) agent.enabled = false;
            if (weaponModel != null) weaponModel.SetActive(false);
            if (mapBlip != null) mapBlip.enabled = false;
            gameObject.layer = defaultLayer;
            gameObject.tag = defaultTag;
        }

        private void UpdateCorpse()
        {
            float now = Time.time;
            if (now < nextCorpseCheck) return;
            nextCorpseCheck = now + 1f;
            if (now - deadSince >= settings.corpseSeconds && owner.CanVanish(damage.BodyPosition, recallKeepDistance)) owner.Release(this);
        }

        /// <summary>True for a police officer: one of these units or a street officer of the pedestrian system.</summary>
        public static bool IsOfficer(PedestrianDamage pedestrian)
        {
            return pedestrian != null && (pedestrian.GetComponent<PoliceOfficerUnit>() != null || PoliceDispatcher.IsStreetOfficerName(pedestrian.name));
        }

        /// <summary>The first standing officer within <paramref name="tolerance"/> of a line (aim checks).</summary>
        public static PoliceOfficerUnit FindAlongLine(Vector3 from, Vector3 direction, float length, float tolerance)
        {
            PoliceOfficerUnit closest = null;
            float closestAlong = float.MaxValue;
            for (int i = 0; i < active.Count; i++)
            {
                PoliceOfficerUnit unit = active[i];
                if (!unit.IsStanding) continue;
                Vector3 body = unit.transform.position + Vector3.up * 1.1f;
                float along = Vector3.Dot(body - from, direction);
                if (along < 0f || along > length || along >= closestAlong) continue;
                if ((body - (from + direction * along)).sqrMagnitude > tolerance * tolerance) continue;
                closestAlong = along;
                closest = unit;
            }
            return closest;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => active.Clear();
    }
}
