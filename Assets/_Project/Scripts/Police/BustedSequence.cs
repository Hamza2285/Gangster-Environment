using System;
using System.Collections;
using Invector.vCamera;
using OpenworldCrime.Economy;
using OpenworldCrime.Player;
using OpenworldCrime.UI;
using UnityEngine;

namespace OpenworldCrime.Police
{
    using PlayerManager = OpenworldCrime.Player.PlayerManager; // SharedAssets has a global PlayerManager

    /// <summary>
    /// What happens when the police arrest the player (scene manager under ///Manager///, not a Singleton), GTA V style:
    /// slow motion behind the BUSTED screen (shown by the HUD from <see cref="WantedManager.OnBusted"/>), the player is taken out
    /// of his car, fade to black, police are recalled, a fine is taken (Busted Cash Share of the cash, clamped to min / max and to
    /// what he has; weapons and ammo are kept), he is released at the nearest Release Point, the wanted level is cleared and the
    /// screen fades back in. Runs only as a coroutine during the arrest.
    /// </summary>
    [DisallowMultipleComponent]
    public class BustedSequence : MonoBehaviour
    {
        [SerializeField] private PoliceSettings settings;
        [SerializeField] private PoliceDispatcher dispatcher;

        [Header("Player")]
        [SerializeField] private PlayerManager playerManager;
        [SerializeField] private InvectorGamePlayer gamePlayer;
        [SerializeField] private PlayerVehicleInteraction vehicleInteraction;
        [SerializeField] private vThirdPersonCamera thirdPersonCamera;

        [Header("Presentation")]
        [SerializeField] private ScreenFader fader;

        [Header("Release")]
        [Tooltip("Where an arrested player is released (police stations); the nearest one to the arrest is used.")]
        [SerializeField] private Transform[] releasePoints = new Transform[0];
        [Tooltip("Seconds (behind the black screen) the player gets to climb out of his car; no exit = released where he is.")]
        [SerializeField, Min(0f)] private float vehicleExitTimeout = 8f;

        /// <summary>Raised once the fine was taken, while the screen is black. Arg: the amount taken.</summary>
        public static event Action<int> OnFined;

        private Coroutine routine;
        private bool valid;

        private void Awake()
        {
            valid = settings != null && dispatcher != null && playerManager != null && gamePlayer != null && vehicleInteraction != null
                    && thirdPersonCamera != null && fader != null;
            if (!valid)
            {
                Debug.LogError($"[{nameof(BustedSequence)}] Missing references on '{name}' (settings, dispatcher, player, game player, " +
                               "vehicle interaction, camera, fader). An arrest only clears the wanted level.", this);
            }
            if (releasePoints.Length == 0) Debug.LogWarning($"[{nameof(BustedSequence)}] No release points on '{name}'; an arrested player stays where he is.", this);
        }

        private void OnEnable() => WantedManager.OnBusted += HandleBusted;

        private void OnDisable()
        {
            WantedManager.OnBusted -= HandleBusted;
            if (routine != null)
            {
                routine = null;
                Time.timeScale = 1f;
            }
        }

        private void HandleBusted()
        {
            if (routine != null) return;
            if (!valid)
            {
                if (WantedManager.Instance != null) WantedManager.Instance.CompleteBust();
                return;
            }
            routine = StartCoroutine(BustedRoutine());
        }

        private IEnumerator BustedRoutine()
        {
            Vector3 arrestedAt = playerManager.transform.position;
            float timeScaleBefore = Time.timeScale;
            Time.timeScale = settings.bustedTimeScale;

            // Hands up: no running off on foot; a driver is taken out of the car first (in slow motion behind the BUSTED screen).
            if (vehicleInteraction.IsInVehicle) vehicleInteraction.RequestExit();
            else playerManager.SetMovementLocked(true);
            yield return new WaitForSecondsRealtime(settings.bustedScreenSeconds);
            yield return fader.FadeTo(1f, settings.bustedFadeSeconds);
            Time.timeScale = timeScaleBefore;

            // Behind the black screen, at normal speed: let the exit animation finish (it owns the input lock until then).
            float waited = 0f;
            while (vehicleInteraction.State != VehicleInteractionState.OnFoot && waited < vehicleExitTimeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            bool onFoot = vehicleInteraction.State == VehicleInteractionState.OnFoot;
            if (onFoot) playerManager.SetInputLocked(true);
            dispatcher.ReleaseAll();
            int fine = TakeFine();
            Transform point = NearestReleasePoint(arrestedAt);
            if (point != null && onFoot) MovePlayer(point);
            else if (!onFoot) Debug.LogWarning($"[{nameof(BustedSequence)}] The player could not get out of his vehicle; released where he was.", this);
            WantedManager.Instance.CompleteBust();
            OnFined?.Invoke(fine);

            yield return null; // one frame so the camera settles on the new position
            yield return new WaitForSecondsRealtime(0.4f);
            yield return fader.FadeTo(0f, settings.bustedFadeSeconds);
            playerManager.SetMovementLocked(false);
            playerManager.SetInputLocked(false);
            routine = null;
        }

        private int TakeFine()
        {
            MoneyManager money = MoneyManager.Instance;
            if (money == null) return 0;
            int fine = Mathf.RoundToInt(money.Balance * settings.bustedCashShare);
            fine = Mathf.Clamp(fine, settings.bustedCashMin, Mathf.Max(settings.bustedCashMin, settings.bustedCashMax));
            fine = Mathf.Min(fine, money.Balance);
            if (fine > 0) money.TrySpend(fine);
            return fine;
        }

        private Transform NearestReleasePoint(Vector3 from)
        {
            Transform best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < releasePoints.Length; i++)
            {
                if (releasePoints[i] == null) continue;
                float sqr = (releasePoints[i].position - from).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = releasePoints[i];
            }
            return best;
        }

        // Same as the interior doors: teleport, then snap the Invector camera behind the player.
        private void MovePlayer(Transform point)
        {
            gamePlayer.Teleport(point.position, point.eulerAngles.y);
            float yaw = point.eulerAngles.y;
            thirdPersonCamera.transform.SetPositionAndRotation(point.position - point.forward * 2.5f + Vector3.up * 1.7f, Quaternion.Euler(8f, yaw, 0f));
            thirdPersonCamera.Init();
            thirdPersonCamera.mouseX = yaw;
            thirdPersonCamera.mouseY = 8f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => OnFined = null;
    }
}
