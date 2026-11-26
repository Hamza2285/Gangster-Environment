using System;
using Invector;
using Invector.vCharacterController;
using Invector.vShooter;
using OpenworldCrime.Environment;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Swimming for the playable characters (Jason, Mia; one per character, not a Singleton). The project has no Invector
    /// swimming add-on, so this drives the Invector controller from outside without changing it:
    /// - Deeper than chest height (feet more than Enter Depth under the surface) the character swims: Invector's movement,
    ///   rotation, ground check (so no fall damage / landing ragdoll) and gravity are switched off and this moves the Rigidbody
    ///   itself - floating with the head above the surface, a gentle bob, and the same move input (keyboard or touch joystick,
    ///   Invector's own input) turned into swimming relative to the camera; sprint swims faster.
    ///   A fall or jump into the water plunges in by the impact speed and comes back up (a damped spring, never deeper than Max
    ///   Plunge Depth) - it never walks on the seabed and never sinks.
    /// - Weapons are holstered (and drawn again on land), shooting, aiming, punching, jumping, rolling, crouching, entering
    ///   vehicles and the weapon wheel are blocked while swimming.
    /// - Out of the water: standing up where it gets shallow (Exit Depth) or climbing out over an edge up to Max Climb Height
    ///   above the surface when swimming into it.
    /// - The camera is kept above the surface; a ragdoll that lands in the water floats and is turned back into a swimmer.
    /// - Animation: generated humanoid clips (tread water / breaststroke, see SwimAnimationBuilder) and Invector's ClimbUp, played
    ///   over the Animator Controller through a small PlayableGraph that only exists while in the water.
    /// No drowning (optional later: a breath timer while under the surface).
    /// Costs a few float compares per physics step on land; one raycast per step while swimming (two while pushing forward).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1001)] // after Invector (movement / camera) and SmoothPlayerMotion (1000)
    public class PlayerSwimController : MonoBehaviour
    {
        private enum Mode { Dry, Swimming, Climbing, DeadFloat }

        [Header("References (filled automatically, except Water)")]
        [SerializeField] private WaterManager water;
        [SerializeField] private vThirdPersonController controller;
        [SerializeField] private vShooterMeleeInput input;
        [SerializeField] private Rigidbody body;
        [SerializeField] private CapsuleCollider capsule;
        [SerializeField] private Animator animator;
        [SerializeField] private PlayerWeaponController weaponController;
        [SerializeField] private PlayerVehicleInteraction vehicleInteraction;
        [SerializeField] private vRagdoll ragdoll;
        [SerializeField] private vHeadTrack headTrack;

        [Header("Animation")]
        [SerializeField] private AnimationClip treadClip;
        [SerializeField] private AnimationClip swimClip;
        [SerializeField] private AnimationClip climbClip;
        [SerializeField, Min(0.01f)] private float blendTime = 0.25f;

        [Header("Entering / Leaving")]
        [Tooltip("Swims once the feet are this deep (m) under the surface (about chest height).")]
        [SerializeField, Min(0.5f)] private float enterDepth = 1.25f;
        [Tooltip("Stands up again where the water is shallower than this (m).")]
        [SerializeField, Min(0.3f)] private float exitDepth = 1.1f;
        [Tooltip("A fall into the water splashes from this vertical speed (m/s); slower is a wading ripple.")]
        [SerializeField, Min(0f)] private float minSplashSpeed = 1.2f;

        [Header("Floating")]
        [Tooltip("How high (m) the head floats above the surface. The float depth follows the character's own head height in the " +
                 "swim pose, so a taller and a shorter character (Jason, Mia) both keep the head out by this much.")]
        [SerializeField] private float headAboveSurface = 0.3f;
        [Tooltip("Starting / fallback float depth (m of the character root under the surface) before the head height is known.")]
        [SerializeField, Min(0.5f)] private float floatDepth = 1.3f;
        [SerializeField, Min(0f)] private float bobHeight = 0.045f;
        [SerializeField, Min(0f)] private float bobFrequency = 0.55f;
        [Tooltip("Buoyancy spring (rad/s): higher pops back to the surface faster after a plunge.")]
        [SerializeField, Min(0.5f)] private float buoyancy = 4f;
        [Tooltip("Deepest a plunge goes (m below the floating height).")]
        [SerializeField, Min(0f)] private float maxPlungeDepth = 1.6f;

        [Header("Swimming")]
        [SerializeField, Min(0.1f)] private float swimSpeed = 2.1f;
        [SerializeField, Min(0.1f)] private float fastSwimSpeed = 3.4f;
        [SerializeField, Min(0.1f)] private float acceleration = 3f;
        [Tooltip("Turn speed (deg/s).")]
        [SerializeField, Min(10f)] private float turnSpeed = 220f;
        [Tooltip("Time between ripples while swimming (s), and while wading through shallow water.")]
        [SerializeField, Min(0.1f)] private float rippleInterval = 0.55f;

        [Header("Climbing Out")]
        [Tooltip("Highest edge (m above the surface) that can be climbed out over.")]
        [SerializeField, Min(0.2f)] private float maxClimbHeight = 2.1f;
        [Tooltip("How close (m, from the capsule centre) the edge must be.")]
        [SerializeField, Min(0.3f)] private float climbReach = 0.85f;
        [SerializeField, Min(0.3f)] private float climbDuration = 1.15f;
        [Tooltip("How long (s) to swim into an edge before climbing out.")]
        [SerializeField, Min(0f)] private float climbPushTime = 0.12f;

        [Header("Body Pitch")]
        [Tooltip("Forward lean (deg from vertical) when floating still. The skeleton is tilted at the hips after the animation; " +
                 "the physics capsule stays upright.")]
        [SerializeField, Range(0f, 30f)] private float idlePitch = 10f;
        [Tooltip("Forward lean (deg from vertical) at full swim speed: the legs trail behind and the pelvis rises with speed.")]
        [SerializeField, Range(0f, 80f)] private float swimPitch = 60f;
        [Tooltip("How fast the lean changes (deg/s).")]
        [SerializeField, Min(1f)] private float pitchSpeed = 90f;

        [Header("Camera")]
        [Tooltip("The third-person camera stays at least this high (m) above the surface while swimming.")]
        [SerializeField] private float cameraMinAboveSurface = 0.3f;

        [Header("Ragdoll")]
        [Tooltip("A ragdoll in the water floats this long (s) before it becomes a swimmer again.")]
        [SerializeField, Min(0.1f)] private float ragdollRecoverTime = 1.1f;
        [Tooltip("Upward acceleration (m/s²) on each ragdoll bone fully under water (above 9.81 floats).")]
        [SerializeField, Min(0f)] private float ragdollBuoyancy = 18f;
        [Tooltip("Water drag on each ragdoll bone under the surface (1/s).")]
        [SerializeField, Min(0f)] private float ragdollDrag = 4f;

        /// <summary>The character hit the water from a fall / jump / bail. Args: point on the surface, impact speed (m/s).</summary>
        public static event Action<Vector3, float> OnWaterEntered;
        /// <summary>A swim stroke or a wading step. Args: point on the surface, intensity 0-1.</summary>
        public static event Action<Vector3, float> OnSwimStroke;
        /// <summary>The character climbed or walked out of the water. Arg: point on the surface.</summary>
        public static event Action<Vector3> OnClimbedOut;
        /// <summary>The vehicle the player drives hit the water. Args: point on the surface, speed (m/s).</summary>
        public static event Action<Vector3, float> OnVehicleEnteredWater;
        /// <summary>The played character started (true) or stopped (false) swimming.</summary>
        public static event Action<bool> OnPlayerSwimmingChanged;
        /// <summary>This character started (true) or stopped (false) swimming.</summary>
        public event Action<bool> OnSwimmingChanged;

        /// <summary>Swimming or climbing out (Invector's movement is off).</summary>
        public bool IsSwimming => mode == Mode.Swimming || mode == Mode.Climbing;
        /// <summary>In the water: swimming, climbing out, a ragdoll in the water, or a body floating.</summary>
        public bool IsInWater => mode != Mode.Dry || ragdollInWater;

        /// <summary>
        /// In the water right now: <see cref="IsInWater"/>, or below the surface away from dry ground before swimming has started
        /// (e.g. just put out of a sinking car). For damage checks; a few float compares (one ray only when airborne under water).
        /// </summary>
        public bool IsSubmerged
        {
            get
            {
                if (IsInWater) return true;
                if (!isActiveAndEnabled) return false;
                Vector3 p = body.position;
                return water.TryGetSurface(p, out float surface) && p.y < surface - 0.3f && !IsOnDryGround(p);
            }
        }

        private Mode mode;
        private float surfaceY;
        private bool wasBelowSurface;
        private float lastVerticalSpeed;
        private float nextRipple;
        private float bobPhase;
        private float pushTimer;

        // Saved Invector state while swimming.
        private bool savedJump, savedRoll, savedCrouch;
        private bool savedHeadTrack;
        private int savedWeaponSlot = -1;
        private bool weaponsBlockedBySwim;

        // Climbing out.
        private Vector3 climbStart, climbEnd;
        private float climbTimer;

        // Ragdoll in the water.
        private bool ragdollInWater;
        private float ragdollWaterTime;
        private Rigidbody hipsBody;
        private Rigidbody[] ragdollBodies = new Rigidbody[0];
        private Transform hips;
        private Transform head;
        private float headOffset; // smoothed head height above the root in the swim pose

        // Player's vehicle.
        private float nextVehicleCheck;
        private Rigidbody vehicleBody;
        private bool vehicleWasInWater;

        // Animation.
        private PlayableGraph graph;
        private AnimationPlayableOutput output;
        private AnimationMixerPlayable mixer;
        private AnimationClipPlayable climbPlayable;
        private float animWeight, animTarget, swimBlend;

        private static readonly RaycastHit[] Hits = new RaycastHit[8];
        private readonly System.Collections.Generic.Dictionary<Collider, bool> groundDryCache = new System.Collections.Generic.Dictionary<Collider, bool>();

        private void Reset()
        {
            controller = GetComponent<vThirdPersonController>();
            input = GetComponent<vShooterMeleeInput>();
            body = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            animator = GetComponent<Animator>();
            weaponController = GetComponent<PlayerWeaponController>();
            vehicleInteraction = GetComponent<PlayerVehicleInteraction>();
            ragdoll = GetComponent<vRagdoll>();
            headTrack = GetComponent<vHeadTrack>();
        }

        private void Awake()
        {
            if (controller == null || input == null || body == null || capsule == null || animator == null) Reset();
            if (water == null || controller == null || input == null || body == null || capsule == null || animator == null)
            {
                Debug.LogError($"[{nameof(PlayerSwimController)}] Missing references on '{name}' (WaterManager, Invector controller / " +
                               "input, Rigidbody, CapsuleCollider, Animator). Run Tools > OpenworldCrime > Environment > Set Up Water & Swimming.", this);
                enabled = false;
                return;
            }
            if (treadClip == null || swimClip == null) Debug.LogWarning($"[{nameof(PlayerSwimController)}] Swim clips missing on '{name}': swims in the idle pose.", this);
            if (animator.isHuman)
            {
                hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                hipsBody = hips != null ? hips.GetComponent<Rigidbody>() : null;
            }
            if (hips == null) hips = transform;
            head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            // The ragdoll's bones (every body under the character except its own root body): floated while in the water.
            if (hipsBody != null)
            {
                var bodies = new System.Collections.Generic.List<Rigidbody>(hips.GetComponentsInChildren<Rigidbody>(true));
                bodies.Remove(body);
                ragdollBodies = bodies.ToArray();
            }
        }

        private void OnEnable()
        {
            if (vehicleInteraction != null) vehicleInteraction.OnStateChanged += HandleVehicleState;
            HandleVehicleState(vehicleInteraction != null ? vehicleInteraction.State : VehicleInteractionState.OnFoot);
            wasBelowSurface = false;
        }

        private void OnDisable()
        {
            if (vehicleInteraction != null) vehicleInteraction.OnStateChanged -= HandleVehicleState;
            // Character switch / death cleanup: give Invector everything back at once.
            if (mode != Mode.Dry) LeaveWater(false, false);
            ragdollInWater = false;
            DestroyGraph();
        }

        private void OnDestroy() => DestroyGraph();

        private void HandleVehicleState(VehicleInteractionState state)
        {
            // Only the played character reports (the other one is switched off).
            if (water != null && isActiveAndEnabled) water.SetPlayerDriving(state == VehicleInteractionState.InVehicle);
            vehicleBody = null;
            vehicleWasInWater = false;
        }

        // ------------------------------------------------------------------ physics step

        private void FixedUpdate()
        {
            if (vehicleInteraction != null && IsSeated(vehicleInteraction.State))
            {
                WatchVehicle();
                return;
            }
            if (controller.isDead)
            {
                if (mode == Mode.Swimming || mode == Mode.Climbing) StartDeadFloat();
                if (controller.ragdolled) FloatRagdoll(false);
                else if (mode == Mode.DeadFloat) DeadFloat();
                return;
            }
            if (mode == Mode.DeadFloat) LeaveWater(false, false); // revived (respawn)
            if (controller.ragdolled)
            {
                if (mode != Mode.Dry) LeaveWater(false, true);
                FloatRagdoll(true);
                return;
            }
            ragdollInWater = false;
            switch (mode)
            {
                case Mode.Dry: WatchForWater(); break;
                case Mode.Swimming: Swim(); break;
                case Mode.Climbing: Climb(); break;
            }
        }

        private static bool IsSeated(VehicleInteractionState state) =>
            state == VehicleInteractionState.InVehicle || state == VehicleInteractionState.Entering || state == VehicleInteractionState.Exiting;

        // On land: a few float compares. Splashes when the feet cross the surface, swims when deep enough.
        private void WatchForWater()
        {
            Vector3 position = body.position;
            if (!water.TryGetSurface(position, out surfaceY))
            {
                wasBelowSurface = false;
                return;
            }
            float submersion = surfaceY - position.y;
            bool below = submersion > 0f;
            float verticalSpeed = body.linearVelocity.y;
            if (!below)
            {
                wasBelowSurface = false;
                lastVerticalSpeed = verticalSpeed;
                return;
            }
            bool dry = IsOnDryGround(position);
            if (!wasBelowSurface && !dry)
            {
                // Crossed the surface this step: the speed it hit the water with (contact may already have slowed the body).
                float impact = -Mathf.Min(verticalSpeed, lastVerticalSpeed);
                if (impact >= minSplashSpeed) OnWaterEntered?.Invoke(SurfacePoint(position), impact);
            }
            wasBelowSurface = true;
            lastVerticalSpeed = verticalSpeed;
            if (dry) return;

            if (submersion > enterDepth)
            {
                EnterWater();
                return;
            }
            // Wading: ripples while walking through it.
            if (Time.time >= nextRipple && new Vector2(body.linearVelocity.x, body.linearVelocity.z).sqrMagnitude > 1f)
            {
                nextRipple = Time.time + rippleInterval;
                OnSwimStroke?.Invoke(SurfacePoint(position), 0.4f);
            }
        }

        // Below the water line on dry ground (the sunken underpass roads): Invector's own ground hit, or one ray when airborne.
        private bool IsOnDryGround(Vector3 position)
        {
            // Invector's ground hit when it is really under the feet (right after a teleport it still holds the old ground);
            // the answer is kept per ground collider, so walking costs nothing.
            Collider ground = controller.groundHit.collider;
            if (controller.isGrounded && ground != null && Mathf.Abs(controller.groundHit.point.y - position.y) < 0.5f)
            {
                if (groundDryCache.TryGetValue(ground, out bool known)) return known;
                bool dry = water.IsDryGround(ground) || RayFindsDryGround(position + Vector3.up, 1.6f, out _, out _);
                groundDryCache[ground] = dry;
                return dry;
            }
            return RayFindsDryGround(position + Vector3.up, enterDepth + 3f, out _, out _);
        }

        // One ray down. The nearest ground is returned; it counts as dry when it, or a collider at the same height, belongs to
        // dry ground (the visible road mesh often lies exactly on its dry-ground collider, like WorldBounds handles it).
        private bool RayFindsDryGround(Vector3 origin, float length, out bool hasGround, out RaycastHit nearestHit)
        {
            nearestHit = default;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, length, controller.groundLayer, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].distance >= nearest || Hits[i].collider.transform.IsChildOf(transform)) continue;
                nearest = Hits[i].distance;
                nearestHit = Hits[i];
            }
            hasGround = nearest < float.MaxValue;
            if (!hasGround) return false;
            for (int i = 0; i < count; i++)
                if (Hits[i].distance <= nearest + 0.3f && water.IsDryGround(Hits[i].collider)) return true;
            return false;
        }

        private void EnterWater()
        {
            mode = Mode.Swimming;
            pushTimer = 0f;
            if (headOffset <= 0f) headOffset = floatDepth + headAboveSurface;
            bobPhase = UnityEngine.Random.value * Mathf.PI * 2f;

            controller.isJumping = false;
            controller.isRolling = false;
            if (controller.isCrouching) controller.Crouch(); // toggles back to standing
            ApplyInvectorLocks();
            body.useGravity = false;

            savedJump = input.jumpInput.useInput;
            savedRoll = input.rollInput.useInput;
            savedCrouch = input.crouchInput.useInput;
            input.jumpInput.useInput = false;
            input.rollInput.useInput = false;
            input.crouchInput.useInput = false;
            input.SetLockShooterInput(true);
            input.SetLockMeleeInput(true);

            if (weaponController != null && !weaponsBlockedBySwim)
            {
                // Kept through a ragdoll in the water (it leaves and re-enters): drawn again only on land.
                if (savedWeaponSlot < 0) savedWeaponSlot = weaponController.HasWeapon ? weaponController.GetCurrentSlotIndex() : -1;
                weaponController.SetWeaponsBlocked(true);
                weaponsBlockedBySwim = true;
            }
            if (headTrack != null)
            {
                savedHeadTrack = headTrack.enabled;
                headTrack.enabled = false;
            }

            PlayAnimation(true);
            OnSwimmingChanged?.Invoke(true);
            OnPlayerSwimmingChanged?.Invoke(true);
        }

        // Back to Invector. onLand: walked / climbed out (weapons come back). keepWeaponsAway: a ragdoll in the water.
        private void LeaveWater(bool onLand, bool keepWeaponsAway)
        {
            bool wasSwimming = mode != Mode.Dry;
            // Only undo our own trigger (a ragdoll sets the capsule as trigger itself).
            if (mode == Mode.Climbing && capsule != null) capsule.isTrigger = false;
            mode = Mode.Dry;
            wasBelowSurface = onLand;

            if (controller != null)
            {
                controller.lockMovement = false;
                controller.lockRotation = false;
                controller.disableCheckGround = false;
                controller.verticalVelocity = 0f;
                controller.heightReached = transform.position.y;
            }
            if (body != null && !body.isKinematic) body.useGravity = true;

            if (input != null && wasSwimming)
            {
                input.jumpInput.useInput = savedJump;
                input.rollInput.useInput = savedRoll;
                input.crouchInput.useInput = savedCrouch;
                input.SetLockShooterInput(false);
                input.SetLockMeleeInput(false);
            }
            if (headTrack != null && wasSwimming) headTrack.enabled = savedHeadTrack;

            if (!keepWeaponsAway && weaponController != null && weaponsBlockedBySwim)
            {
                weaponController.SetWeaponsBlocked(false);
                weaponsBlockedBySwim = false;
                if (onLand && savedWeaponSlot >= 0 && isActiveAndEnabled) weaponController.EquipSlot(savedWeaponSlot);
                savedWeaponSlot = -1;
            }

            PlayAnimation(false);
            if (wasSwimming)
            {
                OnSwimmingChanged?.Invoke(false);
                OnPlayerSwimmingChanged?.Invoke(false);
            }
        }

        // Invector writes these back from other places (vehicle unlock, actions): kept on every step while swimming.
        private void ApplyInvectorLocks()
        {
            controller.lockMovement = true;
            controller.lockRotation = true;
            controller.disableCheckGround = true; // grounded as far as Invector knows: no air control, fall damage or landing ragdoll
            controller.verticalVelocity = 0f;
            controller.heightReached = transform.position.y;
            controller.isJumping = false;
            if (!body.isKinematic) body.useGravity = false; // Invector's ragdoll reset turns it back on
            if (!input.lockMeleeInput) input.SetLockMeleeInput(true);
        }

        private void Swim()
        {
            Vector3 position = body.position;
            float depth = CurrentFloatDepth();
            if (!water.TryGetSurface(position, out surfaceY) || position.y > surfaceY - depth + 1.5f)
            {
                // Out of the water some other way (teleport, respawn, switch, thrown out): just walk on.
                LeaveWater(true, false);
                return;
            }
            float floatY = surfaceY - depth;
            ApplyInvectorLocks();
            if (body.isKinematic) return;
            float dt = Time.fixedDeltaTime;
            // Follow the head height of the pose being played (smoothed: the strokes bob the head).
            if (head != null && animWeight > 0.9f)
                headOffset = Mathf.Lerp(headOffset, Mathf.Clamp(head.position.y - position.y, 1.1f, 1.9f), dt * 2f);

            // Ground under the swimmer: shallow enough to stand, or keep the feet on it.
            // (The ray reaches a few metres under the feet so dry ground below the water line - the underpass - is seen.)
            if (RayFindsDryGround(position + Vector3.up * 1.2f, 1.2f + depth + 3f, out bool hasGround, out RaycastHit groundHit))
            {
                // Not water after all: let Invector take over and land on it.
                LeaveWater(true, false);
                return;
            }
            hasGround = hasGround && groundHit.point.y > position.y - 0.25f; // only ground at the feet holds the swimmer up
            if (hasGround && surfaceY - groundHit.point.y < exitDepth)
            {
                body.position = new Vector3(position.x, Mathf.Max(position.y, groundHit.point.y), position.z);
                LeaveWater(true, false);
                OnClimbedOut?.Invoke(SurfacePoint(position));
                return;
            }

            // Move input relative to the camera (Invector's raw input: keyboard or the touch joystick).
            Vector3 move = CameraRelativeInput();
            float speed = controller.isSprinting ? fastSwimSpeed : swimSpeed;
            Vector3 velocity = body.linearVelocity;
            Vector3 flat = Vector3.MoveTowards(new Vector3(velocity.x, 0f, velocity.z), move * speed, acceleration * dt);

            // Float: a damped spring to the surface (critically damped, so a plunge comes back up without bouncing).
            bobPhase += dt * bobFrequency * Mathf.PI * 2f;
            float targetY = floatY + Mathf.Sin(bobPhase) * bobHeight;
            if (hasGround) targetY = Mathf.Max(targetY, groundHit.point.y + 0.02f);
            float vy = velocity.y + (buoyancy * buoyancy * (targetY - position.y) - 2f * buoyancy * velocity.y) * dt;
            if (position.y < floatY - maxPlungeDepth && vy < 0f) vy = 0f;
            body.linearVelocity = new Vector3(flat.x, vy, flat.z);

            if (move.sqrMagnitude > 0.0025f)
            {
                Quaternion facing = Quaternion.LookRotation(move, Vector3.up);
                body.MoveRotation(Quaternion.RotateTowards(body.rotation, facing, turnSpeed * dt));
            }

            float speed01 = Mathf.Clamp01(flat.magnitude / swimSpeed);
            swimBlend = Mathf.MoveTowards(swimBlend, speed01, dt * 2.5f); // tread -> breaststroke (and the lean) by speed

            if (Time.time >= nextRipple)
            {
                nextRipple = Time.time + rippleInterval * (speed01 > 0.15f ? 1f : 2.2f);
                OnSwimStroke?.Invoke(SurfacePoint(position + transform.forward * 0.4f), speed01);
            }

            // Swimming into an edge: climb out over it.
            if (move.sqrMagnitude > 0.16f && Vector3.Dot(move.normalized, transform.forward) > 0.6f && TryFindLedge(out Vector3 top))
            {
                pushTimer += dt;
                if (pushTimer >= climbPushTime) StartClimb(top);
            }
            else pushTimer = 0f;
        }

        private Vector3 CameraRelativeInput()
        {
            Vector3 raw = controller.input;
            raw.y = 0f;
            if (raw.sqrMagnitude < 0.0025f) return Vector3.zero;
            Camera cam = input.cameraMain;
            Vector3 forward = Vector3.forward, right = Vector3.right;
            if (cam != null)
            {
                right = cam.transform.right;
                right.y = 0f;
                right.Normalize();
                forward = Vector3.Cross(right, Vector3.up); // camera forward on the ground plane
            }
            Vector3 move = right * raw.x + forward * raw.z;
            return move.sqrMagnitude > 1f ? move.normalized : move;
        }

        // An edge in front at the surface whose top is walkable, not higher than Max Climb Height, with room to stand.
        private bool TryFindLedge(out Vector3 top)
        {
            top = default;
            Vector3 forward = transform.forward;
            Vector3 origin = new Vector3(body.position.x, surfaceY + 0.15f, body.position.z);
            if (!Physics.Raycast(origin, forward, out RaycastHit wall, climbReach, controller.groundLayer, QueryTriggerInteraction.Ignore)) return false;
            if (wall.normal.y > 0.55f) return false; // a slope: walk out instead
            Vector3 probe = wall.point + forward * (capsule.radius + 0.15f);
            probe.y = surfaceY + maxClimbHeight + 0.3f;
            if (!Physics.Raycast(probe, Vector3.down, out RaycastHit ground, maxClimbHeight + 0.6f, controller.groundLayer, QueryTriggerInteraction.Ignore)) return false;
            if (ground.normal.y < 0.7f || ground.point.y < surfaceY - 0.1f) return false;
            float radius = capsule.radius;
            Vector3 bottom = ground.point + Vector3.up * (radius + 0.08f);
            Vector3 head = ground.point + Vector3.up * Mathf.Max(capsule.height - radius, radius + 0.1f);
            if (Physics.CheckCapsule(bottom, head, radius * 0.9f, controller.groundLayer, QueryTriggerInteraction.Ignore)) return false;
            top = ground.point;
            return true;
        }

        private void StartClimb(Vector3 top)
        {
            mode = Mode.Climbing;
            climbTimer = 0f;
            climbStart = body.position;
            climbEnd = top + Vector3.up * 0.02f;
            capsule.isTrigger = true; // no snagging on the edge on the way up
            Vector3 face = climbEnd - climbStart;
            face.y = 0f;
            if (face.sqrMagnitude > 0.001f) body.MoveRotation(Quaternion.LookRotation(face, Vector3.up));
            if (climbPlayable.IsValid())
            {
                climbPlayable.SetTime(0d);
                climbPlayable.SetSpeed(climbClip.length / climbDuration);
            }
            OnClimbedOut?.Invoke(SurfacePoint(climbStart));
        }

        // Up the wall first, then over the edge; moved by velocity so physics smoothing still applies.
        private void Climb()
        {
            ApplyInvectorLocks();
            if (body.isKinematic) return;
            float dt = Time.fixedDeltaTime;
            climbTimer += dt;
            float t = Mathf.Clamp01(climbTimer / climbDuration);
            float up = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.65f));
            float over = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.45f) / 0.55f));
            Vector3 target = new Vector3(
                Mathf.Lerp(climbStart.x, climbEnd.x, over),
                Mathf.Lerp(climbStart.y, climbEnd.y + 0.08f, up),
                Mathf.Lerp(climbStart.z, climbEnd.z, over));
            body.linearVelocity = (target - body.position) / dt;
            if (t >= 1f)
            {
                body.linearVelocity = Vector3.zero;
                body.position = climbEnd;
                LeaveWater(true, false);
            }
        }

        // Died while swimming: the body keeps floating (no horizontal drive) until the respawn.
        private void StartDeadFloat()
        {
            LeaveWater(false, true);
            mode = Mode.DeadFloat;
            if (body != null && !body.isKinematic) body.useGravity = false;
        }

        private void DeadFloat()
        {
            if (body.isKinematic || !water.TryGetSurface(body.position, out surfaceY)) return;
            Vector3 v = body.linearVelocity;
            float vy = v.y + (buoyancy * buoyancy * (surfaceY - CurrentFloatDepth() - body.position.y) - 2f * buoyancy * v.y) * Time.fixedDeltaTime;
            body.useGravity = false;
            body.linearVelocity = new Vector3(v.x * 0.95f, vy, v.z * 0.95f);
        }

        // A ragdoll (bail, hit, death) in the water floats on it; alive, it becomes a swimmer again after a moment.
        private void FloatRagdoll(bool recover)
        {
            if (hipsBody == null || hipsBody.isKinematic || !water.TryGetSurface(hips.position, out surfaceY))
            {
                ragdollInWater = false;
                return;
            }
            float depth = surfaceY - hips.position.y;
            if (depth <= -0.3f && !ragdollInWater) return;
            if (!ragdollInWater)
            {
                ragdollInWater = true;
                ragdollWaterTime = Time.time;
                float impact = hipsBody.linearVelocity.magnitude;
                if (impact >= minSplashSpeed) OnWaterEntered?.Invoke(SurfacePoint(hips.position), impact);
            }
            for (int i = 0; i < ragdollBodies.Length; i++)
            {
                Rigidbody part = ragdollBodies[i];
                if (part != null && !part.isKinematic) Buoy(part, surfaceY - part.position.y);
            }

            if (recover && ragdoll != null && Time.time - ragdollWaterTime >= ragdollRecoverTime)
            {
                Vector3 at = hips.position;
                ragdoll.RestoreRagdoll();
                ragdollInWater = false;
                if (controller.ragdolled || body.isKinematic) return;
                surfaceY = water.TryGetSurface(at, out float s) ? s : surfaceY;
                body.position = new Vector3(at.x, surfaceY - CurrentFloatDepth(), at.z);
                body.linearVelocity = Vector3.zero;
                wasBelowSurface = true;
                EnterWater();
            }
        }

        // Lift grows with depth up to Ragdoll Buoyancy (more than gravity, so it floats); water drag only under the surface.
        private void Buoy(Rigidbody part, float depth)
        {
            if (depth <= -0.1f) return;
            float lift = Mathf.Clamp01(depth + 0.1f) * ragdollBuoyancy;
            part.AddForce(Vector3.up * lift - part.linearVelocity * ragdollDrag, ForceMode.Acceleration);
        }

        // Driving: one check every 0.1 s for the vehicle hitting the water.
        private void WatchVehicle()
        {
            if (Time.time < nextVehicleCheck) return;
            nextVehicleCheck = Time.time + 0.1f;
            var vehicle = vehicleInteraction.CurrentVehicle as Component;
            if (vehicle == null) return;
            if (vehicleBody == null || vehicleBody.transform != vehicle.transform) vehicleBody = vehicle.GetComponent<Rigidbody>();
            Vector3 position = vehicle.transform.position;
            bool inWater = water.TryGetSurface(position, out float surface) && position.y < surface;
            if (inWater && !vehicleWasInWater)
            {
                float speed = vehicleBody != null ? vehicleBody.linearVelocity.magnitude : 5f;
                OnVehicleEnteredWater?.Invoke(new Vector3(position.x, surface, position.z), speed);
            }
            vehicleWasInWater = inWater;
        }

        private float CurrentFloatDepth() => head != null && headOffset > 0f ? headOffset - headAboveSurface : floatDepth;

        private Vector3 SurfacePoint(Vector3 position) => new Vector3(position.x, surfaceY, position.z);

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            if (!graph.IsValid()) return;
            animWeight = Mathf.MoveTowards(animWeight, animTarget, Time.deltaTime / blendTime);
            output.SetWeight(animWeight);
            bool climbing = mode == Mode.Climbing && climbPlayable.IsValid();
            mixer.SetInputWeight(0, climbing ? 0f : 1f - swimBlend);
            mixer.SetInputWeight(1, climbing ? 0f : swimBlend);
            mixer.SetInputWeight(2, climbing ? 1f : 0f);
            if (animWeight <= 0f && animTarget <= 0f) DestroyGraph();
        }

        // Keeps the camera rig above the water while swimming (after Invector's camera and SmoothPlayerMotion).
        private void LateUpdate()
        {
            // After the Animator (and SmoothPlayerMotion): write the swim pose over the controller's, then lean it.
            if (graph.IsValid() && animWeight > 0f) graph.Evaluate(Time.deltaTime);
            TiltBody();
            if (!IsSwimming || input.tpCamera == null) return;
            Transform rig = input.tpCamera.transform;
            float minY = surfaceY + cameraMinAboveSurface;
            Vector3 p = rig.position;
            if (p.y < minY) rig.position = new Vector3(p.x, minY, p.z);
        }

        // Leans the whole skeleton forward around the hips (Invector drops a clip's root rotation, so the clips stay upright):
        // a little when floating, towards horizontal with speed, upright again for climbing out; faded with the animation weight.
        private float bodyPitch;

        private void TiltBody()
        {
            if (!graph.IsValid() || animWeight <= 0f || hips == transform)
            {
                bodyPitch = 0f;
                return;
            }
            float target = mode == Mode.Swimming ? Mathf.Lerp(idlePitch, swimPitch, swimBlend) : 0f;
            bodyPitch = Mathf.MoveTowards(bodyPitch, target, pitchSpeed * Time.deltaTime);
            float angle = bodyPitch * animWeight;
            if (angle > 0.01f) hips.rotation = Quaternion.AngleAxis(angle, transform.right) * hips.rotation;
        }

        // ------------------------------------------------------------------ animation

        private void PlayAnimation(bool on)
        {
            if (on)
            {
                if (!graph.IsValid() && !BuildGraph()) return;
                swimBlend = 0f;
                animTarget = 1f;
                graph.Play();
            }
            else animTarget = 0f;
        }

        private bool BuildGraph()
        {
            if (treadClip == null || swimClip == null || animator == null) return false;
            graph = PlayableGraph.Create(name + " Swim");
            // Evaluated by hand in LateUpdate: the characters' Animator runs in Fixed (Animate Physics) update mode, where an
            // automatically updated graph output never reaches the bones and Invector's controller pose (fall / sprint) shows instead.
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 3);
            ConnectClip(treadClip, 0);
            ConnectClip(swimClip, 1);
            climbPlayable = climbClip != null ? ConnectClip(climbClip, 2) : default;
            output = AnimationPlayableOutput.Create(graph, "Swim", animator);
            output.SetSourcePlayable(mixer);
            animWeight = 0f;
            output.SetWeight(0f);
            return true;
        }

        private AnimationClipPlayable ConnectClip(AnimationClip clip, int port)
        {
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            graph.Connect(playable, 0, mixer, port);
            return playable;
        }

        private void DestroyGraph()
        {
            if (graph.IsValid()) graph.Destroy();
            animWeight = 0f;
        }

#if UNITY_EDITOR
        public void EditorSetup(WaterManager waterManager, AnimationClip tread, AnimationClip swim, AnimationClip climb)
        {
            Reset();
            water = waterManager;
            treadClip = tread;
            swimClip = swim;
            climbClip = climb;
        }
#endif
    }
}
