using System;
using System.Collections;
using Invector.vCamera;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using OpenworldCrime.Economy;
using OpenworldCrime.Player;
using OpenworldCrime.UI;
using UnityEngine;

namespace OpenworldCrime.Police
{
    using PlayerManager = OpenworldCrime.Player.PlayerManager; // SharedAssets has a global PlayerManager

    /// <summary>
    /// What happens when the police arrest the player (scene manager under ///Manager///, not a Singleton), GTA V style:
    /// a driver is taken out of his car first; then the player goes down on his knees (Kneel Clip, only the part from standing to
    /// kneeling, held there); once he kneels the screen turns blurry and pink, the BUSTED screen shows (the HUD, from
    /// <see cref="OnKneeling"/>) and time slows; then fade to black, police are recalled, a fine is taken (Busted Cash Share of the cash, clamped to min / max and to
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

        [Header("Kneel")]
        [Tooltip("Humanoid clip of the player going down on his knees (hands up). Only Kneel Start Time to Kneel Down Time is played, " +
                 "then the pose is held. Without it the BUSTED screen shows straight away.")]
        [SerializeField] private AnimationClip kneelClip;
        [SerializeField, Min(0f)] private float kneelStartTime = 1.2f;
        [SerializeField, Min(0f)] private float kneelDownTime = 3.1f;
        [SerializeField, Min(0.1f)] private float kneelSpeed = 1.3f;
        [Tooltip("Seconds of cross-fade from the current pose into the kneel clip.")]
        [SerializeField, Min(0f)] private float kneelBlendSeconds = 0.25f;

        [Header("Busted Look")]
        [Tooltip("Colour the screen is tinted while BUSTED shows.")]
        [SerializeField] private Color bustedTint = new Color(1f, 0.55f, 0.8f, 1f);
        [SerializeField] private Color bustedVignette = new Color(0.85f, 0.1f, 0.55f, 1f);
        [SerializeField, Range(0f, 1f)] private float vignetteIntensity = 0.45f;
        [Tooltip("Blur radius of the busted look (Gaussian depth of field over the whole view).")]
        [SerializeField, Range(0.5f, 1.5f)] private float blurRadius = 1.5f;
        [Tooltip("Seconds (real time) for the busted look to come in.")]
        [SerializeField, Min(0.05f)] private float lookFadeSeconds = 0.5f;

        [Header("Release")]
        [Tooltip("Where an arrested player is released (police stations); the nearest one to the arrest is used.")]
        [SerializeField] private Transform[] releasePoints = new Transform[0];
        [Tooltip("Seconds (behind the black screen) the player gets to climb out of his car; no exit = released where he is.")]
        [SerializeField, Min(0f)] private float vehicleExitTimeout = 8f;

        /// <summary>Raised once the fine was taken, while the screen is black. Arg: the amount taken.</summary>
        public static event Action<int> OnFined;
        /// <summary>Raised when the arrested player is down on his knees (the BUSTED screen shows from here).</summary>
        public static event Action OnKneeling;

