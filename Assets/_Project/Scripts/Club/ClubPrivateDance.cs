using System;
using System.Collections;
using OpenworldCrime.Economy;
using OpenworldCrime.Player;
using OpenworldCrime.UI;
using UnityEngine;

namespace OpenworldCrime.Club
{
    using PlayerManager = OpenworldCrime.Player.PlayerManager; // SharedAssets has a global PlayerManager

    /// <summary>
    /// Paid private dance in the VIP booth. The marker (trigger + "Private Dance $X" prompt) takes the payment, then behind a
    /// fade the player is seated on the booth sofa, the booth dancer and the booth light come on and she dances for him
    /// (dance clips only) for <see cref="durationSeconds"/> or until he leaves; another fade puts him back at the marker.
    /// </summary>
    [DisallowMultipleComponent]
    public class ClubPrivateDance : MonoBehaviour, IPlayerInteractable
    {
        [Header("References")]
        [SerializeField] private ClubPlayerSeating seating;
        [Tooltip("Black fade of the club HUD.")]
        [SerializeField] private ScreenFader fader;
        [SerializeField] private Transform boothSeat;
        [SerializeField] private Transform boothCamera;
        [Tooltip("Where the player stands after the dance (the marker), facing its forward.")]
        [SerializeField] private Transform standPoint;
        [Tooltip("The booth dancer (inactive until a dance is bought).")]
        [SerializeField] private GameObject dancer;
        [Tooltip("Warm booth light, on only during a dance.")]
        [SerializeField] private Light boothLight;
        [Tooltip("Booth props moved out of the way during a dance (table, bottles).")]
        [SerializeField] private GameObject[] hideDuringDance = new GameObject[0];
        [SerializeField] private GameObject marker;

        [Header("Tuning")]
        [SerializeField, Min(0)] private int price = 100;
        [SerializeField, Min(5f)] private float durationSeconds = 40f;
        [SerializeField, Min(0f)] private float fadeDuration = 0.45f;

        /// <summary>Raised when a dance starts (true, screen still black) and after it ended (false).</summary>
        public event Action<bool> OnSessionChanged;
        /// <summary>Whole seconds left, raised when the value changes.</summary>
        public event Action<int> OnSecondsLeftChanged;
        public event Action<string> OnMessage;

        public bool IsActive { get; private set; }
        public int Price => price;
        public string InteractionPrompt => prompt;

        private string prompt;
        private string brokeMessage;
        private bool leaveRequested;

        private void Awake()
        {
            prompt = "Private Dance  $" + price;
            brokeMessage = "Not enough cash (private dance $" + price + ")";
            if (seating == null || fader == null || boothSeat == null || dancer == null)
            {
                Debug.LogError($"[{nameof(ClubPrivateDance)}] Missing references on '{name}'.", this);
                enabled = false;
                return;
            }
            dancer.SetActive(false);
            if (boothLight != null) boothLight.enabled = false;
        }

        public bool CanInteract(PlayerManager player) => isActiveAndEnabled && !IsActive && player != null && !seating.IsSeated && !player.IsInVehicle;

        public void Interact(PlayerManager player)
        {
            if (!CanInteract(player)) return;
            MoneyManager money = MoneyManager.Instance;
            if (money == null || !money.TrySpend(price))
            {
                OnMessage?.Invoke(brokeMessage);
                return;
            }
            StartCoroutine(Session(player));
        }

        /// <summary>Ends the running dance early (HUD button / key).</summary>
        public void Leave()
        {
            if (IsActive) leaveRequested = true;
        }

        private IEnumerator Session(PlayerManager player)
        {
            IsActive = true;
            leaveRequested = false;
            player.SetInputLocked(true);
            yield return fader.FadeTo(1f, fadeDuration, "PRIVATE DANCE");

            if (!seating.Sit(player, boothSeat, boothCamera))
            {
                // Could not seat (should not happen): refund and give control back.
                MoneyManager.Instance?.Add(price);
                player.SetInputLocked(false);
                yield return fader.FadeTo(0f, fadeDuration);
                IsActive = false;
                yield break;
            }
            SetBooth(true);
            OnSessionChanged?.Invoke(true);
            yield return new WaitForSeconds(0.8f); // hips fitted and camera settled behind the black
            seating.SnapCamera();
            yield return fader.FadeTo(0f, fadeDuration);

            float timeLeft = durationSeconds;
            int shown = -1;
            while (timeLeft > 0f && !leaveRequested && seating.IsSeated)
            {
                int seconds = Mathf.CeilToInt(timeLeft);
                if (seconds != shown)
                {
                    shown = seconds;
                    OnSecondsLeftChanged?.Invoke(seconds);
                }
                timeLeft -= Time.deltaTime;
                yield return null;
            }

            yield return fader.FadeTo(1f, fadeDuration);
            seating.StandUp(standPoint);
            SetBooth(false);
            IsActive = false;
            OnSessionChanged?.Invoke(false);
            yield return null;
            yield return fader.FadeTo(0f, fadeDuration);
        }

        private void SetBooth(bool dancing)
        {
            dancer.SetActive(dancing);
            if (boothLight != null) boothLight.enabled = dancing;
            if (marker != null) marker.SetActive(!dancing);
            for (int i = 0; i < hideDuringDance.Length; i++)
                if (hideDuringDance[i] != null) hideDuringDance[i].SetActive(!dancing);
        }

        private void OnDisable()
        {
            if (!IsActive) return;
            StopAllCoroutines();
            IsActive = false;
            SetBooth(false);
        }

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }
    }
}
