using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenworldCrime.Audio;
using OpenworldCrime.Controls;
using OpenworldCrime.Player;
using OpenworldCrime.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Car radio from the Assets/GTARadio folders (Tools > OpenworldCrime > Audio > Set Up Car Radio):
    /// - a station for every sub-folder that has songs (clips of 30 s or more; empty / missing folders give no station). Short
    ///   clips in a folder are that station's idents, except the tuning-noise clips ("radio-lines", "other-radio-stations"),
    ///   which become the shared station-switch sound.
    /// - station logos cut out of "Neon Radio Icons Grid.png" (read from the file; its import settings are not changed) into
    ///   Assets/_Project/Art/Radio, station assets into Assets/_Project/Data/Audio/Radio.
    /// - songs set to stream from disk (Load Type Streaming, Vorbis 50%, no preload): ~60 minutes of music would otherwise be
    ///   decompressed into memory at scene load. Only the import settings of the GTARadio songs change.
    /// - in the open scene: "Radio" under ///Manager/// (RadioManager + music / tuning AudioSources) and the radio bar at the
    ///   top centre of the gameplay HUD (3 looping station logos, prev / next arrows; see RadioStationView). Rebuilds them if
    ///   present, and removes the earlier pop-up / mobile radio button.
    /// </summary>
    public static class RadioSetup
    {
        private const string Source = "Assets/GTARadio";
        private const string Grid = Source + "/Neon Radio Icons Grid.png";
        private const string ArtFolder = "Assets/_Project/Art/Radio";
        private const string DataFolder = "Assets/_Project/Data/Audio/Radio";
        private const float SongMinSeconds = 30f;

        // Folder -> name shown on the HUD, and the logo's cell in the 5 x 2 icon grid (column, row from the top).
        private static readonly Dictionary<string, KeyValuePair<string, Vector2Int>> Known = new Dictionary<string, KeyValuePair<string, Vector2Int>>
        {
            { "LosSantos", new KeyValuePair<string, Vector2Int>("Los Santos Rock Radio", new Vector2Int(0, 0)) },
            { "VineWood", new KeyValuePair<string, Vector2Int>("Vinewood FM", new Vector2Int(1, 0)) },
            { "RebelRadio", new KeyValuePair<string, Vector2Int>("Rebel Radio", new Vector2Int(3, 0)) },
            { "KULT", new KeyValuePair<string, Vector2Int>("Kult FM", new Vector2Int(1, 1)) },
            { "TheFunkyRadio", new KeyValuePair<string, Vector2Int>("The Funky Radio", new Vector2Int(2, 1)) },
            { "WCTR", new KeyValuePair<string, Vector2Int>("WCTR Talk Radio", new Vector2Int(3, 1)) },
            { "Mirage", new KeyValuePair<string, Vector2Int>("Radio Mirage", new Vector2Int(4, 1)) },
        };

        [MenuItem("Tools/OpenworldCrime/Audio/Set Up Car Radio (city scene)")]
        public static void SetupMenu() => Debug.Log(Setup());

        public static string Setup()
        {
            var log = new StringBuilder("[RadioSetup]\n");
            if (EditorApplication.isPlaying) return log.Append("  stop play mode first").ToString();
            if (!AssetDatabase.IsValidFolder(Source)) return log.Append("  no " + Source).ToString();
            EnsureFolder(ArtFolder);
            EnsureFolder(DataFolder);

            Texture2D grid = LoadGrid();
            var stations = new List<RadioStation>();
            var tuning = new List<AudioClip>();
            int streamed = 0;
            foreach (string folder in AssetDatabase.GetSubFolders(Source))
            {
                string key = Path.GetFileName(folder);
                var songs = new List<AudioClip>();
                var idents = new List<AudioClip>();
                foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (clip == null) continue;
                    string file = Path.GetFileName(path).ToLowerInvariant();
                    if (clip.length >= SongMinSeconds) { if (MakeStreaming(path)) streamed++; songs.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(path)); }
                    else if (file.Contains("radio-lines") || file.Contains("other-radio-stations")) tuning.Add(clip);
                    else idents.Add(clip);
                }
                if (songs.Count == 0) { log.AppendLine("  " + key + ": no songs, skipped"); continue; }

                KeyValuePair<string, Vector2Int> info;
                bool known = Known.TryGetValue(key, out info);
                Sprite logo = known && grid != null ? CutLogo(grid, key, info.Value) : null;
                string assetPath = DataFolder + "/Radio_" + key + ".asset";
                var station = AssetDatabase.LoadAssetAtPath<RadioStation>(assetPath);
                if (station == null) { station = ScriptableObject.CreateInstance<RadioStation>(); AssetDatabase.CreateAsset(station, assetPath); }
                station.EditorSet(known ? info.Key : key, logo, songs.ToArray(), idents.ToArray());
                EditorUtility.SetDirty(station);
                stations.Add(station);
                log.AppendLine("  " + key + ": " + songs.Count + " songs, " + idents.Count + " idents" + (logo != null ? "" : " (no logo)"));
            }
            if (grid != null) Object.DestroyImmediate(grid);
            AssetDatabase.SaveAssets();
            log.AppendLine("  switch sounds: " + tuning.Count + "; songs set to streaming now: " + streamed);

            // Scene
            var ui = Object.FindFirstObjectByType<UIManager>(FindObjectsInactive.Include);
            if (ui == null) return log.Append("  no UIManager in the open scene").ToString();
            var scene = ui.gameObject.scene;
            GameObject managers = null;
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == "///Manager///") managers = root;
            var old = Object.FindFirstObjectByType<RadioManager>(FindObjectsInactive.Include);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("Radio");
            if (managers != null) go.transform.SetParent(managers.transform, false);
            else UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            AudioSource music = NewSource(go.transform, "Radio Music");
            AudioSource tune = NewSource(go.transform, "Radio Tuning");
            var radio = go.AddComponent<RadioManager>();
            var players = Object.FindObjectsByType<PlayerVehicleInteraction>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            radio.EditorSetup(stations.ToArray(), tuning.ToArray(), players, music, tune);
            log.AppendLine("  Radio under " + (managers != null ? "///Manager///" : "scene root") + ", listening to " + players.Length + " player(s)");

            // Radio bar on the gameplay HUD (replaces the earlier pop-up and the single mobile radio button, removed here).
            var so = new SerializedObject(ui);
            var hud = so.FindProperty("hudPanel").objectReferenceValue as GameObject;
            var fontSource = so.FindProperty("moneyText").objectReferenceValue as Text;
            foreach (RadioStationView v in Object.FindObjectsByType<RadioStationView>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(v.gameObject);
            var controls = Object.FindFirstObjectByType<ControlsModeManager>(FindObjectsInactive.Include);
            GameObject vehicleControls = controls != null ? new SerializedObject(controls).FindProperty("vehicleControls").objectReferenceValue as GameObject : null;
            Transform oldButton = vehicleControls != null ? vehicleControls.transform.Find("Radio") : null;
            if (oldButton != null) { Object.DestroyImmediate(oldButton.gameObject); log.AppendLine("  removed the old radio button from " + vehicleControls.name); }
            if (hud == null) return log.Append("  no HUD panel on UIManager: bar not built").ToString();

            Sprite pill = MakeSprite("RadioBarPill", 64, 64, 30, Pill);
            Sprite glow = MakeSprite("RadioGlow", 128, 128, 0, Glow);
            Sprite off = MakeSprite("RadioOff", 128, 128, 0, PowerIcon);
            Sprite chevron = MakeSprite("RadioChevron", 64, 64, 0, Chevron);

            var view = new GameObject("Radio HUD", typeof(RectTransform)).GetComponent<RectTransform>();
            view.SetParent(hud.transform, false);
            Stretch(view);
            var bar = new GameObject("Radio Bar", typeof(RectTransform)).GetComponent<RectTransform>();
            bar.SetParent(view, false);
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.anchoredPosition = new Vector2(0f, -16f);
            bar.sizeDelta = new Vector2(600f, 124f);
            var group = bar.gameObject.AddComponent<CanvasGroup>();
            var bg = bar.gameObject.AddComponent<Image>();
            bg.sprite = pill; bg.type = Image.Type.Sliced; bg.color = new Color(0.02f, 0.02f, 0.05f, 0.6f); bg.raycastTarget = true;

            var stationView = view.gameObject.AddComponent<RadioStationView>();
            Button prev = ArrowButton(bar, "Prev", pill, chevron, false);
            Button next = ArrowButton(bar, "Next", pill, chevron, true);
            UnityEventTools.AddVoidPersistentListener(prev.onClick, stationView.Previous);
            UnityEventTools.AddVoidPersistentListener(next.onClick, stationView.Next);

            var strip = new GameObject("Logos", typeof(RectTransform)).GetComponent<RectTransform>();
            strip.SetParent(bar, false);
            strip.anchorMin = strip.anchorMax = new Vector2(0.5f, 0.5f);
            strip.sizeDelta = new Vector2(400f, 124f);
            strip.gameObject.AddComponent<RectMask2D>();
            var halo = NewImage(strip, "Glow", Vector2.zero, new Vector2(150f, 150f), new Vector2(0.5f, 0.5f));
            halo.sprite = glow; halo.color = new Color(1f, 0.85f, 1f, 0.45f);
            var slots = new Image[5];
            for (int i = 0; i < 5; i++) slots[i] = NewImage(strip, "Slot " + (i - 2), Vector2.zero, new Vector2(72f, 72f), new Vector2(0.5f, 0.5f));

            var label = new GameObject("Station Name", typeof(RectTransform)).AddComponent<Text>();
            label.rectTransform.SetParent(bar, false);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            label.rectTransform.pivot = new Vector2(0.5f, 1f);
            label.rectTransform.anchoredPosition = new Vector2(0f, -2f);
            label.rectTransform.sizeDelta = new Vector2(520f, 30f);
            label.font = fontSource != null ? fontSource.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 20;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(1f, 1f, 1f, 0.9f);
            label.raycastTarget = false;
            label.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.75f);

            stationView.EditorSetup(radio, bar, group, slots, off, label, FindAvoidPanels());
            bar.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
            log.AppendLine("  radio bar on " + hud.name + " (top centre)");
            return log.ToString();
        }

        // Top-centre panels the bar steps below while they are shown (mission panel, the HUD notification line).
        private static RectTransform[] FindAvoidPanels()
        {
            var list = new List<RectTransform>();
            foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas) continue;
                if (c.name == "Ambulance Mission Canvas") { Transform t = FindDeep(c.transform, "Mission"); if (t != null) list.Add((RectTransform)t); }
                if (c.name == "Game HUD")
                    foreach (Transform t in c.transform)
                    {
                        var text = t.GetComponent<Text>();
                        var r = (RectTransform)t;
                        if (text != null && r.anchorMin.y >= 0.99f) list.Add(r); // the notification line at the top
                    }
            }
            return list.ToArray();
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static Button ArrowButton(RectTransform bar, string name, Sprite bgSprite, Sprite chevron, bool right)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(bar, false);
            r.anchorMin = r.anchorMax = new Vector2(right ? 1f : 0f, 0.5f);
            r.pivot = new Vector2(right ? 1f : 0f, 0.5f);
            r.anchoredPosition = new Vector2(right ? -12f : 12f, 0f);
            r.sizeDelta = new Vector2(84f, 100f); // tap target
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = bgSprite; img.type = Image.Type.Sliced; img.color = new Color(1f, 1f, 1f, 0.1f);
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            ColorBlock colors = b.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.colorMultiplier = 2.5f;
            b.colors = colors;
            var icon = NewImage(r, "Arrow", Vector2.zero, new Vector2(40f, 40f), new Vector2(0.5f, 0.5f));
            icon.sprite = chevron;
            icon.color = new Color(1f, 1f, 1f, 0.9f);
            icon.rectTransform.localScale = new Vector3(right ? 1f : -1f, 1f, 1f);
            return b;
        }

        // ---- generated sprites (small, white, tinted in the UI)

        private static Color Pill(float x, float y, int w, int h)
        {
            float r = w * 0.5f - 1f;
            float dx = Mathf.Max(0f, Mathf.Abs(x - w * 0.5f) - (w * 0.5f - r - 1f));
            float dy = Mathf.Max(0f, Mathf.Abs(y - h * 0.5f) - (h * 0.5f - r - 1f));
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            return new Color(1f, 1f, 1f, Mathf.Clamp01(r - d + 0.5f));
        }

        private static Color Glow(float x, float y, int w, int h)
        {
            float d = new Vector2(x - w * 0.5f, y - h * 0.5f).magnitude / (w * 0.5f);
            float a = Mathf.Clamp01(1f - d);
            return new Color(1f, 1f, 1f, a * a);
        }

        // A power symbol: a ring open at the top and a bar through the gap.
        private static Color PowerIcon(float x, float y, int w, int h)
        {
            Vector2 p = new Vector2(x - w * 0.5f, y - h * 0.5f);
            float ring = Mathf.Abs(p.magnitude - w * 0.3f);
            float angle = Mathf.Atan2(p.x, p.y) * Mathf.Rad2Deg; // 0 = up
            float a = ring < w * 0.045f && Mathf.Abs(angle) > 38f ? 1f : 0f;
            if (Mathf.Abs(p.x) < w * 0.045f && p.y > -w * 0.02f && p.y < w * 0.36f) a = 1f;
            return new Color(1f, 1f, 1f, a);
        }

        // A right-pointing chevron (two strokes meeting at the right).
        private static Color Chevron(float x, float y, int w, int h)
        {
            float u = x / w, v = y / h;
            float target = 0.68f - Mathf.Abs(v - 0.5f) * 0.75f; // x of the stroke at this height
            float a = Mathf.Abs(u - target) < 0.085f && Mathf.Abs(v - 0.5f) < 0.36f ? 1f : 0f;
            return new Color(1f, 1f, 1f, a);
        }

        private static Sprite MakeSprite(string name, int w, int h, int border, System.Func<float, float, int, int, Color> pixel)
        {
            string path = ArtFolder + "/" + name + ".png";
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // 4x supersampling for smooth edges
                    Color sum = Color.clear;
                    for (int s = 0; s < 4; s++) sum += pixel(x + 0.25f + (s % 2) * 0.5f, y + 0.25f + (s / 2) * 0.5f, w, h);
                    t.SetPixel(x, y, sum * 0.25f);
                }
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.spriteBorder = new Vector4(border, border, border, border);
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Texture2D LoadGrid()
        {
            if (!File.Exists(Grid)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            return t.LoadImage(File.ReadAllBytes(Grid)) ? t : null;
        }

        // The grid is 5 columns x 2 rows of logos (1536 x 1024); each logo is cut as a square around its measured centre
        // (fractions of the image, so a resized grid still works).
        private static readonly float[] LogoCentreX = { 0.117f, 0.317f, 0.507f, 0.700f, 0.898f };
        private static readonly float[] LogoCentreY = { 0.300f, 0.670f };

        private static Sprite CutLogo(Texture2D grid, string key, Vector2Int cell)
        {
            int size = Mathf.RoundToInt(grid.width * 0.205f);
            float cx = LogoCentreX[Mathf.Clamp(cell.x, 0, 4)] * grid.width;
            float cyTop = LogoCentreY[Mathf.Clamp(cell.y, 0, 1)] * grid.height;
            int x0 = Mathf.Clamp(Mathf.RoundToInt(cx - size * 0.5f), 0, grid.width - size);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(grid.height - cyTop - size * 0.5f), 0, grid.height - size);
            var cut = new Texture2D(size, size, TextureFormat.RGBA32, false);
            cut.SetPixels(grid.GetPixels(x0, y0, size, size));
            cut.Apply();
            string path = ArtFolder + "/RadioLogo_" + key + ".png";
            File.WriteAllBytes(path, cut.EncodeToPNG());
            Object.DestroyImmediate(cut);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.maxTextureSize = 256;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static bool MakeStreaming(string path)
        {
            var imp = (AudioImporter)AssetImporter.GetAtPath(path);
            AudioImporterSampleSettings s = imp.defaultSampleSettings;
            if (s.loadType == AudioClipLoadType.Streaming && Mathf.Approximately(s.quality, 0.5f) && !s.preloadAudioData && !imp.ContainsSampleSettingsOverride("Android")) return false;
            s.loadType = AudioClipLoadType.Streaming;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.5f;
            s.preloadAudioData = false;
            imp.defaultSampleSettings = s;
            imp.ClearSampleSettingOverride("Android");
            imp.loadInBackground = true;
            imp.SaveAndReimport();
            return true;
        }

        private static AudioSource NewSource(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.loop = false;
            a.spatialBlend = 0f;
            a.priority = 32;
            a.volume = 0f;
            return a;
        }

        private static Image NewImage(RectTransform parent, string name, Vector2 pos, Vector2 size, Vector2 anchor)
        {
            var img = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            img.rectTransform.SetParent(parent, false);
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = anchor;
            img.rectTransform.pivot = anchor;
            img.rectTransform.anchoredPosition = pos;
            img.rectTransform.sizeDelta = size;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
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
