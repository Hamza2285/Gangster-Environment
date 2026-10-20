using System.Collections.Generic;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Adds and wires <see cref="VehicleLights"/> on the traffic and drivable car prefabs from their existing light materials
    /// (no new geometry). Light slots are found by material name:
    /// - "...brake...light..." -> Brake; "head light" / "xenon" / "angels" -> Headlight;
    /// - other "...light..." materials (front and rear lamps in one atlas material) -> Combined, unless the car has dedicated
    ///   head-light materials (then the generic one is a housing and is left alone);
    /// - a mesh named "...reverse..." -> Reverse; meshes named "...indicator..." and shadow-only meshes are skipped;
    /// - brake discs, light plastic / glass / bulbs, indicators (one material for both sides, so no left/right) and iTS'
    ///   NewCar_BrakeGlow objects (still switched by iTS) are skipped.
    /// Traffic: brake from the iTS AI; where a dedicated brake-lamp material exists, iTS' own on/off switching of that renderer
    /// is turned off (TSSimpleCar.enableDisableBrakeLights) so the lamp stays visible and only its emission changes.
    /// Drivable: brake/reverse/headlights from RCC; RCC's head-light Light components are kept for the player's car only, every
    /// other Light component is listed to be switched off. Re-running rebuilds the slot lists.
    /// </summary>
    public static class VehicleLightsSetup
    {
        private static readonly string[] Folders =
        {
            "Assets/_Project/Prefabs/Vehicles/Traffic",
            "Assets/_Project/Prefabs/Vehicles/Drivable"
        };

        [MenuItem("Tools/OpenworldCrime/Vehicles/Setup Vehicle Lights")]
        public static void SetupAll() => Debug.Log(SetupAllReport());

        public static string SetupAllReport()
        {
            var report = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", Folders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    report.Add(Setup(root));
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            return "[VehicleLightsSetup]\n" + string.Join("\n", report);
        }

        private static string Setup(GameObject root)
        {
            var lights = root.GetComponent<VehicleLights>();
            if (lights == null) lights = root.AddComponent<VehicleLights>();

            var trafficAI = root.GetComponent<TSTrafficAI>();
            var simpleCar = root.GetComponent<TSSimpleCar>();
            var rcc = root.GetComponent<RCC_CarControllerV3>();
            var adapter = root.GetComponent<RCCVehicleAdapter>();

            // Renderers iTS switches on and off as brake lights stay iTS' (glow planes) unless they carry a real brake-lamp material.
            var itsBrakeRenderers = new HashSet<Renderer>();
            if (simpleCar != null && simpleCar.brakeLigths != null)
                foreach (Renderer r in simpleCar.brakeLigths) if (r != null) itsBrakeRenderers.Add(r);

            var slots = new List<VehicleLights.LightSlot>();
            bool hasHeadlightMaterial = false;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                if (!(r is MeshRenderer)) continue;
                foreach (Material m in r.sharedMaterials)
                    if (m != null && Classify(m) == Kind.Headlight) hasHeadlightMaterial = true;
            }

            bool itsBrakeTakenOver = false;
            foreach (Renderer r in renderers)
            {
                if (!(r is MeshRenderer) || r.GetComponentInParent<RCC_Light>() != null) continue;
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    Kind kind = Classify(mats[i]);
                    if (kind == Kind.None) continue;
                    if (kind == Kind.Combined && hasHeadlightMaterial) continue;
                    // RCC models reuse the lamp materials on separate indicator / reverse meshes: the mesh name decides there.
                    string rendererName = r.name.ToLowerInvariant();
                    if (rendererName.Contains("indicator") || r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;
                    VehicleLightRole role = rendererName.Contains("reverse") ? VehicleLightRole.Reverse
                        : kind == Kind.Brake ? VehicleLightRole.Brake
                        : kind == Kind.Tail ? VehicleLightRole.TailAndBrake
                        : kind == Kind.Headlight ? VehicleLightRole.Headlight : VehicleLightRole.Combined;
                    if (role == VehicleLightRole.Brake && itsBrakeRenderers.Contains(r)) itsBrakeTakenOver = true;
                    slots.Add(new VehicleLights.LightSlot { renderer = r, materialIndex = i, role = role });
                }
            }

            if (simpleCar != null && itsBrakeTakenOver && simpleCar.enableDisableBrakeLights)
            {
                Undo.RecordObject(simpleCar, "Vehicle lights");
                simpleCar.enableDisableBrakeLights = false;
                foreach (Renderer r in itsBrakeRenderers) r.enabled = true;
            }

            var headLights = new List<Light>();
            var otherLights = new List<Light>();
            foreach (Light l in root.GetComponentsInChildren<Light>(true))
            {
                var rccLight = l.GetComponent<RCC_Light>();
                if (rccLight != null && rccLight.lightType == RCC_Light.LightType.HeadLight && l.type == LightType.Spot) headLights.Add(l);
                else otherLights.Add(l);
            }

            var so = new SerializedObject(lights);
            SerializedProperty slotsProp = so.FindProperty("slots");
            slotsProp.arraySize = slots.Count;
            for (int i = 0; i < slots.Count; i++)
            {
                SerializedProperty e = slotsProp.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("renderer").objectReferenceValue = slots[i].renderer;
                e.FindPropertyRelative("materialIndex").intValue = slots[i].materialIndex;
                e.FindPropertyRelative("role").enumValueIndex = (int)slots[i].role;
            }
            // Traffic cars show braking through iTS' glow objects; drivable cars have none, so their combined lamps glow dimly.
            so.FindProperty("combinedShowsBrake").boolValue = rcc != null;
            so.FindProperty("trafficAI").objectReferenceValue = trafficAI;
            so.FindProperty("rccController").objectReferenceValue = rcc;
            so.FindProperty("drivableAdapter").objectReferenceValue = adapter;
            SetArray(so.FindProperty("realtimeHeadlights"), headLights);
            SetArray(so.FindProperty("otherRealtimeLights"), otherLights);
            so.ApplyModifiedPropertiesWithoutUndo();

            return $"{root.name}: {slots.Count} slots ({Describe(slots)}), realtime headlights {headLights.Count}, other lights off {otherLights.Count}" +
                   (itsBrakeTakenOver ? ", iTS brake renderer switching off" : "");
        }

        private enum Kind { None, Brake, Headlight, Combined, Tail }

        private static Kind Classify(Material m)
        {
            string n = m.name.ToLowerInvariant();
            // Final car pack: rear lamp atlases (tail + brake) and trim atlases whose emission mask covers the headlights.
            if (n.Contains("tailight") || n.Contains("taillight")) return Kind.Tail;
            if ((n.Contains("matte") || n.Contains("mate")) && m.HasProperty("_EmissionMap") && m.GetTexture("_EmissionMap") != null) return Kind.Headlight;
            return Classify(n);
        }

        private static Kind Classify(string n)
        {
            if (n.Contains("brakeglow") || n.Contains("indicator")) return Kind.None;
            bool light = n.Contains("light");
            if (n.Contains("brake")) return light ? Kind.Brake : Kind.None;
            if (n.Contains("headlight") || n.Contains("head light") || n.Contains("xenon") || n.Contains("angels")) return Kind.Headlight;
            if (!light) return Kind.None;
            if (n.Contains("plastic") || n.Contains("glass") || n.Contains("bulb")) return Kind.None;
            return Kind.Combined;
        }

        private static string Describe(List<VehicleLights.LightSlot> slots)
        {
            var parts = new List<string>();
            foreach (var s in slots) parts.Add(s.renderer.name + "[" + s.materialIndex + "]=" + s.role);
            return string.Join(", ", parts);
        }

        private static void SetArray(SerializedProperty prop, List<Light> items)
        {
            prop.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }
    }
}
