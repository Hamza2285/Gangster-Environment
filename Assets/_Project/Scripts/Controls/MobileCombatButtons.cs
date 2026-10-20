using OpenworldCrime.Player;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.Controls
{
    /// <summary>
    /// Context-sensitive on-foot combat buttons (visuals only, event-driven):
    /// - unarmed: one attack button (punch), no aim / reload / fire buttons;
    /// - gun out: AIM and RELOAD show; the two FIRE buttons (right and left thumb) show only while aiming.
    /// </summary>
    public class MobileCombatButtons : MonoBehaviour
    {
        [SerializeField] private PlayerCombatController combat;

        [Header("Buttons")]
        [SerializeField] private GameObject fireRight;
        [SerializeField] private GameObject fireLeft;
        [SerializeField] private GameObject aim;
        [SerializeField] private GameObject reload;
        [Tooltip("Shown while unarmed (melee attack, same input as fire).")]
        [SerializeField] private GameObject punch;

        private void OnEnable()
        {
            if (combat == null)
            {
                Debug.LogError($"[{nameof(MobileCombatButtons)}] No PlayerCombatController assigned on '{name}'.", this);
                return;
            }
            combat.OnArmedChanged += HandleChanged;
            combat.OnAimChanged += HandleChanged;
            Refresh();
        }

        private void OnDisable()
        {
            if (combat == null) return;
            combat.OnArmedChanged -= HandleChanged;
            combat.OnAimChanged -= HandleChanged;
        }

        private void HandleChanged(bool _) => Refresh();

        private void Refresh()
        {
            bool armed = combat.IsArmed;
            bool aiming = armed && combat.IsAiming;
            SetActive(punch, !armed);
            SetActive(aim, armed);
            SetActive(reload, armed);
            SetActive(fireRight, aiming);
            SetActive(fireLeft, aiming);
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }
    }
}
