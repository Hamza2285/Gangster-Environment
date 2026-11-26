using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The first drive, straight after the intro cutscene, in the middle of the police chase: the car controls come in one at a time.
// 1. Only the accelerator (the game steers along the road): a white glow blinks on it, time slows until it is pressed.
// 2. The steering buttons appear the same way; the first press hands the wheel to the player.
// 3. Then the brake.
// 4. Then everything else (handbrake, nitro, exit, dashboard, minimap, the whole HUD) comes back.
// On a phone the glow sits on the RCC touch pedals; with a keyboard it is drawn round on-screen keys (W, A D, S).
// Added at run time by IntroMission; everything it hides or changes is put back when it ends or is destroyed.
[DisallowMultipleComponent]
public class DriveTutorial : MonoBehaviour
{
    public enum Step { Gas, Steer, Brake, Done }

    public Step Current { get; private set; } = Step.Gas;
    public bool Finished => Current == Step.Done;
    /// <summary>The player has the wheel (from the first steering press on). Until then the game steers.</summary>
    public bool PlayerSteers { get; private set; }

    const float SlowMotion = 0.3f;        // time scale while a new control waits for its first press
    const float PressThreshold = 0.35f;

    RCC_CarControllerV3 car;
    Func<float> autoSteer;
    Canvas keep;                          // the mission's own HUD (letterbox, subtitles, objective) stays visible
    Font font;
    bool mobile;

    Canvas canvas;
    RectTransform canvasRect;
    Text prompt, promptSub;
    Image promptBack;
    readonly List<Glow> glows = new List<Glow>();
    readonly List<GameObject> keys = new List<GameObject>();

    class Glow
    {
        public RectTransform root, disk, ring;
        public Image diskImage, ringImage;
        public RectTransform target;      // a touch button to sit on (null: a fixed spot on this canvas)
        public float phase;
    }

    // What this hides while it runs, to put back exactly as it was.
    readonly List<Canvas> hiddenCanvases = new List<Canvas>();
    class Part { public CanvasGroup group; public bool added; public float alpha; public bool raycasts, interactable; }
    readonly Dictionary<Transform, Part> hiddenParts = new Dictionary<Transform, Part>();
    readonly HashSet<Transform> shownParts = new HashSet<Transform>();
    readonly Dictionary<Transform, float> revealStart = new Dictionary<Transform, float>();
    RCC_MobileButtons buttons;
    Transform rccRoot, buttonRow;

    float timeScaleTarget = 1f;
    bool restored;

    static Sprite diskSprite, ringSprite;

    public static DriveTutorial Begin(GameObject host, RCC_CarControllerV3 car, Func<float> autoSteer, Canvas keep, Font font)
    {
        var tutorial = host.AddComponent<DriveTutorial>();
        tutorial.car = car;
        tutorial.autoSteer = autoSteer;
        tutorial.keep = keep;
        tutorial.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tutorial.mobile = GameInput.MobileControls;
        tutorial.Build();
        tutorial.FindTouchControls();
        tutorial.HideAll();
        tutorial.StartCoroutine(tutorial.Run());
        return tutorial;
    }

    static RCC_Inputs Pedals => RCC_InputManager.GetInputs();

    // ---------- The steps ----------

