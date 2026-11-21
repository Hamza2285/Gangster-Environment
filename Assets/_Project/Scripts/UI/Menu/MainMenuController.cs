using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace OpenworldCrime.UI.Menu
{
    /// <summary>
    /// The main menu scene (scene-level controller under ///Manager///, not a Singleton):
    /// - a full-screen looping background video (muted, into a RenderTexture shown by a RawImage that fills the screen; it
    ///   fades in once the first frame is ready, so there is never a black or white flash);
    /// - looping menu music fading in;
    /// - the right-hand icon bar (policy, share, settings, exit) opening panels (SetActive), and ENTER GAME, which hands over to
    ///   the <see cref="LoadingScreen"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class MainMenuController : MonoBehaviour
    {
        [Header("Background")]
        [SerializeField] private VideoPlayer video;
        [SerializeField] private RawImage videoImage;
        [SerializeField] private AspectRatioFitter videoFitter;
        [SerializeField, Min(0.1f)] private float videoFadeSeconds = 0.8f;

        [Header("Music")]
        [SerializeField] private AudioSource music;
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.8f;
        [SerializeField, Min(0.1f)] private float musicFadeInSeconds = 2.5f;

        [Header("Buttons")]
        [SerializeField] private Button enterGameButton;
        [SerializeField] private Button policyButton;
        [SerializeField] private Button shareButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button exitButton;

        [Header("Panels (shown with SetActive)")]
        [SerializeField] private GameObject policyPanel;
        [SerializeField] private GameObject sharePanel;
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private GameObject exitPanel;
        [Tooltip("Buttons that close whichever panel is open.")]
        [SerializeField] private Button[] closeButtons = new Button[0];
        [SerializeField] private Button quitConfirmButton;
        [SerializeField] private Slider musicSlider;
        [Tooltip("If set, the policy button opens this page instead of the placeholder panel.")]
        [SerializeField] private string privacyPolicyUrl = "";

        [Header("Loading")]
        [SerializeField] private LoadingScreen loadingScreen;

        private RenderTexture videoTexture;

        /// <summary>The background video has its first frame and is fading in (the splash waits for this before revealing the menu).</summary>
        public bool VideoShowing { get; private set; }

        private void Awake()
        {
            if (video == null || videoImage == null || enterGameButton == null || loadingScreen == null)
            {
                Debug.LogError($"[{nameof(MainMenuController)}] References missing on '{name}'.", this);
                enabled = false;
                return;
            }
            Time.timeScale = 1f;
            ClosePanels();
        }

        private void OnEnable()
        {
            enterGameButton.onClick.AddListener(EnterGame);
            if (policyButton != null) policyButton.onClick.AddListener(OpenPolicy);
            if (shareButton != null) shareButton.onClick.AddListener(OpenShare);
            if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
            if (exitButton != null) exitButton.onClick.AddListener(OpenExit);
            foreach (Button b in closeButtons) if (b != null) b.onClick.AddListener(ClosePanels);
            if (quitConfirmButton != null) quitConfirmButton.onClick.AddListener(Quit);
            if (musicSlider != null) musicSlider.onValueChanged.AddListener(SetMusicVolume);
            loadingScreen.OnCovered += HandleLoadingCovered;
        }

        private void OnDisable()
        {
            enterGameButton.onClick.RemoveListener(EnterGame);
            if (policyButton != null) policyButton.onClick.RemoveListener(OpenPolicy);
            if (shareButton != null) shareButton.onClick.RemoveListener(OpenShare);
            if (settingsButton != null) settingsButton.onClick.RemoveListener(OpenSettings);
            if (exitButton != null) exitButton.onClick.RemoveListener(OpenExit);
            foreach (Button b in closeButtons) if (b != null) b.onClick.RemoveListener(ClosePanels);
            if (quitConfirmButton != null) quitConfirmButton.onClick.RemoveListener(Quit);
            if (musicSlider != null) musicSlider.onValueChanged.RemoveListener(SetMusicVolume);
            if (loadingScreen != null) loadingScreen.OnCovered -= HandleLoadingCovered;
        }

        private IEnumerator Start()
        {
            if (musicSlider != null) musicSlider.SetValueWithoutNotify(musicVolume);
            if (music != null)
            {
                music.loop = true;
                music.volume = 0f;
                music.Play();
                StartCoroutine(FadeMusicIn());
            }

            videoImage.color = new Color(1f, 1f, 1f, 0f);
            video.isLooping = true;
            video.playOnAwake = false;
            video.audioOutputMode = VideoAudioOutputMode.None;
            video.renderMode = VideoRenderMode.RenderTexture;
            video.Prepare();
            while (!video.isPrepared) yield return null;
            int w = (int)video.width, h = (int)video.height;
            if (w <= 0 || h <= 0) { w = 1920; h = 1080; }
            videoTexture = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "MenuVideo" };
            video.targetTexture = videoTexture;
            videoImage.texture = videoTexture;
            if (videoFitter != null) videoFitter.aspectRatio = (float)w / h;
            video.Play();
            // Wait for the first frame before showing it.
            while (video.frame < 1) yield return null;
            VideoShowing = true;
            for (float t = 0f; t < videoFadeSeconds; t += Time.unscaledDeltaTime)
            {
                videoImage.color = new Color(1f, 1f, 1f, t / videoFadeSeconds);
                yield return null;
            }
            videoImage.color = Color.white;
        }

        private void OnDestroy()
        {
            if (videoTexture != null)
            {
                videoTexture.Release();
                Destroy(videoTexture);
            }
        }

        private IEnumerator FadeMusicIn()
        {
            for (float t = 0f; t < musicFadeInSeconds; t += Time.unscaledDeltaTime)
            {
                if (loadingScreen.IsLoading) yield break;
                music.volume = musicVolume * (t / musicFadeInSeconds);
                yield return null;
            }
            if (!loadingScreen.IsLoading) music.volume = musicVolume;
        }

        public void EnterGame()
        {
            if (loadingScreen.IsLoading) return;
            ClosePanels();
            SetButtonsInteractable(false);
            loadingScreen.Begin(music);
        }

        private void HandleLoadingCovered()
        {
            // Hidden behind the loading screen now: free the decoder and its texture before the game loads.
            video.Stop();
        }

        private void OpenPolicy()
        {
            if (!string.IsNullOrEmpty(privacyPolicyUrl)) { Application.OpenURL(privacyPolicyUrl); return; }
            Open(policyPanel);
        }

        private void OpenShare() => Open(sharePanel);
        private void OpenSettings() => Open(settingsPanel);
        private void OpenExit() => Open(exitPanel);

        private void Open(GameObject panel)
        {
            ClosePanels();
            if (panel != null) panel.SetActive(true);
        }

        public void ClosePanels()
        {
            if (policyPanel != null) policyPanel.SetActive(false);
            if (sharePanel != null) sharePanel.SetActive(false);
            if (settingsPanel != null) settingsPanel.SetActive(false);
            if (exitPanel != null) exitPanel.SetActive(false);
        }

        private void SetMusicVolume(float value)
        {
            musicVolume = value;
            if (music != null && !loadingScreen.IsLoading) music.volume = value;
        }

        private void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void SetButtonsInteractable(bool on)
        {
            enterGameButton.interactable = on;
            if (policyButton != null) policyButton.interactable = on;
            if (shareButton != null) shareButton.interactable = on;
            if (settingsButton != null) settingsButton.interactable = on;
            if (exitButton != null) exitButton.interactable = on;
        }
    }
}
