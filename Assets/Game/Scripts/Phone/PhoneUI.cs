using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The player's phone. P (or the phone button in touch mode) slides it in on the right of the screen: a phone-shaped panel with
// a home screen and three apps for now -
//   Contacts: Mia (calling her: she's busy for now),
//   Messages: empty,
//   Settings: the game settings (graphics quality, volume, minimap shape, controls).
// Incoming calls (IncomingCall) show a small ringing badge - the caller's picture in a circle with a phone icon - answered with
// Enter or a tap; the conversation plays as subtitles on the call screen, then the call ends.
// Built at run time in the gameplay scene (no prefab, no scene object), all art drawn in code.
[DisallowMultipleComponent]
public class PhoneUI : MonoBehaviour
{
    public static PhoneUI Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.open;
    public static bool InCall => Instance != null && (Instance.ringing || Instance.calling);

    public const string VolumeKey = "Settings.Volume";

    public class Contact
    {
        public string name;
        public string number;
        public Sprite portrait;
        public bool busy = true;
    }

    readonly List<Contact> contacts = new List<Contact>();
    public Contact Mia { get; private set; }

    // ---------- Start-up ----------

    static bool hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; hooked = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        ApplySavedVolume();
        if (!hooked) { SceneManager.sceneLoaded += (s, m) => TryCreate(); hooked = true; }
        TryCreate();
    }

    static void ApplySavedVolume() => AudioListener.volume = PlayerPrefs.GetFloat(VolumeKey, 1f);

    // Only where there is someone to carry it: the gameplay scene.
    static void TryCreate()
    {
        if (Instance != null) return;
        if (FindFirstObjectByType<OpenworldCrime.Player.PlayerVehicleInteraction>(FindObjectsInactive.Include) == null) return;
        new GameObject("[Phone]").AddComponent<PhoneUI>();
    }

    /// <summary>The phone of this scene (made if missing).</summary>
    public static PhoneUI Ensure()
    {
        if (Instance == null) new GameObject("[Phone]").AddComponent<PhoneUI>();
        return Instance;
    }

    // ---------- State ----------

    enum Screen { Home, Contacts, Messages, Settings, Call }
    Screen screen = Screen.Home;
    bool open, ringing, calling;
    float slide;                       // 0 closed .. 1 open
    Contact caller;
    string[] callLines;
    Action callDone;
    bool answer, hangUp;
    Coroutine callRoutine;
    bool cursorWasVisible;
    CursorLockMode cursorWasLock;

    Font font;
    Canvas canvas;
    RectTransform canvasRect, phone, screenArea;
    readonly Dictionary<Screen, RectTransform> screens = new Dictionary<Screen, RectTransform>();
    Text clock, statusClock, bigDate;
    Text subtitle;
    GameObject phoneButton;
    RectTransform incoming;
    RectTransform incomingRing;
    Text incomingHint;
    AudioSource audioSource;
    AudioClip ringClip, ringbackClip, busyClip;

    // Call screen
    Image callPortrait;
    Text callName, callStatus;
    RectTransform callPulse;
    Button endCallButton;

    // Settings screen
    Text graphicsValue, minimapValue, controlsValue;
    Slider volumeSlider;

    static readonly Color Pink = new Color(1f, 0.36f, 0.62f);
    static readonly Color Green = new Color(0.2f, 0.8f, 0.42f);
    static readonly Color Red = new Color(0.93f, 0.25f, 0.25f);

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        font = GameButtonStyle.Font != null ? GameButtonStyle.Font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Mia = new Contact { name = "Mia", number = "305-555-0147", portrait = FindPortrait("girl"), busy = true };
        contacts.Add(Mia);
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.ignoreListenerPause = true;
        ringClip = Tone("Ring", new[] { 440f, 480f }, new[] { 0.4f, 0.2f, 0.4f, 1.6f });
        ringbackClip = Tone("Ringback", new[] { 440f, 480f }, new[] { 1.0f, 1.5f });
        busyClip = Tone("Busy", new[] { 480f, 620f }, new[] { 0.25f, 0.25f });
        Build();
        SetScreen(Screen.Home);
        phone.anchoredPosition = ClosedPosition;
        phone.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    static Sprite FindPortrait(string id)
    {
        foreach (var c in Resources.FindObjectsOfTypeAll<PlayableCharacter>())
            if (c != null && c.gameObject.scene.IsValid() && c.id == id && c.portrait != null) return c.portrait;
        return null;
    }

    // ---------- Open / close ----------

    static Vector2 OpenPosition => new Vector2(-60f, 0f);
    static Vector2 ClosedPosition => new Vector2(480f, 0f);

    public void Open(bool on)
    {
        if (open == on) return;
        if (on && GamePlayer.MenuOpen) return;           // the map, a shop or a cutscene is up
        open = on;
        if (on)
        {
            EnsureEventSystem();
            phone.gameObject.SetActive(true);
            if (!calling) SetScreen(Screen.Home);
            cursorWasVisible = Cursor.visible;
            cursorWasLock = Cursor.lockState;
        }
        else if (!GameInput.MobileControls)
        {
            Cursor.visible = cursorWasVisible && cursorWasLock == CursorLockMode.None;
            Cursor.lockState = Cursor.visible ? CursorLockMode.None : CursorLockMode.Locked;
        }
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null && EventSystem.current.isActiveAndEnabled) return;
        foreach (var e in FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (e.isActiveAndEnabled) return;
        var go = new GameObject("Phone EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        go.transform.SetParent(Instance.transform, false);
    }

    void Update()
    {
        // Keys (the cutscene and the menus keep the phone shut).
        bool blocked = GamePlayer.MenuOpen || canvas == null || !canvas.enabled;
        if (!blocked && !calling && !ringing && GameInput.PhoneDown) Open(!open);
        if (open && !calling && GameInput.MenuBackDown)
        {
            if (screen == Screen.Home) Open(false); else SetScreen(Screen.Home);
        }
        if (ringing && GameInput.EnterDown && !blocked) answer = true;

        // Mouse for the phone's buttons while it is out (keyboard and mouse).
        if (open && !GameInput.MobileControls)
        {
            if (!Cursor.visible) Cursor.visible = true;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        }

        float target = open ? 1f : 0f;
        if (!Mathf.Approximately(slide, target))
        {
            slide = Mathf.MoveTowards(slide, target, Time.unscaledDeltaTime / 0.25f);
            phone.anchoredPosition = Vector2.Lerp(ClosedPosition, OpenPosition, Mathf.SmoothStep(0f, 1f, slide));
            if (slide <= 0f) phone.gameObject.SetActive(false);
        }

        if (open) UpdateClock();
        if (phoneButton != null) phoneButton.SetActive(GameInput.MobileControls && !open && !ringing && !GamePlayer.MenuOpen);
        AnimateIncoming();
        if (callPulse != null && callPulse.gameObject.activeSelf)
        {
            float k = Mathf.Repeat(Time.unscaledTime * 0.8f, 1f);
            callPulse.localScale = Vector3.one * Mathf.Lerp(1f, 1.35f, k);
            callPulse.GetComponent<Image>().color = new Color(1f, 1f, 1f, (1f - k) * 0.5f);
        }
    }

    void UpdateClock()
    {
        float hour = -1f;
        var tod = FindTimeOfDay();
        if (tod != null) hour = tod.Hour;
        string text = hour >= 0f ? string.Format("{0:00}:{1:00}", Mathf.FloorToInt(hour) % 24, Mathf.FloorToInt(Mathf.Repeat(hour, 1f) * 60f))
                                 : DateTime.Now.ToString("HH:mm");
        if (clock != null) clock.text = text;
        if (statusClock != null) statusClock.text = text;
    }

    OpenworldCrime.Environment.TimeOfDayManager timeOfDay;
    bool timeOfDaySearched;
    OpenworldCrime.Environment.TimeOfDayManager FindTimeOfDay()
    {
        if (!timeOfDaySearched) { timeOfDay = FindFirstObjectByType<OpenworldCrime.Environment.TimeOfDayManager>(); timeOfDaySearched = true; }
        return timeOfDay;
    }

    // ---------- Calls ----------

    /// <summary>The contact rings: a small badge (their picture, a phone icon) until answered (Enter or a tap); then the
    /// lines play as subtitles on the call screen ("NAME:  text"), the call ends and <paramref name="done"/> runs.</summary>
    public void IncomingCall(Contact from, string[] lines, Action done)
    {
        if (callRoutine != null) StopCoroutine(callRoutine);
        callRoutine = StartCoroutine(IncomingRoutine(from, lines, done));
    }

    IEnumerator IncomingRoutine(Contact from, string[] lines, Action done)
    {
        caller = from;
        ringing = true;
        answer = false;
        hangUp = false;
        if (open) Open(false);
        SetIncoming(from);
        incoming.gameObject.SetActive(true);
        audioSource.clip = ringClip;
        audioSource.loop = true;
        audioSource.volume = 0.55f;
        audioSource.Play();
#if UNITY_ANDROID || UNITY_IOS
        float buzz = 0f;
#endif
        while (!answer)
        {
            // Rings on through a cutscene or a menu, but it is answered only once the game is back.
#if UNITY_ANDROID || UNITY_IOS
            buzz -= Time.unscaledDeltaTime;
            if (buzz <= 0f) { Handheld.Vibrate(); buzz = 2.6f; }
#endif
            incoming.gameObject.SetActive(canvas.enabled);
            yield return null;
        }
        ringing = false;
        incoming.gameObject.SetActive(false);
        audioSource.Stop();

        // On the call.
        calling = true;
        Open(true);
        ShowCall(from, "00:00", false);
        float started = Time.unscaledTime;
        foreach (string line in lines)
        {
            if (hangUp) break;
            ShowLine(line);
            float seconds = Mathf.Clamp(1.4f + line.Length * 0.055f, 2.4f, 5.5f);
            for (float t = 0f; t < seconds && !hangUp; t += Time.unscaledDeltaTime)
            {
                callStatus.text = CallTime(Time.unscaledTime - started);
                if (GameInput.EnterDown && t > 0.4f) break;     // Enter skips ahead
                yield return null;
            }
        }
        ShowLine("");
        callStatus.text = "Call ended";
        endCallButton.interactable = false;
        yield return new WaitForSecondsRealtime(1.3f);
        calling = false;
        Open(false);
        callRoutine = null;
        done?.Invoke();
    }

    static string CallTime(float seconds) => string.Format("{0:00}:{1:00}", Mathf.FloorToInt(seconds / 60f), Mathf.FloorToInt(seconds) % 60);

    void ShowLine(string line)
    {
        if (string.IsNullOrEmpty(line)) { subtitle.text = ""; return; }
        int colon = line.IndexOf(':');
        if (colon > 0)
        {
            string who = line.Substring(0, colon + 1);
            string colour = who.StartsWith(caller != null ? caller.name.ToUpperInvariant() : "\u0000") ? "#FF5C9E" : "#7FD4FF";
            subtitle.text = "<color=" + colour + ">" + who + "</color>" + line.Substring(colon + 1);
        }
        else subtitle.text = line;
    }

    // Calling someone from Contacts. Everyone is busy for now.
    IEnumerator OutgoingRoutine(Contact to)
    {
        calling = true;
        hangUp = false;
        ShowCall(to, "Calling...", true);
        audioSource.clip = ringbackClip;
        audioSource.loop = true;
        audioSource.volume = 0.45f;
        audioSource.Play();
        for (float t = 0f; t < 3.2f && !hangUp; t += Time.unscaledDeltaTime)
        {
            callStatus.text = "Calling" + new string('.', 1 + Mathf.FloorToInt(t * 2f) % 3);
            yield return null;
        }
        audioSource.Stop();
        if (!hangUp)
        {
            callPulse.gameObject.SetActive(false);
            callStatus.text = to.busy ? "Busy" : "No answer";
            subtitle.text = to.busy ? to.name + " is busy right now. Try again later." : "";
            audioSource.clip = busyClip;
            audioSource.loop = true;
            audioSource.Play();
            for (float t = 0f; t < 2.2f && !hangUp; t += Time.unscaledDeltaTime) yield return null;
            audioSource.Stop();
        }
        subtitle.text = "";
        callStatus.text = "Call ended";
        endCallButton.interactable = false;
        yield return new WaitForSecondsRealtime(0.8f);
        calling = false;
        callRoutine = null;
        SetScreen(Screen.Contacts);
    }

    void Call(Contact to)
    {
        if (calling || ringing) return;
        if (callRoutine != null) StopCoroutine(callRoutine);
        callRoutine = StartCoroutine(OutgoingRoutine(to));
    }

    void ShowCall(Contact who, string status, bool pulsing)
    {
        SetScreen(Screen.Call);
        callPortrait.sprite = who.portrait != null ? who.portrait : PhoneArt.Person;
        callPortrait.preserveAspect = true;
        callName.text = who.name;
        callStatus.text = status;
        callPulse.gameObject.SetActive(pulsing);
        endCallButton.interactable = true;
    }

    // ---------- Screens ----------

    void SetScreen(Screen s)
    {
        screen = s;
        foreach (var pair in screens) pair.Value.gameObject.SetActive(pair.Key == s);
        if (s == Screen.Settings) RefreshSettings();
        if (s != Screen.Call && subtitle != null && !calling) subtitle.text = "";
    }

    void Build()
    {
        var go = new GameObject("Phone Canvas");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        canvasRect = (RectTransform)go.transform;

        BuildPhone();
        BuildIncoming();
        BuildPhoneButton();

        subtitle = Label(canvasRect, "Call Subtitle", "", 36, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        var sr = subtitle.rectTransform;
        sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0f);
        sr.anchoredPosition = new Vector2(-180f, 330f);
        sr.sizeDelta = new Vector2(1300f, 90f);
        subtitle.horizontalOverflow = HorizontalWrapMode.Wrap;
        subtitle.supportRichText = true;
        Shadowed(subtitle);
    }

    // The handset: a dark body with a thin rim, the screen inset into it, a notch, a home bar.
    void BuildPhone()
    {
        phone = Rect(canvasRect, "Phone", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(420f, 840f));
        phone.pivot = new Vector2(1f, 0.5f);          // its right edge on the screen's
        var rim = Round(phone, "Rim", new Color(0.32f, 0.33f, 0.38f, 1f), 62f);
        Fill(rim.rectTransform, -3f);
        var body = Round(phone, "Body", new Color(0.05f, 0.05f, 0.07f, 1f), 60f);
        Fill(body.rectTransform, 0f);
        // The screen: a rounded mask (its corners) over the wallpaper.
        var screenImage = Round(phone, "Screen", Color.white, 46f);
        screenArea = screenImage.rectTransform;
        Fill(screenArea, 14f);
        screenArea.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var wallpaper = Img(screenArea, "Wallpaper", PhoneArt.Wallpaper, Color.white);
        wallpaper.preserveAspect = false;
        Fill(wallpaper.rectTransform, 0f);

        var notch = Round(phone, "Notch", Color.black, 13f);
        Place(notch.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(120f, 28f));
        var bar = Round(phone, "Home Bar", new Color(1f, 1f, 1f, 0.8f), 3f);
        Place(bar.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(140f, 6f));
        var homeHit = bar.gameObject.AddComponent<Button>();
        homeHit.onClick.AddListener(() => { if (!calling) SetScreen(Screen.Home); });
        bar.rectTransform.sizeDelta = new Vector2(140f, 6f);

        // Status bar.
        statusClock = Label(screenArea, "Status Clock", "12:00", 22, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        Place(statusClock.rectTransform, new Vector2(0f, 1f), new Vector2(70f, -24f), new Vector2(110f, 30f));
        var signal = Label(screenArea, "Status Right", "LTE  100%", 18, FontStyle.Bold, TextAnchor.MiddleRight, Color.white);
        Place(signal.rectTransform, new Vector2(1f, 1f), new Vector2(-70f, -24f), new Vector2(140f, 30f));

        screens[Screen.Home] = BuildHome();
        screens[Screen.Contacts] = BuildContacts();
        screens[Screen.Messages] = BuildMessages();
        screens[Screen.Settings] = BuildSettings();
        screens[Screen.Call] = BuildCall();

        // A close button on the side of the phone (touch: there is no P key).
        var close = Round(phone, "Close", new Color(0.05f, 0.05f, 0.07f, 0.95f), 28f);
        Place(close.rectTransform, new Vector2(0f, 1f), new Vector2(-34f, -40f), new Vector2(56f, 56f));
        var x = Label(close.rectTransform, "X", "X", 28, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Fill(x.rectTransform, 0f);
        close.gameObject.AddComponent<Button>().onClick.AddListener(() => { if (!calling) Open(false); });
    }

    RectTransform Page(string name)
    {
        var page = Rect(screenArea, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        page.offsetMin = new Vector2(0f, 40f);
        page.offsetMax = new Vector2(0f, -48f);
        return page;
    }

    RectTransform BuildHome()
    {
        var page = Page("Home");
        clock = Label(page, "Clock", "12:00", 92, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Place(clock.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(380f, 110f));
        Shadowed(clock);
        bigDate = Label(page, "Place", "VICE BEACH", 22, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.8f));
        Place(bigDate.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(380f, 30f));

        AppIcon(page, "Contacts", PhoneArt.Person, Green, new Vector2(-118f, -280f), () => SetScreen(Screen.Contacts));
        AppIcon(page, "Messages", PhoneArt.Bubble, new Color(0.2f, 0.55f, 1f), new Vector2(0f, -280f), () => SetScreen(Screen.Messages));
        AppIcon(page, "Settings", PhoneArt.Gear, new Color(0.45f, 0.47f, 0.53f), new Vector2(118f, -280f), () => SetScreen(Screen.Settings));

        var hint = Label(page, "Hint", "", 18, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.55f));
        Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(360f, 30f));
        hint.text = Application.isMobilePlatform ? "" : "P  to put the phone away";
        return page;
    }

    void AppIcon(RectTransform parent, string name, Sprite glyph, Color colour, Vector2 at, UnityAction onClick)
    {
        var tile = Round(parent, name, colour, 24f);
        Place(tile.rectTransform, new Vector2(0.5f, 1f), at, new Vector2(96f, 96f));
        var icon = Img(tile.rectTransform, "Glyph", glyph, Color.white);
        Fill(icon.rectTransform, 20f);
        var label = Label(parent, name + " Label", name, 20, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Place(label.rectTransform, new Vector2(0.5f, 1f), at + new Vector2(0f, -66f), new Vector2(120f, 28f));
        Shadowed(label);
        var button = tile.gameObject.AddComponent<Button>();
        button.onClick.AddListener(onClick);
        Pressable(button);
    }

    RectTransform Header(RectTransform page, string title)
    {
        var back = Label(page, "Back", "<", 44, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Place(back.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -40f), new Vector2(56f, 56f));
        back.raycastTarget = true;
        back.gameObject.AddComponent<Button>().onClick.AddListener(() => SetScreen(Screen.Home));
        var t = Label(page, "Title", title, 36, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(200f, -40f), new Vector2(260f, 50f));
        var line = Img(page, "Rule", null, new Color(1f, 1f, 1f, 0.15f));
        Place(line.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(340f, 2f));
        return page;
    }

    RectTransform BuildContacts()
    {
        var page = Header(Page("Contacts"), "Contacts");
        float y = -130f;
        foreach (var contact in contacts)
        {
            var row = Round(page, contact.name, new Color(0f, 0f, 0f, 0.35f), 20f);
            Place(row.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(350f, 100f));
            var face = Portrait(row.rectTransform, contact.portrait, 74f);
            Place(face, new Vector2(0f, 0.5f), new Vector2(52f, 0f), new Vector2(74f, 74f));
            var name = Label(row.rectTransform, "Name", contact.name, 30, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(170f, 14f), new Vector2(140f, 36f));
            var number = Label(row.rectTransform, "Number", contact.number, 18, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.6f));
            Place(number.rectTransform, new Vector2(0f, 0.5f), new Vector2(170f, -18f), new Vector2(160f, 26f));
            var call = Round(row.rectTransform, "Call", Green, 30f);
            Place(call.rectTransform, new Vector2(1f, 0.5f), new Vector2(-46f, 0f), new Vector2(60f, 60f));
            var handset = Img(call.rectTransform, "Handset", PhoneArt.Handset, Color.white);
            Fill(handset.rectTransform, 13f);
            Contact c = contact;
            var button = call.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() => Call(c));
            Pressable(button);
            y -= 116f;
        }
        return page;
    }

    RectTransform BuildMessages()
    {
        var page = Header(Page("Messages"), "Messages");
        var icon = Img(page, "Empty Icon", PhoneArt.Bubble, new Color(1f, 1f, 1f, 0.18f));
        Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(130f, 130f));
        var text = Label(page, "Empty", "No messages", 28, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.6f));
        Place(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(340f, 40f));
        return page;
    }

    RectTransform BuildSettings()
    {
        var page = Header(Page("Settings"), "Settings");
        float y = -130f;
        graphicsValue = Stepper(page, "Graphics", ref y, () => StepGraphics(-1), () => StepGraphics(1));
        volumeSlider = SliderRow(page, "Volume", ref y, PlayerPrefs.GetFloat(VolumeKey, 1f), SetVolume);
        minimapValue = Stepper(page, "Minimap", ref y, ToggleMinimap, ToggleMinimap);
        if (!Application.isMobilePlatform) controlsValue = Stepper(page, "Controls", ref y, ToggleControls, ToggleControls);
        return page;
    }

    RectTransform BuildCall()
    {
        var page = Page("Call");
        var dim = Img(page, "Dim", null, new Color(0f, 0f, 0f, 0.55f));
        Fill(dim.rectTransform, 0f);
        dim.rectTransform.offsetMin = new Vector2(0f, -40f);
        dim.rectTransform.offsetMax = new Vector2(0f, 48f);

        var pulse = Img(page, "Pulse", PhoneArt.Ring, Color.white);
        callPulse = pulse.rectTransform;
        Place(callPulse, new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(230f, 230f));
        var face = Portrait(page, null, 190f);
        Place(face, new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(190f, 190f));
        callPortrait = face.GetChild(0).GetComponent<Image>();
        callName = Label(page, "Name", "", 44, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Place(callName.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -340f), new Vector2(360f, 56f));
        callStatus = Label(page, "Status", "", 26, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.75f));
        Place(callStatus.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -385f), new Vector2(360f, 36f));

        var end = Round(page, "End Call", Red, 40f);
        Place(end.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(84f, 84f));
        var handset = Img(end.rectTransform, "Handset", PhoneArt.Handset, Color.white);
        Fill(handset.rectTransform, 18f);
        handset.rectTransform.localEulerAngles = new Vector3(0f, 0f, 135f);
        endCallButton = end.gameObject.AddComponent<Button>();
        endCallButton.onClick.AddListener(() => hangUp = true);
        Pressable(endCallButton);
        return page;
    }

    // ---------- Settings ----------

    OpenworldCrime.Settings.GraphicsSettingsManager Graphics => FindFirstObjectByType<OpenworldCrime.Settings.GraphicsSettingsManager>();

    void RefreshSettings()
    {
        var g = Graphics;
        if (graphicsValue != null) graphicsValue.text = g != null && g.CurrentProfile != null ? g.CurrentProfile.DisplayName : "-";
        if (volumeSlider != null) volumeSlider.SetValueWithoutNotify(AudioListener.volume);
        if (minimapValue != null) minimapValue.text = OpenworldCrime.UI.Menu.NavigationScreen.SavedShape;
        if (controlsValue != null) controlsValue.text = GameInput.MobileControls ? "Touch" : "Keyboard";
    }

    void StepGraphics(int direction)
    {
        var g = Graphics;
        if (g == null || g.Profiles == null || g.Profiles.Count == 0) return;
        int index = 0;
        for (int i = 0; i < g.Profiles.Count; i++) if (g.Profiles[i] == g.CurrentProfile) index = i;
        index = Mathf.Clamp(index + direction, 0, g.Profiles.Count - 1);
        g.SetProfile(index);
        RefreshSettings();
    }

    void SetVolume(float value)
    {
        AudioListener.volume = value;
        PlayerPrefs.SetFloat(VolumeKey, value);
        PlayerPrefs.Save();
    }

    void ToggleMinimap()
    {
        string next = OpenworldCrime.UI.Menu.NavigationScreen.SavedShape == OpenworldCrime.UI.Menu.NavigationScreen.Circle
            ? OpenworldCrime.UI.Menu.NavigationScreen.Square : OpenworldCrime.UI.Menu.NavigationScreen.Circle;
        PlayerPrefs.SetString(OpenworldCrime.UI.Menu.NavigationScreen.PrefKey, next);
        PlayerPrefs.Save();
        var map = FindFirstObjectByType<GameMapUI>();
        if (map != null) map.SetMinimapShape(next == OpenworldCrime.UI.Menu.NavigationScreen.Circle ? GameMapUI.MinimapShape.Circle : GameMapUI.MinimapShape.Square);
        RefreshSettings();
    }

    void ToggleControls()
    {
        var mode = FindFirstObjectByType<OpenworldCrime.Controls.ControlsModeManager>(FindObjectsInactive.Include);
        if (mode == null) return;
        mode.SetMobile(!mode.IsMobile);
        EnsureEventSystem();
        RefreshSettings();
    }

    Text Stepper(RectTransform page, string title, ref float y, UnityAction previous, UnityAction next)
    {
        var row = Round(page, title, new Color(0f, 0f, 0f, 0.35f), 18f);
        Place(row.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(350f, 84f));
        var label = Label(row.rectTransform, "Label", title, 24, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(80f, 0f), new Vector2(140f, 34f));
        var value = Label(row.rectTransform, "Value", "-", 22, FontStyle.Bold, TextAnchor.MiddleCenter, Pink);
        Place(value.rectTransform, new Vector2(1f, 0.5f), new Vector2(-95f, 0f), new Vector2(120f, 34f));
        ArrowButton(row.rectTransform, "<", new Vector2(-170f, 0f), previous);
        ArrowButton(row.rectTransform, ">", new Vector2(-22f, 0f), next);
        y -= 96f;
        return value;
    }

    void ArrowButton(RectTransform parent, string glyph, Vector2 at, UnityAction onClick)
    {
        var b = Round(parent, glyph == "<" ? "Previous" : "Next", new Color(1f, 1f, 1f, 0.12f), 20f);
        Place(b.rectTransform, new Vector2(1f, 0.5f), at, new Vector2(40f, 40f));
        var t = Label(b.rectTransform, "Glyph", glyph, 28, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Fill(t.rectTransform, 0f);
        var button = b.gameObject.AddComponent<Button>();
        button.onClick.AddListener(onClick);
        Pressable(button);
    }

    Slider SliderRow(RectTransform page, string title, ref float y, float value, UnityAction<float> onChange)
    {
        var row = Round(page, title, new Color(0f, 0f, 0f, 0.35f), 18f);
        Place(row.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(350f, 84f));
        var label = Label(row.rectTransform, "Label", title, 24, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(80f, 0f), new Vector2(140f, 34f));

        var root = Rect(row.rectTransform, "Slider", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-96f, 0f), new Vector2(170f, 30f));
        var back = Round(root, "Background", new Color(1f, 1f, 1f, 0.2f), 6f);
        Place(back.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(170f, 10f));
        var fillArea = Rect(root, "Fill Area", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        fillArea.offsetMin = new Vector2(0f, 10f);
        fillArea.offsetMax = new Vector2(0f, -10f);
        var fill = Round(fillArea, "Fill", Pink, 5f);
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2(0f, 1f);
        fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
        var handleArea = Rect(root, "Handle Area", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        handleArea.offsetMin = new Vector2(12f, 0f);
        handleArea.offsetMax = new Vector2(-12f, 0f);
        var handle = Img(handleArea, "Handle", PhoneArt.Disk, Color.white);
        handle.rectTransform.sizeDelta = new Vector2(28f, 28f);

        var slider = root.gameObject.AddComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.SetValueWithoutNotify(value);
        slider.onValueChanged.AddListener(onChange);
        y -= 96f;
        return slider;
    }

    // ---------- Incoming call badge ----------

    void BuildIncoming()
    {
        incoming = Rect(canvasRect, "Incoming Call", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -250f), new Vector2(400f, 120f));
        incoming.pivot = new Vector2(1f, 1f);
        var back = Round(incoming, "Back", new Color(0.05f, 0.05f, 0.08f, 0.9f), 60f);
        Fill(back.rectTransform, 0f);
        var rimLine = back.gameObject.AddComponent<Outline>();
        rimLine.effectColor = new Color(0.2f, 0.85f, 0.45f, 0.6f);
        rimLine.effectDistance = new Vector2(2f, -2f);

        var ring = Img(incoming, "Ring", PhoneArt.Ring, Green);
        incomingRing = ring.rectTransform;
        Place(incomingRing, new Vector2(0f, 0.5f), new Vector2(62f, 0f), new Vector2(100f, 100f));
        var face = Portrait(incoming, null, 88f);
        face.name = "Caller";
        Place(face, new Vector2(0f, 0.5f), new Vector2(62f, 0f), new Vector2(88f, 88f));

        var badge = Img(incoming, "Phone Badge", PhoneArt.Disk, Green);
        Place(badge.rectTransform, new Vector2(0f, 0.5f), new Vector2(96f, -32f), new Vector2(40f, 40f));
        var handset = Img(badge.rectTransform, "Handset", PhoneArt.Handset, Color.white);
        Fill(handset.rectTransform, 8f);

        var name = Label(incoming, "Name", "", 32, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        name.name = "Name";
        Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(240f, 26f), new Vector2(220f, 38f));
        var what = Label(incoming, "What", "Incoming call...", 20, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.75f));
        Place(what.rectTransform, new Vector2(0f, 0.5f), new Vector2(240f, -4f), new Vector2(220f, 28f));
        incomingHint = Label(incoming, "Hint", "", 20, FontStyle.Bold, TextAnchor.MiddleLeft, Green);
        Place(incomingHint.rectTransform, new Vector2(0f, 0.5f), new Vector2(240f, -34f), new Vector2(240f, 28f));

        var button = incoming.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        button.onClick.AddListener(() => { if (ringing) answer = true; });
        incoming.gameObject.SetActive(false);
    }

    void SetIncoming(Contact from)
    {
        var portrait = incoming.Find("Caller").GetChild(0).GetComponent<Image>();
        portrait.sprite = from.portrait != null ? from.portrait : PhoneArt.Person;
        portrait.preserveAspect = true;
        incoming.Find("Name").GetComponent<Text>().text = from.name;
        incomingHint.text = GameInput.MobileControls ? "TAP TO ANSWER" : "PRESS ENTER TO ANSWER";
    }

    void AnimateIncoming()
    {
        if (incoming == null || !incoming.gameObject.activeSelf) return;
        float t = Time.unscaledTime;
        float k = Mathf.Repeat(t * 1.2f, 1f);
        incomingRing.localScale = Vector3.one * Mathf.Lerp(0.9f, 1.3f, k);
        incomingRing.GetComponent<Image>().color = new Color(Green.r, Green.g, Green.b, 1f - k);
        // A buzz while the ring sounds (the first half of every 2.6 s ring cycle).
        bool buzzing = Mathf.Repeat(t, 2.6f) < 1.0f;
        float shake = buzzing ? Mathf.Sin(t * 70f) * 2.5f : 0f;
        incoming.localEulerAngles = new Vector3(0f, 0f, shake * 0.4f);
        incomingHint.color = new Color(Green.r, Green.g, Green.b, 0.6f + 0.4f * Mathf.Sin(t * 5f));
    }

    // ---------- Touch: the phone button ----------

    void BuildPhoneButton()
    {
        var b = Round(canvasRect, "Phone Button", new Color(0.05f, 0.05f, 0.08f, 0.85f), 46f);
        Place(b.rectTransform, new Vector2(1f, 1f), new Vector2(-80f, -330f), new Vector2(92f, 92f));
        b.rectTransform.anchorMin = b.rectTransform.anchorMax = new Vector2(1f, 1f);
        var icon = Img(b.rectTransform, "Handset", PhoneArt.Handset, Green);
        Fill(icon.rectTransform, 22f);
        var button = b.gameObject.AddComponent<Button>();
        button.onClick.AddListener(() => Open(true));
        Pressable(button);
        phoneButton = b.gameObject;
        phoneButton.SetActive(false);
    }

    // ---------- UI helpers ----------

    static RectTransform Rect(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    static void Fill(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    static Image Img(RectTransform parent, string name, Sprite sprite, Color colour)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = colour;
        image.raycastTarget = false;
        image.preserveAspect = sprite != null;
        return image;
    }

    // A rounded rectangle of the given corner radius (one sliced sprite, scaled per use).
    static Image Round(RectTransform parent, string name, Color colour, float radius)
    {
        var image = Img(parent, name, PhoneArt.Rounded, colour);
        image.type = Image.Type.Sliced;
        image.preserveAspect = false;
        image.pixelsPerUnitMultiplier = PhoneArt.RoundedRadius / Mathf.Max(1f, radius);
        image.raycastTarget = true;
        return image;
    }

    // A picture in a circle.
    static RectTransform Portrait(RectTransform parent, Sprite sprite, float size)
    {
        var frame = Img(parent, "Portrait", PhoneArt.Disk, new Color(0.2f, 0.2f, 0.24f, 1f));
        frame.rectTransform.sizeDelta = new Vector2(size, size);
        frame.preserveAspect = false;
        frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var picture = Img(frame.rectTransform, "Picture", sprite != null ? sprite : PhoneArt.Person, Color.white);
        Fill(picture.rectTransform, sprite != null ? 0f : size * 0.18f);
        picture.preserveAspect = true;
        return frame.rectTransform;
    }

    Text Label(RectTransform parent, string name, string text, int size, FontStyle style, TextAnchor align, Color colour)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        var label = rect.gameObject.AddComponent<Text>();
        label.font = font;
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.alignment = align;
        label.color = colour;
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        return label;
    }

    static void Shadowed(Text text)
    {
        var outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        outline.effectDistance = new Vector2(2f, -2f);
    }

    static void Pressable(Button button)
    {
        var colours = button.colors;
        colours.normalColor = Color.white;
        colours.highlightedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        colours.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colours.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);
        button.colors = colours;
    }

    // ---------- Sounds ----------

    // A two-tone telephone signal: the pattern alternates on and off (seconds), looped by the source.
    static AudioClip Tone(string name, float[] hz, float[] pattern)
    {
        const int rate = 22050;
        float total = 0f;
        foreach (float p in pattern) total += p;
        int samples = Mathf.CeilToInt(total * rate);
        var data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / rate;
            float local = t;
            int segment = 0;
            while (segment < pattern.Length && local > pattern[segment]) { local -= pattern[segment]; segment++; }
            if (segment % 2 == 1) continue;                    // off
            float edge = Mathf.Min(1f, Mathf.Min(local, pattern[Mathf.Min(segment, pattern.Length - 1)] - local) / 0.01f);
            float v = 0f;
            foreach (float f in hz) v += Mathf.Sin(2f * Mathf.PI * f * t);
            data[i] = v / hz.Length * 0.6f * edge;
        }
        var clip = AudioClip.Create(name, samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}

