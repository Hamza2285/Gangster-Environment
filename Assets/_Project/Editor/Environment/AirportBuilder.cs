using System.Collections.Generic;
using System.Text;
using OpenworldCrime.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Small airport on its own island at "AirportPoint" (west of the city, in the sea; re-runnable; menu
    /// Tools > OpenworldCrime > Airport > Build Airport). Everything goes under the scene root "Airport":
    /// - the island (one box collider, top at the city's street height, sea wall sides) with a grass top;
    /// - a 650 m runway with centre line, edge lines, threshold bars and "18" / "36" numbers, a parallel taxiway with three
    ///   links, a concrete apron, a helipad (with a second helicopter);
    /// - two hangars assembled from the imported HQ Hangar parts (URP prefab variants under Assets/_Project/Prefabs/Airport),
    ///   a terminal and a control tower from primitives, a windsock (the flyable jet, the scene's Embraer Phenom 100, is set
    ///   up on the apron by PlaneSetup, outside the "Airport" root, so a rebuild leaves it alone);
    /// - the landside: a causeway road from the beach (where the coastal road is) to the terminal car park, fences with a gate;
    /// - the imported Street Props (cones, barrels, water barricades, pedestrian barriers, wheel stops, panels) as LOD prefabs;
    /// - runway / taxiway lights as unlit emissive cards (no realtime lights), signs, a map blip.
    /// Paved surfaces are thin visual meshes over the island's collider, so the whole airport is one flat drivable ground.
    /// Static (batching / occlusion) but not lightmapped: lit by light probes until the next bake.
    /// </summary>
    public static class AirportBuilder
    {
        private const string RootName = "Airport";
        private const string ArtFolder = "Assets/_Project/Art/Airport";
        private const string MeshFolder = ArtFolder + "/Meshes";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Airport";
        private const string HangarSource = "Assets/HQ Hangar Free/Prefabs";
        private const string HangarMaterials = "Assets/HQ Hangar Free/Materials";
        private const string PropsSource = "Assets/Prototype Collection/Assets";
        private const string HelicopterPrefab = "Assets/_Project/Prefabs/Vehicles/Helicopter/Helicopter.prefab";
        private const string CarPrefab = "Assets/_Project/Prefabs/Vehicles/Drivable/Drivable_Car3.prefab";

        /// <summary>Street height of the city (the airport ground is level with it).</summary>
        private const float GroundY = 27.75f;
        private const float SeaFloorY = 12f;

        // Layout (world x / z). Island centred on AirportPoint (-612.5, -134.1).
        private static readonly Rect Island = Rect.MinMaxRect(-765f, -525f, -468f, 255f);
        private const float RunwayX = -705f, RunwayWidth = 45f, RunwayZMin = -465f, RunwayZMax = 195f;
        private const float TaxiwayX = -652f, TaxiwayWidth = 20f;
        private static readonly Rect Apron = Rect.MinMaxRect(-642f, -320f, -522f, 60f);
        private const float RoadZ = -134f;
        private const float BeachRoadEndX = -334f;

        private static Material grass, asphalt, concrete, seawall, white, yellow, black, wall, glass, roof, metal, red, orange, fence,
            lightWhite, lightGreen, lightRed, lightBlue, lightAmber, signYellow;
        private static readonly List<GameObject> staticRoots = new List<GameObject>();

        [MenuItem("Tools/OpenworldCrime/Airport/Build Airport")]
        public static void Menu() => Debug.Log(Run());

        public static string Run()
        {
            var log = new StringBuilder("[AirportBuilder]\n");
            Scene scene = SceneManager.GetActiveScene();
            GameObject point = GameObject.Find("env/AirportPoint");
            if (point == null) return "[AirportBuilder] No env/AirportPoint in the scene.";
            ProceduralArt.EnsureFolder(ArtFolder);
            ProceduralArt.EnsureFolder(MeshFolder);
            ProceduralArt.EnsureFolder(PrefabFolder);
            BuildMaterials();
            staticRoots.Clear();

            GameObject old = ParachuteSetup.FindRoot(scene, RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, RootName);
            log.AppendLine($"  AirportPoint at {point.transform.position}; island x {Island.xMin}..{Island.xMax}, z {Island.yMin}..{Island.yMax}, ground y {GroundY}");

            BuildIsland(root.transform);
            BuildRunway(root.transform);
            BuildTaxiways(root.transform);
            BuildApron(root.transform);
            BuildLandside(root.transform, log);
            Dictionary<string, GameObject> hangarParts = BuildHangarVariants(log);
            BuildHangar(root.transform, "Hangar 1", new Vector3(-600f, GroundY, -270f), hangarParts, false);
            BuildHangar(root.transform, "Hangar 2", new Vector3(-600f, GroundY, -232f), hangarParts, true);
            BuildTerminal(root.transform);
            BuildTower(root.transform, new Vector3(-545f, GroundY, 75f));
            BuildHelipad(root.transform, new Vector3(-560f, GroundY, 30f), log);
            BuildWindsock(root.transform, new Vector3(-735f, GroundY, -60f));
            Dictionary<string, GameObject> props = BuildPropPrefabs(log);
            PlaceProps(root.transform, props);
            BuildCars(root.transform, log);
            BuildBlip(root.transform);

            foreach (GameObject s in staticRoots) MarkStatic(s);
            log.AppendLine("  " + CombineByMaterial(root.transform));
            EditorSceneManager.MarkSceneDirty(scene);
            log.AppendLine("  how to get there: drive / walk west on the causeway road at z -134 from the beach (coastal road at x -330) - " +
                           "about 140 m - or fly the helicopter from the Yatch_Port helipad.");
            return log.ToString();
        }

        // ---------------------------------------------------------------- materials

        private static void BuildMaterials()
        {
            Texture2D grassTex = ProceduralArt.SaveTexture(ArtFolder + "/Grass.png", 128, 128, (u, v) =>
            {
                float n = Noise(u, v, 8) * 0.6f + Noise(u, v, 32) * 0.4f;
                return Color.Lerp(new Color(0.3f, 0.47f, 0.2f), new Color(0.44f, 0.6f, 0.27f), n);
            }, repeat: true, maxSize: 128);
            Texture2D asphaltTex = ProceduralArt.SaveTexture(ArtFolder + "/Asphalt.png", 128, 128, (u, v) =>
            {
                float n = Noise(u, v, 16) * 0.5f + Noise(u, v, 64) * 0.5f;
                return Color.Lerp(new Color(0.2f, 0.21f, 0.22f), new Color(0.3f, 0.3f, 0.31f), n);
            }, repeat: true, maxSize: 128);
            Texture2D concreteTex = ProceduralArt.SaveTexture(ArtFolder + "/Concrete.png", 128, 128, (u, v) =>
            {
                float n = Noise(u, v, 12) * 0.5f + Noise(u, v, 48) * 0.5f;
                float joint = (u < 0.012f || v < 0.012f) ? 0.75f : 1f;
                return Color.Lerp(new Color(0.62f, 0.62f, 0.6f), new Color(0.74f, 0.73f, 0.7f), n) * joint;
            }, repeat: true, maxSize: 128);
            Texture2D fenceTex = ProceduralArt.SaveTexture(ArtFolder + "/ChainLink.png", 64, 64, (u, v) =>
            {
                float a = Mathf.Repeat(u * 4f + v * 4f, 1f), b = Mathf.Repeat(u * 4f - v * 4f, 1f);
                bool wire = a < 0.12f || b < 0.12f;
                return wire ? new Color(0.65f, 0.67f, 0.7f, 1f) : new Color(0f, 0f, 0f, 0f);
            }, repeat: true, maxSize: 64);

            grass = ProceduralArt.Lit(ArtFolder + "/AirportGrass.mat", Color.white, grassTex, 0.05f);
            asphalt = ProceduralArt.Lit(ArtFolder + "/AirportAsphalt.mat", Color.white, asphaltTex, 0.2f);
            concrete = ProceduralArt.Lit(ArtFolder + "/AirportConcrete.mat", new Color(0.72f, 0.73f, 0.75f), concreteTex, 0.15f);
            seawall = ProceduralArt.Lit(ArtFolder + "/AirportSeawall.mat", new Color(0.55f, 0.53f, 0.5f), concreteTex, 0.1f);
            white = ProceduralArt.Lit(ArtFolder + "/AirportMarkingWhite.mat", new Color(0.95f, 0.95f, 0.93f), null, 0.3f);
            yellow = ProceduralArt.Lit(ArtFolder + "/AirportMarkingYellow.mat", new Color(0.98f, 0.78f, 0.1f), null, 0.3f);
            black = ProceduralArt.Lit(ArtFolder + "/AirportBlack.mat", new Color(0.06f, 0.06f, 0.07f), null, 0.3f);
            wall = ProceduralArt.Lit(ArtFolder + "/AirportWall.mat", new Color(0.88f, 0.87f, 0.84f), null, 0.2f);
            glass = ProceduralArt.Lit(ArtFolder + "/AirportGlass.mat", new Color(0.18f, 0.32f, 0.45f), null, 0.9f, 0.3f);
            roof = ProceduralArt.Lit(ArtFolder + "/AirportRoof.mat", new Color(0.36f, 0.38f, 0.42f), null, 0.3f, 0.2f);
            metal = ProceduralArt.Lit(ArtFolder + "/AirportMetal.mat", new Color(0.6f, 0.62f, 0.65f), null, 0.5f, 0.6f);
            red = ProceduralArt.Lit(ArtFolder + "/AirportRed.mat", new Color(0.8f, 0.12f, 0.1f), null, 0.4f);
            orange = ProceduralArt.Lit(ArtFolder + "/AirportOrange.mat", new Color(1f, 0.45f, 0.05f), null, 0.3f);
            fence = ProceduralArt.Lit(ArtFolder + "/AirportFence.mat", Color.white, fenceTex, 0.3f, 0.5f, null, true);
            fence.SetFloat("_AlphaClip", 1f);
            fence.SetFloat("_Cutoff", 0.5f);
            fence.EnableKeyword("_ALPHATEST_ON");
            fence.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            lightWhite = ProceduralArt.Unlit(ArtFolder + "/LightWhite.mat", new Color(1f, 0.97f, 0.85f));
            lightGreen = ProceduralArt.Unlit(ArtFolder + "/LightGreen.mat", new Color(0.2f, 1f, 0.35f));
            lightRed = ProceduralArt.Unlit(ArtFolder + "/LightRed.mat", new Color(1f, 0.15f, 0.1f));
            lightBlue = ProceduralArt.Unlit(ArtFolder + "/LightBlue.mat", new Color(0.25f, 0.5f, 1f));
            lightAmber = ProceduralArt.Unlit(ArtFolder + "/LightAmber.mat", new Color(1f, 0.7f, 0.15f));
            signYellow = ProceduralArt.Unlit(ArtFolder + "/SignYellow.mat", new Color(1f, 0.82f, 0.1f));
        }

        // Tileable value noise.
        private static float Noise(float u, float v, int cells)
        {
            float x = u * cells, y = v * cells;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(x0 % cells, y0 % cells), b = Hash((x0 + 1) % cells, y0 % cells);
            float c = Hash(x0 % cells, (y0 + 1) % cells), d = Hash((x0 + 1) % cells, (y0 + 1) % cells);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h & 0xFFFF) / 65535f;
        }

        // ---------------------------------------------------------------- ground

        private static void BuildIsland(Transform root)
        {
            Transform ground = Group(root, "Island");
            float height = GroundY - SeaFloorY;
            var island = Box(ground, "Island Ground", new Vector3(Island.center.x, GroundY - height * 0.5f, Island.center.y),
                new Vector3(Island.width, height, Island.height), seawall, true);
            island.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Surface(ground, "Grass", new Vector3(Island.center.x, GroundY + 0.005f, Island.center.y), Island.width - 0.2f, Island.height - 0.2f, 0f, grass, 6f);
            // Kerb along the sea wall.
            Box(ground, "Edge North", new Vector3(Island.center.x, GroundY + 0.15f, Island.yMax - 0.3f), new Vector3(Island.width, 0.3f, 0.6f), concrete, true);
            Box(ground, "Edge South", new Vector3(Island.center.x, GroundY + 0.15f, Island.yMin + 0.3f), new Vector3(Island.width, 0.3f, 0.6f), concrete, true);
            Box(ground, "Edge West", new Vector3(Island.xMin + 0.3f, GroundY + 0.15f, Island.center.y), new Vector3(0.6f, 0.3f, Island.height), concrete, true);
            Box(ground, "Edge East North", new Vector3(Island.xMax - 0.3f, GroundY + 0.15f, (RoadZ + 9f + Island.yMax) * 0.5f), new Vector3(0.6f, 0.3f, Island.yMax - RoadZ - 9f), concrete, true);
            Box(ground, "Edge East South", new Vector3(Island.xMax - 0.3f, GroundY + 0.15f, (RoadZ - 9f + Island.yMin) * 0.5f), new Vector3(0.6f, 0.3f, RoadZ - 9f - Island.yMin), concrete, true);
            staticRoots.Add(ground.gameObject);
        }

        private static void BuildRunway(Transform root)
        {
            Transform runway = Group(root, "Runway");
            float length = RunwayZMax - RunwayZMin, cz = (RunwayZMax + RunwayZMin) * 0.5f;
            Surface(runway, "Runway Surface", new Vector3(RunwayX, GroundY + 0.03f, cz), RunwayWidth, length, 0f, asphalt, 8f);
            float y = GroundY + 0.05f;
            // Edge lines and centre line.
            Surface(runway, "Edge Line W", new Vector3(RunwayX - RunwayWidth * 0.5f + 1f, y, cz), 0.9f, length - 2f, 0f, white, 4f);
            Surface(runway, "Edge Line E", new Vector3(RunwayX + RunwayWidth * 0.5f - 1f, y, cz), 0.9f, length - 2f, 0f, white, 4f);
            for (float z = RunwayZMin + 70f; z < RunwayZMax - 70f; z += 50f)
                Surface(runway, "Centre Line", new Vector3(RunwayX, y, z + 15f), 0.9f, 30f, 0f, white, 4f);
            // Threshold bars, numbers and touchdown marks at both ends.
            for (int end = 0; end < 2; end++)
            {
                float edge = end == 0 ? RunwayZMin : RunwayZMax;
                float dir = end == 0 ? 1f : -1f;
                for (int i = 0; i < 8; i++)
                {
                    float x = RunwayX - 18f + i * 5.15f;
                    if (i >= 4) x += 1.6f;
                    Surface(runway, "Threshold", new Vector3(x, y, edge + dir * 18f), 1.8f, 30f, 0f, white, 4f);
                }
                Number(runway, end == 0 ? "36" : "18", new Vector3(RunwayX, y, edge + dir * 50f), end == 0 ? 0f : 180f);
                for (int s = -1; s <= 1; s += 2)
                    Surface(runway, "Aiming Point", new Vector3(RunwayX + s * 8f, y, edge + dir * 150f), 6f, 45f, 0f, white, 4f);
            }
            // Edge lights (white), threshold lights (green / red), approach lights.
            for (float z = RunwayZMin + 5f; z <= RunwayZMax - 5f; z += 50f)
                for (int s = -1; s <= 1; s += 2) LightCard(runway, new Vector3(RunwayX + s * (RunwayWidth * 0.5f + 1.5f), GroundY, z), lightWhite);
            for (float x = RunwayX - 20f; x <= RunwayX + 20f; x += 5f)
            {
                LightCard(runway, new Vector3(x, GroundY, RunwayZMin - 2f), lightGreen);
                LightCard(runway, new Vector3(x, GroundY, RunwayZMax + 2f), lightRed);
            }
            for (int i = 1; i <= 4; i++)
                for (int k = -2; k <= 2; k++) LightCard(runway, new Vector3(RunwayX + k * 3f, GroundY, RunwayZMin - 2f - i * 12f), lightAmber);
            staticRoots.Add(runway.gameObject);
        }

        private static void BuildTaxiways(Transform root)
        {
            Transform taxi = Group(root, "Taxiways");
            float zMin = RunwayZMin + 20f, zMax = RunwayZMax - 20f;
            Surface(taxi, "Taxiway", new Vector3(TaxiwayX, GroundY + 0.03f, (zMin + zMax) * 0.5f), TaxiwayWidth, zMax - zMin, 0f, asphalt, 8f);
            Surface(taxi, "Taxiway Line", new Vector3(TaxiwayX, GroundY + 0.05f, (zMin + zMax) * 0.5f), 0.4f, zMax - zMin, 0f, yellow, 4f);
            float linkFrom = RunwayX + RunwayWidth * 0.5f, linkTo = TaxiwayX - TaxiwayWidth * 0.5f;
            foreach (float z in new[] { zMin + 10f, RoadZ, zMax - 10f })
            {
                Surface(taxi, "Link", new Vector3((linkFrom + linkTo) * 0.5f, GroundY + 0.031f, z), linkTo - linkFrom + 2f, 22f, 0f, asphalt, 8f);
                Surface(taxi, "Link Line", new Vector3((linkFrom + TaxiwayX) * 0.5f, GroundY + 0.052f, z), TaxiwayX - linkFrom, 0.4f, 0f, yellow, 4f);
                // Hold line: double yellow across the link near the runway.
                Surface(taxi, "Hold Line", new Vector3(linkFrom + 6f, GroundY + 0.053f, z), 0.4f, 20f, 0f, yellow, 4f);
                Surface(taxi, "Hold Line", new Vector3(linkFrom + 7f, GroundY + 0.053f, z), 0.4f, 20f, 0f, yellow, 4f);
                Sign(taxi, new Vector3(linkTo - 2f, GroundY, z + 13f), 90f);
            }
            for (float z = zMin; z <= zMax; z += 40f)
                for (int s = -1; s <= 1; s += 2) LightCard(taxi, new Vector3(TaxiwayX + s * (TaxiwayWidth * 0.5f + 1f), GroundY, z), lightBlue);
            staticRoots.Add(taxi.gameObject);
        }

        private static void BuildApron(Transform root)
        {
            Transform apron = Group(root, "Apron");
            Surface(apron, "Apron Surface", new Vector3(Apron.center.x, GroundY + 0.032f, Apron.center.y), Apron.width, Apron.height, 0f, concrete, 10f);
            Surface(apron, "Apron Link", new Vector3((TaxiwayX + Apron.xMin) * 0.5f, GroundY + 0.031f, -150f), Apron.xMin - TaxiwayX + 4f, 60f, 0f, concrete, 10f);
            // Parking stands: yellow lead-in lines and stop bars.
            for (int i = 0; i < 4; i++)
            {
                float z = -150f + i * 45f;
                Surface(apron, "Stand Line", new Vector3(-600f, GroundY + 0.055f, z), 70f, 0.4f, 0f, yellow, 4f);
                Surface(apron, "Stop Bar", new Vector3(-568f, GroundY + 0.055f, z), 0.5f, 8f, 0f, yellow, 4f);
            }
            // Service road (white dashes) along the east edge of the apron.
            for (float z = Apron.yMin + 5f; z < Apron.yMax - 5f; z += 12f)
                Surface(apron, "Service Road", new Vector3(Apron.xMax - 10f, GroundY + 0.055f, z), 0.3f, 6f, 0f, white, 4f);
            staticRoots.Add(apron.gameObject);
        }

        // Causeway from the beach, landside road to the car park, fences with a gate.
        private static void BuildLandside(Transform root, StringBuilder log)
        {
            Transform land = Group(root, "Landside");
            float deckFrom = Island.xMax - 1f, deckTo = -378f;
            float width = 16f;
            // Causeway deck (collider) and road.
            Box(land, "Causeway Deck", new Vector3((deckFrom + deckTo) * 0.5f, GroundY - 0.6f, RoadZ), new Vector3(deckTo - deckFrom, 1.2f, width), seawall, true);
            Surface(land, "Causeway Road", new Vector3((Apron.xMax + BeachRoadEndX) * 0.5f, GroundY + 0.04f, RoadZ), BeachRoadEndX - Apron.xMax, 12f, 0f, asphalt, 8f);
            for (float x = Apron.xMax + 4f; x < BeachRoadEndX - 4f; x += 10f)
                Surface(land, "Road Centre", new Vector3(x, GroundY + 0.06f, RoadZ), 5f, 0.3f, 0f, white, 4f);
            Surface(land, "Road Edge N", new Vector3((Apron.xMax + BeachRoadEndX) * 0.5f, GroundY + 0.06f, RoadZ + 5.6f), BeachRoadEndX - Apron.xMax, 0.25f, 0f, white, 4f);
            Surface(land, "Road Edge S", new Vector3((Apron.xMax + BeachRoadEndX) * 0.5f, GroundY + 0.06f, RoadZ - 5.6f), BeachRoadEndX - Apron.xMax, 0.25f, 0f, white, 4f);
            for (int s = -1; s <= 1; s += 2)
            {
                Box(land, "Causeway Barrier", new Vector3((deckFrom + deckTo) * 0.5f, GroundY + 0.45f, RoadZ + s * (width * 0.5f - 0.3f)), new Vector3(deckTo - deckFrom, 0.9f, 0.5f), concrete, true);
                for (float x = deckFrom + 4f; x < deckTo; x += 16f)
                    Box(land, "Pillar", new Vector3(x, (GroundY - 1.2f + SeaFloorY) * 0.5f, RoadZ + s * 5f), new Vector3(1.6f, GroundY - 1.2f - SeaFloorY, 1.6f), seawall, false);
            }
            // Car park and road up to it.
            Surface(land, "Car Park", new Vector3(-492f, GroundY + 0.035f, -20f), 36f, 80f, 0f, asphalt, 8f);
            Surface(land, "Car Park Road", new Vector3(-492f, GroundY + 0.036f, (RoadZ + 6f - 60f) * 0.5f), 12f, -60f - (RoadZ + 6f), 0f, asphalt, 8f);
            for (int i = 0; i < 12; i++)
            {
                float z = -55f + i * 6f;
                Surface(land, "Bay Line", new Vector3(-503f, GroundY + 0.06f, z), 10f, 0.2f, 0f, white, 4f);
                Surface(land, "Bay Line", new Vector3(-481f, GroundY + 0.06f, z), 10f, 0.2f, 0f, white, 4f);
            }
            // Fence between landside and airside (x -520), gate open at the road.
            Fence(land, new Vector3(-519f, GroundY, Apron.yMin - 40f), new Vector3(-519f, GroundY, RoadZ - 9f));
            Fence(land, new Vector3(-519f, GroundY, RoadZ + 9f), new Vector3(-519f, GroundY, -50f));
            Fence(land, new Vector3(-519f, GroundY, 20f), new Vector3(-519f, GroundY, 110f)); // the terminal closes the gap between
            Box(land, "Gate Post", new Vector3(-519f, GroundY + 1.5f, RoadZ - 9f), new Vector3(0.4f, 3f, 0.4f), red, true);
            Box(land, "Gate Post", new Vector3(-519f, GroundY + 1.5f, RoadZ + 9f), new Vector3(0.4f, 3f, 0.4f), red, true);
            var arm = Box(land, "Gate Arm (raised)", new Vector3(-519f, GroundY + 4.5f, RoadZ - 8.6f), new Vector3(0.15f, 6f, 0.15f), red, false);
            arm.name = "Gate Arm";
            Box(land, "Gate Booth", new Vector3(-515f, GroundY + 1.4f, RoadZ - 13f), new Vector3(3f, 2.8f, 3f), wall, true);
            Box(land, "Gate Booth Roof", new Vector3(-515f, GroundY + 2.9f, RoadZ - 13f), new Vector3(3.6f, 0.2f, 3.6f), roof, false);
            Box(land, "Gate Booth Window", new Vector3(-516.52f, GroundY + 1.7f, RoadZ - 13f), new Vector3(0.05f, 1f, 2.2f), glass, false);
            staticRoots.Add(land.gameObject);
            log.AppendLine($"  causeway road: z {RoadZ}, from the beach (x {BeachRoadEndX}) to the airport gate (x -519); car park at the terminal");
        }

        // ---------------------------------------------------------------- buildings

        // URP prefab variants of the hangar parts (the pack's materials are HDRP).
        private static Dictionary<string, GameObject> BuildHangarVariants(StringBuilder log)
        {
            string folder = PrefabFolder + "/Hangar";
            ProceduralArt.EnsureFolder(folder);
            var map = new Dictionary<Material, Material>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { HangarMaterials }))
            {
                var source = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                map[source] = UrpCopy(source, folder + "/" + source.name + "_URP.mat");
            }
            var parts = new Dictionary<string, GameObject>();
            foreach (string name in new[] { "Hangar_part_01", "Hangar_part_02", "Hangar_part_03", "Hangar_part_04", "Hangar_part_05" })
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(HangarSource + "/" + name + ".prefab");
                if (source == null) continue;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
                foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != null && map.TryGetValue(mats[i], out Material urp)) mats[i] = urp;
                        else if (mats[i] == null) mats[i] = metal;
                    r.sharedMaterials = mats;
                    RooftopAccessBuilder.ProbeLit(r);
                }
                parts[name] = PrefabUtility.SaveAsPrefabAsset(instance, folder + "/" + name + "_URP.prefab");
                Object.DestroyImmediate(instance);
            }
            log.AppendLine($"  hangar: {parts.Count} URP prefab variants of the HQ Hangar parts in {folder}");
            return parts;
        }

        private static Material UrpCopy(Material source, string path)
        {
            var so = new SerializedObject(source);
            Texture baseMap = null, normal = null, mask = null;
            Color colour = Color.white;
            SerializedProperty texEnvs = so.FindProperty("m_SavedProperties.m_TexEnvs");
            for (int i = 0; texEnvs != null && i < texEnvs.arraySize; i++)
            {
                SerializedProperty e = texEnvs.GetArrayElementAtIndex(i);
                string key = e.FindPropertyRelative("first").stringValue;
                Texture t = e.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;
                if (key == "_BaseColorMap") baseMap = t;
                else if (key == "_NormalMap") normal = t;
                else if (key == "_MaskMap") mask = t;
            }
            SerializedProperty colours = so.FindProperty("m_SavedProperties.m_Colors");
            for (int i = 0; colours != null && i < colours.arraySize; i++)
            {
                SerializedProperty e = colours.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("first").stringValue == "_BaseColor") colour = e.FindPropertyRelative("second").colorValue;
            }
            bool isGlass = source.name.ToLower().Contains("glass");
            Material m = isGlass ? ProceduralArt.LitTransparent(path, new Color(0.55f, 0.7f, 0.8f, 0.35f), false)
                                 : ProceduralArt.Lit(path, colour, baseMap as Texture2D, 0.4f, 0f, normal as Texture2D);
            if (!isGlass && mask != null)
            {
                // HDRP mask map: R metallic, A smoothness - the same channels URP's metallic map reads.
                m.SetTexture("_MetallicGlossMap", mask);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_Smoothness", 0.8f);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        // Sections along local x like the pack's example scene: end wall, alternating bays, door end. Door end faces the taxiway (-x).
        private static void BuildHangar(Transform root, string name, Vector3 position, Dictionary<string, GameObject> parts, bool variantEnd)
        {
            Transform hangar = Group(root, name);
            hangar.SetPositionAndRotation(position, Quaternion.Euler(0f, 180f, 0f));
            string[] order = { "Hangar_part_04", "Hangar_part_03", "Hangar_part_02", "Hangar_part_03", "Hangar_part_02", "Hangar_part_03", "Hangar_part_02",
                variantEnd ? "Hangar_part_01" : "Hangar_part_05" };
            for (int i = 0; i < order.Length; i++)
            {
                if (!parts.TryGetValue(order[i], out GameObject prefab)) continue;
                var part = (GameObject)PrefabUtility.InstantiatePrefab(prefab, hangar.gameObject.scene);
                part.transform.SetParent(hangar, false);
                part.transform.localPosition = new Vector3(-17.5f + (i == 0 ? 0f : i * 5f), 0f, 0f);
            }
            Surface(hangar, "Hangar Floor", hangar.position + Vector3.up * 0.06f, 0f, 0f, 0f, concrete, 10f, new Vector2(38f, 20f), hangar.rotation);
            staticRoots.Add(hangar.gameObject);
        }

        private static void BuildTerminal(Transform root)
        {
            Transform t = Group(root, "Terminal");
            t.position = new Vector3(-532f, GroundY, -15f);
            Vector3 c = t.position;
            Box(t, "Hall", c + new Vector3(0f, 5f, 0f), new Vector3(16f, 10f, 62f), wall, true);
            Box(t, "Glass Front", c + new Vector3(8.05f, 4.6f, 0f), new Vector3(0.1f, 7f, 56f), glass, false);
            Box(t, "Glass Airside", c + new Vector3(-8.05f, 4.6f, 0f), new Vector3(0.1f, 7f, 56f), glass, false);
            Box(t, "Roof", c + new Vector3(0f, 10.3f, 0f), new Vector3(19f, 0.6f, 66f), roof, true);
            Box(t, "Canopy", c + new Vector3(11f, 5.2f, 0f), new Vector3(6f, 0.3f, 40f), metal, false);
            for (int i = -3; i <= 3; i++) Box(t, "Canopy Column", c + new Vector3(13.6f, 2.5f, i * 6.5f), new Vector3(0.35f, 5f, 0.35f), metal, false);
            Box(t, "Entrance", c + new Vector3(8.1f, 1.6f, 0f), new Vector3(0.2f, 3.2f, 6f), black, false);
            // "AIRPORT" sign: a lit plane icon board on the roof.
            Texture2D icon = ProceduralArt.Icon(ArtFolder + "/AirportIcon.png", PlaneGlyph, new Color(0.05f, 0.2f, 0.5f, 1f));
            Material iconMat = ProceduralArt.Unlit(ArtFolder + "/AirportSign.mat", Color.white, icon, true);
            Card(t, "Sign", c + new Vector3(8.3f, 12.5f, 0f), Quaternion.Euler(0f, -90f, 0f), 4f, iconMat);
            Card(t, "Sign Back", c + new Vector3(8.2f, 12.5f, 0f), Quaternion.Euler(0f, 90f, 0f), 4f, iconMat);
            // Jet bridge stub towards the apron.
            Box(t, "Jet Bridge", c + new Vector3(-14f, 4.5f, -20f), new Vector3(12f, 3f, 3f), metal, false);
            Box(t, "Jet Bridge Leg", c + new Vector3(-19f, 1.5f, -20f), new Vector3(0.5f, 3f, 0.5f), metal, false);
            staticRoots.Add(t.gameObject);
        }

        private static void BuildTower(Transform root, Vector3 at)
        {
            Transform t = Group(root, "Control Tower");
            t.position = at;
            Cylinder(t, "Base", at + new Vector3(0f, 2f, 0f), new Vector3(9f, 2f, 9f), wall, true);
            Cylinder(t, "Shaft", at + new Vector3(0f, 13f, 0f), new Vector3(4.5f, 11f, 4.5f), wall, true);
            Cylinder(t, "Deck", at + new Vector3(0f, 24.3f, 0f), new Vector3(10f, 0.3f, 10f), roof, true);
            Cylinder(t, "Cab", at + new Vector3(0f, 26.6f, 0f), new Vector3(8.6f, 2f, 8.6f), glass, false);
            Cylinder(t, "Cab Roof", at + new Vector3(0f, 28.8f, 0f), new Vector3(10.4f, 0.25f, 10.4f), roof, false);
            Box(t, "Antenna", at + new Vector3(0f, 31f, 0f), new Vector3(0.15f, 4.5f, 0.15f), metal, false);
            LightCard(t, at + new Vector3(0f, 33.3f, 0f), lightRed, 0.5f);
            Box(t, "Door", at + new Vector3(4.45f, 1.3f, 0f), new Vector3(0.2f, 2.6f, 1.4f), black, false);
            staticRoots.Add(t.gameObject);
        }

        private static void BuildHelipad(Transform root, Vector3 at, StringBuilder log)
        {
            Transform pad = Group(root, "Airport Helipad");
            pad.position = at;
            Texture2D hTex = ProceduralArt.SaveTexture(ArtFolder + "/HelipadMarking.png", 256, 256, (u, v) =>
            {
                float x = u * 2f - 1f, y = v * 2f - 1f, r = Mathf.Sqrt(x * x + y * y);
                if (r > 0.98f) return new Color(0, 0, 0, 0);
                bool ring = Mathf.Abs(r - 0.85f) < 0.05f;
                bool h = (Mathf.Abs(x + 0.3f) < 0.08f || Mathf.Abs(x - 0.3f) < 0.08f) && Mathf.Abs(y) < 0.45f || Mathf.Abs(y) < 0.07f && Mathf.Abs(x) < 0.3f;
                if (ring) return new Color(1f, 0.8f, 0.1f, 1f);
                if (h) return Color.white;
                return new Color(0.28f, 0.3f, 0.32f, 1f);
            }, maxSize: 256);
            Material padMat = ProceduralArt.Lit(ArtFolder + "/HelipadPad.mat", Color.white, hTex, 0.2f);
            padMat.SetFloat("_AlphaClip", 1f);
            padMat.SetFloat("_Cutoff", 0.5f);
            padMat.EnableKeyword("_ALPHATEST_ON");
            Surface(pad, "Pad", at + Vector3.up * 0.07f, 22f, 22f, 0f, padMat, 22f);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                LightCard(pad, at + new Vector3(Mathf.Cos(a) * 11.5f, 0f, Mathf.Sin(a) * 11.5f), lightGreen);
            }
            staticRoots.Add(pad.gameObject);
            var heli = AssetDatabase.LoadAssetAtPath<GameObject>(HelicopterPrefab);
            if (heli != null)
            {
                var h = (GameObject)PrefabUtility.InstantiatePrefab(heli, root.gameObject.scene);
                h.name = "Helicopter - Airport";
                h.transform.SetParent(root, false);
                h.transform.SetPositionAndRotation(at + Vector3.up * 0.1f, Quaternion.Euler(0f, 270f, 0f));
                log.AppendLine("  second helicopter on the airport helipad");
            }
        }

        private static void BuildWindsock(Transform root, Vector3 at)
        {
            Transform w = Group(root, "Windsock");
            w.position = at;
            Box(w, "Pole", at + new Vector3(0f, 3f, 0f), new Vector3(0.15f, 6f, 0.15f), metal, true);
            Transform sock = Cylinder(w, "Sock", at + new Vector3(1.4f, 5.8f, 0f), new Vector3(0.6f, 1.4f, 0.6f), orange, false);
            sock.rotation = Quaternion.Euler(0f, 0f, 80f);
            staticRoots.Add(w.gameObject);
        }

        // ---------------------------------------------------------------- street props (LOD prefabs)

        private struct PropSpec
        {
            public string name, folder, fbx, texture;
            public float height;
            public PropSpec(string n, string f, string m, string t, float h) { name = n; folder = f; fbx = m; texture = t; height = h; }
        }

        private static readonly PropSpec[] Specs =
        {
            new PropSpec("Cone", "Cone", "Cone", "Cone/Cone_Clean_BaseColor.png", 0.72f),
            new PropSpec("Barrel", "Barrel", "Barrel", "Barrel/Barrel_Used_BaseColor.png", 0.95f),
            new PropSpec("WaterBarricade", "Water Barricade", "Water_Barricade", "Water Barricade/Water_Barricade_Clear_1_BaseColor.png", 1f),
            new PropSpec("PedestrianBarrier", "Pedestrian Barrier", "Pedestrian_Barrier", "Pedestrian Barrier/Pedestrian_Barrier_1_Clean_BaseColor.png", 1.1f),
            new PropSpec("VerticalPanel", "Vertical Panel", "Vertical_Panel", "Vertical Panel/Vertical_Panel_Clear_BaseColor.png", 1.25f),
            new PropSpec("Channelizer", "Channelizing", "Channelizing_1", "Channelizing/Channelizing_1_Clean_BaseColor.png", 1f),
        };

        private static Dictionary<string, GameObject> BuildPropPrefabs(StringBuilder log)
        {
            string folder = PrefabFolder + "/StreetProps";
            ProceduralArt.EnsureFolder(folder);
            var result = new Dictionary<string, GameObject>();
            foreach (PropSpec spec in Specs)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(PropsSource + "/Textures/" + spec.texture);
                Material mat = ProceduralArt.Lit(folder + "/" + spec.name + ".mat", Color.white, tex, 0.3f);
                var root = new GameObject(spec.name);
                var lodGroup = root.AddComponent<LODGroup>();
                var lods = new List<LOD>();
                string[] levels = { "_lod0", "_lod02", "_lod04" };
                float[] heights = { 0.25f, 0.08f, 0.015f };
                for (int i = 0; i < levels.Length; i++)
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(PropsSource + "/FBX/" + spec.folder + "/" + spec.fbx + levels[i] + ".fbx");
                    if (model == null) continue;
                    var lod = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    lod.name = "LOD" + i;
                    lod.transform.SetParent(root.transform, false);
                    Renderer[] rs = lod.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer r in rs)
                    {
                        r.sharedMaterial = mat;
                        r.shadowCastingMode = i == 0 ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                        RooftopAccessBuilder.ProbeLit(r);
                    }
                    lods.Add(new LOD(heights[i], rs));
                }
                // Scale to a real-world height and stand it on its base.
                Bounds b = PropBounds(root);
                float scale = spec.height / Mathf.Max(0.01f, b.size.y);
                foreach (Transform child in root.transform) child.localScale *= scale;
                b = PropBounds(root);
                foreach (Transform child in root.transform) child.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                b = PropBounds(root);
                lodGroup.SetLODs(lods.ToArray());
                lodGroup.RecalculateBounds();
                var box = root.AddComponent<BoxCollider>();
                box.center = b.center;
                box.size = b.size;
                result[spec.name] = PrefabUtility.SaveAsPrefabAsset(root, folder + "/" + spec.name + ".prefab");
                Object.DestroyImmediate(root);
            }
            log.AppendLine($"  street props: {result.Count} LOD prefabs (3 levels, culled when tiny) in {folder}");
            return result;
        }

        private static Bounds PropBounds(GameObject root)
        {
            Renderer[] rs = root.GetComponentsInChildren<Renderer>(true);
            Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds();
            foreach (Renderer r in rs) if (r.name.Contains("lod0") || r.transform.parent.name == "LOD0" || r.transform.name == "LOD0") b.Encapsulate(r.bounds);
            // Use LOD0 only.
            Transform lod0 = root.transform.Find("LOD0");
            if (lod0 != null)
            {
                Renderer[] l0 = lod0.GetComponentsInChildren<Renderer>(true);
                if (l0.Length > 0)
                {
                    b = l0[0].bounds;
                    foreach (Renderer r in l0) b.Encapsulate(r.bounds);
                }
            }
            return b;
        }

        private static void PlaceProps(Transform root, Dictionary<string, GameObject> props)
        {
            Transform group = Group(root, "Street Props");
            var rng = new System.Random(11);
            // Water barricades along both sides of the causeway entrance and at the gate.
            for (int i = 0; i < 8; i++)
            {
                Prop(group, props, "WaterBarricade", new Vector3(-525f - i * 1.15f, GroundY, RoadZ + 7.5f), 0f);
                Prop(group, props, "WaterBarricade", new Vector3(-525f - i * 1.15f, GroundY, RoadZ - 7.5f), 0f);
            }
            // Cones in front of the hangars and around a work area on the apron.
            for (int i = 0; i < 10; i++)
            {
                Prop(group, props, "Cone", new Vector3(-621f, GroundY, -282f + i * 2.6f), (float)rng.NextDouble() * 360f);
                Prop(group, props, "Cone", new Vector3(-621f, GroundY, -244f + i * 2.6f), (float)rng.NextDouble() * 360f);
            }
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI * 2f / 6f;
                Prop(group, props, "Channelizer", new Vector3(-560f + Mathf.Cos(a) * 4f, GroundY, -300f + Mathf.Sin(a) * 4f), 0f);
            }
            // Barrels by the hangar side walls.
            for (int i = 0; i < 6; i++) Prop(group, props, "Barrel", new Vector3(-578f + (i % 3) * 0.75f, GroundY, -287f + (i / 3) * 0.75f), (float)rng.NextDouble() * 360f);
            for (int i = 0; i < 4; i++) Prop(group, props, "Barrel", new Vector3(-578f + (i % 2) * 0.75f, GroundY, -213f + (i / 2) * 0.75f), (float)rng.NextDouble() * 360f);
            // Pedestrian barriers along the terminal front.
            for (int i = 0; i < 18; i++) Prop(group, props, "PedestrianBarrier", new Vector3(-517f, GroundY, -42f + i * 3f), 90f);
            // Panels at the car park entrance.
            for (int i = 0; i < 4; i++) Prop(group, props, "VerticalPanel", new Vector3(-486f + i * 1.2f, GroundY, -63f), 0f);
            staticRoots.Add(group.gameObject);
        }

        private static void Prop(Transform parent, Dictionary<string, GameObject> props, string name, Vector3 position, float yaw)
        {
            if (!props.TryGetValue(name, out GameObject prefab) || prefab == null) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }

        // Two of the project's drivable cars parked at the terminal.
        private static void BuildCars(Transform root, StringBuilder log)
        {
            var car = AssetDatabase.LoadAssetAtPath<GameObject>(CarPrefab);
            if (car == null) { log.AppendLine("  no car prefab at " + CarPrefab); return; }
            Transform cars = Group(root, "Parked Cars");
            for (int i = 0; i < 2; i++)
            {
                var c = (GameObject)PrefabUtility.InstantiatePrefab(car, root.gameObject.scene);
                c.transform.SetParent(cars, false);
                c.transform.SetPositionAndRotation(new Vector3(-503f, GroundY + 0.4f, -46f + i * 12f), Quaternion.Euler(0f, 90f, 0f));
            }
            log.AppendLine("  2 drivable cars (" + car.name + ") in the car park");
        }

        private static void BuildBlip(Transform root)
        {
            var go = new GameObject("Airport Map Icon");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(-560f, GroundY, -60f);
            var blip = go.AddComponent<MapBlip>();
            var so = new SerializedObject(blip);
            so.FindProperty("icon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(ArtFolder + "/AirportIcon.png");
            so.FindProperty("iconColour").colorValue = Color.white;
            so.FindProperty("backgroundColour").colorValue = new Color(0.05f, 0.2f, 0.5f, 0.92f);
            so.FindProperty("size").floatValue = 30f;
            so.ApplyModifiedPropertiesWithoutUndo();
            var importer = (TextureImporter)AssetImporter.GetAtPath(ArtFolder + "/AirportIcon.png");
            if (importer != null && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.SaveAndReimport();
            }
        }

        // ---------------------------------------------------------------- details

        private static void LightCard(Transform parent, Vector3 groundPoint, Material material, float size = 0.35f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Light";
            go.transform.SetParent(parent, true);
            go.transform.position = groundPoint + Vector3.up * (size * 0.5f + 0.05f);
            go.transform.localScale = Vector3.one * size;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        }

        private static void Sign(Transform parent, Vector3 at, float yaw)
        {
            Transform s = Group(parent, "Taxi Sign");
            s.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            LocalBox(s, "Board", new Vector3(0f, 0.9f, 0f), new Vector3(2.4f, 0.9f, 0.25f), black, true);
            LocalBox(s, "Face", new Vector3(0f, 0.9f, 0.13f), new Vector3(2.2f, 0.75f, 0.02f), signYellow, false);
            LocalBox(s, "Leg L", new Vector3(-0.9f, 0.25f, 0f), new Vector3(0.1f, 0.5f, 0.1f), metal, false);
            LocalBox(s, "Leg R", new Vector3(0.9f, 0.25f, 0f), new Vector3(0.1f, 0.5f, 0.1f), metal, false);
        }

        private static void Fence(Transform parent, Vector3 from, Vector3 to)
        {
            Transform f = Group(parent, "Fence");
            Vector3 d = to - from;
            float length = d.magnitude;
            Quaternion rot = Quaternion.LookRotation(d.normalized);
            Vector3 mid = (from + to) * 0.5f;
            // The flat mesh stood up: its x along the fence, its z up.
            Quaternion panel = Quaternion.LookRotation(Vector3.up, Vector3.Cross(Vector3.up, d.normalized));
            Surface(f, "Mesh", mid + Vector3.up * 1.25f, 0f, 0f, 0f, fence, 2.5f, new Vector2(length, 2.5f), panel, true);
            var col = new GameObject("Fence Collider");
            col.transform.SetParent(f, false);
            col.transform.SetPositionAndRotation(mid + Vector3.up * 1.25f, rot);
            col.AddComponent<BoxCollider>().size = new Vector3(0.1f, 2.5f, length);
            for (float t = 0f; t <= length; t += 3f)
            {
                Vector3 p = from + d.normalized * t;
                Box(f, "Post", p + Vector3.up * 1.3f, new Vector3(0.08f, 2.6f, 0.08f), metal, false);
            }
            Transform rail = Box(f, "Top Rail", mid + Vector3.up * 2.55f, new Vector3(0.06f, 0.06f, length), metal, false);
            rail.rotation = rot;
        }

        // Runway number from two 7-segment digits, painted (white boxes).
        private static void Number(Transform parent, string digits, Vector3 centre, float yaw)
        {
            Transform n = Group(parent, "Runway Number " + digits);
            n.SetPositionAndRotation(centre, Quaternion.Euler(0f, yaw, 0f));
            const float w = 6f, h = 18f, t = 1.3f, gap = 3f;
            for (int i = 0; i < digits.Length; i++)
            {
                float ox = (i - (digits.Length - 1) * 0.5f) * (w + gap);
                int[] segs = Segments(digits[i]);
                // a top, b top-right, c bottom-right, d bottom, e bottom-left, f top-left, g middle (x across, z along the runway)
                Vector3[] pos =
                {
                    new Vector3(0f, 0f, h * 0.5f), new Vector3(w * 0.5f, 0f, h * 0.25f), new Vector3(w * 0.5f, 0f, -h * 0.25f),
                    new Vector3(0f, 0f, -h * 0.5f), new Vector3(-w * 0.5f, 0f, -h * 0.25f), new Vector3(-w * 0.5f, 0f, h * 0.25f), Vector3.zero
                };
                Vector3[] size =
                {
                    new Vector3(w, 0.01f, t), new Vector3(t, 0.01f, h * 0.5f), new Vector3(t, 0.01f, h * 0.5f),
                    new Vector3(w, 0.01f, t), new Vector3(t, 0.01f, h * 0.5f), new Vector3(t, 0.01f, h * 0.5f), new Vector3(w, 0.01f, t)
                };
                foreach (int s in segs) LocalBox(n, "Segment", pos[s] + new Vector3(ox, 0f, 0f), size[s], white, false);
            }
        }

        private static int[] Segments(char c)
        {
            switch (c)
            {
                case '1': return new[] { 1, 2 };
                case '3': return new[] { 0, 1, 2, 3, 6 };
                case '6': return new[] { 0, 2, 3, 4, 5, 6 };
                case '8': return new[] { 0, 1, 2, 3, 4, 5, 6 };
                default: return new int[0];
            }
        }

        private static float PlaneGlyph(float x, float y)
        {
            float body = ProceduralArt.Segment(x, y, new Vector2(0f, -0.6f), new Vector2(0f, 0.6f), 0.09f);
            float wings = Mathf.Max(ProceduralArt.Segment(x, y, new Vector2(0f, 0.15f), new Vector2(-0.6f, -0.1f), 0.08f),
                ProceduralArt.Segment(x, y, new Vector2(0f, 0.15f), new Vector2(0.6f, -0.1f), 0.08f));
            float tail = Mathf.Max(ProceduralArt.Segment(x, y, new Vector2(0f, -0.5f), new Vector2(-0.25f, -0.62f), 0.06f),
                ProceduralArt.Segment(x, y, new Vector2(0f, -0.5f), new Vector2(0.25f, -0.62f), 0.06f));
            return Mathf.Max(body, Mathf.Max(wings, tail));
        }

        // ---------------------------------------------------------------- primitives

        private static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static Transform Box(Transform parent, string name, Vector3 worldCentre, Vector3 size, Material material, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.position = worldCentre;
            go.transform.rotation = parent.rotation;
            go.transform.localScale = size;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            RooftopAccessBuilder.ProbeLit(r);
            return go.transform;
        }

        private static Transform LocalBox(Transform parent, string name, Vector3 local, Vector3 size, Material material, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            RooftopAccessBuilder.ProbeLit(r);
            return go.transform;
        }

        private static Transform Cylinder(Transform parent, string name, Vector3 worldCentre, Vector3 scale, Material material, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.position = worldCentre;
            go.transform.localScale = scale;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            RooftopAccessBuilder.ProbeLit(r);
            return go.transform;
        }

        private static void Card(Transform parent, string name, Vector3 worldCentre, Quaternion rotation, float size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(worldCentre, rotation);
            go.transform.localScale = new Vector3(size, size, 1f);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();

        /// <summary>
        /// A flat rectangle (visual only) with world-scaled UVs, so one material tiles evenly at any size. Size by x / z, or
        /// by <paramref name="planeSize"/> with an arbitrary <paramref name="rotation"/> (vertical fence panels).
        /// </summary>
        private static void Surface(Transform parent, string name, Vector3 centre, float sizeX, float sizeZ, float yaw, Material material, float tile,
            Vector2? planeSize = null, Quaternion? rotation = null, bool doubleSided = false)
        {
            Vector2 size = planeSize ?? new Vector2(sizeX, sizeZ);
            Mesh mesh = PlaneMesh(size, tile);
            var go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(centre, rotation ?? Quaternion.Euler(0f, yaw, 0f));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            RooftopAccessBuilder.ProbeLit(r);
        }

        private static Mesh PlaneMesh(Vector2 size, float tile)
        {
            string key = $"Plane_{size.x:F2}x{size.y:F2}_t{tile:F1}".Replace('.', 'p');
            if (meshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
            string path = MeshFolder + "/" + key + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null;
            if (create) mesh = new Mesh { name = key };
            float hx = size.x * 0.5f, hz = size.y * 0.5f, u = size.x / tile, v = size.y / tile;
            mesh.Clear();
            mesh.vertices = new[] { new Vector3(-hx, 0f, -hz), new Vector3(-hx, 0f, hz), new Vector3(hx, 0f, hz), new Vector3(hx, 0f, -hz) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, v), new Vector2(u, v), new Vector2(u, 0f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.tangents = new[] { new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1) };
            mesh.RecalculateBounds();
            if (create) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            meshCache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// Mobile: the hundreds of primitive pieces (markings, lights, posts, buildings) are merged into one mesh per material
        /// and 250 m cell (saved under Art/Airport/Meshes). Pieces with a collider keep their collider; prefab instances
        /// (hangar parts, street props with LODs, helicopter, cars) are left alone.
        /// </summary>
        private static string CombineByMaterial(Transform root)
        {
            const float cell = 250f;
            var groups = new Dictionary<string, List<MeshRenderer>>();
            int before = 0;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(r) || r.GetComponentInParent<LODGroup>() != null) continue;
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || r.sharedMaterials.Length != 1 || r.sharedMaterial == null) continue;
                if (r.name == "Island Ground") continue; // the big collider box stays one piece
                Vector3 c = r.bounds.center;
                string key = r.sharedMaterial.name + "_" + Mathf.FloorToInt(c.x / cell) + "_" + Mathf.FloorToInt(c.z / cell) + "_" + r.shadowCastingMode;
                if (!groups.TryGetValue(key, out List<MeshRenderer> list)) groups[key] = list = new List<MeshRenderer>();
                list.Add(r);
                before++;
            }
            Transform combinedRoot = Group(root, "Combined");
            int after = 0;
            foreach (KeyValuePair<string, List<MeshRenderer>> g in groups)
            {
                if (g.Value.Count < 2) continue;
                var combine = new CombineInstance[g.Value.Count];
                int vertices = 0;
                for (int i = 0; i < combine.Length; i++)
                {
                    MeshFilter f = g.Value[i].GetComponent<MeshFilter>();
                    combine[i] = new CombineInstance { mesh = f.sharedMesh, transform = f.transform.localToWorldMatrix };
                    vertices += f.sharedMesh.vertexCount;
                }
                string path = MeshFolder + "/Combined_" + g.Key.Replace(' ', '_') + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                bool create = mesh == null;
                if (create) mesh = new Mesh();
                mesh.Clear();
                mesh.name = "Combined_" + g.Key;
                mesh.indexFormat = vertices > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.CombineMeshes(combine, true, true);
                mesh.RecalculateBounds();
                if (create) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
                MeshRenderer template = g.Value[0];
                var go = new GameObject("Combined " + g.Key);
                go.transform.SetParent(combinedRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = template.sharedMaterial;
                mr.shadowCastingMode = template.shadowCastingMode;
                mr.receiveShadows = template.receiveShadows;
                mr.lightProbeUsage = template.lightProbeUsage;
                RooftopAccessBuilder.ProbeLit(mr);
                if (template.lightProbeUsage == UnityEngine.Rendering.LightProbeUsage.Off) mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                after++;
                foreach (MeshRenderer r in g.Value)
                {
                    GameObject owner = r.gameObject;
                    if (owner.GetComponent<Collider>() != null)
                    {
                        Object.DestroyImmediate(r);
                        Object.DestroyImmediate(owner.GetComponent<MeshFilter>());
                    }
                    else if (owner.transform.childCount == 0) Object.DestroyImmediate(owner);
                    else
                    {
                        Object.DestroyImmediate(r);
                        Object.DestroyImmediate(owner.GetComponent<MeshFilter>());
                    }
                }
            }
            MarkStatic(combinedRoot.gameObject);
            return $"merged {before} primitive renderers into {after} meshes (one per material and 250 m cell)";
        }

        private static void MarkStatic(GameObject root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        }
    }
}
