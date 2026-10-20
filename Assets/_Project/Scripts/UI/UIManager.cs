using Invector.vCharacterController;
using Invector.vShooter;
using OpenworldCrime.Player;
using OpenworldCrime.Vehicles;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.UI
{
    /// <summary>
    /// Scene UI owner (lives under ///Manager///, not a Singleton). Opens/closes panels, keeps the
    /// HUD (health, weapon, ammo) up to date by listening to player events, handles the
    /// hold-to-open weapon wheel and shows the vehicle prompt/messages and on-foot HUD from
    /// <see cref="PlayerVehicleInteraction"/> events. Equipping is delegated to <see cref="PlayerWeaponController"/>,
    /// which uses Invector's inventory equip API.
    /// </summary>
    [DisallowMultipleComponent]
    public class UIManager : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField] private PlayerManager playerManager;
        [SerializeField] private PlayerWeaponController weaponController;

        [Header("HUD")]
        [SerializeField] private GameObject hudPanel;
        [Tooltip("Left-pivoted bar image; its X scale is set to the health fraction.")]
        [SerializeField] private RectTransform healthBarFill;
        [SerializeField] private Text healthText;
        [SerializeField] private GameObject weaponInfoGroup;
        [SerializeField] private Text weaponNameText;
        [SerializeField] private Text ammoText;

        [Header("Weapon Wheel")]
        [SerializeField] private GameObject weaponWheelPanel;
        [SerializeField] private WeaponWheelView weaponWheelView;
        [Tooltip("Hold to open, release to equip. Default: Left Alt / gamepad D-Pad Up (Tab and LB are already used by Invector).")]
        [SerializeField] private GenericInput weaponWheelInput = new GenericInput("LeftAlt", false, false, "D-Pad Vertical", true, false, "D-Pad Vertical", true, false);
        [SerializeField] private GenericInput wheelCursorX = new GenericInput("Mouse X", "RightAnalogHorizontal", "Mouse X");
        [SerializeField] private GenericInput wheelCursorY = new GenericInput("Mouse Y", "RightAnalogVertical", "Mouse Y");
        [Tooltip("Mouse delta to wheel-cursor scale.")]
        [SerializeField, Min(0.001f)] private float mouseSensitivity = 0.08f;
        [Tooltip("Slow motion while the wheel is open (1 = off).")]
        [SerializeField, Range(0.05f, 1f)] private float wheelTimeScale = 0.3f;

        [Header("Vehicle")]
        [SerializeField] private PlayerVehicleInteraction vehicleInteraction;
        [Tooltip("Shown while a vehicle can be entered.")]
        [SerializeField] private GameObject vehiclePromptPanel;
        [SerializeField] private Text vehiclePromptText;
        [SerializeField] private string enterVehiclePrompt = "Enter Vehicle  [F]";
        [Tooltip("Shown instead when a living driver sits at the wheel.")]
        [SerializeField] private string stealVehiclePrompt = "Steal Vehicle  [F]";
        [Tooltip("Short feedback message (e.g. exit refused).")]
        [SerializeField] private GameObject vehicleMessagePanel;
        [SerializeField] private Text vehicleMessageText;
        [SerializeField] private string vehicleMovingMessage = "Stop the vehicle to get out";
        [SerializeField] private string exitBlockedMessage = "No room to get out";
        [SerializeField, Min(0.1f)] private float vehicleMessageDuration = 2f;
        [Tooltip("On-foot HUD objects hidden while in a vehicle (e.g. Invector HUD and aim canvas). The weapon info group is hidden automatically.")]
        [SerializeField] private GameObject[] onFootHudObjects = new GameObject[0];

        public bool IsWeaponWheelOpen { get; private set; }

        private Vector2 wheelCursor;
        private float timeScaleBeforeWheel = 1f;
        private float fixedDeltaTimeBeforeWheel;
        private bool onFootHudHidden;
        private bool[] onFootHudWasActive;
        private float vehicleMessageTimer;

        private void Awake()
        {
            if (playerManager == null || weaponController == null || weaponWheelPanel == null || weaponWheelView == null)
            {
                Debug.LogError($"[{nameof(UIManager)}] Missing references on '{name}' (PlayerManager, PlayerWeaponController, weapon wheel panel/view).", this);
                enabled = false;
                return;
            }
            weaponWheelPanel.SetActive(false);

            onFootHudWasActive = new bool[onFootHudObjects.Length];
            if (vehiclePromptPanel != null) vehiclePromptPanel.SetActive(false);
            if (vehicleMessagePanel != null) vehicleMessagePanel.SetActive(false);
            if (vehicleInteraction == null)
            {
                Debug.LogWarning($"[{nameof(UIManager)}] No PlayerVehicleInteraction assigned on '{name}'; vehicle prompt/HUD switching is disabled.", this);
            }
        }

        private void OnEnable()
        {
            if (!enabled) return;
            PlayerHealth health = playerManager.Health;
            if (health != null)
            {
                health.OnHealthChanged += HandleHealthChanged;
                health.OnHealthReset += RefreshHealth;
                health.OnDied += HandlePlayerDied;
            }
            weaponController.OnWeaponEquipped += HandleWeaponChanged;
            weaponController.OnWeaponUnequipped += HandleWeaponChanged;
            weaponController.OnAmmoChanged += HandleAmmoChanged;

            if (vehicleInteraction != null)
            {
                vehicleInteraction.OnVehicleFocusChanged += HandleVehicleFocusChanged;
                vehicleInteraction.OnVehicleEnterStarted += HandleVehicleEnterStarted;
                vehicleInteraction.OnVehicleEntered += HandleVehicleEntered;
                vehicleInteraction.OnVehicleExited += HandleVehicleExited;
                vehicleInteraction.OnExitBlocked += HandleVehicleExitBlocked;
            }
        }

        private void OnDisable()
        {
            if (playerManager != null && playerManager.Health != null)
            {
                PlayerHealth health = playerManager.Health;
                health.OnHealthChanged -= HandleHealthChanged;
                health.OnHealthReset -= RefreshHealth;
                health.OnDied -= HandlePlayerDied;
            }
            if (weaponController != null)
            {
                weaponController.OnWeaponEquipped -= HandleWeaponChanged;
                weaponController.OnWeaponUnequipped -= HandleWeaponChanged;
                weaponController.OnAmmoChanged -= HandleAmmoChanged;
            }
            if (vehicleInteraction != null)
            {
                vehicleInteraction.OnVehicleFocusChanged -= HandleVehicleFocusChanged;
                vehicleInteraction.OnVehicleEnterStarted -= HandleVehicleEnterStarted;
                vehicleInteraction.OnVehicleEntered -= HandleVehicleEntered;
                vehicleInteraction.OnVehicleExited -= HandleVehicleExited;
                vehicleInteraction.OnExitBlocked -= HandleVehicleExitBlocked;
            }
            if (IsWeaponWheelOpen) CloseWeaponWheel(false);
        }

        private void Start()
        {
            if (hudPanel != null) hudPanel.SetActive(true);
            RefreshHealth();
            RefreshWeaponInfo();
        }

        // Per-frame only for the hold-to-open wheel input/cursor and the vehicle message timer.
        private void Update()
        {
            if (vehicleMessageTimer > 0f)
            {
                vehicleMessageTimer -= Time.unscaledDeltaTime;
                if (vehicleMessageTimer <= 0f) HideVehicleMessage();
            }

            if (!IsWeaponWheelOpen)
            {
                if (weaponWheelInput.GetButtonDown() && CanOpenWeaponWheel()) OpenWeaponWheel();
                return;
            }

            if (!weaponWheelInput.GetButton() || !CanOpenWeaponWheel())
            {
                CloseWeaponWheel(CanOpenWeaponWheel());
                return;
            }

            UpdateWheelCursor();
            weaponWheelView.UpdateSelection(wheelCursor);
        }

        private bool CanOpenWeaponWheel()
        {
            return !playerManager.IsInputLocked
                && playerManager.CurrentState != PlayerState.Dead
                && weaponController.CanChangeWeapon;
        }

        private void OpenWeaponWheel()
        {
            IsWeaponWheelOpen = true;
            wheelCursor = Vector2.zero;

            var slots = weaponController.WeaponSlots;
            for (int i = 0; i < weaponWheelView.SlotCount; i++)
            {
                bool hasSlot = i < slots.Count;
                weaponWheelView.SetSlot(i, hasSlot ? slots[i].DisplayName : string.Empty, hasSlot && weaponController.IsSlotAvailable(i));
            }
            weaponWheelView.SetHighlighted(weaponController.GetCurrentSlotIndex());
            weaponWheelView.UpdateSelection(wheelCursor);
            weaponWheelPanel.SetActive(true);

            if (playerManager.Combat != null) playerManager.Combat.SetCombatInputLocked(true);

            timeScaleBeforeWheel = Time.timeScale;
            fixedDeltaTimeBeforeWheel = Time.fixedDeltaTime;
            if (wheelTimeScale < 1f)
            {
                Time.timeScale = timeScaleBeforeWheel * wheelTimeScale;
                Time.fixedDeltaTime = fixedDeltaTimeBeforeWheel * wheelTimeScale;
            }
        }

        private void CloseWeaponWheel(bool equipSelection)
        {
            IsWeaponWheelOpen = false;
            weaponWheelPanel.SetActive(false);

            Time.timeScale = timeScaleBeforeWheel;
            Time.fixedDeltaTime = fixedDeltaTimeBeforeWheel;
            if (playerManager != null && playerManager.Combat != null) playerManager.Combat.SetCombatInputLocked(false);

            if (equipSelection && weaponWheelView.HighlightedIndex >= 0 && weaponWheelView.HighlightedIndex != weaponController.GetCurrentSlotIndex())
            {
                weaponController.EquipSlot(weaponWheelView.HighlightedIndex);
            }
        }

        private void UpdateWheelCursor()
        {
            Vector2 input = new Vector2(wheelCursorX.GetAxisRaw(), wheelCursorY.GetAxisRaw());
            if (vInput.instance.inputDevice == InputDevice.Joystick)
            {
                wheelCursor = Vector2.ClampMagnitude(input, 1f);
            }
            else
            {
                wheelCursor = Vector2.ClampMagnitude(wheelCursor + input * mouseSensitivity, 1f);
            }
        }

        private void HandleHealthChanged(float current, float max) => SetHealth(current, max);

        private void RefreshHealth()
        {
            PlayerHealth health = playerManager.Health;
            if (health != null) SetHealth(health.CurrentHealth, health.MaxHealth);
        }

        private void SetHealth(float current, float max)
        {
            float fraction = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            if (healthBarFill != null) healthBarFill.localScale = new Vector3(fraction, 1f, 1f);
            if (healthText != null) healthText.text = Mathf.CeilToInt(current).ToString();
        }

        private void HandlePlayerDied()
        {
            if (IsWeaponWheelOpen) CloseWeaponWheel(false);
            RefreshHealth();
        }

        private void HandleWeaponChanged(vShooterWeapon weapon, bool isLeftHand) => RefreshWeaponInfo();

        private void HandleAmmoChanged(int ammoInClip, int extraAmmo) => RefreshWeaponInfo();

        private void RefreshWeaponInfo()
        {
            bool showWeapon = weaponController.HasWeapon && !onFootHudHidden;
            if (weaponInfoGroup != null) weaponInfoGroup.SetActive(showWeapon);
            if (!showWeapon) return;

            if (weaponNameText != null) weaponNameText.text = weaponController.GetCurrentSlotName();
            if (ammoText != null) ammoText.text = $"{weaponController.AmmoInClip} / {weaponController.ExtraAmmo}";
        }

        private void HandleVehicleFocusChanged(IVehicleInteractionTarget vehicle)
        {
            if (vehiclePromptPanel == null) return;
            bool show = vehicle != null;
            if (show && vehiclePromptText != null)
                vehiclePromptText.text = vehicle.EntryPrompt == VehicleEntryPrompt.Steal ? stealVehiclePrompt : enterVehiclePrompt;
            vehiclePromptPanel.SetActive(show);
        }

        private void HandleVehicleEnterStarted(IVehicleController vehicle)
        {
            if (vehiclePromptPanel != null) vehiclePromptPanel.SetActive(false);
            if (IsWeaponWheelOpen) CloseWeaponWheel(false);
        }

        private void HandleVehicleEntered(IVehicleController vehicle) => SetOnFootHudVisible(false);

        private void HandleVehicleExited(IVehicleController vehicle)
        {
            HideVehicleMessage();
            SetOnFootHudVisible(true);
        }

        private void HandleVehicleExitBlocked(VehicleExitBlockReason reason)
        {
            ShowVehicleMessage(reason == VehicleExitBlockReason.VehicleMoving ? vehicleMovingMessage : exitBlockedMessage);
        }

        private void SetOnFootHudVisible(bool visible)
        {
            if (onFootHudHidden == !visible) return;
            onFootHudHidden = !visible;

            for (int i = 0; i < onFootHudObjects.Length; i++)
            {
                GameObject hudObject = onFootHudObjects[i];
                if (hudObject == null) continue;
                if (visible)
                {
                    hudObject.SetActive(onFootHudWasActive[i]);
                }
                else
                {
                    onFootHudWasActive[i] = hudObject.activeSelf;
                    hudObject.SetActive(false);
                }
            }
            RefreshWeaponInfo();
        }

        private void ShowVehicleMessage(string message)
        {
            if (vehicleMessagePanel == null) return;
            if (vehicleMessageText != null) vehicleMessageText.text = message;
            vehicleMessagePanel.SetActive(true);
            vehicleMessageTimer = vehicleMessageDuration;
        }

        private void HideVehicleMessage()
        {
            vehicleMessageTimer = 0f;
            if (vehicleMessagePanel != null) vehicleMessagePanel.SetActive(false);
        }
    }
}
