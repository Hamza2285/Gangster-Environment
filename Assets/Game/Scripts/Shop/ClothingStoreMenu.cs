using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The clothing store's fitting room: pick a category, browse what is on sale, try things on before buying and keep what
// you like. Selecting an item puts it on straight away; leaving without buying puts back what you came in wearing.
// Lives in the store scene, so clothes can only be changed here.
public class ClothingStoreMenu : MonoBehaviour
{
    public static ClothingStoreMenu Instance { get; private set; }
    public static bool IsOpen { get; private set; }

    public ClothingCatalog catalog;
    [Header("Look")]
    public Color accent = new Color(1f, 0.78f, 0.24f, 1f);
    public Color highlight = new Color(0.88f, 0.25f, 0.98f, 1f);
    public Color panel = new Color(0.035f, 0.035f, 0.05f, 0.9f);
    [Tooltip("Background of the side panel (Assets/Cloth-Panel.png, made for its 600 x 900 size). Empty: plain dark blocks.")]
    public Sprite panelSprite;
    [Tooltip("Titles, tabs, prices and buttons (Anton). Empty: Unity's built-in font.")]
    public Font headingFont;
    [Tooltip("Everything else (Bebas Kai). Empty: Unity's built-in font.")]
    public Font textFont;
    [Header("Camera")]
    public float turnSpeed = 90f;
    public float dragTurn = 0.35f;
    public float cameraSmoothing = 6f;

    static readonly Color Muted = new Color(1f, 1f, 1f, 0.55f);
    static readonly Color Good = new Color(0.36f, 0.9f, 0.45f, 1f);
    static readonly Color Bad = new Color(1f, 0.36f, 0.33f, 1f);
    const float RowHeight = 92f, PanelWidth = 600f;

    PlayerWardrobe wardrobe;
    IGamePlayer player;
    Camera fittingCamera, mainCamera;
    readonly List<Canvas> hiddenCanvases = new List<Canvas>();
    readonly List<Renderer> hiddenWeapon = new List<Renderer>();
    GameObject root;
    Font font, heading;
    // On the panel picture the blocks are only a light shade over it; without it they are the old solid dark blocks.
    Color Block(float darkAlpha) => panelSprite != null ? new Color(0.06f, 0.02f, 0.08f, darkAlpha * 0.35f) : new Color(0.05f, 0.04f, 0.08f, darkAlpha);
    Color ListBack => panelSprite != null ? Color.clear : panel;

    int categoryIndex, itemIndex;
    readonly List<Button> tabs = new List<Button>();
    readonly List<Row> rows = new List<Row>();
    RectTransform listContent, tabStrip;
    ScrollRect scroll;
    Text cashText, categoryTitle, countText, itemTitle, itemDetail, actionText, toastText, hintText;
    Image actionButton;
    float toastUntil;

    float orbitYaw, zoom = 1f, bodyScale = 1f;
    bool dragging;
    Vector2 lastPointer;
    Vector3 cameraVelocity;

    sealed class Row
    {
        public RectTransform rect;
        public Image background, stripe, icon;
        public Text name, price;
    }

    void Awake()
    {
        Instance = this;
        var builtIn = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        font = textFont != null ? textFont : builtIn;
        heading = headingFont != null ? headingFont : font;
    }

    void OnDestroy()
    {
        if (IsOpen) CloseNow();
        if (Instance == this) Instance = null;
    }

    public void Open()
    {
        if (!IsOpen) StartCoroutine(OpenRoutine());
    }