// The phone's drawn art: rounded rectangles, discs, rings, the wallpaper and the app glyphs (white, tinted where used).
static class PhoneArt
{
    public const float RoundedRadius = 48f;
    static Sprite rounded, disk, ring, wallpaper, person, bubble, gear, handset;

    public static Sprite Rounded => rounded != null ? rounded : (rounded = MakeRounded());
    public static Sprite Disk => disk != null ? disk : (disk = Glyph((u, v) => u * u + v * v < 0.98f));
    public static Sprite Ring => ring != null ? ring : (ring = Glyph((u, v) => { float r = Mathf.Sqrt(u * u + v * v); return r < 0.98f && r > 0.86f; }));
    public static Sprite Wallpaper => wallpaper != null ? wallpaper : (wallpaper = MakeWallpaper());

    public static Sprite Person => person != null ? person : (person = Glyph((u, v) =>
        u * u + (v - 0.34f) * (v - 0.34f) < 0.26f * 0.26f ||
        v < 0.0f && v > -0.8f && u * u / (0.62f * 0.62f) + (v + 0.8f) * (v + 0.8f) / (0.72f * 0.72f) < 1f));

    public static Sprite Bubble => bubble != null ? bubble : (bubble = Glyph((u, v) =>
    {
        float qx = Mathf.Abs(u) - 0.5f, qy = Mathf.Abs(v - 0.14f) - 0.32f;
        float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - 0.3f;
        return d < 0f || InTriangle(u, v, -0.5f, -0.2f, -0.05f, -0.3f, -0.62f, -0.78f);
    }));

