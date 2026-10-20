using System;
using System.Collections;
using Invector;
using Invector.vCharacterController;
using OpenworldCrime.Combat;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>Life cycle of a vehicle that started as iTS traffic.</summary>
    public enum TrafficVehicleState
    {
        /// <summary>Driven by iTS (lightweight traffic car, <see cref="TrafficVehicleAdapter"/>).</summary>
        TrafficControlled,
        /// <summary>Taken out of traffic for the player: stopped, driver still inside or player getting in.</summary>
        BeingCarjacked,
        /// <summary>The player drives it.</summary>
        PlayerControlled,
        /// <summary>Left by the player: parked, never returns to traffic.</summary>
        Abandoned,
        /// <summary>The traffic driver was shot dead: the car brakes to a stop and is then handed over as an empty parked vehicle.</summary>
        DriverKilled
    }

    /// <summary>
    /// NPC driver of a drivable vehicle taken over from traffic (lives on the drivable vehicle next to its
    /// <see cref="IVehicleController"/>). While seated, the driver is only a static seated mesh (NPC Seated Visual) in the
    /// traffic driver's outfit - no character, animation or physics cost. For a carjack the player's side
    /// (PlayerVehicleInteraction) drives the beats:
    /// 1. <see cref="PrepareForPullOut"/>: a real <see cref="EnemyDummy"/> in the same outfit is spawned in the exact seated pose.
    /// 2. <see cref="BeginPullOut"/>: he is yanked out (Pulled Out State, default the Vehicle_Exit clip: a fast "yank" section,
    ///    then a slower "stumble out" section) with root motion warped onto the pull-out point in front of the player, feet kept
    ///    above the road (<see cref="FootGroundClamp"/>) and an upper-body flinch layer.
    /// 3. <see cref="Shove"/>: he stumbles away in the direction he was pushed (one of four directional stumble states, so he
    ///    barely has to turn) and only then drops into Invector's ragdoll gently (<see cref="EnemyVehicleImpact.Launch"/>),
    ///    which also handles the get-up.
    /// <see cref="Eject"/> runs the same beats on its own (fallback without player choreography).
    /// Clips are assigned to the named states in the NPC's Animator Controller, so better clips can be dropped in there.
    /// While seated the driver can be shot through the windows (<see cref="SeatedDriverHealth"/>, cheap trigger hit zones):
    /// once dead, the driver door opens and a real NPC in the same outfit is spawned in the seated pose, killed through
    /// Invector's damage and falls out as a ragdoll (<see cref="DropDead"/>); the vehicle is then an empty parked car.
    /// The driver never gets back in. Also reports the <see cref="TrafficVehicleState"/> of the vehicle.
    /// </summary>
    [DisallowMultipleComponent]
    public class TrafficVehicleDriver : MonoBehaviour
    {
        [Header("NPC")]
        [Tooltip("Character spawned when the driver is pulled out (EnemyDummy prefab with EnemyVehicleImpact).")]
        [SerializeField] private EnemyVehicleImpact npcPrefab;
        [Tooltip("Static seated driver mesh under the DriverSeat (inactive by default). Shown while the NPC sits at the wheel.")]
        [SerializeField] private GameObject npcSeatedVisual;
        [Tooltip("Highest driver head (driver seat space, y) this car's roof allows; taller pedestrians sit a little smaller. " +
                 "0 = no limit. Set by Build Pedestrian Drivers.")]
        [SerializeField] private float driverCrownLimit;
        [Tooltip("Outfit used when no traffic car hands one over.")]
        [SerializeField] private Material defaultOutfit;

        [Header("Animator States (NPC controller)")]
        [Tooltip("Seated pose (same pose as the seated visual).")]
        [SerializeField] private string seatedStateName = "Vehicle_Seated";
        [Tooltip("Being pulled out of the seat (root motion; default clip Vehicle_Exit).")]
        [SerializeField] private string pulledOutStateName = "Vehicle_PulledOut";
        [Tooltip("Standing, dazed, if the shove comes late.")]
        [SerializeField] private string standingStateName = "Idle";
        [Tooltip("Shoved from behind: stumbles forward (default Hit Big Back).")]
        [SerializeField] private string stumbleForwardStateName = "Carjack_StumbleForward";
        [Tooltip("Shoved from the front: stumbles backward (default Hit Big Front).")]
        [SerializeField] private string stumbleBackwardStateName = "Carjack_StumbleBackward";
        [Tooltip("Shoved from the right: stumbles to his left (default Hit Big Right).")]
        [SerializeField] private string stumbleLeftStateName = "Carjack_Stumble";
        [Tooltip("Shoved from the left: stumbles to his right (default Hit Big Left).")]
        [SerializeField] private string stumbleRightStateName = "Carjack_StumbleRight";
        [Tooltip("Upper-body flinch played on the Flinch Layer while he is grabbed (optional).")]
        [SerializeField] private string flinchStateName = "Carjack_Flinch";
        [SerializeField] private string flinchLayerName = "UpperBodyReact";

        [Header("Carjack Points")]
        [Tooltip("Where the player stands to pull the driver out: right at the door opening, facing the driver.")]
        [SerializeField] private Transform carjackStandPoint;
        [Tooltip("Where the driver's root ends the pull-out: outside, toward the front of the door opening, clear of the player and the open door. " +
                 "Its forward is the direction he is shoved in (away from the car, clear of the open door).")]
        [SerializeField] private Transform ejectPoint;
        [Tooltip("Layers the pull-out point is dropped onto.")]
        [SerializeField] private LayerMask groundLayers = 1;

        [Header("Pull Out")]
        [Tooltip("Pulled Out clip time where the pull starts ...")]
        [SerializeField, Range(0f, 1f)] private float pullStartTime = 0.3f;
        [Tooltip("... the end of the fast yank (legs out, body over the sill) ...")]
        [SerializeField, Range(0f, 1f)] private float yankEndTime = 0.45f;
        [Tooltip("... and where he stands outside.")]
        [SerializeField, Range(0f, 1f)] private float pullEndTime = 0.6f;
        [Tooltip("Seconds of the fast yank.")]
        [SerializeField, Min(0.1f)] private float yankDuration = 0.35f;
        [Tooltip("Seconds of the stumble out after the yank.")]
        [SerializeField, Min(0.1f)] private float stumbleOutDuration = 0.5f;
        [SerializeField, Min(0f)] private float pullCrossFade = 0.12f;
        [Tooltip("Flinch layer weight while being grabbed.")]
        [SerializeField, Range(0f, 1f)] private float flinchWeight = 0.6f;
        [Tooltip("Seconds the pulled-out driver waits to be shoved before stumbling away on his own.")]
        [SerializeField, Min(0f)] private float maxWaitForShove = 1f;
        [Tooltip("Seconds without a shove after which he settles into the standing state (a quick shove follows the pull directly).")]
        [SerializeField, Min(0f)] private float standAfter = 0.3f;

        [Header("Shove / Fall")]
        [SerializeField, Min(0f)] private float stumbleCrossFade = 0.08f;
        [Tooltip("Seconds used to turn the remaining angle toward the shove direction.")]
        [SerializeField, Min(0.01f)] private float stumbleTurnDuration = 0.12f;
        [Tooltip("Normalized time of the stumble at which the driver loses balance and falls (ragdoll).")]
        [SerializeField, Range(0f, 1f)] private float fallTime = 0.55f;
        [Tooltip("Velocity (m/s) the falling body keeps along the shove direction (gentle - the stumble already moved him).")]
        [SerializeField, Min(0f)] private float fallSpeed = 1.4f;
        [SerializeField, Min(0f)] private float fallUpSpeed = 0.3f;
        [Tooltip("Tumble (rad/s) tipping the body over along the shove direction.")]
        [SerializeField, Min(0f)] private float fallTumble = 1.5f;
        [Tooltip("Minimum seconds on the ground before getting up.")]
        [SerializeField, Min(0f)] private float groundTime = 3f;

        [Header("Shot While Seated")]
        [Tooltip("Hit zones and health of the seated driver (child of the bullet hitbox). Optional: without it the seated driver cannot be shot.")]
        [SerializeField] private SeatedDriverHealth seatedHealth;
        [Tooltip("Seconds the driver door takes to swing open for the dead driver.")]
        [SerializeField, Min(0.05f)] private float deadDoorOpenDuration = 0.25f;
        [Tooltip("Seconds the dead body takes to slide out over the door sill (the pull-out clip's motion, from Pull Start Time).")]
        [SerializeField, Min(0.1f)] private float deadSlideDuration = 0.35f;
        [Tooltip("Pulled Out clip time at which the sliding body goes limp (ragdoll): legs out, hips on the sill, leaning out.")]
        [SerializeField, Range(0f, 1f)] private float deadCollapseTime = 0.42f;
        [Tooltip("Velocity (m/s) with which the limp body keeps falling away from the car.")]
        [SerializeField, Min(0f)] private float deadFallOutSpeed = 1.2f;
        [Tooltip("Tumble (rad/s) rolling the limp body out over the sill.")]
        [SerializeField, Min(0f)] private float deadFallTumble = 1.5f;
        [Tooltip("Top speed (m/s) at which the car's body collider eases limbs still inside the cabin out through the door opening.")]
        [SerializeField, Min(0.1f)] private float deadDepenetrationSpeed = 1.5f;
        [Tooltip("Seconds that gentle push-out lasts before the limbs get their normal physics back.")]
        [SerializeField, Min(0.1f)] private float deadSettleTime = 1.5f;

        /// <summary>Raised when a driver has been thrown out (the spawned character now lives on its own).</summary>
        public static event Action<EnemyDummy> OnDriverEjected;

#if UNITY_EDITOR
        public void EditorSetDriverRoster(PedDriverRoster driverRoster) => roster = driverRoster;
        public void EditorSetDriverCrownLimit(float crownLimit) => driverCrownLimit = crownLimit;
        public GameObject EditorSeatedVisual => npcSeatedVisual;
#endif

        public bool HasLivingDriver { get; private set; }
        /// <summary>True while a dead driver's body is being dropped out of the car.</summary>
        public bool IsDroppingDeadDriver => deadRoutine != null;
        /// <summary>True from the pull-out until the driver has fallen.</summary>
        public bool IsEjecting => IsHolding;
        /// <summary>True once the pulled-out driver stands outside the car (ready to be shoved).</summary>
        public bool IsPulledOut { get; private set; }
        /// <summary>Where the player stands for the carjack (null = no carjack choreography on this vehicle).</summary>
        public Transform CarjackStandPoint => carjackStandPoint;
        /// <summary>Bone the player's hand grabs while pulling (the driver's upper chest); the seat before he is spawned.</summary>
        public Transform GrabTarget { get; private set; }
        /// <summary>Flat direction the driver is shoved in (the Eject Point's forward; away from the car without one).</summary>
        public Vector3 ShoveDirection
        {
            get
            {
                Vector3 direction = ejectPoint != null ? ejectPoint.forward : -transform.right;
                direction.y = 0f;
                return direction.sqrMagnitude > 0.0001f ? direction.normalized : -transform.right;
            }
        }
        /// <summary>0 before the driver starts moving, 1 once he stands outside (progress through the pull-out clip window).</summary>
        public float PullProgress { get; private set; }
        /// <summary>Seconds the whole pull-out takes.</summary>
        public float PullDuration => yankDuration + stumbleOutDuration;

        public TrafficVehicleState VehicleState
        {
            get
            {
                if (vehicle == null) return TrafficVehicleState.Abandoned;
                if (vehicle.IsControlled) return TrafficVehicleState.PlayerControlled;
                return vehicle.IsOccupied || HasLivingDriver || IsEjecting ? TrafficVehicleState.BeingCarjacked : TrafficVehicleState.Abandoned;
            }
        }

        private IVehicleController vehicle;
        private int seatedStateHash;
        private int pulledOutStateHash;
        private int standingStateHash;
        private int stumbleForwardHash;
        private int stumbleBackwardHash;
        private int stumbleLeftHash;
        private int stumbleRightHash;
        private int flinchStateHash;
        private Material outfit;
        private EnemyVehicleImpact heldNpc;
        private Animator heldAnimator;
        private FootGroundClamp heldFeet;
        private int flinchLayer = -1;
        private AnimatorCullingMode heldCullingMode;
        private bool heldRootMotion;
        private float pullClipLength = 1f;
        private Coroutine pullRoutine;
        private Coroutine shoveRoutine;
        private Coroutine deadRoutine;
        private bool shoveRequested;
        private Vector3 shoveDirection;

        // Real street pedestrian as the driver (PedDriverRoster). While pulled out it is a held walker of the PedestrianSpawner
        // playing the carjack clips through its humanoid avatar; afterwards it is a normal walker again.
        private PedDriverRoster roster;
        private int pedIndex = -1;
        private PedestrianWalker heldPed;
        private Avatar heldPedAvatar;
        private RuntimeAnimatorController heldPedController;
        private Collider heldPedCapsule;
        private Vector3 heldPedScale;

        private bool IsHolding => heldNpc != null || heldPed != null;
        private Transform HeldTransform => heldNpc != null ? heldNpc.transform : heldPed != null ? heldPed.transform : null;
        private bool CanSpawnPedestrian => roster != null && roster.Get(pedIndex) != null && roster.Get(pedIndex).humanoidAvatar != null
                                           && roster.CarjackController != null && PedestrianSpawner.Active != null;

        private void Awake()
        {
            vehicle = GetComponent<IVehicleController>();
            if (vehicle == null) Debug.LogError($"[{nameof(TrafficVehicleDriver)}] No IVehicleController on '{name}'; the driver cannot be shown or pulled out.", this);
            if (npcPrefab == null) Debug.LogWarning($"[{nameof(TrafficVehicleDriver)}] No NPC prefab on '{name}'; a pulled-out driver will just disappear.", this);
            if (npcSeatedVisual == null) Debug.LogWarning($"[{nameof(TrafficVehicleDriver)}] No NPC seated visual on '{name}'; the seated driver is invisible.", this);
            else npcSeatedVisual.SetActive(false);
            seatedStateHash = Animator.StringToHash(seatedStateName);
            pulledOutStateHash = Animator.StringToHash(pulledOutStateName);
            standingStateHash = Animator.StringToHash(standingStateName);
            stumbleForwardHash = Animator.StringToHash(stumbleForwardStateName);
            stumbleBackwardHash = Animator.StringToHash(stumbleBackwardStateName);
            stumbleLeftHash = Animator.StringToHash(stumbleLeftStateName);
            stumbleRightHash = Animator.StringToHash(stumbleRightStateName);
            flinchStateHash = Animator.StringToHash(flinchStateName);
            outfit = defaultOutfit;
            if (seatedHealth != null) seatedHealth.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (seatedHealth != null) seatedHealth.OnKilled += HandleSeatedDriverKilled;
        }

        private void OnDisable()
        {
            if (seatedHealth != null) seatedHealth.OnKilled -= HandleSeatedDriverKilled;
            deadRoutine = null;
            // Never leave a half-pulled character hanging: let it fall where it is.
            if (!IsHolding) return;
            StopAllCoroutines();
            pullRoutine = null;
            shoveRoutine = null;
            Fall(vehicle != null ? -vehicle.transform.right : -transform.right);
        }

        /// <summary>
        /// Seats (shows) or removes a real street pedestrian as the driver: entry <paramref name="index"/> of the roster (the
        /// traffic car's driver), shown as its baked seated mesh and pulled out as that same pedestrian.
        /// </summary>
        public void SetSeatedPedestrian(bool seated, PedDriverRoster driverRoster, int index)
        {
            roster = driverRoster;
            pedIndex = index;
            HasLivingDriver = seated;
            SetSeatedHitZones(seated);
            if (npcSeatedVisual == null) return;
            if (seated && roster != null) roster.ApplySeated(npcSeatedVisual, index, driverCrownLimit);
            npcSeatedVisual.SetActive(seated);
        }

        /// <summary>Seats (shows) or removes the driver without any reaction. The outfit (null = default) is the traffic driver's.</summary>
        public void SetSeated(bool seated, Material driverOutfit = null)
        {
            pedIndex = -1;
            HasLivingDriver = seated;
            if (driverOutfit != null) outfit = driverOutfit;
            SetSeatedHitZones(seated);
            if (npcSeatedVisual == null) return;
            if (seated && outfit != null) ApplyOutfit(npcSeatedVisual, outfit);
            npcSeatedVisual.SetActive(seated);
        }

        /// <summary>
        /// Kills the seated driver without a carjack: the driver door opens and his body falls out as a ragdoll (no get-up).
        /// <paramref name="push"/> is the direction of the killing shot (flat, may be zero). Returns false if nobody sits there.
        /// </summary>
        public bool DropDead(Vector3 push)
        {
            if (!HasLivingDriver || vehicle == null || IsHolding || deadRoutine != null) return false;
            HasLivingDriver = false;
            SetSeatedHitZones(false);
            if (npcSeatedVisual != null) npcSeatedVisual.SetActive(false);
            if ((npcPrefab == null && !CanSpawnPedestrian) || !isActiveAndEnabled) return true;
            deadRoutine = StartCoroutine(CanSpawnPedestrian ? DeadPedestrianFallOutRoutine(push) : DeadFallOutRoutine(push));
            return true;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Real pedestrian drivers

        // Takes the driver's pedestrian from the spawner in the exact seated pose (humanoid avatar + carjack clips).
        private PedestrianWalker SpawnPedestrianInSeat(Transform seat, out Animator animator)
        {
            animator = null;
            PedDriverRoster.Entry entry = roster.Get(pedIndex);
            // Same size and place as the seated mesh it replaces (scaled about the hips in a low-roofed car).
            float scale = roster.SeatedScale(pedIndex, driverCrownLimit);
            Vector3 rootInSeat = entry.seatedHips + (entry.seatOffset - entry.seatedHips) * scale;
            PedestrianWalker walker = PedestrianSpawner.Active.AdoptDriver(entry.spawnerIndex, seat.TransformPoint(rootInSeat), seat.rotation);
            if (walker == null) return null;
            walker.transform.SetParent(seat, true);
            heldPedScale = walker.transform.localScale;
            if (scale < 1f) walker.transform.localScale = heldPedScale * scale;
            animator = walker.GetComponentInChildren<Animator>();
            heldPedCapsule = walker.GetComponent<CapsuleCollider>();
            if (heldPedCapsule != null) heldPedCapsule.enabled = false;
            if (animator != null)
            {
                heldPedAvatar = animator.avatar;
                heldPedController = animator.runtimeAnimatorController;
                animator.avatar = entry.humanoidAvatar;
                animator.runtimeAnimatorController = roster.CarjackController;
                animator.Rebind();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                animator.speed = 1f;
                animator.Play(seatedStateHash, 0, 0f);
                animator.Update(0f);
                if (animator.GetComponent<FootGroundClamp>() == null) animator.gameObject.AddComponent<FootGroundClamp>();
            }
            return walker;
        }

        // Back to the pedestrian's own rig and walker animations (before its ragdoll / get-up / run take over).
        private void RestorePedestrianRig(PedestrianWalker walker, Animator animator)
        {
            if (walker == null) return;
            if (animator != null)
            {
                FootGroundClamp feet = animator.GetComponent<FootGroundClamp>();
                if (feet != null) feet.Clear();
                animator.speed = 1f;
                animator.applyRootMotion = false;
                if (heldPedAvatar != null) animator.avatar = heldPedAvatar;
                if (heldPedController != null) animator.runtimeAnimatorController = heldPedController;
                animator.Rebind();
                animator.cullingMode = AnimatorCullingMode.CullCompletely;
            }
            if (walker.transform.parent != null && PedestrianSpawner.Active != null) walker.transform.SetParent(PedestrianSpawner.Active.ActiveRoot, true);
            if (heldPedScale != Vector3.zero) walker.transform.localScale = heldPedScale;
            heldPedScale = Vector3.zero;
            if (heldPedCapsule != null) heldPedCapsule.enabled = true;
            heldPedAvatar = null;
            heldPedController = null;
            heldPedCapsule = null;
        }

        // Same beats as DeadFallOutRoutine with the real pedestrian: slides out over the sill, then is killed (its own ragdoll).
        private IEnumerator DeadPedestrianFallOutRoutine(Vector3 push)
        {
            VehicleDoor door = vehicle.GetDriverDoor();
            if (door != null && door.IsAvailable) door.Open(deadDoorOpenDuration);
            Transform seat = vehicle.GetDriverSeat();
            PedestrianWalker walker = SpawnPedestrianInSeat(seat, out Animator animator);
            if (walker == null)
            {
                deadRoutine = null;
                yield break;
            }
            yield return null;

            Vector3 target = GetGroundedPullOutPosition(seat);
            walker.transform.SetParent(PedestrianSpawner.Active != null ? PedestrianSpawner.Active.ActiveRoot : null, true);
            FootGroundClamp feet = animator != null ? animator.GetComponent<FootGroundClamp>() : null;
            if (feet != null) feet.SetGround(target.y);
            if (animator != null)
            {
                animator.Play(pulledOutStateHash, 0, 0f);
                animator.Update(0f);
                float stateLength = animator.GetCurrentAnimatorStateInfo(0).length;
                float clipLength = stateLength > 0.1f ? stateLength : 5.8f;
                animator.Play(seatedStateHash, 0, 0f);
                animator.Update(0f);
                animator.applyRootMotion = true;
                animator.speed = Mathf.Max(0.1f, (deadCollapseTime - pullStartTime) * clipLength / deadSlideDuration);
                animator.CrossFadeInFixedTime(pulledOutStateHash, pullCrossFade, 0, pullStartTime * clipLength / animator.speed);
                float timer = 0f;
                while (walker != null && timer < deadSlideDuration + 1f)
                {
                    AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
                    bool inPull = info.shortNameHash == pulledOutStateHash;
                    if (inPull && info.normalizedTime >= deadCollapseTime) break;
                    if (inPull && !animator.IsInTransition(0))
                        animator.MatchTarget(target, Quaternion.identity, AvatarTarget.Root, new MatchTargetWeightMask(Vector3.one, 0f),
                            Mathf.Max(info.normalizedTime, pullStartTime), pullEndTime);
                    timer += Time.deltaTime;
                    yield return null;
                }
            }
            if (walker == null)
            {
                deadRoutine = null;
                yield break;
            }

            RestorePedestrianRig(walker, animator);
            // Killed first (its ragdoll takes the body), then handed back: a dead walker stays a corpse until recycled.
            var damage = walker.GetComponent<OpenworldCrime.Pedestrians.PedestrianDamage>();
            if (damage != null) damage.Kill(vehicle.transform, seat.position);
            walker.Release(vehicle.transform.position);
            deadRoutine = null;
        }

        private void SetSeatedHitZones(bool active)
        {
            if (seatedHealth == null) return;
            if (active) seatedHealth.ResetHealth();
            seatedHealth.gameObject.SetActive(active);
        }

        private void HandleSeatedDriverKilled(SeatedDriverHealth health, vDamage damage)
        {
            DropDead(health.LastHitDirection);
        }

        // Spawned in the exact seated pose, it slides out through the open door with the pull-out clip's motion (warped onto the
        // pull-out point, like a carjack but faster and without the struggle) and is killed through Invector as it leans out over
        // the sill: EnemyDummy switches to its ragdoll on death. The car keeps colliding with it; limbs still inside the cabin
        // (inside the car's solid body collider) are eased out through the door opening at a gentle, capped speed, so the body
        // slumps out along the car's side instead of dropping through the floor or balancing upright.
        private IEnumerator DeadFallOutRoutine(Vector3 push)
        {
            VehicleDoor door = vehicle.GetDriverDoor();
            if (door != null && door.IsAvailable) door.Open(deadDoorOpenDuration);

            Transform seat = vehicle.GetDriverSeat();
            EnemyVehicleImpact npc = Instantiate(npcPrefab, seat.position, seat.rotation, seat);
            if (outfit != null) ApplyOutfit(npc.gameObject, outfit);
            Animator npcAnimator = npc.GetComponent<Animator>();
            FootGroundClamp feet = npc.GetComponent<FootGroundClamp>();
            EnemyDummy dummy = npc.GetComponent<EnemyDummy>();
            if (dummy != null && dummy.AliveCollider != null) dummy.AliveCollider.enabled = false;
            if (npcAnimator != null)
            {
                npcAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                npcAnimator.Play(seatedStateHash, 0, 0f);
                npcAnimator.Update(0f);
            }

            // One frame for Invector (vRagdoll, vCharacter) to initialise, and for the door to start swinging.
            yield return null;
            if (npc == null || (vehicle as UnityEngine.Object) == null)
            {
                deadRoutine = null;
                yield break;
            }

            // 1. Slides out over the sill (root motion warped onto the pull-out point, feet kept above the road).
            Vector3 target = GetGroundedPullOutPosition(seat);
            npc.transform.SetParent(null, true);
            if (feet != null) feet.SetGround(target.y);
            if (npcAnimator != null)
            {
                npcAnimator.Play(pulledOutStateHash, 0, 0f);
                npcAnimator.Update(0f);
                float stateLength = npcAnimator.GetCurrentAnimatorStateInfo(0).length;
                float clipLength = stateLength > 0.1f ? stateLength : 5.8f;
                npcAnimator.Play(seatedStateHash, 0, 0f);
                npcAnimator.Update(0f);
                npcAnimator.applyRootMotion = true;
                npcAnimator.speed = Mathf.Max(0.1f, (deadCollapseTime - pullStartTime) * clipLength / deadSlideDuration);
                npcAnimator.CrossFadeInFixedTime(pulledOutStateHash, pullCrossFade, 0, pullStartTime * clipLength / npcAnimator.speed);
                float timer = 0f;
                while (npc != null && timer < deadSlideDuration + 1f)
                {
                    AnimatorStateInfo info = npcAnimator.GetCurrentAnimatorStateInfo(0);
                    bool inPull = info.shortNameHash == pulledOutStateHash;
                    if (inPull && info.normalizedTime >= deadCollapseTime) break;
                    if (inPull && !npcAnimator.IsInTransition(0))
                    {
                        npcAnimator.MatchTarget(target, Quaternion.identity, AvatarTarget.Root, new MatchTargetWeightMask(Vector3.one, 0f),
                            Mathf.Max(info.normalizedTime, pullStartTime), pullEndTime);
                    }
                    timer += Time.deltaTime;
                    yield return null;
                }
                if (npc == null)
                {
                    deadRoutine = null;
                    yield break;
                }
                npcAnimator.speed = 1f;
            }
            if (feet != null) feet.Clear();

            // 2. Goes limp: killed through Invector's damage (EnemyDummy activates its ragdoll), falling on away from the car.
            Transform car = vehicle.transform;
            Vector3 side = Vector3.Dot(seat.position - car.position, car.right) >= 0f ? car.right : -car.right;
            Vector3 fall = side * deadFallOutSpeed;
            if (push.sqrMagnitude > 0.0001f) fall += Vector3.ProjectOnPlane(push, Vector3.up).normalized * (deadFallOutSpeed * 0.3f);
            vCharacter character = npc.GetComponent<vCharacter>();
            if (character != null && !character.isDead)
            {
                character.TakeDamage(new vDamage(Mathf.CeilToInt(character.MaxHealth * 10f), true)
                {
                    ignoreDefense = true,
                    sender = car,
                    hitPosition = seat.position,
                    damageType = "Shot"
                });
            }
            Animator bodyAnimator = npc.GetComponent<Animator>();
            Transform hips = bodyAnimator != null && bodyAnimator.isHuman ? bodyAnimator.GetBoneTransform(HumanBodyBones.Hips) : npc.transform;
            Vector3 tumble = Vector3.Cross(Vector3.up, side) * deadFallTumble;
            Rigidbody[] limbs = npc.GetComponentsInChildren<Rigidbody>();
            var savedDepenetration = new float[limbs.Length];
            for (int i = 0; i < limbs.Length; i++)
            {
                Rigidbody limb = limbs[i];
                savedDepenetration[i] = limb.maxDepenetrationVelocity;
                if (limb.isKinematic || limb.gameObject == npc.gameObject) continue;
                limb.maxDepenetrationVelocity = deadDepenetrationSpeed;
                limb.linearVelocity = fall + Vector3.Cross(tumble, limb.worldCenterOfMass - hips.position);
            }
            if (dummy != null) OnDriverEjected?.Invoke(dummy);

            // 3. Once it has slumped out, the limbs get their normal physics back.
            float wait = 0f;
            while (npc != null && wait < deadSettleTime)
            {
                wait += Time.deltaTime;
                yield return null;
            }
            for (int i = 0; i < limbs.Length; i++)
            {
                if (limbs[i] != null) limbs[i].maxDepenetrationVelocity = savedDepenetration[i];
            }
            deadRoutine = null;
        }

        /// <summary>
        /// Spawns the real driver character in the seated pose (replacing the static visual). Called when the player grabs, a
        /// frame or more before <see cref="BeginPullOut"/> so Invector initialises. Returns false if there is no living driver.
        /// </summary>
        public bool PrepareForPullOut()
        {
            if (IsHolding) return true;
            if (!HasLivingDriver || vehicle == null) return false;
            HasLivingDriver = false;
            SetSeatedHitZones(false);
            if (npcSeatedVisual != null) npcSeatedVisual.SetActive(false);

            Transform seat = vehicle.GetDriverSeat();
            if (CanSpawnPedestrian)
            {
                heldPed = SpawnPedestrianInSeat(seat, out heldAnimator);
                if (heldPed != null)
                {
                    heldFeet = heldAnimator != null ? heldAnimator.GetComponent<FootGroundClamp>() : null;
                    heldCullingMode = AnimatorCullingMode.CullCompletely;
                    heldRootMotion = false;
                    flinchLayer = heldAnimator != null ? heldAnimator.GetLayerIndex(flinchLayerName) : -1;
                    GrabTarget = heldAnimator != null && heldAnimator.isHuman
                        ? heldAnimator.GetBoneTransform(HumanBodyBones.UpperChest) ?? heldAnimator.GetBoneTransform(HumanBodyBones.Chest)
                        : heldPed.transform;
                    IsPulledOut = false;
                    PullProgress = 0f;
                    shoveRequested = false;
                    return true;
                }
            }
            if (npcPrefab == null) return false;

            heldNpc = Instantiate(npcPrefab, seat.position, seat.rotation, seat);
            if (outfit != null) ApplyOutfit(heldNpc.gameObject, outfit);
            heldAnimator = heldNpc.GetComponent<Animator>();
            heldFeet = heldNpc.GetComponent<FootGroundClamp>();
            EnemyDummy dummy = heldNpc.GetComponent<EnemyDummy>();
            if (dummy != null && dummy.AliveCollider != null) dummy.AliveCollider.enabled = false;
            if (heldAnimator != null)
            {
                heldCullingMode = heldAnimator.cullingMode;
                heldRootMotion = heldAnimator.applyRootMotion;
                heldAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                heldAnimator.Play(seatedStateHash, 0, 0f);
                heldAnimator.Update(0f);
                flinchLayer = heldAnimator.GetLayerIndex(flinchLayerName);
                GrabTarget = heldAnimator.isHuman ? heldAnimator.GetBoneTransform(HumanBodyBones.UpperChest) : null;
                if (GrabTarget == null && heldAnimator.isHuman) GrabTarget = heldAnimator.GetBoneTransform(HumanBodyBones.Chest);
            }
            IsPulledOut = false;
            PullProgress = 0f;
            shoveRequested = false;
            return true;
        }

        /// <summary>Starts the pull-out (spawns the driver first if needed). Returns false if there is no driver.</summary>
        public bool BeginPullOut()
        {
            if (!IsHolding && !PrepareForPullOut()) return false;
            if (pullRoutine != null || IsPulledOut) return true;
            pullRoutine = StartCoroutine(PullOutRoutine());
            return true;
        }

        /// <summary>Shoves the driver along the given direction: he stumbles, then falls. Queued if he is still being pulled out.</summary>
        public void Shove(Vector3 direction)
        {
            if (!IsHolding) return;
            direction.y = 0f;
            shoveDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : -vehicle.transform.right;
            shoveRequested = true;
            if (IsPulledOut && shoveRoutine == null) shoveRoutine = StartCoroutine(StumbleRoutine());
        }

        /// <summary>Runs the whole pull-out on its own (no player choreography). Returns false if there is no living driver.</summary>
        public bool Eject()
        {
            if (!BeginPullOut()) return false;
            Shove(ShoveDirection);
            return true;
        }

        private IEnumerator PullOutRoutine()
        {
            // At least one frame after spawning so Invector (vRagdoll) finishes initialising.
            yield return null;
            if (!IsHolding)
            {
                pullRoutine = null;
                yield break;
            }

            Transform seat = vehicle.GetDriverSeat();
            Vector3 targetPosition = GetGroundedPullOutPosition(seat);
            HeldTransform.SetParent(heldPed != null && PedestrianSpawner.Active != null ? PedestrianSpawner.Active.ActiveRoot : null, true);
            if (heldFeet != null) heldFeet.SetGround(targetPosition.y);

            // Length of the pull-out clip (for timing). Read now: a freshly spawned Animator does not report it yet.
            heldAnimator.Play(pulledOutStateHash, 0, 0f);
            heldAnimator.Update(0f);
            float stateLength = heldAnimator.GetCurrentAnimatorStateInfo(0).length;
            pullClipLength = stateLength > 0.1f ? stateLength : 5.8f;
            heldAnimator.Play(seatedStateHash, 0, 0f);
            heldAnimator.Update(0f);

            // 1. The yank: fast, with an upper-body flinch. Root motion is warped in position only (no forced spin).
            heldAnimator.applyRootMotion = true;
            SetFlinch(true);
            heldAnimator.speed = Mathf.Max(0.1f, (yankEndTime - pullStartTime) * pullClipLength / yankDuration);
            // The fixed-time offset is scaled by the animator speed, so it is divided back out.
            heldAnimator.CrossFadeInFixedTime(pulledOutStateHash, pullCrossFade, 0, pullStartTime * pullClipLength / heldAnimator.speed);
            float slowSpeed = Mathf.Max(0.1f, (pullEndTime - yankEndTime) * pullClipLength / stumbleOutDuration);
            bool slowed = false;

            float timer = 0f;
            float timeout = PullDuration + 1.5f;
            while (IsHolding && timer < timeout)
            {
                AnimatorStateInfo info = heldAnimator.GetCurrentAnimatorStateInfo(0);
                bool inPull = info.shortNameHash == pulledOutStateHash;
                if (inPull && info.normalizedTime >= pullEndTime) break;
                if (inPull && !slowed && info.normalizedTime >= yankEndTime)
                {
                    // 2. Stumbling out: slower, the flinch fades.
                    slowed = true;
                    heldAnimator.speed = slowSpeed;
                    SetFlinch(false);
                }
                if (inPull) PullProgress = Mathf.Clamp01((info.normalizedTime - pullStartTime) / Mathf.Max(0.01f, pullEndTime - pullStartTime));
                if (inPull && !heldAnimator.IsInTransition(0))
                {
                    heldAnimator.MatchTarget(targetPosition, Quaternion.identity, AvatarTarget.Root, new MatchTargetWeightMask(Vector3.one, 0f),
                        Mathf.Max(info.normalizedTime, pullStartTime), pullEndTime);
                }
                timer += Time.deltaTime;
                yield return null;
            }

            if (!IsHolding)
            {
                pullRoutine = null;
                yield break;
            }

            heldAnimator.speed = 1f;
            heldAnimator.applyRootMotion = false;
            SetFlinch(false);
            PullProgress = 1f;
            IsPulledOut = true;
            pullRoutine = null;

            // Shoved straight away when the player pushes quickly; otherwise he settles into a dazed stand first.
            float wait = 0f;
            bool standing = false;
            while (!shoveRequested && wait < maxWaitForShove && IsHolding)
            {
                if (!standing && wait >= standAfter)
                {
                    standing = true;
                    heldAnimator.CrossFadeInFixedTime(standingStateHash, 0.2f, 0, 0f);
                }
                wait += Time.deltaTime;
                yield return null;
            }
            if (!shoveRequested) shoveDirection = ShoveDirection;
            if (shoveRoutine == null) shoveRoutine = StartCoroutine(StumbleRoutine());
        }

        private IEnumerator StumbleRoutine()
        {
            if (!IsHolding)
            {
                shoveRoutine = null;
                yield break;
            }

            // Pick the stumble whose travel direction is closest to the shove, then turn only the small remaining angle.
            Transform npcTransform = HeldTransform;
            Vector3 local = Quaternion.Inverse(npcTransform.rotation) * shoveDirection;
            int stateHash;
            Vector3 travelLocal;
            if (Mathf.Abs(local.z) >= Mathf.Abs(local.x))
            {
                stateHash = local.z >= 0f ? stumbleForwardHash : stumbleBackwardHash;
                travelLocal = local.z >= 0f ? Vector3.forward : Vector3.back;
            }
            else
            {
                stateHash = local.x < 0f ? stumbleLeftHash : stumbleRightHash;
                travelLocal = local.x < 0f ? Vector3.left : Vector3.right;
            }
            Vector3 travelWorld = npcTransform.rotation * travelLocal;
            float turn = Vector3.SignedAngle(new Vector3(travelWorld.x, 0f, travelWorld.z), shoveDirection, Vector3.up);
            Quaternion startRotation = npcTransform.rotation;
            Quaternion endRotation = Quaternion.AngleAxis(turn, Vector3.up) * startRotation;

            heldAnimator.applyRootMotion = true;
            heldAnimator.CrossFadeInFixedTime(stateHash, stumbleCrossFade, 0, 0f);
            float timer = 0f;
            while (IsHolding && timer < 3f)
            {
                npcTransform.rotation = Quaternion.Slerp(startRotation, endRotation, Mathf.Clamp01(timer / stumbleTurnDuration));
                AnimatorStateInfo info = heldAnimator.GetCurrentAnimatorStateInfo(0);
                if (info.shortNameHash == stateHash && info.normalizedTime >= fallTime) break;
                timer += Time.deltaTime;
                yield return null;
            }

            shoveRoutine = null;
            if (IsHolding) Fall(shoveDirection);
        }

        private void SetFlinch(bool on)
        {
            if (heldAnimator == null || flinchLayer < 0) return;
            if (on) heldAnimator.Play(flinchStateHash, flinchLayer, 0f);
            heldAnimator.SetLayerWeight(flinchLayer, on ? flinchWeight : 0f);
        }

        private void Fall(Vector3 direction)
        {
            if (heldPed != null)
            {
                FallPedestrian(direction);
                return;
            }
            EnemyVehicleImpact npc = heldNpc;
            heldNpc = null;
            GrabTarget = null;
            IsPulledOut = false;
            PullProgress = 0f;
            if (npc == null) return;
            if (heldFeet != null) heldFeet.Clear();
            heldFeet = null;
            if (heldAnimator != null)
            {
                SetFlinch(false);
                heldAnimator.speed = 1f;
                heldAnimator.applyRootMotion = heldRootMotion;
                heldAnimator.cullingMode = heldCullingMode;
            }
            heldAnimator = null;
            if (npc.transform.parent != null) npc.transform.SetParent(null, true);

            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            Vector3 velocity = direction * fallSpeed + Vector3.up * fallUpSpeed;
            Vector3 tumble = Vector3.Cross(Vector3.up, direction) * fallTumble;
            npc.Launch(velocity, tumble, groundTime, vehicle != null ? vehicle.transform : transform);

            EnemyDummy dummy = npc.GetComponent<EnemyDummy>();
            if (dummy != null) OnDriverEjected?.Invoke(dummy);
        }

        // The pulled-out pedestrian goes back to its own rig, is knocked down along the shove by its own ragdoll
        // (PedestrianDamage) and, once up again, runs from the car like any frightened pedestrian.
        private void FallPedestrian(Vector3 direction)
        {
            PedestrianWalker walker = heldPed;
            Animator animator = heldAnimator;
            heldPed = null;
            heldAnimator = null;
            heldFeet = null;
            GrabTarget = null;
            IsPulledOut = false;
            PullProgress = 0f;
            if (walker == null) return;
            SetFlinchOn(animator, false);
            RestorePedestrianRig(walker, animator);

            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            Vector3 threat = vehicle != null ? vehicle.transform.position : transform.position;
            // Knocked down first, then handed back: PedestrianDamage gets it up and the walker runs once it is on its feet.
            var damage = walker.GetComponent<OpenworldCrime.Pedestrians.PedestrianDamage>();
            if (damage != null)
                damage.KnockDownBy(direction * fallSpeed + Vector3.up * fallUpSpeed, Vector3.Cross(Vector3.up, direction) * fallTumble, groundTime, threat);
            walker.Release(threat);
        }

        private void SetFlinchOn(Animator animator, bool on)
        {
            if (animator == null || flinchLayer < 0 || flinchLayer >= animator.layerCount) return;
            animator.SetLayerWeight(flinchLayer, on ? flinchWeight : 0f);
        }

        private static void ApplyOutfit(GameObject root, Material material)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is SkinnedMeshRenderer || r is MeshRenderer) r.sharedMaterial = material;
            }
        }

        private Vector3 GetGroundedPullOutPosition(Transform seat)
        {
            Vector3 point = ejectPoint != null ? ejectPoint.position : seat.position - vehicle.transform.right * 1.36f;
            return Physics.Raycast(point + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 4f, groundLayers, QueryTriggerInteraction.Ignore)
                ? hit.point
                : point;
        }
    }
}
