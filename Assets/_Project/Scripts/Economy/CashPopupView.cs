using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.Economy
{
    /// <summary>
    /// "+$X" under the money counter when cash is picked up (gameplay canvas; visuals only). Follows
    /// <see cref="CashDropManager.OnCashCollected"/>; quick pickups add up into one popup. Update runs only while it shows.
    /// </summary>
    [DisallowMultipleComponent]
    public class CashPopupView : MonoBehaviour
    {
        [SerializeField] private CashDropManager drops;
        [SerializeField] private Text label;
        [SerializeField, Min(0.2f)] private float showSeconds = 1.4f;
        [SerializeField] private float rise = 26f;

        private RectTransform rect;
        private Vector2 restPosition;
        private Color colour;
        private float timer;
        private int total;

        private void Awake()
        {
            if (drops == null || label == null) { Debug.LogError($"[{nameof(CashPopupView)}] Missing references on '{name}'.", this); enabled = false; return; }
            rect = (RectTransform)label.transform;
            restPosition = rect.anchoredPosition;
            colour = label.color;
            label.gameObject.SetActive(false);
            drops.OnCashCollected += HandleCollected;
            enabled = false;
        }

        private void OnDestroy()
        {
            if (drops != null) drops.OnCashCollected -= HandleCollected;
        }

        private void HandleCollected(int amount, Vector3 where)
        {
            total = timer > 0f ? total + amount : amount;
            timer = showSeconds;
            label.text = "+$" + total;
            label.gameObject.SetActive(true);
            enabled = true;
        }

        private void Update()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer <= 0f)
            {
                label.gameObject.SetActive(false);
                rect.anchoredPosition = restPosition;
                enabled = false;
                return;
            }
            float t = 1f - timer / showSeconds;
            rect.anchoredPosition = restPosition + Vector2.up * (rise * t);
            Color c = colour;
            c.a = colour.a * Mathf.Clamp01(timer / (showSeconds * 0.35f));
            label.color = c;
        }
    }
}
