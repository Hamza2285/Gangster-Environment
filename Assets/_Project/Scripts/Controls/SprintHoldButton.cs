using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenworldCrime.Controls
{
    /// <summary>
    /// The phone SPRINT button: sprints while held, through the stick's own sprint handling (<see cref="VirtualJoystick"/>), which
    /// copes with Invector's toggle sprint and its "only once moving" rule. Released, the run goes back to the stick's choice.
    /// </summary>
    public class SprintHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private Graphic targetGraphic;
        [SerializeField] private Color pressedTint = new Color(1f, 0.8f, 0.5f, 1f);
        private Color normal;
        private bool held;

        private void Awake()
        {
            if (targetGraphic == null) targetGraphic = GetComponent<Graphic>();
            if (targetGraphic != null) normal = targetGraphic.color;
        }

        public void OnPointerDown(PointerEventData eventData) => Set(true);
        public void OnPointerUp(PointerEventData eventData) => Set(false);
        private void OnDisable() => Set(false);

        private void Set(bool value)
        {
            if (held == value) return;
            held = value;
            VirtualJoystick.SprintButtonHeld = value;
            if (targetGraphic != null) targetGraphic.color = value ? pressedTint : normal;
        }
    }
}
