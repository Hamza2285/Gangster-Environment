using System.Collections;
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
    /// Car crews (<see cref="ActivateFromCar"/>): the officer starts in his seat of a <see cref="PoliceCarUnit"/> and climbs out with the
    /// car-exit clip (door opened and closed by <see cref="PoliceCarCrew"/>, root steered onto the ground beside the door), then joins
    /// the chase on foot. Called back by his car (<see cref="BeginBoarding"/>) he runs to the door and gets back in with the enter clip.
    /// </summary>
    [DisallowMultipleComponent]
    public class PoliceOfficerUnit : MonoBehaviour
    {
        public enum UnitState { Inactive, Pursue, Engage, Search, Return, Down, Dead, ExitingCar, Boarding }

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int ShootStateId = Animator.StringToHash("Shoot");
        private static readonly int LocomotionStateId = Animator.StringToHash("Locomotion");
        private static readonly int ExitCarStateId = Animator.StringToHash("ExitCar");
        private static readonly int ExitCarMirrorStateId = Animator.StringToHash("ExitCarMirror");
        private static readonly int EnterCarStateId = Animator.StringToHash("EnterCar");
        private static readonly int EnterCarMirrorStateId = Animator.StringToHash("EnterCarMirror");

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

        [Header("Getting Out Of / Into The Car")]
        [Tooltip("Playback speed of the car exit / enter clips (they are slow, relaxed motions; police get out in a hurry).")]
        [SerializeField, Min(0.1f)] private float carClipSpeed = 1.7f;
        [Tooltip("Exit clip (normalised time): door opens, root steered onto the ground beside the door between Match Start and End, " +
                 "the officer is out at End Time and the door shuts.")]
        [SerializeField, Range(0f, 1f)] private float exitDoorOpenTime = 0.06f;
        [SerializeField, Range(0f, 1f)] private float exitMatchStart = 0.12f;
        [SerializeField, Range(0f, 1f)] private float exitMatchEnd = 0.62f;
        [SerializeField, Range(0f, 1f)] private float exitEndTime = 0.7f;
        [Tooltip("Enter clip (normalised time): door opens, root steered into the seat between Match Start and End, seated at End Time.")]
        [SerializeField, Range(0f, 1f)] private float enterDoorOpenTime = 0.12f;
        [SerializeField, Range(0f, 1f)] private float enterMatchStart = 0.2f;
        [SerializeField, Range(0f, 1f)] private float enterMatchEnd = 0.75f;
        [SerializeField, Range(0f, 1f)] private float enterEndTime = 0.85f;
        [Tooltip("Seconds a called-back officer may take to reach his car before he carries on on foot.")]
        [SerializeField, Min(1f)] private float boardTimeout = 15f;

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
        /// <summary>The car this officer climbed out of (null for officers who came on foot or whose car is gone).</summary>
        public PoliceCarUnit HomeCar { get; private set; }
        public int HomeSeat { get; private set; } = -1;

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
        private Coroutine carRoutine;
        private Collider[] ownColliders = new Collider[0];
        private PedestrianRagdoll ragdoll;
        private float boardingSince;
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
            ownColliders = GetComponentsInChildren<Collider>(true);
            ragdoll = GetComponent<PedestrianRagdoll>();
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
            damage.SetAliveColliderEnabled(true); // an officer who got back into his car went to the pool with it off
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
            HomeCar = null;
            HomeSeat = -1;
            SetState(UnitState.Pursue);
            if (!active.Contains(this)) active.Add(this);
            return true;
        }

        /// <summary>
        /// Puts the officer in his seat of <paramref name="car"/> and climbs him out (exit clip, door, onto the ground beside the door),
        /// after which he pursues on foot like any officer. False when there is no NavMesh beside the door (he stays pooled).
        /// </summary>
        public bool ActivateFromCar(PoliceDispatcher dispatcher, PoliceCarUnit car, int seat, int sensorLayer)
        {
            if (!valid || car == null || car.Crew == null) return false;
            Vector3 ground = car.Crew.GetDoorGroundPoint(seat);
            if (!NavMesh.SamplePosition(ground, out NavMeshHit hit, 4f, NavMesh.AllAreas)) return false;
            owner = dispatcher;
            settings = dispatcher.Settings;
            wanted = WantedManager.Instance;

            car.Crew.GetSeatPose(seat, out Vector3 seatPosition, out Quaternion seatRotation);
            transform.SetPositionAndRotation(seatPosition, seatRotation);
            gameObject.SetActive(true);
            damage.ResetForReuse();
            damage.SetHitboxesEnabled(true);
            // His solid body would shove the car while he is still inside it.
            damage.SetAliveColliderEnabled(false);
            gameObject.layer = sensorLayer;
            gameObject.tag = "Player";
            agent.enabled = false;

            armsWeight = 0f;
            armsTarget = 0f;
            animator.SetLayerWeight(armsLayer, 0f);
            if (weaponModel != null) weaponModel.SetActive(false);
            if (mapBlip != null) mapBlip.enabled = true;
            nextRepath = 0f;
            nextShot = 0f;
            nextLineCheck = 0f;
            hasLineOfFire = false;
            searchPointUntil = 0f;
            HomeCar = car;
            HomeSeat = seat;
            car.Crew.SetCollisionIgnored(ownColliders, true);
            SetState(UnitState.ExitingCar);
            if (!active.Contains(this)) active.Add(this);
            carRoutine = StartCoroutine(ExitCarRoutine(car, seat, ground));
            return true;
        }

        /// <summary>His car is leaving: run back to its door and get in (the car shows him seated again and he goes back to the pool).</summary>
        public void BeginBoarding(PoliceCarUnit car)
        {
            if (!IsStanding || car == null || car != HomeCar || State == UnitState.ExitingCar) return;
            SetArms(false);
            boardingSince = Time.time;
            SetState(UnitState.Boarding);
        }

        /// <summary>The car is gone (recalled, wrecked) or gave up waiting: he carries on as an officer on foot.</summary>
        public void ClearHomeCar()
        {
            HomeCar = null;
            HomeSeat = -1;
            if (State == UnitState.Boarding && carRoutine == null) SetState(UnitState.Pursue);
        }

        private IEnumerator ExitCarRoutine(PoliceCarUnit car, int seat, Vector3 groundTarget)
        {
            int stateId = car.Crew.IsMirrored(seat) ? ExitCarMirrorStateId : ExitCarStateId;
            animator.applyRootMotion = true;
            animator.speed = carClipSpeed;
            animator.Play(stateId, 0, 0f);
            animator.Update(0f);
            bool doorOpened = false;
            float timer = 0f;
            while (State == UnitState.ExitingCar && timer < 8f)
            {
                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
                if (info.shortNameHash == stateId)
                {
                    float n = info.normalizedTime;
                    if (!doorOpened && n >= exitDoorOpenTime)
                    {
                        doorOpened = true;
                        car.Crew.OpenDoor(seat);
                    }
                    if (n >= exitEndTime) break;
                    if (n >= exitMatchStart && n < exitMatchEnd && !animator.IsInTransition(0))
                        animator.MatchTarget(groundTarget, Quaternion.identity, AvatarTarget.Root, new MatchTargetWeightMask(Vector3.one, 0f), Mathf.Max(n, exitMatchStart), exitMatchEnd);
                }
                timer += Time.deltaTime;
                yield return null;
            }
            carRoutine = null;
            animator.speed = 1f;
            animator.applyRootMotion = false;
            if (car != null && car.Crew != null) car.Crew.CloseDoor(seat);
            if (State != UnitState.ExitingCar) yield break; // shot or knocked down on the way out
            damage.SetAliveColliderEnabled(true);
            // Clear of the car a moment later (he is still at its side when he sets off).
            if (car != null && car.Crew != null) StartCoroutine(RestoreCarCollision(car.Crew, 1.5f));
            animator.CrossFadeInFixedTime(LocomotionStateId, 0.25f, 0, 0f);
            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                owner.Release(this);
                yield break;
            }
            agent.enabled = true;
            agent.Warp(hit.position);
            SetState(UnitState.Pursue);
        }

        private IEnumerator RestoreCarCollision(PoliceCarCrew crew, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (crew != null) crew.SetCollisionIgnored(ownColliders, false);
        }

        // Called back: run to the door, line up beside it, then the enter clip carries him into the seat.
        private void Board()
        {
            if (HomeCar == null || !HomeCar.isActiveAndEnabled || HomeCar.Crew == null || Time.time - boardingSince > boardTimeout)
            {
                if (HomeCar != null) HomeCar.OnCrewGaveUp(this);
                ClearHomeCar();
                SetState(UnitState.Pursue);
                return;
            }
            if (carRoutine != null) return;
            Vector3 door = HomeCar.Crew.GetDoorGroundPoint(HomeSeat);
            agent.speed = settings.officerRunSpeed;
            if (FlatDistance(door) > 0.7f)
            {
                MoveTo(door, true);
                return;
            }
            carRoutine = StartCoroutine(EnterCarRoutine(HomeCar, HomeSeat, door));
        }

        private IEnumerator EnterCarRoutine(PoliceCarUnit car, int seat, Vector3 door)
        {
            if (agent.enabled) agent.enabled = false;
            damage.SetAliveColliderEnabled(false);
            car.Crew.SetCollisionIgnored(ownColliders, true);
            animator.SetFloat(SpeedId, 0f);
            // Beside the door, facing across the car (the clip turns him into the seat).
            Vector3 across = car.Crew.IsMirrored(seat) ? -car.transform.right : car.transform.right;
            Quaternion facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(across, Vector3.up).normalized);
            Vector3 fromPosition = transform.position;
            Quaternion fromRotation = transform.rotation;
            for (float t = 0f; t < 0.25f; t += Time.deltaTime)
            {
                float k = t / 0.25f;
                transform.SetPositionAndRotation(Vector3.Lerp(fromPosition, door, k), Quaternion.Slerp(fromRotation, facing, k));
                yield return null;
            }
            transform.SetPositionAndRotation(door, facing);

            int stateId = car.Crew.IsMirrored(seat) ? EnterCarMirrorStateId : EnterCarStateId;
            animator.applyRootMotion = true;
            animator.speed = carClipSpeed;
            animator.CrossFadeInFixedTime(stateId, 0.15f, 0, 0f);
            bool doorOpened = false;
            float timer = 0f;
            while (State == UnitState.Boarding && timer < 8f && car != null && car.isActiveAndEnabled)
            {
                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
                if (info.shortNameHash == stateId)
                {
                    float n = info.normalizedTime;
                    if (!doorOpened && n >= enterDoorOpenTime)
                    {
                        doorOpened = true;
                        car.Crew.OpenDoor(seat);
                    }
                    if (n >= enterEndTime) break;
                    if (n >= enterMatchStart && n < enterMatchEnd && !animator.IsInTransition(0))
                    {
                        car.Crew.GetSeatPose(seat, out Vector3 seatPosition, out Quaternion seatRotation);
                        animator.MatchTarget(seatPosition, seatRotation, AvatarTarget.Root, new MatchTargetWeightMask(Vector3.one, 1f), Mathf.Max(n, enterMatchStart), enterMatchEnd);
                    }
                }
                timer += Time.deltaTime;
                yield return null;
            }
            carRoutine = null;
            animator.speed = 1f;
            animator.applyRootMotion = false;
            if (car != null && car.Crew != null) car.Crew.SetCollisionIgnored(ownColliders, false);
            if (State != UnitState.Boarding || car == null || !car.isActiveAndEnabled)
            {
                damage.SetAliveColliderEnabled(true);
                yield break;
            }
            car.OnCrewBoarded(this, seat);
            HomeCar = null;
            HomeSeat = -1;
            owner.Release(this);
        }


        /// <summary>Back to the pool (called by the dispatcher).</summary>
        public void Deactivate()
        {
            active.Remove(this);
            State = UnitState.Inactive;
            if (carRoutine != null) StopCoroutine(carRoutine);
            carRoutine = null;
            HomeCar = null;
            HomeSeat = -1;
            animator.speed = 1f;
            animator.applyRootMotion = false;
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
                case UnitState.ExitingCar:
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
            if (State == UnitState.Boarding)
            {
                Board();
                return;
            }
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
            AbortCarSequence();
            State = UnitState.Down;
            if (agent.enabled) agent.enabled = false;
            armsTarget = 0f;
            armsWeight = 0f;
            if (weaponModel != null) weaponModel.SetActive(false);
        }

        // Back up: the ragdoll plays its get-up clip (sits up, stands) and only then does the officer move and draw again.
        private void HandleRecovered(Vector3 threat)
        {
            if (State != UnitState.Down) return;
            float getUp = ragdoll != null ? ragdoll.GetUpDuration : 0f;
            if (carRoutine != null) StopCoroutine(carRoutine);
            carRoutine = getUp > 0f ? StartCoroutine(StandUpRoutine(getUp)) : null;
            if (getUp <= 0f) ResumeAfterStandUp();
        }

        private IEnumerator StandUpRoutine(float seconds)
        {
            animator.SetFloat(SpeedId, 0f);
            yield return new WaitForSeconds(seconds);
            carRoutine = null;
            if (State != UnitState.Down) yield break;
            animator.CrossFadeInFixedTime(LocomotionStateId, 0.3f, 0, 0f);
            ResumeAfterStandUp();
        }

        private void ResumeAfterStandUp()
        {
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
            AbortCarSequence();
            active.Remove(this);
            State = UnitState.Dead;
            deadSince = Time.time;
            if (agent.enabled) agent.enabled = false;
            if (weaponModel != null) weaponModel.SetActive(false);
            if (mapBlip != null) mapBlip.enabled = false;
            gameObject.layer = defaultLayer;
            gameObject.tag = defaultTag;
        }

        // Hit while getting in or out: his car stops waiting for him, the clip state is dropped (the ragdoll takes over).
        private void AbortCarSequence()
        {
            if (carRoutine != null) StopCoroutine(carRoutine);
            carRoutine = null;
            animator.speed = 1f;
            animator.applyRootMotion = false;
            if (HomeCar != null)
            {
                if (HomeCar.Crew != null && HomeSeat >= 0) HomeCar.Crew.CloseDoor(HomeSeat);
                // The falling body may start inside the cabin: it keeps ignoring the car until it has dropped clear.
                if (HomeCar.Crew != null && isActiveAndEnabled) StartCoroutine(RestoreCarCollision(HomeCar.Crew, 2f));
                HomeCar.OnCrewGaveUp(this);
            }
            HomeCar = null;
            HomeSeat = -1;
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
