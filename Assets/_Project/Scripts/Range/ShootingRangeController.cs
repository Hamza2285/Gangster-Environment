using System;
using System.Collections;
using Invector.vCamera;
using Invector.vCharacterController;
using Invector.vShooter;
using OpenworldCrime.Economy;
using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.Range
{
    using PlayerManager = OpenworldCrime.Player.PlayerManager; // SharedAssets has a global PlayerManager

    /// <summary>Result of one range challenge.</summary>
    public struct RangeResult
    {
        public int score;
        public int hits;
        public int headshots;
        public int shown;
        public int reward;
        public string rating;
        public bool aborted;
    }

    /// <summary>
    /// GTA-style shooting range challenge (lives in the gun shop interior scene). Started from the lane booth
    /// (<see cref="RangeLane"/>): the player is walked to the fixed shooting spot at the bench, held there (movement locked,
    /// aiming forced so shots clear the bench) and targets pop up at different distances, some sliding on rails. Head / torso
    /// hits score points; at the end a rating and a cash reward are given. Ammo is free during the challenge. Esc / back or
    /// the HUD's Quit button cancels it. Outside a challenge every target stands for free practice and pops back up after a hit.
    /// UI in the city scene follows the static events.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShootingRangeController : MonoBehaviour
    {
        [SerializeField] private RangeTarget[] targets = new RangeTarget[0];

        [Header("Shooting Spot")]
        [Tooltip("Where the player stands during a challenge (feet position, forward = downrange).")]
        [SerializeField] private Transform shootingSpot;
        [SerializeField, Min(0.05f)] private float moveToSpotTime = 0.45f;
        [Tooltip("Camera pitch while settling at the spot (degrees, + = down).")]
        [SerializeField] private float cameraPitch = 2f;

        [Header("Challenge")]
        [SerializeField, Min(5f)] private float duration = 40f;
        [SerializeField, Min(0.2f)] private float popInterval = 1.1f;
        [SerializeField, Min(0.3f)] private float targetUpTime = 2.6f;
        [Tooltip("Chance a raised target slides on its rail.")]
        [SerializeField, Range(0f, 1f)] private float slideChance = 0.35f;
        [SerializeField, Min(0f)] private float countdown = 3f;
        [SerializeField] private int torsoPoints = 10;
        [SerializeField] private int headPoints = 25;

        [Header("Rating (score needed) and reward")]
        [SerializeField] private int bronzeScore = 120;
        [SerializeField] private int silverScore = 260;
        [SerializeField] private int goldScore = 400;
        [SerializeField] private int bronzeReward = 250;
        [SerializeField] private int silverReward = 600;
        [SerializeField] private int goldReward = 1200;

        [Header("Audio")]
        [SerializeField] private AudioSource buzzer;

        [Header("Practice")]
        [SerializeField, Min(0.1f)] private float practiceResetTime = 1.5f;

        public static event Action OnChallengeStarted;
        /// <summary>Countdown before the start. Arg: seconds left (3, 2, 1).</summary>
        public static event Action<int> OnCountdown;
        /// <summary>Args: time left, score, hits.</summary>
        public static event Action<float, int, int> OnChallengeUpdated;
        public static event Action<RangeResult> OnChallengeEnded;
        private static event Action QuitRequested;

        /// <summary>Asks the running challenge to cancel (the city HUD's Quit button; interiors are separate scenes).</summary>
        public static void RequestQuit() => QuitRequested?.Invoke();

        public bool IsRunning { get; private set; }
        public bool PlayerInLane { get; private set; }

        private PlayerManager player;
        private vShooterManager shooter;
        private vShooterMeleeInput shooterInput;
        private InvectorGamePlayer gamePlayer;
        private PlayerWeaponController weapons;
        private Coroutine routine;
        private float[] downTimers;
        private RangeResult result;
        private bool alwaysAimBefore;

        private void Awake()
        {
            downTimers = new float[targets.Length];
            if (shootingSpot == null) Debug.LogWarning($"[{nameof(ShootingRangeController)}] No shooting spot on '{name}': the player will not be placed at the bench.", this);
        }

        private void OnEnable()
        {
            for (int i = 0; i < targets.Length; i++) if (targets[i] != null) targets[i].OnHit += HandleHit;
            QuitRequested += HandleQuitRequested;
        }

        private void OnDisable()
        {
            for (int i = 0; i < targets.Length; i++) if (targets[i] != null) targets[i].OnHit -= HandleHit;
            QuitRequested -= HandleQuitRequested;
            if (IsRunning) EndChallenge(true);
        }

        private void Start()
        {
            for (int i = 0; i < targets.Length; i++) if (targets[i] != null) targets[i].Raise(false); // practice
        }

        // Per-frame: practice targets popping back up and the cancel key while a challenge runs (few targets).
        private void Update()
        {
            if (IsRunning && Input.GetKeyDown(KeyCode.Escape))
            {
                EndChallenge(true);
                return;
            }
            for (int i = 0; i < downTimers.Length; i++)
            {
                if (downTimers[i] <= 0f) continue;
                downTimers[i] -= Time.deltaTime;
                if (downTimers[i] <= 0f && !IsRunning && targets[i] != null) targets[i].Raise(false);
            }
        }

        public bool CanStart => !IsRunning && isActiveAndEnabled;

        public void StartChallenge(PlayerManager playerManager)
        {
            if (!CanStart || playerManager == null) return;
            player = playerManager;
            shooter = player.GetComponent<vShooterManager>();
            shooterInput = player.GetComponent<vShooterMeleeInput>();
            gamePlayer = player.GetComponent<InvectorGamePlayer>();
            weapons = player.GetComponent<PlayerWeaponController>();
            routine = StartCoroutine(ChallengeRoutine());
        }

        /// <summary>Called by the lane trigger (the lane shows the start prompt; the challenge itself is cancelled with Quit).</summary>
        public void SetPlayerInLane(bool inLane) => PlayerInLane = inLane;

        private IEnumerator ChallengeRoutine()
        {
            IsRunning = true;
            result = new RangeResult();
            for (int i = 0; i < targets.Length; i++) if (targets[i] != null) targets[i].ResetDown();
            for (int i = 0; i < downTimers.Length; i++) downTimers[i] = 0f;
            if (shooter != null) shooter.AllAmmoInfinity = true;
            HoldPlayer(true);
            OnChallengeStarted?.Invoke();

            if (shootingSpot != null) yield return MoveToSpot();

            for (int c = Mathf.CeilToInt(countdown); c > 0; c--)
            {
                OnCountdown?.Invoke(c);
                yield return new WaitForSeconds(1f);
            }
            OnCountdown?.Invoke(0);
            PlayBuzzer();

            float timeLeft = duration;
            float nextPop = 0f;
            var upSince = new float[targets.Length];
            while (timeLeft > 0f)
            {
                timeLeft -= Time.deltaTime;
                nextPop -= Time.deltaTime;
                if (nextPop <= 0f)
                {
                    nextPop = popInterval * UnityEngine.Random.Range(0.75f, 1.25f);
                    int index = PickDownTarget();
                    if (index >= 0)
                    {
                        targets[index].Raise(UnityEngine.Random.value < slideChance);
                        upSince[index] = Time.time;
                        result.shown++;
                    }
                }
                for (int i = 0; i < targets.Length; i++)
                    if (targets[i] != null && targets[i].IsUp && Time.time - upSince[i] > targetUpTime) targets[i].Lower();
                OnChallengeUpdated?.Invoke(Mathf.Max(0f, timeLeft), result.score, result.hits);
                yield return null;
            }
            EndChallenge(false);
        }

        // Movement is locked for the whole challenge; aiming is forced so the shoulder-height aimed muzzle clears the bench
        // (the hip-held muzzle sits below the bench top).
        private void HoldPlayer(bool hold)
        {
            if (player == null) return;
            player.SetMovementLocked(hold);
            if (shooterInput == null) return;
            if (hold)
            {
                alwaysAimBefore = shooterInput.LockAiming;
                if (weapons != null) weapons.DrawFirearm();
                shooterInput.SetAlwaysAim(true);
            }
            else
            {
                shooterInput.SetAlwaysAim(alwaysAimBefore);
            }
        }

        // Glides the player onto the spot facing downrange and turns the camera with him.
        private IEnumerator MoveToSpot()
        {
            Transform body = player.transform;
            Vector3 startPosition = body.position;
            float startYaw = body.eulerAngles.y;
            float targetYaw = shootingSpot.eulerAngles.y;
            vThirdPersonCamera camera = shooterInput != null ? shooterInput.tpCamera : null;
            float cameraYaw = camera != null ? camera.mouseX : targetYaw;
            float cameraPitchStart = camera != null ? camera.mouseY : cameraPitch;
            var rigidbody = body.GetComponent<Rigidbody>();

            for (float t = 0f; t < moveToSpotTime; t += Time.fixedDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / moveToSpotTime);
                Vector3 position = Vector3.Lerp(startPosition, shootingSpot.position, k);
                position.y = body.position.y;
                Quaternion rotation = Quaternion.Euler(0f, Mathf.LerpAngle(startYaw, targetYaw, k), 0f);
                if (rigidbody != null)
                {
                    rigidbody.MovePosition(position);
                    rigidbody.MoveRotation(rotation);
                }
                else body.SetPositionAndRotation(position, rotation);
                if (camera != null)
                {
                    camera.mouseX = Mathf.LerpAngle(cameraYaw, targetYaw, k);
                    camera.mouseY = Mathf.Lerp(cameraPitchStart, cameraPitch, k);
                }
                yield return new WaitForFixedUpdate();
            }
            if (gamePlayer != null) gamePlayer.Teleport(shootingSpot.position, targetYaw);
            if (camera != null)
            {
                camera.mouseX = targetYaw;
                camera.mouseY = cameraPitch;
            }
        }

        private int PickDownTarget()
        {
            int start = UnityEngine.Random.Range(0, targets.Length);
            for (int k = 0; k < targets.Length; k++)
            {
                int i = (start + k) % targets.Length;
                if (targets[i] != null && !targets[i].IsUp) return i;
            }
            return -1;
        }

        private void HandleQuitRequested()
        {
            if (IsRunning) EndChallenge(true);
        }

        private void EndChallenge(bool aborted)
        {
            if (!IsRunning) return;
            IsRunning = false;
            if (routine != null) StopCoroutine(routine);
            routine = null;
            if (shooter != null) shooter.AllAmmoInfinity = false;
            HoldPlayer(false);
            PlayBuzzer();

            result.aborted = aborted;
            result.rating = aborted ? "Aborted" : result.score >= goldScore ? "Gold" : result.score >= silverScore ? "Silver" : result.score >= bronzeScore ? "Bronze" : "Failed";
            result.reward = aborted ? 0 : result.score >= goldScore ? goldReward : result.score >= silverScore ? silverReward : result.score >= bronzeScore ? bronzeReward : 0;
            if (result.reward > 0 && MoneyManager.Instance != null) MoneyManager.Instance.Add(result.reward);

            for (int i = 0; i < targets.Length; i++) if (targets[i] != null) targets[i].Raise(false);
            OnChallengeEnded?.Invoke(result);
        }

        private void HandleHit(RangeTarget target, RangeHitZone zone, Vector3 point)
        {
            int index = Array.IndexOf(targets, target);
            target.Lower();
            if (!IsRunning)
            {
                if (index >= 0) downTimers[index] = practiceResetTime;
                return;
            }
            result.hits++;
            if (zone == RangeHitZone.Head) result.headshots++;
            result.score += zone == RangeHitZone.Head ? headPoints : torsoPoints;
        }

        private void PlayBuzzer()
        {
            if (buzzer != null && buzzer.clip != null) buzzer.PlayOneShot(buzzer.clip);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnChallengeStarted = null;
            OnCountdown = null;
            OnChallengeUpdated = null;
            OnChallengeEnded = null;
            QuitRequested = null;
        }
    }
}
