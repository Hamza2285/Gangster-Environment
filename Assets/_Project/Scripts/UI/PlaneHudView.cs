using OpenworldCrime.Controls;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.UI
{
    /// <summary>
    /// Touch controls while flying a plane: shows the plane stick + THR + / THR - buttons (mobile mode only) while the player
    /// pilots (<see cref="PlaneController.OnPlayerPilotingChanged"/>) and keeps RCC's car pedals / steering hidden meanwhile
    /// (ControlsModeManager shows them for every vehicle; the exit button of the vehicle controls stays). With keyboard and
    /// mouse a short key hint shows instead. Same pattern as <see cref="HelicopterHudView"/>. Visual only: the controls press
    /// virtual inputs that <see cref="PlaneController"/> reads. No Update.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlaneHudView : MonoBehaviour
    {
        [Tooltip("Plane stick and THR + / THR - buttons (on the touch controls canvas).")]
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
            PlaneController.OnPlayerPilotingChanged += HandlePiloting;
            ControlsModeManager.OnControlsModeChanged += HandleControlsMode;
            Refresh();
        }

        private void OnDisable()
        {
            PlaneController.OnPlayerPilotingChanged -= HandlePiloting;
            ControlsModeManager.OnControlsModeChanged -= HandleControlsMode;
        }

        private void HandlePiloting(PlaneController plane, bool isPiloting)
        {
            piloting = isPiloting;
            Refresh();
        }

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

#if UNITY_EDITOR
        public void EditorSetup(GameObject touch, GameObject hint, GameObject rcc, ControlsModeManager controls)
        {
            touchControls = touch; keyHint = hint; rccCanvas = rcc; controlsMode = controls;
        }
#endif
    }
}