    public static Sprite Gear => gear != null ? gear : (gear = Glyph((u, v) =>
    {
        float r = Mathf.Sqrt(u * u + v * v);
        float a = Mathf.Atan2(v, u);
        return r > 0.27f && (r < 0.64f || r < 0.86f && Mathf.Cos(8f * a) > 0.15f);
    }));

    // A telephone receiver lying diagonally: a thick curved handle with a pad at each end.
    public static Sprite Handset => handset != null ? handset : (handset = Glyph((u, v) =>
    {
        float cx = 0.32f, cy = 0.32f;
        float dx = u - cx, dy = v - cy;
        float r = Mathf.Sqrt(dx * dx + dy * dy);
        float a = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
        if (a < 0f) a += 360f;
        bool handle = r > 0.78f && r < 1.02f && a > 182f && a < 268f;
        bool pad1 = Pad(u, v, cx, cy, 176f), pad2 = Pad(u, v, cx, cy, 274f);
        return handle || pad1 || pad2;
    }));

    static bool Pad(float u, float v, float cx, float cy, float degrees)
    {
        float a = degrees * Mathf.Deg2Rad;
        float px = cx + Mathf.Cos(a) * 0.86f, py = cy + Mathf.Sin(a) * 0.86f;
        // An ellipse along the radius (the pad faces the middle).
        float rx = Mathf.Cos(a), ry = Mathf.Sin(a);
        float along = (u - px) * rx + (v - py) * ry, across = -(u - px) * ry + (v - py) * rx;
        return along * along / (0.2f * 0.2f) + across * across / (0.3f * 0.3f) < 1f;
    }

