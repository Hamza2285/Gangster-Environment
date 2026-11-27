using OpenworldCrime.Controls;
using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.UI
{
    /// <summary>
    /// Shows the parachute button only while the played character can open a parachute (carrying the bag and falling high
    /// enough, <see cref="PlayerParachute.OnPlayerDeployAvailableChanged"/>): the touch button in the on-foot controls in
    /// mobile mode, a key hint on the HUD with keyboard and mouse. Visual only: the button presses the "M_Parachute" virtual
    /// button that <see cref="PlayerParachute"/> reads, like every other touch control. While gliding the same button lets go of
    /// the canopy (<see cref="PlayerParachute.OnPlayerReleaseAvailableChanged"/>, its own key hint). No Update.
    /// </summary>
    [DisallowMultipleComponent]
    public class ParachuteButtonView : MonoBehaviour
    {
        [Tooltip("Touch button (child of the on-foot touch controls).")]
        [SerializeField] private GameObject touchButton;
        [Tooltip("Keyboard hint on the HUD (e.g. 'Open Parachute [B]').")]
        [SerializeField] private GameObject keyHint;
        [Tooltip("Keyboard hint while gliding (e.g. 'Release Parachute [B]'). Optional.")]
        [SerializeField] private GameObject releaseKeyHint;
        [SerializeField] private ControlsModeManager controlsMode;

        private bool available;
        private bool release;
        private bool mobile;

        private void Awake()
        {
            if (touchButton == null && keyHint == null) Debug.LogWarning($"[{nameof(ParachuteButtonView)}] Nothing to show on '{name}'.", this);
            mobile = controlsMode != null && controlsMode.IsMobile;
        }

        private void OnEnable()
        {
            PlayerParachute.OnPlayerDeployAvailableChanged += HandleDeployAvailable;
            PlayerParachute.OnPlayerReleaseAvailableChanged += HandleReleaseAvailable;
            ControlsModeManager.OnControlsModeChanged += HandleControlsMode;
            Refresh();
        }

        private void OnDisable()
        {
            PlayerParachute.OnPlayerDeployAvailableChanged -= HandleDeployAvailable;
            PlayerParachute.OnPlayerReleaseAvailableChanged -= HandleReleaseAvailable;
            ControlsModeManager.OnControlsModeChanged -= HandleControlsMode;
        }

        private void HandleDeployAvailable(bool canDeploy)
        {
            available = canDeploy;
            Refresh();
        }

        private void HandleReleaseAvailable(bool canRelease)
        {
            release = canRelease;
            Refresh();
        }

        private void HandleControlsMode(bool isMobile)
        {
            mobile = isMobile;
            Refresh();
        }

        private void Refresh()
        {
            bool button = (available || release) && mobile;
            if (touchButton != null && touchButton.activeSelf != button) touchButton.SetActive(button);
            if (keyHint != null && keyHint.activeSelf != (available && !mobile)) keyHint.SetActive(available && !mobile);
            if (releaseKeyHint != null && releaseKeyHint.activeSelf != (release && !mobile)) releaseKeyHint.SetActive(release && !mobile);
        }
    }
}
