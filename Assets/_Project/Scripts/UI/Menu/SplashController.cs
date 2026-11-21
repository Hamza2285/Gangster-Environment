using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OpenworldCrime.UI.Menu
{
    /// <summary>
    /// The splash scene (on the splash canvas root, which outlives the scene to reveal the menu; not a Singleton): a random GTA-style artwork
    /// (<see cref="ArtSlideshow"/>, the same slides as the loading screen) with the title, fading in from black. The main menu
    /// loads in the background meanwhile; after Hold Seconds (or a tap) the screen fades to black, the menu activates, and the
    /// black overlay (kept across the scene change) waits until the menu's video shows its first frame before fading out, so
    /// the menu never flashes black.
    /// </summary>
    [DisallowMultipleComponent]
    public class SplashController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Root of the splash canvas (a scene root: kept across the scene change for the fade into the menu).")]
        [SerializeField] private GameObject root;
        [SerializeField] private ArtSlideshow slideshow;
        [SerializeField] private CanvasGroup content;
        [SerializeField] private RectTransform title;
        [SerializeField] private Image blackFade;
        [Tooltip("Full-screen invisible button: a tap skips the wait.")]
        [SerializeField] private Button skipButton;

        [Header("Timing")]
        [SerializeField] private string menuScene = "MainMenu";
        [SerializeField, Min(0.5f)] private float holdSeconds = 3.5f;
        [Tooltip("A tap skips the wait only after this long (so the splash is always seen).")]
        [SerializeField, Min(0f)] private float minSeconds = 1.2f;
        [SerializeField, Min(0.1f)] private float fadeInSeconds = 0.8f;
        [SerializeField, Min(0.1f)] private float fadeOutSeconds = 0.6f;
        [SerializeField, Min(0.1f)] private float menuRevealSeconds = 0.7f;
        [Tooltip("Safety limit (s) for waiting on the menu's video before revealing the menu anyway.")]
        [SerializeField, Min(0.5f)] private float maxVideoWait = 4f;
        [Tooltip("Title scale over the splash (slow, eased).")]
        [SerializeField] private Vector2 titleScale = new Vector2(0.96f, 1.04f);

        /// <summary>For tests / logs: the artwork shown.</summary>
        public string Slide => slideshow != null ? slideshow.CurrentSlide : "";

        private bool skip;
        private float started;

        private void Awake()
        {
            if (root == null || slideshow == null || !slideshow.IsValid || blackFade == null)
            {
                Debug.LogError($"[{nameof(SplashController)}] References missing on '{name}'.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (skipButton != null) skipButton.onClick.AddListener(Skip);
        }

        private void OnDisable()
        {
            if (skipButton != null) skipButton.onClick.RemoveListener(Skip);
        }

        public void Skip()
        {
            if (Time.unscaledTime - started >= minSeconds) skip = true;
        }

        private IEnumerator Start()
        {
            Time.timeScale = 1f;
            started = Time.unscaledTime;
            SetBlack(1f);
            slideshow.Begin();
            // The menu loads behind the splash (activation held until the splash ends).
            AsyncOperation op = SceneManager.LoadSceneAsync(menuScene, LoadSceneMode.Single);
            if (op != null) op.allowSceneActivation = false;
            else Debug.LogError($"[{nameof(SplashController)}] Scene '{menuScene}' is not in the build settings.", this);

            for (float t = 0f; t < fadeInSeconds; t += Time.unscaledDeltaTime)
            {
                SetBlack(1f - Smooth(t / fadeInSeconds));
                AnimateTitle();
                yield return null;
            }
            SetBlack(0f);
            while (!skip && Time.unscaledTime - started < holdSeconds)
            {
                AnimateTitle();
                yield return null;
            }
            for (float t = 0f; t < fadeOutSeconds; t += Time.unscaledDeltaTime)
            {
                SetBlack(Smooth(t / fadeOutSeconds));
                AnimateTitle();
                yield return null;
            }
            SetBlack(1f);
            if (op == null) yield break;

            slideshow.Stop();
            if (content != null) content.alpha = 0f;
            if (skipButton != null) skipButton.gameObject.SetActive(false);
            DontDestroyOnLoad(root);
            op.allowSceneActivation = true;
            while (!op.isDone) yield return null;

            // Reveal the menu once its video has a frame (a one-time lookup after the scene change).
            var menu = FindFirstObjectByType<MainMenuController>();
            for (float waited = 0f; menu != null && !menu.VideoShowing && waited < maxVideoWait; waited += Time.unscaledDeltaTime)
                yield return null;
            for (float t = 0f; t < menuRevealSeconds; t += Time.unscaledDeltaTime)
            {
                SetBlack(1f - Smooth(t / menuRevealSeconds));
                yield return null;
            }
            Destroy(root);
        }

        private void AnimateTitle()
        {
            if (title == null) return;
            float k = Smooth(Mathf.Clamp01((Time.unscaledTime - started) / (holdSeconds + fadeOutSeconds)));
            float s = Mathf.Lerp(titleScale.x, titleScale.y, k);
            title.localScale = new Vector3(s, s, 1f);
            if (content != null) content.alpha = Mathf.Clamp01((Time.unscaledTime - started - 0.3f) / 0.8f);
        }

        private void SetBlack(float alpha)
        {
            Color c = blackFade.color;
            c.a = alpha;
            blackFade.color = c;
        }

        private static float Smooth(float t) => 0.5f - 0.5f * Mathf.Cos(Mathf.Clamp01(t) * Mathf.PI);
    }
}
