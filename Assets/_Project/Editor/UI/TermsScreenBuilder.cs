using System.IO;
using OpenworldCrime.UI.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Adds the "Legal Terms &amp; Policies" screen (<see cref="TermsScreen"/>) to the main menu scene: purple gradient, title,
    /// Privacy Policy and Terms of Service tick boxes, Confirm on the left; a CONFIRM note on the right. It opens after the
    /// Navigation Experience screen and leads to the loading screen. Rerunnable: the old screen is replaced.
    /// Menu: Tools > OpenworldCrime > UI > Build Terms Screen.
    /// </summary>
    public static class TermsScreenBuilder
    {
        private const string ArtFolder = "Assets/_Project/Art/Menu/Terms";
        private const string BackgroundPath = ArtFolder + "/TermsBG.png";
        private const string FontPath = "Assets/_Project/Art/Menu/BebasKai-Regular.otf";

        private static readonly Color Pink = new Color(0.93f, 0.55f, 0.82f, 1f);
        private static readonly Color Line = new Color(1f, 1f, 1f, 0.18f);
        private static readonly Color Body = new Color(1f, 1f, 1f, 0.9f);

        private const float Left = 120f;          // left column x (reference 1920x1080, from the top-left)
        private const float ColumnWidth = 860f;
        private const float RightX = 1160f;       // right column x
        private const float RightWidth = 660f;

        [MenuItem("Tools/OpenworldCrime/UI/Build Terms Screen")]
        public static void Menu() => Debug.Log(Build());

        public static string Build()
        {
            Sprite bg = MakeBackground();
            Font title = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            Font body = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Sprite check = MakeCheckmark();
            Sprite vignette = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Menu/Vignette.png");

            Scene scene = EditorSceneManager.OpenScene(MainMenuBuilder.ScenePath, OpenSceneMode.Single);
            var old = GameObject.Find("Terms Screen");
            if (old != null) Object.DestroyImmediate(old);

            // Above the navigation screen (50), below the loading screen (100).
            var canvasGo = new GameObject("Terms Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            var screen = canvasGo.AddComponent<TermsScreen>();

            RectTransform root = Rect("Root", canvasGo.transform);
            Stretch(root);
            var group = root.gameObject.AddComponent<CanvasGroup>();

            Image back = NewImage("Background", root, bg, Color.white);
            back.raycastTarget = true;   // blocks clicks to the screens behind
            back.preserveAspect = false;
            Stretch(back.rectTransform);
            if (vignette != null)
            {
                Image v = NewImage("Vignette", root, vignette, new Color(0f, 0f, 0f, 0.45f));
                v.preserveAspect = false;
                Stretch(v.rectTransform);
            }

            RectTransform content = Rect("Content", root);
            Stretch(content);
            content.gameObject.AddComponent<SafeAreaFitter>();

            // Left column.
            Label(content, title, "LEGAL TERMS & POLICIES", 68, Color.white, Left, 80f, ColumnWidth, 80f);
            Divider(content, Left, 175f, ColumnWidth, Line);

            Label(content, title, "PRIVACY POLICY", 46, Color.white, Left, 200f, ColumnWidth, 56f);
            Button privacyLink = Link(content, title, "READ", Left + ColumnWidth - 120f, 200f);
            Toggle privacy = Tick(content, body, check, "I acknowledge that I have read and understand the Privacy Policy", 270f);
            Divider(content, Left, 375f, ColumnWidth, Line);

            Label(content, title, "TERMS OF SERVICE", 46, Color.white, Left, 400f, ColumnWidth, 56f);
            Button termsLink = Link(content, title, "READ", Left + ColumnWidth - 120f, 400f);
            Toggle terms = Tick(content, body, check, "I have read and agree to the Terms of Service", 470f);
            Divider(content, Left, 545f, ColumnWidth, Line);

            // Confirm: a light plate, label on the left, chevron on the right; dim until both boxes are ticked.
            Image plate = NewImage("Confirm Button", content, null, new Color(0.93f, 0.93f, 0.95f, 1f));
            plate.preserveAspect = false;
            plate.raycastTarget = true;
            Place(plate.rectTransform, Left, 585f, 540f, 80f);
            var shine = plate.gameObject.AddComponent<Shadow>();
            shine.effectColor = new Color(0f, 0f, 0f, 0.35f);
            shine.effectDistance = new Vector2(0f, -4f);
            var confirmGroup = plate.gameObject.AddComponent<CanvasGroup>();
            var confirm = plate.gameObject.AddComponent<Button>();
            confirm.targetGraphic = plate;
            var colors = confirm.colors;
            colors.disabledColor = Color.white;   // the group's alpha shows the locked state
            colors.pressedColor = new Color(0.8f, 0.75f, 0.85f);
            confirm.colors = colors;
            plate.gameObject.AddComponent<UIPressFeedback>();
            Text confirmText = NewText("Label", plate.transform, body, "Confirm", 34, new Color(0.25f, 0.22f, 0.3f), TextAnchor.MiddleLeft, FontStyle.Bold);
            Stretch(confirmText.rectTransform);
            confirmText.rectTransform.offsetMin = new Vector2(26f, 0f);
            Text chevron = NewText("Chevron", plate.transform, body, ">", 40, new Color(0.45f, 0.42f, 0.5f), TextAnchor.MiddleRight, FontStyle.Normal);
            Stretch(chevron.rectTransform);
            chevron.rectTransform.offsetMax = new Vector2(-26f, 0f);

            // The user's CONFIRM art (pink when ready, dark while locked) replaces the plain plate when present.
            Sprite ready = ImportButton("Confirm_Button.png"), locked = ImportButton("Dark_Confirm_Button.png");
            bool art = ready != null && locked != null;
            if (art)
            {
                plate.sprite = locked;
                plate.color = Color.white;
                plate.preserveAspect = true;
                Place(plate.rectTransform, Left, 580f, 540f, 540f * 87f / 466f);
                Object.DestroyImmediate(shine);
                confirmText.gameObject.SetActive(false);
                chevron.gameObject.SetActive(false);
            }

            // Right column.
            Label(content, title, "CONFIRM", 46, Color.white, RightX, 200f, RightWidth, 56f);
            Divider(content, RightX, 262f, RightWidth + 120f, Pink);
            Label(content, body, "Confirm setup and start the game.", 30, Body, RightX, 285f, RightWidth, 44f);

            var so = new SerializedObject(screen);
            so.FindProperty("root").objectReferenceValue = root.gameObject;
            so.FindProperty("group").objectReferenceValue = group;
            so.FindProperty("privacyToggle").objectReferenceValue = privacy;
            so.FindProperty("termsToggle").objectReferenceValue = terms;
            so.FindProperty("confirmButton").objectReferenceValue = confirm;
            so.FindProperty("confirmGroup").objectReferenceValue = confirmGroup;
            so.FindProperty("confirmImage").objectReferenceValue = art ? plate : null;
            so.FindProperty("readySprite").objectReferenceValue = art ? ready : null;
            so.FindProperty("lockedSprite").objectReferenceValue = art ? locked : null;
            so.FindProperty("privacyLink").objectReferenceValue = privacyLink;
            so.FindProperty("termsLink").objectReferenceValue = termsLink;
            so.ApplyModifiedPropertiesWithoutUndo();
            root.gameObject.SetActive(false);

            string wired = "  MainMenuController not found";
            var menu = Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
            if (menu != null)
            {
                var mso = new SerializedObject(menu);
                mso.FindProperty("termsScreen").objectReferenceValue = screen;
                mso.ApplyModifiedPropertiesWithoutUndo();
                wired = "  wired: START GAME -> minimap -> terms -> loading";
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return "[TermsScreenBuilder]\n" + wired + "\n  screen built";
        }

        // A tick box with its sentence; the whole row toggles it.
        private static Toggle Tick(RectTransform parent, Font font, Sprite check, string text, float top)
        {
            RectTransform row = Rect("Toggle " + text.Substring(text.Length - 14).Trim(), parent);
            Place(row, Left + 14f, top, ColumnWidth - 14f, 90f);
            var hit = row.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);

            Image border = NewImage("Box", row, null, new Color(1f, 1f, 1f, 0.75f));
            border.preserveAspect = false;
            Place(border.rectTransform, 0f, 8f, 38f, 38f);
            Image inner = NewImage("Inside", border.transform, null, new Color(0.24f, 0.13f, 0.3f, 1f));
            inner.preserveAspect = false;
            Stretch(inner.rectTransform);
            inner.rectTransform.offsetMin = new Vector2(2f, 2f);
            inner.rectTransform.offsetMax = new Vector2(-2f, -2f);
            Image mark = NewImage("Checkmark", border.transform, check, Color.white);
            Stretch(mark.rectTransform);
            mark.rectTransform.offsetMin = new Vector2(3f, 3f);
            mark.rectTransform.offsetMax = new Vector2(-3f, -3f);

            Text label = NewText("Label", row, font, text, 30, Body, TextAnchor.UpperLeft, FontStyle.Normal);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.lineSpacing = 1.15f;
            Place(label.rectTransform, 62f, 8f, ColumnWidth - 100f, 90f);

            var toggle = row.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = border;
            toggle.graphic = mark;
            toggle.isOn = false;
            toggle.toggleTransition = Toggle.ToggleTransition.Fade;
            return toggle;
        }

        private static Button Link(RectTransform parent, Font font, string text, float x, float top)
        {
            Text t = Label(parent, font, text, 34, Pink, x, top + 6f, 120f, 50f);
            t.alignment = TextAnchor.MiddleRight;
            t.raycastTarget = true;
            var button = t.gameObject.AddComponent<Button>();
            button.targetGraphic = t;
            return button;
        }

        private static Text Label(RectTransform parent, Font font, string text, int size, Color color, float x, float top, float width, float height)
        {
            Text t = NewText(text.Length > 24 ? text.Substring(0, 24) : text, parent, font, text, size, color, TextAnchor.MiddleLeft, FontStyle.Normal);
            Place(t.rectTransform, x, top, width, height);
            return t;
        }

        private static void Divider(RectTransform parent, float x, float top, float width, Color color)
        {
            Image line = NewImage("Divider", parent, null, color);
            line.preserveAspect = false;
            Place(line.rectTransform, x, top, width, 2f);
        }

        // Positions from the top-left corner of the reference frame.
        private static void Place(RectTransform r, float x, float top, float width, float height)
        {
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.sizeDelta = new Vector2(width, height);
            r.anchoredPosition = new Vector2(x, -top);
        }

        // A diagonal purple gradient: deep plum bottom-left to soft magenta top-right.
        private static Sprite MakeBackground()
        {
            if (!AssetDatabase.IsValidFolder(ArtFolder)) AssetDatabase.CreateFolder("Assets/_Project/Art/Menu", "Terms");
            if (AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPath) == null)
            {
                const int w = 512, h = 288;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                var dark = new Color(0.24f, 0.12f, 0.30f);
                var mid = new Color(0.40f, 0.22f, 0.45f);
                var light = new Color(0.58f, 0.32f, 0.56f);
                var pixels = new Color[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float t = Mathf.Clamp01((x / (float)w) * 0.6f + (y / (float)h) * 0.4f);
                        pixels[y * w + x] = t < 0.5f ? Color.Lerp(dark, mid, t * 2f) : Color.Lerp(mid, light, (t - 0.5f) * 2f);
                    }
                tex.SetPixels(pixels);
                File.WriteAllBytes(BackgroundPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(BackgroundPath);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(BackgroundPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPath);
        }

        // Moves a button image the user dropped into Assets/ into the Terms art folder and imports it as a sprite.
        private static Sprite ImportButton(string file)
        {
            string loose = "Assets/" + file, path = ArtFolder + "/" + file;
            if (!AssetDatabase.IsValidFolder(ArtFolder)) AssetDatabase.CreateFolder("Assets/_Project/Art/Menu", "Terms");
            if (AssetDatabase.LoadMainAssetAtPath(path) == null && AssetDatabase.LoadMainAssetAtPath(loose) != null)
                AssetDatabase.MoveAsset(loose, path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return null;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // A white tick (Unity's built-in checkmark is dark grey and can't be tinted lighter).
        private static Sprite MakeCheckmark()
        {
            const string path = ArtFolder + "/Checkmark.png";
            if (!AssetDatabase.IsValidFolder(ArtFolder)) AssetDatabase.CreateFolder("Assets/_Project/Art/Menu", "Terms");
            if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null)
            {
                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                Vector2 a = new Vector2(12f, 33f), b = new Vector2(26f, 18f), c = new Vector2(53f, 47f);
                const float half = 4.2f;
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        float d = Mathf.Min(SegmentDistance(p, a, b), SegmentDistance(p, b, c));
                        byte alpha = (byte)(255f * Mathf.Clamp01(half + 0.5f - d));
                        pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                    }
                tex.SetPixels32(pixels);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
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

        private static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static Text NewText(string name, Transform parent, Font font, string text, int size, Color color, TextAnchor anchor, FontStyle style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = anchor;
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }
}
