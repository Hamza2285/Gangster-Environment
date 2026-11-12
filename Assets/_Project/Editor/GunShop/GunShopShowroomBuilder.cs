using System.Collections.Generic;
using System.Text;
using OpenworldCrime.Interiors;
using OpenworldCrime.Range;
using OpenworldCrime.Shop;
using OpenworldCrime.Weapons;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// GTA (Ammu-Nation) presentation of the gun shop interior, applied on top of <see cref="GunShopWorldBuilder"/>'s scene
    /// without touching the baked lighting: every catalog weapon hangs on the back wall rack (long guns) or stands on the
    /// counter / island case tops (handguns, melee, SMGs, heavy) as a <see cref="ShopDisplaySlot"/>, replacing the package's wall guns
    /// (instance overrides only); the range doors open automatically (<see cref="ProximityDoorTrigger"/>); the range
    /// challenge gets its fixed shooting spot at the bench.
    /// All positions are in metres in the interior root's space (the GANGFIRE instance is already scaled by ShopScale).
    /// </summary>
    public static class GunShopShowroomBuilder
    {
        // Back wall rack: the package's four wood panels (measured from its wall guns), front face of the rack and the
        // usable height band.
        private const float WallFrontX = 1.745f;
        private static readonly float[] WallPanelZ = { 5.675f, 7.55f, 9.375f, 11.25f };
        /// <summary>Row heights (bounds centre) of the package's own wall guns: its rack hooks sit under these rows.</summary>
        private static readonly float[] WallRowY = { 2.15f, 1.75f, 1.35f };
        private const float WallBandBottom = 1.02f;
        private const float WallBandTop = 2.40f;
        private static readonly Vector3 WallViewDirection = new Vector3(0f, 0.1f, 1f);

        // Service counter glass top: handguns and melee stand on it facing the player, the wall rack behind them. (The
        // package's guns inside the glass case stay: their shadows are in the baked lightmap.)
        private const float CounterTopY = 1.175f;
        private const float CounterX = 3.95f;
        private const float CounterStartZ = 7.35f;
        private const float CounterEndZ = 11.4f;
        private const float CounterGap = 0.18f;
        private static readonly Vector3 StandingViewDirection = new Vector3(0f, 0.3f, 1f);

        // Island display case top (SMGs and heavy weapons stand on it, seen from the counter side).
        private const float IslandX = 7.56f;
        private const float IslandStartZ = 7.75f;
        private const float IslandEndZ = 10.65f;

        private static readonly WeaponCategory[] WallOrder = { WeaponCategory.Shotgun, WeaponCategory.AssaultRifle, WeaponCategory.Sniper };
        private static readonly WeaponCategory[] CounterOrder = { WeaponCategory.Pistol, WeaponCategory.Melee }; // melee only if the catalog sells any
        private static readonly WeaponCategory[] IslandOrder = { WeaponCategory.SMG, WeaponCategory.Heavy };

        // Range doors: the two hinged leaves of the corridor; the trigger reaches this far either side of them.
        private static readonly Vector3 DoorTriggerCenter = new Vector3(6.575f, 1.25f, 4.06f);
        private static readonly Vector3 DoorTriggerSize = new Vector3(2.2f, 2.5f, 5.2f);
        private const float DoorSpeed = 200f;

        /// <summary>Feet position at the bench (behind the bench edge, lane centre), facing downrange (-X).</summary>
        private static readonly Vector3 ShootingSpot = new Vector3(-0.72f, 0.05f, -7.48f);
        private const float ShootingYaw = 270f;

        public static string Apply(Transform root)
        {
            var log = new StringBuilder();
            Transform gameplay = root.Find("Gameplay");
            Transform shop = root.Find("GANGFIRE Interior - Master Prefab");
            var counter = gameplay != null ? gameplay.GetComponentInChildren<ShopCounter>(true) : null;
            var range = gameplay != null ? gameplay.GetComponentInChildren<ShootingRangeController>(true) : null;
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(WeaponBuilder.CatalogPath);
            if (gameplay == null || shop == null || counter == null || range == null || catalog == null)
                return "  showroom: interior gameplay, GANGFIRE instance, counter, range or catalog missing\n";

            foreach (string old in new[] { "Preview Pivot", "Preview Camera", "Shop Displays", "Range Doors Trigger", "Shooting Spot", "Range Gate Trigger", "Downrange Blocker" })
            {
                Transform t = gameplay.Find(old);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }

            log.Append(BuildDisplays(root, gameplay, shop, counter, catalog));
            log.Append(BakeShopReflectionProbe(root));
            log.Append(BuildDoors(gameplay, shop));
            log.Append(BuildRangeGates(gameplay, shop));
            var spot = new GameObject("Shooting Spot").transform;
            spot.SetParent(gameplay, false);
            spot.localPosition = ShootingSpot;
            spot.localRotation = Quaternion.Euler(0f, ShootingYaw, 0f);
            Set(range, "shootingSpot", spot);
            log.AppendLine("  range: shooting spot at the bench " + ShootingSpot);
            return log.ToString();
        }

        // ---------------------------------------------------------------- displays

        private static string BuildDisplays(Transform root, Transform gameplay, Transform shop, ShopCounter counter, WeaponCatalog catalog)
        {
            // The package's static wall guns make room for the real catalog (instance override).
            int hidden = 0;
            Transform approved = shop.Find("01 Shop/GANGFIRE Approved Shop");
            if (approved != null)
                foreach (Transform t in approved)
                    if (t.name.StartsWith("Batched original guns - Wall") && t.gameObject.activeSelf) { t.gameObject.SetActive(false); hidden++; }

            var displays = new GameObject("Shop Displays").transform;
            displays.SetParent(gameplay, false);
            var wall = new GameObject("Wall Rack").transform;
            wall.SetParent(displays, false);
            var top = new GameObject("Counter Top").transform;
            top.SetParent(displays, false);
            var slots = new List<ShopDisplaySlot>();

            // Wall: long guns in category order, top to bottom then panel by panel (left to right from the counter).
            List<WeaponDefinition> wallWeapons = Collect(catalog, WallOrder);
            int perPanel = Mathf.CeilToInt(wallWeapons.Count / (float)WallPanelZ.Length);
            for (int panel = 0, index = 0; panel < WallPanelZ.Length && index < wallWeapons.Count; panel++)
            {
                int count = Mathf.Min(perPanel, wallWeapons.Count - index);
                float pitch = (WallBandTop - WallBandBottom) / count;
                float firstY = (WallBandTop + WallBandBottom) * 0.5f + pitch * (count - 1) * 0.5f;
                for (int row = 0; row < count; row++, index++)
                {
                    float y = perPanel <= WallRowY.Length ? WallRowY[row] : firstY - row * pitch;
                    WeaponDefinition weapon = wallWeapons[index];
                    // Barrel toward +Z (screen right from the counter), right side facing the room.
                    ShopDisplaySlot slot = NewSlot(wall, root, weapon, Quaternion.LookRotation(Vector3.right), Quaternion.identity, WallViewDirection, out Bounds b);
                    if (slot == null) continue;
                    slot.transform.localPosition = new Vector3(WallFrontX + b.extents.x + 0.012f, y, WallPanelZ[panel]);
                    slots.Add(slot);
                }
            }

            // Counter (handguns, melee) and island case (SMGs, heavy): standing upright, right side toward the viewer.
            int onCounter = StandInRow(top, root, catalog, CounterOrder, Vector3.right, CounterX, CounterStartZ, CounterEndZ, slots);
            var island = new GameObject("Island Case").transform;
            island.SetParent(displays, false);
            int onIsland = StandInRow(island, root, catalog, IslandOrder, Vector3.left, IslandX, IslandStartZ, IslandEndZ, slots);

            // Spot light that follows the selected weapon while browsing (realtime, no shadows; off outside the menu).
            var lightGo = new GameObject("Shop Highlight Light");
            lightGo.transform.SetParent(displays, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(1f, 0.93f, 0.82f);
            light.intensity = 2.2f;
            light.range = 2.6f;
            light.spotAngle = 42f;
            light.innerSpotAngle = 18f;
            light.shadows = LightShadows.None;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            light.enabled = false;

            var so = new SerializedObject(counter);
            SerializedProperty slotsProp = so.FindProperty("displaySlots");
            slotsProp.arraySize = slots.Count;
            for (int i = 0; i < slots.Count; i++) slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
            so.FindProperty("highlightLight").objectReferenceValue = light;
            so.ApplyModifiedPropertiesWithoutUndo();
            return $"  showroom: {wallWeapons.Count} weapons on the wall rack, {onCounter} on the counter, {onIsland} on the island case, {hidden} package wall gun groups hidden\n";
        }

        /// <summary>
        /// Stands the weapons of <paramref name="order"/> on a glass top (height <see cref="CounterTopY"/>) in a row along Z at
        /// <paramref name="x"/>, centred between the two Z limits. <paramref name="towardViewer"/> is the horizontal side the
        /// camera looks from: guns show their right side with the barrel to the viewer's right; melee weapons stand on their
        /// handle with the head up.
        /// </summary>
        private static int StandInRow(Transform parent, Transform root, WeaponCatalog catalog, WeaponCategory[] order, Vector3 towardViewer, float x, float startZ, float endZ, List<ShopDisplaySlot> slots)
        {
            Vector3 barrel = Vector3.Cross(towardViewer, Vector3.up); // viewer's right
            Quaternion standing = Quaternion.LookRotation(barrel, Vector3.up);
            // Melee (modelled lying: handle along Z, blade along X, flat side Y): handle up, blade toward the viewer's right,
            // flat side facing the viewer.
            Quaternion melee = Quaternion.LookRotation(Vector3.up, towardViewer);
            Quaternion slotRotation = Quaternion.LookRotation(towardViewer);
            var pending = new List<KeyValuePair<ShopDisplaySlot, Bounds>>();
            float run = 0f;
            foreach (WeaponDefinition weapon in Collect(catalog, order))
            {
                Quaternion modelRotation = weapon.category == WeaponCategory.Melee ? melee : standing;
                ShopDisplaySlot slot = NewSlot(parent, root, weapon, slotRotation, modelRotation, StandingViewDirection, out Bounds b);
                if (slot == null) continue;
                pending.Add(new KeyValuePair<ShopDisplaySlot, Bounds>(slot, b));
                run += b.size.z + CounterGap;
            }
            float direction = Mathf.Sign(barrel.z); // lay out toward the viewer's right
            float z = (startZ + endZ) * 0.5f - direction * (run - CounterGap) * 0.5f;
            foreach (var entry in pending)
            {
                Bounds b = entry.Value;
                entry.Key.transform.localPosition = new Vector3(x, CounterTopY + b.extents.y + 0.004f, z + direction * b.extents.z);
                z += direction * (b.size.z + CounterGap);
                slots.Add(entry.Key);
            }
            return pending.Count;
        }

        private static List<WeaponDefinition> Collect(WeaponCatalog catalog, WeaponCategory[] order)
        {
            var result = new List<WeaponDefinition>();
            var buffer = new List<WeaponDefinition>();
            foreach (WeaponCategory category in order)
            {
                catalog.GetCategory(category, buffer);
                foreach (WeaponDefinition weapon in buffer)
                    if (!weapon.hiddenInShop && weapon.displayPrefab != null) result.Add(weapon);
            }
            return result;
        }

        /// <summary>
        /// New slot (rotation <paramref name="slotRotation"/> in root space, forward = toward the viewer) holding the display
        /// prefab at <paramref name="modelRotation"/> (root space), its bounds centred on the slot. <paramref name="bounds"/>
        /// is the model's size in root space, centred at the origin.
        /// </summary>
        private static ShopDisplaySlot NewSlot(Transform parent, Transform root, WeaponDefinition weapon, Quaternion slotRotation, Quaternion modelRotation, Vector3 viewDirection, out Bounds bounds)
        {
            bounds = new Bounds();
            var slotGo = new GameObject("Slot - " + weapon.id);
            slotGo.transform.SetParent(parent, false);
            slotGo.transform.rotation = root.rotation * slotRotation;

            var model = (GameObject)PrefabUtility.InstantiatePrefab(weapon.displayPrefab, slotGo.transform);
            model.transform.rotation = root.rotation * modelRotation;
            model.transform.localPosition = Vector3.zero;
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Object.DestroyImmediate(slotGo);
                return null;
            }
            Bounds world = renderers[0].bounds;
            foreach (Renderer r in renderers)
            {
                world.Encapsulate(r.bounds);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            model.transform.position += slotGo.transform.position - world.center;
            bounds = new Bounds(Vector3.zero, root.InverseTransformVector(world.size));
            bounds.extents = new Vector3(Mathf.Abs(bounds.extents.x), Mathf.Abs(bounds.extents.y), Mathf.Abs(bounds.extents.z));

            var slot = slotGo.AddComponent<ShopDisplaySlot>();
            var so = new SerializedObject(slot);
            so.FindProperty("weaponId").stringValue = weapon.id;
            so.FindProperty("model").objectReferenceValue = model.transform;
            so.FindProperty("viewDirection").vector3Value = viewDirection;
            so.ApplyModifiedPropertiesWithoutUndo();
            return slot;
        }

        // ---------------------------------------------------------------- reflections

        private const string ShopReflectionPath = "Assets/_Project/Art/Interiors/ShopRoomReflection.exr";
        private static readonly Vector3 ShopProbeCapture = new Vector3(5.6f, 1.6f, 8.4f);
        private static readonly Bounds ShopProbeBox = new Bounds(new Vector3(6.0f, 1.7f, 8.4f), new Vector3(9.4f, 3.4f, 8.6f));

        /// <summary>
        /// The display weapons are dynamic, mostly metallic objects: without a reflection probe in the shop room (the package's
        /// interior-wide probe is disabled) they reflect black and read as silhouettes. One box-projected probe is baked on its
        /// own (Custom mode, cubemap asset) so the room's lightmaps are not touched. Re-run after rebaking the interior.
        /// </summary>
        private static string BakeShopReflectionProbe(Transform root)
        {
            Transform old = root.Find("Shop Reflection Probe");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("Shop Reflection Probe");
            go.transform.SetParent(root, false);
            go.transform.localPosition = ShopProbeCapture;
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
            probe.renderDynamicObjects = false;
            probe.resolution = 128;
            probe.hdr = true;
            probe.boxProjection = true;
            probe.size = ShopProbeBox.size;
            probe.center = ShopProbeBox.center - ShopProbeCapture;
            probe.importance = 1;
            if (!Lightmapping.BakeReflectionProbe(probe, ShopReflectionPath)) return "  shop reflection probe: bake FAILED\n";
            AssetDatabase.ImportAsset(ShopReflectionPath);
            probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(ShopReflectionPath);
            return "  shop reflection probe baked -> " + ShopReflectionPath + "\n";
        }

        // ---------------------------------------------------------------- doors

        private static string BuildDoors(Transform gameplay, Transform shop)
        {
            var doors = new List<GangfireHingedDoor>();
            foreach (GangfireHingedDoor door in shop.GetComponentsInChildren<GangfireHingedDoor>(true))
            {
                if (!door.name.StartsWith("Range Door")) continue;
                // Static batching froze the leaves' meshes in place (only their colliders turned), so the doors never visibly
                // opened. Moving parts must not be batching / occlusion static; they keep their lightmap (ContributeGI).
                foreach (Transform t in door.GetComponentsInChildren<Transform>(true))
                {
                    StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
                    flags &= ~(StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
                }
                door.speedDegrees = DoorSpeed;
                door.Close();
                EditorUtility.SetDirty(door);
                doors.Add(door);
            }

            var go = new GameObject("Range Doors Trigger");
            go.transform.SetParent(gameplay, false);
            go.transform.localPosition = DoorTriggerCenter;
            go.layer = LayerMask.NameToLayer("Ignore Raycast");
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = DoorTriggerSize;
            var trigger = go.AddComponent<ProximityDoorTrigger>();
            var so = new SerializedObject(trigger);
            SerializedProperty doorsProp = so.FindProperty("doors");
            doorsProp.arraySize = doors.Count;
            for (int i = 0; i < doors.Count; i++) doorsProp.GetArrayElementAtIndex(i).objectReferenceValue = doors[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return $"  range doors: {doors.Count} leaves open automatically (trigger {DoorTriggerSize.z} m deep)\n";
        }

        // ---------------------------------------------------------------- range cage gates and downrange blocker

        private const string RangeLevelPath = "03 Original Corridor and Range/Level_Interior/";
        /// <summary>Gate in the cage's east side: the only way into the shooting lane (the player used to walk through it).</summary>
        private const string EntryGateName = "Prop_Gate (5)";
        /// <summary>Gate in the cage's west side, leading downrange to the targets: kept shut.</summary>
        private const string DownrangeGateName = "Prop_Gate";
        private static readonly Vector3 GateTriggerCenter = new Vector3(2.66f, 1.25f, -1.95f);
        private static readonly Vector3 GateTriggerSize = new Vector3(4.2f, 2.5f, 2.3f);
        /// <summary>Invisible wall just downrange of the cage fence and the benches, across the whole range room.</summary>
        private static readonly Vector3 BlockerCenter = new Vector3(-1.66f, 1.8f, -7.72f);
        private static readonly Vector3 BlockerSize = new Vector3(0.1f, 3.6f, 14.4f);
        /// <summary>Above the benches (from just over the bench top up to the ceiling): stops jumping onto the benches.</summary>
        private static readonly Vector3 BenchTopBlockerCenter = new Vector3(-1.38f, 2.425f, -9.57f);
        private static readonly Vector3 BenchTopBlockerSize = new Vector3(0.56f, 2.35f, 10.64f);
        /// <summary>
        /// Invector's movement-stop layer: the player capsule collides with it, while bullets, aim rays and the camera
        /// (Invector damage / block-aim / camera culling layers) do not include it.
        /// </summary>
        private const string BlockerLayer = "StopMove";

        private static string BuildRangeGates(Transform gameplay, Transform shop)
        {
            int blockerLayer = LayerMask.NameToLayer(BlockerLayer);
            if (blockerLayer < 0) return "  range gates: layer '" + BlockerLayer + "' missing\n";
            Transform entry = shop.Find(RangeLevelPath + EntryGateName);
            Transform downrange = shop.Find(RangeLevelPath + DownrangeGateName);
            if (entry == null || downrange == null) return "  range gates: GANGFIRE cage gates not found\n";

            // Entry gate: hinged, swings out of the cage (+X) when the player comes near, solid while closed.
            MakeMovable(entry.gameObject);
            var door = entry.GetComponent<GangfireHingedDoor>();
            Quaternion closed = entry.localRotation;
            if (door == null) door = entry.gameObject.AddComponent<GangfireHingedDoor>();
            else closed = new SerializedObject(door).FindProperty("closedRotation").quaternionValue;
            door.Configure(closed, closed * Quaternion.Euler(0f, -90f, 0f), false);
            door.speedDegrees = DoorSpeed;
            AddGateCollider(entry, blockerLayer);
            EditorUtility.SetDirty(door);

            var go = new GameObject("Range Gate Trigger");
            go.transform.SetParent(gameplay, false);
            go.transform.localPosition = GateTriggerCenter;
            go.layer = LayerMask.NameToLayer("Ignore Raycast");
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = GateTriggerSize;
            var trigger = go.AddComponent<ProximityDoorTrigger>();
            var so = new SerializedObject(trigger);
            SerializedProperty doorsProp = so.FindProperty("doors");
            doorsProp.arraySize = 1;
            doorsProp.GetArrayElementAtIndex(0).objectReferenceValue = door;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Downrange gate: was left ajar; shut (leaf along the fence) and solid. It never moves, so it stays static.
            downrange.rotation = Quaternion.Euler(0f, 270f, 0f);
            AddGateCollider(downrange, blockerLayer);

            // Nobody walks downrange: an invisible wall behind the cage fence and the benches.
            var blocker = new GameObject("Downrange Blocker");
            blocker.transform.SetParent(gameplay, false);
            blocker.transform.localPosition = BlockerCenter;
            blocker.layer = blockerLayer;
            blocker.AddComponent<BoxCollider>().size = BlockerSize;
            var benchTop = new GameObject("Bench Top");
            benchTop.transform.SetParent(blocker.transform, false);
            benchTop.transform.position = blocker.transform.parent.TransformPoint(BenchTopBlockerCenter);
            benchTop.layer = blockerLayer;
            benchTop.AddComponent<BoxCollider>().size = BenchTopBlockerSize;
            return "  range cage: entry gate opens on approach, downrange gate shut, downrange blocker on '" + BlockerLayer + "'\n";
        }

        // Moving parts must not be batching / occlusion static (they keep their lightmap).
        private static void MakeMovable(GameObject target)
        {
            foreach (Transform t in target.GetComponentsInChildren<Transform>(true))
            {
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
                flags &= ~(StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            }
        }

        // Box around the gate leaf (its mesh, in the gate's local space) on the blocker layer: stops the player, not bullets
        // or the camera.
        private static void AddGateCollider(Transform gate, int layer)
        {
            Mesh mesh = gate.GetComponent<MeshFilter>().sharedMesh;
            var box = gate.GetComponent<BoxCollider>();
            if (box == null) box = gate.gameObject.AddComponent<BoxCollider>();
            Bounds b = mesh.bounds;
            box.center = b.center;
            box.size = new Vector3(b.size.x, b.size.y, Mathf.Max(b.size.z, 0.08f));
            gate.gameObject.layer = layer;
        }

        private static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
