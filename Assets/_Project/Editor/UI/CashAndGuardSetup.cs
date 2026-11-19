using System.IO;
using System.Text;
using OpenworldCrime.Economy;
using OpenworldCrime.Player;
using OpenworldCrime.Racing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// City scene setup for cash drops and the character switch guard (Tools > OpenworldCrime > Economy > Set Up Cash Drops):
    /// - CashPickup prefab: a small bundle of green notes with a paper band and a soft glow, on a trigger (Ignore Raycast layer);
    /// - ///Manager///: "Cash Drops" (CashDropManager, pickup sound) and "Character Switch Guard";
    /// - Gameplay UI: "+$X" popup under the money counter.
    /// </summary>
    public static class CashAndGuardSetup
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Pickups/CashPickup.prefab";
        private const string ArtFolder = "Assets/_Project/Art/Pickups";
        private const string Sound = "Assets/Invector-3rdPersonController/Basic Locomotion/Audio/Others/pickup.wav";

        [MenuItem("Tools/OpenworldCrime/Economy/Set Up Cash Drops and Switch Guard (city scene)")]
        public static void SetupMenu() => Debug.Log(Setup());

        public static string Setup()
        {
            var log = new StringBuilder("[CashAndGuardSetup]\n");
            EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
            EnsureFolder(ArtFolder);
            CashPickup prefab = BuildPickup(log);

            var scene = EditorSceneManager.GetActiveScene();
            GameObject managers = null, ui = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "///Manager///") managers = root;
                if (root.name == "Gameplay UI") ui = root;
            }
            if (managers == null || ui == null) { log.AppendLine("- scene not wired: needs ///Manager/// and Gameplay UI"); return log.ToString(); }

            // Cash drops.
            Transform dropsT = managers.transform.Find("Cash Drops") ?? new GameObject("Cash Drops").transform;
            dropsT.SetParent(managers.transform, false);
            var drops = GetOrAdd<CashDropManager>(dropsT.gameObject);
            var audio = GetOrAdd<AudioSource>(dropsT.gameObject);
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            audio.volume = 0.85f;
            var so = new SerializedObject(drops);
            so.FindProperty("pickupPrefab").objectReferenceValue = prefab;
            so.FindProperty("audioSource").objectReferenceValue = audio;
            so.FindProperty("pickupSound").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(Sound);
            so.ApplyModifiedPropertiesWithoutUndo();

            // Popup under the money counter.
            Transform money = ui.transform.Find("HUD/Money");
            Transform oldPopup = ui.transform.Find("HUD/Cash Popup");
            if (oldPopup != null) Object.DestroyImmediate(oldPopup.gameObject);
            if (money != null)
            {
                var src = (RectTransform)money;
                var go = new GameObject("Cash Popup", typeof(RectTransform));
                go.layer = money.gameObject.layer;
                var rt = (RectTransform)go.transform;
                rt.SetParent(money.parent, false);
                rt.anchorMin = src.anchorMin;
                rt.anchorMax = src.anchorMax;
                rt.pivot = src.pivot;
                rt.sizeDelta = new Vector2(Mathf.Max(src.sizeDelta.x, 200f), 36f);
                rt.anchoredPosition = src.anchoredPosition + new Vector2(0f, -38f);
                var text = go.AddComponent<Text>();
                Text moneyText = money.GetComponentInChildren<Text>(true);
                text.font = moneyText != null ? moneyText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = moneyText != null ? Mathf.RoundToInt(moneyText.fontSize * 0.85f) : 24;
                text.alignment = moneyText != null ? moneyText.alignment : TextAnchor.MiddleRight;
                text.fontStyle = FontStyle.Bold;
                text.color = new Color(0.55f, 1f, 0.5f, 1f);
                text.raycastTarget = false;
                go.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.6f);
                var view = go.AddComponent<CashPopupView>();
                var vso = new SerializedObject(view);
                vso.FindProperty("drops").objectReferenceValue = drops;
                vso.FindProperty("label").objectReferenceValue = text;
                vso.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine("- '+$X' popup under HUD/Money");
            }
            else log.AppendLine("- HUD/Money not found: no popup");

            // Character switch guard.
            Transform guardT = managers.transform.Find("Character Switch Guard") ?? new GameObject("Character Switch Guard").transform;
            guardT.SetParent(managers.transform, false);
            var guard = GetOrAdd<CharacterSwitchGuard>(guardT.gameObject);
            var gso = new SerializedObject(guard);
            gso.FindProperty("switcher").objectReferenceValue = Object.FindFirstObjectByType<CharacterSwitcher>(FindObjectsInactive.Include);
            gso.FindProperty("race").objectReferenceValue = Object.FindFirstObjectByType<RaceManager>(FindObjectsInactive.Include);
            gso.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            log.AppendLine("- ///Manager///: Cash Drops, Character Switch Guard");
            return log.ToString();
        }

        private static CashPickup BuildPickup(StringBuilder log)
        {
            Material notes = Mat(ArtFolder + "/CashNotes.mat", new Color(0.32f, 0.62f, 0.3f), new Color(0.1f, 0.45f, 0.12f));
            Material band = Mat(ArtFolder + "/CashBand.mat", new Color(0.92f, 0.88f, 0.75f), Color.black);
            var root = new GameObject("CashPickup") { layer = 2 };
            var trigger = root.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 1.1f;
            trigger.center = new Vector3(0f, 0.5f, 0f);
            var model = new GameObject("Model");
            model.layer = 2;
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            for (int i = 0; i < 3; i++)
            {
                GameObject stack = Box("Notes " + i, model.transform, new Vector3(0.02f * i, 0.035f * i, -0.015f * i), new Vector3(0.3f, 0.032f, 0.14f), notes);
                stack.transform.localRotation = Quaternion.Euler(0f, i * 9f - 9f, 0f);
            }
            Box("Band", model.transform, new Vector3(0.02f, 0.035f, -0.015f), new Vector3(0.06f, 0.11f, 0.15f), band);
            var pickup = root.AddComponent<CashPickup>();
            var so = new SerializedObject(pickup);
            so.FindProperty("model").objectReferenceValue = model.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            log.AppendLine("- prefab " + PrefabPath);
            return saved.GetComponent<CashPickup>();
        }

        private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = 2;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        private static Material Mat(string path, Color colour, Color emission)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", colour);
            m.SetFloat("_Smoothness", 0.25f);
            if (emission.maxColorComponent > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // Explicit null check: in the editor GetComponent returns a "fake null" for missing built-in components, which ?? misses.
        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }
}