    IEnumerator OpenRoutine()
    {
        player = GamePlayer.Current;
        var interior = ClothingStoreInterior.Instance;
        // Whoever walked in shops for their own clothes; the screen is rebuilt when that is someone else.
        var travel = ClothingStoreTravel.Instance;
        var clothes = travel != null ? travel.CurrentCatalog : null;
        if (clothes != null && clothes != catalog)
        {
            catalog = clothes;
            if (root != null) { Destroy(root); root = null; }
            tabs.Clear();
            rows.Clear();
        }
        if (player == null || interior == null || catalog == null) yield break;
        bodyScale = BodyScale(player.transform);
        wardrobe = PlayerWardrobe.Attach(player.transform.gameObject, catalog);
        IsOpen = true;
        player.SetControl(false);

        var blink = ScreenBlink.Instance;
        if (blink != null) yield return blink.Close(0.22f);

        // Stand in front of the mirror, facing the fitting camera.
        Vector3 toCamera = interior.fittingCamera.position - interior.fittingSpot.position;
        toCamera.y = 0f;
        player.Teleport(interior.fittingSpot.position, Quaternion.LookRotation(toCamera).eulerAngles.y);
        player.LockCursor(false);

        mainCamera = Camera.main;
        if (fittingCamera == null) fittingCamera = MakeCamera(interior);
        orbitYaw = 0f;
        zoom = 1f;
        fittingCamera.transform.SetPositionAndRotation(interior.fittingCamera.position, interior.fittingCamera.rotation);
        fittingCamera.gameObject.SetActive(true);
        if (mainCamera != null && mainCamera != fittingCamera) mainCamera.enabled = false;

        HideOtherScreens();
        HideWeapon(player.transform);
        if (root == null) Build();
        root.SetActive(true);
        EnsureEventSystem();
        categoryIndex = 0;
        ShowCategory(0, true);
        UpdateCash();

        if (blink != null) yield return blink.Open(0.3f);
    }

    public void Close()
    {
        if (IsOpen) StartCoroutine(CloseRoutine());
    }

    IEnumerator CloseRoutine()
    {
        var blink = ScreenBlink.Instance;
        if (blink != null) yield return blink.Close(0.2f);
        CloseNow();
        if (blink != null) yield return blink.Open(0.3f);
    }

    void CloseNow()
    {
        IsOpen = false;
        if (wardrobe != null) wardrobe.CancelPreview();
        if (root != null) root.SetActive(false);
        if (fittingCamera != null) fittingCamera.gameObject.SetActive(false);
        if (mainCamera != null) mainCamera.enabled = true;
        foreach (var canvas in hiddenCanvases) if (canvas != null) canvas.enabled = true;
        hiddenCanvases.Clear();
        foreach (var renderer in hiddenWeapon) if (renderer != null) renderer.enabled = true;
        hiddenWeapon.Clear();
        if (player != null)
        {
            player.SetControl(true);
            player.LockCursor(true);
        }
    }

