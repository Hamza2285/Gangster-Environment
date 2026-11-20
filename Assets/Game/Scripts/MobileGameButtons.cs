using System.Collections.Generic;
using OpenworldCrime.Controls;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityStandardAssets.CrossPlatformInput;

// The game's own on-screen buttons for phones, next to the combat and driving controls (Touch Controls, RCC):
// ENTER CAR / STEAL CAR / RIDE BIKE when standing at a vehicle, TAXI and SWITCH (character) on foot, MAP and START MISSION (in an ambulance, when a mission is on offer) in a vehicle,
// CLOSE while the map is open, and the taxi's GET IN / WATCH / SKIP / BACK / CAMERA when the taxi asks for them. Each button
// presses the same action as its key through CrossPlatformInputManager (see GameInput). Only the buttons that make sense right
// now are shown, and only in mobile mode. Styled like the Touch Controls buttons; starts by itself with the touch controls.
public class MobileGameButtons : MonoBehaviour
{
    class Button
    {
        public GameObject root;
        public System.Func<bool> visible;
    }

    readonly List<Button> buttons = new List<Button>();
    ControlsModeManager controls;
    GameMapUI map;
    TaxiService taxi;
    AmbulanceMission mission;
    Sprite knob;
    Font font;
    GameObject canvasRoot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<MobileGameButtons>() != null) return;
        if (FindFirstObjectByType<ControlsModeManager>(FindObjectsInactive.Include) == null) return;
        new GameObject("Mobile Game Buttons").AddComponent<MobileGameButtons>();
    }

    void Start()
    {
        controls = FindFirstObjectByType<ControlsModeManager>(FindObjectsInactive.Include);
        map = FindFirstObjectByType<GameMapUI>();
        taxi = FindFirstObjectByType<TaxiService>();
        mission = FindFirstObjectByType<AmbulanceMission>();
        FindStyle();
        Build();
    }

    // The round button art and font of the existing touch buttons, so these match them.
    void FindStyle()
    {
        foreach (var image in FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (image.name != "Jump" || image.sprite == null) continue;
            knob = image.sprite;
            var label = image.GetComponentInChildren<Text>(true);
            if (label != null) font = label.font;
            break;
        }
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    void Build()
    {
        canvasRoot = new GameObject("Mobile Game Buttons Canvas");
        canvasRoot.transform.SetParent(transform, false);
        var canvas = canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1100;   // above the full map, the character wheel, the taxi screens and the touch controls
        var scaler = canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRoot.AddComponent<GraphicRaycaster>();

        Color dark = new Color(0.2f, 0.2f, 0.2f, 0.65f);
        Color yellow = new Color(0.85f, 0.62f, 0.1f, 0.7f);
        Color pink = new Color(0.8f, 0.3f, 0.6f, 0.65f);
        Color green = new Color(0.15f, 0.5f, 0.2f, 0.7f);
        Color red = new Color(0.85f, 0.15f, 0.15f, 0.8f);
        Color blue = new Color(0.1f, 0.35f, 0.8f, 0.65f);

        // The minimap itself (top left): tap it to open the full map. Invisible; follows the minimap each frame.
        minimapHotspot = Add("", "M_Map", new Vector2(0f, 1f), new Vector2(175f, -245f), 280f, Color.clear, 10, () => Free() && !MapOpen());
        minimapHotspot.graphic = null;   // no press flash
        // Standing at a vehicle: get in (replaces Touch Controls' always-on CAR button, same input).
        enterButton = Add("ENTER\nCAR", "M_Vehicle", new Vector2(1f, 0f), new Vector2(-130f, 550f), 135f, new Color(0.15f, 0.5f, 0.2f, 0.75f), 22,
            () => Free() && !MapOpen() && !GamePlayer.InVehicle && FocusedVehicle() != null);
        // Whatever the player can interact with right now (buy food at a beach cart, enter a shop): its own label, the interact input.
        actionButton = Add("ACTION", "M_Interact", new Vector2(0.5f, 0f), new Vector2(0f, 330f), 160f, new Color(0.85f, 0.45f, 0.1f, 0.8f), 22,
            () => Free() && !MapOpen() && !GamePlayer.InVehicle && FocusPrompt() != null);
        // On foot, beside the minimap (top left; tapping the minimap opens the full map).
        besideMinimap.Add(Add("TAXI", "M_Taxi", new Vector2(0f, 1f), new Vector2(395f, -170f), 100f, yellow, 20, () => Free() && !MapOpen() && !GamePlayer.InVehicle && TaxiIdle()));
        besideMinimap.Add(Add("HORN", "M_Horn", new Vector2(0f, 1f), new Vector2(395f, -170f), 100f, yellow, 20,
            () => Free() && !MapOpen() && GamePlayer.InVehicle));
        // Hold SWITCH: the character wheel opens; drag onto a character and let go to switch.
        switchButton = Add("SWITCH", "M_Switch", new Vector2(0f, 1f), new Vector2(395f, -285f), 100f, pink, 18,
            () => (Free() || WheelOpen()) && !MapOpen() && !GamePlayer.InVehicle && CanSwitch());
        switchButton.reportsPointer = true;
        besideMinimap.Add(switchButton);

        // Sniper scope: while aiming a scoped gun (next to AIM).
        scopeButton = Add("SCOPE", "M_Scope", new Vector2(1f, 0f), new Vector2(-545f, 300f), 115f, blue, 22, () => !GamePlayer.InVehicle && !MapOpen() && CanScope());

        // The ambulance: start a paramedic mission (beside the vehicle EXIT button).
        Add("START\nMISSION", "M_Mission", new Vector2(1f, 1f), new Vector2(-335f, -420f), 170f, red, 24, () => GamePlayer.InVehicle && !MapOpen() && mission != null && mission.OfferingStart);

        // The full map: close it.
        Add("EXIT\nMAP", "M_Cancel", new Vector2(1f, 1f), new Vector2(-110f, -110f), 110f, dark, 20, () => MapOpen() && TaxiStateIs(TaxiService.State.Idle, TaxiService.State.TaxiWaiting, TaxiService.State.TaxiComing, TaxiService.State.ChoosingDestination));

        // The taxi.
        Add("GET IN", "M_Enter", new Vector2(0.5f, 0f), new Vector2(0f, 200f), 150f, green, 26, () => TaxiStateIs(TaxiService.State.TaxiWaiting) && !MapOpen() && !GamePlayer.InVehicle);
        Add("WATCH", "M_Watch", new Vector2(0.5f, 0f), new Vector2(-190f, 200f), 140f, blue, 22, () => TaxiStateIs(TaxiService.State.ChoosingTrip));
        Add("SKIP", "M_Skip", new Vector2(0.5f, 0f), new Vector2(0f, 200f), 140f, green, 22, () => TaxiStateIs(TaxiService.State.ChoosingTrip));
        Add("BACK", "M_Cancel", new Vector2(0.5f, 0f), new Vector2(190f, 200f), 140f, dark, 22, () => TaxiStateIs(TaxiService.State.ChoosingTrip));
        Add("SKIP\nRIDE", "M_Enter", new Vector2(0.5f, 0f), new Vector2(-110f, 200f), 140f, green, 22, () => TaxiStateIs(TaxiService.State.Watching));
        Add("CAMERA", "M_Camera", new Vector2(0.5f, 0f), new Vector2(110f, 200f), 140f, dark, 20, () => TaxiStateIs(TaxiService.State.Watching));
    }

    TapButton switchButton, scopeButton, minimapHotspot, enterButton, actionButton;
    // TAXI / HORN / SWITCH sit just right of the minimap, whatever its shape and width.
    readonly List<TapButton> besideMinimap = new List<TapButton>();
    Text actionLabel;
    OpenworldCrime.Player.PlayerInteractionController interactionFocus;
    Text enterLabel;
    GameObject touchCarButton;
    GameObject rccControls;

    TapButton Add(string label, string action, Vector2 anchor, Vector2 position, float size, Color colour, int fontSize, System.Func<bool> visible)
    {
        var go = new GameObject(label.Replace("\n", " "), typeof(RectTransform));
        go.transform.SetParent(canvasRoot.transform, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(size, size);
        var image = go.AddComponent<Image>();
        image.sprite = knob;
        image.color = colour;
        var tap = go.AddComponent<TapButton>();
        tap.action = action;
        tap.graphic = image;

        var text = new GameObject("Label", typeof(RectTransform));
        text.transform.SetParent(go.transform, false);
        var textRect = (RectTransform)text.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        var t = text.AddComponent<Text>();
        t.font = font;
        t.text = label;
        t.fontSize = fontSize;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.raycastTarget = false;
        go.SetActive(false);
        buttons.Add(new Button { root = go, visible = visible });
        return tap;
    }

    void Update()
    {
        bool mobile = controls != null && controls.IsMobile;
        if (canvasRoot.activeSelf != mobile) canvasRoot.SetActive(mobile);
        if (!mobile) return;
        if (GamePlayer.InVehicle) LayOutCarDashboard();
        if (scopeButton != null) scopeButton.action = ScopeAction();
        FollowMinimap();
        UpdateEnterButton();
        UpdateActionButton();
        // The finger on SWITCH steers the character wheel.
        CharacterWheelUI.TouchPoint = switchButton != null && switchButton.Held ? switchButton.Pointer : (Vector2?)null;
        foreach (var b in buttons)
        {
            bool show = SafeVisible(b.visible) || b.root.GetComponent<TapButton>().Held;
            if (b.root.activeSelf != show)
            {
                if (!show) b.root.GetComponent<TapButton>().Release();
                b.root.SetActive(show);
            }
        }
    }

    // The vehicle the current character could get into right now (Ghost's PlayerVehicleInteraction picks it), or null.
    static OpenworldCrime.Vehicles.IVehicleInteractionTarget FocusedVehicle()
    {
        var player = GamePlayer.Current;
        if (player == null) return null;
        var interaction = player.transform.GetComponent<OpenworldCrime.Player.PlayerVehicleInteraction>();
        if (interaction == null || interaction.State != OpenworldCrime.Player.VehicleInteractionState.OnFoot) return null;
        var target = interaction.FocusedVehicle;
        return target != null && (target as Object) != null && target.CanEnter ? target : null;
    }

    static bool IsBike(Transform vehicle)
    {
        if (vehicle.GetComponentInParent<OpenworldCrime.Vehicles.TrafficBikeAdapter>() != null) return true;
        if (vehicle.GetComponentInParent<OpenworldCrime.Vehicles.BikeMountAnimations>() != null) return true;
        var controller = vehicle.GetComponentInParent<OpenworldCrime.Vehicles.IVehicleController>();
        return controller != null && controller.UsesQuickMount;
    }

    void UpdateEnterButton()
    {
        // Touch Controls' own CAR button is always on; the contextual one here takes its place.
        if (touchCarButton == null)
            foreach (var b in FindObjectsByType<VirtualButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b.name == "Vehicle") touchCarButton = b.gameObject;
        if (touchCarButton != null && touchCarButton.activeSelf) touchCarButton.SetActive(false);

        if (enterButton == null) return;
        if (enterLabel == null) enterLabel = enterButton.GetComponentInChildren<Text>(true);
        var target = FocusedVehicle();
        if (target == null || enterLabel == null) return;
        bool bike = IsBike(target.transform);
        bool steal = target.EntryPrompt == OpenworldCrime.Vehicles.VehicleEntryPrompt.Steal;
        string label = bike ? (steal ? "STEAL\nBIKE" : "RIDE\nBIKE") : (steal ? "STEAL\nCAR" : "ENTER\nCAR");
        if (enterLabel.text != label) enterLabel.text = label;
    }

    // The prompt of the interactable in focus ("Buy food ($20)"), or null.
    string FocusPrompt()
    {
        var player = GamePlayer.Current;
        if (player == null) return null;
        if (interactionFocus == null || interactionFocus.transform != player.transform)
            interactionFocus = player.transform.GetComponent<OpenworldCrime.Player.PlayerInteractionController>();
        var focus = interactionFocus != null && interactionFocus.isActiveAndEnabled ? interactionFocus.CurrentFocus : null;
        return focus != null && (focus as Object) != null ? focus.InteractionPrompt : null;
    }

    void UpdateActionButton()
    {
        if (actionButton == null) return;
        if (actionLabel == null) actionLabel = actionButton.GetComponentInChildren<Text>(true);
        string prompt = FocusPrompt();
        if (prompt == null || actionLabel == null) return;
        // "Buy food ($20)" -> "BUY FOOD" over "$20"
        string label = prompt.Replace(" ($", System.Environment.NewLine.Substring(System.Environment.NewLine.Length - 1) + "$").Replace(")", "").ToUpperInvariant();
        if (actionLabel.text != label) actionLabel.text = label;
    }

    void FollowMinimap()
    {
        var mini = map != null ? map.MinimapRect : null;
        if (minimapHotspot == null || mini == null) return;
        var rect = (RectTransform)minimapHotspot.transform;
        rect.position = mini.position;   // both are screen-space overlay canvases
        var mine = rect.GetComponentInParent<Canvas>();
        var theirs = mini.GetComponentInParent<Canvas>();
        if (mine != null && theirs != null && mine.scaleFactor > 0f)
            rect.sizeDelta = mini.rect.size * theirs.scaleFactor / mine.scaleFactor;
        if (mine == null || mine.scaleFactor <= 0f) return;
        float rightEdge = mini.TransformPoint(new Vector3(mini.rect.xMax, 0f, 0f)).x;   // screen pixels
        foreach (var button in besideMinimap)
        {
            if (button == null) continue;
            var b = (RectTransform)button.transform;
            Vector3 p = b.position;
            p.x = rightEdge + (b.rect.width * 0.5f + 12f) * mine.scaleFactor;
            b.position = p;
        }
    }

    static bool SafeVisible(System.Func<bool> visible)
    {
        try { return visible(); }
        catch { return false; }
    }

    // Nothing else is on screen that takes over (a shop, a menu, a character switch, being dead, a taxi ride).
    bool Free()
    {
        if (GamePlayer.MenuOpen && !MapOpen()) return false;
        var wanted = OpenworldCrime.Police.WantedManager.Instance;
        if (wanted != null && wanted.IsBustInProgress) return false;   // being arrested
        var switcher = CharacterSwitcher.Instance;
        if (switcher != null && switcher.Busy) return false;
        if (taxi != null && taxi.CurrentState != TaxiService.State.Idle && taxi.CurrentState != TaxiService.State.TaxiWaiting && taxi.CurrentState != TaxiService.State.TaxiComing) return false;
        var player = GamePlayer.Current;
        return player != null && player.ControlEnabled || GamePlayer.InVehicle;
    }

    bool MapOpen() => map != null && map.IsMapOpen;

    static bool WheelOpen()
    {
        var switcher = CharacterSwitcher.Instance;
        return switcher != null && GameInput.SwitchHeld;
    }

    // The current character's Invector shooter (Jason or Mia).
    static Invector.vCharacterController.vShooterMeleeInput Shooter()
    {
        var player = GamePlayer.Current;
        return player != null ? player.transform.GetComponent<Invector.vCharacterController.vShooterMeleeInput>() : null;
    }

    // The scope button presses the shooter's own scope input (its mobile button name).
    static string ScopeAction()
    {
        var shooter = Shooter();
        if (shooter == null || shooter.scopeViewInput == null) return "M_Scope";
        // Invector ships the scope input without a mobile name; give it one so the button can press it.
        if (string.IsNullOrEmpty(shooter.scopeViewInput.mobile)) shooter.scopeViewInput.mobile = "M_Scope";
        return shooter.scopeViewInput.mobile;
    }

    static bool CanScope()
    {
        var shooter = Shooter();
        if (shooter == null) return false;
        var manager = shooter.GetComponent<Invector.vShooter.vShooterManager>();
        var weapon = manager != null ? manager.CurrentWeapon : null;
        return weapon != null && weapon.scopeTarget != null && shooter.IsAiming;
    }

    // In a car on a phone: RCC's speed and rev gauges go to the bottom middle (smaller), out of the minimap's and the HUD's
    // way, and RCC's demo buttons (options, customiser, record / replay) are hidden.
    // RCC switches its demo buttons back on whenever a car is entered, so this runs every frame while driving.
    void LayOutCarDashboard()
    {
        if (rccControls == null)
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == "RCC Mobile Controls") rccControls = root;
        var rcc = rccControls;
        if (rcc == null) return;
        Place(rcc.transform.Find("Dashboard/KMH"), new Vector2(-215f, 150f));
        Place(rcc.transform.Find("Dashboard/RPM"), new Vector2(215f, 150f));
        foreach (var path in new[] { "TEST BUTTONS", "Controller Buttons/Record / Play" })
        {
            var t = rcc.transform.Find(path);
            if (t == null) continue;
            // RCC's dashboard script turns its top buttons back on every frame, so hide them instead of disabling them.
            var group = t.GetComponent<CanvasGroup>();
            if (group == null) group = t.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = group.blocksRaycasts = false;
        }
    }

    static void Place(Transform t, Vector2 position)
    {
        var rect = t as RectTransform;
        if (rect == null || rect.anchorMin == new Vector2(0.5f, 0f)) return;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.anchoredPosition = position;
        rect.localScale = Vector3.one * 0.7f;
    }

    bool TaxiIdle() => taxi == null || taxi.CurrentState == TaxiService.State.Idle;

    bool TaxiStateIs(params TaxiService.State[] states)
    {
        if (taxi == null) return false;
        foreach (var s in states) if (taxi.CurrentState == s) return true;
        return false;
    }

    static bool CanSwitch()
    {
        var switcher = CharacterSwitcher.Instance;
        return switcher != null && !switcher.Busy && switcher.characters != null && switcher.characters.Length > 1;
    }

    // Presses its action while the finger is down (a tap is a press and a release), with a little feedback.
    public class TapButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public string action;
        public Graphic graphic;
        public bool reportsPointer;
        bool held;
        Color normal;
        public bool Held => held;
        public Vector2 Pointer { get; private set; }

        public void OnDrag(PointerEventData eventData) => Pointer = eventData.position;

        void Awake()
        {
            if (graphic != null) normal = graphic.color;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (held) return;
            held = true;
            Pointer = eventData.position;
            CrossPlatformInputManager.SetButtonDown(action);
            if (graphic != null) { normal = graphic.color; graphic.color = new Color(normal.r, normal.g, normal.b, Mathf.Min(1f, normal.a + 0.25f)); }
            transform.localScale = Vector3.one * 0.94f;
        }

        public void OnPointerUp(PointerEventData eventData) => Release();

        public void Release()
        {
            if (!held) return;
            held = false;
            CrossPlatformInputManager.SetButtonUp(action);
            if (graphic != null) graphic.color = normal;
            transform.localScale = Vector3.one;
        }

        void OnDisable() => Release();
    }
}
