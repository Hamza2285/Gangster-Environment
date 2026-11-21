using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OpenworldCrime.UI.Menu
{
    /// <summary>
    /// GTA-style loading screen (main menu scene; one per scene, not a Singleton). The artwork is an <see cref="ArtSlideshow"/>
    /// that changes every Slide Interval seconds with a crossfade (each slide keeps its own slow scale). The menu music fades
    /// out, a bar / spinner / tip show progress, and the game scene loads asynchronously in the background. The screen stays
    /// at least Min Display Seconds: the bar follows the real progress but is held back by the elapsed time, so it only
    /// reaches 100% at the end. Then it fades to black, the scene activates, and the black overlay survives the scene change
    /// to fade into the game, then removes itself.
    /// </summary>
    [DisallowMultipleComponent]
    public class LoadingScreen : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Root object of the loading canvas (a scene root: it is kept across the scene change for the fade-in).")]
        [SerializeField] private GameObject root;
        [SerializeField] private CanvasGroup screenGroup;
        [SerializeField] private ArtSlideshow slideshow;
        [SerializeField] private Image progressFill;
        [Tooltip("Small bright head riding the end of the progress line (optional).")]
        [SerializeField] private Image progressGlint;
        [SerializeField] private Text percentText;
        [SerializeField] private RectTransform spinner;
        [SerializeField] private Text tipText;
        [SerializeField] private Image blackFade;
        [SerializeField, TextArea] private string[] tips = new string[0];

        [Header("Loading")]
        [SerializeField] private string gameScene = "Gangstar_Gameplay";
        [Tooltip("The loading screen shows at least this long (the game activates then, or when loaded if later).")]
        [SerializeField, Min(0f)] private float minDisplaySeconds = 10f;
        [Tooltip("A new artwork every this many seconds.")]
        [SerializeField, Min(0.5f)] private float slideInterval = 3f;
        [SerializeField, Min(0.1f)] private float fadeInSeconds = 0.6f;
        [SerializeField, Min(0.1f)] private float fadeToBlackSeconds = 0.6f;
        [SerializeField, Min(0.1f)] private float gameFadeInSeconds = 1f;
        [SerializeField, Min(0.1f)] private float musicFadeOutSeconds = 1.5f;
        [SerializeField, Min(10f)] private float spinnerSpeed = 220f;
        [Tooltip("Editor only: run the slideshow first and load the scene after it, behind the black. In the editor a play-mode " +
                 "scene load blocks the main thread for its whole duration (it reads through the asset database), so loading during " +
                 "the slideshow would only freeze the artwork. Builds always load in the background while the artwork plays.")]
        [SerializeField] private bool editorLoadAfterSlideshow = true;

        /// <summary>The loading screen fully covers the menu (the menu can stop its video).</summary>
        public event Action OnCovered;
        /// <summary>Frame pacing while the artwork was on screen (from the load start to the fade to black), for tests / tuning.</summary>
        public struct FrameStats
        {
            public int Frames;
            public float Seconds;
            public float LongestFrame;
            public int FramesOver50ms;
            public float SecondsInLongFrames; // time spent in frames longer than 50 ms (visibly stuck)
        }

        public FrameStats LastStats { get; private set; }

        /// <summary>A new artwork started (for tests / analytics). Arg: seconds since the load started.</summary>
        public event Action<float> OnSlideChanged;

        public bool IsLoading { get; private set; }

        private void Awake()
        {
            if (root == null || screenGroup == null || slideshow == null || !slideshow.IsValid || blackFade == null)
            {
                Debug.LogError($"[{nameof(LoadingScreen)}] References missing on '{name}'.", this);
                enabled = false;
                return;
            }
            root.SetActive(false);
        }

        /// <summary>Shows the loading screen and loads the game. music (optional) is faded out.</summary>
        public void Begin(AudioSource music)
        {
            if (IsLoading || !enabled) return;
            IsLoading = true;
            root.SetActive(true);
            StartCoroutine(LoadRoutine(music));
        }

        private IEnumerator LoadRoutine(AudioSource music)
        {
            Time.timeScale = 1f;
            screenGroup.alpha = 0f;
            SetBlack(0f);
            SetProgress(0f);
            slideshow.Begin();
            if (tipText != null && tips.Length > 0) tipText.text = tips[UnityEngine.Random.Range(0, tips.Length)];
            if (music != null) StartCoroutine(FadeOutMusic(music));

            float started = Time.unscaledTime;
            OnSlideChanged?.Invoke(0f);
            for (float t = 0f; t < fadeInSeconds; t += Time.unscaledDeltaTime)
            {
                screenGroup.alpha = Smooth(t / fadeInSeconds);
                yield return null;
            }
            screenGroup.alpha = 1f;
            OnCovered?.Invoke();

            // The loader gets small time slices while the artwork is on screen (Low = ~2 ms of integration per frame), so the
            // slideshow keeps animating; it goes back to High for the activation, which happens behind the black.
            ThreadPriority priorityBefore = Application.backgroundLoadingPriority;
            bool deferLoad = Application.isEditor && editorLoadAfterSlideshow;
            AsyncOperation op = null;
            if (!deferLoad)
            {
                Application.backgroundLoadingPriority = ThreadPriority.Low;
                op = SceneManager.LoadSceneAsync(gameScene, LoadSceneMode.Single);
                if (op == null)
                {
                    Debug.LogError($"[{nameof(LoadingScreen)}] Scene '{gameScene}' is not in the build settings.", this);
                    Application.backgroundLoadingPriority = priorityBefore;
                    yield break;
                }
                op.allowSceneActivation = false;
            }
            float shown = 0f;
            var stats = new FrameStats();
            float statsStart = Time.realtimeSinceStartup;
            float lastFrame = statsStart;
            float nextSlide = started + slideInterval;
            while (true)
            {
                float elapsed = Time.unscaledTime - started;
                float real = op != null ? Mathf.Clamp01(op.progress / 0.9f) : 1f;
                float wanted = Mathf.Min(real, minDisplaySeconds > 0f ? Mathf.Clamp01(elapsed / minDisplaySeconds) : 1f);
                shown = Mathf.MoveTowards(shown, wanted, Time.unscaledDeltaTime * 0.5f);
                SetProgress(shown);
                float nowReal = Time.realtimeSinceStartup;
                float frame = nowReal - lastFrame;
                lastFrame = nowReal;
                stats.Frames++;
                stats.LongestFrame = Mathf.Max(stats.LongestFrame, frame);
                if (frame > 0.05f) { stats.FramesOver50ms++; stats.SecondsInLongFrames += frame; }
                if (Time.unscaledTime >= nextSlide)
                {
                    // On the 3 s beat; after a hitch (a late change) the next one still waits a while, so two never crowd together.
                    nextSlide = Mathf.Max(nextSlide + slideInterval, Time.unscaledTime + slideInterval * 0.6f);
                    slideshow.Next();
                    OnSlideChanged?.Invoke(elapsed);
                }
                if ((op == null || op.progress >= 0.9f) && elapsed >= minDisplaySeconds && shown >= 0.999f) break;
                yield return null;
            }

            stats.Seconds = Time.realtimeSinceStartup - statsStart;
            LastStats = stats;
            for (float t = 0f; t < fadeToBlackSeconds; t += Time.unscaledDeltaTime)
            {
                SetBlack(Smooth(t / fadeToBlackSeconds));
                yield return null;
            }
            SetBlack(1f);
            slideshow.Stop();
            // The activation (the unavoidable main-thread hitch) happens now, behind full black.
            Application.backgroundLoadingPriority = ThreadPriority.High;
            if (op == null)
            {
                op = SceneManager.LoadSceneAsync(gameScene, LoadSceneMode.Single);
                if (op == null)
                {
                    Debug.LogError($"[{nameof(LoadingScreen)}] Scene '{gameScene}' is not in the build settings.", this);
                    Application.backgroundLoadingPriority = priorityBefore;
                    yield break;
                }
            }
            // Keep the black overlay through the scene change, then fade into the game.
            DontDestroyOnLoad(root);
            op.allowSceneActivation = true;
            while (!op.isDone) yield return null;
            Application.backgroundLoadingPriority = priorityBefore;
            screenGroup.alpha = 0f;
            yield return null;
            yield return null;
            for (float t = 0f; t < gameFadeInSeconds; t += Time.unscaledDeltaTime)
            {
                SetBlack(1f - Smooth(t / gameFadeInSeconds));
                yield return null;
            }
            Destroy(root);
        }

        private void Update()
        {
            if (!IsLoading) return;
            if (spinner != null) spinner.Rotate(0f, 0f, -spinnerSpeed * Time.unscaledDeltaTime);
            if (progressGlint != null)
            {
                Color c = progressGlint.color;
                c.a = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 6f);
                progressGlint.color = c;
            }
        }

        private IEnumerator FadeOutMusic(AudioSource music)
        {
            float from = music.volume;
            for (float t = 0f; t < musicFadeOutSeconds && music != null; t += Time.unscaledDeltaTime)
            {
                music.volume = from * (1f - Smooth(t / musicFadeOutSeconds));
                yield return null;
            }
            if (music != null) music.Stop();
        }

        private void SetProgress(float value)
        {
            if (progressFill != null)
            {
                progressFill.fillAmount = value;
                if (progressGlint != null)
                {
                    var bar = (RectTransform)progressFill.rectTransform.parent;
                    progressGlint.rectTransform.anchoredPosition = new Vector2(bar.rect.width * value, 0f);
                }
            }
            if (percentText != null) percentText.text = Mathf.RoundToInt(value * 100f) + "%";
        }

        private void SetBlack(float alpha)
        {
            Color c = blackFade.color;
            c.a = alpha;
            blackFade.color = c;
            blackFade.raycastTarget = alpha > 0.01f;
        }

        private static float Smooth(float t) => 0.5f - 0.5f * Mathf.Cos(Mathf.Clamp01(t) * Mathf.PI);
    }
}
