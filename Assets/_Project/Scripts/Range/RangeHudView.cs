using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.Range
{
    /// <summary>
    /// Range challenge HUD (city canvas; visuals only): timer + score while running, a big countdown, and the result card
    /// (rating, hits, reward). Follows <see cref="ShootingRangeController"/>'s static events; the Quit button asks the running
    /// challenge to cancel.
    /// </summary>
    [DisallowMultipleComponent]
    public class RangeHudView : MonoBehaviour
    {
        [SerializeField] private GameObject runningPanel;
        [SerializeField] private Text timerText;
        [SerializeField] private Text scoreText;
        [SerializeField] private Text countdownText;
        [Tooltip("Cancels the running challenge (Esc / back does the same).")]
        [SerializeField] private Button quitButton;
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private Text resultTitle;
        [SerializeField] private Text resultBody;
        [SerializeField, Min(1f)] private float resultDuration = 6f;

        private float resultTimer;
        private int lastSecond = -1;
        private int lastScore = -1;

        private void Awake()
        {
            SetActive(runningPanel, false);
            SetActive(resultPanel, false);
            if (countdownText != null) countdownText.gameObject.SetActive(false);
            if (quitButton != null) quitButton.onClick.AddListener(ShootingRangeController.RequestQuit);
        }

        private void OnEnable()
        {
            ShootingRangeController.OnChallengeStarted += HandleStarted;
            ShootingRangeController.OnCountdown += HandleCountdown;
            ShootingRangeController.OnChallengeUpdated += HandleUpdated;
            ShootingRangeController.OnChallengeEnded += HandleEnded;
        }

        private void OnDisable()
        {
            ShootingRangeController.OnChallengeStarted -= HandleStarted;
            ShootingRangeController.OnCountdown -= HandleCountdown;
            ShootingRangeController.OnChallengeUpdated -= HandleUpdated;
            ShootingRangeController.OnChallengeEnded -= HandleEnded;
        }

        // Per-frame only while the result card is showing (auto-hide timer).
        private void Update()
        {
            if (resultTimer <= 0f) return;
            resultTimer -= Time.unscaledDeltaTime;
            if (resultTimer <= 0f) SetActive(resultPanel, false);
        }

        private void HandleStarted()
        {
            SetActive(resultPanel, false);
            resultTimer = 0f;
            lastSecond = lastScore = -1;
            SetActive(runningPanel, true);
            HandleUpdated(0f, 0, 0);
        }

        private void HandleCountdown(int secondsLeft)
        {
            if (countdownText == null) return;
            countdownText.gameObject.SetActive(true);
            countdownText.text = secondsLeft > 0 ? secondsLeft.ToString() : "GO!";
            if (secondsLeft <= 0) Invoke(nameof(HideCountdown), 0.8f);
        }

        private void HideCountdown()
        {
            if (countdownText != null) countdownText.gameObject.SetActive(false);
        }

        // Text is only rebuilt when the shown second or the score changes (no per-frame string allocations).
        private void HandleUpdated(float timeLeft, int score, int hits)
        {
            int second = Mathf.CeilToInt(timeLeft);
            if (timerText != null && second != lastSecond) timerText.text = "TIME  " + second;
            if (scoreText != null && score != lastScore) scoreText.text = "SCORE  " + score;
            lastSecond = second;
            lastScore = score;
        }

        private void HandleEnded(RangeResult result)
        {
            SetActive(runningPanel, false);
            HideCountdown();
            if (resultTitle != null) resultTitle.text = result.aborted ? "CANCELLED" : result.rating.ToUpperInvariant();
            if (resultBody != null)
            {
                float accuracy = result.shown > 0 ? 100f * result.hits / result.shown : 0f;
                resultBody.text = "Score  " + result.score
                    + "\nTargets hit  " + result.hits + " / " + result.shown + "  (" + accuracy.ToString("0") + "%)"
                    + "\nHeadshots  " + result.headshots
                    + (result.reward > 0 ? "\nReward  $" + result.reward.ToString("N0") : result.aborted ? "\nChallenge cancelled" : "\nNo reward");
            }
            SetActive(resultPanel, true);
            resultTimer = resultDuration;
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }
    }
}
