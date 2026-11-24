using System.Collections.Generic;
using System.Text;
using OpenworldCrime.Vehicles;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// The city taxi: the detailed "Car 4" saloon (four doors, full interior) built like the other final cars (Drivable_Taxi +
    /// Traffic_Taxi, damage, wreck, seated pedestrian driver, lamps) on its own, so the other cars are not rebuilt, then dressed
    /// as a cab: always taxi yellow (no paint variants), a lit TAXI sign on the roof and TAXI lettering on the rear doors.
    /// The phone-call taxi (TaxiService) gets a visual-only copy of it, with the driver at the wheel: Taxi_Service.prefab.
    /// Menu: Tools > OpenworldCrime > Vehicles > Build Taxi.
    /// </summary>
    public static partial class FinalCarBuilder
    {
        private const string TaxiBaseSource = "Car 4";          // the detailed saloon the taxi is made from
        private const string TaxiBaseName = "Car4";
        private const string TaxiName = "Taxi";
        private const int TaxiTrafficFrequency = 3;
        private const string ServiceTaxiPath = "Assets/_Project/Prefabs/Vehicles/Taxi_Service.prefab";
        private const string TaxiArtFolder = "Assets/_Project/Materials/Vehicles/Taxi";
        private static readonly Color TaxiYellow = new Color(0.98f, 0.76f, 0.05f);

        [MenuItem("Tools/OpenworldCrime/Vehicles/Build Taxi")]
        public static void BuildTaxiMenu() => Debug.Log(BuildTaxi());

        public static string BuildTaxi()
        {
            var sb = new StringBuilder("[FinalCarBuilder] Taxi:\n");
            EnsureFolder(DrivableFolder);
            EnsureFolder(TrafficFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(TaxiArtFolder);
            materialCache.Clear();

            // Source: a copy of the saloon's exported prefab, next to the other sources.
            string basePath = $"{SourceFolder}/{TaxiBaseSource}.prefab";
            string sourcePath = $"{SourceFolder}/{TaxiName}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(basePath) == null) return sb.Append("  missing " + basePath).ToString();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath) != null) AssetDatabase.DeleteAsset(sourcePath);
            if (!AssetDatabase.CopyAsset(basePath, sourcePath)) return sb.Append("  could not copy " + basePath).ToString();

            // The saloon's own numbers (Car4), a little gentler: it is a cab.
            //                    source    name      class            mass   km/h  torque brake  drive                                   freq                  scale
            var def = new CarDef(TaxiName, TaxiName, CarClass.Sedan, 2000f, 210f, 440f, 2800f, RCC_CarControllerV3.WheelType.RWD, TaxiTrafficFrequency, 0.96f);
            string dPath = $"{DrivableFolder}/Drivable_{def.Name}.prefab";
            string tPath = $"{TrafficFolder}/Traffic_{def.Name}.prefab";
            try
            {
                sb.AppendLine("  " + BuildDrivable(def, dPath));
                var drivable = AssetDatabase.LoadAssetAtPath<RCCVehicleAdapter>(dPath);
                sb.AppendLine("  " + BuildTraffic(def, tPath, drivable));
                AssetDatabase.SaveAssets();

                VehicleDamageSettings settings = EnsureSettings();
                GameObject wreck = BuildWreck(def);
                RigDrivable(dPath, settings, wreck);
                RigTraffic(tPath, dPath, settings, wreck);
                AssetDatabase.SaveAssets();
                sb.AppendLine("  damage and drivers ok");
            }
            catch (System.Exception e)
            {
                return sb.AppendLine($"  FAILED {e.Message}\n{e.StackTrace}").ToString();
            }
            sb.AppendLine("  " + PedDriverBuilder.RepatchPrefabs(TaxiName));
            sb.AppendLine("  " + VehicleLampBuilder.BuildAll(TaxiName));
            sb.AppendLine("  " + DressTaxi(dPath));
            sb.AppendLine("  " + DressTaxi(tPath));
            sb.AppendLine("  " + BuildServiceTaxi(tPath));
            sb.AppendLine("  " + AddTaxiToOpenScene(tPath, dPath));
            return sb.ToString();
        }

        // ---------- Livery ----------

        // Taxi yellow on every body-paint slot (the saloon's own colour material), a roof sign and door lettering.
        private static string DressTaxi(string prefabPath)
        {
            Material yellow = TaxiPaint(out Material ownBody, out string paintNote);
            if (yellow == null) return "no taxi paint: " + paintNote;
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var slots = new List<(Renderer renderer, int index)>();
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null || (mats[i] != ownBody && mats[i] != yellow && mats[i] != yellowVariant)) continue;
                        mats[i] = yellow;
                        slots.Add((r, i));
                        changed = true;
                    }
                    if (changed) r.sharedMaterials = mats;
                }
                // The paint component keeps the car yellow (one colour only) instead of rolling a random one.
                var paint = root.GetComponent<VehiclePaintVariants>();
                if (paint != null)
                {
                    var so = new SerializedObject(paint);
                    var slotProp = so.FindProperty("slots");
                    slotProp.arraySize = slots.Count;
                    for (int i = 0; i < slots.Count; i++)
                    {
                        var e = slotProp.GetArrayElementAtIndex(i);
                        e.FindPropertyRelative("renderer").objectReferenceValue = slots[i].renderer;
                        e.FindPropertyRelative("materialIndex").intValue = slots[i].index;
                    }
                    var variants = so.FindProperty("variants");
                    variants.arraySize = 1;
                    variants.GetArrayElementAtIndex(0).objectReferenceValue = yellow;
                    var names = so.FindProperty("colourNames");
                    names.arraySize = 1;
                    names.GetArrayElementAtIndex(0).stringValue = "Taxi Yellow";
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                Bounds body = BodyBounds(root.transform);
                AddRoofSign(root.transform, body);
                AddDoorLettering(root.transform, body);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return $"{System.IO.Path.GetFileNameWithoutExtension(prefabPath)}: {slots.Count} paint slots yellow ({paintNote}), roof sign, door lettering";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // The saloon's yellow paint variant (the same Car Paint material the yellow traffic Car4 uses), so it shades exactly like
        // the other cars; a plain URP copy of its own body in taxi yellow when no variant is found.
        private static Material TaxiPaint(out Material ownBody, out string note)
        {
            ownBody = null;
            note = "";
            var reference = AssetDatabase.LoadAssetAtPath<GameObject>($"{DrivableFolder}/Drivable_{TaxiBaseName}.prefab");
            var paint = reference != null ? reference.GetComponent<VehiclePaintVariants>() : null;
            Material found = null;
            if (paint != null)
            {
                var so = new SerializedObject(paint);
                var variants = so.FindProperty("variants");
                var names = so.FindProperty("colourNames");
                if (variants.arraySize > 0) ownBody = variants.GetArrayElementAtIndex(0).objectReferenceValue as Material;
                for (int i = 0; i < variants.arraySize && i < names.arraySize; i++)
                    if (names.GetArrayElementAtIndex(i).stringValue == "Yellow") found = variants.GetArrayElementAtIndex(i).objectReferenceValue as Material;
            }
            // The taxi's own body material is a fresh URP conversion of the same source material: match by name.
            if (ownBody != null)
            {
                string bodyName = ownBody.name;
                var taxi = AssetDatabase.LoadAssetAtPath<GameObject>($"{DrivableFolder}/Drivable_{TaxiName}.prefab");
                if (taxi != null)
                    foreach (var r in taxi.GetComponentsInChildren<Renderer>(true))
                        foreach (var m in r.sharedMaterials)
                            if (m != null && m.name == bodyName) { ownBody = m; break; }
            }
            string path = TaxiArtFolder + "/TaxiPaint.mat";
            if (found != null)
            {
                // The saloon's own yellow, made a deeper cab yellow with less sky reflection: flat panels (boot, bonnet) facing a
                // bright sky otherwise wash out to beige.
                var taxiPaint = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (taxiPaint == null) { taxiPaint = new Material(found); AssetDatabase.CreateAsset(taxiPaint, path); }
                else { taxiPaint.shader = found.shader; taxiPaint.CopyPropertiesFromMaterial(found); }
                taxiPaint.name = "TaxiPaint";
                SetIf(taxiPaint, "_PaintColor", TaxiYellow);
                SetIf(taxiPaint, "_Saturation", 1.4f);
                SetIf(taxiPaint, "_ReflectionStrength", 0.5f);
                SetIf(taxiPaint, "_HorizonStrength", 0.55f);
                SetIf(taxiPaint, "_ReflectionTint", 0.7f);
                SetIf(taxiPaint, "_BaseSheen", 0.2f);
                EditorUtility.SetDirty(taxiPaint);
                yellowVariant = found;
                note = "cab yellow from the Car4 paint";
                return taxiPaint;
            }
            if (ownBody == null) { note = "Car4 has no paint variants"; return null; }
            var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (copy == null) { copy = new Material(ownBody); AssetDatabase.CreateAsset(copy, path); }
            if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", TaxiYellow);
            EditorUtility.SetDirty(copy);
            note = "plain yellow copy";
            return copy;
        }

        private static Material yellowVariant;   // the Car4 yellow paint the taxi paint was made from (replaced on rebuilds)

        private static void SetIf(Material m, string property, float value) { if (m.HasProperty(property)) m.SetFloat(property, value); }
        private static void SetIf(Material m, string property, Color value) { if (m.HasProperty(property)) m.SetColor(property, value); }

        // The car body without its wheels, in root space.
        private static Bounds BodyBounds(Transform root)
        {
            bool first = true;
            var bounds = new Bounds();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                string n = r.name.ToLowerInvariant();
                if (n.Contains("wheel") || n.Contains("tire") || n.Contains("flare") || n.Contains("shadow") || n.Contains("light") && !n.Contains("body")) continue;
                if (r.GetComponentInParent<Animator>() != null) continue;   // the seated driver
                var b = RootBounds(r, root);
                if (first) { bounds = b; first = false; }
                else bounds.Encapsulate(b);
            }
            return bounds;
        }

        // A lit TAXI box on the middle of the roof, black lettering on both long sides.
        private static void AddRoofSign(Transform root, Bounds body)
        {
            var old = root.Find("Taxi Roof Sign");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            float roofY = RoofHeight(root, body);
            var sign = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sign.name = "Taxi Roof Sign";
            Object.DestroyImmediate(sign.GetComponent<Collider>());
            sign.transform.SetParent(root, false);
            sign.transform.localScale = new Vector3(0.62f, 0.2f, 0.26f);
            sign.transform.localPosition = new Vector3(body.center.x, roofY + 0.1f, body.center.z - body.size.z * 0.04f);
            var r = sign.GetComponent<MeshRenderer>();
            r.sharedMaterial = SignMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (int side in new[] { 1, -1 })
            {
                var label = Lettering("TAXI", sign.transform, 0.17f, Color.black);
                label.transform.localPosition = new Vector3(0f, 0f, 0.505f * side);
                label.transform.localRotation = Quaternion.Euler(0f, side > 0 ? 180f : 0f, 0f);
                // The cube is scaled: undo it so the text keeps its proportions.
                label.transform.localScale = new Vector3(1f / 0.62f, 1f / 0.2f, 1f / 0.26f);
            }
        }

        // The roof: the highest body point near the middle of the car (not the boot or bonnet).
        private static float RoofHeight(Transform root, Bounds body)
        {
            float best = body.min.y + body.size.y * 0.9f;
            float found = float.NegativeInfinity;
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null || f.GetComponentInParent<Animator>() != null) continue;
                string n = f.name.ToLowerInvariant();
                if (n.Contains("wheel") || n.Contains("glass") || n.Contains("flare")) continue;
                if (!f.sharedMesh.isReadable) continue;
                var toRoot = root.worldToLocalMatrix * f.transform.localToWorldMatrix;
                foreach (var v in f.sharedMesh.vertices)
                {
                    Vector3 p = toRoot.MultiplyPoint3x4(v);
                    if (Mathf.Abs(p.x - body.center.x) < 0.3f && Mathf.Abs(p.z - body.center.z) < body.size.z * 0.12f) found = Mathf.Max(found, p.y);
                }
            }
            return found > body.min.y ? found : best;
        }

        // "TAXI" low on both rear doors (they don't swing open), facing out.
        private static void AddDoorLettering(Transform root, Bounds body)
        {
            foreach (string n in new[] { "Taxi Door Lettering L", "Taxi Door Lettering R" })
            {
                var old = root.Find(n);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }
            foreach (int side in new[] { -1, 1 })
            {
                var label = Lettering("TAXI", root, 0.32f, new Color(0.05f, 0.05f, 0.05f));
                label.name = side < 0 ? "Taxi Door Lettering L" : "Taxi Door Lettering R";
                float x = body.center.x + side * (body.extents.x - 0.06f);
                label.transform.localPosition = new Vector3(x, body.min.y + body.size.y * 0.42f, body.center.z - body.size.z * 0.12f);
                label.transform.localRotation = Quaternion.Euler(0f, side < 0 ? 90f : -90f, 0f);
                // Pushed out until it sits just outside the door skin.
                PushOntoSurface(label.transform, root, side);
            }
        }

        // Moves the lettering outward along x until it is clear of the body's mesh at that height (vertex scan; a few mm out).
        private static void PushOntoSurface(Transform label, Transform root, int side)
        {
            Vector3 p = label.localPosition;
            float outer = side < 0 ? float.PositiveInfinity : float.NegativeInfinity;
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null || !f.sharedMesh.isReadable || f.GetComponentInParent<Animator>() != null) continue;
                string n = f.name.ToLowerInvariant();
                if (n.Contains("wheel") || n.Contains("mirror") || n.Contains("flare")) continue;
                var toRoot = root.worldToLocalMatrix * f.transform.localToWorldMatrix;
                foreach (var v in f.sharedMesh.vertices)
                {
                    Vector3 q = toRoot.MultiplyPoint3x4(v);
                    if (Mathf.Abs(q.y - p.y) > 0.12f || Mathf.Abs(q.z - p.z) > 0.35f) continue;
                    outer = side < 0 ? Mathf.Min(outer, q.x) : Mathf.Max(outer, q.x);
                }
            }
            if (!float.IsInfinity(outer)) p.x = outer + side * 0.012f;
            label.localPosition = p;
        }

        private static TextMeshPro Lettering(string text, Transform parent, float height, Color colour)
        {
            var go = new GameObject("TAXI Lettering", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = colour;
            tmp.enableAutoSizing = false;
            tmp.fontSize = height * 10f;
            tmp.rectTransform.sizeDelta = new Vector2(height * 4f, height * 1.4f);
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return tmp;
        }

        private static Material SignMaterial()
        {
            string path = TaxiArtFolder + "/TaxiRoofSign.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", new Color(1f, 0.86f, 0.25f));
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.2f) * 1.6f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------- The phone-call taxi ----------

        // TaxiService drives its taxi along the road itself (no physics, no AI): the traffic taxi stripped to what is seen, with
        // its seated driver kept and shown.
        private static string BuildServiceTaxi(string trafficPath)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(trafficPath);
            try
            {
                var adapter = root.GetComponent<TrafficVehicleAdapter>();
                GameObject driver = adapter != null ? adapter.EditorDriverVisual : null;
                if (driver != null)
                {
                    for (Transform t = driver.transform; t != null && t != root.transform; t = t.parent) t.gameObject.SetActive(true);
                    foreach (var r in driver.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
                }
                StripToVisuals(root);
                root.name = "Taxi_Service";
                PrefabUtility.SaveAsPrefabAsset(root, ServiceTaxiPath);
                return "service taxi " + ServiceTaxiPath + (driver != null ? " (driver seated)" : " (no driver visual found)");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // Keeps transforms, meshes, renderers, the driver's animator, LOD groups and the lettering; everything else goes, in an
        // order Unity accepts (a component only once nothing left on its object still needs it).
        private static void StripToVisuals(GameObject root)
        {
            var left = new List<Component>();
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform || c is Renderer || c is MeshFilter || c is Animator || c is LODGroup) continue;
                if (c is TMP_Text) continue;
                left.Add(c);
            }
            for (int pass = 0; pass < 12 && left.Count > 0; pass++)
            {
                for (int i = left.Count - 1; i >= 0; i--)
                {
                    Component c = left[i];
                    if (c == null) { left.RemoveAt(i); continue; }
                    if (NeededByOthers(c, left)) continue;
                    Object.DestroyImmediate(c, true);
                    left.RemoveAt(i);
                }
            }
        }

        private static bool NeededByOthers(Component target, List<Component> left)
        {
            var type = target.GetType();
            foreach (var other in left)
            {
                if (other == null || other == target || other.gameObject != target.gameObject) continue;
                if (target is Rigidbody && other is Joint) return true;
                foreach (RequireComponent require in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                {
                    if (require.m_Type0 != null && require.m_Type0.IsAssignableFrom(type)) return true;
                    if (require.m_Type1 != null && require.m_Type1.IsAssignableFrom(type)) return true;
                    if (require.m_Type2 != null && require.m_Type2.IsAssignableFrom(type)) return true;
                }
            }
            return false;
        }

        // ---------- Scene ----------

        // Common in traffic (weight 3 of the mix), pre-warmed for the takeover like the other drivables, and the phone-call taxi.
        private static string AddTaxiToOpenScene(string trafficPath, string drivablePath)
        {
            var traffic = AssetDatabase.LoadAssetAtPath<GameObject>(trafficPath);
            var drivable = AssetDatabase.LoadAssetAtPath<RCCVehicleAdapter>(drivablePath);
            var spawner = Object.FindFirstObjectByType<TSTrafficSpawner>(FindObjectsInactive.Include);
            if (spawner == null || traffic == null) return "no traffic spawner in the open scene";
            var list = new List<TSTrafficSpawner.TSSpawnVehicles>();
            foreach (var entry in spawner.cars) if (entry.cars != null && entry.cars != traffic) list.Add(entry);
            list.Add(new TSTrafficSpawner.TSSpawnVehicles { cars = traffic, frequency = TaxiTrafficFrequency });
            Undo.RecordObject(spawner, "Add taxi to traffic");
            spawner.cars = list.ToArray();
            EditorUtility.SetDirty(spawner);

            var takeover = Object.FindFirstObjectByType<TrafficTakeoverManager>(FindObjectsInactive.Include);
            if (takeover != null && drivable != null)
            {
                var so = new SerializedObject(takeover);
                var prop = so.FindProperty("prewarmedPrefabs");
                bool present = false;
                for (int i = 0; i < prop.arraySize; i++) present |= prop.GetArrayElementAtIndex(i).objectReferenceValue == drivable;
                if (!present)
                {
                    prop.arraySize++;
                    prop.GetArrayElementAtIndex(prop.arraySize - 1).objectReferenceValue = drivable;
                    so.ApplyModifiedProperties();
                }
            }

            string service = "";
            var serviceTaxi = AssetDatabase.LoadAssetAtPath<GameObject>(ServiceTaxiPath);
            var taxiService = Object.FindFirstObjectByType<TaxiService>(FindObjectsInactive.Include);
            if (taxiService != null && serviceTaxi != null)
            {
                Undo.RecordObject(taxiService, "Service taxi");
                taxiService.taxiPrefab = serviceTaxi;
                EditorUtility.SetDirty(taxiService);
                service = ", phone-call taxi set";
            }
            EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);
            return $"traffic: {list.Count} entries (taxi weight {TaxiTrafficFrequency}){service}";
        }
    }
}
