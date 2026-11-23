using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.UI.Menu
{
    /// <summary>
    /// "Navigation Experience": the screen between the main menu and the loading screen where the player picks the minimap
    /// style. A selector wheel: one minimap card at a time in the middle, arrows either side switch to the other style, its name
    /// and page dots underneath. The shown style is the choice: saved (PlayerPrefs <see cref="PrefKey"/>, "Square" or "Circle")
    /// on NEXT, which goes on to the loading screen; the game's Minimap Manager builds that minimap. It reopens on the last choice.
    /// Built by NavigationScreenBuilder (Tools > OpenworldCrime > UI > Build Navigation Screen).
    /// </summary>
    [DisallowMultipleComponent]
    public class NavigationScreen : MonoBehaviour
    {
        public const string PrefKey = "MinimapShape";
        public const string Square = "Square";
        public const string Circle = "Circle";

        private static readonly string[] Options = { Square, Circle };
        private static readonly string[] Names = { "MINIMAP (SQUARE)", "MINIMAP (CIRCLE)" };

        [SerializeField] private GameObject root;
        [SerializeField] private CanvasGroup group;
        [Tooltip("One card per option, in the order Square, Circle.")]
        [SerializeField] private RectTransform[] cards = new RectTransform[0];
        [SerializeField] private Button previousButton;
        [SerializeField] private Button nextOptionButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private Text optionLabel;
        [SerializeField] private Image[] dots = new Image[0];
        [SerializeField] private Color dotOn = Color.white;
        [SerializeField] private Color dotOff = new Color(1f, 1f, 1f, 0.35f);
        [SerializeField, Min(0.05f)] private float fadeSeconds = 0.3f;
        [SerializeField, Min(0.05f)] private float switchSeconds = 0.22f;

        private Action onDone;
        private int index;
        private bool leaving;
        private Coroutine switching;

        /// <summary>The saved minimap style ("Square" or "Circle"); Square when none was chosen yet.</summary>
        public static string SavedShape => PlayerPrefs.GetString(PrefKey, Square) == Circle ? Circle : Square;

        public bool IsOpen => root != null && root.activeSelf;

        private void Awake()
        {
            if (root != null) root.SetActive(false);
        }

        private void OnEnable()
        {
            if (previousButton != null) previousButton.onClick.AddListener(Previous);
            if (nextOptionButton != null) nextOptionButton.onClick.AddListener(NextOption);
            if (nextButton != null) nextButton.onClick.AddListener(Next);
        }

        private void OnDisable()
        {
            if (previousButton != null) previousButton.onClick.RemoveListener(Previous);
            if (nextOptionButton != null) nextOptionButton.onClick.RemoveListener(NextOption);
            if (nextButton != null) nextButton.onClick.RemoveListener(Next);
        }

        /// <summary>Opens the screen; <paramref name="done"/> runs when the player presses NEXT.</summary>
        public void Show(Action done)
        {
            onDone = done;
            leaving = false;
            index = SavedShape == Circle ? 1 : 0;
            ShowOption(0);
            SetInteractable(true);
            root.SetActive(true);
            StopAllCoroutines();
            switching = null;
            StartCoroutine(Fade(0f, 1f));
        }

        /// <summary>The style currently on screen ("Square" or "Circle").</summary>
        public string Current => Options[index];

        private void Previous() => Step(-1);
        private void NextOption() => Step(1);

        private void Step(int direction)
        {
            if (leaving) return;
            index = (index + direction + Options.Length) % Options.Length;
            ShowOption(direction);
        }

        // The new card slides in from the side it came from, while the old one is hidden.
        private void ShowOption(int direction)
        {
            for (int i = 0; i < cards.Length; i++)
                if (cards[i] != null) cards[i].gameObject.SetActive(i == index);
            if (optionLabel != null) optionLabel.text = Names[Mathf.Clamp(index, 0, Names.Length - 1)];
            for (int i = 0; i < dots.Length; i++)
                if (dots[i] != null) dots[i].color = i == index ? dotOn : dotOff;
            if (direction != 0 && index < cards.Length && cards[index] != null && isActiveAndEnabled)
            {
                if (switching != null) StopCoroutine(switching);
                switching = StartCoroutine(SlideIn(cards[index], direction));
            }
        }

        private IEnumerator SlideIn(RectTransform card, int direction)
        {
            Vector2 home = new Vector2(0f, card.anchoredPosition.y);
            var cardGroup = card.GetComponent<CanvasGroup>();
            for (float t = 0f; t < switchSeconds; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / switchSeconds);
                card.anchoredPosition = home + Vector2.right * (1f - k) * 160f * direction;
                if (cardGroup != null) cardGroup.alpha = k;
                yield return null;
            }
            card.anchoredPosition = home;
            if (cardGroup != null) cardGroup.alpha = 1f;
            switching = null;
        }

        private void Next()
        {
            if (leaving) return;
            leaving = true;
            PlayerPrefs.SetString(PrefKey, Current);
            PlayerPrefs.Save();
            SetInteractable(false);
            // The loading screen fades in over this screen; it goes with the menu scene.
            onDone?.Invoke();
        }

        private void SetInteractable(bool on)
        {
            if (previousButton != null) previousButton.interactable = on;
            if (nextOptionButton != null) nextOptionButton.interactable = on;
            if (nextButton != null) nextButton.interactable = on;
        }

        private IEnumerator Fade(float from, float to)
        {
            if (group == null) yield break;
            for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.Lerp(from, to, t / fadeSeconds);
                yield return null;
            }
            group.alpha = to;
        }
    }
}
