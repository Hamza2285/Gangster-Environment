using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.UI.Menu
{
    /// <summary>
    /// GTA-style artwork slides (splash and loading screens; per screen). Each slide is either
    /// - layered: a still scene with a character cut-out in front. The scene pushes in slowly and drifts one way, the
    ///   character pushes in more strongly and drifts the other way (parallax: it reads as closer), and on the slide's start
    ///   the character slides in from its side while fading in; or
    /// - full: a complete artwork pushing in and drifting as a whole (Ken Burns).
    /// Every motion is a continuous eased curve over the slide on unscaled time (no steps or jitter; it keeps moving even
    /// with timeScale 0). <see cref="Next"/> crossfades to another slide, never the same as the one before (also across screens).
    /// Two views are used in turn; only they are touched per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public class ArtSlideshow : MonoBehaviour
    {
        [Serializable]
        public class SlideView
        {
            public CanvasGroup group;
            [Tooltip("The scene layer (the background's parent): pushed in and drifted.")]
            public RectTransform artRoot;
            public Image background;
            public AspectRatioFitter backgroundFitter;
            public Image character;
        }

        [SerializeField] private SplashArtSet art;
        [SerializeField] private SlideView[] views = new SlideView[2];

        [Header("Motion (per slide)")]
        [Tooltip("Seconds over which a slide's motion runs (a little longer than it is shown, so it never comes to rest on screen).")]
        [SerializeField, Min(0.5f)] private float slideSeconds = 4.6f;
        [Tooltip("Scene layer push-in (layered slides).")]
        [SerializeField] private Vector2 sceneScale = new Vector2(1.04f, 1.10f);
        [Tooltip("Scene layer drift (reference pixels) over the slide.")]
        [SerializeField, Min(0f)] private float sceneDrift = 50f;
        [Tooltip("Character push-in.")]
        [SerializeField] private Vector2 characterScale = new Vector2(1f, 1.12f);
        [Tooltip("Character drift (reference pixels), opposite to the scene's: parallax.")]
        [SerializeField, Min(0f)] private float characterDrift = 70f;
        [Tooltip("Character slide-in at the slide's start: distance (reference pixels) and seconds.")]
        [SerializeField, Min(0f)] private float slideInDistance = 170f;
        [SerializeField, Min(0.05f)] private float slideInSeconds = 0.9f;
        [Tooltip("Full artwork push-in and drift.")]
        [SerializeField] private Vector2 fullArtScale = new Vector2(1.06f, 1.18f);
        [SerializeField, Min(0f)] private float fullArtDrift = 90f;
        [SerializeField, Min(0.1f)] private float crossfadeSeconds = 1f;

        private struct Motion
        {
            public float Start;
            public bool Full;
            public bool Left;
            public Vector2 Drift; // unit direction of the scene's drift
        }

        // The slides shown most recently (any screen): new picks avoid them, so a 10 s loading screen never repeats one.
        private static readonly List<string> recent = new List<string>();
        private const int RecentMemory = 4;
        private readonly Motion[] motion = new Motion[2];
        private int front;
        private bool running;
        private Coroutine fading;

        /// <summary>Name of the slide on screen now (background / character), for logs.</summary>
        public string CurrentSlide { get; private set; }

        public bool IsValid => art != null && views != null && views.Length == 2 && views[0].group != null && views[1].group != null;

        /// <summary>Starts with a fresh random slide, fully shown (no fade: the caller fades the whole screen).</summary>
        public void Begin()
        {
            if (!IsValid) return;
            running = true;
            front = 0;
            views[1].group.alpha = 0f;
            Show(0);
            views[0].group.alpha = 1f;
        }

        /// <summary>Crossfades to another random slide.</summary>
        public void Next()
        {
            if (!running) return;
            if (fading != null)
            {
                StopCoroutine(fading);
                FinishFade();
            }
            fading = StartCoroutine(Crossfade());
        }

        public void Stop() => running = false;

        private void Update()
        {
            if (!running) return;
            for (int i = 0; i < 2; i++) Animate(i);
        }

        private IEnumerator Crossfade()
        {
            int back = 1 - front;
            Show(back);
            // (The two slides are adjacent siblings: the outgoing one takes the lower index, the incoming one the next.)
            Transform f = views[front].group.transform, b = views[back].group.transform;
            int lo = Mathf.Min(f.GetSiblingIndex(), b.GetSiblingIndex());
            f.SetSiblingIndex(lo);
            b.SetSiblingIndex(lo + 1);
            for (float t = 0f; t < crossfadeSeconds; t += Time.unscaledDeltaTime)
            {
                views[back].group.alpha = Smooth(t / crossfadeSeconds);
                yield return null;
            }
            FinishFade();
        }

        private void FinishFade()
        {
            int back = 1 - front;
            views[back].group.alpha = 1f;
            views[front].group.alpha = 0f;
            front = back;
            fading = null;
        }

        private void Animate(int i)
        {
            SlideView view = views[i];
            if (view.group.alpha <= 0f) return;
            Motion m = motion[i];
            float age = Time.unscaledTime - m.Start;
            float e = Smooth(Mathf.Clamp01(age / slideSeconds));
            if (m.Full)
            {
                float s = Mathf.Lerp(fullArtScale.x, fullArtScale.y, e);
                view.artRoot.localScale = new Vector3(s, s, 1f);
                view.artRoot.anchoredPosition = m.Drift * Mathf.Lerp(-0.5f, 0.5f, e) * fullArtDrift;
                return;
            }
            // Scene layer: slow push-in, drifting one way.
            float sceneS = Mathf.Lerp(sceneScale.x, sceneScale.y, e);
            view.artRoot.localScale = new Vector3(sceneS, sceneS, 1f);
            view.artRoot.anchoredPosition = m.Drift * Mathf.Lerp(-0.5f, 0.5f, e) * sceneDrift;
            // Character: stronger push-in, drifting the other way (parallax), sliding in from its side at the start.
            RectTransform rt = view.character.rectTransform;
            float charS = Mathf.Lerp(characterScale.x, characterScale.y, e);
            rt.localScale = new Vector3(charS, charS, 1f);
            float k = Mathf.Clamp01(age / slideInSeconds);
            float slideIn = 1f - EaseOutCubic(k);
            Vector2 side = new Vector2(m.Left ? -1f : 1f, 0f);
            rt.anchoredPosition = -m.Drift * Mathf.Lerp(-0.5f, 0.5f, e) * characterDrift + side * slideIn * slideInDistance;
            Color c = view.character.color;
            c.a = Smooth(k);
            view.character.color = c;
        }

        // A random slide, never the one shown last (on this or the previous screen).
        private void Show(int index)
        {
            SlideView view = views[index];
            var options = new List<string>();
            if (art.stillBackgrounds.Length > 0)
                for (int c = 0; c < art.characters.Length; c++) options.Add("c" + c);
            for (int f = 0; f < art.fullArtworks.Length; f++) options.Add("f" + f);
            if (options.Count == 0) return;
            // Avoid as many recent slides as possible while leaving at least two to choose from.
            for (int r = recent.Count - 1; r >= 0 && options.Count > 2; r--) options.Remove(recent[r]);
            if (options.Count > 1 && recent.Count > 0) options.Remove(recent[recent.Count - 1]);
            string key = options[UnityEngine.Random.Range(0, options.Count)];
            recent.Add(key);
            if (recent.Count > RecentMemory) recent.RemoveAt(0);
            bool full = key[0] == 'f';
            int n = int.Parse(key.Substring(1));

            Sprite bg = full ? art.fullArtworks[n] : art.stillBackgrounds[UnityEngine.Random.Range(0, art.stillBackgrounds.Length)];
            view.background.sprite = bg;
            if (view.backgroundFitter != null && bg != null) view.backgroundFitter.aspectRatio = bg.rect.width / bg.rect.height;
            view.character.gameObject.SetActive(!full);
            CurrentSlide = bg != null ? bg.name : "-";
            bool left = UnityEngine.Random.value < 0.5f;
            if (!full)
            {
                view.character.sprite = art.characters[n];
                CurrentSlide += " + " + art.characters[n].name;
                // Left or right half, feet at the bottom; it grows upward from the feet.
                RectTransform rt = view.character.rectTransform;
                rt.anchorMin = new Vector2(left ? 0.04f : 0.5f, 0f);
                rt.anchorMax = new Vector2(left ? 0.5f : 0.96f, 0.97f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            // Mostly sideways drift (with a little vertical), away from the character's side so the two layers separate.
            float x = left ? 1f : -1f;
            Vector2 drift = new Vector2(x, UnityEngine.Random.Range(-0.35f, 0.35f)).normalized;
            if (full) drift = UnityEngine.Random.insideUnitCircle.normalized;
            motion[index] = new Motion { Start = Time.unscaledTime, Full = full, Left = left, Drift = drift };
            Animate(index);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => recent.Clear();

        private static float Smooth(float t) => 0.5f - 0.5f * Mathf.Cos(Mathf.Clamp01(t) * Mathf.PI);
        private static float EaseOutCubic(float t) { t = 1f - Mathf.Clamp01(t); return 1f - t * t * t; }
    }
}
