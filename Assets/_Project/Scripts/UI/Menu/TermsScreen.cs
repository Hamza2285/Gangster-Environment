using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.UI.Menu
{
    /// <summary>
    /// "Legal Terms &amp; Policies": shown after the Navigation Experience (minimap) screen, before loading. Two tick boxes,
    /// Privacy Policy and Terms of Service; Confirm unlocks once both are ticked and starts the game. Acceptance is saved
    /// (PlayerPrefs <see cref="PrefKey"/>), so later launches skip the screen unless <see cref="showEveryTime"/> is on.
    /// Built by TermsScreenBuilder (Tools > OpenworldCrime > UI > Build Terms Screen).
    /// </summary>
    [DisallowMultipleComponent]
    public class TermsScreen : MonoBehaviour
    {
        public const string PrefKey = "LegalTermsAccepted";

        [SerializeField] private GameObject root;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Toggle privacyToggle;
        [SerializeField] private Toggle termsToggle;
        [SerializeField] private Button confirmButton;
        [SerializeField] private CanvasGroup confirmGroup;
        [Tooltip("Optional button art: the image swaps to Ready Sprite once both boxes are ticked, Locked Sprite before.")]
        [SerializeField] private Image confirmImage;
        [SerializeField] private Sprite readySprite;
        [SerializeField] private Sprite lockedSprite;
        [Tooltip("Optional: opens these pages from the READ links (the links hide when empty).")]
        [SerializeField] private string privacyPolicyUrl = "";
        [SerializeField] private string termsOfServiceUrl = "";
        [SerializeField] private Button privacyLink;
        [SerializeField] private Button termsLink;
        [Tooltip("Show the screen on every start, even after the player has accepted.")]
        [SerializeField] private bool showEveryTime;
        [SerializeField, Min(0.05f)] private float fadeSeconds = 0.3f;

        private Action onDone;
        private bool leaving;

        public static bool Accepted => PlayerPrefs.GetInt(PrefKey, 0) == 1;
        public bool IsOpen => root != null && root.activeSelf;
        /// <summary>Whether <see cref="Show"/> will open the screen (otherwise it calls straight through).</summary>
        public bool WillShow => showEveryTime || !Accepted;

        private void Awake()
        {
            if (root != null) root.SetActive(false);
            if (privacyLink != null) privacyLink.gameObject.SetActive(!string.IsNullOrEmpty(privacyPolicyUrl));
            if (termsLink != null) termsLink.gameObject.SetActive(!string.IsNullOrEmpty(termsOfServiceUrl));
        }

        private void OnEnable()
        {
            if (privacyToggle != null) privacyToggle.onValueChanged.AddListener(Refresh);
            if (termsToggle != null) termsToggle.onValueChanged.AddListener(Refresh);
            if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);
            if (privacyLink != null) privacyLink.onClick.AddListener(OpenPrivacy);
            if (termsLink != null) termsLink.onClick.AddListener(OpenTerms);
        }

        private void OnDisable()
        {
            if (privacyToggle != null) privacyToggle.onValueChanged.RemoveListener(Refresh);
            if (termsToggle != null) termsToggle.onValueChanged.RemoveListener(Refresh);
            if (confirmButton != null) confirmButton.onClick.RemoveListener(Confirm);
            if (privacyLink != null) privacyLink.onClick.RemoveListener(OpenPrivacy);
            if (termsLink != null) termsLink.onClick.RemoveListener(OpenTerms);
        }

        /// <summary>Opens the screen; <paramref name="done"/> runs on Confirm (at once if it was already accepted).</summary>
        public void Show(Action done)
        {
            if (!WillShow) { done?.Invoke(); return; }
            onDone = done;
            leaving = false;
            bool accepted = Accepted;
            privacyToggle.SetIsOnWithoutNotify(accepted);
            termsToggle.SetIsOnWithoutNotify(accepted);
            privacyToggle.interactable = termsToggle.interactable = true;
            Refresh(false);
            root.SetActive(true);
            StopAllCoroutines();
            StartCoroutine(Fade(0f, 1f));
        }

        private void Refresh(bool _)
        {
            bool ready = privacyToggle.isOn && termsToggle.isOn;
            confirmButton.interactable = ready && !leaving;
            bool art = confirmImage != null && readySprite != null && lockedSprite != null;
            if (art) confirmImage.sprite = ready ? readySprite : lockedSprite;
            if (confirmGroup != null) confirmGroup.alpha = ready || art ? 1f : 0.45f;
        }

        private void Confirm()
        {
            if (leaving || !privacyToggle.isOn || !termsToggle.isOn) return;
            leaving = true;
            PlayerPrefs.SetInt(PrefKey, 1);
            PlayerPrefs.Save();
            confirmButton.interactable = false;
            privacyToggle.interactable = termsToggle.interactable = false;
            // The loading screen fades in over this screen; it goes with the menu scene.
            onDone?.Invoke();
        }

        private void OpenPrivacy() { if (!string.IsNullOrEmpty(privacyPolicyUrl)) Application.OpenURL(privacyPolicyUrl); }
        private void OpenTerms() { if (!string.IsNullOrEmpty(termsOfServiceUrl)) Application.OpenURL(termsOfServiceUrl); }

        private IEnumerator Fade(float from, float to)
        {
            if (group == null) yield break;
            for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.Lerp(from, to, t / fadeSeconds);
                yield return null;
            }
            group.alpha = to;
        }
    }
}
