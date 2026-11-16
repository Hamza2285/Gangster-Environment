using System;
using System.Collections;
using Invector.vCamera;
using Invector.vCharacterController;
using OpenworldCrime.Player;
using OpenworldCrime.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.Interiors
{
    using PlayerManager = OpenworldCrime.Player.PlayerManager; // SharedAssets has a global PlayerManager

    /// <summary>
    /// Moves the player between the city and additively loaded interiors (global manager under ///Manager///; Singleton
    /// because doors inside interior scenes cannot hold references to the city scene).
    ///
    /// Architecture: the player never leaves the city scene, so health, weapons, ammo and every Invector / RCC / project
    /// reference stay intact. The interior scene is authored far from the city (beyond the camera far plane), loaded
    /// additively behind a fade, made the active scene (its ambient / fog / lightmap apply) and unloaded on exit.
    /// </summary>
    [DisallowMultipleComponent]
    public class InteriorTravelManager : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField] private PlayerManager playerManager;
        [SerializeField] private InvectorGamePlayer gamePlayer;
        [SerializeField] private PlayerWeaponController weaponController;
        [SerializeField] private vShooterMeleeInput shooterInput;
        [SerializeField] private vThirdPersonCamera thirdPersonCamera;

        [Header("Presentation")]
        [SerializeField] private ScreenFader fader;
        [SerializeField, Min(0f)] private float fadeDuration = 0.45f;
        [Tooltip("Optional door sound played when entering / leaving.")]
        [SerializeField] private AudioSource doorAudio;

        [Header("City While Indoors")]
        [Tooltip("Disabled while the player is inside an interior and restored on exit (e.g. the city sun, whose light would reach the interior's dynamic objects).")]
        [SerializeField] private Behaviour[] disableWhileInside = new Behaviour[0];
        [Tooltip("City objects hidden while indoors and restored on exit (e.g. the day / night buttons).")]
        [SerializeField] private GameObject[] hideWhileInside = new GameObject[0];

        /// <summary>Invector camera state (OWC_ShooterMelee_CameraStates) used indoors while not aiming: closer and lower.</summary>
        public const string InteriorCameraState = "Interior";

        [Header("Camera Indoors")]
        [Tooltip("Use the closer 'Interior' camera state indoors while not aiming (aiming keeps Invector's aim camera).")]
        [SerializeField] private bool useInteriorCamera = true;

        public static InteriorTravelManager Instance { get; private set; }
        /// <summary>Raised after the player arrived inside (true) or back outside (false), while the screen is still black.</summary>
        public static event Action<bool> OnInteriorChanged;

        public bool IsInside { get; private set; }
        public bool IsTransitioning { get; private set; }
        public bool CanTravel => !IsTransitioning && enabled;
        public bool WeaponsAllowed { get; private set; } = true;
        public ShopEntrance CurrentEntrance { get; private set; }

        private Scene cityScene;
        private Scene interiorScene;
        private bool[] disabledWasEnabled;
        private bool[] hiddenWasActive;
        private bool interiorCameraActive;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[{nameof(InteriorTravelManager)}] A second manager on '{name}' was disabled.", this);
                enabled = false;
                return;
            }
            if (playerManager == null || gamePlayer == null || weaponController == null || shooterInput == null || thirdPersonCamera == null || fader == null)
            {
                Debug.LogError($"[{nameof(InteriorTravelManager)}] Missing references on '{name}' (player, weapons, shooter input, camera, fader).", this);
                enabled = false;
                return;
            }
            Instance = this;
            cityScene = gameObject.scene;
            disabledWasEnabled = new bool[disableWhileInside.Length];
            hiddenWasActive = new bool[hideWhileInside.Length];
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // Per-frame only indoors: swaps between the interior camera and Invector's aim camera.
        private void Update()
        {
            if (!useInteriorCamera || !IsInside || IsTransitioning) return;
            SetInteriorCamera(!shooterInput.isAimingByInput);
        }

        private void SetInteriorCamera(bool active)
        {
            if (interiorCameraActive == active) return;
            interiorCameraActive = active;
            if (active) shooterInput.ChangeCameraState(InteriorCameraState, true);
            else shooterInput.ResetCameraState();
        }

        public void Enter(ShopEntrance entrance)
        {
            if (!CanTravel || IsInside || entrance == null) return;
            StartCoroutine(EnterRoutine(entrance));
        }

        public void Exit()
        {
            if (!CanTravel || !IsInside) return;
            StartCoroutine(ExitRoutine());
        }

        /// <summary>Weapons may be drawn only inside <see cref="InteriorWeaponZone"/>s while indoors (shop = holstered, range = free).</summary>
        public void SetWeaponsAllowed(bool allowed)
        {
            if (!IsInside) allowed = true;
            WeaponsAllowed = allowed;
            weaponController.SetWeaponsBlocked(!allowed);
            shooterInput.SetLockShooterInput(!allowed);
            shooterInput.SetLockMeleeInput(!allowed);
        }

        private IEnumerator EnterRoutine(ShopEntrance entrance)
        {
            IsTransitioning = true;
            playerManager.SetInputLocked(true);
            PlayDoor();
            yield return fader.FadeTo(1f, fadeDuration, "GUN SHOP");

            CurrentEntrance = entrance;
            AsyncOperation load = SceneManager.LoadSceneAsync(entrance.InteriorSceneName, LoadSceneMode.Additive);
            if (load == null)
            {
                Debug.LogError($"[{nameof(InteriorTravelManager)}] Interior scene '{entrance.InteriorSceneName}' is not in Build Settings.", this);
                yield return Finish(false);
                yield break;
            }
            while (!load.isDone) yield return null;

            interiorScene = SceneManager.GetSceneByName(entrance.InteriorSceneName);
            InteriorScene interior = InteriorScene.Current;
            if (!interiorScene.IsValid() || interior == null)
            {
                Debug.LogError($"[{nameof(InteriorTravelManager)}] '{entrance.InteriorSceneName}' has no InteriorScene root.", this);
                yield return Finish(false);
                yield break;
            }

            SceneManager.SetActiveScene(interiorScene);
            LightProbes.TetrahedralizeAsync(); // add the interior's baked light probes to the loaded set
            SetCityBehavioursEnabled(false);
            IsInside = true;
            GamePlayer.MenuOpen = true; // hides the city minimap and the taxi whistle while indoors
            SetWeaponsAllowed(false);
            MovePlayer(interior.SpawnPoint);
            OnInteriorChanged?.Invoke(true);
            yield return Finish(true);
        }

        private IEnumerator ExitRoutine()
        {
            IsTransitioning = true;
            playerManager.SetInputLocked(true);
            PlayDoor();
            yield return fader.FadeTo(1f, fadeDuration);

            IsInside = false;
            SetInteriorCamera(false);
            SetWeaponsAllowed(true);
            Transform exitPoint = CurrentEntrance != null ? CurrentEntrance.ExitPoint : null;
            if (exitPoint != null) MovePlayer(exitPoint);
            SceneManager.SetActiveScene(cityScene);
            SetCityBehavioursEnabled(true);
            GamePlayer.MenuOpen = false;
            OnInteriorChanged?.Invoke(false);

            if (interiorScene.IsValid() && interiorScene.isLoaded)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(interiorScene);
                while (unload != null && !unload.isDone) yield return null;
                LightProbes.TetrahedralizeAsync();
            }
            yield return Finish(true);
        }

        private IEnumerator Finish(bool fadeIn)
        {
            yield return null; // one frame so the camera settles on the new position
            yield return fader.FadeTo(0f, fadeIn ? fadeDuration : 0.1f);
            playerManager.SetInputLocked(false);
            IsTransitioning = false;
        }

        private void MovePlayer(Transform point)
        {
            gamePlayer.Teleport(point.position, point.eulerAngles.y);
            // Snap the Invector camera behind the player instead of letting it glide across the map.
            float yaw = point.eulerAngles.y;
            thirdPersonCamera.transform.SetPositionAndRotation(point.position - point.forward * 2.5f + Vector3.up * 1.7f, Quaternion.Euler(8f, yaw, 0f));
            thirdPersonCamera.Init();
            thirdPersonCamera.mouseX = yaw;
            thirdPersonCamera.mouseY = 8f;
        }

        private void SetCityBehavioursEnabled(bool enabledState)
        {
            for (int i = 0; i < disableWhileInside.Length; i++)
            {
                Behaviour behaviour = disableWhileInside[i];
                if (behaviour == null) continue;
                if (!enabledState)
                {
                    disabledWasEnabled[i] = behaviour.enabled;
                    behaviour.enabled = false;
                }
                else
                {
                    behaviour.enabled = disabledWasEnabled[i];
                }
            }
            for (int i = 0; i < hideWhileInside.Length; i++)
            {
                GameObject target = hideWhileInside[i];
                if (target == null) continue;
                if (!enabledState)
                {
                    hiddenWasActive[i] = target.activeSelf;
                    target.SetActive(false);
                }
                else
                {
                    target.SetActive(hiddenWasActive[i]);
                }
            }
        }

        private void PlayDoor()
        {
            if (doorAudio != null && doorAudio.clip != null) doorAudio.Play();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            OnInteriorChanged = null;
        }
    }
}
