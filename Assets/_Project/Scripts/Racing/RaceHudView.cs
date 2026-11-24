using Invector.vCharacterController;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.Racing
{
    /// <summary>
    /// Street race HUD (gameplay canvas; visuals and input only): the START RACE prompt at a start point, the 3-2-1-GO
    /// countdown, position / checkpoint or lap / time while racing with Respawn and Quit, warnings, and the results.
    /// Buttons work on touch; keys work on keyboard / gamepad. <see cref="RaceManager"/> owns the race and raises the events.
    /// </summary>
    [DisallowMultipleComponent]
    public class RaceHudView : MonoBehaviour
    {
        [SerializeField] private RaceManager manager;

        [Header("Prompt")]
        [SerializeField] private GameObject promptPanel;
        [SerializeField] private Text promptTitle;
        [SerializeField] private Text promptInfo;
        [SerializeField] private Button startButton;
        [SerializeField] private Text startLabel;

        [Header("Racing")]
        [SerializeField] private GameObject racePanel;
        [SerializeField] private Text positionText;
        [SerializeField] private Text progressText;
        [SerializeField] private Text timeText;
        [SerializeField] private Button respawnButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private GameObject wrongWayPanel;
        [Tooltip("'Return to the race' countdown (off the route / wrong way for long).")]
        [SerializeField] private GameObject returnPanel;
        [SerializeField] private Text returnText;
        [SerializeField] private Text countdownText;

        [Header("Messages / results")]
        [SerializeField] private GameObject messagePanel;
        [SerializeField] private Text messageText;
        [SerializeField, Min(0.5f)] private float messageSeconds = 2.5f;
        [SerializeField] private GameObject resultsPanel;
        [SerializeField] private Text resultsTitle;
        [SerializeField] private Text resultsBody;
        [SerializeField] private Button continueButton;

        [Header("Keys (keyboard / gamepad)")]
        [SerializeField] private GenericInput startInput = new GenericInput("Return", "Y", "");
        [SerializeField] private GenericInput respawnInput = new GenericInput("Backspace", "Back", "");
        [SerializeField] private GenericInput quitInput = new GenericInput("X", "", "");

        private float messageTimer;
        private float countdownTimer;
        private int lastSecond = -1;

        private void Awake()
        {
            if (manager == null) { Debug.LogError($"[{nameof(RaceHudView)}] No RaceManager on '{name}'.", this); enabled = false; return; }
            if (startButton != null) startButton.onClick.AddListener(manager.RequestStart);
            if (respawnButton != null) respawnButton.onClick.AddListener(manager.RequestRespawn);
            if (quitButton != null) quitButton.onClick.AddListener(manager.RequestQuit);
            if (continueButton != null) continueButton.onClick.AddListener(manager.CloseResults);
            SetActive(promptPanel, false);
            SetActive(racePanel, false);
            SetActive(resultsPanel, false);
            SetActive(messagePanel, false);
            SetActive(wrongWayPanel, false);
            SetActive(returnPanel, false);
            if (countdownText != null) countdownText.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (manager == null) return;
            manager.OnPromptChanged += HandlePrompt;
            manager.OnStateChanged += HandleState;
            manager.OnCountdown += HandleCountdown;
            manager.OnMessage += ShowMessage;
            manager.OnFinished += HandleFinished;
        }

        private void OnDisable()
        {
            if (manager == null) return;
            manager.OnPromptChanged -= HandlePrompt;
            manager.OnStateChanged -= HandleState;
            manager.OnCountdown -= HandleCountdown;
            manager.OnMessage -= ShowMessage;
            manager.OnFinished -= HandleFinished;
        }

        private void Update()
        {
            if (messageTimer > 0f && (messageTimer -= Time.unscaledDeltaTime) <= 0f) SetActive(messagePanel, false);
            if (countdownTimer > 0f && (countdownTimer -= Time.unscaledDeltaTime) <= 0f && countdownText != null) countdownText.gameObject.SetActive(false);

            switch (manager.State)
            {
                case RaceState.Idle:
                    if (manager.PromptPoint != null && manager.PromptBlockedReason == null && startInput.GetButtonDown()) manager.RequestStart();
                    break;
                case RaceState.Racing:
                    UpdateRacePanel();
                    if (respawnInput.GetButtonDown()) manager.RequestRespawn();
                    else if (quitInput.GetButtonDown()) manager.RequestQuit();
                    break;
                case RaceState.Countdown:
                    if (quitInput.GetButtonDown()) manager.RequestQuit();
                    break;
                case RaceState.Results:
                    if (startInput.GetButtonDown()) manager.CloseResults();
                    break;
            }
        }

        private void UpdateRacePanel()
        {
            RaceDefinition race = manager.CurrentRace;
            if (race == null) return;
            if (positionText != null) positionText.text = Ordinal(manager.PlayerPosition) + " / " + manager.Racers;
            if (progressText != null)
            {
                int cpCount = race.Checkpoints.Length;
                int inLap = Mathf.Min(manager.CheckpointsPassed - (manager.CurrentLap - 1) * cpCount, cpCount);
                progressText.text = race.IsCircuit
                    ? "LAP " + manager.CurrentLap + "/" + race.Laps + "   CP " + inLap + "/" + cpCount
                    : "CHECKPOINT " + manager.CheckpointsPassed + "/" + race.TotalCheckpoints;
            }
            // The clock text changes ten times a second at most.
            int tenths = Mathf.FloorToInt(manager.Elapsed * 10f);
            if (tenths != lastSecond && timeText != null)
            {
                lastSecond = tenths;
                timeText.text = FormatTime(manager.Elapsed);
            }
            SetActive(wrongWayPanel, manager.WrongWay);
            bool leaving = manager.ReturnTimeLeft >= 0f;
            SetActive(returnPanel, leaving);
            if (leaving && returnText != null)
            {
                int seconds = Mathf.CeilToInt(manager.ReturnTimeLeft);
                if (seconds != lastReturnSeconds)
                {
                    lastReturnSeconds = seconds;
                    returnText.text = "RETURN TO THE RACE  " + seconds;
                }
            }
            else lastReturnSeconds = -1;
        }

        private int lastReturnSeconds = -1;

        private void HandlePrompt(RaceStartPoint point, string blocked)
        {
            if (point == null || point.Race == null) { SetActive(promptPanel, false); return; }
            SetActive(promptPanel, true);
            RaceDefinition race = point.Race;
            if (promptTitle != null) promptTitle.text = race.DisplayName.ToUpperInvariant();
            if (promptInfo != null)
            {
                string kind = race.IsCircuit ? race.Laps + " laps, " + race.Checkpoints.Length + " checkpoints" : "Sprint, " + race.Checkpoints.Length + " checkpoints";
                string best = "";
                float bestTime = OpenworldCrime.Economy.SaveManager.Instance != null ? OpenworldCrime.Economy.SaveManager.Instance.GetRaceBestTime(race.RaceId) : 0f;
                if (bestTime > 0f) best = "   Best " + FormatTime(bestTime);
                promptInfo.text = blocked ?? (kind + "   1st: $" + race.RewardFor(1) + best);
            }
            if (startButton != null) startButton.interactable = blocked == null;
            if (startLabel != null) startLabel.text = blocked == null ? (GameInput.MobileControls ? "START RACE" : "START RACE  [ENTER]") : "UNAVAILABLE";
        }

        private void HandleState(RaceState state)
        {
            SetActive(promptPanel, false);
            SetActive(racePanel, state == RaceState.Racing || state == RaceState.Countdown);
            SetActive(resultsPanel, state == RaceState.Results);
            if (state != RaceState.Racing) { SetActive(wrongWayPanel, false); SetActive(returnPanel, false); }
            if ((state == RaceState.Idle || state == RaceState.Results) && countdownText != null) countdownText.gameObject.SetActive(false);
            if (respawnButton != null) respawnButton.gameObject.SetActive(state == RaceState.Racing);
            lastSecond = -1;
            if (state == RaceState.Countdown && timeText != null) timeText.text = FormatTime(0f);
        }

        private void HandleCountdown(int n)
        {
            if (countdownText == null) return;
            countdownText.text = n > 0 ? n.ToString() : "GO!";
            countdownText.color = n > 0 ? Color.white : new Color(0.55f, 1f, 0.45f);
            countdownText.gameObject.SetActive(true);
            countdownTimer = n > 0 ? 0f : 0.9f;
        }

        private void HandleFinished(RaceResult result)
        {
            if (resultsTitle != null) resultsTitle.text = result.position == 1 ? "YOU WON!" : Ordinal(result.position) + " PLACE";
            if (resultsBody != null)
            {
                string best = result.newBest ? "NEW BEST TIME!" : result.bestTime > 0f ? "Best  " + FormatTime(result.bestTime) : "";
                resultsBody.text = result.race.DisplayName + "\n\nPosition  " + Ordinal(result.position) + " / " + result.racers
                                   + "\nTime  " + FormatTime(result.time) + "\n" + best
                                   + "\n\nReward  " + (result.reward > 0 ? "$" + result.reward : "none");
            }
        }

        public void ShowMessage(string message)
        {
            if (messageText == null) return;
            messageText.text = message;
            SetActive(messagePanel, true);
            messageTimer = messageSeconds;
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }

        private static string Ordinal(int n)
        {
            int m = n % 100;
            string s = m >= 11 && m <= 13 ? "th" : (n % 10) == 1 ? "st" : (n % 10) == 2 ? "nd" : (n % 10) == 3 ? "rd" : "th";
            return n + s;
        }

        private static string FormatTime(float seconds)
        {
            int m = Mathf.FloorToInt(seconds / 60f);
            float s = seconds - m * 60f;
            return m + ":" + s.ToString("00.00");
        }
    }
}
