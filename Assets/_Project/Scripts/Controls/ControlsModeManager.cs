using System;
using Invector.vCharacterController;
using OpenworldCrime.Player;
using UnityEngine;
using UnityStandardAssets.CrossPlatformInput;

namespace OpenworldCrime.Controls
{
    /// <summary>
    /// Owner of the control scheme (lives under ///Manager///, one per scene, not a Singleton).
    /// - Mobile on: Invector reads the on-screen controls (vInput device = Mobile, its virtual input names), RCC uses its
    ///   mobile controller, the on-foot buttons show while walking and the RCC mobile canvas + exit button while driving.
    /// - Mobile off: keyboard / mouse only, no on-screen buttons.
    /// Invector's automatic device switching (vInput.OnGUI) is turned off so this checkbox is the only source of truth.
    /// Toggle <see cref="useMobileControls"/> in the Inspector (also in play mode) or call <see cref="SetMobile"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-10000)]
    public class ControlsModeManager : MonoBehaviour
    {
        [Header("Mode")]
        [Tooltip("On: on-screen touch controls. Off: keyboard and mouse, no on-screen buttons.")]
        [SerializeField] private bool useMobileControls;

        [Header("References")]
        [SerializeField] private OpenworldCrime.Player.PlayerManager playerManager; // qualified: SharedAssets has a global PlayerManager
        [SerializeField] private vThirdPersonInput playerInput;
        [Tooltip("On-foot controls (joystick, look area, action buttons). Shown in mobile mode while not in a vehicle.")]
        [SerializeField] private GameObject onFootControls;
        [Tooltip("Our in-vehicle controls (exit button). Shown in mobile mode while in a vehicle.")]
        [SerializeField] private GameObject vehicleControls;
        [Tooltip("RCC mobile canvas (gas, brake, steering, handbrake, dashboard). Shown in mobile mode while in a vehicle.")]
        [SerializeField] private GameObject rccCanvas;
        [Tooltip("Keyboard-only HUD (e.g. Invector's equip-slot display with key hints). Hidden in mobile mode so it never " +
                 "covers or blocks the touch controls.")]
        [SerializeField] private GameObject[] keyboardOnlyObjects = new GameObject[0];

        /// <summary>Raised after the control mode changed (true = mobile).</summary>
        public static event Action<bool> OnControlsModeChanged;

        public bool IsMobile => useMobileControls;

        private bool applied;
        private bool appliedMobile;
        private bool[] keyboardOnlyStartStates = new bool[0];
#if UNITY_EDITOR
        // RCC keeps the controller type on its shared settings asset; put it back after play mode.
        private int originalRccControllerIndex = -1;
#endif

        private void Awake()
        {
            if (playerManager == null) Debug.LogWarning($"[{nameof(ControlsModeManager)}] No PlayerManager on '{name}'; on-foot / vehicle layouts won't switch.", this);
            if (onFootControls == null || rccCanvas == null) Debug.LogWarning($"[{nameof(ControlsModeManager)}] On-screen control roots are not all assigned on '{name}'.", this);
            // Remember how each keyboard-only HUD started (a canvas may be off by design) so keyboard mode restores exactly that.
            keyboardOnlyStartStates = new bool[keyboardOnlyObjects.Length];
            for (int i = 0; i < keyboardOnlyObjects.Length; i++)
            {
                Canvas canvas = keyboardOnlyObjects[i] != null ? keyboardOnlyObjects[i].GetComponent<Canvas>() : null;
                keyboardOnlyStartStates[i] = canvas == null || canvas.enabled;
            }
#if UNITY_EDITOR
            originalRccControllerIndex = RCC_Settings.Instance.controllerSelectedIndex;
#endif
        }

        private void OnEnable()
        {
            if (playerManager != null) playerManager.OnStateChanged += HandlePlayerStateChanged;
        }

        private void Start() => Apply();

        private void OnDisable()
        {
            if (playerManager != null) playerManager.OnStateChanged -= HandlePlayerStateChanged;
        }

#if UNITY_EDITOR
        private void OnDestroy()
        {
            if (originalRccControllerIndex >= 0 && RCC_Settings.Instance.controllerSelectedIndex != originalRccControllerIndex)
                RCC_SceneManager.SetController(originalRccControllerIndex);
        }

        // Ticking the checkbox in play mode switches immediately.
        private void OnValidate()
        {
            if (Application.isPlaying && applied && appliedMobile != useMobileControls) Apply();
        }
#endif

        private void Update()
        {
            // Presses from the on-screen controls become visible to every script this frame.
            CrossPlatformInputManager.Tick();

            // Unity's UI input module ignores all drags (joystick, look area) while the cursor is locked, and Invector locks it
            // in its own Start and again after menus. Touch controls need it free, so mobile mode keeps it unlocked.
            if (useMobileControls && Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        /// <summary>Switches between on-screen touch controls (true) and keyboard / mouse (false).</summary>
        public void SetMobile(bool mobile)
        {
            if (applied && mobile == useMobileControls) return;
            useMobileControls = mobile;
            Apply();
        }

        private void Apply()
        {
            applied = true;
            appliedMobile = useMobileControls;
            CrossPlatformInputManager.ResetAll();

            vInput input = vInput.instance;
            input.inputDevice = useMobileControls ? InputDevice.Mobile : InputDevice.MouseKeyboard;
            input.enabled = false;

            RCC_SceneManager.SetController(useMobileControls ? 1 : 0);

            if (useMobileControls && playerInput != null)
            {
                playerInput.ShowCursor(true);
                playerInput.LockCursor(true);
            }
            else if (playerInput != null)
            {
                playerInput.ShowCursor(playerInput.showCursorOnStart);
                playerInput.LockCursor(playerInput.unlockCursorOnStart);
            }

            RefreshLayout();
            OnControlsModeChanged?.Invoke(useMobileControls);
        }

        private void HandlePlayerStateChanged(PlayerState state) => RefreshLayout();

        private void RefreshLayout()
        {
            bool inVehicle = playerManager != null && playerManager.IsInVehicle;
            if (onFootControls != null) onFootControls.SetActive(useMobileControls && !inVehicle);
            if (vehicleControls != null) vehicleControls.SetActive(useMobileControls && inVehicle);
            if (rccCanvas != null) rccCanvas.SetActive(useMobileControls && inVehicle);
            for (int i = 0; i < keyboardOnlyObjects.Length; i++)
                if (keyboardOnlyObjects[i] != null) SetHudVisible(keyboardOnlyObjects[i], !useMobileControls && keyboardOnlyStartStates[i]);
        }

        // Third-party HUD keeps running (Invector starts coroutines on these objects), so it is hidden without deactivating:
        // a canvas is switched off, anything else gets a transparent, non-blocking CanvasGroup.
        private static void SetHudVisible(GameObject target, bool visible)
        {
            Canvas canvas = target.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.enabled = visible;
                UnityEngine.UI.GraphicRaycaster raycaster = target.GetComponent<UnityEngine.UI.GraphicRaycaster>();
                if (raycaster != null) raycaster.enabled = visible;
                return;
            }
            CanvasGroup group = target.GetComponent<CanvasGroup>();
            if (group == null)
            {
                if (visible) return;
                group = target.AddComponent<CanvasGroup>();
            }
            group.alpha = visible ? 1f : 0f;
            group.blocksRaycasts = visible;
            group.interactable = visible;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => OnControlsModeChanged = null;
    }
}
