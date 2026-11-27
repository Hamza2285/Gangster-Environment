using System.Collections.Generic;
using System.Text;
using OpenworldCrime.Controls;
using OpenworldCrime.Environment;
using OpenworldCrime.Interiors;
using OpenworldCrime.Parachute;
using OpenworldCrime.Player;
using OpenworldCrime.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Parachutes (re-runnable; menu Tools > OpenworldCrime > Parachute > Set Up Parachutes):
    /// - art under Assets/_Project/Art/Parachute: URP copies of the blue backpack (backpack_2 of the imported bags pack) and of
    ///   the imported parachute's canopy material (opaque + a transparent fade copy), beam / sparkle / ring materials, icons;
    /// - prefabs under Assets/_Project/Prefabs/Parachute: the worn bag, the canopy (baked mesh without the model's harness rig,
    ///   pivot between the two riser ends; "Rebuild Canopy Only" redoes just that) and the rooftop
    ///   pickup (bobbing bag, light beam, sparkles, ground ring, map blip, trigger);
    /// - the generated hang clip (Assets/_Project/Animations/Player/Parachute/Parachute_Hang.anim);
    /// - <see cref="PlayerParachute"/> on every playable character in the scene (Jason, Mia);
    /// - one pickup on the roof of every hivizcayne tower ("Parachute Pickups" in the scene);
    /// - the touch button (Touch Controls / OnFootControls / Parachute), the keyboard hint and <see cref="ParachuteButtonView"/>.
    /// The imported packs are not changed: everything is a copy or a reference.
    /// </summary>
    public static class ParachuteSetup
    {
        public const string ArtFolder = "Assets/_Project/Art/Parachute";
        public const string PrefabFolder = "Assets/_Project/Prefabs/Parachute";
        public const string AnimFolder = "Assets/_Project/Animations/Player/Parachute";
        public const string BagPrefabPath = PrefabFolder + "/ParachuteBag.prefab";
        public const string CanopyPrefabPath = PrefabFolder + "/ParachuteCanopy.prefab";
        public const string PickupPrefabPath = PrefabFolder + "/ParachutePickup.prefab";
        public const string IconPath = ArtFolder + "/ParachuteIcon.png";

        private const string BagsFolder = "Assets/Bags - Props for Interior/Bags - Props for Interior/models";
        private const string BagModelPath = BagsFolder + "/backpack_2.fbx"; // the blue one of the three backpacks
        private const string BagAlbedoPath = BagsFolder + "/textures/backpack_AlbedoTransparency.png";
        private const string BagNormalPath = BagsFolder + "/textures/backpack_Normal.png";
        private const string CanopyModelPath = "Assets/ImageToStl.com_pubg_green_parachute.fbx";
        private const string WindClipPath = "Assets/AudioClip 1/Wind.ogg";
        private const string OpenClipPath = "Assets/ParachuteOpening.mp3";
        private const string PickupClipPath = "Assets/AudioClip 1/0_achievement_claim.ogg";
        private const string ColumnMeshPath = "Assets/_Project/Art/Markers/MarkerColumn.asset";
        private const string ColumnTexturePath = "Assets/_Project/Art/Markers/MarkerColumn.png";
        private const string QuadMeshPath = "Assets/_Project/Art/Markers/MarkerQuad.asset";
        private const string RingTexturePath = "Assets/_Project/Art/Markers/MarkerRing.png";
        private const string ButtonFontName = "LegacyRuntime.ttf";
        private const string HintSpritePath = "Assets/Game/Resources/UIButtons/Btn_Pink.png";

        private const string PickupsRootName = "Parachute Pickups";
        private const string TouchButtonName = "Parachute";
        private const string HintName = "Parachute Prompt";
        private const string ViewName = "Parachute Button View";

        public static readonly Color BagBlue = new Color(0.16f, 0.45f, 0.86f, 1f);
        private static readonly Color CanopyBlue = new Color(0.13f, 0.42f, 0.9f, 1f);
        private static readonly Color BeamColour = new Color(0.35f, 0.75f, 1f, 0.5f);

        [MenuItem("Tools/OpenworldCrime/Parachute/Set Up Parachutes")]
        public static void Menu() => Debug.Log(Run());

        public static string Run()
        {
            var log = new StringBuilder("[ParachuteSetup]\n");
            Scene scene = SceneManager.GetActiveScene();
            ProceduralArt.EnsureFolder(ArtFolder);
            ProceduralArt.EnsureFolder(PrefabFolder);
            ProceduralArt.EnsureFolder(AnimFolder);

            // --- art
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(BagAlbedoPath);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(BagNormalPath);
            Material bagMat = ProceduralArt.Lit(ArtFolder + "/ParachuteBag.mat", Color.white, albedo, 0.25f, 0f, normal);
            Material canopyMat = ProceduralArt.Lit(ArtFolder + "/ParachuteCanopy.mat", CanopyBlue, null, 0.3f, 0f, null, true);
            Material canopyFade = ProceduralArt.LitTransparent(ArtFolder + "/ParachuteCanopyFade.mat", CanopyBlue);
            var columnTex = AssetDatabase.LoadAssetAtPath<Texture2D>(ColumnTexturePath);
            var ringTex = AssetDatabase.LoadAssetAtPath<Texture2D>(RingTexturePath);
            Material beamMat = ProceduralArt.Unlit(ArtFolder + "/ParachuteBeam.mat", BeamColour, columnTex, true);
            Material ringMat = ProceduralArt.Unlit(ArtFolder + "/ParachuteRing.mat", new Color(0.35f, 0.75f, 1f, 0.9f), ringTex, true);
            Texture2D dot = ProceduralArt.SoftDot(ArtFolder + "/ParachuteSparkle.png");
            Material sparkleMat = ProceduralArt.Particle(ArtFolder + "/ParachuteSparkle.mat", new Color(0.7f, 0.9f, 1f, 1f), dot, true);
            Texture2D icon = ProceduralArt.Icon(IconPath, ParachuteGlyph, new Color(0.06f, 0.2f, 0.45f, 0.85f));
            Sprite iconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
            log.AppendLine("  art: " + ArtFolder);

            // --- prefabs
            GameObject bagPrefab = BuildBagPrefab(bagMat);
            GameObject canopyPrefab = BuildCanopyPrefab(canopyMat);
            GameObject pickupPrefab = BuildPickupPrefab(bagPrefab, beamMat, ringMat, sparkleMat, iconSprite);
            log.AppendLine("  prefabs: " + BagPrefabPath + ", " + CanopyPrefabPath + ", " + PickupPrefabPath);

            // --- animation
            AnimationClip hang = SwimAnimationBuilder.Write(AnimFolder + "/Parachute_Hang.anim", "Parachute_Hang", 2.4f, HangPose);
            AssetDatabase.SaveAssets();
            log.AppendLine("  clip: " + AssetDatabase.GetAssetPath(hang));

            // --- characters
            WaterManager water = Object.FindFirstObjectByType<WaterManager>(FindObjectsInactive.Include);
            var wind = AssetDatabase.LoadAssetAtPath<AudioClip>(WindClipPath);
            if (wind == null) log.AppendLine("  WARNING: no wind sound at " + WindClipPath);
            var open = AssetDatabase.LoadAssetAtPath<AudioClip>(OpenClipPath);
            if (open == null) log.AppendLine("  WARNING: no parachute open sound at " + OpenClipPath);
            foreach (PlayableCharacter character in Resources.FindObjectsOfTypeAll<PlayableCharacter>())
            {
                if (EditorUtility.IsPersistent(character) || character.gameObject.scene != scene) continue;
                var parachute = character.GetComponent<PlayerParachute>();
                if (parachute == null) parachute = Undo.AddComponent<PlayerParachute>(character.gameObject);
                parachute.EditorSetup(water, bagPrefab, canopyPrefab, canopyFade, hang, wind, open);
                EditorUtility.SetDirty(parachute);
                log.AppendLine("  " + character.name + ": PlayerParachute");
            }

            // --- pickups on the towers
            log.AppendLine("  " + PlacePickups(scene, pickupPrefab));

            // --- UI
            log.AppendLine("  " + BuildUi(scene, iconSprite));

            EditorSceneManager.MarkSceneDirty(scene);
            return log.ToString();
        }

        // ---------------------------------------------------------------- prefabs

        private static GameObject BuildBagPrefab(Material material)
        {
            var root = new GameObject("ParachuteBag");
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            Mesh mesh = LoadMesh(BagModelPath);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = model.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, BagPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        [MenuItem("Tools/OpenworldCrime/Parachute/Rebuild Canopy Only")]
        public static void RebuildCanopyMenu()
        {
            Material canopyMat = ProceduralArt.Lit(ArtFolder + "/ParachuteCanopy.mat", CanopyBlue, null, 0.3f, 0f, null, true);
            Debug.Log("[ParachuteSetup] rebuilt " + AssetDatabase.GetAssetPath(BuildCanopyPrefab(canopyMat)));
        }

        // The imported canopy is 27 m wide with its lines and carries a whole harness / seat rig under the lines. That rig is cut
        // away (it stuck out of the character's chest), the rest is baked into one mesh asset (CanopyMeshPath; the FBX is not
        // changed): a 9 m wing whose two risers end at the prefab's pivot, ~0.55 m apart, so they leave the worn bag at the
        // shoulders.
        private static GameObject BuildCanopyPrefab(Material material)
        {
            var temp = new GameObject("Canopy Bake");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(CanopyModelPath);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.transform.SetParent(temp.transform, false);
            // Leading edge (open cells) to the front (+z).
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Bounds b = RendererBounds(model);
            model.transform.localScale *= 9f / Mathf.Max(0.01f, b.size.x); // the model's own root carries the FBX unit scale
            Mesh baked = BakeCanopyMesh(model, temp.transform);
            Object.DestroyImmediate(temp);

            var root = new GameObject("ParachuteCanopy");
            var visual = new GameObject("Model");
            visual.transform.SetParent(root.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = baked;
            var renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, CanopyPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private const string CanopyMeshPath = ArtFolder + "/ParachuteCanopyMesh.asset";

        // One mesh in the bake root's space: triangles entirely in the bottom 15% of the model (the harness rig) are dropped,
        // the lines reaching down into that band are kept and their lower ends (the risers) set the pivot.
        private static Mesh BakeCanopyMesh(GameObject model, Transform space)
        {
            var filters = model.GetComponentsInChildren<MeshFilter>(true);
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float minY = float.MaxValue, maxY = float.MinValue;
            var all = new List<Vector3[]>();
            foreach (MeshFilter mf in filters)
            {
                Vector3[] v = mf.sharedMesh.vertices;
                var w = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    w[i] = space.InverseTransformPoint(mf.transform.TransformPoint(v[i]));
                    minY = Mathf.Min(minY, w[i].y);
                    maxY = Mathf.Max(maxY, w[i].y);
                }
                all.Add(w);
            }
            float cut = minY + (maxY - minY) * 0.15f;
            Vector3 riserMin = Vector3.one * float.MaxValue, riserMax = Vector3.one * float.MinValue;
            for (int f = 0; f < filters.Length; f++)
            {
                Mesh mesh = filters[f].sharedMesh;
                Vector3[] w = all[f];
                Vector3[] n = mesh.normals;
                Vector2[] uv = mesh.uv;
                int[] tris = mesh.triangles;
                var remap = new Dictionary<int, int>();
                for (int t = 0; t < tris.Length; t += 3)
                {
                    float hi = Mathf.Max(w[tris[t]].y, Mathf.Max(w[tris[t + 1]].y, w[tris[t + 2]].y));
                    if (hi < cut) continue; // harness rig
                    for (int k = 0; k < 3; k++)
                    {
                        int src = tris[t + k];
                        if (w[src].y < cut)
                        {
                            riserMin = Vector3.Min(riserMin, w[src]);
                            riserMax = Vector3.Max(riserMax, w[src]);
                        }
                        if (!remap.TryGetValue(src, out int dst))
                        {
                            dst = positions.Count;
                            remap[src] = dst;
                            positions.Add(w[src]);
                            normals.Add(n.Length > src ? space.InverseTransformDirection(filters[f].transform.TransformDirection(n[src])).normalized : Vector3.up);
                            uvs.Add(uv.Length > src ? uv[src] : Vector2.zero);
                        }
                        triangles.Add(dst);
                    }
                }
            }
            Vector3 pivot = riserMin.x < float.MaxValue ? (riserMin + riserMax) * 0.5f : new Vector3(0f, cut, 0f);
            for (int i = 0; i < positions.Count; i++) positions[i] -= pivot;

            var baked = new Mesh { name = "ParachuteCanopy" };
            if (positions.Count > 65535) baked.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            baked.SetVertices(positions);
            baked.SetNormals(normals);
            baked.SetUVs(0, uvs);
            baked.SetTriangles(triangles, 0);
            baked.RecalculateBounds();
            baked.RecalculateTangents();
            baked.UploadMeshData(false);

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(CanopyMeshPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(baked, existing); // keeps the asset (and its references)
                Object.DestroyImmediate(baked);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return existing;
            }
            AssetDatabase.CreateAsset(baked, CanopyMeshPath);
            AssetDatabase.SaveAssets();
            return baked;
        }

        private static GameObject BuildPickupPrefab(GameObject bagPrefab, Material beamMat, Material ringMat, Material sparkleMat, Sprite icon)
        {
            var root = new GameObject("ParachutePickup") { layer = 2 }; // Ignore Raycast, like the shop door triggers
            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(2.2f, 2.4f, 2.2f);
            trigger.center = new Vector3(0f, 1.2f, 0f);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            var bag = (GameObject)PrefabUtility.InstantiatePrefab(bagPrefab);
            bag.name = "Bag";
            bag.transform.SetParent(visual.transform, false);
            bag.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            bag.transform.localScale = Vector3.one * 1.9f;
            Transform bagModel = bag.transform.Find("Model");
            // Centre the model on its own pivot so it spins in place.
            Bounds mb = bagModel.GetComponent<MeshFilter>().sharedMesh.bounds;
            bagModel.localPosition = new Vector3(-mb.center.x, -mb.center.y, -mb.center.z);
            var bob = bagModel.gameObject.AddComponent<MarkerBob>();
            var bobSo = new SerializedObject(bob);
            bobSo.FindProperty("bobHeight").floatValue = 0.12f;
            bobSo.FindProperty("bobSpeed").floatValue = 2.2f;
            bobSo.FindProperty("spinSpeed").floatValue = 70f;
            bobSo.ApplyModifiedPropertiesWithoutUndo();

            var beam = new GameObject("Beam");
            beam.transform.SetParent(visual.transform, false);
            beam.transform.localScale = new Vector3(0.7f, 16f, 0.7f);
            beam.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(ColumnMeshPath);
            var beamRenderer = beam.AddComponent<MeshRenderer>();
            beamRenderer.sharedMaterial = beamMat;
            NoShadows(beamRenderer);

            var ring = new GameObject("Ring");
            ring.transform.SetParent(visual.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localScale = new Vector3(2.6f, 2.6f, 1f);
            ring.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(QuadMeshPath);
            var ringRenderer = ring.AddComponent<MeshRenderer>();
            ringRenderer.sharedMaterial = ringMat;
            NoShadows(ringRenderer);

            var sparkles = new GameObject("Sparkles");
            sparkles.transform.SetParent(visual.transform, false);
            sparkles.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            sparkles.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            BuildSparkles(sparkles.AddComponent<ParticleSystem>(), sparkleMat);

            var blip = root.AddComponent<MapBlip>();
            var blipSo = new SerializedObject(blip);
            blipSo.FindProperty("icon").objectReferenceValue = icon;
            blipSo.FindProperty("iconColour").colorValue = Color.white;
            blipSo.FindProperty("backgroundColour").colorValue = new Color(0.06f, 0.25f, 0.55f, 0.92f);
            blipSo.FindProperty("size").floatValue = 24f;
            blipSo.ApplyModifiedPropertiesWithoutUndo();

            var pickup = root.AddComponent<ParachutePickup>();
            pickup.EditorSetup(visual, trigger, blip, AssetDatabase.LoadAssetAtPath<AudioClip>(PickupClipPath), bag.transform);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PickupPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void BuildSparkles(ParticleSystem ps, Material material)
        {
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.85f, 1f, 1f), new Color(1f, 1f, 1f, 1f));
            main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.Pause;
            var emission = ps.emission;
            emission.rateOverTime = 12f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.9f;
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            colour.color = gradient;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            NoShadows(renderer);
        }

        // ---------------------------------------------------------------- scene

        private static string PlacePickups(Scene scene, GameObject prefab)
        {
            GameObject root = FindRoot(scene, PickupsRootName);
            if (root != null) Undo.DestroyObjectImmediate(root);
            root = new GameObject(PickupsRootName);
            Undo.RegisterCreatedObjectUndo(root, "Parachute Pickups");
            int placed = 0;
            foreach (Transform building in HivizcayneRoofLayout.FindBuildings())
            {
                Collider col = HivizcayneRoofLayout.FindCollider(building);
                Vector3 p = HivizcayneRoofLayout.ToWorld(building, HivizcayneRoofLayout.Pickup);
                float y = HivizcayneRoofLayout.SurfaceY(col, p);
                if (float.IsNaN(y)) { Debug.LogWarning("[ParachuteSetup] No roof found on " + building.name, building); continue; }
                var pickup = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                pickup.name = "Parachute Pickup - " + building.name;
                pickup.transform.SetParent(root.transform, true);
                pickup.transform.SetPositionAndRotation(new Vector3(p.x, y, p.z), HivizcayneRoofLayout.Yaw(building));
                placed++;
            }
            return placed + " parachute pickups on hivizcayne roofs";
        }

        private static string BuildUi(Scene scene, Sprite icon)
        {
            GameObject touch = FindRoot(scene, "Touch Controls");
            GameObject gameplayUi = FindRoot(scene, "Gameplay UI");
            var controls = Object.FindFirstObjectByType<ControlsModeManager>(FindObjectsInactive.Include);
            if (touch == null || gameplayUi == null) return "WARNING: Touch Controls / Gameplay UI not found: no parachute button";
            Transform onFoot = touch.transform.Find("OnFootControls");
            Transform jump = onFoot != null ? onFoot.Find("Jump") : null;
            if (onFoot == null) return "WARNING: OnFootControls not found";

            // Touch button (above the jump button, same style).
            Transform old = onFoot.Find(TouchButtonName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var button = new GameObject(TouchButtonName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(button, "Parachute Button");
            button.layer = onFoot.gameObject.layer;
            var rect = (RectTransform)button.transform;
            rect.SetParent(onFoot, false);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(150f, 150f);
            rect.anchoredPosition = new Vector2(-226f, 800f);
            var image = button.GetComponent<Image>();
            image.sprite = icon;
            image.color = new Color(1f, 1f, 1f, 0.95f);
            var virtualButton = button.AddComponent<VirtualButton>();
            var vbSo = new SerializedObject(virtualButton);
            vbSo.FindProperty("buttonName").stringValue = "M_Parachute";
            vbSo.FindProperty("targetGraphic").objectReferenceValue = image;
            vbSo.FindProperty("pressedTint").colorValue = new Color(0.75f, 0.9f, 1f, 1f);
            vbSo.ApplyModifiedPropertiesWithoutUndo();
            Font labelFont = null;
            Transform jumpLabel = jump != null ? jump.Find("Label") : null;
            if (jumpLabel != null && jumpLabel.TryGetComponent(out Text jt)) labelFont = jt.font;
            if (labelFont == null) labelFont = Resources.GetBuiltinResource<Font>(ButtonFontName);
            var label = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            label.layer = button.layer;
            var labelRect = (RectTransform)label.transform;
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.sizeDelta = new Vector2(60f, 30f);
            labelRect.anchoredPosition = new Vector2(0f, 2f);
            var text = label.GetComponent<Text>();
            text.font = labelFont;
            text.fontSize = 20;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.UpperCenter;
            text.color = Color.white;
            text.text = "PARACHUTE";
            text.raycastTarget = false;
            button.SetActive(false);

            // Keyboard hint (like the interact prompt, above it).
            Transform oldHint = gameplayUi.transform.Find(HintName);
            if (oldHint != null) Undo.DestroyObjectImmediate(oldHint.gameObject);
            Transform interact = gameplayUi.transform.Find("InteractPrompt");
            var hint = new GameObject(HintName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(hint, "Parachute Prompt");
            hint.layer = gameplayUi.layer;
            var hintRect = (RectTransform)hint.transform;
            hintRect.SetParent(gameplayUi.transform, false);
            hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 0f);
            hintRect.sizeDelta = new Vector2(520f, 74f);
            hintRect.anchoredPosition = new Vector2(0f, 200f); // low, under the character: the wide glide camera has the character mid-screen
            var hintImage = hint.GetComponent<Image>();
            Image interactImage = interact != null ? interact.GetComponent<Image>() : null;
            hintImage.sprite = interactImage != null ? interactImage.sprite : AssetDatabase.LoadAssetAtPath<Sprite>(HintSpritePath);
            hintImage.type = interactImage != null ? interactImage.type : Image.Type.Simple;
            hintImage.color = new Color(0.45f, 0.75f, 1f, 1f);
            hintImage.raycastTarget = false;
            var hintText = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            hintText.layer = hint.layer;
            var hintTextRect = (RectTransform)hintText.transform;
            hintTextRect.SetParent(hintRect, false);
            hintTextRect.anchorMin = Vector2.zero;
            hintTextRect.anchorMax = Vector2.one;
            hintTextRect.offsetMin = hintTextRect.offsetMax = Vector2.zero;
            var ht = hintText.GetComponent<Text>();
            Text interactText = interact != null ? interact.GetComponentInChildren<Text>(true) : null;
            ht.font = interactText != null ? interactText.font : labelFont;
            ht.fontSize = 30;
            ht.alignment = TextAnchor.MiddleCenter;
            ht.color = Color.white;
            ht.text = "OPEN PARACHUTE  [B]";
            ht.raycastTarget = false;
            hint.SetActive(false);

            // The view (its own object under the canvas, not on the canvas itself).
            Transform oldView = gameplayUi.transform.Find(ViewName);
            if (oldView != null) Undo.DestroyObjectImmediate(oldView.gameObject);
            var viewGo = new GameObject(ViewName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(viewGo, "Parachute Button View");
            viewGo.transform.SetParent(gameplayUi.transform, false);
            var view = viewGo.AddComponent<ParachuteButtonView>();
            var viewSo = new SerializedObject(view);
            viewSo.FindProperty("touchButton").objectReferenceValue = button;
            viewSo.FindProperty("keyHint").objectReferenceValue = hint;
            viewSo.FindProperty("controlsMode").objectReferenceValue = controls;
            viewSo.ApplyModifiedPropertiesWithoutUndo();
            return "UI: touch button OnFootControls/" + TouchButtonName + " (M_Parachute), hint '" + HintName + "', view '" + ViewName + "'";
        }

        // ---------------------------------------------------------------- hang pose

        // Hanging under the canopy: hands up at the brake toggles beside the head, elbows out, legs hanging a little forward and
        // apart, swaying slowly. Muscle conventions as in SwimAnimationBuilder.
        private static void HangPose(float p, Dictionary<string, float> m, out Vector3 bodyPos, out Quaternion bodyRot)
        {
            float a = p * Mathf.PI * 2f;
            bodyPos = new Vector3(0f, 0.96f, 0f);
            bodyRot = Quaternion.identity;
            m["Spine Front-Back"] = 0.05f;
            m["Chest Front-Back"] = 0.05f;
            m["Spine Left-Right"] = 0.04f * Mathf.Sin(a);
            m["Neck Nod Down-Up"] = -0.15f;
            m["Head Nod Down-Up"] = -0.05f;
            m["Head Turn Left-Right"] = 0.08f * Mathf.Sin(a * 0.5f);
            for (int s = 0; s < 2; s++)
            {
                string side = s == 0 ? "Left" : "Right";
                float sway = Mathf.Sin(a + (s == 0 ? 0f : Mathf.PI));
                m[side + " Shoulder Down-Up"] = 0.35f;
                m[side + " Arm Down-Up"] = 0.95f;
                m[side + " Arm Front-Back"] = 0.05f;
                m[side + " Arm Twist In-Out"] = 0f;
                m[side + " Forearm Stretch"] = 0.15f + 0.04f * sway;
                m[side + " Forearm Twist In-Out"] = 0.2f;
                m[side + " Hand Down-Up"] = 0.1f;
                m[side + " Hand In-Out"] = 0f;

                m[side + " Upper Leg Front-Back"] = 0.38f + 0.05f * sway;
                m[side + " Upper Leg In-Out"] = 0.12f;
                m[side + " Upper Leg Twist In-Out"] = 0.05f;
                m[side + " Lower Leg Stretch"] = 0.78f + 0.08f * sway;
                m[side + " Foot Up-Down"] = 0.35f;
            }
            SwimAnimationBuilder.Fingers(m, -0.4f); // fists round the toggles
        }

        // Canopy arc over lines meeting at the bottom (u, v in -1..1).
        private static float ParachuteGlyph(float x, float y)
        {
            // Canopy: an arc band.
            float cy = -0.05f;
            float r = Mathf.Sqrt(x * x + (y - cy) * (y - cy));
            float arc = (y > cy + 0.12f) ? Mathf.Clamp01((0.11f - Mathf.Abs(r - 0.56f)) / 0.015f) : 0f;
            float lines = 0f;
            Vector2 meet = new Vector2(0f, -0.55f);
            foreach (float lx in new[] { -0.52f, -0.26f, 0f, 0.26f, 0.52f })
            {
                float ly = cy + Mathf.Sqrt(Mathf.Max(0f, 0.56f * 0.56f - lx * lx)) - 0.08f;
                lines = Mathf.Max(lines, ProceduralArt.Segment(x, y, new Vector2(lx, ly), meet, 0.025f));
            }
            float body = Mathf.Clamp01((0.11f - Vector2.Distance(new Vector2(x, y), new Vector2(0f, -0.64f))) / 0.015f);
            return Mathf.Max(arc, Mathf.Max(lines, body));
        }

        // ---------------------------------------------------------------- helpers

        public static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name) return root;
            return null;
        }

        private static Mesh LoadMesh(string modelPath)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(modelPath))
                if (o is Mesh mesh) return mesh;
            return null;
        }

        private static Bounds RendererBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            Bounds b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(go.transform.position, Vector3.zero);
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        private static void NoShadows(Renderer r)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }
    }
}
