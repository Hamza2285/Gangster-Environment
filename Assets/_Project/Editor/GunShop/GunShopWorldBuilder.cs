using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenworldCrime.Interiors;
using OpenworldCrime.Range;
using OpenworldCrime.Shop;
using OpenworldCrime.UI;
using OpenworldCrime.Weapons;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Builds the gun shop interior scene around the user's GANGFIRE master prefab (the prefab asset is never changed; the
    /// scene only adds gameplay objects and instance overrides) and places the street entrances in the open city scene.
    /// Re-running rebuilds the generated objects.
    /// </summary>
    public static class GunShopWorldBuilder
    {
        public const string InteriorScenePath = "Assets/_Project/Scenes/GunShop_Interior.unity";
        public const string InteriorSceneName = "GunShop_Interior";
        private const string MasterPrefabPath = "Assets/GANGFIRE Interior - Master Prefab.prefab";
        private const string EntrancePrefabPath = "Assets/_Project/Prefabs/Interiors/GunShopEntrance.prefab";
        private const string MaterialFolder = "Assets/_Project/Art/Interiors";

        /// <summary>The interior is authored ~4 km from the city: beyond the 800 m camera far plane, so the city is culled.</summary>
        public static readonly Vector3 InteriorOrigin = new Vector3(-4000f, 200f, -4000f);

        /// <summary>
        /// The package was modelled small for this player (~1.6 m tall: range ceiling 1.8 m). The instance is scaled up in the
        /// scene (the prefab asset is unchanged); every layout value below is in unscaled prefab space and multiplied by this.
        /// 1.25 keeps the service counter just above belt height (1.15 m) while doors become 2.6 m. The shooting range is
        /// enlarged on top of this by <see cref="GunShopRangeScaler"/> (RangeScale).
        /// </summary>
        public const float ShopScale = 1.25f;

        // Shop layout in GANGFIRE prefab space (measured from its colliders).
        private static readonly Vector3 SpawnPoint = new Vector3(7.1f, 0.02f, 5.05f);
        private static readonly Vector3 ExitDoorCenter = new Vector3(8.05f, 1.0f, 5.05f);
        private static readonly Vector3 CounterTrigger = new Vector3(3.95f, 1.0f, 7.4f);

        // Range layout. Shooter stands at the bench, facing -X. Lane / zone plans are in the original GANGFIRE prefab space
        // and pass through GunShopRangeScaler.Warp (their heights are set here, in prefab units).
        private static readonly Bounds LanePlan = new Bounds(new Vector3(1.6f, 0f, -3.9f), new Vector3(1.1f, 0f, 5.6f));
        private const float LaneHeight = 2.4f;
        private static readonly Bounds RangeZonePlan = new Bounds(new Vector3(-2.6f, 0f, -4.05f), new Vector3(14.4f, 0f, 7.3f));
        private const float LaneCenterZ = -3.9f;
        // Targets by real distance from the bench (m) and lateral offset from the lane centre (m, + = toward the corridor).
        private static readonly Vector2[] TargetLayout =
        {
            new Vector2(6f, -0.8f), new Vector2(8f, 2.4f), new Vector2(10f, -2.8f), new Vector2(12f, 0.9f),
            new Vector2(14f, -4.0f), new Vector2(16f, 3.2f), new Vector2(18f, -1.6f), new Vector2(20f, 0.4f),
        };
        /// <summary>Board bottom height above the floor (m): the board centre sits at about chest height.</summary>
        private const float TargetStandHeight = 0.55f;
        /// <summary>How far a sliding target travels toward the lane centre (m).</summary>
        private const float RailTravel = 3f;
        /// <summary>Downward faces lower than this (m above the floor) are left out of the range collision so the camera is
        /// never pushed into the player's head under fixtures; the 3.6 m ceiling itself is solid.</summary>
        private const float CeilingCollisionMinHeight = 2.6f;
        private const string GradeProfilePath = "Assets/_Project/Art/Interiors/GunShopInteriorGrade.asset";
        private const string RangeCollisionMeshPath = "Assets/_Project/Art/Interiors/RangeShellCollision.asset";

        private static readonly bool[] TargetSlides = { false, true, false, true, false, true, false, true };

        [MenuItem("Tools/OpenworldCrime/Gun Shop/Build Interior Scene")]
        public static void BuildInteriorMenu() => Debug.Log(BuildInterior());

        [MenuItem("Tools/OpenworldCrime/Gun Shop/Place City Entrances In Open Scene")]
        public static void PlaceEntrancesMenu() => Debug.Log(PlaceEntrances());

        // ---------------------------------------------------------------- interior

        public static string BuildInterior()
        {
            var log = new StringBuilder("[GunShopWorldBuilder] interior\n");
            EnsureFolder(MaterialFolder);
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(InteriorScenePath);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                if (File.Exists(InteriorScenePath)) scene = EditorSceneManager.OpenScene(InteriorScenePath, OpenSceneMode.Additive);
                else
                {
                    scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    EditorSceneManager.SaveScene(scene, InteriorScenePath);
                }
                openedHere = true;
            }
            SceneManager.SetActiveScene(scene);

            foreach (GameObject old in scene.GetRootGameObjects()) Object.DestroyImmediate(old);

            // Lighting of the interior scene (applies while it is the active scene at runtime).
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.55f, 0.48f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.37f, 0.33f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.18f, 0.17f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0.8f;
            RenderSettings.sun = null;
            Lightmapping.lightingSettings = GetLightingSettings();

            var root = new GameObject("GunShopInterior");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = InteriorOrigin;

            var master = AssetDatabase.LoadAssetAtPath<GameObject>(MasterPrefabPath);
            var shop = (GameObject)PrefabUtility.InstantiatePrefab(master, scene);
            shop.transform.SetParent(root.transform, false);
            shop.transform.localScale = Vector3.one * ShopScale;
            ScaleShopLighting(shop);
            DisableLightProbes(shop);

            // The prefab carries a lightmap baked in the package's original (missing) scene; it no longer matches the
            // meshes and over-brightens them. This scene bakes its own lighting instead, so the saved maps are cleared on
            // this instance only (the prefab asset keeps them).
            var savedLightmaps = shop.GetComponent<GangfirePrefabLightmaps>();
            if (savedLightmaps != null)
            {
                var lmSo = new SerializedObject(savedLightmaps);
                lmSo.FindProperty("colorMaps").arraySize = 0;
                lmSo.FindProperty("renderers").arraySize = 0;
                lmSo.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (Renderer r in shop.GetComponentsInChildren<Renderer>(true))
            {
                // Static for the bake and for static batching (the shop never moves); doors / lockers keep their flags.
                if (r.GetComponentInParent<GangfireHingedDoor>() != null) continue;
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            }

            // The package's own pop-up boards are replaced by working targets (instance override only).
            var targetTemplates = new List<Transform>();
            foreach (Transform t in shop.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Target_1") && t.GetComponent<MeshRenderer>() != null) targetTemplates.Add(t);
            foreach (Transform t in targetTemplates) t.gameObject.SetActive(false);

            // Enlarge the range section (corridor mouth, shop room and prefab asset unchanged).
            log.Append(GunShopRangeScaler.Apply(shop));
            AddRangeReflectionProbe(root.transform);

            var gameplay = new GameObject("Gameplay").transform;
            gameplay.SetParent(root.transform, false);

            // Walkable range: the range shell mesh has no collider in the prefab.
            Transform shell = shop.transform.Find("03 Original Corridor and Range/Level_Interior/Interior_Shell");
            BuildRangeCollision(gameplay, shell);
            AddGradeVolume(root.transform);

            var interior = root.AddComponent<InteriorScene>();
            Transform spawn = NewPoint("Spawn Point", gameplay, SpawnPoint, 270f);
            Set(interior, "spawnPoint", spawn);

            var door = NewTrigger("Exit Door", gameplay, ExitDoorCenter, new Vector3(0.9f, 2f, 1.6f));
            door.AddComponent<InteriorExitDoor>();

            // Shop counter (its displayed weapons are added by GunShopShowroomBuilder below).
            var counterGo = NewTrigger("Shop Counter", gameplay, CounterTrigger, new Vector3(1.2f, 2f, 2.6f));
            var counter = counterGo.AddComponent<ShopCounter>();
            var purchase = counterGo.AddComponent<AudioSource>();
            purchase.playOnAwake = false;
            purchase.spatialBlend = 0f;
            purchase.clip = FindClip("Collect", "collect", "Pickup", "pickup");
            Set(counter, "purchaseAudio", purchase);

            // Range
            var rangeGo = new GameObject("Shooting Range");
            rangeGo.transform.SetParent(gameplay, false);
            var range = rangeGo.AddComponent<ShootingRangeController>();
            var buzzer = rangeGo.AddComponent<AudioSource>();
            buzzer.playOnAwake = false;
            buzzer.spatialBlend = 0f;
            buzzer.clip = FindClip("Buzzer", "buzzer", "Beep", "beep", "Alarm");
            Set(range, "buzzer", buzzer);

            Bounds zone = RangeBounds(RangeZonePlan, GunShopRangeScaler.CeilingTarget);
            var zoneGo = NewTrigger("Weapons Allowed Zone (Range)", gameplay, zone.center, zone.size);
            zoneGo.AddComponent<InteriorWeaponZone>();
            Bounds laneBounds = RangeBounds(LanePlan, LaneHeight);
            var laneGo = NewTrigger("Range Lane", gameplay, laneBounds.center, laneBounds.size);
            var lane = laneGo.AddComponent<RangeLane>();
            Set(lane, "range", range);

            MeshRenderer boardTemplate = targetTemplates.Count > 0 ? targetTemplates[0].GetComponent<MeshRenderer>() : null;
            AudioClip hitClip = FindClip("metal", "Metal", "Impact");
            var targets = new List<RangeTarget>();
            float benchX = GunShopRangeScaler.WarpX(GunShopRangeScaler.BenchX) * ShopScale;
            float laneZ = GunShopRangeScaler.WarpZ(LaneCenterZ) * ShopScale;
            for (int i = 0; i < TargetLayout.Length; i++)
            {
                var position = new Vector3(benchX - TargetLayout[i].x, 0f, laneZ + TargetLayout[i].y);
                targets.Add(BuildTarget(rangeGo.transform, i, position, TargetSlides[i], boardTemplate, hitClip));
            }
            var rangeSo = new SerializedObject(range);
            SerializedProperty targetsProp = rangeSo.FindProperty("targets");
            targetsProp.arraySize = targets.Count;
            for (int i = 0; i < targets.Count; i++) targetsProp.GetArrayElementAtIndex(i).objectReferenceValue = targets[i];
            rangeSo.ApplyModifiedPropertiesWithoutUndo();

            // GTA showroom (wall rack + counter weapons), automatic range doors, shooting spot.
            log.Append(GunShopShowroomBuilder.Apply(root.transform));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, InteriorScenePath);
            AddToBuildSettings(InteriorScenePath);
            if (previousActive.IsValid()) SceneManager.SetActiveScene(previousActive);
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
            log.AppendLine($"  {InteriorScenePath}: GANGFIRE master at {InteriorOrigin}, spawn, exit door, counter + preview stage, range ({targets.Count} targets), weapon zone, fill lights; in Build Settings");
            return log.ToString();
        }

        /// <summary>
        /// Re-applies the gameplay tuning (spawn, player key lights, colour grade, range collision, showroom weapons, range
        /// doors and shooting spot) to the saved interior without rebuilding it, so the baked lighting stays valid.
        /// </summary>
        [MenuItem("Tools/OpenworldCrime/Gun Shop/Update Interior Gameplay (keeps the bake)")]
        public static void UpdateInteriorMenu() => Debug.Log(UpdateInterior());

        public static string UpdateInterior()
        {
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(InteriorScenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(InteriorScenePath, OpenSceneMode.Additive);
            GameObject root = null;
            foreach (GameObject r in scene.GetRootGameObjects()) if (r.GetComponent<InteriorScene>() != null) root = r;
            if (root == null) return "[GunShopWorldBuilder] interior root not found";
            Transform gameplay = root.transform.Find("Gameplay");
            Transform spawn = gameplay.Find("Spawn Point");
            if (spawn != null) spawn.localPosition = SpawnPoint * ShopScale;
            // The extra realtime player lights made the character glow next to the baked room; light probes light him now.
            foreach (Light l in gameplay.GetComponentsInChildren<Light>(true))
                if (l.name.StartsWith("Player Key Light") || l.name.StartsWith("Realtime Fill")) Object.DestroyImmediate(l.gameObject);
            Transform shopRoot = root.transform.Find("GANGFIRE Interior - Master Prefab");
            if (shopRoot != null) DisableLightProbes(shopRoot.gameObject);
            Transform oldCollision = gameplay.Find("Range Shell Collision");
            if (oldCollision != null) Object.DestroyImmediate(oldCollision.gameObject);
            Transform shell = root.transform.Find("GANGFIRE Interior - Master Prefab/03 Original Corridor and Range/Level_Interior/Interior_Shell");
            BuildRangeCollision(gameplay, shell);
            Transform oldGrade = root.transform.Find("Interior Grade");
            if (oldGrade != null) Object.DestroyImmediate(oldGrade.gameObject);
            AddGradeVolume(root.transform);
            string showroom = GunShopShowroomBuilder.Apply(root.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (previousActive.IsValid()) SceneManager.SetActiveScene(previousActive);
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
            return "[GunShopWorldBuilder] interior gameplay updated (bake kept)\n" + showroom;
        }

        // Baked light falloff does not follow the transform scale: lengthen ranges / areas by the scale and raise point and
        // spot intensities by its square so the room bakes as the package intended (instance overrides only).
        private static void ScaleShopLighting(GameObject shop)
        {
            foreach (Light light in shop.GetComponentsInChildren<Light>(true))
            {
                if (light.type == LightType.Directional) continue;
                if (light.type == LightType.Rectangle || light.type == LightType.Disc) light.areaSize *= ShopScale;
                else
                {
                    light.range *= ShopScale;
                    light.intensity *= ShopScale * ShopScale;
                }
            }
            foreach (ReflectionProbe probe in shop.GetComponentsInChildren<ReflectionProbe>(true))
            {
                probe.size *= ShopScale;
                probe.center *= ShopScale;
            }
        }

        // The package's light probes pick up its 9-intensity ceiling softboxes at close range and made the player glow
        // (several times brighter than the lightmapped floor next to him). Without probes, dynamic objects (player,
        // weapons, the shop preview) use the interior scene's ambient, which is tuned to the baked room.
        private static void DisableLightProbes(GameObject shop)
        {
            foreach (LightProbeGroup group in shop.GetComponentsInChildren<LightProbeGroup>(true))
            {
                group.probePositions = new Vector3[0];
                group.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(group);
            }
        }

        // Walls, floor and the (now 3.6 m) ceiling of the range shell. Downward faces of low overhangs are left out so the
        // third-person camera is not pushed into the player's head under them.
        private static void BuildRangeCollision(Transform gameplay, Transform shell)
        {
            if (shell == null || shell.GetComponent<MeshFilter>() == null) return;
            Mesh source = shell.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = source.vertices;
            var triangles = new List<int>();
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                int[] ids = source.GetTriangles(sub);
                for (int i = 0; i < ids.Length; i += 3)
                {
                    Vector3 a = shell.TransformPoint(vertices[ids[i]]), b = shell.TransformPoint(vertices[ids[i + 1]]), c = shell.TransformPoint(vertices[ids[i + 2]]);
                    Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                    if (normal.y < -0.5f && (a.y + b.y + c.y) / 3f - gameplay.position.y < CeilingCollisionMinHeight) continue; // low overhangs
                    triangles.Add(ids[i]); triangles.Add(ids[i + 1]); triangles.Add(ids[i + 2]);
                }
            }
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(RangeCollisionMeshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "RangeShellCollision" };
                AssetDatabase.CreateAsset(mesh, RangeCollisionMeshPath);
            }
            mesh.Clear();
            mesh.indexFormat = vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            var col = new GameObject("Range Shell Collision");
            col.transform.SetParent(gameplay, false);
            col.transform.SetPositionAndRotation(shell.position, shell.rotation);
            col.transform.localScale = shell.lossyScale;
            col.AddComponent<MeshCollider>().sharedMesh = mesh;
            col.isStatic = true;
        }

        // Toned-down grade for the freshly baked lighting (the package's own volumes were set up for its old lightmap):
        // a higher-priority global volume, only present while the interior scene is loaded.
        private static void AddGradeVolume(Transform root)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(GradeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, GradeProfilePath);
                var color = profile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
                color.postExposure.value = 0.35f;
                color.saturation.value = -5f;
                color.contrast.value = 8f;
                foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            var go = new GameObject("Interior Grade");
            go.transform.SetParent(root, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 80f;
            volume.sharedProfile = profile;
        }

        /// <param name="localPosition">Floor position under the target, in the interior root's space (metres).</param>
        private static RangeTarget BuildTarget(Transform parent, int index, Vector3 localPosition, bool slides, MeshRenderer boardTemplate, AudioClip hitClip)
        {
            // Root yawed so the hinge's local X runs across the lane; the board faces +X (the shooter).
            var root = new GameObject("Target " + (index + 1));
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            root.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(root.transform, false);
            hinge.localPosition = new Vector3(0f, TargetStandHeight - 0.12f, 0f);

            // Floor plate and a post up to the hinge.
            Material standMaterial = GetMaterial("Range Stand", new Color(0.12f, 0.12f, 0.13f), false);
            NewStandPart(root.transform, "Stand Base", new Vector3(0f, 0.02f, 0f), new Vector3(0.5f, 0.04f, 0.3f), standMaterial);
            NewStandPart(root.transform, "Stand Post", new Vector3(0f, TargetStandHeight * 0.5f, 0.05f), new Vector3(0.06f, TargetStandHeight, 0.06f), standMaterial);

            var board = new GameObject("Board");
            board.transform.SetParent(hinge, false);
            if (boardTemplate != null)
            {
                board.AddComponent<MeshFilter>().sharedMesh = boardTemplate.GetComponent<MeshFilter>().sharedMesh;
                var renderer = board.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = boardTemplate.sharedMaterials;
                // Same orientation / size as the package board, re-centred so it stands on the hinge.
                board.transform.rotation = boardTemplate.transform.rotation;
                board.transform.localScale = boardTemplate.transform.lossyScale;
                board.transform.position = hinge.position;
                Bounds world = renderer.bounds;
                board.transform.position += hinge.position + Vector3.up * (0.12f + world.extents.y) - world.center;
            }
            float height = boardTemplate != null ? boardTemplate.bounds.size.y : 0.88f;
            float width = boardTemplate != null ? Mathf.Max(boardTemplate.bounds.size.x, boardTemplate.bounds.size.z) : 0.41f;

            // Zones: head = top 22 %, torso = the rest (thin boxes on the hinge so they flip with the board).
            NewZone(hinge, "Head", RangeHitZone.Head, new Vector3(0f, 0.12f + height * 0.89f, 0f), new Vector3(width * 0.55f, height * 0.22f, 0.06f));
            NewZone(hinge, "Torso", RangeHitZone.Torso, new Vector3(0f, 0.12f + height * 0.39f, 0f), new Vector3(width, height * 0.78f, 0.06f));

            var target = root.AddComponent<RangeTarget>();
            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.maxDistance = 35f;
            audio.clip = hitClip;
            var so = new SerializedObject(target);
            so.FindProperty("hinge").objectReferenceValue = hinge;
            so.FindProperty("hitAudio").objectReferenceValue = audio;
            // Slide toward the lane centre (parent space: across the range).
            float laneZ = GunShopRangeScaler.WarpZ(LaneCenterZ) * ShopScale;
            so.FindProperty("railOffset").vector3Value = slides ? new Vector3(0f, 0f, localPosition.z < laneZ ? RailTravel : -RailTravel) : Vector3.zero;
            so.ApplyModifiedPropertiesWithoutUndo();
            return target;
        }

        private static void NewStandPart(Transform parent, string name, Vector3 localPosition, Vector3 size, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = size;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        // The package's range has no active reflection probe (its interior-wide one is disabled): one baked, box-projected
        // 128 probe covers the enlarged range so the guns and metal fittings reflect the room.
        private static void AddRangeReflectionProbe(Transform root)
        {
            const float captureHeight = 1.5f;
            Bounds box = RangeBounds(RangeZonePlan, GunShopRangeScaler.CeilingTarget);
            Vector3 center = box.center * ShopScale;
            Vector3 size = box.size * ShopScale;
            var go = new GameObject("Range Reflection Probe");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(center.x, captureHeight, center.z);
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.resolution = 128;
            probe.boxProjection = true;
            probe.size = size;
            probe.center = new Vector3(0f, center.y - captureHeight, 0f);
            probe.importance = 1;
        }

        // Plan bounds (original prefab space) through the range warp; y from the floor up to the given prefab-space height.
        private static Bounds RangeBounds(Bounds plan, float height)
        {
            Bounds warped = GunShopRangeScaler.Warp(plan);
            var b = new Bounds();
            b.SetMinMax(new Vector3(warped.min.x, 0f, warped.min.z), new Vector3(warped.max.x, height, warped.max.z));
            return b;
        }

        private static void NewZone(Transform hinge, string name, RangeHitZone zone, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(hinge, false);
            go.transform.localPosition = center;
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            var z = go.AddComponent<RangeTargetZone>();
            var so = new SerializedObject(z);
            so.FindProperty("zone").enumValueIndex = (int)zone;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static LightingSettings GetLightingSettings()
        {
            string path = MaterialFolder + "/GunShopInterior.lighting";
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (settings != null) return settings;
            settings = new LightingSettings { name = "GunShopInterior" };
            AssetDatabase.CreateAsset(settings, path);
            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            settings.lightmapResolution = 11f; // ~14 texels per unscaled metre: the same lightmap budget after the 1.25 scale
            settings.lightmapMaxSize = 1024;
            settings.lightmapPadding = 4;
            settings.lightmapCompression = LightmapCompression.NormalQuality;
            settings.directionalityMode = LightmapsMode.NonDirectional;
            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
            settings.ao = true;
            settings.aoMaxDistance = 0.35f;
            settings.maxBounces = 2;
            settings.directSampleCount = 32;
            settings.indirectSampleCount = 128;
            settings.environmentSampleCount = 32;
            settings.filteringMode = LightingSettings.FilterMode.Auto;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        /// <summary>
        /// Bakes the interior's lightmaps, light probes and reflection probes with only the interior open (a multi-scene bake
        /// would bake the whole city). Call <see cref="FinishBake"/> when Lightmapping.isRunning is false.
        /// </summary>
        [MenuItem("Tools/OpenworldCrime/Gun Shop/Bake Interior Lighting (opens the interior alone)")]
        public static void BakeInteriorMenu() => Debug.Log(StartBake());

        public static string StartBake()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "Bake cancelled.";
            cityPathBeforeBake = SceneManager.GetActiveScene().path;
            Scene scene = EditorSceneManager.OpenScene(InteriorScenePath, OpenSceneMode.Single);
            Lightmapping.lightingSettings = GetLightingSettings();
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            Lightmapping.bakeCompleted += OnBakeCompleted;
            return Lightmapping.BakeAsync() ? "[GunShopWorldBuilder] interior bake started" : "[GunShopWorldBuilder] bake could not start";
        }

        private static string cityPathBeforeBake;

        private static void OnBakeCompleted()
        {
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            EditorApplication.delayCall += () => Debug.Log(FinishBake());
        }

        public static string FinishBake()
        {
            Scene scene = SceneManager.GetSceneByPath(InteriorScenePath);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.SaveScene(scene);
            string city = string.IsNullOrEmpty(cityPathBeforeBake) ? "Assets/_Project/Scenes/Gangstar_Gameplay.unity" : cityPathBeforeBake;
            if (city != InteriorScenePath && File.Exists(city)) EditorSceneManager.OpenScene(city, OpenSceneMode.Single);
            return "[GunShopWorldBuilder] interior bake saved; reopened " + city;
        }

        // ---------------------------------------------------------------- city entrances

        public static string PlaceEntrances()
        {
            var log = new StringBuilder("[GunShopWorldBuilder] entrances\n");
            GameObject prefab = BuildEntrancePrefab();
            Scene city = SceneManager.GetActiveScene();
            GameObject container = null;
            foreach (GameObject r in city.GetRootGameObjects()) if (r.name == "Gun Shop Entrances") container = r;
            if (container != null) Object.DestroyImmediate(container);
            container = new GameObject("Gun Shop Entrances");
            SceneManager.MoveGameObjectToScene(container, city);

            // 1: the GANGFIRE storefront (door faces +Z); 2: the white storefront block near the start (faces -Z).
            PlaceEntrance(container.transform, prefab, "gunshop_gangfire", new Vector3(856f, 0f, 508.1f), 0f, false, log);
            PlaceEntrance(container.transform, prefab, "gunshop_downtown", new Vector3(492f, 0f, 215.0f), 180f, true, log);
            EditorSceneManager.MarkSceneDirty(city);
            return log.ToString();
        }

        private static void PlaceEntrance(Transform parent, GameObject prefab, string id, Vector3 position, float facing, bool addSign, StringBuilder log)
        {
            RaycastHit hit;
            if (Physics.Raycast(position + Vector3.up * 40f, Vector3.down, out hit, 80f, ~0, QueryTriggerInteraction.Ignore)) position.y = hit.point.y;
            var entrance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
            entrance.transform.SetParent(parent, false);
            entrance.name = "Gun Shop Entrance - " + id;
            entrance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, facing, 0f));
            var so = new SerializedObject(entrance.GetComponent<ShopEntrance>());
            so.FindProperty("entranceId").stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
            if (addSign) AddSign(entrance.transform);
            log.AppendLine($"  {id} at {position}");
        }

        private static void AddSign(Transform entrance)
        {
            // "GUN SHOP" board on the facade behind the marker (the marker faces the street, the facade is behind it).
            var sign = new GameObject("Gun Shop Sign");
            sign.transform.SetParent(entrance, false);
            sign.transform.localPosition = new Vector3(0f, 3.6f, -1.4f);
            sign.transform.localRotation = Quaternion.identity;
            var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            back.name = "Board";
            Object.DestroyImmediate(back.GetComponent<Collider>());
            back.transform.SetParent(sign.transform, false);
            back.transform.localScale = new Vector3(4.2f, 0.9f, 0.08f);
            back.GetComponent<MeshRenderer>().sharedMaterial = GetMaterial("GunShop Sign Board", new Color(0.05f, 0.05f, 0.06f), false);
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(sign.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            textGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var text = textGo.AddComponent<TextMeshPro>();
            text.text = "GUN SHOP";
            text.fontSize = 7f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(1f, 0.15f, 0.18f);
            text.rectTransform.sizeDelta = new Vector2(4f, 0.9f);
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        }

        private static GameObject BuildEntrancePrefab()
        {
            EnsureFolder(Path.GetDirectoryName(EntrancePrefabPath).Replace('\\', '/'));
            var root = new GameObject("GunShopEntrance");
            root.layer = LayerMask.NameToLayer("Ignore Raycast"); // aim / bullet rays pass through the trigger
            try
            {
                var trigger = root.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.center = new Vector3(0f, 1f, 0f);
                trigger.size = new Vector3(2.2f, 2f, 2.2f);
                var entrance = root.AddComponent<ShopEntrance>();
                var exit = new GameObject("Exit Point").transform;
                exit.SetParent(root.transform, false);
                exit.localPosition = new Vector3(0f, 0.05f, 2.4f);
                var so = new SerializedObject(entrance);
                so.FindProperty("interiorSceneName").stringValue = InteriorSceneName;
                so.FindProperty("exitPoint").objectReferenceValue = exit;
                so.ApplyModifiedPropertiesWithoutUndo();

                // GTA marker: a soft yellow column with a bobbing ring.
                var column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                column.name = "Marker Column";
                Object.DestroyImmediate(column.GetComponent<Collider>());
                column.transform.SetParent(root.transform, false);
                column.transform.localPosition = new Vector3(0f, 0.75f, 0f);
                column.transform.localScale = new Vector3(1.1f, 0.75f, 1.1f);
                var columnRenderer = column.GetComponent<MeshRenderer>();
                columnRenderer.sharedMaterial = GetMaterial("Entrance Marker", new Color(1f, 0.8f, 0.1f, 0.28f), true);
                columnRenderer.shadowCastingMode = ShadowCastingMode.Off;
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ring.name = "Marker Ring";
                Object.DestroyImmediate(ring.GetComponent<Collider>());
                ring.transform.SetParent(root.transform, false);
                ring.transform.localPosition = new Vector3(0f, 1.7f, 0f);
                ring.transform.localScale = new Vector3(0.45f, 0.03f, 0.45f);
                var ringRenderer = ring.GetComponent<MeshRenderer>();
                ringRenderer.sharedMaterial = GetMaterial("Entrance Marker Ring", new Color(1f, 0.85f, 0.2f, 0.8f), true);
                ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
                ring.AddComponent<MarkerBob>();

                var blip = root.AddComponent<MapBlip>();
                var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(WeaponBuilder.CatalogPath);
                WeaponDefinition icon = catalog != null ? catalog.FindById("deagle_chrome") : null;
                var blipSo = new SerializedObject(blip);
                blipSo.FindProperty("icon").objectReferenceValue = icon != null ? icon.icon : null;
                blipSo.FindProperty("backgroundColour").colorValue = new Color(0.75f, 0.08f, 0.1f, 0.95f);
                blipSo.FindProperty("size").floatValue = 28f;
                blipSo.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(root, EntrancePrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ---------------------------------------------------------------- helpers

        private static Transform NewPoint(string name, Transform parent, Vector3 localPosition, float yaw)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition * ShopScale;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return t;
        }

        private static GameObject NewTrigger(string name, Transform parent, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center * ShopScale;
            go.layer = LayerMask.NameToLayer("Ignore Raycast");
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size * ShopScale;
            return go;
        }

        private static Material GetMaterial(string name, Color color, bool transparent)
        {
            EnsureFolder(MaterialFolder);
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(transparent ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            if (transparent)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetShaderPassEnabled("ShadowCaster", false);
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static AudioClip FindClip(params string[] names)
        {
            foreach (string n in names)
                foreach (string guid in AssetDatabase.FindAssets(n + " t:AudioClip"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.Contains("Invector") || path.Contains("_Project") || path.Contains("RealisticCarController")) return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                }
            return null;
        }

        private static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == path)) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
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
