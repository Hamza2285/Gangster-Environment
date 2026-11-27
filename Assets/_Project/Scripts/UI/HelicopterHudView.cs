using OpenworldCrime.Controls;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.UI
{
    /// <summary>
    /// Touch controls while flying a helicopter: shows the helicopter stick + UP / DOWN buttons (mobile mode only) while the
    /// player pilots (<see cref="HelicopterController.OnPlayerPilotingChanged"/>), and keeps RCC's car pedals / steering hidden
    /// meanwhile (ControlsModeManager shows them for every vehicle; the exit button of the vehicle controls stays). With
    /// keyboard and mouse a short key hint shows instead. Visual only: the controls press virtual inputs that
    /// <see cref="HelicopterController"/> reads. No Update.
    /// </summary>
    [DisallowMultipleComponent]
    public class HelicopterHudView : MonoBehaviour
    {
        [Tooltip("Helicopter stick and UP / DOWN buttons (on the touch controls canvas).")]
        [SerializeField] private GameObject touchControls;
        [Tooltip("Keyboard hint shown while flying with keyboard and mouse.")]
        [SerializeField] private GameObject keyHint;
        [Tooltip("RCC's mobile car controls, hidden while flying.")]
        [SerializeField] private GameObject rccCanvas;
        [SerializeField] private ControlsModeManager controlsMode;

        private bool piloting;
        private bool mobile;

        private void Awake() => mobile = controlsMode != null && controlsMode.IsMobile;

        private void OnEnable()
        {
            HelicopterController.OnPlayerPilotingChanged += HandlePiloting;
            ControlsModeManager.OnControlsModeChanged += HandleControlsMode;
            Refresh();
        }

        private void OnDisable()
        {
            HelicopterController.OnPlayerPilotingChanged -= HandlePiloting;
            ControlsModeManager.OnControlsModeChanged -= HandleControlsMode;
        }

        private void HandlePiloting(HelicopterController helicopter, bool isPiloting)
        {
            piloting = isPiloting;
            Refresh();
        }

        // ControlsModeManager re-shows the RCC controls when the mode changes: hidden again right after (its event comes last).
        private void HandleControlsMode(bool isMobile)
        {
            mobile = isMobile;
            Refresh();
        }

        private void Refresh()
        {
            if (touchControls != null) touchControls.SetActive(piloting && mobile);
            if (keyHint != null) keyHint.SetActive(piloting && !mobile);
            if (piloting && rccCanvas != null && rccCanvas.activeSelf) rccCanvas.SetActive(false);
        }
    }
}
