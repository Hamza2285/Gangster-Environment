using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenworldCrime.UI.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Builds the main menu scene (Tools > OpenworldCrime > UI > Build Main Menu Scene) from the user's assets in
    /// Assets/LatestUiMenu: mobile import settings for the art, video and music, the right-hand icon bar sliced from the icon
    /// sheet (by its transparent gaps), the MainMenu scene (video background, music, icon bar, ENTER GAME, panels, loading
    /// screen) and the build list (MainMenu first, every other scene kept in its order).
    /// </summary>
    public static class MainMenuBuilder
    {
        private const string Source = "Assets/LatestUiMenu";
        private const string SplashFolder = Source + "/LoadingSplash";
        private const string IconSheet = Source + "/menu side icons right .png";
        private const string VideoPath = Source + "/MenuBg.mp4";
        private const string MusicPath = Source + "/gtaMusic.MP3";
        private const string ArtFolder = "Assets/_Project/Art/Menu";
        private const string FontSource = "Assets/Invector-3rdPersonController/Basic Locomotion/Editor/Resources/BebasKai-Regular.otf";
        private const string FontPath = ArtFolder + "/BebasKai-Regular.otf";
        public const string ScenePath = "Assets/_Project/Scenes/MainMenu.unity";
        public const string SplashScenePath = "Assets/_Project/Scenes/Splash.unity";
        private const string ArtSetFolder = "Assets/_Project/Data/UI";
        private const string ArtSetPath = ArtSetFolder + "/SplashArt.asset";
        // Splash title (no logo asset in the project yet; plain text in the scene, easy to change).
        private const string Title = "GANGSTER";
        private const string Tagline = "OPEN WORLD CRIME";
        private const string GameScene = "Assets/_Project/Scenes/Gangstar_Gameplay.unity";

        // Backgrounds with no people (cut-outs go in front of them); the transparent PNGs are the character cut-outs.
        private static readonly string[] StillBackgrounds = { "Neon Nights at the Empty Nightclub", "Sunset City Beach Reflections" };
        private static readonly string[] IconNames = { "Icon_Policy", "Icon_Share", "Icon_Settings", "Icon_Exit" };
        // Right-hand menu icons (were 132: the user asked for them smaller).
        public const float IconSize = 92f;

        private static readonly string[] Tips =
        {
            "Sprint and tap Punch to throw a running punch that knocks people flat.",
            "Press T to whistle for a taxi.",
            "Bailing out of a moving car hurts. The faster you go, the harder you land.",
            "Steal a car by walking up to the driver's door and pressing Enter.",
            "Draw a gun and the street clears fast. So does the police's patience.",
            "Open the map with M and set a waypoint.",
            "Street races start at the checkered markers. Win them for cash.",
            "Switch between Jason and Mia from the character wheel.",
        };

        [MenuItem("Tools/OpenworldCrime/UI/Build Main Menu Scene")]
        public static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            var log = new StringBuilder("[MainMenuBuilder]\n");
            if (EditorApplication.isPlaying) return log.Append("  stop play mode first").ToString();
            EnsureFolder(ArtFolder);
            ImportArt(log);
            Sprite[] icons = SliceIcons(log);
            ImportMedia(log);
            Font font = CopyFont(log);
            Sprite gradient = MakeGradient();
            Sprite ring = MakeRing();
            Sprite shadeUp = MakeVerticalGradient();
            MakeSoftTextures();

            var splash = new Dictionary<string, Sprite>();
            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { SplashFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (s != null) splash[Path.GetFileNameWithoutExtension(path)] = s;
            }
            var still = new List<Sprite>();
            var cutouts = new List<Sprite>();
            var full = new List<Sprite>();
            foreach (var pair in splash.OrderBy(p => p.Key))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(pair.Value));
                if (StillBackgrounds.Contains(pair.Key)) still.Add(pair.Value);
                else if (importer.DoesSourceTextureHaveAlpha()) cutouts.Add(pair.Value);
                else full.Add(pair.Value);
            }
            log.AppendLine($"  loading art: {still.Count} still backgrounds, {cutouts.Count} character cut-outs, {full.Count} full artworks");

            SplashArtSet artSet = MakeArtSet(still, cutouts, full);
            BuildScene(icons, font, gradient, shadeUp, ring, artSet, log);
            BuildSplash(font, shadeUp, artSet, log);
            UpdateBuildList(log);
            return log.ToString();
        }

        // ------------------------------------------------------------------------------------------------ imports

        private static void ImportArt(StringBuilder log)
        {
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { SplashFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = ti.DoesSourceTextureHaveAlpha();
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.maxTextureSize = 2048;
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.filterMode = FilterMode.Bilinear;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                {
                    name = "Android", overridden = true, maxTextureSize = 2048,
                    format = TextureImporterFormat.ASTC_6x6, compressionQuality = 50
                });
                ti.SaveAndReimport();
                n++;
            }
            log.AppendLine($"  {n} splash images: Sprite, max 2048, compressed (Android ASTC 6x6), no mipmaps, no power-of-two stretching");
        }

        // The icon sheet: one sprite per icon, cut at the fully transparent columns between them.
        private static Sprite[] SliceIcons(StringBuilder log)
        {
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(IconSheet));
            int w = tex.width, h = tex.height;
            Color32[] px = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            var columnHasInk = new bool[w];
            int top = 0, bottom = h;
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    if (px[y * w + x].a > 24) { columnHasInk[x] = true; top = Mathf.Max(top, y); bottom = Mathf.Min(bottom, y); }
            var runs = new List<Vector2Int>();
            for (int x = 0; x < w; x++)
            {
                if (!columnHasInk[x]) continue;
                int start = x;
                while (x < w && columnHasInk[x]) x++;
                if (x - start > 40) runs.Add(new Vector2Int(start, x));
            }
            var ti = (TextureImporter)AssetImporter.GetAtPath(IconSheet);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Multiple;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = 2048, format = TextureImporterFormat.ASTC_6x6, compressionQuality = 50
            });
            var meta = new List<SpriteMetaData>();
            for (int i = 0; i < runs.Count && i < IconNames.Length; i++)
                meta.Add(new SpriteMetaData
                {
                    name = IconNames[i],
                    rect = new Rect(runs[i].x, bottom, runs[i].y - runs[i].x, top - bottom + 1),
                    alignment = (int)SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f)
                });