    static bool InTriangle(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
    {
        float d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
        float d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
        float d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    // White shape on transparent, 4x4 supersampled edges. u, v run -1..1 (v up).
    static Sprite Glyph(Func<float, float, bool> inside)
    {
        const int size = 128, ss = 4;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < ss; sy++)
                    for (int sx = 0; sx < ss; sx++)
                    {
                        float u = (x + (sx + 0.5f) / ss) / size * 2f - 1f;
                        float v = (y + (sy + 0.5f) / ss) / size * 2f - 1f;
                        if (inside(u, v)) hits++;
                    }
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(hits * 255 / (ss * ss)));
            }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    // A rounded square for 9-slicing: corner radius RoundedRadius pixels.
    static Sprite MakeRounded()
    {
        const int size = 128;
        float r = RoundedRadius;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float cx = Mathf.Clamp(px, r, size - r), cy = Mathf.Clamp(py, r, size - r);
                float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)) - r;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(0.5f - d) * 255f));
            }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(r, r, r, r));
    }

    // Night purple to deep blue, with a soft pink glow low down: the game's own colours.
    static Sprite MakeWallpaper()
    {
        const int w = 64, h = 128;
        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color32[w * h];
        Color top = new Color(0.16f, 0.07f, 0.3f), bottom = new Color(0.04f, 0.08f, 0.22f), glow = new Color(0.85f, 0.2f, 0.55f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = (float)y / (h - 1), u = (float)x / (w - 1);
                Color c = Color.Lerp(bottom, top, v);
                float g = Mathf.Exp(-(((u - 0.3f) * (u - 0.3f)) / 0.08f + ((v - 0.3f) * (v - 0.3f)) / 0.03f));
                c = Color.Lerp(c, glow, g * 0.45f);
                pixels[y * w + x] = c;
            }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }
}
