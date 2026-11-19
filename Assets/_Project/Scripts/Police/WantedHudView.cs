using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.Police
{
    /// <summary>
    /// Wanted-level HUD visuals (a local UI view on its own HUD object, no game logic): the stars (flashing while police search),
    /// the arrest bar at the bottom centre, the BUSTED screen and the fine message afterwards. Everything is driven by
    /// <see cref="WantedManager"/> / <see cref="BustedSequence"/> events; Update only runs while something animates.
    /// </summary>
    [DisallowMultipleComponent]
    public class WantedHudView : MonoBehaviour
    {
        [Header("Stars")]
        [SerializeField] private GameObject starsRoot;
        [SerializeField] private Image[] stars = new Image[0];
        [Tooltip("Earned star (filled).")]
        [SerializeField] private Sprite activeStarSprite;
        [Tooltip("Star not earned (outline only).")]
        [SerializeField] private Sprite inactiveStarSprite;
        [SerializeField] private Color activeStarColour = Color.white;
        [SerializeField] private Color inactiveStarColour = Color.white;
        [Tooltip("Searching: the earned stars blink at this interval (unscaled seconds).")]
        [SerializeField, Min(0.05f)] private float flashInterval = 0.35f;

        [Header("Arrest Bar")]
        [SerializeField] private GameObject bustBarRoot;
        [Tooltip("Left-pivoted fill; its X scale is set to the arrest progress.")]
        [SerializeField] private RectTransform bustBarFill;

        [Header("Busted")]
        [SerializeField] private GameObject bustedScreen;
        [SerializeField] private GameObject fineMessage;
        [SerializeField] private Text fineText;
        [SerializeField] private string fineFormat = "BUSTED  -  Fined ${0:N0}";
        [SerializeField, Min(0.5f)] private float fineMessageSeconds = 4f;

        private int starCount;
        private bool searching;
        private float fineMessageUntil;

        private void Awake()
        {
            if (starsRoot != null) starsRoot.SetActive(false);
            if (bustBarRoot != null) bustBarRoot.SetActive(false);
            if (bustedScreen != null) bustedScreen.SetActive(false);
            if (fineMessage != null) fineMessage.SetActive(false);
        }

        private void OnEnable()
        {
            WantedManager.OnWantedChanged += HandleWantedChanged;
            WantedManager.OnBustProgressChanged += HandleBustProgress;
            WantedManager.OnBusted += HandleBusted;
            BustedSequence.OnFined += HandleFined;
            BustedSequence.OnKneeling += HandleKneeling;
            WantedManager wanted = WantedManager.Instance;
            if (wanted != null) HandleWantedChanged(wanted.Stars, wanted.State);
        }

        private void OnDisable()
        {
            WantedManager.OnWantedChanged -= HandleWantedChanged;
            WantedManager.OnBustProgressChanged -= HandleBustProgress;
            WantedManager.OnBusted -= HandleBusted;
            BustedSequence.OnFined -= HandleFined;
            BustedSequence.OnKneeling -= HandleKneeling;
        }

        // Only does work for the search flashing and the fine message timer (the component stays enabled: it holds the subscriptions).
        private void Update()
        {
            if (searching)
            {
                bool on = Mathf.Repeat(Time.unscaledTime, flashInterval * 2f) < flashInterval;
                for (int i = 0; i < stars.Length && i < starCount; i++) SetStar(stars[i], on);
            }
            if (fineMessage != null && fineMessage.activeSelf && Time.unscaledTime >= fineMessageUntil) fineMessage.SetActive(false);
        }

        private void HandleWantedChanged(int count, WantedState state)
        {
            starCount = count;
            searching = state == WantedState.Searching;
            if (starsRoot != null) starsRoot.SetActive(count > 0);
            for (int i = 0; i < stars.Length; i++) SetStar(stars[i], i < count);
            if (count == 0 && bustedScreen != null) bustedScreen.SetActive(false);
        }

        private void SetStar(Image star, bool earned)
        {
            star.color = earned ? activeStarColour : inactiveStarColour;
            Sprite sprite = earned ? activeStarSprite : inactiveStarSprite;
            if (sprite != null && star.sprite != sprite) star.sprite = sprite;
        }

        private void HandleBustProgress(float progress)
        {
            if (bustBarRoot != null) bustBarRoot.SetActive(progress > 0.001f);
            if (bustBarFill != null) bustBarFill.localScale = new Vector3(progress, 1f, 1f);
        }

        // Caught: the arrest bar goes; BUSTED itself waits until the player is on his knees.
        private void HandleBusted()
        {
            if (bustBarRoot != null) bustBarRoot.SetActive(false);
        }

        private void HandleKneeling()
        {
            if (bustedScreen != null) bustedScreen.SetActive(true);
        }

        private void HandleFined(int amount)
        {
            if (bustedScreen != null) bustedScreen.SetActive(false);
            if (fineMessage == null) return;
            if (fineText != null) fineText.text = string.Format(fineFormat, amount);
            fineMessage.SetActive(true);
            fineMessageUntil = Time.unscaledTime + fineMessageSeconds;
        }
    }
}
