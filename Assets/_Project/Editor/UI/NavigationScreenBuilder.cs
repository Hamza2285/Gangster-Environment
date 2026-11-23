using System.Text;
using OpenworldCrime.UI.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Adds the "Navigation Experience" screen (minimap style: square or circle) to the main menu scene, from the art the user
    /// supplied (background, title bar, the square / circle cards selected and unselected, NEXT). It opens after ENTER GAME and
    /// leads to the loading screen; the choice drives the in-game minimap (<see cref="NavigationScreen"/>, MinimapManager).
    /// Rerunnable: the old screen is replaced. Menu: Tools > OpenworldCrime > UI > Build Navigation Screen.
    /// </summary>
    public static class NavigationScreenBuilder
    {
        private const string ArtFolder = "Assets/_Project/Art/Menu/Navigation";
        private const string FontPath = "Assets/_Project/Art/Menu/BebasKai-Regular.otf";
        private static readonly string[] Art =
        {
            "BG.png", "Navigation_Experience_Bar.png", "Next_butn.png",
            "Select_Square-Map.png", "Unselect_Square-Map.png", "Selected_Circle-Map.png", "Unselect_Circle-Map.png",
        };

        [MenuItem("Tools/OpenworldCrime/UI/Build Navigation Screen")]
        public static void Menu() => Debug.Log(Build());

        public static string Build()
        {
            var log = new StringBuilder("[NavigationScreenBuilder]\n");
            ImportArt(log);
            Sprite bg = Load("BG.png"), bar = Load("Navigation_Experience_Bar.png"), next = Load("Next_butn.png");
            Sprite squareOn = Load("Select_Square-Map.png"), squareOff = Load("Unselect_Square-Map.png");
            Sprite circleOn = Load("Selected_Circle-Map.png"), circleOff = Load("Unselect_Circle-Map.png");
            if (bg == null || bar == null || next == null || squareOn == null || squareOff == null || circleOn == null || circleOff == null)
                return log.Append("  art missing").ToString();
            Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);

            Scene scene = EditorSceneManager.OpenScene(MainMenuBuilder.ScenePath, OpenSceneMode.Single);
            var old = GameObject.Find("Navigation Screen");
            if (old != null) Object.DestroyImmediate(old);

            // Its own canvas between the menu (0) and the loading screen (100).
            var canvasGo = new GameObject("Navigation Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            var screen = canvasGo.AddComponent<NavigationScreen>();

            RectTransform root = Rect("Root", canvasGo.transform);
            Stretch(root);
            var group = root.gameObject.AddComponent<CanvasGroup>();

            // Background (covers the screen at any aspect; it also blocks clicks to the menu behind).
            Image back = NewImage("Background", root, bg);
            back.raycastTarget = true;
            Stretch(back.rectTransform);
            var fit = back.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = 16f / 9f;

            // Title bar: the art is a full 1920x1080 frame with the bar near the top, so it is laid over the whole screen.
            Image title = NewImage("Title Bar", root, bar);
            Stretch(title.rectTransform);
            var titleFit = title.gameObject.AddComponent<AspectRatioFitter>();
            titleFit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            titleFit.aspectRatio = 16f / 9f;

            Text hint = NewText("Hint", root, font, "CHOOSE YOUR MINIMAP", 40, new Color(1f, 1f, 1f, 0.85f));
            hint.rectTransform.anchoredPosition = new Vector2(0f, 250f);

            // A selector wheel: one card at a time in the middle (the shown one is the choice, so its selected art).
            RectTransform squareCard = Card("Square Card", root, squareOn, new Vector2(688f, 384f));
            RectTransform circleCard = Card("Circle Card", root, circleOn, new Vector2(420f, 420f));

            // Arrows either side: the game's pink chevrons in dark round buttons.
            var style = AssetDatabase.LoadAssetAtPath<GameButtonStyleAsset>("Assets/Game/Resources/UIButtons/ButtonStyle.asset");
            Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            Button previous = ArrowButton("Previous Minimap", root, knob, style != null ? style.arrowLeft : null, "<", font, new Vector2(-520f, 20f));
            Button following = ArrowButton("Next Minimap", root, knob, style != null ? style.arrowRight : null, ">", font, new Vector2(520f, 20f));

            Text optionLabel = NewText("Option Label", root, font, "MINIMAP (SQUARE)", 54, Color.white);
            optionLabel.rectTransform.anchoredPosition = new Vector2(0f, -232f);
            var dots = new Image[2];
            for (int d = 0; d < dots.Length; d++)
            {
                dots[d] = NewImage("Dot " + (d + 1), root, knob);
                dots[d].preserveAspect = false;
                dots[d].rectTransform.sizeDelta = new Vector2(18f, 18f);
                dots[d].rectTransform.anchoredPosition = new Vector2((d - 0.5f) * 36f, -290f);
            }

            // NEXT.
            Image nextImage = NewImage("Next Button", root, next);
            nextImage.raycastTarget = true;
            nextImage.rectTransform.sizeDelta = new Vector2(466f, 87f);
            nextImage.rectTransform.anchoredPosition = new Vector2(0f, -400f);
            var nextButton = nextImage.gameObject.AddComponent<Button>();
            nextButton.targetGraphic = nextImage;
            Feedback(nextButton.gameObject);

            var so = new SerializedObject(screen);
            so.FindProperty("root").objectReferenceValue = root.gameObject;
            so.FindProperty("group").objectReferenceValue = group;
            var cards = so.FindProperty("cards");
            cards.arraySize = 2;
            cards.GetArrayElementAtIndex(0).objectReferenceValue = squareCard;
            cards.GetArrayElementAtIndex(1).objectReferenceValue = circleCard;
            so.FindProperty("previousButton").objectReferenceValue = previous;
            so.FindProperty("nextOptionButton").objectReferenceValue = following;
            so.FindProperty("nextButton").objectReferenceValue = nextButton;
            so.FindProperty("optionLabel").objectReferenceValue = optionLabel;
            var dotsProp = so.FindProperty("dots");
            dotsProp.arraySize = dots.Length;
            for (int d = 0; d < dots.Length; d++) dotsProp.GetArrayElementAtIndex(d).objectReferenceValue = dots[d];
            so.ApplyModifiedPropertiesWithoutUndo();
            root.gameObject.SetActive(false);

            var menu = Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
            if (menu != null)
            {
                var mso = new SerializedObject(menu);
                mso.FindProperty("navigationScreen").objectReferenceValue = screen;
                mso.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine("  wired to the main menu (ENTER GAME -> navigation -> loading)");
            }
            else log.AppendLine("  MainMenuController not found");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return log.Append("  screen built").ToString();
        }

        private static void ImportArt(StringBuilder log)
        {
            if (!AssetDatabase.IsValidFolder(ArtFolder)) AssetDatabase.CreateFolder("Assets/_Project/Art/Menu", "Navigation");
            foreach (string file in Art)
            {
                string loose = "Assets/" + file, path = ArtFolder + "/" + file;
                if (AssetDatabase.LoadMainAssetAtPath(path) == null && AssetDatabase.LoadMainAssetAtPath(loose) != null)
                {
                    string error = AssetDatabase.MoveAsset(loose, path);
                    if (!string.IsNullOrEmpty(error)) log.AppendLine("  move " + file + ": " + error);
                }
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
            log.AppendLine("  art in " + ArtFolder);
        }

        private static Sprite Load(string file) => AssetDatabase.LoadAssetAtPath<Sprite>(ArtFolder + "/" + file);

        private static RectTransform Card(string name, Transform parent, Sprite sprite, Vector2 size)
        {
            Image image = NewImage(name, parent, sprite);
            image.rectTransform.sizeDelta = size;
            image.rectTransform.anchoredPosition = new Vector2(0f, 20f);
            image.gameObject.AddComponent<CanvasGroup>();   // the slide-in fades it
            return image.rectTransform;
        }

        private static Button ArrowButton(string name, Transform parent, Sprite circle, Sprite chevron, string fallback, Font font, Vector2 position)
        {
            Image back = NewImage(name, parent, circle);
            back.preserveAspect = false;
            back.color = new Color(0.08f, 0.05f, 0.1f, 0.75f);
            back.raycastTarget = true;
            back.rectTransform.sizeDelta = new Vector2(120f, 120f);
            back.rectTransform.anchoredPosition = position;
            var outline = back.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.35f);
            outline.effectDistance = new Vector2(2f, -2f);
            if (chevron != null)
            {
                Image icon = NewImage("Arrow", back.transform, chevron);
                icon.rectTransform.sizeDelta = new Vector2(54f, 54f);
            }
            else
            {
                Text t = NewText("Arrow", back.transform, font, fallback, 80, Color.white);
                t.rectTransform.anchoredPosition = new Vector2(0f, 4f);
            }
            var button = back.gameObject.AddComponent<Button>();
            button.targetGraphic = back;
            Feedback(back.gameObject);
            return button;
        }

        private static void Feedback(GameObject go)
        {
            if (go.GetComponent<UIPressFeedback>() == null) go.AddComponent<UIPressFeedback>();
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        private static Image NewImage(string name, Transform parent, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static Text NewText(string name, Transform parent, Font font, string text, int size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var shadow = go.AddComponent<Shadow>();
            shadow.effectDistance = new Vector2(2f, -2f);
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            return t;
        }
    }
}