    IEnumerator Run()
    {
        // 1. Accelerator only.
        Current = Step.Gas;
        if (car != null) car.externalController = true;
        Reveal(buttons != null ? buttons.gasButton : null);
        if (mobile) GlowOn(buttons != null ? buttons.gasButton : null);
        else Keys(new[] { "W" }, new[] { "ACCELERATE" });
        Prompt(mobile ? "HOLD TO ACCELERATE" : "HOLD  W  TO ACCELERATE", "Keep up with Mia!");
        yield return FirstPress(() => Pedals.throttleInput > PressThreshold);
        Prompt("KEEP IT DOWN!", "Stay on her tail");
        for (float held = 0f; held < 1.6f;)
        {
            if (Pedals.throttleInput > PressThreshold) held += Time.unscaledDeltaTime;
            yield return null;
        }

        // 2. Steering.
        Current = Step.Steer;
        ClearHints();
        if (buttons != null)
        {
            Reveal(buttons.leftButton);
            Reveal(buttons.rightButton);
            Reveal(buttons.steeringWheel);
            Reveal(buttons.joystick);
        }
        if (mobile)
        {
            if (buttons != null && buttons.steeringWheel != null && buttons.steeringWheel.gameObject.activeInHierarchy) GlowOn(buttons.steeringWheel);
            else if (buttons != null && buttons.joystick != null && buttons.joystick.gameObject.activeInHierarchy) GlowOn(buttons.joystick);
            else { GlowOn(buttons != null ? buttons.leftButton : null); GlowOn(buttons != null ? buttons.rightButton : null); }
        }
        else Keys(new[] { "A", "D" }, new[] { "LEFT", "RIGHT" });
        Prompt(mobile ? "TAP LEFT / RIGHT TO STEER" : "A / D  TO STEER", "You have the wheel");
        yield return FirstPress(() => Mathf.Abs(Pedals.steerInput) > PressThreshold);
        PlayerSteers = true;
        if (car != null) car.externalController = false;      // RCC reads the player's own input from here on
        Prompt("STEER AROUND THE TRAFFIC", "");
        yield return new WaitForSecondsRealtime(2f);

        // 3. Brake.
        Current = Step.Brake;
        ClearHints();
        Reveal(buttons != null ? buttons.brakeButton : null);
        if (mobile) GlowOn(buttons != null ? buttons.brakeButton : null);
        else Keys(new[] { "S" }, new[] { "BRAKE" });
        Prompt(mobile ? "TAP TO BRAKE" : "S  TO BRAKE", "Brake for the corners");
        yield return FirstPress(() => Pedals.brakeInput > PressThreshold);
        yield return new WaitForSecondsRealtime(0.5f);

        // 4. Everything.
        ClearHints();
        RestoreAll();
        Current = Step.Done;
        Prompt("ALL CONTROLS UNLOCKED", "");
        yield return new WaitForSecondsRealtime(1.8f);
        Prompt("", "");
    }

    // Slow motion until the control is first used; full speed again from that moment.
    IEnumerator FirstPress(Func<bool> pressed)
    {
        timeScaleTarget = SlowMotion;
        float since = 0f;
        while (true)
        {
            if (pressed() && since > 0.15f) break;     // a moment to read the prompt (and not a press still held from before)
            since += Time.unscaledDeltaTime;
            yield return null;
        }
        timeScaleTarget = 1f;
    }

    // While the game steers: the player's accelerator, the game's wheel (RCC reads nothing itself then).
    void FixedUpdate()
    {
        if (car == null || PlayerSteers || Finished) return;
        car.externalController = true;
        RCC_Inputs input = Pedals;
        car.throttleInput = Mathf.Clamp01(input.throttleInput);
        car.brakeInput = 0f;
        car.handbrakeInput = 0f;
        car.boostInput = 0f;
        car.steerInput = autoSteer != null ? Mathf.Clamp(autoSteer(), -1f, 1f) : 0f;
    }

    void Update()
    {
        // Eased in and out, and never over a pause (time scale 0 belongs to the pause menu).
        if (!restored && Time.timeScale > 0f && !Mathf.Approximately(Time.timeScale, timeScaleTarget))
            Time.timeScale = Mathf.MoveTowards(Time.timeScale, timeScaleTarget, Time.unscaledDeltaTime * 4f);
    }

    void LateUpdate()
    {
        if (restored) return;
        if (!Finished) HideAll();   // anything that switched itself back on (a HUD refresh, a popup) goes again
        float now = Time.unscaledTime;
        foreach (var pair in revealStart)
        {
            if (pair.Key == null || !hiddenParts.TryGetValue(pair.Key, out Part part) || part.group == null) continue;
            float k = Mathf.Clamp01((now - pair.Value) / 0.3f);
            part.group.alpha = Mathf.SmoothStep(0f, 1f, k);
            part.group.blocksRaycasts = part.group.interactable = true;
        }
        foreach (var glow in glows) UpdateGlow(glow, now);
    }

    void OnDestroy()
    {
        RestoreAll();
        if (Time.timeScale > 0f) Time.timeScale = 1f;
        if (car != null && !PlayerSteers) car.externalController = false;
        if (canvas != null) Destroy(canvas.gameObject);
    }

    // ---------- Hiding and showing the controls ----------

    void FindTouchControls()
    {
        buttons = FindFirstObjectByType<RCC_MobileButtons>(FindObjectsInactive.Include);
        if (buttons == null) return;
        Canvas c = buttons.GetComponentInParent<Canvas>(true);
        rccRoot = c != null ? c.rootCanvas.transform : buttons.transform;
        buttonRow = buttons.gasButton != null ? buttons.gasButton.transform.parent : null;
    }

