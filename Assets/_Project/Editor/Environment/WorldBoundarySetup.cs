using System.Text;
using OpenworldCrime.Environment;
using OpenworldCrime.UI;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// The map's outer limit (re-runnable; menu Tools > OpenworldCrime > World > Set Up World Boundary):
    /// - measures the land (every active renderer in the scene except the sea plane, effects and sky beams: city, airport
    ///   island, harbour, props) and puts the soft limit Land Margin further out on every side, the invisible walls another
    ///   Hard Margin beyond that, the aircraft ceiling and the lid above (<see cref="WorldBoundary"/> on "World Boundary");
    /// - widens Hamza's <c>WorldBounds</c> area (scene value only; its script is not changed) to the hard limit, so its old
    ///   walls at the edge of the sea plane no longer stop anyone early and its sea / fall-out checks cover the whole area;
    /// - widens the WaterManager's ocean to the same area (so swimming and water landings work out to the walls; the sea
    ///   surface there is the OceanHorizon ring);
    /// - links the plane and the helicopters (turned / pushed back) and adds the HUD warning (<see cref="BoundaryWarningView"/>).
    /// </summary>
    public static class WorldBoundarySetup
    {
        private const string RootName = "World Boundary";
        /// <summary>Soft limit: this far (m) beyond the furthest land / props on every side.</summary>
        public const float LandMargin = 600f;
        /// <summary>Invisible walls: this far (m) beyond the soft limit.</summary>
        public const float HardMargin = 350f;
        public const float SoftCeiling = 700f;
        public const float HardCeiling = 900f;

        [MenuItem("Tools/OpenworldCrime/World/Set Up World Boundary")]
        public static void Menu() => Debug.Log(Run());

        public static string Run()
        {
            var log = new StringBuilder("[WorldBoundarySetup]\n");
            Scene scene = SceneManager.GetActiveScene();

            Bounds land = MeasureLand(scene, out int counted);
            Rect play = Rect.MinMaxRect(land.min.x - LandMargin, land.min.z - LandMargin, land.max.x + LandMargin, land.max.z + LandMargin);
            Rect hard = Rect.MinMaxRect(play.xMin - HardMargin, play.yMin - HardMargin, play.xMax + HardMargin, play.yMax + HardMargin);
            log.AppendLine($"  land ({counted} renderers): x {land.min.x:F0}..{land.max.x:F0}, z {land.min.z:F0}..{land.max.z:F0} ({land.size.x:F0} x {land.size.z:F0} m)");
            log.AppendLine($"  soft limit: x {play.xMin:F0}..{play.xMax:F0}, z {play.yMin:F0}..{play.yMax:F0} ({play.width:F0} x {play.height:F0} m), ceiling {SoftCeiling}");
            log.AppendLine($"  walls:      x {hard.xMin:F0}..{hard.xMax:F0}, z {hard.yMin:F0}..{hard.yMax:F0} ({hard.width:F0} x {hard.height:F0} m), lid {HardCeiling}");

            // --- boundary object
            GameObject root = ParachuteSetup.FindRoot(scene, RootName);
            if (root == null)
            {
                root = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(root, RootName);
            }
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var boundary = root.GetComponent<WorldBoundary>();
            if (boundary == null) boundary = Undo.AddComponent<WorldBoundary>(root);
            Undo.RecordObject(boundary, "World Boundary");
            boundary.EditorSetup(play, HardMargin, SoftCeiling, HardCeiling);
            EditorUtility.SetDirty(boundary);

            // --- Hamza's walls / sea checks: widened to the hard limit (scene value)
            var bounds = Object.FindAnyObjectByType<WorldBounds>(FindObjectsInactive.Include);
            Rect oldArea = bounds != null ? bounds.area : new Rect();
            if (bounds != null)
            {
                Undo.RecordObject(bounds, "World Bounds area");
                bounds.area = hard;
                EditorUtility.SetDirty(bounds);
                log.AppendLine($"  WorldBounds area {oldArea} -> {hard}");
            }
            else log.AppendLine("  (no WorldBounds in the scene)");

            // --- ocean to the walls
            var water = Object.FindAnyObjectByType<WaterManager>(FindObjectsInactive.Include);
            if (water != null)
            {
                var so = new SerializedObject(water);
                SerializedProperty bodies = so.FindProperty("bodies");
                for (int i = 0; i < bodies.arraySize; i++)
                {
                    SerializedProperty body = bodies.GetArrayElementAtIndex(i);
                    if (body.FindPropertyRelative("unbounded").boolValue) continue;
                    SerializedProperty area = body.FindPropertyRelative("area");
                    Rect r = area.rectValue;
                    // The sea body: the one that covered the old map area (bigger than a pool or lake).
                    if (r.width < 1000f || r.height < 1000f) continue;
                    Rect sea = Rect.MinMaxRect(hard.xMin - 20f, hard.yMin - 20f, hard.xMax + 20f, hard.yMax + 20f);
                    area.rectValue = sea;
                    log.AppendLine($"  water body '{body.FindPropertyRelative("name").stringValue}' {r} -> {sea}");
                }
                so.ApplyModifiedProperties();
            }

            // --- aircraft
            foreach (PlaneController plane in Resources.FindObjectsOfTypeAll<PlaneController>())
            {
                if (EditorUtility.IsPersistent(plane) || plane.gameObject.scene != scene) continue;
                Undo.RecordObject(plane, "Plane boundary");
                plane.EditorSetBoundary(boundary);
                var pso = new SerializedObject(plane);
                pso.FindProperty("invertPitch").boolValue = true;
                pso.ApplyModifiedProperties();
                EditorUtility.SetDirty(plane);
                log.AppendLine("  plane '" + plane.name + "' linked (invert pitch on)");
            }
            foreach (HelicopterController heli in Resources.FindObjectsOfTypeAll<HelicopterController>())
            {
                if (EditorUtility.IsPersistent(heli) || heli.gameObject.scene != scene) continue;
                Undo.RecordObject(heli, "Helicopter boundary");
                heli.EditorSetBoundary(boundary);
                EditorUtility.SetDirty(heli);
                PrefabUtility.RecordPrefabInstancePropertyModifications(heli);
                log.AppendLine("  helicopter '" + heli.name + "' linked");
            }

            log.AppendLine("  " + BuildWarning(scene));
            EditorSceneManager.MarkSceneDirty(scene);
            return log.ToString();
        }

        // Every active renderer except the sea plane / ocean ring, effects, sky beams and anything huge (sky, volumes).
        private static Bounds MeasureLand(Scene scene, out int counted)
        {
            counted = 0;
            var land = new Bounds();
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (r.gameObject.scene != scene || !r.enabled) continue;
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                if (r.name == "Water" || r.name == "Beam" || r.GetComponentInParent<OceanHorizon>() != null) continue;
                if (r.transform.root.name == RootName) continue;
                Bounds b = r.bounds;
                if (b.size.x > 2500f || b.size.z > 2500f) continue;
                if (counted == 0) land = b;
                else land.Encapsulate(b);
                counted++;
            }
            if (counted == 0) land = new Bounds(Vector3.zero, new Vector3(2000f, 100f, 2000f));
            return land;
        }

        // Top centre, under the flight instruments: a red bar with the warning (hidden until the player is out there).
        private static string BuildWarning(Scene scene)
        {
            GameObject gameplayUi = ParachuteSetup.FindRoot(scene, "Gameplay UI");
            if (gameplayUi == null) return "WARNING: Gameplay UI not found: no boundary warning";
            const string PanelName = "Boundary Warning";
            const string ViewName = "Boundary Warning View";
            Transform old = gameplayUi.transform.Find(PanelName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            Transform oldView = gameplayUi.transform.Find(ViewName);
            if (oldView != null) Undo.DestroyObjectImmediate(oldView.gameObject);

            Transform interact = gameplayUi.transform.Find("InteractPrompt");
            Text interactText = interact != null ? interact.GetComponentInChildren<Text>(true) : null;
            Font font = interactText != null ? interactText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var panel = new GameObject(PanelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(panel, PanelName);
            panel.layer = gameplayUi.layer;
            var rect = (RectTransform)panel.transform;
            rect.SetParent(gameplayUi.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(860f, 72f);
            rect.anchoredPosition = new Vector2(0f, -330f);
            var image = panel.GetComponent<Image>();
            image.color = new Color(0.75f, 0.05f, 0.05f, 0.72f);
            image.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textGo.layer = panel.layer;
            var textRect = (RectTransform)textGo.transform;
            textRect.SetParent(rect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            var text = textGo.GetComponent<Text>();
            text.font = font;
            text.fontSize = 36;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = "RESTRICTED AIRSPACE - TURN BACK";
            text.raycastTarget = false;
            panel.SetActive(false);

            var viewGo = new GameObject(ViewName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(viewGo, ViewName);
            viewGo.transform.SetParent(gameplayUi.transform, false);
            viewGo.AddComponent<BoundaryWarningView>().EditorSetup(panel, text);
            return "HUD: 'Boundary Warning' + view under Gameplay UI";
        }
    }
}