        private Coroutine routine;
        private PlayableGraph kneelGraph;
        private AnimationPlayableOutput kneelOutput;
        private AnimationClipPlayable kneelPlayable;
        private Volume bustedVolume;
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
            StopKneel();
            SetLook(0f);
        }

        private void OnDestroy()
        {
            StopKneel();
            if (bustedVolume != null)
            {
                Destroy(bustedVolume.sharedProfile);
                Destroy(bustedVolume.gameObject);
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

            // A driver is taken out of the car first (the exit animation owns the input lock until he is out).
            if (vehicleInteraction.IsInVehicle) vehicleInteraction.RequestExit();
            float waited = 0f;
            while (vehicleInteraction.State != VehicleInteractionState.OnFoot && waited < vehicleExitTimeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            bool onFoot = vehicleInteraction.State == VehicleInteractionState.OnFoot;

            // Hands up and down on his knees; nothing more until he kneels.
            if (onFoot)
            {
                playerManager.SetMovementLocked(true);
                playerManager.SetInputLocked(true);
                yield return Kneel();
            }

            // On his knees: blurry pink, BUSTED, slow motion.
            Time.timeScale = settings.bustedTimeScale;
            OnKneeling?.Invoke();
            for (float t = 0f; t < lookFadeSeconds; t += Time.unscaledDeltaTime)
            {
                SetLook(t / lookFadeSeconds);
                yield return null;
            }
            SetLook(1f);
            yield return new WaitForSecondsRealtime(settings.bustedScreenSeconds);
            yield return fader.FadeTo(1f, settings.bustedFadeSeconds);
            Time.timeScale = timeScaleBefore;

            // Behind the black screen: back on his feet (animation and look reset) for the release.
            StopKneel();
            SetLook(0f);
            if (!onFoot)
            {
                waited = 0f;
                while (vehicleInteraction.State != VehicleInteractionState.OnFoot && waited < vehicleExitTimeout)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
                onFoot = vehicleInteraction.State == VehicleInteractionState.OnFoot;
            }
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

        // Plays the kneel clip over the player's own animation (a playable graph on his Animator, which wins over the controller
        // while it runs): from Kneel Start Time, cross-faded in, until Kneel Down Time, then holds that frame.
        private IEnumerator Kneel()
        {
            Animator animator = playerManager.GetComponentInChildren<Animator>();
            if (kneelClip == null || animator == null || !animator.isHuman) yield break;
            StopKneel();
            kneelGraph = PlayableGraph.Create("Busted Kneel");
            kneelGraph.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
            // The output's weight blends the clip over the Animator's own (controller) pose, so it fades in from whatever he was doing.
            kneelOutput = AnimationPlayableOutput.Create(kneelGraph, "Kneel", animator);
            kneelPlayable = AnimationClipPlayable.Create(kneelGraph, kneelClip);
            kneelPlayable.SetApplyFootIK(true);
            kneelPlayable.SetTime(kneelStartTime);
            kneelPlayable.SetSpeed(kneelSpeed);
            kneelOutput.SetSourcePlayable(kneelPlayable);
            kneelOutput.SetWeight(0f);
            kneelGraph.Play();

            float timeout = (kneelDownTime - kneelStartTime) / kneelSpeed + 2f;
            for (float t = 0f; t < timeout; t += Time.unscaledDeltaTime)
            {
                float blend = kneelBlendSeconds > 0f ? Mathf.Clamp01(t / kneelBlendSeconds) : 1f;
                kneelOutput.SetWeight(blend);
                if (kneelPlayable.GetTime() >= kneelDownTime) break;
                yield return null;
            }
            // Hold the kneeling pose (the clip goes on to the hands behind the head and standing up again).
            kneelPlayable.SetTime(kneelDownTime);
            kneelPlayable.SetSpeed(0f);
            kneelOutput.SetWeight(1f);
        }

        private void StopKneel()
        {
            if (kneelGraph.IsValid()) kneelGraph.Destroy();
        }

        // The busted look: a full-screen blur with a pink tint and vignette, in a global volume above the game's own.
        private void SetLook(float weight)
        {
            if (bustedVolume == null)
            {
                if (weight <= 0f) return;
                CreateLook();
            }
            bustedVolume.weight = weight;
            bustedVolume.enabled = weight > 0f;
        }

        private void CreateLook()
        {
            var go = new GameObject("Busted Look Volume");
            go.transform.SetParent(transform, false);
            bustedVolume = go.AddComponent<Volume>();
            bustedVolume.isGlobal = true;
            bustedVolume.priority = 1000f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Busted Look";
            var dof = profile.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(0f);
            dof.gaussianEnd.Override(0.5f);
            dof.gaussianMaxRadius.Override(blurRadius);
            dof.highQualitySampling.Override(true);
            var colour = profile.Add<ColorAdjustments>(true);
            colour.colorFilter.Override(bustedTint);
            colour.saturation.Override(-15f);
            var vignette = profile.Add<Vignette>(true);
            vignette.color.Override(bustedVignette);
            vignette.intensity.Override(vignetteIntensity);
            vignette.smoothness.Override(0.6f);
            bustedVolume.sharedProfile = profile;
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
        private static void ResetStatics()
        {
            OnFined = null;
            OnKneeling = null;
        }
    }
}