#pragma warning disable 618
            ti.spritesheet = meta.ToArray();
#pragma warning restore 618
            ti.SaveAndReimport();
            var sprites = AssetDatabase.LoadAllAssetsAtPath(IconSheet).OfType<Sprite>().ToDictionary(s => s.name);
            var result = new Sprite[IconNames.Length];
            for (int i = 0; i < IconNames.Length; i++) sprites.TryGetValue(IconNames[i], out result[i]);
            log.AppendLine($"  icon sheet {w}x{h}: {runs.Count} icons found -> " + string.Join(", ", meta.Select(m => $"{m.name} {m.rect.width:F0}x{m.rect.height:F0}")));
            return result;
        }

        private static void ImportMedia(StringBuilder log)
        {
            var vi = (VideoClipImporter)AssetImporter.GetAtPath(VideoPath);
            var android = vi.GetTargetSettings("Android") ?? new VideoImporterTargetSettings();
            android.enableTranscoding = true;
            android.codec = VideoCodec.H264;
            android.resizeMode = VideoResizeMode.OriginalSize;
            android.bitrateMode = VideoBitrateMode.Medium;
            vi.SetTargetSettings("Android", android);
            vi.importAudio = false;
            vi.SaveAndReimport();

            var ai = (AudioImporter)AssetImporter.GetAtPath(MusicPath);
            var s = ai.defaultSampleSettings;
            s.loadType = AudioClipLoadType.Streaming;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
            ai.defaultSampleSettings = s;
            ai.loadInBackground = true;
            ai.SaveAndReimport();
            log.AppendLine("  video: Android transcode H.264 (original size, medium bitrate), audio track dropped (muted); music: streaming Vorbis");
        }

        private static Font CopyFont(StringBuilder log)
        {
            if (AssetDatabase.LoadAssetAtPath<Font>(FontPath) == null) AssetDatabase.CopyAsset(FontSource, FontPath);
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            log.AppendLine("  font: " + (font != null ? "Bebas Kai (copied from Invector's editor folder, which is not in builds)" : "MISSING"));
            return font;
        }

        // A soft horizontal ramp (white, alpha 0 -> 1) for edge shading, tinted in the scene.
        private static Sprite MakeGradient()
        {
            string path = ArtFolder + "/Gradient.png";
            if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null)
            {
                var t = new Texture2D(256, 4, TextureFormat.RGBA32, false);
                for (int x = 0; x < 256; x++)
                {
                    float a = x / 255f;
                    a = a * a * (3f - 2f * a);
                    for (int y = 0; y < 4; y++) t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
            }
            SpriteImport(path);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // An anti-aliased three-quarter ring for the loading spinner.
        private static Sprite MakeRing()
        {
            string path = ArtFolder + "/SpinnerRing.png";
            if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null)
            {
                const int s = 128;
                var t = new Texture2D(s, s, TextureFormat.RGBA32, false);
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float dx = x - (s - 1) * 0.5f, dy = y - (s - 1) * 0.5f;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float band = Mathf.Clamp01(1f - Mathf.Abs(r - 52f) / 7f) * 1.8f;
                        float angle = (Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 1f) % 1f;
                        float tail = Mathf.Clamp01(angle / 0.75f);
                        t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(band) * (angle < 0.75f ? tail : 0f)));
                    }
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
            }
            SpriteImport(path);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void SpriteImport(string path)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();
        }

        // ------------------------------------------------------------------------------------------------ scene

        private static void BuildScene(Sprite[] icons, Font font, Sprite gradient, Sprite shadeUp, Sprite ring, SplashArtSet artSet, StringBuilder log)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Sprite ui = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            // Camera (clears to black; all UI is on overlay canvases) and input.
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
            camGo.AddComponent<AudioListener>();
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();

            // ///Manager/// with the menu controller, its video player and music.
            var managers = new GameObject("///Manager///");
            var menuGo = new GameObject("Main Menu");
            menuGo.transform.SetParent(managers.transform, false);
            var controller = menuGo.AddComponent<MainMenuController>();
            var video = menuGo.AddComponent<VideoPlayer>();
            video.source = VideoSource.VideoClip;
            video.clip = AssetDatabase.LoadAssetAtPath<VideoClip>(VideoPath);
            video.playOnAwake = false;
            video.isLooping = true;
            video.waitForFirstFrame = true;
            video.skipOnDrop = true;
            video.audioOutputMode = VideoAudioOutputMode.None;
            video.renderMode = VideoRenderMode.RenderTexture;
            var music = menuGo.AddComponent<AudioSource>();
            music.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
            music.loop = true;
            music.playOnAwake = false;
            music.spatialBlend = 0f;
            music.volume = 0f;

            // ---- Menu canvas
            Canvas menuCanvas = NewCanvas("Menu Canvas", 0);
            RectTransform menuRoot = (RectTransform)menuCanvas.transform;
            var bgBlack = NewImage("Background", menuRoot, ui, Color.black);
            Stretch(bgBlack.rectTransform);
            bgBlack.raycastTarget = false;
            var videoGo = new GameObject("Video", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            videoGo.transform.SetParent(menuRoot, false);
            var videoImage = videoGo.GetComponent<RawImage>();
            videoImage.raycastTarget = false;
            videoImage.color = new Color(1f, 1f, 1f, 0f);
            var videoFitter = videoGo.GetComponent<AspectRatioFitter>();
            videoFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            videoFitter.aspectRatio = 16f / 9f;
            CenterAnchors((RectTransform)videoGo.transform);
            // Edge shading so the icons and the button read over any frame of the video.
            var right = NewImage("Shade Right", menuRoot, gradient, new Color(0f, 0f, 0f, 0.6f));
            right.raycastTarget = false;
            Anchor(right.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(460f, 0f), Vector2.zero);
            var bottom = NewImage("Shade Bottom", menuRoot, shadeUp, new Color(0f, 0f, 0f, 0.7f));
            bottom.raycastTarget = false;
            Anchor(bottom.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 300f), Vector2.zero);

            var safe = NewRect("Safe Area", menuRoot);
            Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // Right-hand icon bar.
            var bar = NewRect("Right Bar", safe);
            Anchor(bar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(120f, 460f), new Vector2(-30f, 40f));
            var layout = bar.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 20f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            string[] labels = { "Privacy Policy", "Share", "Settings", "Exit" };
            var iconButtons = new Button[4];
            for (int i = 0; i < 4; i++)
            {
                var b = NewImage(labels[i] + " Button", bar, icons[i], Color.white);
                b.preserveAspect = true;
                b.rectTransform.sizeDelta = new Vector2(IconSize, IconSize);
                iconButtons[i] = b.gameObject.AddComponent<Button>();
                iconButtons[i].transition = Selectable.Transition.ColorTint;
                var colors = iconButtons[i].colors;
                colors.highlightedColor = new Color(1f, 0.85f, 0.55f);
                colors.pressedColor = new Color(0.85f, 0.65f, 0.35f);
                colors.fadeDuration = 0.08f;
                iconButtons[i].colors = colors;
                b.gameObject.AddComponent<UIPressFeedback>();
            }

            // START GAME (bottom centre): the user's button art; the button scales on press, its inner group breathes.
            Sprite startArt = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Menu/Start_Game_button.png");
            var enterImage = NewImage("Enter Game Button", safe, ui, new Color(0f, 0f, 0f, 0.001f));
            Anchor(enterImage.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                startArt != null ? new Vector2(600f, 600f * 87f / 466f) : new Vector2(560f, 120f), new Vector2(0f, 54f));
            var enter = enterImage.gameObject.AddComponent<Button>();
            enter.transition = Selectable.Transition.None;
            enterImage.gameObject.AddComponent<UIPressFeedback>();
            var pulseRoot = NewRect("Pulse", enterImage.rectTransform);
            Stretch(pulseRoot);
            var plate = NewImage("Plate", pulseRoot, ui, new Color(0.02f, 0.02f, 0.04f, 0.7f));
            Stretch(plate.rectTransform);
            plate.raycastTarget = false;
            plate.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.35f);
            var glow = NewImage("Glow Line", pulseRoot, ui, new Color(1f, 0.55f, 0.85f, 0.6f));
            Anchor(glow.rectTransform, new Vector2(0.12f, 0f), new Vector2(0.88f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 5f), new Vector2(0f, 10f));
            glow.raycastTarget = false;
            var enterText = NewText("Label", pulseRoot, font, "ENTER GAME", 78, TextAnchor.MiddleCenter, Color.white);
            Stretch(enterText.rectTransform);
            var shadow = enterText.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(3f, -3f);
            var pulse = pulseRoot.gameObject.AddComponent<UIPulse>();
            SetRef(pulse, "target", pulseRoot);
            if (startArt != null)
            {
                plate.gameObject.SetActive(false);
                glow.gameObject.SetActive(false);
                enterText.gameObject.SetActive(false);
                var art = NewImage("Art", pulseRoot, startArt, Color.white);
                Stretch(art.rectTransform);
                art.preserveAspect = true;
                art.raycastTarget = false;
            }
            else SetRef(pulse, "glow", glow);

            // Panels.
            var closeButtons = new List<Button>();
            GameObject policy = NewPanel("Policy Panel", safe, ui, font, "PRIVACY POLICY & TERMS",
                "The privacy policy and terms of use will be shown here.\n(Set the policy page link on the Main Menu controller to open it in the browser instead.)", closeButtons);
            GameObject share = NewPanel("Share Panel", safe, ui, font, "SHARE",
                "Sharing the game with friends is coming soon.", closeButtons);
            GameObject settings = NewPanel("Settings Panel", safe, ui, font, "SETTINGS", "", closeButtons);
            var sliderLabel = NewText("Music Label", settings.transform.Find("Window"), font, "MUSIC VOLUME", 44, TextAnchor.MiddleLeft, Color.white);
            Anchor(sliderLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(640f, 60f), new Vector2(0f, 40f));
            Slider slider = NewSlider(settings.transform.Find("Window"), ui, knob);
            GameObject exit = NewPanel("Exit Panel", safe, ui, font, "QUIT GAME?", "", closeButtons, "NO");
            Button yes = NewTextButton("Yes Button", exit.transform.Find("Window"), ui, font, "YES", new Vector2(-150f, 70f), new Color(0.75f, 0.12f, 0.2f, 0.95f));
            closeButtons[closeButtons.Count - 1].GetComponent<RectTransform>().anchoredPosition = new Vector2(150f, 70f);

            // ---- Loading screen (its own canvas root; kept across the scene change for the fade into the game)
            Canvas loadCanvas = NewCanvas("Loading Screen", 100);
            RectTransform loadRoot = (RectTransform)loadCanvas.transform;
            var loading = loadCanvas.gameObject.AddComponent<LoadingScreen>();
            var screen = NewRect("Screen", loadRoot);
            Stretch(screen);
            var screenGroup = screen.gameObject.AddComponent<CanvasGroup>();
            var screenBg = NewImage("Black", screen, ui, Color.black);
            Stretch(screenBg.rectTransform);
            ArtSlideshow slideshow = NewSlideshow(screen, artSet);
            NewAmbience(screen);
            // A dark strip at the bottom so the tip and the progress always read over the artwork (and over a character's legs).
            var loadShade = NewImage("Shade Bottom", screen, shadeUp, new Color(0f, 0f, 0f, 0.88f));
            loadShade.raycastTarget = false;
            Anchor(loadShade.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 330f), Vector2.zero);
            var loadSafe = NewRect("Safe Area", screen);
            Stretch(loadSafe);
            loadSafe.gameObject.AddComponent<SafeAreaFitter>();
            var tip = NewText("Tip", loadSafe, font, "", 40, TextAnchor.LowerLeft, new Color(1f, 1f, 1f, 0.92f));
            Anchor(tip.rectTransform, new Vector2(0f, 0f), new Vector2(0.72f, 0f), new Vector2(0f, 0f), new Vector2(0f, 110f), new Vector2(60f, 58f));
            tip.horizontalOverflow = HorizontalWrapMode.Wrap;
            tip.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            var barBack = NewImage("Progress Back", loadSafe, ui, new Color(1f, 1f, 1f, 0.18f));
            Anchor(barBack.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(-120f, 6f), new Vector2(0f, 34f));
            var fill = NewImage("Fill", barBack.rectTransform, ui, new Color(1f, 0.55f, 0.85f, 1f));
            Stretch(fill.rectTransform);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            var glint = NewImage("Glint", barBack.rectTransform, dotSprite, new Color(1f, 0.85f, 0.95f, 1f));
            Anchor(glint.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(34f, 34f), Vector2.zero);
            glint.raycastTarget = false;
            var percent = NewText("Percent", loadSafe, font, "0%", 40, TextAnchor.LowerRight, Color.white);
            Anchor(percent.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(200f, 60f), new Vector2(-150f, 56f));
            var loadingLabel = NewText("Loading Label", loadSafe, font, "LOADING", 40, TextAnchor.LowerRight, Color.white);
            Anchor(loadingLabel.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(260f, 60f), new Vector2(-260f, 56f));
            var spinner = NewImage("Spinner", loadSafe, ring, Color.white);
            spinner.raycastTarget = false;
            Anchor(spinner.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(64f, 64f), new Vector2(-96f, 88f));
            var black = NewImage("Fade", loadRoot, ui, new Color(0f, 0f, 0f, 0f));
            Stretch(black.rectTransform);

            // ---- References
            var lso = new SerializedObject(loading);
            lso.FindProperty("root").objectReferenceValue = loadCanvas.gameObject;
            lso.FindProperty("screenGroup").objectReferenceValue = screenGroup;
            lso.FindProperty("slideshow").objectReferenceValue = slideshow;
            lso.FindProperty("progressFill").objectReferenceValue = fill;
            lso.FindProperty("progressGlint").objectReferenceValue = glint;
            lso.FindProperty("percentText").objectReferenceValue = percent;
            lso.FindProperty("spinner").objectReferenceValue = spinner.rectTransform;
            lso.FindProperty("tipText").objectReferenceValue = tip;
            lso.FindProperty("blackFade").objectReferenceValue = black;
            var tipsProp = lso.FindProperty("tips");
            tipsProp.arraySize = Tips.Length;
            for (int i = 0; i < Tips.Length; i++) tipsProp.GetArrayElementAtIndex(i).stringValue = Tips[i];
            lso.FindProperty("gameScene").stringValue = Path.GetFileNameWithoutExtension(GameScene);
            lso.ApplyModifiedPropertiesWithoutUndo();

            var mso = new SerializedObject(controller);
            mso.FindProperty("video").objectReferenceValue = video;
            mso.FindProperty("videoImage").objectReferenceValue = videoImage;
            mso.FindProperty("videoFitter").objectReferenceValue = videoFitter;
            mso.FindProperty("music").objectReferenceValue = music;
            mso.FindProperty("enterGameButton").objectReferenceValue = enter;
            mso.FindProperty("policyButton").objectReferenceValue = iconButtons[0];
            mso.FindProperty("shareButton").objectReferenceValue = iconButtons[1];
            mso.FindProperty("settingsButton").objectReferenceValue = iconButtons[2];
            mso.FindProperty("exitButton").objectReferenceValue = iconButtons[3];
            mso.FindProperty("policyPanel").objectReferenceValue = policy;
            mso.FindProperty("sharePanel").objectReferenceValue = share;
            mso.FindProperty("settingsPanel").objectReferenceValue = settings;
            mso.FindProperty("exitPanel").objectReferenceValue = exit;
            var closes = mso.FindProperty("closeButtons");
            closes.arraySize = closeButtons.Count;
            for (int i = 0; i < closeButtons.Count; i++) closes.GetArrayElementAtIndex(i).objectReferenceValue = closeButtons[i];
            mso.FindProperty("quitConfirmButton").objectReferenceValue = yes;
            mso.FindProperty("musicSlider").objectReferenceValue = slider;
            mso.FindProperty("loadingScreen").objectReferenceValue = loading;
            mso.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            log.AppendLine("  scene saved: " + ScenePath);
        }

        private static SplashArtSet MakeArtSet(List<Sprite> still, List<Sprite> cutouts, List<Sprite> full)
        {
            EnsureFolder(ArtSetFolder);
            var set = AssetDatabase.LoadAssetAtPath<SplashArtSet>(ArtSetPath);
            if (set == null) { set = ScriptableObject.CreateInstance<SplashArtSet>(); AssetDatabase.CreateAsset(set, ArtSetPath); }
            set.stillBackgrounds = still.ToArray();
            set.characters = cutouts.ToArray();
            set.fullArtworks = full.ToArray();
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssetIfDirty(set);
            return set;
        }

        // Two full-screen slide views (background in an envelope-fitted art root, a character image) and the slideshow on their parent.
        private static ArtSlideshow NewSlideshow(RectTransform parent, SplashArtSet artSet)
        {
            var host = NewRect("Slides", parent);
            Stretch(host);
            var show = host.gameObject.AddComponent<ArtSlideshow>();
            var so = new SerializedObject(show);
            // Reloaded by path: opening a new scene unloads assets nothing references yet, which leaves a stale (destroyed) object.
            if (artSet == null) artSet = AssetDatabase.LoadAssetAtPath<SplashArtSet>(ArtSetPath);
            so.FindProperty("art").objectReferenceValue = artSet;
            var viewsProp = so.FindProperty("views");
            viewsProp.arraySize = 2;
            for (int i = 0; i < 2; i++)
            {
                var slide = NewRect(i == 0 ? "Slide A" : "Slide B", host);
                Stretch(slide);
                var group = slide.gameObject.AddComponent<CanvasGroup>();
                group.blocksRaycasts = false;
                group.alpha = 0f;
                var art = NewRect("Art", slide);
                Stretch(art);
                var bgGo = new GameObject("Background", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
                bgGo.transform.SetParent(art, false);
                var bgImage = bgGo.GetComponent<Image>();
                bgImage.raycastTarget = false;
                var fitter = bgGo.GetComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = 16f / 9f;
                CenterAnchors((RectTransform)bgGo.transform);
                var character = NewImage("Character", slide, null, Color.white);
                character.preserveAspect = true;
                character.raycastTarget = false;
                var v = viewsProp.GetArrayElementAtIndex(i);
                v.FindPropertyRelative("group").objectReferenceValue = group;
                v.FindPropertyRelative("artRoot").objectReferenceValue = art;
                v.FindPropertyRelative("background").objectReferenceValue = bgImage;
                v.FindPropertyRelative("backgroundFitter").objectReferenceValue = fitter;
                v.FindPropertyRelative("character").objectReferenceValue = character;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return show;
        }

        // A vertical ramp (white, alpha 1 at the bottom -> 0 at the top), tinted dark for bottom strips.
        private static Sprite MakeVerticalGradient()
        {
            string path = ArtFolder + "/GradientUp.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
            {
                var t = new Texture2D(4, 256, TextureFormat.RGBA32, false);
                for (int y = 0; y < 256; y++)
                {
                    float a = 1f - y / 255f;
                    a = a * a * (3f - 2f * a);
                    for (int x = 0; x < 4; x++) t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
            }
            SpriteImport(path);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // The splash scene: a random artwork (the same slides as the loading screen), the title, tap to skip, a black fade.
        private static void BuildSplash(Font font, Sprite shadeUp, SplashArtSet artSet, StringBuilder log)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Sprite ui = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
            camGo.AddComponent<AudioListener>();
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
            Canvas canvas = NewCanvas("Splash Canvas", 200);
            RectTransform root = (RectTransform)canvas.transform;
            // The controller lives on the canvas root: that object is kept across the scene change to reveal the menu, so its
            // coroutine must run there (a controller in the splash scene would be destroyed with it, leaving the screen black).
            var controller = canvas.gameObject.AddComponent<SplashController>();
            var bg = NewImage("Black", root, ui, Color.black);
            Stretch(bg.rectTransform);
            bg.raycastTarget = false;
            ArtSlideshow slideshow = NewSlideshow(root, artSet);
            NewAmbience(root);
            var shade = NewImage("Shade Bottom", root, shadeUp, new Color(0f, 0f, 0f, 0.8f));
            shade.raycastTarget = false;
            Anchor(shade.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 460f), Vector2.zero);

            var safe = NewRect("Safe Area", root);
            Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();
            var content = NewRect("Content", safe);
            Stretch(content);
            var group = content.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            var titleRoot = NewRect("Title", content);
            Anchor(titleRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(1400f, 300f), new Vector2(0f, 110f));
            var title = NewText("Name", titleRoot, font, Title, 200, TextAnchor.UpperCenter, Color.white);
            Stretch(title.rectTransform);
            var tShadow = title.gameObject.AddComponent<Shadow>();
            tShadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
            tShadow.effectDistance = new Vector2(6f, -6f);
            var tagline = NewText("Tagline", titleRoot, font, Tagline, 56, TextAnchor.LowerCenter, new Color(1f, 0.62f, 0.88f, 1f));
            Stretch(tagline.rectTransform);
            tagline.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            var tap = NewText("Tap", content, font, "TAP TO CONTINUE", 34, TextAnchor.LowerRight, new Color(1f, 1f, 1f, 0.7f));
            Anchor(tap.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(400f, 50f), new Vector2(-50f, 40f));

            var skipImage = NewImage("Skip", root, ui, new Color(0f, 0f, 0f, 0f));
            Stretch(skipImage.rectTransform);
            var skip = skipImage.gameObject.AddComponent<Button>();
            skip.transition = Selectable.Transition.None;
            var black = NewImage("Fade", root, ui, Color.black);
            Stretch(black.rectTransform);
            black.raycastTarget = false;

            var so = new SerializedObject(controller);
            so.FindProperty("root").objectReferenceValue = canvas.gameObject;
            so.FindProperty("slideshow").objectReferenceValue = slideshow;
            so.FindProperty("content").objectReferenceValue = group;
            so.FindProperty("title").objectReferenceValue = titleRoot;
            so.FindProperty("blackFade").objectReferenceValue = black;
            so.FindProperty("skipButton").objectReferenceValue = skip;
            so.FindProperty("menuScene").stringValue = Path.GetFileNameWithoutExtension(ScenePath);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene, SplashScenePath);
            log.AppendLine("  scene saved: " + SplashScenePath);
        }

        // ---- Ambience (light sweep, breathing vignette, lens dust) and its generated soft textures.
        private static Sprite vignetteSprite, bandSprite, dotSprite;

        private static void MakeSoftTextures()
        {
            vignetteSprite = MakeTexture("Vignette.png", 256, 256, (u, v) =>
            {
                float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f;
                float d = Mathf.Sqrt(dx * dx * 0.8f + dy * dy * 1.1f);
                float a = Mathf.Clamp01((d - 0.55f) / 0.75f);
                return a * a * (3f - 2f * a);
            });
            bandSprite = MakeTexture("LightBand.png", 128, 4, (u, v) => Mathf.Pow(Mathf.Sin(u * Mathf.PI), 3f));
            dotSprite = MakeTexture("SoftDot.png", 64, 64, (u, v) =>
            {
                float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                return a * a;
            });
        }

        private static Sprite MakeTexture(string file, int w, int h, System.Func<float, float, float> alpha)
        {
            string path = ArtFolder + "/" + file;
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
            {
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        t.SetPixel(x, y, new Color(1f, 1f, 1f, alpha((x + 0.5f) / w, (y + 0.5f) / h)));
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
            }
            SpriteImport(path);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static LoadingAmbience NewAmbience(RectTransform parent)
        {
            var host = NewRect("Ambience", parent);
            Stretch(host);
            var vignette = NewImage("Vignette", host, vignetteSprite, new Color(0f, 0f, 0f, 0.5f));
            Stretch(vignette.rectTransform);
            vignette.raycastTarget = false;
            var sweep = NewImage("Light Sweep", host, bandSprite, new Color(1f, 0.92f, 0.85f, 0f));
            Anchor(sweep.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(520f, 600f), Vector2.zero);
            sweep.rectTransform.localEulerAngles = new Vector3(0f, 0f, -18f);
            sweep.raycastTarget = false;
            var motes = new Image[14];
            for (int i = 0; i < motes.Length; i++)
            {
                motes[i] = NewImage("Mote", host, dotSprite, new Color(1f, 0.9f, 0.95f, 0f));
                motes[i].raycastTarget = false;
                CenterAnchors(motes[i].rectTransform);
            }
            var amb = host.gameObject.AddComponent<LoadingAmbience>();
            var so = new SerializedObject(amb);
            so.FindProperty("area").objectReferenceValue = host;
            so.FindProperty("sweep").objectReferenceValue = sweep;
            so.FindProperty("vignette").objectReferenceValue = vignette;
            var arr = so.FindProperty("motes");
            arr.arraySize = motes.Length;
            for (int i = 0; i < motes.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = motes[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return amb;
        }

        private static void UpdateBuildList(StringBuilder log)
        {
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath && s.path != SplashScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(SplashScenePath, true));
            list.Insert(1, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            log.AppendLine("  build list: " + string.Join(" | ", list.Select((s, i) => $"{i}:{Path.GetFileNameWithoutExtension(s.path)}{(s.enabled ? "" : " (off)")}")));
        }

        // ------------------------------------------------------------------------------------------------ UI helpers

        private static Canvas NewCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private static GameObject NewPanel(string name, RectTransform parent, Sprite ui, Font font, string title, string body,
            List<Button> closeButtons, string closeLabel = "CLOSE")
        {
            var root = NewRect(name, parent);
            Stretch(root);
            var dim = NewImage("Dim", root, ui, new Color(0f, 0f, 0f, 0.6f));
            Stretch(dim.rectTransform);
            var window = NewImage("Window", root, ui, new Color(0.05f, 0.04f, 0.08f, 0.94f));
            window.rectTransform.sizeDelta = new Vector2(900f, 520f);
            window.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 0.55f, 0.85f, 0.6f);
            var t = NewText("Title", window.rectTransform, font, title, 70, TextAnchor.UpperCenter, Color.white);
            Anchor(t.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 100f), new Vector2(0f, -30f));
            if (!string.IsNullOrEmpty(body))
            {
                var b = NewText("Body", window.rectTransform, font, body, 40, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.85f));
                Anchor(b.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-120f, -280f), new Vector2(0f, 10f));
                b.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            closeButtons.Add(NewTextButton("Close Button", window.rectTransform, ui, font, closeLabel, new Vector2(0f, 70f), new Color(1f, 1f, 1f, 0.14f)));
            root.gameObject.SetActive(false);
            return root.gameObject;
        }

        private static Button NewTextButton(string name, Transform parent, Sprite ui, Font font, string label, Vector2 position, Color color)
        {
            var img = NewImage(name, parent, ui, color);
            Anchor(img.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(260f, 90f), position);
            var button = img.gameObject.AddComponent<Button>();
            img.gameObject.AddComponent<UIPressFeedback>();
            var text = NewText("Label", img.rectTransform, font, label, 50, TextAnchor.MiddleCenter, Color.white);
            Stretch(text.rectTransform);
            return button;
        }

        private static Slider NewSlider(Transform parent, Sprite ui, Sprite knob)
        {
            var root = NewRect("Music Slider", parent);
            Anchor(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(640f, 50f), new Vector2(0f, -40f));
            var back = NewImage("Background", root, ui, new Color(1f, 1f, 1f, 0.2f));
            Anchor(back.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 12f), Vector2.zero);
            var fillArea = NewRect("Fill Area", root);
            Anchor(fillArea, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-30f, 12f), Vector2.zero);
            var fill = NewImage("Fill", fillArea, ui, new Color(1f, 0.55f, 0.85f, 1f));
            fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = NewRect("Handle Slide Area", root);
            Anchor(handleArea, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-30f, 0f), Vector2.zero);
            var handle = NewImage("Handle", handleArea, knob, Color.white);
            handle.type = Image.Type.Simple;
            handle.preserveAspect = true;
            handle.rectTransform.sizeDelta = new Vector2(50f, 0f); // full height of the 50 px slider: a round knob
            var slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0.8f;
            return slider;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            // Rounded UI sprites keep their corners (9-slice) at any size.
            if (sprite != null && sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
            return img;
        }

        private static Text NewText(string name, Transform parent, Font font, string text, int size, TextAnchor align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static void CenterAnchors(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }

        private static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot, Vector2 size, Vector2 position)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
        }

        private static void SetRef(Object component, string property, Object value)
        {
            var so = new SerializedObject(component);
            so.FindProperty(property).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(SerializedProperty array, List<Sprite> sprites)
        {
            array.arraySize = sprites.Count;
            for (int i = 0; i < sprites.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
