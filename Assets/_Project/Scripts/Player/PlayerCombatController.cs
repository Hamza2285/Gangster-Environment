using System;
using Invector.vCharacterController;
using Invector.vShooter;
using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Owns the GTA-style "free when unarmed / strafe when armed or aiming" locomotion policy and
    /// re-exposes Invector's aim / shot / reload events as C# events.
    /// Aiming itself (camera state, IK, aim-strafe) is still handled by Invector's vShooterMeleeInput.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerCombatController : MonoBehaviour
    {
        [Header("Invector References")]
        [SerializeField] private vThirdPersonController controller;
        [SerializeField] private vShooterMeleeInput shooterInput;
        [SerializeField] private vShooterManager shooterManager;

        [Header("Locomotion Policy")]
        [Tooltip("When a shooter weapon is drawn the player strafes (faces camera direction) even when not aiming. " +
                 "Unarmed movement is always free/camera-relative. Sprinting temporarily returns to free movement (Invector 'Sprint Only Free').")]
        [SerializeField] private bool strafeWhileArmed = true;

        /// <summary>Raised when a shooter weapon is drawn (true) or holstered/removed (false).</summary>
        public event Action<bool> OnArmedChanged;
        /// <summary>Raised when aiming starts (true) or stops (false).</summary>
        public event Action<bool> OnAimChanged;
        public event Action<vShooterWeapon> OnShot;
        public event Action<vShooterWeapon> OnReloadStarted;
        public event Action<vShooterWeapon> OnReloadFinished;

        public bool IsArmed { get; private set; }
        /// <summary>True only while the player is aiming with the Aim input (not during hip fire).</summary>
        public bool IsAiming => shooterInput != null && shooterInput.isAimingByInput;
        /// <summary>
        /// True while Invector's hip fire (vShooterManager > HipFire) holds the weapon up after shooting without Aim.
        /// The hold/crosshair time is vShooterManager.hipfireAimTime + the weapon's shootFrequency.
        /// </summary>
        public bool IsHipFiring => shooterInput != null && !shooterInput.isAimingByInput && shooterInput.IsAiming;
        public bool IsReloading => shooterInput != null && shooterInput.isReloading;

        private bool strafeLockedByPolicy;

        private void Awake()
        {
            if (controller == null || shooterInput == null || shooterManager == null)
            {
                Debug.LogError($"[{nameof(PlayerCombatController)}] Missing Invector references on '{name}' " +
                               "(vThirdPersonController, vShooterMeleeInput, vShooterManager).", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (shooterManager == null) return;
            shooterManager.onEnableAim.AddListener(HandleAimEnabled);
            shooterManager.onDisableAim.AddListener(HandleAimDisabled);
            shooterManager.onShot.AddListener(HandleShot);
            shooterManager.onStartReloadWeapon.AddListener(HandleReloadStarted);
            shooterManager.onFinishReloadWeapon.AddListener(HandleReloadFinished);
        }

        private void OnDisable()
        {
            if (shooterManager == null) return;
            shooterManager.onEnableAim.RemoveListener(HandleAimEnabled);
            shooterManager.onDisableAim.RemoveListener(HandleAimDisabled);
            shooterManager.onShot.RemoveListener(HandleShot);
            shooterManager.onStartReloadWeapon.RemoveListener(HandleReloadStarted);
            shooterManager.onFinishReloadWeapon.RemoveListener(HandleReloadFinished);

            ReleaseStrafeLock();
        }

        // LateUpdate so Invector's input (Update) has already processed equip/aim for this frame.
        // Invector has no draw/holster event, so "armed" is a single cached bool check per frame.
        private void LateUpdate()
        {
            bool armed = shooterManager.CurrentActiveWeapon != null && !controller.isDead;
            if (armed != IsArmed)
            {
                IsArmed = armed;
                ApplyStrafePolicy();
                OnArmedChanged?.Invoke(armed);
            }

            // Invector drops strafe while equipping/reloading; restore it afterwards while still armed.
            if (strafeLockedByPolicy && !controller.isStrafing && !controller.isSprinting
                && !shooterInput.isReloading && !shooterInput.isEquipping)
            {
                controller.isStrafing = true;
            }
        }

        /// <summary>Enable/disable the armed-strafe policy at runtime (e.g. for cutscenes or specific weapons).</summary>
        public void SetStrafeWhileArmed(bool value)
        {
            strafeWhileArmed = value;
            ApplyStrafePolicy();
        }

        /// <summary>
        /// Locks/unlocks camera look, shooting/aiming and melee input while keeping locomotion
        /// (used while the weapon wheel is open). Cancels aiming when locking.
        /// </summary>
        public void SetCombatInputLocked(bool locked)
        {
            if (shooterInput == null) return;
            shooterInput.SetLockCameraInput(locked);
            shooterInput.SetLockShooterInput(locked);
            shooterInput.SetLockMeleeInput(locked);
        }

        private void ApplyStrafePolicy()
        {
            if (strafeWhileArmed && IsArmed)
            {
                shooterInput.SetStrafeLocomotion(true);
                strafeLockedByPolicy = true;
            }
            else
            {
                ReleaseStrafeLock();
            }
        }

        private void ReleaseStrafeLock()
        {
            if (!strafeLockedByPolicy || shooterInput == null) return;
            shooterInput.SetStrafeLocomotion(false);
            strafeLockedByPolicy = false;
        }

        private void HandleAimEnabled(vShooterWeapon weapon) => OnAimChanged?.Invoke(true);
        private void HandleAimDisabled(vShooterWeapon weapon) => OnAimChanged?.Invoke(false);
        private void HandleShot(vShooterWeapon weapon) => OnShot?.Invoke(weapon);
        private void HandleReloadStarted(vShooterWeapon weapon) => OnReloadStarted?.Invoke(weapon);
        private void HandleReloadFinished(vShooterWeapon weapon) => OnReloadFinished?.Invoke(weapon);
    }
}
