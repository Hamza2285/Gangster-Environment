using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityStandardAssets.CrossPlatformInput;

namespace OpenworldCrime.Controls
{
    /// <summary>
    /// On-screen button. Presses the named virtual input button (read by Invector through its mobile input names) and/or
    /// raises <see cref="onPressed"/> for actions that are plain method calls (e.g. next weapon). Visual feedback only;
    /// no gameplay state lives here.
    /// </summary>
    public class VirtualButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [Tooltip("Virtual input button name, matching the 'mobile' name of the Invector GenericInput. Empty = none.")]
        [SerializeField] private string buttonName;
        [Tooltip("Tap toggles the button on / off instead of holding it (e.g. aim).")]
        [SerializeField] private bool toggle;
        [SerializeField] private UnityEvent onPressed = new UnityEvent();

        [Header("Feedback")]
        [SerializeField] private Graphic targetGraphic;
        [SerializeField] private Color pressedTint = new Color(1f, 1f, 1f, 0.9f);

        private Color normalColor;
        private bool held;

        private void Awake()
        {
            if (targetGraphic != null) normalColor = targetGraphic.color;
        }

        private void OnDisable()
        {
            if (held) Release();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            onPressed.Invoke();
            if (toggle)
            {
                if (held) Release();
                else Press();
                return;
            }
            Press();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!toggle) Release();
        }

        private void Press()
        {
            held = true;
            CrossPlatformInputManager.SetButtonDown(buttonName);
            if (targetGraphic != null) targetGraphic.color = pressedTint;
        }

        private void Release()
        {
            held = false;
            CrossPlatformInputManager.SetButtonUp(buttonName);
            if (targetGraphic != null) targetGraphic.color = normalColor;
        }
    }
}
