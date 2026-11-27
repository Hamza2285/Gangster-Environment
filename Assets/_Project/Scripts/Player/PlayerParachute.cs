using System;
using Invector;
using Invector.vCharacterController;
using OpenworldCrime.Environment;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace OpenworldCrime.Player
{
    /// <summary>What the character has of a parachute.</summary>
    public enum ParachuteState
    {
        /// <summary>No parachute.</summary>
        None,
        /// <summary>The parachute bag is on the back (picked up from a <c>ParachutePickup</c>).</summary>
        Equipped,
        /// <summary>The canopy is open and the character glides down.</summary>
        Gliding
    }

    /// <summary>
    /// Parachute for the playable characters (Jason, Mia; one per character, not a Singleton). Drives the Invector controller
    /// from outside without changing it, like <see cref="PlayerSwimController"/>:
    /// - <see cref="TryEquip"/> (a rooftop pickup) puts the parachute bag on the upper back, sized from the character's own torso;
    ///   each character keeps their own bag through character switches.
    /// - Falling or jumping with the bag at least Min Deploy Height above the ground makes the parachute button available
    ///   (<see cref="OnPlayerDeployAvailableChanged"/>; keyboard key and touch button). Deploying pulls the canopy out of the
    ///   bag: its two risers stay anchored at the top of the bag between the shoulders while the folded pack shoots up out of it
    ///   (tilted back by the fall), the lines stretch and the wing spreads and inflates (Open Duration). The character hangs from
    ///   it (generated humanoid hang clip played over the Animator).
    /// - Gliding: Invector's movement, rotation, gravity and ground check are off (so no fall damage or landing ragdoll) and this
    ///   moves the Rigidbody: a slow descent with forward drift; the move input steers (left / right turn with the canopy and the
    ///   body banking, forward dives faster, back brakes). Inputs, turn rate, bank and speeds are all eased (SmoothDamp), and the
    ///   fall speed blends into the glide over Open Brake Time, so nothing snaps. Weapons are holstered meanwhile; the Invector camera eases out to its
    ///   wide "Parachute" state (Glide Camera State) so the whole canopy is in view, and back on landing.
    /// - Pressing the parachute button again while gliding lets go of the canopy: the bag is used up and the character falls as a
    ///   screaming ragdoll (<see cref="PlayerFreeFall"/>). Falling with the bag before opening shows the stable skydiving pose
    ///   (also PlayerFreeFall).
    /// - Landing on the ground (or the character dying) detaches the canopy, which collapses and fades out, and uses up the bag.
    ///   Reaching the water lets go just above the surface and <see cref="PlayerSwimController"/> takes over.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1002)] // after Invector, SmoothPlayerMotion (1000) and PlayerSwimController (1001)
    public class PlayerParachute : MonoBehaviour
    {
        [Header("References (filled automatically, except Water)")]
        [SerializeField] private vThirdPersonController controller;
        [SerializeField] private vShooterMeleeInput input;
        [SerializeField] private Rigidbody body;
        [SerializeField] private Animator animator;
        [SerializeField] private PlayerWeaponController weaponController;
        [SerializeField] private PlayerVehicleInteraction vehicleInteraction;
        [SerializeField] private PlayerSwimController swimController;
        [SerializeField] private vHeadTrack headTrack;
        [Tooltip("Sea surface: landing in the water hands over to swimming. Optional.")]
        [SerializeField] private WaterManager water;
        [Tooltip("Falls as a ragdoll after letting go of the canopy. Optional (without it letting go is not possible).")]
        [SerializeField] private PlayerFreeFall freeFall;

        [Header("Visuals")]
        [Tooltip("Parachute bag worn on the back (visual only, no colliders).")]
        [SerializeField] private GameObject bagPrefab;
        [Tooltip("Open canopy with its lines; its pivot is where the lines meet (the character's shoulders).")]
        [SerializeField] private GameObject canopyPrefab;
        [Tooltip("Transparent copy of the canopy material, swapped in while the detached canopy fades out.")]
        [SerializeField] private Material canopyFadeMaterial;
        [Tooltip("Hanging pose while gliding (generated humanoid clip, see ParachuteSetup).")]
        [SerializeField] private AnimationClip hangClip;

        [Header("Bag On The Back (per metre of the character's torso, hips to neck)")]
        [Tooltip("Height of the bag.")]
        [SerializeField, Min(0.1f)] private float bagHeightPerTorso = 0.95f;
        [Tooltip("How far behind the upper chest bone the bag's centre sits.")]
        [SerializeField] private float bagBackPerTorso = 0.33f;
        [Tooltip("How far above (+) or below (-) the upper chest bone the bag's centre sits.")]
        [SerializeField] private float bagUpPerTorso = -0.08f;
        [Tooltip("Rotation of the bag model relative to the character (so its straps face the back).")]
        [SerializeField] private Vector3 bagRotation = new Vector3(0f, 90f, 0f);
        [Tooltip("Where the canopy's risers leave the bag, relative to the top centre of the bag (character space, m): " +
                 "x right, y up, z forward.")]
        [SerializeField] private Vector3 riserAnchorOffset = new Vector3(0f, -0.03f, 0.02f);

        [Header("Deploying")]
        [Tooltip("Keyboard key / touch button that opens the parachute.")]
        [SerializeField] private GenericInput deployInput = new GenericInput("B", "", "M_Parachute");
        [Tooltip("The parachute can only be opened this high (m) above the ground or more.")]
        [SerializeField, Min(1f)] private float minDeployHeight = 9f;
        [Tooltip("How often (s) the height above the ground is measured while falling with the bag.")]
        [SerializeField, Min(0.02f)] private float heightCheckInterval = 0.1f;
        [Tooltip("Letting go of the canopy is possible this many seconds after it opened (and at least this high above the ground).")]
        [SerializeField, Min(0f)] private float minReleaseTime = 1f;
        [SerializeField, Min(0f)] private float minReleaseHeight = 6f;
        [Tooltip("Seconds the canopy takes to come out of the bag, stretch its lines and inflate.")]
        [SerializeField, Min(0.05f)] private float openDuration = 0.85f;
        [Tooltip("Backward tilt (deg) of the canopy as it leaves the bag (dragged back by the fall), easing to upright.")]
        [SerializeField, Range(0f, 80f)] private float deployTilt = 45f;
        [Tooltip("Seconds over which the fall is braked down to the glide speed when the canopy opens.")]
        [SerializeField, Min(0.05f)] private float openBrakeTime = 0.9f;

        [Header("Gliding")]
        [Tooltip("Sink speed (m/s) with no input.")]
        [SerializeField, Min(0.5f)] private float descentSpeed = 4.2f;
        [Tooltip("Sink speed (m/s) when diving (move input forward).")]
        [SerializeField, Min(0.5f)] private float diveDescentSpeed = 7.5f;
        [Tooltip("Sink speed (m/s) when braking (move input back).")]
        [SerializeField, Min(0.5f)] private float brakeDescentSpeed = 3f;
        [Tooltip("Forward drift (m/s) with no input.")]
        [SerializeField, Min(0f)] private float forwardSpeed = 7f;
        [Tooltip("Forward speed (m/s) when diving.")]
        [SerializeField, Min(0f)] private float diveForwardSpeed = 12f;
        [Tooltip("Forward speed (m/s) when braking.")]
        [SerializeField, Min(0f)] private float brakeForwardSpeed = 2.5f;
        [Tooltip("How fast the glide speed follows the input (m/s²).")]
        [SerializeField, Min(0.1f)] private float speedResponse = 4f;
        [Tooltip("Turn rate (deg/s) at full left / right input.")]
        [SerializeField, Min(1f)] private float turnSpeed = 75f;
        [Tooltip("Bank (deg) of the canopy and the character at full turn.")]
        [SerializeField, Range(0f, 40f)] private float maxBank = 18f;
        [Tooltip("Seconds the steering / dive / brake inputs take to ease in and out.")]
        [SerializeField, Range(0.01f, 1f)] private float inputSmoothTime = 0.25f;
        [Tooltip("Seconds the turn rate takes to build up / die away (the canopy swings into a turn, it does not snap).")]
        [SerializeField, Range(0.01f, 1.5f)] private float turnSmoothTime = 0.35f;
        [Tooltip("Seconds the bank of the canopy and the body takes to follow the turn.")]
        [SerializeField, Range(0.01f, 1.5f)] private float bankSmoothTime = 0.45f;

        [Header("Landing")]
        [Tooltip("The character lands once the ground is this close (m) under the feet.")]
        [SerializeField, Min(0.05f)] private float landDistance = 0.35f;
        [Tooltip("Lets go of the canopy this high (m) above a water surface, so swimming takes over.")]
        [SerializeField, Min(0f)] private float waterReleaseHeight = 0.6f;
        [Tooltip("Seconds the detached canopy takes to collapse and fade out.")]
        [SerializeField, Min(0.1f)] private float canopyFadeTime = 1.6f;

        [Header("Camera")]
        [Tooltip("Invector camera state used while gliding (in the camera's state list, OWC_ShooterMelee_CameraStates: " +
                 "further back and higher, so the whole canopy is in view). Empty: keep the normal camera.")]
        [SerializeField] private string glideCameraState = "Parachute";

        [Header("Animation")]
        [SerializeField, Min(0.01f)] private float blendTime = 0.3f;

        [Header("Audio")]
        [Tooltip("Wind / cloth flap loop while gliding (optional).")]
        [SerializeField] private AudioClip windLoop;
        [SerializeField, Range(0f, 1f)] private float windVolume = 0.55f;
        [Tooltip("Played when the canopy opens (ParachuteOpening). Played on the character (mostly 2D) so it is heard clearly " +
                 "from the chase camera.")]
        [SerializeField] private AudioClip openClip;
        [SerializeField, Range(0f, 1f)] private float openVolume = 0.8f;

        /// <summary>The played character's parachute changed (picked up, opened, used up).</summary>
        public static event Action<ParachuteState> OnPlayerParachuteChanged;
        /// <summary>The parachute button becomes available (true: falling high enough with the bag) or goes (false).</summary>
        public static event Action<bool> OnPlayerDeployAvailableChanged;
        /// <summary>The parachute button now lets go of the open canopy (true) or no longer does (false).</summary>
        public static event Action<bool> OnPlayerReleaseAvailableChanged;
        /// <summary>This character's parachute changed.</summary>
        public event Action<ParachuteState> OnStateChanged;

        public ParachuteState State { get; private set; }
        public bool HasParachute => State != ParachuteState.None;
        public bool IsGliding => State == ParachuteState.Gliding;
        public bool CanDeploy => deployAvailable;

        private Transform upperChest, hips, neck;
        private GameObject bag;
        private GameObject canopy;
        private Renderer[] canopyRenderers = new Renderer[0];
        private Material[][] canopyMaterials = new Material[0][];
        private MaterialPropertyBlock fadeBlock;
        private Vector3 canopyBaseScale = Vector3.one;
        private Vector3 canopyLocalPosition;
        private Vector3 bagTopLocal;   // top centre of the worn bag, character space (measured when the bag is fitted)
        private Vector3 bagTopChestLocal; // the same point in the upper chest bone's space (follows the free-fall pose)
        private bool hasBagTop;
        private AudioSource windSource;

        private bool deployAvailable;
        private bool releaseAvailable;
        private float nextHeightCheck;
        private bool highEnough;

        // Gliding.
        private float glideTimer;
        private float openVerticalSpeed;
        private float currentForward, forwardVelocity;
        private float bank, bankVelocity;
        private float yaw, yawRate, yawRateVelocity;
        private float steerCmd, steerCmdVelocity, pitchCmd, pitchCmdVelocity;
        private float currentSink, sinkVelocity;
        private bool savedJump, savedRoll, savedCrouch, savedHeadTrack;
        private int savedWeaponSlot = -1;

        // Canopy fade after landing.
        private float fadeTimer = -1f;
        private Vector3 fadeStartScale;
        private Vector3 fadeDrift;

        // Animation.
        private PlayableGraph graph;
        private AnimationPlayableOutput output;
        private float animWeight, animTarget;

        private static readonly RaycastHit[] Hits = new RaycastHit[8];
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private void Reset()
        {
            controller = GetComponent<vThirdPersonController>();
            input = GetComponent<vShooterMeleeInput>();
            body = GetComponent<Rigidbody>();
            animator = GetComponent<Animator>();
            weaponController = GetComponent<PlayerWeaponController>();
            vehicleInteraction = GetComponent<PlayerVehicleInteraction>();
            swimController = GetComponent<PlayerSwimController>();
            headTrack = GetComponent<vHeadTrack>();
        }

        private void Awake()
        {
            if (controller == null || input == null || body == null || animator == null) Reset();
            if (controller == null || input == null || body == null || animator == null)
            {
                Debug.LogError($"[{nameof(PlayerParachute)}] Missing Invector references on '{name}'.", this);
                enabled = false;
                return;
            }
            if (bagPrefab == null || canopyPrefab == null)
                Debug.LogWarning($"[{nameof(PlayerParachute)}] Bag or canopy prefab missing on '{name}'. Run Tools > OpenworldCrime > Parachute > Set Up Parachutes.", this);
            if (animator.isHuman)
            {
                upperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
                if (upperChest == null) upperChest = animator.GetBoneTransform(HumanBodyBones.Chest);
                hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            }
            if (upperChest == null) upperChest = transform;
            fadeBlock = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            Application.onBeforeRender += HandleBeforeRender;
            // The character that becomes the played one reports its own parachute (character switch).
            OnPlayerParachuteChanged?.Invoke(State);
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= HandleBeforeRender;
            // Character switch / death cleanup: give Invector everything back at once, the canopy goes.
            if (State == ParachuteState.Gliding) EndGlide(false, true);
            SetDeployAvailable(false);
            SetReleaseAvailable(false);
            FinishCanopyFade();
            DestroyGraph();
        }

        private void OnDestroy() => DestroyGraph();

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Gives the character a parachute (the bag goes on the back). False when they already have one or cannot take one now
        /// (driving, dead, swimming).
        /// </summary>
        public bool TryEquip()
        {
            if (!isActiveAndEnabled || State != ParachuteState.None || controller.isDead) return false;
            if (vehicleInteraction != null && vehicleInteraction.State != VehicleInteractionState.OnFoot) return false;
            if (swimController != null && swimController.IsInWater) return false;
            ShowBag(true);
            SetState(ParachuteState.Equipped);
            return true;
        }

        /// <summary>Opens the parachute when it is available (the touch button and the key call this).</summary>
        public void Deploy()
        {
            if (!deployAvailable || State != ParachuteState.Equipped) return;
            StartGlide();
        }

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            if (State == ParachuteState.Equipped) UpdateDeployAvailability();
            else if (deployAvailable) SetDeployAvailable(false);

            if (deployAvailable && !GamePlayer.MenuOpen && deployInput.GetButtonDown()) Deploy();
            else if (State == ParachuteState.Gliding) UpdateRelease();
            else if (releaseAvailable) SetReleaseAvailable(false);

            if (fadeTimer >= 0f) UpdateCanopyFade();

            if (graph.IsValid())
            {
                animWeight = Mathf.MoveTowards(animWeight, animTarget, Time.deltaTime / blendTime);
                output.SetWeight(animWeight);
                if (animWeight <= 0f && animTarget <= 0f) DestroyGraph();
            }
        }

        // After the Animator: the hang pose over the controller's, then the bank.
        private void LateUpdate()
        {
            if (graph.IsValid() && animWeight > 0f)
            {
                graph.Evaluate(Time.deltaTime);
                if (hips != null && Mathf.Abs(bank) > 0.01f) hips.rotation = Quaternion.AngleAxis(-bank * 0.6f * animWeight, transform.forward) * hips.rotation;
            }
        }

        // Last thing before the frame renders, after every pose script (the hang pose here, PlayerFreeFall's belly-down pitch
        // after it): the risers are placed on the bag where the final pose has it.
        private void HandleBeforeRender()
        {
            if (State == ParachuteState.Gliding) UpdateCanopyOpening();
        }

        private void FixedUpdate()
        {
            if (State != ParachuteState.Gliding) return;
            if (controller.isDead || controller.ragdolled || body.isKinematic
                || (vehicleInteraction != null && vehicleInteraction.State != VehicleInteractionState.OnFoot))
            {
                EndGlide(false, true);
                return;
            }
            Glide(Time.fixedDeltaTime);
        }

        // ------------------------------------------------------------------ deploy availability

        // Only while carrying the bag in the air: one ray every Height Check Interval.
        private void UpdateDeployAvailability()
        {
            bool airborne = !controller.isGrounded && !controller.isDead && !controller.ragdolled && !body.isKinematic
                            && (vehicleInteraction == null || vehicleInteraction.State == VehicleInteractionState.OnFoot)
                            && (swimController == null || !swimController.IsInWater);
            if (!airborne)
            {
                highEnough = false;
                if (deployAvailable) SetDeployAvailable(false);
                return;
            }
            if (Time.time >= nextHeightCheck)
            {
                nextHeightCheck = Time.time + heightCheckInterval;
                highEnough = HeightAboveGround(body.position, minDeployHeight + 1f) >= minDeployHeight;
            }
            SetDeployAvailable(highEnough);
        }

        // Distance (m) down to the nearest ground or water surface, up to maxDistance.
        private float HeightAboveGround(Vector3 position, float maxDistance)
        {
            float height = maxDistance;
            int count = Physics.RaycastNonAlloc(position + Vector3.up * 0.2f, Vector3.down, Hits, maxDistance + 0.2f,
                controller.groundLayer, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].collider.transform.IsChildOf(transform)) continue;
                height = Mathf.Min(height, Hits[i].distance - 0.2f);
            }
            if (water != null && water.TryGetSurface(position, out float surface)) height = Mathf.Min(height, position.y - surface);
            return height;
        }

        // While gliding (after the canopy is fully open and high enough) the same button lets go of the canopy.
        private void UpdateRelease()
        {
            bool can = freeFall != null && glideTimer >= Mathf.Max(minReleaseTime, openDuration);
            if (can && Time.time >= nextHeightCheck)
            {
                nextHeightCheck = Time.time + heightCheckInterval;
                highEnough = HeightAboveGround(body.position, minReleaseHeight + 1f) >= minReleaseHeight;
            }
            SetReleaseAvailable(can && highEnough);
            if (releaseAvailable && !GamePlayer.MenuOpen && deployInput.GetButtonDown()) Release();
        }

        /// <summary>Lets go of the open canopy: the bag is used up and the character falls as a ragdoll.</summary>
        public void Release()
        {
            if (State != ParachuteState.Gliding || freeFall == null) return;
            SetReleaseAvailable(false);
            Vector3 velocity = body.linearVelocity;
            EndGlide(false, true);
            freeFall.StartRagdollFall(velocity, transform.right * 1.5f, null);
        }

        private void SetReleaseAvailable(bool available)
        {
            if (releaseAvailable == available) return;
            releaseAvailable = available;
            OnPlayerReleaseAvailableChanged?.Invoke(available);
        }

        private void SetDeployAvailable(bool available)
        {
            if (deployAvailable == available) return;
            deployAvailable = available;
            OnPlayerDeployAvailableChanged?.Invoke(available);
        }

        // ------------------------------------------------------------------ gliding

        private void StartGlide()
        {
            SetDeployAvailable(false);
            SetState(ParachuteState.Gliding);
            glideTimer = 0f;
            openVerticalSpeed = Mathf.Min(body.linearVelocity.y, -descentSpeed);
            Vector3 flat = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            currentForward = Mathf.Clamp(Vector3.Dot(flat, transform.forward), brakeForwardSpeed, diveForwardSpeed);
            yaw = transform.eulerAngles.y;
            bank = bankVelocity = 0f;
            yawRate = yawRateVelocity = 0f;
            steerCmd = steerCmdVelocity = pitchCmd = pitchCmdVelocity = 0f;
            forwardVelocity = 0f;
            currentSink = descentSpeed;
            sinkVelocity = 0f;

            controller.isJumping = false;
            controller.isSprinting = false;
            if (controller.isCrouching) controller.Crouch(); // toggles back to standing
            ApplyInvectorLocks();

            savedJump = input.jumpInput.useInput;
            savedRoll = input.rollInput.useInput;
            savedCrouch = input.crouchInput.useInput;
            input.jumpInput.useInput = false;
            input.rollInput.useInput = false;
            input.crouchInput.useInput = false;
            input.SetLockShooterInput(true);
            input.SetLockMeleeInput(true);
            if (weaponController != null)
            {
                savedWeaponSlot = weaponController.HasWeapon ? weaponController.GetCurrentSlotIndex() : -1;
                weaponController.SetWeaponsBlocked(true);
            }
            if (headTrack != null)
            {
                savedHeadTrack = headTrack.enabled;
                headTrack.enabled = false;
            }

            OpenCanopy();
            // Wide chase view (camera further back and higher) so the character and the whole canopy are in view.
            if (!string.IsNullOrEmpty(glideCameraState)) input.ChangeCameraState(glideCameraState, true);
            PlayAnimation(true);
            PlayWind(true);
            PlayOpenSound();
        }

        // Invector writes these back from other places (vehicle unlock, actions): kept on every step while gliding.
        private void ApplyInvectorLocks()
        {
            controller.lockMovement = true;
            controller.lockRotation = true;
            controller.disableCheckGround = true; // grounded as far as Invector knows: no air control, fall damage or landing ragdoll
            controller.verticalVelocity = 0f;
            controller.heightReached = transform.position.y;
            controller.isJumping = false;
            if (!body.isKinematic) body.useGravity = false;
        }

        private void Glide(float dt)
        {
            ApplyInvectorLocks();
            glideTimer += dt;
            Vector3 position = body.position;

            // Water below: let go just above the surface; PlayerSwimController takes over from the splash.
            if (water != null && water.TryGetSurface(position, out float surface) && position.y <= surface + waterReleaseHeight)
            {
                EndGlide(false, true);
                return;
            }
            // Ground under the feet: landed.
            Vector3 velocity = body.linearVelocity;
            float probe = landDistance + Mathf.Max(0f, -velocity.y) * dt;
            if (HeightAboveGround(position, probe + 0.5f) <= probe)
            {
                EndGlide(true, true);
                return;
            }

            // Move input: x turns, z dives (forward) or brakes (back); both eased in and out.
            Vector3 raw = controller.input;
            steerCmd = Mathf.SmoothDamp(steerCmd, Mathf.Clamp(raw.x, -1f, 1f), ref steerCmdVelocity, inputSmoothTime, Mathf.Infinity, dt);
            pitchCmd = Mathf.SmoothDamp(pitchCmd, Mathf.Clamp(raw.z, -1f, 1f), ref pitchCmdVelocity, inputSmoothTime, Mathf.Infinity, dt);
            float steer = steerCmd, pitch = pitchCmd;
            float targetForward = pitch >= 0f ? Mathf.Lerp(forwardSpeed, diveForwardSpeed, pitch) : Mathf.Lerp(forwardSpeed, brakeForwardSpeed, -pitch);
            float targetSink = pitch >= 0f ? Mathf.Lerp(descentSpeed, diveDescentSpeed, pitch) : Mathf.Lerp(descentSpeed, brakeDescentSpeed, -pitch);
            float speedTime = Mathf.Max(0.05f, (diveForwardSpeed - brakeForwardSpeed) / speedResponse * 0.5f);
            currentForward = Mathf.SmoothDamp(currentForward, targetForward, ref forwardVelocity, speedTime, Mathf.Infinity, dt);
            currentSink = Mathf.SmoothDamp(currentSink, targetSink, ref sinkVelocity, speedTime, Mathf.Infinity, dt);

            // Turning: the turn rate builds up and dies away (no snap on changing direction); the bank follows it.
            float open = Mathf.Clamp01(glideTimer / openBrakeTime);
            float control = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(glideTimer / Mathf.Max(0.05f, openDuration))); // no steering until open
            yawRate = Mathf.SmoothDamp(yawRate, steer * turnSpeed * control, ref yawRateVelocity, turnSmoothTime, Mathf.Infinity, dt);
            yaw += yawRate * dt;
            bank = Mathf.SmoothDamp(bank, yawRate / turnSpeed * maxBank, ref bankVelocity, bankSmoothTime, Mathf.Infinity, dt);
            Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
            body.MoveRotation(heading);

            // The opening jolt: the fall speed is braked down to the glide sink speed over Open Brake Time (smooth both ends),
            // and the leftover horizontal fall speed swings round into the glide direction.
            float vy = Mathf.Lerp(openVerticalSpeed, -currentSink, open * open * (3f - 2f * open));
            Vector3 forward = heading * Vector3.forward;
            Vector3 flat = Vector3.ProjectOnPlane(velocity, Vector3.up);
            Vector3 targetFlat = forward * currentForward;
            float flatSharpness = Mathf.Lerp(2.5f, 5f, open);
            flat = Vector3.Lerp(flat, targetFlat, 1f - Mathf.Exp(-flatSharpness * dt));
            body.linearVelocity = new Vector3(flat.x, vy, flat.z);
            body.angularVelocity = Vector3.zero;
        }

        // Landed (true: on the ground, the bag is used up) or interrupted. The canopy always detaches.
        private void EndGlide(bool landed, bool consumeBag)
        {
            if (State != ParachuteState.Gliding) return;
            if (controller != null)
            {
                controller.lockMovement = false;
                controller.lockRotation = false;
                controller.disableCheckGround = false;
                controller.verticalVelocity = 0f;
                controller.heightReached = transform.position.y; // no fall damage for the landing
                controller.isJumping = false;
            }
            if (body != null && !body.isKinematic)
            {
                body.useGravity = true;
                Vector3 v = body.linearVelocity;
                // A soft touchdown: what is left of the forward drift carries into the first steps.
                body.linearVelocity = landed ? new Vector3(v.x * 0.3f, 0f, v.z * 0.3f) : new Vector3(v.x, Mathf.Max(v.y, -descentSpeed), v.z);
            }
            if (input != null)
            {
                input.jumpInput.useInput = savedJump;
                input.rollInput.useInput = savedRoll;
                input.crouchInput.useInput = savedCrouch;
                input.SetLockShooterInput(false);
                input.SetLockMeleeInput(false);
            }
            if (headTrack != null) headTrack.enabled = savedHeadTrack;
            // Back to the normal follow camera (only if nothing else changed the state meanwhile).
            if (input != null && !string.IsNullOrEmpty(glideCameraState) && input.customCameraState == glideCameraState) input.ResetCameraState();
            if (weaponController != null)
            {
                weaponController.SetWeaponsBlocked(false);
                if (landed && savedWeaponSlot >= 0 && isActiveAndEnabled) weaponController.EquipSlot(savedWeaponSlot);
                savedWeaponSlot = -1;
            }
            bank = 0f;
            SetReleaseAvailable(false);
            PlayAnimation(false);
            PlayWind(false);
            DetachCanopy();
            if (consumeBag) ShowBag(false);
            SetState(consumeBag ? ParachuteState.None : ParachuteState.Equipped);
        }

        private void SetState(ParachuteState newState)
        {
            if (State == newState) return;
            State = newState;
            OnStateChanged?.Invoke(newState);
            if (isActiveAndEnabled) OnPlayerParachuteChanged?.Invoke(newState);
        }

        // ------------------------------------------------------------------ bag

        private void ShowBag(bool show)
        {
            if (show && bag == null && bagPrefab != null)
            {
                bag = Instantiate(bagPrefab);
                bag.name = "Parachute Bag";
                foreach (Collider c in bag.GetComponentsInChildren<Collider>(true)) Destroy(c);
                FitBag();
            }
            if (bag != null) bag.SetActive(show);
        }

        // Sized and placed from the character's own skeleton, so it fits Jason and Mia alike.
        private void FitBag()
        {
            float torso = hips != null && neck != null ? Vector3.Distance(hips.position, neck.position) : 0.5f;
            Renderer[] renderers = bag.GetComponentsInChildren<Renderer>(true);
            float modelHeight = 0.45f;
            if (renderers.Length > 0)
            {
                bag.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                bag.transform.localScale = Vector3.one;
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                modelHeight = Mathf.Max(0.05f, bounds.size.y);
            }
            float scale = bagHeightPerTorso * torso / modelHeight;
            Vector3 up = transform.up;
            Vector3 back = -transform.forward;
            Vector3 centre = upperChest.position + back * (bagBackPerTorso * torso) + up * (bagUpPerTorso * torso);
            Quaternion rotation = transform.rotation * Quaternion.Euler(bagRotation);
            bag.transform.localScale = Vector3.one * scale;
            bag.transform.rotation = rotation;
            // Place by the bag's visual centre, not its pivot.
            Vector3 pivotToCentre = Vector3.zero;
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                pivotToCentre = b.center - bag.transform.position;
            }
            bag.transform.position = centre - pivotToCentre;
            // Where the risers come out: the top centre of the bag, in character space (stable, not swaying with the bones).
            Vector3 bagTop = centre + up * (bagHeightPerTorso * torso * 0.5f);
            bagTopLocal = transform.InverseTransformPoint(bagTop);
            bagTopChestLocal = upperChest.InverseTransformPoint(bagTop);
            hasBagTop = true;
            bag.transform.SetParent(upperChest, true);
            SetLayer(bag.transform, upperChest.gameObject.layer);
        }

        private static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) SetLayer(root.GetChild(i), layer);
        }

        // ------------------------------------------------------------------ canopy

        private void OpenCanopy()
        {
            if (canopyPrefab == null) return;
            FinishCanopyFade();
            if (canopy == null)
            {
                canopy = Instantiate(canopyPrefab, transform);
                canopy.name = "Parachute Canopy";
                foreach (Collider c in canopy.GetComponentsInChildren<Collider>(true)) Destroy(c);
                canopyBaseScale = canopy.transform.localScale;
                canopyLocalPosition = canopy.transform.localPosition;
                canopyRenderers = canopy.GetComponentsInChildren<Renderer>(true);
                canopyMaterials = new Material[canopyRenderers.Length][];
                for (int i = 0; i < canopyRenderers.Length; i++) canopyMaterials[i] = canopyRenderers[i].sharedMaterials;
            }
            canopy.transform.SetParent(transform, false);
            // The risers leave the top of the bag between the shoulders of whoever is played (Jason and Mia differ in height).
            Vector3 anchor = hasBagTop ? bagTopLocal
                : upperChest != transform ? transform.InverseTransformPoint(upperChest.position) + new Vector3(0f, 0.15f, -0.18f) : new Vector3(0f, 1.5f, -0.2f);
            canopyLocalPosition = anchor + riserAnchorOffset;
            canopy.transform.localPosition = canopyLocalPosition;
            canopy.transform.localRotation = Quaternion.Euler(-deployTilt, 0f, 0f);
            canopy.transform.localScale = Vector3.Scale(canopyBaseScale, new Vector3(0.04f, 0.02f, 0.04f));
            for (int i = 0; i < canopyRenderers.Length; i++)
            {
                canopyRenderers[i].sharedMaterials = canopyMaterials[i];
                canopyRenderers[i].SetPropertyBlock(null);
            }
            canopy.SetActive(true);
        }

        // Out of the bag: the folded pack shoots up from the bag top (tilted back), the lines stretch to full length, then the
        // wing spreads and inflates with a little overshoot and swings upright; afterwards it follows the bank.
        private void UpdateCanopyOpening()
        {
            if (canopy == null || canopy.transform.parent != transform) return;
            float t = Mathf.Clamp01(glideTimer / openDuration);
            float lines = Mathf.Clamp01(t / 0.5f);
            float up = Mathf.Lerp(0.02f, 1f, 1f - (1f - lines) * (1f - lines) * (1f - lines)); // fast out, eases at full length
            float spread = Mathf.Clamp01((t - 0.25f) / 0.75f);
            float overshoot = 1f + 0.1f * Mathf.Sin(spread * Mathf.PI) * (1f - spread * 0.3f);
            float pack = Mathf.Lerp(0.04f, 0.12f, lines); // the pack opens up a little while the lines stretch
            float wide = Mathf.Lerp(pack, 1f, spread * spread * (3f - 2f * spread)) * overshoot;
            float upright = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.15f) / 0.85f));
            // While the body swings from the free-fall pose into the hang the risers stay on the bag (chest bone), then settle
            // on the steady point between the shoulders (character space) once it is open.
            Vector3 position = canopyLocalPosition;
            if (hasBagTop && upperChest != transform && upright < 1f)
            {
                Vector3 onBag = transform.InverseTransformPoint(upperChest.TransformPoint(bagTopChestLocal)) + riserAnchorOffset;
                position = Vector3.Lerp(onBag, canopyLocalPosition, upright);
            }
            canopy.transform.localPosition = position;
            canopy.transform.localScale = Vector3.Scale(canopyBaseScale, new Vector3(wide, up, wide));
            canopy.transform.localRotation = Quaternion.Euler(-deployTilt * (1f - upright), 0f, -bank);
        }

        private void DetachCanopy()
        {
            if (canopy == null || !canopy.activeSelf) return;
            canopy.transform.SetParent(null, true);
            fadeStartScale = canopy.transform.localScale;
            fadeDrift = transform.forward * 1.5f + Vector3.down * 0.8f;
            fadeTimer = 0f;
            if (canopyFadeMaterial != null)
            {
                for (int i = 0; i < canopyRenderers.Length; i++)
                {
                    var mats = new Material[canopyMaterials[i].Length];
                    for (int m = 0; m < mats.Length; m++) mats[m] = canopyFadeMaterial;
                    canopyRenderers[i].sharedMaterials = mats;
                }
            }
        }

        // The detached canopy drifts on, collapses and fades out, then is kept (hidden) for the next jump.
        private void UpdateCanopyFade()
        {
            if (canopy == null) { fadeTimer = -1f; return; }
            fadeTimer += Time.deltaTime;
            float t = Mathf.Clamp01(fadeTimer / canopyFadeTime);
            canopy.transform.position += fadeDrift * Time.deltaTime * (1f - t);
            canopy.transform.localScale = Vector3.Scale(fadeStartScale, new Vector3(1f + 0.25f * t, Mathf.Lerp(1f, 0.15f, t * t), 1f + 0.25f * t));
            if (canopyFadeMaterial != null)
            {
                Color colour = canopyFadeMaterial.HasProperty(BaseColorId) ? canopyFadeMaterial.GetColor(BaseColorId) : Color.white;
                colour.a = 1f - t;
                fadeBlock.SetColor(BaseColorId, colour);
                for (int i = 0; i < canopyRenderers.Length; i++) canopyRenderers[i].SetPropertyBlock(fadeBlock);
            }
            if (t >= 1f) FinishCanopyFade();
        }

        private void FinishCanopyFade()
        {
            fadeTimer = -1f;
            if (canopy == null) return;
            canopy.SetActive(false);
            canopy.transform.SetParent(transform, false);
        }

        // ------------------------------------------------------------------ audio

        private void PlayWind(bool on)
        {
            if (windLoop == null) return;
            if (windSource == null)
            {
                windSource = gameObject.AddComponent<AudioSource>();
                windSource.clip = windLoop;
                windSource.loop = true;
                windSource.playOnAwake = false;
                windSource.spatialBlend = 0f;
                windSource.volume = windVolume;
            }
            if (on && !windSource.isPlaying) windSource.Play();
            else if (!on && windSource.isPlaying) windSource.Stop();
        }

        private void PlayOpenSound()
        {
            if (openClip == null) return;
            // Through the character's own 2D wind source (created here when there is no wind loop).
            if (windSource == null)
            {
                windSource = gameObject.AddComponent<AudioSource>();
                windSource.loop = true;
                windSource.playOnAwake = false;
                windSource.spatialBlend = 0f;
                windSource.volume = windVolume;
            }
            windSource.PlayOneShot(openClip, openVolume / Mathf.Max(0.01f, windSource.volume));
        }

        // ------------------------------------------------------------------ animation

        private void PlayAnimation(bool on)
        {
            if (on)
            {
                if (!graph.IsValid() && !BuildGraph()) return;
                animTarget = 1f;
                graph.Play();
            }
            else animTarget = 0f;
        }

        private bool BuildGraph()
        {
            if (hangClip == null || animator == null) return false;
            graph = PlayableGraph.Create(name + " Parachute");
            // Evaluated by hand in LateUpdate: the characters' Animator runs in Fixed (Animate Physics) mode (see PlayerSwimController).
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable = AnimationClipPlayable.Create(graph, hangClip);
            playable.SetApplyFootIK(false);
            output = AnimationPlayableOutput.Create(graph, "Parachute", animator);
            output.SetSourcePlayable(playable);
            animWeight = 0f;
            output.SetWeight(0f);
            return true;
        }

        private void DestroyGraph()
        {
            if (graph.IsValid()) graph.Destroy();
            animWeight = 0f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnPlayerParachuteChanged = null;
            OnPlayerDeployAvailableChanged = null;
            OnPlayerReleaseAvailableChanged = null;
        }

#if UNITY_EDITOR
        public void EditorSetup(WaterManager waterManager, GameObject bagVisual, GameObject canopyVisual, Material fadeMaterial,
            AnimationClip hang, AudioClip wind, AudioClip open)
        {
            Reset();
            water = waterManager;
            bagPrefab = bagVisual;
            canopyPrefab = canopyVisual;
            canopyFadeMaterial = fadeMaterial;
            hangClip = hang;
            windLoop = wind;
            openClip = open;
        }

        public void EditorSetFreeFall(PlayerFreeFall fall) => freeFall = fall;
#endif
    }
}
