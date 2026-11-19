using Invector.vCharacterController;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.Club
{
    /// <summary>
    /// Club HUD (club scene canvas; visuals and input only): while the player is seated it shows Throw Cash and Stand Up
    /// (or Leave during a private dance, with the time left), plus short messages such as "Not enough cash".
    /// Buttons work on touch; keys work on keyboard / gamepad. The systems own the state and raise the events this view follows.
    /// </summary>
    [DisallowMultipleComponent]
    public class ClubHudView : MonoBehaviour
    {
        [Header("Systems")]
        [SerializeField] private ClubPlayerSeating seating;
        [SerializeField] private ClubCashThrower cashThrower;
        [SerializeField] private ClubPrivateDance privateDance;

        [Header("Panels")]
        [SerializeField] private GameObject actionsPanel;
        [SerializeField] private Button throwButton;
        [SerializeField] private Text throwLabel;
        [SerializeField] private Button leaveButton;
        [SerializeField] private Text leaveLabel;
        [SerializeField] private Text timerText;
        [SerializeField] private GameObject messagePanel;
        [SerializeField] private Text messageText;
        [SerializeField, Min(0.5f)] private float messageSeconds = 2.5f;

        [Header("Keys (keyboard / gamepad)")]
        [SerializeField] private GenericInput throwInput = new GenericInput("G", "X", "");
        [SerializeField] private GenericInput leaveInput = new GenericInput("X", "B", "");

        private float messageTimer;
        private string throwText;
        private string throwTextKeys;

        private void Awake()
        {
            SetActive(actionsPanel, false);
            SetActive(messagePanel, false);
            if (throwButton != null) throwButton.onClick.AddListener(HandleThrowPressed);
            if (leaveButton != null) leaveButton.onClick.AddListener(HandleLeavePressed);
            int price = cashThrower != null ? cashThrower.PricePerThrow : 0;
            throwText = "THROW CASH  $" + price;
            throwTextKeys = throwText + "  [G]";
        }

        private void OnEnable()
        {
            if (seating != null) seating.OnSeatedChanged += HandleSeatedChanged;
            if (cashThrower != null) cashThrower.OnMessage += ShowMessage;
            if (privateDance != null)
            {
                privateDance.OnMessage += ShowMessage;
                privateDance.OnSecondsLeftChanged += HandleSecondsLeft;
            }
        }

        private void OnDisable()
        {
            if (seating != null) seating.OnSeatedChanged -= HandleSeatedChanged;
            if (cashThrower != null) cashThrower.OnMessage -= ShowMessage;
            if (privateDance != null)
            {
                privateDance.OnMessage -= ShowMessage;
                privateDance.OnSecondsLeftChanged -= HandleSecondsLeft;
            }
        }

        // Per-frame only while seated (keys) or while a message is showing (auto-hide).
        private void Update()
        {
            if (messageTimer > 0f)
            {
                messageTimer -= Time.unscaledDeltaTime;
                if (messageTimer <= 0f) SetActive(messagePanel, false);
            }
            if (seating == null || !seating.IsSeated) return;
            if (throwInput.GetButtonDown()) HandleThrowPressed();
            else if (leaveInput.GetButtonDown()) HandleLeavePressed();
        }

        private void HandleThrowPressed()
        {
            if (cashThrower != null) cashThrower.Throw();
        }

        private void HandleLeavePressed()
        {
            if (privateDance != null && privateDance.IsActive) privateDance.Leave();
            else if (seating != null) seating.StandUp(null);
        }

        private void HandleSeatedChanged(bool seated)
        {
            SetActive(actionsPanel, seated);
            if (!seated) return;
            bool keys = !IsTouch();
            bool dance = privateDance != null && privateDance.IsActive;
            if (throwLabel != null) throwLabel.text = keys ? throwTextKeys : throwText;
            if (leaveLabel != null) leaveLabel.text = (dance ? "LEAVE" : "STAND UP") + (keys ? "  [X]" : "");
            if (timerText != null) timerText.gameObject.SetActive(dance);
        }

        private void HandleSecondsLeft(int seconds)
        {
            if (timerText != null) timerText.text = "PRIVATE DANCE  0:" + seconds.ToString("00");
        }

        private void ShowMessage(string message)
        {
            if (messageText != null) messageText.text = message;
            SetActive(messagePanel, true);
            messageTimer = messageSeconds;
        }

        private static bool IsTouch() => vInput.instance != null && vInput.instance.inputDevice == InputDevice.Mobile;

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }
    }
}
