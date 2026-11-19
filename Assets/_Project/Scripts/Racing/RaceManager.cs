using System;
using System.Collections;
using System.Collections.Generic;
using OpenworldCrime.Economy;
using OpenworldCrime.Player;
using OpenworldCrime.Police;
using OpenworldCrime.UI;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Racing
{
    public enum RaceState { Idle, Starting, Countdown, Racing, Results }

    public struct RaceResult
    {
        public RaceDefinition race;
        public int position;
        public int racers;
        public float time;
        public float bestTime;
        public bool newBest;
        public int reward;
    }

    /// <summary>
    /// Street races (global manager under ///Manager///). Owns the race flow and state:
    /// - Idle: offers a race when the player drives into an available start (cars only, not while wanted); start points are
    ///   switched on / greyed out by the time of day (Hamza's CityDayNightController, whatever drives it).
    /// - Start: fade out, no ambient traffic (iTS cars and traffic bikes taken off the streets and not spawned again until the
    ///   race is over; parked cars stay), player's car and 3-5 AI opponents on the grid, fade in, 3-2-1-GO with the cars held
    ///   until GO.
    /// - Race: checkpoints in order (next one bright, the one after dim, next one on the map), position, lap, time, missed
    ///   checkpoint and wrong way warnings, respawn at the last checkpoint, quit. Leaving or wrecking the car fails the race.
    /// - Finish: results, best time (SaveManager), cash by position (MoneyManager); opponents and markers are cleaned up and the
    ///   traffic comes back (also after a failed or abandoned race).
    /// The HUD (<see cref="RaceHudView"/>) follows the events and properties here and sends the player's requests.
    /// </summary>
    [DisallowMultipleComponent]
    public class RaceManager : MonoBehaviour
    {
        public static RaceManager Instance { get; private set; }
        /// <summary>True from the start fade until the race is cleaned up (other systems, e.g. character switching, stay out).</summary>
        public static bool IsRaceActive => Instance != null && Instance.State != RaceState.Idle;

        [Header("Scene")]
        [SerializeField] private RaceStartPoint[] startPoints = new RaceStartPoint[0];
        [Tooltip("Day / night state (Hamza's controller; the sky cycle, if any, drives it).")]
        [SerializeField] private CityDayNightController dayNight;
        [SerializeField] private PlayerVehicleInteraction vehicleInteraction;
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private ScreenFader fader;
        [Tooltip("iTS traffic spawner (cars and traffic bikes), emptied during a race. Empty = the scene's one, found at start.")]
        [SerializeField] private TSTrafficSpawner trafficSpawner;

        [Header("Race objects")]
        [Tooltip("Drivable cars the opponents use (RCC cars with an RCCVehicleAdapter).")]
        [SerializeField] private RCCVehicleAdapter[] opponentPrefabs = new RCCVehicleAdapter[0];
        [SerializeField] private string[] opponentNames = { "Vinnie", "Rosa", "Dex", "Kaito", "Mara" };
        [SerializeField] private RaceCheckpointMarker nextMarker;
        [SerializeField] private RaceCheckpointMarker followingMarker;
        [Tooltip("Map / minimap blip moved to the next checkpoint.")]
        [SerializeField] private MapBlip checkpointBlip;

        [Header("Rules")]
        [SerializeField, Min(3f)] private float checkpointRadius = 13f;
        [Tooltip("Metres past a checkpoint before 'missed checkpoint' is shown.")]
        [SerializeField, Min(10f)] private float missedDistance = 70f;
        [SerializeField, Range(0.5f, 1.2f)] private Vector2 opponentSkill = new Vector2(0.9f, 1f);
        [Tooltip("Opponents this far behind the player drive faster, this far ahead slower (light rubber-banding).")]
        [SerializeField] private float catchUpGap = 60f;
        [SerializeField] private float waitGap = 90f;
        [SerializeField, Range(1f, 1.3f)] private float catchUpScale = 1.12f;
        [SerializeField, Range(0.7f, 1f)] private float waitScale = 0.9f;
        [SerializeField, Min(1f)] private float resultsSeconds = 12f;
        [SerializeField, Min(0.5f)] private float respawnCooldown = 2f;

        public event Action<RaceStartPoint, string> OnPromptChanged;
        public event Action<RaceState> OnStateChanged;
        /// <summary>3, 2, 1, then 0 for GO.</summary>
        public event Action<int> OnCountdown;
        public event Action<string> OnMessage;
        public event Action<RaceResult> OnFinished;

        public RaceState State { get; private set; } = RaceState.Idle;
        public RaceDefinition CurrentRace { get; private set; }
        public float Elapsed => State == RaceState.Racing ? Time.time - startTime : finishTime;
        public int PlayerPosition { get; private set; } = 1;
        public int Racers => opponents.Count + 1;
        public int CheckpointsPassed { get; private set; }
        public int CurrentLap => CurrentRace == null ? 0 : Mathf.Min(CheckpointsPassed / Mathf.Max(1, CurrentRace.Checkpoints.Length), CurrentRace.Laps - 1) + 1;
        public bool WrongWay { get; private set; }
        public RaceStartPoint PromptPoint { get; private set; }
        public string PromptBlockedReason { get; private set; }

        private readonly List<RaceOpponentDriver> opponents = new List<RaceOpponentDriver>();
        private float[] distances;
        private RCCVehicleAdapter playerCar;
        private RCC_CarControllerV3 playerController;
        private Rigidbody playerBody;
        private int playerIndex;
        private int playerLap;
        private float startTime;
        private float finishTime;
        private float nextCheck;
        private float nextMissedMessage;
        private float wrongWayTimer;
        private float lastRespawn;
        private bool lastNight;
        private bool nightKnown;
        private Coroutine flow;
        private bool trafficSuspended;
        private int savedTrafficAmount;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[{nameof(RaceManager)}] A second RaceManager on '{name}' was disabled.", this);
                enabled = false;
                return;
            }
            Instance = this;
            if (vehicleInteraction == null || fader == null || nextMarker == null || followingMarker == null || opponentPrefabs.Length == 0)
                Debug.LogError($"[{nameof(RaceManager)}] Missing references on '{name}' (vehicle interaction, fader, markers, opponent prefabs).", this);
            if (dayNight == null) Debug.LogWarning($"[{nameof(RaceManager)}] No CityDayNightController: every race counts as daytime.", this);
        }

        private void Start()
        {
            // One lookup at start when the spawner is not assigned in the Inspector.
            if (trafficSpawner == null) trafficSpawner = FindFirstObjectByType<TSTrafficSpawner>();
            if (trafficSpawner == null) Debug.LogWarning($"[{nameof(RaceManager)}] No TSTrafficSpawner: races keep the ambient traffic.", this);
            HideRaceObjects();
            RefreshAvailability(true);
        }

        private void OnDestroy()
        {
            ResumeTraffic();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + 0.25f;
                RefreshAvailability(false);
                if (State == RaceState.Idle) UpdatePrompt();
            }
            if (State == RaceState.Countdown || State == RaceState.Racing)
            {
                string failure = CheckFailure();
                if (failure != null) { Fail(failure); return; }
            }
            if (State == RaceState.Racing) UpdateRace();
            // Kept at zero for the whole race (e.g. a graphics profile change re-applies the traffic amount).
            if (trafficSuspended && trafficSpawner != null && trafficSpawner.Amount != 0)
            {
                savedTrafficAmount = trafficSpawner.Amount;
                trafficSpawner.UpdateCarAmountInstantly(0);
            }
        }

        // ---------------------------------------------------------------- traffic

        // All iTS traffic (cars and traffic bikes) back to the pool and none spawned: the spawner's own runtime API, so iTS
        // releases lane and traffic-light reservations itself. Parked / abandoned drivable cars are not iTS traffic and stay.
        private void SuspendTraffic()
        {
            if (trafficSuspended || trafficSpawner == null) return;
            savedTrafficAmount = trafficSpawner.Amount;
            trafficSpawner.UpdateCarAmountInstantly(0);
            trafficSuspended = true;
        }

        private void ResumeTraffic()
        {
            if (!trafficSuspended || trafficSpawner == null) return;
            trafficSuspended = false;
            trafficSpawner.UpdateCarAmountInstantly(savedTrafficAmount);
        }

        // ---------------------------------------------------------------- availability / prompt

        private void RefreshAvailability(bool force)
        {
            bool night = dayNight != null && dayNight.IsNight;
            if (!force && nightKnown && night == lastNight) return;
            nightKnown = true;
            lastNight = night;
            foreach (RaceStartPoint point in startPoints)
                if (point != null && point.Race != null) point.SetAvailable(point.Race.IsAvailable(night));
        }

        private void UpdatePrompt()
        {
            RaceStartPoint near = null;
            string blocked = null;
            if (vehicleInteraction != null && vehicleInteraction.IsInVehicle && vehicleInteraction.CurrentVehicle != null)
            {
                Vector3 p = vehicleInteraction.CurrentVehicle.transform.position;
                foreach (RaceStartPoint point in startPoints)
                {
                    if (point == null || point.Race == null) continue;
                    if ((point.Position - p).sqrMagnitude > point.TriggerRadius * point.TriggerRadius) continue;
                    near = point;
                    break;
                }
                if (near != null)
                {
                    if (!near.IsAvailable) blocked = near.Race.Availability == RaceAvailability.NightOnly ? "This race only runs at night" : "This race only runs by day";
                    else if (!(vehicleInteraction.CurrentVehicle is RCCVehicleAdapter car) || car.UsesQuickMount) blocked = "Street races need a car";
                    else if (WantedManager.IsPlayerWanted) blocked = "Lose the cops before racing";
                }
            }
            if (near == PromptPoint && blocked == PromptBlockedReason) return;
            PromptPoint = near;
            PromptBlockedReason = blocked;
            OnPromptChanged?.Invoke(near, blocked);
        }

        // ---------------------------------------------------------------- player requests (HUD)

        /// <summary>Start the race offered by the prompt.</summary>
        public void RequestStart()
        {
            if (State != RaceState.Idle || PromptPoint == null) return;
            UpdatePrompt();
            if (PromptPoint == null) return;
            if (PromptBlockedReason != null) { OnMessage?.Invoke(PromptBlockedReason); return; }
            var car = vehicleInteraction.CurrentVehicle as RCCVehicleAdapter;
            if (car == null) return;
            flow = StartCoroutine(StartRoutine(PromptPoint.Race, car));
        }

        /// <summary>Back to the last checkpoint (stuck or flipped).</summary>
        public void RequestRespawn()
        {
            if (State != RaceState.Racing || Time.time - lastRespawn < respawnCooldown || playerBody == null) return;
            lastRespawn = Time.time;
            int ordinal = CheckpointsPassed - 1;
            if (ordinal < 0) PlaceOnGrid(playerBody, 0);
            else
            {
                int index = CurrentRace.Checkpoints[ordinal % CurrentRace.Checkpoints.Length];
                PlaceOnRoute(playerBody, index);
                playerIndex = index;
            }
            OnMessage?.Invoke("Back on track");
        }

        public void RequestQuit()
        {
            if (State == RaceState.Countdown || State == RaceState.Racing) Fail("Race abandoned");
        }

        /// <summary>Closes the results (or they close by themselves).</summary>
        public void CloseResults()
        {
            if (State == RaceState.Results) EndRace();
        }

        // ---------------------------------------------------------------- start

        private IEnumerator StartRoutine(RaceDefinition race, RCCVehicleAdapter car)
        {
            CurrentRace = race;
            playerCar = car;
            playerController = car.GetComponent<RCC_CarControllerV3>();
            playerBody = car.GetComponent<Rigidbody>();
            SetState(RaceState.Starting);
            PromptPoint = null;
            OnPromptChanged?.Invoke(null, null);
            foreach (RaceStartPoint point in startPoints) if (point != null) point.SetMarkerShown(false);

            yield return fader.FadeTo(1f, 0.6f, race.DisplayName.ToUpperInvariant());

            distances = Cumulative(race.RoutePoints);
            SuspendTraffic();
            if (playerController != null) playerController.SetCanControl(false);
            PlaceOnGrid(playerBody, 0);
            playerIndex = 0;
            playerLap = 0;
            CheckpointsPassed = 0;
            WrongWay = false;
            PlayerPosition = race.Opponents + 1;

            int count = Mathf.Min(race.Opponents, race.GridPositions.Length - 1);
            var cars = new List<RCCVehicleAdapter>();
            for (int i = 0; i < count; i++)
            {
                RCCVehicleAdapter prefab = opponentPrefabs[UnityEngine.Random.Range(0, opponentPrefabs.Length)];
                if (prefab == null) continue;
                RCCVehicleAdapter opponent = Instantiate(prefab, race.GridPositions[i + 1], Quaternion.Euler(0f, race.GridYaws[i + 1], 0f));
                opponent.name = "Race Opponent " + (i + 1);
                opponent.SetOccupied(true); // not enterable / stealable during the race
                opponent.SetDriverVisualActive(true);
                cars.Add(opponent);
            }
            // The cars' own Start (parks them, engine off) runs on the next frame; the drivers take over after it.
            yield return null;
            yield return null;
            for (int i = 0; i < cars.Count; i++)
            {
                var controller = cars[i].GetComponent<RCC_CarControllerV3>();
                if (controller == null) { Destroy(cars[i].gameObject); continue; }
                var driver = cars[i].gameObject.AddComponent<RaceOpponentDriver>();
                string driverName = opponentNames.Length > 0 ? opponentNames[i % opponentNames.Length] : "Racer " + (i + 1);
                driver.Setup(controller, race, distances, UnityEngine.Random.Range(opponentSkill.x, opponentSkill.y), driverName);
                driver.SetDriving(false);
                opponents.Add(driver);
            }
            ShowCheckpoints();
            yield return new WaitForSeconds(0.3f);
            yield return fader.FadeTo(0f, 0.6f);

            SetState(RaceState.Countdown);
            for (int n = 3; n >= 1; n--)
            {
                OnCountdown?.Invoke(n);
                yield return new WaitForSeconds(1f);
                if (State != RaceState.Countdown) yield break;
            }
            OnCountdown?.Invoke(0);
            if (playerController != null) playerController.SetCanControl(true);
            foreach (RaceOpponentDriver d in opponents) if (d != null) d.SetDriving(true);
            startTime = Time.time;
            SetState(RaceState.Racing);
            flow = null;
        }

        private void PlaceOnGrid(Rigidbody body, int slot)
        {
            if (body == null || CurrentRace.GridPositions.Length == 0) return;
            slot = Mathf.Clamp(slot, 0, CurrentRace.GridPositions.Length - 1);
            Place(body, CurrentRace.GridPositions[slot], Quaternion.Euler(0f, CurrentRace.GridYaws[slot], 0f));
        }

        private void PlaceOnRoute(Rigidbody body, int index)
        {
            Vector3[] route = CurrentRace.RoutePoints;
            int i = Mathf.Clamp(index, 0, route.Length - 1);
            Vector3 forward = Direction(i);
            Place(body, route[i], Quaternion.LookRotation(forward, Vector3.up));
        }

        private static void Place(Rigidbody body, Vector3 position, Quaternion rotation)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            position += Vector3.up * 0.6f;
            body.position = position;
            body.rotation = rotation;
            body.transform.SetPositionAndRotation(position, rotation);
        }

        // ---------------------------------------------------------------- race

        private string CheckFailure()
        {
            if (playerCar == null || playerCar.IsWrecked) return "Your car was wrecked";
            if (playerHealth != null && playerHealth.IsDead) return "Wasted";
            if (vehicleInteraction == null || vehicleInteraction.State != VehicleInteractionState.InVehicle
                || !ReferenceEquals(vehicleInteraction.CurrentVehicle, playerCar)) return "You left your car";
            return null;
        }

        private void UpdateRace()
        {
            RaceDefinition race = CurrentRace;
            Vector3[] route = race.RoutePoints;
            Vector3 p = playerBody.position;
            TrackPlayer(p);

            // Checkpoints in order.
            int cpCount = race.Checkpoints.Length;
            int cpIndex = race.Checkpoints[CheckpointsPassed % cpCount];
            Vector3 cp = route[cpIndex];
            Vector3 flat = cp - p;
            if (Mathf.Abs(flat.y) < 8f)
            {
                flat.y = 0f;
                if (flat.sqrMagnitude < checkpointRadius * checkpointRadius) PassCheckpoint();
            }
            if (State != RaceState.Racing) return;

            // Missed checkpoint: the player is well along the route past it.
            float ahead = distances[playerIndex] - distances[cpIndex];
            if (race.IsCircuit && ahead < -race.LapLength * 0.5f) ahead += race.LapLength;
            if (ahead > missedDistance && ahead < race.LapLength * 0.5f && Time.time >= nextMissedMessage)
            {
                nextMissedMessage = Time.time + 4f;
                OnMessage?.Invoke("Missed a checkpoint - go back");
            }

            // Wrong way.
            Vector3 velocity = playerBody.linearVelocity;
            velocity.y = 0f;
            bool backwards = velocity.sqrMagnitude > 16f && Vector3.Dot(velocity.normalized, Direction(playerIndex)) < -0.3f;
            wrongWayTimer = backwards ? wrongWayTimer + Time.deltaTime : 0f;
            WrongWay = wrongWayTimer > 1f;

            // Opponents: finish, rubber-banding, positions.
            float playerProgress = PlayerProgress();
            float total = race.LapLength * race.Laps;
            int ahead1 = 0;
            for (int i = opponents.Count - 1; i >= 0; i--)
            {
                RaceOpponentDriver d = opponents[i];
                if (d == null) { opponents.RemoveAt(i); continue; }
                VehicleHealth health = d.GetComponent<VehicleHealth>();
                if (health != null && health.IsDestroyed) { d.Finished = true; d.FinishTime = -1f; }
                if (!d.Finished && d.Progress >= total - 4f) { d.Finished = true; d.FinishTime = Elapsed; }
                if (d.Finished && d.FinishTime >= 0f) { ahead1++; continue; }
                if (d.Finished) continue; // wrecked: out of the race
                float gap = d.Progress - playerProgress;
                d.SetSpeedScale(gap < -catchUpGap ? catchUpScale : gap > waitGap ? waitScale : 1f);
                if (d.Progress > playerProgress) ahead1++;
            }
            PlayerPosition = ahead1 + 1;
        }

        private void TrackPlayer(Vector3 p)
        {
            Vector3[] route = CurrentRace.RoutePoints;
            int best = playerIndex;
            float bestD = (route[playerIndex] - p).sqrMagnitude;
            // Mostly forwards; a wider search when the player is far from where he was (shortcut, respawn, detour).
            int range = bestD > 40f * 40f ? 150 : 14;
            for (int k = -4; k <= range; k++)
            {
                int i = playerIndex + k;
                int lapShift = 0;
                if (i >= route.Length) { if (!CurrentRace.IsCircuit) break; i -= route.Length; lapShift = 1; }
                if (i < 0) { if (!CurrentRace.IsCircuit) continue; i += route.Length; lapShift = -1; }
                float d = (route[i] - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; playerLapShift = lapShift; }
            }
            if (best != playerIndex)
            {
                playerLap += playerLapShift;
                playerIndex = best;
            }
            playerLapShift = 0;
        }

        private int playerLapShift;

        private float PlayerProgress()
        {
            RaceDefinition race = CurrentRace;
            float raw = Mathf.Max(0, playerLap) * race.LapLength + distances[playerIndex];
            // Never ahead of the next checkpoint the player still has to pass (no ranking from shortcuts).
            int cpCount = race.Checkpoints.Length;
            int ordinal = Mathf.Min(CheckpointsPassed, race.TotalCheckpoints - 1);
            float cap = (ordinal / cpCount) * race.LapLength + distances[race.Checkpoints[ordinal % cpCount]] + 10f;
            return Mathf.Min(raw, cap);
        }

        private void PassCheckpoint()
        {
            CheckpointsPassed++;
            if (CheckpointsPassed >= CurrentRace.TotalCheckpoints) { Finish(); return; }
            int cpCount = CurrentRace.Checkpoints.Length;
            if (CurrentRace.IsCircuit && CheckpointsPassed % cpCount == 0)
                OnMessage?.Invoke(CheckpointsPassed / cpCount + 1 == CurrentRace.Laps ? "Final lap" : "Lap " + (CheckpointsPassed / cpCount + 1));
            ShowCheckpoints();
        }

        private void ShowCheckpoints()
        {
            RaceDefinition race = CurrentRace;
            int cpCount = race.Checkpoints.Length;
            int total = race.TotalCheckpoints;
            if (CheckpointsPassed < total)
            {
                int idx = race.Checkpoints[CheckpointsPassed % cpCount];
                nextMarker.Show(race.RoutePoints[idx], Direction(idx), true, CheckpointsPassed == total - 1);
                if (checkpointBlip != null)
                {
                    checkpointBlip.transform.position = race.RoutePoints[idx];
                    checkpointBlip.gameObject.SetActive(true);
                }
            }
            else nextMarker.Hide();
            if (CheckpointsPassed + 1 < total)
            {
                int idx = race.Checkpoints[(CheckpointsPassed + 1) % cpCount];
                followingMarker.Show(race.RoutePoints[idx], Direction(idx), false, false);
            }
            else followingMarker.Hide();
        }

        private Vector3 Direction(int index)
        {
            Vector3[] route = CurrentRace.RoutePoints;
            int a = Mathf.Clamp(index, 0, route.Length - 1);
            int b = a + 1;
            if (b >= route.Length) { b = CurrentRace.IsCircuit ? 0 : a; a = CurrentRace.IsCircuit ? a : a - 1; }
            Vector3 d = route[b] - route[Mathf.Max(0, a)];
            d.y = 0f;
            return d.sqrMagnitude > 0.001f ? d.normalized : Vector3.forward;
        }

        // ---------------------------------------------------------------- end

        private void Finish()
        {
            finishTime = Time.time - startTime;
            int position = 1;
            foreach (RaceOpponentDriver d in opponents) if (d != null && d.Finished && d.FinishTime >= 0f) position++;
            PlayerPosition = position;
            var result = new RaceResult
            {
                race = CurrentRace,
                position = position,
                racers = Racers,
                time = finishTime,
                reward = CurrentRace.RewardFor(position)
            };
            if (SaveManager.Instance != null)
            {
                float previous = SaveManager.Instance.GetRaceBestTime(CurrentRace.RaceId);
                result.newBest = SaveManager.Instance.SubmitRaceTime(CurrentRace.RaceId, finishTime);
                result.bestTime = result.newBest ? finishTime : previous;
            }
            if (result.reward > 0 && MoneyManager.Instance != null) MoneyManager.Instance.Add(result.reward);
            nextMarker.Hide();
            followingMarker.Hide();
            if (checkpointBlip != null) checkpointBlip.gameObject.SetActive(false);
            foreach (RaceOpponentDriver d in opponents) if (d != null && !d.Finished) d.SetSpeedScale(1f);
            SetState(RaceState.Results);
            OnFinished?.Invoke(result);
            flow = StartCoroutine(ResultsTimeout());
        }

        private IEnumerator ResultsTimeout()
        {
            yield return new WaitForSeconds(resultsSeconds);
            flow = null;
            if (State == RaceState.Results) EndRace();
        }

        private void Fail(string reason)
        {
            OnMessage?.Invoke("Race failed: " + reason);
            EndRace();
        }

        private void EndRace()
        {
            if (flow != null) { StopCoroutine(flow); flow = null; }
            if (playerController != null && State == RaceState.Countdown) playerController.SetCanControl(true);
            foreach (RaceOpponentDriver d in opponents) if (d != null) Destroy(d.gameObject);
            opponents.Clear();
            HideRaceObjects();
            ResumeTraffic();
            foreach (RaceStartPoint point in startPoints) if (point != null) point.SetMarkerShown(true);
            CurrentRace = null;
            playerCar = null;
            playerController = null;
            playerBody = null;
            WrongWay = false;
            if (fader != null && fader.IsBlack) StartCoroutine(fader.FadeTo(0f, 0.4f));
            SetState(RaceState.Idle);
        }

        private void HideRaceObjects()
        {
            if (nextMarker != null) nextMarker.Hide();
            if (followingMarker != null) followingMarker.Hide();
            if (checkpointBlip != null) checkpointBlip.gameObject.SetActive(false);
        }

        private void SetState(RaceState state)
        {
            State = state;
            OnStateChanged?.Invoke(state);
        }

        private static float[] Cumulative(Vector3[] points)
        {
            var d = new float[points.Length];
            for (int i = 1; i < points.Length; i++) d[i] = d[i - 1] + Vector3.Distance(points[i - 1], points[i]);
            return d;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;
    }
}
