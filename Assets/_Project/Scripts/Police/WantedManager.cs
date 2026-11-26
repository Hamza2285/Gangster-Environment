using System;
using OpenworldCrime.Interiors;
using OpenworldCrime.Player;
using OpenworldCrime.UI;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Police
{
    using PlayerManager = OpenworldCrime.Player.PlayerManager; // SharedAssets has a global PlayerManager

    /// <summary>
    /// Source of truth for the player's wanted level (global manager under ///Manager///, Singleton: shop doors, HUD and units
    /// read it from anywhere).
    /// - Crimes come from <see cref="CrimeReporter"/>. Unless the victim is the police, a crime only counts when police witness it
    ///   (<see cref="PoliceDispatcher.TryWitness"/>: officers and police cars within sight radius with line of sight, gunshots within
    ///   hearing radius). Each crime has a rule in <see cref="PoliceSettings"/> (minimum stars, escalation, lethal or not).
    /// - States: Pursuit (police follow, no shooting, busting possible), Lethal (shoot on sight), Searching (nobody has seen the
    ///   player for Lost Sight Seconds: flashing stars and a search area on the map; seen again = back to the chase; unseen for the
    ///   search time = cleared).
    /// - Busting: an officer on foot within Bust Radius while the player is slow fills the arrest bar; moving away drains it.
    ///   Full = <see cref="OnBusted"/> (<see cref="BustedSequence"/> applies the consequences and calls <see cref="CompleteBust"/>).
    /// - Death clears the wanted level.
    /// Update does nothing while the player is not wanted; visibility is checked at Visibility Check Interval.
    /// </summary>
    [DisallowMultipleComponent]
    public class WantedManager : MonoBehaviour
    {
        public const int MaxStars = 5;

        [SerializeField] private PoliceSettings settings;
        [SerializeField] private PoliceDispatcher dispatcher;

        [Header("Player")]
        [SerializeField] private PlayerManager playerManager;
        [SerializeField] private PlayerVehicleInteraction vehicleInteraction;
        [SerializeField] private Rigidbody playerBody;

        [Header("Map")]
        [Tooltip("Area blip (MapBlip with a world radius) shown at the last known position while police search.")]
        [SerializeField] private MapBlip searchAreaBlip;

        public static WantedManager Instance { get; private set; }
        /// <summary>True while the player has a wanted level.</summary>
        public static bool IsPlayerWanted => Instance != null && Instance.Stars > 0;

        /// <summary>Raised when the stars or the state change. Args: stars, state.</summary>
        public static event Action<int, WantedState> OnWantedChanged;
        /// <summary>Raised when the arrest bar changes. Arg: 0..1.</summary>
        public static event Action<float> OnBustProgressChanged;
        /// <summary>Raised once when the arrest bar fills.</summary>
        public static event Action OnBusted;
        /// <summary>Raised for every crime the police saw (after the wanted level was updated).</summary>
        public static event Action<CrimeReport> OnCrimeWitnessed;

        public PoliceSettings Settings => settings;
        public int Stars { get; private set; }
        public WantedState State { get; private set; } = WantedState.Clear;
        /// <summary>Police shoot on sight (kept through the search until the wanted level is lost).</summary>
        public bool IsLethal { get; private set; }
        public bool IsSearching => State == WantedState.Searching;
        /// <summary>Where police last saw the player (units search here).</summary>
        public Vector3 LastKnownPosition { get; private set; }
        public float BustProgress { get; private set; }
        /// <summary>True from the arrest until the consequences have been applied.</summary>
        public bool IsBustInProgress { get; private set; }
        public float SearchTimeLeft { get; private set; }

        public Transform Player => playerManager != null ? playerManager.transform : null;
        public Vector3 PlayerPosition => playerManager.transform.position;
        public bool PlayerInVehicle => vehicleInteraction != null && vehicleInteraction.IsInVehicle && vehicleInteraction.CurrentVehicle != null;
        /// <summary>What police aim and drive at: the player's car while he drives, otherwise the player.</summary>
        public Transform PlayerTarget => PlayerInVehicle ? vehicleInteraction.CurrentVehicle.transform : playerManager.transform;
        public IVehicleController PlayerVehicle => PlayerInVehicle ? vehicleInteraction.CurrentVehicle : null;
        public PlayerManager PlayerManager => playerManager;

        /// <summary>Flat speed (m/s) of the player or of his car.</summary>
        public float PlayerSpeed
        {
            get
            {
                if (PlayerInVehicle) return vehicleInteraction.CurrentVehicle.CurrentSpeed;
                if (playerBody == null) return 0f;
                Vector3 v = playerBody.linearVelocity;
                v.y = 0f;
                return v.magnitude;
            }
        }

        private float lastSeenTime;
        private float trackedUntil;
        private float nextVisibilityCheck;
        private bool valid;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[{nameof(WantedManager)}] A second manager on '{name}' was disabled.", this);
                enabled = false;
                return;
            }
            valid = settings != null && dispatcher != null && playerManager != null && vehicleInteraction != null && playerBody != null;
            if (!valid)
            {
                Debug.LogError($"[{nameof(WantedManager)}] Missing references on '{name}' (settings, dispatcher, PlayerManager, " +
                               "PlayerVehicleInteraction, player Rigidbody). The police system is off.", this);
                enabled = false;
                return;
            }
            Instance = this;
            if (searchAreaBlip != null) searchAreaBlip.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (!valid) return;
            CrimeReporter.OnCrimeCommitted += HandleCrime;
            if (playerManager.Health != null) playerManager.Health.OnDied += HandlePlayerDied;
        }

        private void OnDisable()
        {
            CrimeReporter.OnCrimeCommitted -= HandleCrime;
            if (playerManager != null && playerManager.Health != null) playerManager.Health.OnDied -= HandlePlayerDied;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (Stars == 0 || IsBustInProgress) return;
            float now = Time.time;

            if (now >= nextVisibilityCheck)
            {
                nextVisibilityCheck = now + settings.visibilityCheckInterval;
                UpdateVisibility(now);
                if (Stars == 0) return;
            }

            if (State == WantedState.Searching)
            {
                SearchTimeLeft -= Time.deltaTime;
                if (SearchTimeLeft <= 0f)
                {
                    ClearWanted();
                    return;
                }
            }

            UpdateBust(Time.deltaTime);
        }

        /// <summary>Sets the wanted level directly (designer tools, missions, tests).</summary>
        public void SetWanted(int stars, bool lethal)
        {
            if (!valid) return;
            stars = Mathf.Clamp(stars, 0, MaxStars);
            if (stars == 0 || playerManager.Health.IsDead)
            {
                ClearWanted();
                return;
            }
            Stars = stars;
            IsLethal = lethal || stars >= settings.lethalFromStars;
            trackedUntil = Time.time + settings.crimeTrackingSeconds;
            MarkSeen();
            SetState(IsLethal ? WantedState.Lethal : WantedState.Pursuit, true);
        }

        /// <summary>Loses the wanted level: police stop and go back to normal.</summary>
        public void ClearWanted()
        {
            bool changed = Stars != 0 || State != WantedState.Clear;
            Stars = 0;
            IsLethal = false;
            trackedUntil = 0f;
            SearchTimeLeft = 0f;
            State = WantedState.Clear;
            SetBustProgress(0f);
            if (searchAreaBlip != null) searchAreaBlip.gameObject.SetActive(false);
            if (changed) OnWantedChanged?.Invoke(Stars, State);
        }

        /// <summary>Called by <see cref="BustedSequence"/> once the arrest consequences are applied.</summary>
        public void CompleteBust()
        {
            IsBustInProgress = false;
            ClearWanted();
        }

        private void HandleCrime(CrimeReport report)
        {
            if (IsBustInProgress || playerManager.Health.IsDead) return;
            if (InteriorTravelManager.Instance != null && InteriorTravelManager.Instance.IsInside) return;
            if (!settings.TryGetRule(report.Type, out PoliceSettings.CrimeRule rule)) return;
            int stars = Stars > 0 ? Mathf.Max(Stars + rule.addStarsWhileWanted, rule.minStars) : rule.minStars;
            stars = Mathf.Clamp(Mathf.Max(stars, Stars), 1, MaxStars);
            if (!report.AgainstPolice && !report.PhonedIn && !dispatcher.TryWitness(report, stars)) return;

            Stars = stars;
            IsLethal |= rule.lethal || Stars >= settings.lethalFromStars;
            trackedUntil = Time.time + settings.crimeTrackingSeconds;
            MarkSeen();
            SetState(IsLethal ? WantedState.Lethal : WantedState.Pursuit, true);
            OnCrimeWitnessed?.Invoke(report);
        }

        private void UpdateVisibility(float now)
        {
            // Right after a crime the police know where he is even if no unit can see him (the witnesses may all be dead).
            if (now < trackedUntil || dispatcher.CanAnyUnitSee(PlayerPosition))
            {
                MarkSeen();
                if (State == WantedState.Searching) SetState(IsLethal ? WantedState.Lethal : WantedState.Pursuit, false);
                return;
            }
            if (State != WantedState.Searching && now - lastSeenTime >= settings.lostSightSeconds)
            {
                SearchTimeLeft = settings.SearchSecondsFor(Stars);
                SetState(WantedState.Searching, false);
            }
        }

        private void MarkSeen()
        {
            lastSeenTime = Time.time;
            LastKnownPosition = PlayerPosition;
        }

        private void UpdateBust(float deltaTime)
        {
            bool canBust = State != WantedState.Searching && !(settings.bustOnlyWhenNonLethal && IsLethal) && !playerManager.Health.IsDead;
            bool closing = false;
            if (canBust)
            {
                bool inVehicle = PlayerInVehicle;
                float radius = inVehicle ? settings.bustRadiusInVehicle : settings.bustRadiusOnFoot;
                float maxSpeed = inVehicle ? settings.bustMaxSpeedInVehicle : settings.bustMaxSpeedOnFoot;
                closing = PlayerSpeed <= maxSpeed && dispatcher.IsOfficerWithin(PlayerTarget.position, radius);
            }
            float progress = closing
                ? BustProgress + deltaTime / settings.bustFillSeconds
                : BustProgress - deltaTime / settings.bustDrainSeconds;
            SetBustProgress(Mathf.Clamp01(progress));
            if (BustProgress >= 1f) Bust();
        }

        private void Bust()
        {
            IsBustInProgress = true;
            OnBusted?.Invoke();
        }

        private void SetBustProgress(float value)
        {
            if (Mathf.Approximately(value, BustProgress)) return;
            BustProgress = value;
            OnBustProgressChanged?.Invoke(value);
        }

        private void SetState(WantedState state, bool forceEvent)
        {
            bool changed = state != State;
            State = state;
            if (searchAreaBlip != null)
            {
                bool searching = state == WantedState.Searching;
                if (searching)
                {
                    searchAreaBlip.transform.position = LastKnownPosition;
                    searchAreaBlip.SetWorldRadius(settings.SearchRadiusFor(Stars));
                }
                searchAreaBlip.gameObject.SetActive(searching);
            }
            if (state == WantedState.Searching) SetBustProgress(0f);
            if (changed || forceEvent) OnWantedChanged?.Invoke(Stars, State);
        }

        private void HandlePlayerDied() => ClearWanted();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            OnWantedChanged = null;
            OnBustProgressChanged = null;
            OnBusted = null;
            OnCrimeWitnessed = null;
        }
    }
}
