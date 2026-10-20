using UnityEngine;
using UnityEngine.EventSystems;
using UnityStandardAssets.CrossPlatformInput;

namespace OpenworldCrime.Controls
{
    /// <summary>
    /// Camera look area (usually the right half of the screen behind the buttons). Finger movement is written into two
    /// virtual axes (Invector's mobile "Mouse X" / "Mouse Y") as a per-frame delta, like a mouse; the axes drop to zero on
    /// frames without movement.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class VirtualTouchLook : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private string horizontalAxis = "Mouse X";
        [SerializeField] private string verticalAxis = "Mouse Y";
        [Tooltip("Axis units per screen pixel at 1080p (scaled with the screen height so it feels the same on every device).")]
        [SerializeField, Min(0.001f)] private float sensitivity = 0.12f;
        [SerializeField] private bool invertY;

        private int pointerId = int.MinValue;
        private int lastMoveFrame = -1;

        private void OnDisable()
        {
            pointerId = int.MinValue;
            Zero();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (pointerId == int.MinValue) pointerId = eventData.pointerId;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != pointerId) return;
            float scale = sensitivity * 1080f / Mathf.Max(1, Screen.height);
            CrossPlatformInputManager.SetAxis(horizontalAxis, eventData.delta.x * scale);
            CrossPlatformInputManager.SetAxis(verticalAxis, eventData.delta.y * scale * (invertY ? -1f : 1f));
            lastMoveFrame = Time.frameCount;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != pointerId) return;
            pointerId = int.MinValue;
            Zero();
        }

        // Runs after the camera has read this frame's delta.
        private void LateUpdate()
        {
            if (lastMoveFrame != Time.frameCount) Zero();
        }

        private void Zero()
        {
            CrossPlatformInputManager.SetAxisZero(horizontalAxis);
            CrossPlatformInputManager.SetAxisZero(verticalAxis);
        }
    }
}