    // Every other overlay canvas is switched off; on the RCC touch canvas everything but the controls taught so far is faded out.
    void HideAll()
    {
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!c.isRootCanvas || !c.enabled || c == canvas || c == keep || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            if (rccRoot != null && c.transform == rccRoot) continue;
            if (c.name == "Blink Canvas" || c.GetComponentInParent<OpenworldCrime.UI.Menu.LoadingScreen>() != null) continue;
            c.enabled = false;
            hiddenCanvases.Add(c);
        }
        if (rccRoot == null) return;
        foreach (Transform child in rccRoot)
        {
            if (child == buttonRow) continue;
            HidePart(child);
        }
        if (buttonRow != null)
            foreach (Transform child in buttonRow)
                if (!shownParts.Contains(child)) HidePart(child);
    }

    void HidePart(Transform t)
    {
        if (t.GetComponent<UnityEngine.EventSystems.EventSystem>() != null) return;
        if (!hiddenParts.TryGetValue(t, out Part part))
        {
            var group = t.GetComponent<CanvasGroup>();
            part = new Part { group = group, added = group == null };
            if (part.added) part.group = t.gameObject.AddComponent<CanvasGroup>();
            else { part.alpha = group.alpha; part.raycasts = group.blocksRaycasts; part.interactable = group.interactable; }
            hiddenParts.Add(t, part);
        }
        if (shownParts.Contains(t) || part.group == null) return;
        part.group.alpha = 0f;
        part.group.blocksRaycasts = false;
        part.group.interactable = false;
    }

    void Reveal(Component control)
    {
        if (control == null) return;
        Transform t = control.transform;
        if (!shownParts.Add(t)) return;
        HidePart(t);
        revealStart[t] = Time.unscaledTime;
    }

    void RestoreAll()
    {
        if (restored) return;
        restored = true;
        timeScaleTarget = 1f;
        foreach (var c in hiddenCanvases) if (c != null) c.enabled = true;
        hiddenCanvases.Clear();
        foreach (var pair in hiddenParts)
        {
            Part part = pair.Value;
            if (part.group == null) continue;
            if (part.added) Destroy(part.group);
            else { part.group.alpha = part.alpha; part.group.blocksRaycasts = part.raycasts; part.group.interactable = part.interactable; }
        }
        hiddenParts.Clear();
        revealStart.Clear();
        ClearHints();
    }

    // ---------- Hints ----------

    void Build()
    {
        var go = new GameObject("Drive Tutorial HUD");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 145;                     // over the touch pedals, so the glow is drawn on top of them
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRect = (RectTransform)go.transform;

        var back = new GameObject("Prompt Back", typeof(RectTransform)).GetComponent<RectTransform>();
        back.SetParent(canvasRect, false);
        back.anchorMin = back.anchorMax = new Vector2(0.5f, 0.5f);
        back.anchoredPosition = new Vector2(0f, 250f);
        back.sizeDelta = new Vector2(900f, 120f);
        promptBack = back.gameObject.AddComponent<Image>();
        promptBack.sprite = DiskSprite;
        promptBack.color = new Color(0f, 0f, 0f, 0.45f);
        promptBack.raycastTarget = false;

        prompt = NewText("Prompt", 56, new Vector2(0f, 262f), FontStyle.Bold);
        promptSub = NewText("Prompt Sub", 30, new Vector2(0f, 212f), FontStyle.Normal);
        promptSub.color = new Color(1f, 0.85f, 0.3f, 1f);
        Prompt("", "");
    }

    Text NewText(string name, int size, Vector2 position, FontStyle style)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(canvasRect, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(1500f, 80f);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(2.5f, -2.5f);
        return text;
    }

    void Prompt(string title, string sub)
    {
        if (prompt == null) return;
        prompt.text = title;
        promptSub.text = sub;
        promptBack.enabled = !string.IsNullOrEmpty(title);
        promptBack.rectTransform.sizeDelta = new Vector2(Mathf.Max(620f, prompt.preferredWidth + 160f), 130f);
    }

    void GlowOn(Component control)
    {
        if (control == null) return;
        var glow = MakeGlow();
        glow.target = control.transform as RectTransform;
        glows.Add(glow);
    }

    // Keyboard: round on-screen keys at the bottom of the screen, each with its glow.
    void Keys(string[] letters, string[] labels)
    {
        float spacing = 190f;
        float x0 = -(letters.Length - 1) * spacing * 0.5f;
        for (int i = 0; i < letters.Length; i++)
        {
            Vector2 at = new Vector2(x0 + i * spacing, 210f);
            var glow = MakeGlow();
            glow.root.anchorMin = glow.root.anchorMax = new Vector2(0.5f, 0f);
            glow.root.anchoredPosition = at;
            glow.root.sizeDelta = new Vector2(190f, 190f);
            glow.phase = i * 0.15f;
            glows.Add(glow);

            var key = new GameObject("Key " + letters[i], typeof(RectTransform)).GetComponent<RectTransform>();
            key.SetParent(canvasRect, false);
            key.anchorMin = key.anchorMax = new Vector2(0.5f, 0f);
            key.anchoredPosition = at;
            key.sizeDelta = new Vector2(118f, 118f);
            var face = key.gameObject.AddComponent<Image>();
            face.sprite = DiskSprite;
            face.color = new Color(0.08f, 0.08f, 0.1f, 0.85f);
            face.raycastTarget = false;
            var letter = new GameObject("Letter", typeof(RectTransform)).GetComponent<RectTransform>();
            letter.SetParent(key, false);
            letter.anchorMin = Vector2.zero;
            letter.anchorMax = Vector2.one;
            letter.offsetMin = letter.offsetMax = Vector2.zero;
            var text = letter.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = 58;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = letters[i];
            var label = NewText("Label", 26, Vector2.zero, FontStyle.Bold);
            label.rectTransform.SetParent(key, false);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            label.rectTransform.anchoredPosition = new Vector2(0f, -30f);
            label.text = labels[i];
            keys.Add(key.gameObject);
        }
    }

    void ClearHints()
    {
        foreach (var glow in glows) if (glow.root != null) Destroy(glow.root.gameObject);
        glows.Clear();
        foreach (var key in keys) if (key != null) Destroy(key);
        keys.Clear();
    }

    Glow MakeGlow()
    {
        var root = new GameObject("Glow", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(canvasRect, false);
        root.SetAsFirstSibling();
        var glow = new Glow { root = root };
        glow.disk = Layer(root, "Blur", DiskSprite, out glow.diskImage);
        glow.ring = Layer(root, "Ring", RingSprite, out glow.ringImage);
        return glow;
    }

    static RectTransform Layer(RectTransform parent, string name, Sprite sprite, out Image image)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.raycastTarget = false;     // touches go straight through to the button underneath
        return rect;
    }

    // A soft white blur that blinks, and a ring that keeps rippling out from it.
    void UpdateGlow(Glow glow, float now)
    {
        if (glow.root == null) return;
        if (glow.target != null)
        {
            if (!glow.target.gameObject.activeInHierarchy) { glow.root.gameObject.SetActive(false); return; }
            glow.root.gameObject.SetActive(true);
            var corners = new Vector3[4];
            glow.target.GetWorldCorners(corners);              // screen pixels: the touch canvas is a screen overlay
            Vector2 a = corners[0], b = corners[2];
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, (a + b) * 0.5f, null, out Vector2 centre);
            float scale = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            float size = Mathf.Max(b.x - a.x, b.y - a.y) / scale * 1.05f;
            glow.root.anchorMin = glow.root.anchorMax = new Vector2(0.5f, 0.5f);
            glow.root.anchoredPosition = centre;
            glow.root.sizeDelta = new Vector2(size, size);
        }
        float t = now + glow.phase;
        float blink = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f * 1.4f);
        glow.diskImage.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.12f, 0.55f, blink));
        glow.disk.localScale = Vector3.one * Mathf.Lerp(0.92f, 1.05f, blink);
        float ripple = Mathf.Repeat(t * 1.1f, 1f);
        glow.ringImage.color = new Color(1f, 1f, 1f, (1f - ripple) * 0.95f);
        glow.ring.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.45f, ripple);
    }

    static Sprite DiskSprite => diskSprite != null ? diskSprite : (diskSprite = MakeSprite(r => Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.35f, 1f, r))));
    static Sprite RingSprite => ringSprite != null ? ringSprite : (ringSprite = MakeSprite(r => Mathf.Exp(-Mathf.Pow((r - 0.8f) / 0.08f, 2f))));

    static Sprite MakeSprite(Func<float, float> alpha)
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r > 1f ? 0f : alpha(r)) * 255f));
            }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
