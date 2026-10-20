using System;
using Invector;
using Invector.vCharacterController;
using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Per-character hub for the player (lives on the player root, NOT a Singleton and NOT under ///Manager///).
    /// Evaluates the high-level <see cref="PlayerState"/> from Invector's controller and the player
    /// components, and raises <see cref="OnStateChanged"/> only when the state actually changes.
    /// Movement, jumping, camera and animation stay owned by Invector.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerManager : MonoBehaviour
    {
        [Header("Invector References")]
        [SerializeField] private vThirdPersonController controller;
        [SerializeField] private vThirdPersonInput input;
        [SerializeField] private Rigidbody body;

        [Header("Player Components")]
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private PlayerCombatController combatController;

        [Header("State Evaluation")]
        [Tooltip("Invector input magnitude above which the player counts as moving.")]
        [SerializeField, Range(0.01f, 0.5f)] private float moveThreshold = 0.1f;
        [Tooltip("Invector input magnitude at/above which movement counts as Run (walk = 0.5, run = 1, sprint = 1.5).")]
        [SerializeField, Range(0.5f, 1f)] private float runThreshold = 0.75f;
        [Tooltip("How long the Hit state is held after receiving damage.")]
        [SerializeField, Min(0f)] private float hitStateDuration = 0.4f;
        [Tooltip("How long the Shoot state is held after a shot.")]
        [SerializeField, Min(0f)] private float shootStateDuration = 0.15f;
        [Tooltip("Time a jump counts as Jump while the character is still leaving the ground (Invector jump anticipation).")]
        [SerializeField, Min(0f)] private float jumpStartGrace = 0.4f;

        /// <summary>Raised whenever the player state changes. Arg: the new state.</summary>
        public event Action<PlayerState> OnStateChanged;
        /// <summary>Raised when player input is locked (true) or unlocked (false), e.g. entering a vehicle.</summary>
        public event Action<bool> OnInputLockChanged;

        public PlayerState CurrentState { get; private set; } = PlayerState.Idle;
        public PlayerState PreviousState { get; private set; } = PlayerState.Idle;
        public bool IsInputLocked { get; private set; }
        /// <summary>True from the moment entering a vehicle starts until exiting has finished.</summary>
        public bool IsInVehicle { get; private set; }

        public vThirdPersonController Controller => controller;
        public PlayerHealth Health => playerHealth;
        public PlayerCombatController Combat => combatController;

        private float hitTimer;
        private float shootTimer;
        private float jumpStartTimer;

        private void Awake()
        {
            if (controller == null || input == null || body == null || playerHealth == null || combatController == null)
            {
                Debug.LogError($"[{nameof(PlayerManager)}] Missing references on '{name}'. Assign vThirdPersonController, " +
                               "vThirdPersonInput (vShooterMeleeInput), Rigidbody, PlayerHealth and PlayerCombatController.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (!enabled) return;
            playerHealth.OnDamaged += HandleDamaged;
            combatController.OnShot += HandleShot;
            controller.OnJump.AddListener(HandleJump);
            controller.OnExitGround.AddListener(HandleExitGround);
        }

        private void OnDisable()
        {
            if (playerHealth != null) playerHealth.OnDamaged -= HandleDamaged;
            if (combatController != null) combatController.OnShot -= HandleShot;
            if (controller != null)
            {
                controller.OnJump.RemoveListener(HandleJump);
                controller.OnExitGround.RemoveListener(HandleExitGround);
            }
        }

        // State depends on continuous locomotion values (input magnitude, grounded, velocity) that Invector
        // does not report through events, so it is evaluated once per frame; listeners are only notified on change.
        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (hitTimer > 0f) hitTimer -= deltaTime;
            if (shootTimer > 0f) shootTimer -= deltaTime;
            if (jumpStartTimer > 0f) jumpStartTimer -= deltaTime;

            SetState(EvaluateState());
        }

        /// <summary>
        /// Locks/unlocks all player input (used by vehicles, cutscenes, menus). Stops the character when locking.
        /// </summary>
        public void SetInputLocked(bool locked)
        {
            if (IsInputLocked == locked) return;
            IsInputLocked = locked;
            input.SetLockAllInput(locked);
            if (locked)
            {
                controller.input = Vector3.zero;
                controller.isSprinting = false;
                controller.StopCharacter();
            }
            OnInputLockChanged?.Invoke(locked);
        }

        /// <summary>Marks the player as getting in / driving / getting out of a vehicle (set by PlayerVehicleInteraction).</summary>
        public void SetInVehicle(bool inVehicle)
        {
            IsInVehicle = inVehicle;
        }

        private PlayerState EvaluateState()
        {
            if (controller.isDead) return PlayerState.Dead;
            if (IsInVehicle) return PlayerState.InVehicle;
            if (hitTimer > 0f) return PlayerState.Hit;
            if (combatController.IsReloading) return PlayerState.Reload;
            if (shootTimer > 0f) return PlayerState.Shoot;

            if (!controller.isGrounded)
            {
                return jumpStartTimer > 0f || body.linearVelocity.y > 0.1f ? PlayerState.Jump : PlayerState.Fall;
            }
            if (jumpStartTimer > 0f) return PlayerState.Jump;

            if (combatController.IsAiming) return PlayerState.Aim;

            bool isMoving = controller.inputMagnitude > moveThreshold;
            if (!isMoving) return PlayerState.Idle;
            if (controller.isStrafing) return PlayerState.Strafe;
            if (controller.isSprinting) return PlayerState.Sprint;
            return controller.inputMagnitude >= runThreshold ? PlayerState.Run : PlayerState.Walk;
        }

        private void SetState(PlayerState newState)
        {
            if (newState == CurrentState) return;
            PreviousState = CurrentState;
            CurrentState = newState;
            OnStateChanged?.Invoke(newState);
        }

        private void HandleDamaged(vDamage damage) => hitTimer = hitStateDuration;
        private void HandleShot(Invector.vShooter.vShooterWeapon weapon) => shootTimer = shootStateDuration;
        private void HandleJump() => jumpStartTimer = jumpStartGrace;
        private void HandleExitGround() => jumpStartTimer = 0f;
    }
}
