using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityStandardAssets.CrossPlatformInput;

namespace OpenworldCrime.Controls
{
    /// <summary>
    /// On-screen movement stick. Writes a -1..1 value into two virtual axes (Invector's mobile Horizontal / Vertical).
    /// The stick re-centres where the finger first touches inside its area. Pushing the finger forward past the rim
    /// (towards the SPRINT marker above the stick) holds the sprint button, GTA-style; pulling back releases it.
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private string horizontalAxis = "Horizontal";
        [SerializeField] private string verticalAxis = "Vertical";
        [SerializeField] private RectTransform background;
        [SerializeField] private RectTransform handle;
        [Tooltip("Handle travel (UI units) for full input.")]
        [SerializeField, Min(10f)] private float radius = 110f;
        [SerializeField, Range(0f, 0.5f)] private float deadZone = 0.1f;

        [Header("Sprint by pushing past the rim")]
        [Tooltip("Virtual button held while sprinting. Empty = no joystick sprint.")]
        [SerializeField] private string sprintButtonName = "M_Sprint";
        [Tooltip("Finger distance from the stick centre, in stick radii, that starts sprinting.")]
        [SerializeField, Min(1f)] private float sprintPull = 1.35f;
        [Tooltip("How forward the push must be (0 = any direction, 1 = straight up).")]
        [SerializeField, Range(0f, 1f)] private float sprintForward = 0.55f;
        [Tooltip("Player controller. With Invector's continuous (toggle) sprint the stick taps the button to switch sprint on " +
                 "when pushed past the rim and taps it again when pulled back; without it the button is simply held.")]
        [SerializeField] private Invector.vCharacterController.vThirdPersonController controller;
        [Tooltip("Optional marker above the stick, tinted while sprinting.")]
        [SerializeField] private Graphic sprintIndicator;
        [SerializeField] private Color sprintIdleColor = new Color(1f, 1f, 1f, 0.35f);
        [SerializeField] private Color sprintActiveColor = new Color(1f, 0.65f, 0.1f, 0.95f);

        private const float SprintRetryInterval = 0.2f;

        private Vector2 backgroundHome;
        private Camera eventCamera;
        private bool sprinting;
        private float nextSprintTry;

        private void Awake()
        {
            if (background == null || handle == null)
            {
                Debug.LogError($"[{nameof(VirtualJoystick)}] Background or handle not assigned on '{name}'.", this);
                enabled = false;
                return;
            }
            backgroundHome = background.anchoredPosition;
            if (sprintIndicator != null) sprintIndicator.color = sprintIdleColor;
        }

        private void OnDisable() => ResetStick();

        // Invector only starts a sprint once the character is already moving forward, so a push from standstill can miss the
        // first tap. While the finger stays in the sprint zone, retry a few times a second until the controller is sprinting.
        private void Update()
        {
            if (!sprinting || controller == null || !controller.useContinuousSprint || controller.isSprinting) return;
            if (Time.unscaledTime < nextSprintTry) return;
            nextSprintTry = Time.unscaledTime + SprintRetryInterval;
            CrossPlatformInputManager.SetButtonDown(sprintButtonName);
            CrossPlatformInputManager.SetButtonUp(sprintButtonName);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            eventCamera = eventData.pressEventCamera;
            // The result is relative to the parent's pivot, so it goes into localPosition (anchoredPosition is relative to the anchors).
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)background.parent, eventData.position, eventCamera, out Vector2 local))
                background.localPosition = local;
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(background, eventData.position, eventCamera, out Vector2 local)) return;
            Vector2 offset = Vector2.ClampMagnitude(local, radius);
            handle.anchoredPosition = offset;
            Vector2 value = offset / radius;
            if (value.magnitude < deadZone) value = Vector2.zero;
            CrossPlatformInputManager.SetAxis(horizontalAxis, value.x);
            CrossPlatformInputManager.SetAxis(verticalAxis, value.y);

            bool wantSprint = local.magnitude > radius * sprintPull && local.normalized.y > sprintForward;
            SetSprint(wantSprint);
        }

        public void OnPointerUp(PointerEventData eventData) => ResetStick();

        private void SetSprint(bool value)
        {
            if (value == sprinting || string.IsNullOrEmpty(sprintButtonName)) return;
            sprinting = value;
            if (controller != null && controller.useContinuousSprint)
            {
                // Toggle sprint: one tap switches it, so only tap when the controller's state differs from what we want.
                if (controller.isSprinting != value)
                {
                    CrossPlatformInputManager.SetButtonDown(sprintButtonName);
                    CrossPlatformInputManager.SetButtonUp(sprintButtonName);
                }
                nextSprintTry = Time.unscaledTime + SprintRetryInterval;
            }
            else if (value) CrossPlatformInputManager.SetButtonDown(sprintButtonName);
            else CrossPlatformInputManager.SetButtonUp(sprintButtonName);
            if (sprintIndicator != null) sprintIndicator.color = value ? sprintActiveColor : sprintIdleColor;
        }

        private void ResetStick()
        {
            SetSprint(false);
            if (handle != null) handle.anchoredPosition = Vector2.zero;
            if (background != null) background.anchoredPosition = backgroundHome;
            CrossPlatformInputManager.SetAxisZero(horizontalAxis);
            CrossPlatformInputManager.SetAxisZero(verticalAxis);
        }
    }
}