    void HideOtherScreens()
    {
        hiddenCanvases.Clear();
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!canvas.isRootCanvas || !canvas.enabled || canvas.renderMode == RenderMode.WorldSpace) continue;
            if (root != null && canvas.transform.IsChildOf(root.transform)) continue;
            if (ScreenBlink.Instance != null && canvas.transform.IsChildOf(ScreenBlink.Instance.transform)) continue;
            canvas.enabled = false;
            hiddenCanvases.Add(canvas);
        }
    }

    // The gun in the player's hand would spoil every outfit, so it is out of sight while trying clothes on.
    void HideWeapon(Transform playerRoot)
    {
        hiddenWeapon.Clear();
        var weapons = playerRoot.GetComponentInChildren<OpenworldCrime.Player.PlayerWeaponController>();
        var weapon = weapons != null ? weapons.CurrentWeapon : null;
        if (weapon == null) return;
        foreach (var renderer in weapon.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled) continue;
            renderer.enabled = false;
            hiddenWeapon.Add(renderer);
        }
    }

    Camera MakeCamera(ClothingStoreInterior interior)
    {
        var go = new GameObject("Fitting Camera");
        go.transform.SetParent(interior.transform, false);
        var cam = go.AddComponent<Camera>();
        if (mainCamera != null)
        {
            cam.CopyFrom(mainCamera);
            var from = mainCamera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            var to = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (to == null) to = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (from != null) to.renderPostProcessing = from.renderPostProcessing;
        }
        cam.fieldOfView = 40f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 80f;
        cam.depth = 50f;
        return cam;
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        go.AddComponent<StandaloneInputModule>();
#endif
    }

    void Update()
    {
        if (!IsOpen || root == null || !root.activeSelf) return;

        if (GameInput.MenuBackDown) { Close(); return; }
        if (GameInput.MenuRightDown) ShowCategory((categoryIndex + 1) % catalog.categories.Length, false);
        else if (GameInput.MenuLeftDown) ShowCategory((categoryIndex + catalog.categories.Length - 1) % catalog.categories.Length, false);
        else if (GameInput.MenuDownDown) SelectItem(Mathf.Min(itemIndex + 1, CurrentCategory.items.Length - 1));
        else if (GameInput.MenuUpDown) SelectItem(Mathf.Max(itemIndex - 1, 0));
        else if (GameInput.EnterDown) BuyOrWear();
        if (GameInput.ResetViewDown) { orbitYaw = 0f; zoom = 1f; }

        orbitYaw += GameInput.TurnKeys * turnSpeed * Time.unscaledDeltaTime;
        HandleDragAndZoom();
        MoveCamera();
        toastText.enabled = Time.unscaledTime < toastUntil;
    }

    void HandleDragAndZoom()
    {
        Vector2 pointer = GameInput.PointerPosition;
        bool overPanel = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (GameInput.PrimaryClickDown && !overPanel)
        {
            dragging = true;
            lastPointer = pointer;
        }
        if (!GameInput.PrimaryHeld) dragging = false;
        if (dragging)
        {
            orbitYaw += (pointer.x - lastPointer.x) * dragTurn;
            lastPointer = pointer;
        }
        if (!overPanel) zoom = Mathf.Clamp(zoom - GameInput.ScrollStep * 0.12f, 0.55f, 1.35f);
    }

    // The camera frames the part of the body the category is about, and circles round the player on request.
    void MoveCamera()
    {
        var interior = ClothingStoreInterior.Instance;
        if (interior == null || fittingCamera == null) return;
        Vector3 feet = interior.fittingSpot.position;
        Vector3 toCamera = interior.fittingCamera.position - feet;
        toCamera.y = 0f;
        Vector3 direction = Quaternion.Euler(0f, orbitYaw, 0f) * toCamera.normalized;

        float height, distance;
        switch (CurrentCategory.focus)
        {
            case ClothingCatalog.Focus.Head: height = 1.62f; distance = 1.05f; break;
            case ClothingCatalog.Focus.Neck: height = 1.42f; distance = 1.25f; break;
            case ClothingCatalog.Focus.Torso: height = 1.2f; distance = 2.0f; break;
            case ClothingCatalog.Focus.Legs: height = 0.62f; distance = 2.0f; break;
            case ClothingCatalog.Focus.Feet: height = 0.2f; distance = 1.25f; break;
            default: height = 0.95f; distance = 3.1f; break;
        }
        // Framed for Jason's height; shorter or taller characters scale with it.
        height *= bodyScale;
        distance *= Mathf.Lerp(1f, bodyScale, 0.5f);
        // The menu covers the left of the screen, so the player is framed a little to the right.
        Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
        Vector3 target = feet + Vector3.up * height + side * (distance * 0.22f);
        Vector3 wanted = target + direction * distance * zoom + Vector3.up * 0.08f;

        var cam = fittingCamera.transform;
        cam.position = Vector3.SmoothDamp(cam.position, wanted, ref cameraVelocity, 1f / cameraSmoothing, Mathf.Infinity, Time.unscaledDeltaTime);
        var look = Quaternion.LookRotation(target - cam.position);
        cam.rotation = Quaternion.Slerp(cam.rotation, look, 1f - Mathf.Exp(-cameraSmoothing * 1.5f * Time.unscaledDeltaTime));
    }

    // Head height against Jason's (about 1.6 m to the head bone).
    static float BodyScale(Transform character)
    {
        var animator = character.GetComponent<Animator>();
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) return 1f;
        return Mathf.Clamp((head.position.y - character.position.y) / 1.59f, 0.8f, 1.2f);
    }

    ClothingCatalog.Category CurrentCategory => catalog.categories[categoryIndex];
    ClothingCatalog.Item CurrentItem => CurrentCategory.items.Length > 0 ? CurrentCategory.items[itemIndex] : null;

    void ShowCategory(int index, bool first)
    {
        // Changing category drops whatever was being tried on in the last one.
        if (!first) wardrobe.CancelPreview();
        categoryIndex = index;
        var category = CurrentCategory;
        for (int i = 0; i < tabs.Count; i++)
        {
            bool on = i == index;
            tabs[i].GetComponent<Image>().color = on ? accent : new Color(1f, 1f, 1f, 0.06f);
            tabs[i].GetComponentInChildren<Text>().color = on ? Color.black : Muted;
        }
        categoryTitle.text = category.displayName.ToUpperInvariant();
        BuildRows(category);
        int worn = 0;
        for (int i = 0; i < category.items.Length; i++) if (category.items[i].id == wardrobe.EquippedId(category.id)) worn = i;
        SelectItem(worn);
    }

    void SelectItem(int index)
    {
        if (CurrentCategory.items.Length == 0) return;
        itemIndex = index;
        var item = CurrentItem;
        wardrobe.Preview(CurrentCategory, item);
        RefreshRows();
        countText.text = (index + 1) + " / " + CurrentCategory.items.Length;
        itemTitle.text = item.displayName.ToUpperInvariant();
        itemDetail.text = item.description;
        ScrollTo(index);
    }

    void BuyOrWear()
    {
        var item = CurrentItem;
        if (item == null) return;
        bool owned = wardrobe.Owns(item);
        switch (wardrobe.BuyOrWear(CurrentCategory, item))
        {
            case PlayerWardrobe.Result.Bought: Toast("Purchased  " + item.displayName + "  -$" + item.price.ToString("N0"), Good); break;
            case PlayerWardrobe.Result.Worn: Toast("Now wearing  " + item.displayName, Good); break;
            case PlayerWardrobe.Result.AlreadyWearing: Toast("Already wearing this", Muted); break;
            case PlayerWardrobe.Result.NotEnoughCash: Toast("Not enough cash for  " + item.displayName, Bad); break;
        }
        if (!owned && wardrobe.Owns(item)) UpdateCash();
        RefreshRows();
    }

    void Toast(string text, Color color)
    {
        toastText.text = text;
        toastText.color = color;
        toastUntil = Time.unscaledTime + 2.2f;
    }

    void UpdateCash()
    {
        cashText.text = "$" + wardrobe.Cash.ToString("N0");
    }

    void RefreshRows()
    {
        var category = CurrentCategory;
        string worn = wardrobe.EquippedId(category.id);
        for (int i = 0; i < rows.Count; i++)
        {
            var item = category.items[i];
            var row = rows[i];
            bool selected = i == itemIndex, wearing = item.id == worn, owned = wardrobe.Owns(item);
            row.background.color = selected ? new Color(1f, 1f, 1f, 0.16f) : new Color(1f, 1f, 1f, i % 2 == 0 ? 0.035f : 0.06f);
            row.stripe.enabled = selected;
            row.name.color = selected ? Color.white : new Color(1f, 1f, 1f, 0.82f);
            if (wearing) { row.price.text = "WEARING"; row.price.color = Good; }
            else if (owned) { row.price.text = "OWNED"; row.price.color = Muted; }
            else { row.price.text = "$" + item.price.ToString("N0"); row.price.color = wardrobe.Cash >= item.price ? accent : Bad; }
        }

        var current = CurrentItem;
        if (current == null) return;
        bool currentWorn = current.id == worn, currentOwned = wardrobe.Owns(current);
        if (currentWorn) { actionText.text = "WEARING"; actionButton.color = new Color(1f, 1f, 1f, 0.12f); actionText.color = Good; }
        else if (currentOwned) { actionText.text = "WEAR"; actionButton.color = accent; actionText.color = Color.black; }
        else
        {
            bool afford = wardrobe.Cash >= current.price;
            actionText.text = "BUY   $" + current.price.ToString("N0");
            actionButton.color = afford ? accent : new Color(Bad.r, Bad.g, Bad.b, 0.35f);
            actionText.color = afford ? Color.black : Color.white;
        }
    }

    void ScrollTo(int index)
    {
        if (scroll == null) return;
        float viewport = scroll.viewport.rect.height;
        float top = index * RowHeight, bottom = top + RowHeight;
        float y = listContent.anchoredPosition.y;
        if (top < y) y = top;
        else if (bottom > y + viewport) y = bottom - viewport;
        listContent.anchoredPosition = new Vector2(0f, y);
    }

    // ---------- Building the screen ----------

    void Build()
    {
        root = new GameObject("Clothing Store Menu");
        root.transform.SetParent(transform, false);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();

        // A soft shade behind the panel so text reads against the bright store.
        var shade = Rect("Shade", root.transform, new Vector2(0f, 0f), new Vector2(0f, 1f));
        shade.pivot = new Vector2(0f, 0.5f);
        shade.sizeDelta = new Vector2(PanelWidth + 260f, 0f);
        var shadeImage = shade.gameObject.AddComponent<Image>();
        shadeImage.sprite = Gradient();
        shadeImage.color = new Color(0f, 0f, 0f, 0.55f);
        shadeImage.raycastTarget = false;

        var side = Rect("Panel", root.transform, new Vector2(0f, 0f), new Vector2(0f, 1f));
        side.pivot = new Vector2(0f, 0.5f);
        side.anchoredPosition = new Vector2(56f, 0f);
        side.offsetMin = new Vector2(56f, 120f);
        side.offsetMax = new Vector2(56f + PanelWidth, -56f);
        if (panelSprite != null)
        {
            var back = side.gameObject.AddComponent<Image>();
            back.sprite = panelSprite;
            back.type = panelSprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            back.raycastTarget = false;
        }

        BuildHeader(side);
        BuildTabs(side);
        BuildList(side);
        BuildFooter(side);
        BuildCash();
        BuildHints();

        toastText = Label("Toast", root.transform, 30, TextAnchor.MiddleCenter, Good);
        var toast = toastText.rectTransform;
        toast.anchorMin = toast.anchorMax = new Vector2(0.62f, 1f);
        toast.anchoredPosition = new Vector2(0f, -150f);
        toast.sizeDelta = new Vector2(1000f, 50f);
        toastText.fontStyle = FontStyle.Bold;
        var outline = toastText.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        outline.effectDistance = new Vector2(2f, -2f);
        toastText.enabled = false;
        UseFonts(root.transform);
    }

    // The heading font for titles, tabs, prices and buttons, the text font for the rest. The fonts are already heavy, so no
    // faked bold on them.
    void UseFonts(Transform under)
    {
        foreach (var text in under.GetComponentsInChildren<Text>(true))
        {
            bool isHeading = text.fontStyle == FontStyle.Bold;
            text.font = isHeading ? heading : font;
            if (text.font != Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")) text.fontStyle = FontStyle.Normal;
        }
    }

    void BuildHeader(RectTransform side)
    {
        var header = Rect("Header", side, new Vector2(0f, 1f), new Vector2(1f, 1f));
        header.pivot = new Vector2(0.5f, 1f);
        header.sizeDelta = new Vector2(0f, 150f);
        header.gameObject.AddComponent<Image>().color = Block(0.96f);

        var bar = Rect("Accent", header, new Vector2(0f, 0f), new Vector2(1f, 0f));
        bar.pivot = new Vector2(0.5f, 0f);
        bar.sizeDelta = new Vector2(0f, 6f);
        var barImage = bar.gameObject.AddComponent<Image>();
        barImage.color = highlight;

        var title = Label("Title", header, 46, TextAnchor.UpperLeft, Color.white);
        title.text = catalog.storeName.ToUpperInvariant();
        title.fontStyle = FontStyle.Bold;
        Place(title.rectTransform, 30f, -26f, PanelWidth - 60f, 60f);
        var tagline = Label("Tagline", header, 22, TextAnchor.UpperLeft, accent);
        tagline.text = "WEAR A BETTER YOU";
        Place(tagline.rectTransform, 32f, -88f, PanelWidth - 60f, 30f);
    }

    void BuildTabs(RectTransform side)
    {
        tabStrip = Rect("Tabs", side, new Vector2(0f, 1f), new Vector2(1f, 1f));
        tabStrip.pivot = new Vector2(0.5f, 1f);
        tabStrip.anchoredPosition = new Vector2(0f, -150f);
        tabStrip.sizeDelta = new Vector2(0f, 64f);
        tabStrip.gameObject.AddComponent<Image>().color = ListBack;
        var layout = tabStrip.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.spacing = 6f;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;
        layout.childControlWidth = layout.childControlHeight = true;

        tabs.Clear();
        for (int i = 0; i < catalog.categories.Length; i++)
        {
            int index = i;
            var tab = Rect("Tab " + catalog.categories[i].displayName, tabStrip, Vector2.zero, Vector2.one);
            var image = tab.gameObject.AddComponent<Image>();
            var button = tab.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => ShowCategory(index, false));
            var label = Label("Label", tab, 17, TextAnchor.MiddleCenter, Muted);
            label.text = catalog.categories[i].displayName.ToUpperInvariant();
            label.fontStyle = FontStyle.Bold;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 11;
            label.resizeTextMaxSize = 17;
            Stretch(label.rectTransform, 4f);
            tabs.Add(button);
        }

        var titleBar = Rect("Category", side, new Vector2(0f, 1f), new Vector2(1f, 1f));
        titleBar.pivot = new Vector2(0.5f, 1f);
        titleBar.anchoredPosition = new Vector2(0f, -214f);
        titleBar.sizeDelta = new Vector2(0f, 56f);
        titleBar.gameObject.AddComponent<Image>().color = Block(0.95f);
        categoryTitle = Label("Name", titleBar, 26, TextAnchor.MiddleLeft, Color.white);
        categoryTitle.fontStyle = FontStyle.Bold;
        Place(categoryTitle.rectTransform, 24f, 0f, 360f, 56f, true);
        countText = Label("Count", titleBar, 22, TextAnchor.MiddleRight, Muted);
        Place(countText.rectTransform, PanelWidth - 184f, 0f, 160f, 56f, true);
    }

    void BuildList(RectTransform side)
    {
        var area = Rect("List", side, Vector2.zero, Vector2.one);
        area.offsetMin = new Vector2(0f, 230f);
        area.offsetMax = new Vector2(0f, -270f);
        area.gameObject.AddComponent<Image>().color = ListBack;
        area.gameObject.AddComponent<RectMask2D>();
        scroll = area.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        scroll.viewport = area;

        listContent = Rect("Content", area, new Vector2(0f, 1f), new Vector2(1f, 1f));
        listContent.pivot = new Vector2(0.5f, 1f);
        scroll.content = listContent;
    }

    void BuildRows(ClothingCatalog.Category category)
    {
        foreach (var row in rows) Destroy(row.rect.gameObject);
        rows.Clear();
        listContent.sizeDelta = new Vector2(0f, category.items.Length * RowHeight);
        listContent.anchoredPosition = Vector2.zero;

        for (int i = 0; i < category.items.Length; i++)
        {
            int index = i;
            var item = category.items[i];
            var row = new Row();
            row.rect = Rect("Row " + item.displayName, listContent, new Vector2(0f, 1f), new Vector2(1f, 1f));
            row.rect.pivot = new Vector2(0.5f, 1f);
            row.rect.anchoredPosition = new Vector2(0f, -i * RowHeight);
            row.rect.sizeDelta = new Vector2(0f, RowHeight - 4f);
            row.background = row.rect.gameObject.AddComponent<Image>();
            var button = row.rect.gameObject.AddComponent<Button>();
            button.targetGraphic = row.background;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() =>
            {
                if (itemIndex == index) BuyOrWear();
                else SelectItem(index);
            });

            var stripe = Rect("Selected", row.rect, new Vector2(0f, 0f), new Vector2(0f, 1f));
            stripe.pivot = new Vector2(0f, 0.5f);
            stripe.sizeDelta = new Vector2(6f, 0f);
            row.stripe = stripe.gameObject.AddComponent<Image>();
            row.stripe.color = accent;

            var frame = Rect("Icon Frame", row.rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            frame.pivot = new Vector2(0f, 0.5f);
            frame.anchoredPosition = new Vector2(18f, 0f);
            frame.sizeDelta = new Vector2(76f, 76f);
            frame.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.07f);
            var icon = Rect("Icon", frame, Vector2.zero, Vector2.one);
            icon.offsetMin = icon.offsetMax = Vector2.zero;
            row.icon = icon.gameObject.AddComponent<Image>();
            row.icon.sprite = item.icon;
            row.icon.preserveAspect = true;
            row.icon.raycastTarget = false;
            row.icon.enabled = item.icon != null;

            row.name = Label("Name", row.rect, 24, TextAnchor.MiddleLeft, Color.white);
            row.name.text = item.displayName;
            Place(row.name.rectTransform, 112f, 0f, 330f, RowHeight - 4f, true);
            row.price = Label("Price", row.rect, 22, TextAnchor.MiddleRight, accent);
            row.price.fontStyle = FontStyle.Bold;
            Place(row.price.rectTransform, PanelWidth - 186f, 0f, 160f, RowHeight - 4f, true);
            rows.Add(row);
        }
        UseFonts(listContent);
    }

    void BuildFooter(RectTransform side)
    {
        var footer = Rect("Details", side, new Vector2(0f, 0f), new Vector2(1f, 0f));
        footer.pivot = new Vector2(0.5f, 0f);
        footer.sizeDelta = new Vector2(0f, 222f);
        footer.gameObject.AddComponent<Image>().color = Block(0.96f);

        itemTitle = Label("Item", footer, 28, TextAnchor.UpperLeft, Color.white);
        itemTitle.fontStyle = FontStyle.Bold;
        Place(itemTitle.rectTransform, 24f, -18f, PanelWidth - 48f, 36f);
        itemDetail = Label("About", footer, 19, TextAnchor.UpperLeft, Muted);
        Place(itemDetail.rectTransform, 24f, -58f, PanelWidth - 48f, 60f);

        var action = Rect("Buy", footer, new Vector2(0f, 0f), new Vector2(1f, 0f));
        action.pivot = new Vector2(0.5f, 0f);
        action.offsetMin = new Vector2(20f, 20f);
        action.offsetMax = new Vector2(-20f, 92f);
        actionButton = action.gameObject.AddComponent<Image>();
        var button = action.gameObject.AddComponent<Button>();
        button.targetGraphic = actionButton;
        button.onClick.AddListener(BuyOrWear);
        actionText = Label("Label", action, 28, TextAnchor.MiddleCenter, Color.black);
        actionText.fontStyle = FontStyle.Bold;
        Stretch(actionText.rectTransform, 0f);

        var leave = Rect("Leave", root.transform, new Vector2(1f, 0f), new Vector2(1f, 0f));
        leave.pivot = new Vector2(1f, 0f);
        leave.anchoredPosition = new Vector2(-56f, 40f);
        leave.sizeDelta = new Vector2(220f, 64f);
        var leaveImage = leave.gameObject.AddComponent<Image>();
        PanelLook(leaveImage);
        var leaveButton = leave.gameObject.AddComponent<Button>();
        leaveButton.targetGraphic = leaveImage;
        leaveButton.onClick.AddListener(Close);
        var leaveText = Label("Label", leave, 24, TextAnchor.MiddleCenter, Color.white);
        leaveText.text = "DONE";
        leaveText.fontStyle = FontStyle.Bold;
        Stretch(leaveText.rectTransform, 0f);
    }

    void BuildCash()
    {
        var box = Rect("Cash", root.transform, new Vector2(1f, 1f), new Vector2(1f, 1f));
        box.pivot = new Vector2(1f, 1f);
        box.anchoredPosition = new Vector2(-56f, -56f);
        box.sizeDelta = new Vector2(320f, 96f);
        PanelLook(box.gameObject.AddComponent<Image>());
        var label = Label("Label", box, 18, TextAnchor.UpperRight, Muted);
        label.text = "CASH";
        Place(label.rectTransform, 20f, -12f, 280f, 26f);
        cashText = Label("Amount", box, 40, TextAnchor.LowerRight, Good);
        cashText.fontStyle = FontStyle.Bold;
        Place(cashText.rectTransform, 20f, -40f, 280f, 48f);
    }

    void BuildHints()
    {
        var bar = Rect("Hints", root.transform, new Vector2(0f, 0f), new Vector2(1f, 0f));
        bar.pivot = new Vector2(0.5f, 0f);
        bar.offsetMin = new Vector2(56f, 40f);
        bar.offsetMax = new Vector2(-300f, 104f);
        hintText = Label("Text", bar, 20, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.8f));
        hintText.supportRichText = true;
        Stretch(hintText.rectTransform, 0f);
        hintText.text = Key("W S") + " Select    " + Key("A D") + " Category    " + Key("Enter") + " Buy / Wear    "
            + Key("Q E") + " / Drag  Turn    " + Key("Scroll") + " Zoom    " + Key("Esc") + " Done";
        var shadow = hintText.gameObject.AddComponent<Outline>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
    }

    // The small boxes (cash, done) in the panel's own look, or the plain dark colour without it.
    void PanelLook(Image image)
    {
        image.color = panelSprite != null ? Color.white : panel;
        if (panelSprite == null) return;
        image.sprite = panelSprite;
        image.type = panelSprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
    }

    string Key(string key) => "<color=#" + ColorUtility.ToHtmlStringRGB(accent) + "><b>" + key + "</b></color>";

    // ---------- Small helpers ----------

    static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        return rect;
    }

    Text Label(string name, Transform parent, int size, TextAnchor anchor, Color color)
    {
        var text = new GameObject(name, typeof(RectTransform)).AddComponent<Text>();
        text.transform.SetParent(parent, false);
        text.font = font;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        // Anton / Bebas sit taller than their size: never clip a line to nothing.
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    // From the top-left corner of the parent, or its middle-left when centred is set.
    static void Place(RectTransform rect, float x, float y, float width, float height, bool centred = false)
    {
        rect.anchorMin = rect.anchorMax = centred ? new Vector2(0f, 0.5f) : new Vector2(0f, 1f);
        rect.pivot = centred ? new Vector2(0f, 0.5f) : new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    // Dark on the left, clear on the right.
    static Sprite Gradient()
    {
        var tex = new Texture2D(64, 4, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int x = 0; x < 64; x++)
        {
            float a = 1f - Mathf.SmoothStep(0.55f, 1f, x / 63f);
            for (int y = 0; y < 4; y++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 64, 4), new Vector2(0.5f, 0.5f));
    }
}
