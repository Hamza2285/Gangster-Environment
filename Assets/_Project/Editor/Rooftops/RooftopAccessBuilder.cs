using System.Text;
using OpenworldCrime.Interiors;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Rooftop access for every hivizcayne tower (re-runnable; menu Tools > OpenworldCrime > Rooftops > Build Rooftop Access).
    /// Per tower, under the scene root "Rooftop Access":
    /// - a street door on the front wall (frame, doors, a lit "roof" sign) with the shop-style <see cref="EntryMarker"/> and a
    ///   <see cref="RooftopDoor"/> ("Go to Roof") that takes the player up behind a fade;
    /// - on the roof a small stair-house with its own door, marker and <see cref="RooftopDoor"/> ("Go Down") back to the street;
    /// - stairs from the roof up to a jump platform that sticks out over the street front, with railings and a striped edge.
    /// Primitives with box colliders (the stairs walk on one hidden ramp collider, well inside Invector's slope limit).
    /// New objects are static for batching but not lightmapped: they take light probes until the next bake.
    /// </summary>
    public static class RooftopAccessBuilder
    {
        private const string RootName = "Rooftop Access";
        private const string ArtFolder = "Assets/_Project/Art/Rooftops";
        private const string MarkerPrefabPath = "Assets/_Project/Prefabs/Interiors/EntryMarker.prefab";
        private const string DoorClipPath = "Assets/AudioClip 1/DoorClose (mp3cut.net).ogg";

        // Stairs and platform (metres). The towers' street-side parapet stands 4.4 m above the roof (top at roof + 4.40, between
        // reference z -18.65 and -19.25), so the stairs climb 19 x 0.25 = 4.75 m and the platform crosses just above the wall.
        private const int StepCount = 19;
        private const float StepRise = 0.25f;
        private const float StepRun = 0.42f;
        private const float StairWidth = 3f;
        private const float PlatformLength = 8.4f;
        private const float RailHeight = 1.05f;

        private static Material concrete, trim, metal, door, rail, hazard, sign, glass;

        [MenuItem("Tools/OpenworldCrime/Rooftops/Build Rooftop Access")]
        public static void Menu() => Debug.Log(Run());

        public static string Run()
        {
            var log = new StringBuilder("[RooftopAccessBuilder]\n");
            Scene scene = SceneManager.GetActiveScene();
            BuildMaterials();
            var marker = AssetDatabase.LoadAssetAtPath<GameObject>(MarkerPrefabPath);
            if (marker == null) log.AppendLine("  WARNING: no EntryMarker prefab (run Interiors > Place Entry Markers once)");

            GameObject root = ParachuteSetup.FindRoot(scene, RootName);
            if (root != null) Undo.DestroyObjectImmediate(root);
            root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, RootName);

            foreach (Transform building in HivizcayneRoofLayout.FindBuildings())
                log.AppendLine("  " + BuildFor(building, root.transform, marker));

            EditorSceneManager.MarkSceneDirty(scene);
            return log.ToString();
        }

        private static void BuildMaterials()
        {
            ProceduralArt.EnsureFolder(ArtFolder);
            concrete = ProceduralArt.Lit(ArtFolder + "/RoofConcrete.mat", new Color(0.86f, 0.84f, 0.8f), null, 0.15f);
            trim = ProceduralArt.Lit(ArtFolder + "/RoofTrim.mat", new Color(0.62f, 0.6f, 0.57f), null, 0.2f);
            metal = ProceduralArt.Lit(ArtFolder + "/RoofMetal.mat", new Color(0.3f, 0.33f, 0.37f), null, 0.45f, 0.6f);
            door = ProceduralArt.Lit(ArtFolder + "/RoofDoor.mat", new Color(0.2f, 0.36f, 0.55f), null, 0.5f, 0.4f);
            rail = ProceduralArt.Lit(ArtFolder + "/RoofRail.mat", new Color(0.95f, 0.72f, 0.12f), null, 0.4f, 0.3f);
            glass = ProceduralArt.Lit(ArtFolder + "/RoofDoorGlass.mat", new Color(0.12f, 0.18f, 0.24f), null, 0.85f, 0.2f);
            Texture2D stripes = ProceduralArt.SaveTexture(ArtFolder + "/HazardStripes.png", 128, 128, (u, v) =>
                Mathf.Repeat((u + v) * 4f, 1f) < 0.5f ? new Color(0.95f, 0.75f, 0.1f) : new Color(0.08f, 0.08f, 0.08f), repeat: true, maxSize: 128);
            hazard = ProceduralArt.Lit(ArtFolder + "/RoofHazard.mat", Color.white, stripes, 0.3f);
            Texture2D signTex = ProceduralArt.Icon(ArtFolder + "/RoofSign.png", (x, y) =>
                Mathf.Max(ProceduralArt.Segment(x, y, new Vector2(0f, -0.5f), new Vector2(0f, 0.45f), 0.09f),
                Mathf.Max(ProceduralArt.Segment(x, y, new Vector2(0f, 0.5f), new Vector2(-0.35f, 0.12f), 0.09f),
                          ProceduralArt.Segment(x, y, new Vector2(0f, 0.5f), new Vector2(0.35f, 0.12f), 0.09f))),
                new Color(0.1f, 0.45f, 0.95f, 1f), 128);
            sign = ProceduralArt.Unlit(ArtFolder + "/RoofSign.mat", Color.white, signTex, true);
        }

        private static string BuildFor(Transform building, Transform parent, GameObject marker)
        {
            Collider col = HivizcayneRoofLayout.FindCollider(building);
            Quaternion yaw = HivizcayneRoofLayout.Yaw(building);
            var group = new GameObject("Rooftop Access - " + building.name).transform;
            group.SetParent(parent, false);
            group.SetPositionAndRotation(building.position, yaw);

            // --- roof
            Vector3 housePos = HivizcayneRoofLayout.ToWorld(building, HivizcayneRoofLayout.StairHouse);
            float roofY = HivizcayneRoofLayout.SurfaceY(col, housePos);
            if (float.IsNaN(roofY)) return building.name + ": no roof found, skipped";
            Vector3 east = yaw * Vector3.right;    // reference +x: along the roof
            Vector3 south = yaw * Vector3.back;    // reference -z: towards the street front
            Transform roof = new GameObject("Roof").transform;
            roof.SetParent(group, false);

            // Stair-house (door on its +x face).
            Transform house = new GameObject("Stair House").transform;
            house.SetParent(roof, false);
            house.SetPositionAndRotation(new Vector3(housePos.x, roofY, housePos.z), yaw);
            Box(house, "Walls", new Vector3(0f, 1.55f, 0f), new Vector3(4f, 3.1f, 4.6f), concrete, true);
            Box(house, "Roof Slab", new Vector3(0f, 3.2f, 0f), new Vector3(4.5f, 0.22f, 5.1f), trim, true);
            Box(house, "Door Frame", new Vector3(2.02f, 1.25f, 0f), new Vector3(0.08f, 2.5f, 1.6f), metal, false);
            Box(house, "Door", new Vector3(2.07f, 1.12f, 0f), new Vector3(0.06f, 2.2f, 1.25f), door, false);
            Box(house, "Door Window", new Vector3(2.1f, 1.7f, 0f), new Vector3(0.02f, 0.55f, 0.7f), glass, false);
            Box(house, "Door Handle", new Vector3(2.13f, 1.1f, -0.45f), new Vector3(0.05f, 0.05f, 0.22f), metal, false);
            Card(house, "Sign", new Vector3(2.08f, 2.75f, 0f), Quaternion.Euler(0f, 90f, 0f), 0.6f, sign);
            Box(house, "Vent", new Vector3(-0.8f, 3.55f, 1.2f), new Vector3(0.7f, 0.5f, 0.7f), metal, false);

            Vector3 roofDoorPos = HivizcayneRoofLayout.ToWorld(building, HivizcayneRoofLayout.RoofDoor);
            Vector3 arrivalPos = HivizcayneRoofLayout.ToWorld(building, HivizcayneRoofLayout.RoofArrival);
            float arrivalY = HivizcayneRoofLayout.SurfaceY(col, arrivalPos);
            Transform roofArrival = Point(roof, "Roof Arrival", new Vector3(arrivalPos.x, float.IsNaN(arrivalY) ? roofY : arrivalY, arrivalPos.z), Quaternion.LookRotation(east));

            // --- street
            float baseY = building.position.y;
            float doorX = PickStreetDoorX(building, baseY);
            Vector3 wall = HivizcayneRoofLayout.ToWorld(building, new Vector2(doorX, HivizcayneRoofLayout.StreetWallZ));
            float groundY = HivizcayneRoofLayout.GroundY(wall + south * 1.6f, baseY);
            Transform street = new GameObject("Street Door").transform;
            street.SetParent(group, false);
            street.SetPositionAndRotation(new Vector3(wall.x, groundY, wall.z), Quaternion.LookRotation(south));
            Box(street, "Frame", new Vector3(0f, 1.55f, 0.06f), new Vector3(2.6f, 3.1f, 0.3f), trim, false);
            Box(street, "Door Left", new Vector3(-0.56f, 1.27f, 0.22f), new Vector3(1.08f, 2.5f, 0.06f), door, false);
            Box(street, "Door Right", new Vector3(0.56f, 1.27f, 0.22f), new Vector3(1.08f, 2.5f, 0.06f), door, false);
            Box(street, "Glass Left", new Vector3(-0.56f, 1.6f, 0.26f), new Vector3(0.7f, 1.2f, 0.02f), glass, false);
            Box(street, "Glass Right", new Vector3(0.56f, 1.6f, 0.26f), new Vector3(0.7f, 1.2f, 0.02f), glass, false);
            Box(street, "Canopy", new Vector3(0f, 3.25f, 0.55f), new Vector3(3.2f, 0.14f, 1.2f), metal, false);
            Card(street, "Sign", new Vector3(0f, 2.75f, 0.27f), Quaternion.Euler(0f, 180f, 0f), 0.55f, sign);
            Transform streetArrival = Point(street, "Street Arrival", street.position + south * 3.3f, Quaternion.LookRotation(south));

            // Doors (trigger + marker) both ways.
            Door(street, "Go to Roof", street.position + south * 1.7f, "Go to Roof", roofArrival, marker);
            Door(roof, "Go Down", new Vector3(roofDoorPos.x, roofY, roofDoorPos.z), "Go Down", streetArrival, marker);

            // --- stairs and jump platform
            BuildJumpStairs(building, col, roof, yaw, south);

            SetStatic(group.gameObject);
            return $"{building.name}: roof {roofY:F1} m, street door at ({street.position.x:F1}, {groundY:F1}, {street.position.z:F1})";
        }

        // Stairs climb from the roof towards the street front onto a platform that sticks out past the facade.
        private static void BuildJumpStairs(Transform building, Collider col, Transform roof, Quaternion yaw, Vector3 south)
        {
            Vector3 foot = HivizcayneRoofLayout.ToWorld(building, HivizcayneRoofLayout.StairsFoot);
            float footY = HivizcayneRoofLayout.SurfaceY(col, foot);
            Transform stairs = new GameObject("Jump Stairs").transform;
            stairs.SetParent(roof, false);
            // Local frame: +z = up the stairs (towards the street), x across.
            stairs.SetPositionAndRotation(new Vector3(foot.x, footY, foot.z), Quaternion.LookRotation(south));

            float rise = StepCount * StepRise;
            float run = StepCount * StepRun;
            for (int i = 0; i < StepCount; i++)
            {
                float top = (i + 1) * StepRise;
                Box(stairs, "Step " + (i + 1), new Vector3(0f, top * 0.5f, i * StepRun + StepRun * 0.5f), new Vector3(StairWidth, top, StepRun), i % 2 == 0 ? concrete : trim, false);
            }
            // One hidden ramp under the step noses: smooth walking up and down.
            float slope = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            float length = Mathf.Sqrt(rise * rise + run * run);
            var ramp = new GameObject("Stairs Ramp Collider");
            ramp.transform.SetParent(stairs, false);
            ramp.transform.localRotation = Quaternion.Euler(-slope, 0f, 0f);
            ramp.transform.localPosition = new Vector3(0f, rise * 0.5f, run * 0.5f) + ramp.transform.localRotation * new Vector3(0f, -0.1f, 0f);
            var rampCol = ramp.AddComponent<BoxCollider>();
            rampCol.size = new Vector3(StairWidth, 0.2f, length + 0.3f);

            // Platform.
            float platformY = rise;
            float platformZ = run + PlatformLength * 0.5f;
            Box(stairs, "Platform", new Vector3(0f, platformY - 0.15f, platformZ), new Vector3(StairWidth + 0.4f, 0.3f, PlatformLength), concrete, true);
            Box(stairs, "Platform Edge", new Vector3(0f, platformY + 0.005f, run + PlatformLength - 0.45f), new Vector3(StairWidth + 0.4f, 0.02f, 0.9f), hazard, false);
            Box(stairs, "Platform Beam", new Vector3(0f, platformY - 0.5f, platformZ), new Vector3(0.6f, 0.4f, PlatformLength), metal, false);
            // Supports down to the roof under the part above it (between the stair top and the parapet, which carries the rest).
            for (int i = 0; i < 2; i++)
            {
                float z = run + 0.3f + i * 1.1f;
                Box(stairs, "Support " + (i + 1), new Vector3(-StairWidth * 0.5f + 0.2f, platformY * 0.5f - 0.15f, z), new Vector3(0.25f, platformY - 0.3f, 0.25f), metal, true);
                Box(stairs, "Support " + (i + 1) + "b", new Vector3(StairWidth * 0.5f - 0.2f, platformY * 0.5f - 0.15f, z), new Vector3(0.25f, platformY - 0.3f, 0.25f), metal, true);
            }
            // Railings along both sides (open at the jump end).
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (StairWidth * 0.5f + 0.1f);
                // Stair rail (sloped).
                var stairRail = Box(stairs, "Stair Rail " + (side < 0 ? "L" : "R"), Vector3.zero, new Vector3(0.08f, 0.08f, length), rail, false);
                stairRail.localRotation = Quaternion.Euler(-slope, 0f, 0f);
                stairRail.localPosition = new Vector3(x, rise * 0.5f + RailHeight, run * 0.5f);
                var stairRailCol = new GameObject("Stair Rail Collider " + (side < 0 ? "L" : "R"));
                stairRailCol.transform.SetParent(stairs, false);
                stairRailCol.transform.localRotation = stairRail.localRotation;
                stairRailCol.transform.localPosition = new Vector3(x, rise * 0.5f + RailHeight * 0.5f, run * 0.5f);
                stairRailCol.AddComponent<BoxCollider>().size = new Vector3(0.1f, RailHeight, length);
                // Platform rail.
                Box(stairs, "Platform Rail " + (side < 0 ? "L" : "R"), new Vector3(x, platformY + RailHeight, platformZ - 0.5f), new Vector3(0.08f, 0.08f, PlatformLength - 1f), rail, false);
                var platformRailCol = new GameObject("Platform Rail Collider " + (side < 0 ? "L" : "R"));
                platformRailCol.transform.SetParent(stairs, false);
                platformRailCol.transform.localPosition = new Vector3(x, platformY + RailHeight * 0.5f, platformZ - 0.5f);
                platformRailCol.AddComponent<BoxCollider>().size = new Vector3(0.1f, RailHeight, PlatformLength - 1f);
                for (int i = 0; i <= 4; i++)
                {
                    float z = run + (PlatformLength - 1f) * i / 4f;
                    Box(stairs, "Post " + side + "_" + i, new Vector3(x, platformY + RailHeight * 0.5f, z), new Vector3(0.07f, RailHeight, 0.07f), rail, false);
                }
                for (int i = 0; i <= 4; i++)
                {
                    float z = run * i / 4f;
                    float y = rise * i / 4f;
                    Box(stairs, "Stair Post " + side + "_" + i, new Vector3(x, y + RailHeight * 0.5f, z), new Vector3(0.07f, RailHeight, 0.07f), rail, false);
                }
            }
        }

        private static void Door(Transform parent, string name, Vector3 position, string prompt, Transform destination, GameObject marker)
        {
            var go = new GameObject(name) { layer = 2 }; // Ignore Raycast, like the shop door triggers
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.identity);
            var trigger = go.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(2.2f, 2f, 2.2f);
            trigger.center = new Vector3(0f, 1f, 0f);
            var door = go.AddComponent<RooftopDoor>();
            door.EditorSetup(prompt, destination, "");
            if (marker == null) return;
            var m = (GameObject)PrefabUtility.InstantiatePrefab(marker, parent.gameObject.scene);
            m.name = "Entry Marker";
            m.transform.SetParent(go.transform, false);
            m.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        }

        // The first candidate along the front wall with nothing standing in front of the door (billboards, lamps, benches).
        private static float PickStreetDoorX(Transform building, float baseY)
        {
            Quaternion yaw = HivizcayneRoofLayout.Yaw(building);
            foreach (float x in HivizcayneRoofLayout.StreetDoorXs)
            {
                Vector3 wall = HivizcayneRoofLayout.ToWorld(building, new Vector2(x, HivizcayneRoofLayout.StreetWallZ));
                Vector3 centre = wall + yaw * Vector3.back * 2.2f + Vector3.up * (baseY - wall.y + 1.4f);
                var hits = Physics.OverlapBox(centre, new Vector3(1.4f, 1.1f, 2.1f), yaw, ~(1 << 2), QueryTriggerInteraction.Ignore);
                bool clear = true;
                foreach (Collider hit in hits)
                {
                    if (hit.attachedRigidbody != null && !hit.attachedRigidbody.isKinematic) continue; // cars, people
                    clear = false;
                    break;
                }
                if (clear) return x;
            }
            return HivizcayneRoofLayout.StreetDoorXs[0];
        }

        // ---------------------------------------------------------------- primitives

        private static Transform Box(Transform parent, string name, Vector3 localPosition, Vector3 size, Material material, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            ProbeLit(renderer);
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go.transform;
        }

        private static void Card(Transform parent, string name, Vector3 localPosition, Quaternion localRotation, float size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = new Vector3(size, size, 1f);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static Transform Point(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            return go.transform;
        }

        /// <summary>Not in the baked lightmaps (built after the bake): lit by light probes until the next bake.</summary>
        public static void ProbeLit(Renderer renderer)
        {
            if (renderer is MeshRenderer meshRenderer) meshRenderer.receiveGI = ReceiveGI.LightProbes;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
        }

        /// <summary>Static for batching and occlusion, but not Contribute GI (so the current bake's look is kept).</summary>
        public static void SetStatic(GameObject root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponent<RooftopDoor>() != null || t.GetComponentInParent<EntryMarker>() != null) continue; // markers animate
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            }
        }
    }
}
