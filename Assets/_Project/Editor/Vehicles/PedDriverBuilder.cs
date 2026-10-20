using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Makes the street pedestrians the drivers of the traffic cars (replacing the static dummy driver):
    /// 1. For every pedestrian type of the scene's PedestrianSpawner it builds a humanoid Avatar for the pedestrian's 3ds Max
    ///    Biped rig (bones mapped by name, arms/legs put in a T-pose first), so the real pedestrian can play the humanoid carjack
    ///    clips during a carjack.
    /// 2. It bakes the pedestrian in the seated driving pose (the seated clip retargeted through that avatar, torso reclined like
    ///    the player's seated pose) into a static mesh in driver seat space: what the traffic car shows at the wheel.
    /// 3. It writes the PedDriverRoster and points every Traffic_ / Drivable_ car prefab at it (driver visuals normalised to the
    ///    seat).
    /// Menu: Tools > OpenworldCrime > Vehicles > Build Pedestrian Drivers. Re-runnable.
    /// </summary>
    public static class PedDriverBuilder
    {
        private const string OutFolder = "Assets/_Project/Characters/PedDrivers";
        private const string RosterPath = "Assets/_Project/Data/Vehicles/PedDriverRoster.asset";
        private const string CarjackControllerPath = "Assets/_Project/Animations/EnemyDummy.controller";
        private const string SeatedClipPath = "Assets/CustomAnimations/Vehicle/Exiting Car.fbx";
        private const float SeatedLeanDegrees = 23f;
        private const float MaxLeanDegrees = 50f;
        // The fitted seated player's crown above his hips over his standing height (0.705 m / 1.76 m).
        private const float PlayerSeatedCrownRatio = 0.40f;
        // The player's hips in driver seat space in the seated pose (VehicleFactory.ClipHipsOffset at the player's 0.93 scale).
        private static readonly Vector3 PlayerSeatedHips = new Vector3(0.01f, 0.98f, -0.13f) * VehicleFactory.PlayerScale;
        private const float HeadLevelShare = 0.55f;
        // The seated player's crown above his hips (VehicleFactory's fitted rig): every roof clears it.
        // The seated player's crown relative to his hips (VehicleFactory's fitted rig), front to back.
        private const float PlayerCrownBehindHips = -0.217f * VehicleFactory.PlayerScale;
        // Kept between a seated driver's head and the roof.
        private const float DriverRoofClearance = 0.03f;

        // Humanoid bone -> Biped bone names (first match wins).
        private static readonly (string human, string[] biped)[] BoneMap =
        {
            ("Hips", new[] { "Bip01 Pelvis" }),
            ("Spine", new[] { "Bip01 Spine" }),
            ("Chest", new[] { "Bip01 Spine1" }),
            ("UpperChest", new[] { "Bip01 Spine2" }),
            ("Neck", new[] { "Bip01 Neck" }),
            ("Head", new[] { "Bip01 Head" }),
            ("LeftShoulder", new[] { "Bip01 L Clavicle" }),
            ("LeftUpperArm", new[] { "Bip01 L UpperArm" }),
            ("LeftLowerArm", new[] { "Bip01 L Forearm" }),
            ("LeftHand", new[] { "Bip01 L Hand" }),
            ("RightShoulder", new[] { "Bip01 R Clavicle" }),
            ("RightUpperArm", new[] { "Bip01 R UpperArm" }),
            ("RightLowerArm", new[] { "Bip01 R Forearm" }),
            ("RightHand", new[] { "Bip01 R Hand" }),
            ("LeftUpperLeg", new[] { "Bip01 L Thigh" }),
            ("LeftLowerLeg", new[] { "Bip01 L Calf" }),
            ("LeftFoot", new[] { "Bip01 L Foot" }),
            ("LeftToes", new[] { "Bip01 L Toe0" }),
            ("RightUpperLeg", new[] { "Bip01 R Thigh" }),
            ("RightLowerLeg", new[] { "Bip01 R Calf" }),
            ("RightFoot", new[] { "Bip01 R Foot" }),
            ("RightToes", new[] { "Bip01 R Toe0" }),
        };

        [MenuItem("Tools/OpenworldCrime/Vehicles/Build Pedestrian Drivers")]
        public static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            var spawner = Object.FindFirstObjectByType<PedestrianSpawner>(FindObjectsInactive.Include);
            if (spawner == null || spawner.peoplePrefabs == null || spawner.peoplePrefabs.Length == 0) return "[PedDriverBuilder] No PedestrianSpawner with People Prefabs in the open scene.";
            AnimationClip seated = null;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(SeatedClipPath))
                if (o is AnimationClip c && !c.name.StartsWith("__preview")) seated = c;
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CarjackControllerPath);
            if (seated == null || controller == null) return "[PedDriverBuilder] Missing the seated clip or the carjack controller.";
            EnsureFolder(OutFolder);

            var sb = new StringBuilder("[PedDriverBuilder]\n");
            var entries = new List<PedDriverRoster.Entry>();
            for (int i = 0; i < spawner.peoplePrefabs.Length; i++)
            {
                GameObject prefab = spawner.peoplePrefabs[i];
                if (prefab == null) continue;
                try
                {
                    PedDriverRoster.Entry e = BuildEntry(prefab, i, seated, out string log);
                    if (e != null) entries.Add(e);
                    sb.AppendLine($"  {prefab.name}: {log}");
                }
                catch (System.Exception ex)
                {
                    sb.AppendLine($"  {prefab.name}: FAILED {ex.Message}");
                }
            }

            var roster = AssetDatabase.LoadAssetAtPath<PedDriverRoster>(RosterPath);
            if (roster == null)
            {
                EnsureFolder(Path.GetDirectoryName(RosterPath).Replace('\\', '/'));
                roster = ScriptableObject.CreateInstance<PedDriverRoster>();
                AssetDatabase.CreateAsset(roster, RosterPath);
            }
            roster.EditorSet(entries.ToArray(), controller);
            EditorUtility.SetDirty(roster);
            AssetDatabase.SaveAssets();
            sb.AppendLine($"  roster: {entries.Count} pedestrian drivers");
            sb.Append(PatchPrefabs(roster));
            return sb.ToString();
        }

        private static PedDriverRoster.Entry BuildEntry(GameObject prefab, int index, AnimationClip seated, out string log)
        {
            var inst = (GameObject)Object.Instantiate(prefab);
            try
            {
                // At the origin: the root's own position is part of the avatar skeleton (the hips height and human scale come from it).
                inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (var mb in inst.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
                var animator = inst.GetComponent<Animator>();
                if (animator == null) { log = "no Animator"; return null; }
                var standingMesh = inst.GetComponentInChildren<SkinnedMeshRenderer>(true);
                float standing = standingMesh != null ? MeshTop(standingMesh) : 1.7f;   // root (feet) at y = 0

                // 1. Humanoid avatar for the Biped rig (T-pose first, as Unity's humanoid importer expects).
                var bones = new Dictionary<string, Transform>();
                foreach (Transform t in inst.GetComponentsInChildren<Transform>(true)) if (!bones.ContainsKey(t.name)) bones[t.name] = t;
                var human = new List<HumanBone>();
                foreach (var (humanName, biped) in BoneMap)
                {
                    foreach (string b in biped)
                    {
                        if (!bones.ContainsKey(b)) continue;
                        human.Add(new HumanBone { humanName = humanName, boneName = b, limit = new HumanLimit { useDefaultValues = true } });
                        break;
                    }
                }
                ForceTPose(inst.transform, bones);
                var skeleton = new List<SkeletonBone>();
                foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
                    skeleton.Add(new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
                var description = new HumanDescription
                {
                    human = human.ToArray(),
                    skeleton = skeleton.ToArray(),
                    upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                    armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
                };
                Avatar avatar = AvatarBuilder.BuildHumanAvatar(inst, description);
                if (avatar == null || !avatar.isValid || !avatar.isHuman) { log = "humanoid avatar invalid"; return null; }
                avatar.name = prefab.name + "_Humanoid";
                string avatarPath = $"{OutFolder}/{prefab.name}_Humanoid.asset";
                var oldAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(avatarPath);
                if (oldAvatar != null) AssetDatabase.DeleteAsset(avatarPath);
                AssetDatabase.CreateAsset(avatar, avatarPath);

                // 2. Seated pose through that avatar, reclined like the player's seated pose, baked in driver seat space.
                animator.avatar = avatar;
                animator.runtimeAnimatorController = null;
                var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (smr == null) { log = "no skinned mesh"; return null; }
                // The clip sits this rig bolt upright. It reclines (positive = back) from the default until the seated crown is
                // as high above the hips, for the pedestrian's height, as the reclined player's (otherwise tall men would not fit
                // under low roofs), but the head never goes much further back than his.
                bones.TryGetValue("Bip01 Pelvis", out Transform pelvisBone);
                bones.TryGetValue("Bip01 Head", out Transform headBone);
                float lean = SeatedLeanDegrees;
                for (float tryLean = SeatedLeanDegrees; tryLean <= MaxLeanDegrees; tryLean += 3f)
                {
                    inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    seated.SampleAnimation(inst, 0f);
                    inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    Lean(inst.transform, bones, tryLean);
                    if (pelvisBone == null || headBone == null) break;
                    if (headBone.position.z - pelvisBone.position.z < PlayerCrownBehindHips - 0.10f) break;   // too far back
                    lean = tryLean;
                    if (HeadTop(smr, headBone) - pelvisBone.position.y <= PlayerSeatedCrownRatio * standing) break;
                }
                inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                seated.SampleAnimation(inst, 0f);
                inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Lean(inst.transform, bones, lean);
                // Hips onto the player's seated hips (the seats are fitted to them); the root moves by the same offset at runtime.
                Vector3 seatOffset = bones.TryGetValue("Bip01 Pelvis", out Transform pelvis) ? PlayerSeatedHips - pelvis.position : Vector3.zero;
                seatOffset.x = 0f;

                var baked = new Mesh { name = prefab.name + "_Seated" };
                smr.BakeMesh(baked, false);
                // Unscaled bake = renderer space: through the renderer's matrix to world, then into the root's space at scale 1.
                Vector3[] v = baked.vertices; Vector3[] n = baked.normals;
                Matrix4x4 toWorld = smr.transform.localToWorldMatrix;
                Quaternion rootInverse = Quaternion.Inverse(inst.transform.rotation);
                for (int i = 0; i < v.Length; i++) v[i] = rootInverse * (toWorld.MultiplyPoint3x4(v[i]) - inst.transform.position) + seatOffset;
                for (int i = 0; i < n.Length; i++) n[i] = (rootInverse * toWorld.MultiplyVector(n[i])).normalized;
                // Pedestrians sit taller than the player (more upright, a little bigger); each car limits the crown to what its
                // roof allows and scales a too-tall driver about these hips (PedDriverRoster.SeatedScale).
                Vector3 hipsPoint = (pelvis != null ? pelvis.position : Vector3.zero) + seatOffset;
                // Their heads also lean a little further back than his; the crown is moved forward onto his (the whole body moves
                // a few cm), so sloping roofs that clear his head clear theirs.
                // Crown = top of the head region only (the hands must not count).
                Vector3 headInSeat = (headBone != null ? headBone.position : Vector3.up * 1.6f) + seatOffset;
                float top = float.NegativeInfinity, crownZ = 0f; int crownCount = 0;
                for (int i = 0; i < v.Length; i++) if ((v[i] - headInSeat).sqrMagnitude < 0.0625f) top = Mathf.Max(top, v[i].y);
                for (int i = 0; i < v.Length; i++) if (v[i].y > top - 0.04f && (v[i] - headInSeat).sqrMagnitude < 0.0625f) { crownZ += v[i].z; crownCount++; }
                crownZ /= Mathf.Max(1, crownCount);
                float forward = Mathf.Clamp(hipsPoint.z + PlayerCrownBehindHips - crownZ, 0f, 0.10f);
                if (forward > 0f)
                {
                    for (int i = 0; i < v.Length; i++) v[i].z += forward;
                    seatOffset.z += forward;
                    hipsPoint.z += forward;
                }
                baked.vertices = v;
                baked.normals = n;
                baked.RecalculateBounds();
                baked.RecalculateTangents();
                string meshPath = $"{OutFolder}/{prefab.name}_Seated.asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
                AssetDatabase.CreateAsset(baked, meshPath);

                log = $"avatar ok ({human.Count} bones), seated mesh {baked.vertexCount} verts, top {baked.bounds.max.y:F2} m, lifted {seatOffset.y:F2} m, standing {standing:F2} m, lean {lean:F0}deg, crown {top - hipsPoint.y:F2} above hips";
                return new PedDriverRoster.Entry
                {
                    pedName = prefab.name,
                    spawnerIndex = index,
                    humanoidAvatar = avatar,
                    seatedMesh = baked,
                    seatOffset = seatOffset,
                    seatedHips = hipsPoint,
                    seatedCrownY = top,
                    materials = smr.sharedMaterials,
                };
            }
            finally { Object.DestroyImmediate(inst); }
        }

        // Arms straight out to the sides, legs straight down (directions in the character's frame).
        private static void ForceTPose(Transform root, Dictionary<string, Transform> bones)
        {
            void Aim(string from, string to, Vector3 worldDirection)
            {
                if (!bones.TryGetValue(from, out Transform a) || !bones.TryGetValue(to, out Transform b)) return;
                Vector3 current = b.position - a.position;
                if (current.sqrMagnitude < 1e-8f) return;
                a.rotation = Quaternion.FromToRotation(current, worldDirection) * a.rotation;
            }
            Vector3 right = root.right, down = -root.up;
            Aim("Bip01 L UpperArm", "Bip01 L Forearm", -right);
            Aim("Bip01 L Forearm", "Bip01 L Hand", -right);
            Aim("Bip01 R UpperArm", "Bip01 R Forearm", right);
            Aim("Bip01 R Forearm", "Bip01 R Hand", right);
            Aim("Bip01 L Thigh", "Bip01 L Calf", down);
            Aim("Bip01 L Calf", "Bip01 L Foot", down);
            Aim("Bip01 R Thigh", "Bip01 R Calf", down);
            Aim("Bip01 R Calf", "Bip01 R Foot", down);
        }

        // Torso rotated back about the hips over the spine chain; the head turns part of it back to keep looking ahead.
        private static void Lean(Transform root, Dictionary<string, Transform> bones, float degrees)
        {
            string[] spine = { "Bip01 Spine", "Bip01 Spine1", "Bip01 Spine2" };
            int links = 0;
            foreach (string s in spine) if (bones.ContainsKey(s)) links++;
            if (links == 0) return;
            foreach (string s in spine)
                if (bones.TryGetValue(s, out Transform t)) t.rotation = Quaternion.AngleAxis(-degrees / links, root.right) * t.rotation;
            if (bones.TryGetValue("Bip01 Head", out Transform head)) head.rotation = Quaternion.AngleAxis(degrees * HeadLevelShare, root.right) * head.rotation;
            // The arms keep reaching forward to the wheel instead of rising with the reclined torso.
            foreach (string arm in new[] { "Bip01 L UpperArm", "Bip01 R UpperArm" })
                if (bones.TryGetValue(arm, out Transform a)) a.rotation = Quaternion.AngleAxis(degrees, root.right) * a.rotation;
        }

        // Top of the head (world y): the highest skinned vertex within 25 cm of the head bone (hands and arms are left out).
        private static float HeadTop(SkinnedMeshRenderer smr, Transform head)
        {
            var m = new Mesh();
            smr.BakeMesh(m, false);
            Matrix4x4 toWorld = smr.transform.localToWorldMatrix;
            float top = head.position.y;
            foreach (Vector3 v in m.vertices)
            {
                Vector3 p = toWorld.MultiplyPoint3x4(v);
                if ((p - head.position).sqrMagnitude < 0.0625f && p.y > top) top = p.y;
            }
            Object.DestroyImmediate(m);
            return top;
        }

        // Highest point of the skinned mesh (world y) in its current pose.
        private static float MeshTop(SkinnedMeshRenderer smr)
        {
            var m = new Mesh();
            smr.BakeMesh(m, false);
            Matrix4x4 toWorld = smr.transform.localToWorldMatrix;
            float top = float.NegativeInfinity;
            foreach (Vector3 v in m.vertices) top = Mathf.Max(top, toWorld.MultiplyPoint3x4(v).y);
            Object.DestroyImmediate(m);
            return top;
        }

        /// <summary>Re-points freshly built car prefabs at the existing roster (called by VehicleFactory after a build).</summary>
        public static string RepatchPrefabs()
        {
            var roster = AssetDatabase.LoadAssetAtPath<PedDriverRoster>(RosterPath);
            return roster != null && roster.Count > 0 ? "[PedDriverBuilder]\n" + PatchPrefabs(roster) : "[PedDriverBuilder] no roster yet (run Build Pedestrian Drivers)";
        }

        // Every car prefab uses the roster; its driver visual sits exactly on the driver seat (the mesh is in seat space).
        private static string PatchPrefabs(PedDriverRoster roster)
        {
            var sb = new StringBuilder();
            int traffic = 0, drivable = 0;
            // Highest driver crown each car's roof allows (seat space), from its drivable prefab (seat + fitted player rig).
            var limits = new Dictionary<string, float>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs/Vehicles/Drivable" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try { limits[root.name.Replace("Drivable_", "")] = CrownLimit(root.transform); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            int scaledCars = 0;
            foreach (var pair in limits)
            {
                if (pair.Value <= 0f) continue;
                float smallest = 1f;
                for (int i = 0; i < roster.Count; i++) smallest = Mathf.Min(smallest, roster.SeatedScale(i, pair.Value));
                if (smallest < 1f) { scaledCars++; sb.AppendLine($"  {pair.Key}: low roof, tallest driver sits at {smallest:F2}"); }
            }
            foreach (string folder in new[] { "Assets/_Project/Prefabs/Vehicles/Traffic", "Assets/_Project/Prefabs/Vehicles/Drivable" })
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        bool changed = false;
                        limits.TryGetValue(root.name.Replace("Traffic_", "").Replace("Drivable_", ""), out float limit);
                        var adapter = root.GetComponent<TrafficVehicleAdapter>();
                        if (adapter != null)
                        {
                            adapter.EditorSetDriverRoster(roster, limit);
                            NormaliseVisual(adapter.EditorDriverVisual, root.transform);
                            roster.ApplySeated(adapter.EditorDriverVisual, 0, limit);
                            traffic++;
                            changed = true;
                        }
                        var driver = root.GetComponent<TrafficVehicleDriver>();
                        if (driver != null)
                        {
                            driver.EditorSetDriverCrownLimit(limit);
                            NormaliseVisual(driver.EditorSeatedVisual, root.transform);
                            roster.ApplySeated(driver.EditorSeatedVisual, 0, limit);
                            drivable++;
                            changed = true;
                        }
                        if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
            sb.AppendLine($"  prefabs: {traffic} traffic, {drivable} drivable now use pedestrian drivers ({scaledCars} low-roof cars scale tall drivers)");
            return sb.ToString();
        }

        // Highest crown (driver seat space, y) the roof allows over the seated player's head, minus the clearance; 0 = open top
        // or no fitted rig (no limit). The roof is the lowest car surface in the roof band over the head and a ring around it.
        private static float CrownLimit(Transform root)
        {
            Transform seat = root.Find("DriverSeat");
            Transform model = root.Find("Model");
            Transform rig = root.Find("DriverSeat/CarPlayer/JasonSeated");
            if (seat == null || model == null || rig == null) return 0f;
            Transform head = null;
            foreach (Transform t in rig.GetComponentsInChildren<Transform>(true)) if (t.name == "mixamorig:Head") head = t;
            if (head == null) return 0f;
            Vector3 crown = head.position + Vector3.up * (0.17f * VehicleFactory.PlayerScale);

            var tris = new List<Vector3>();
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                string n = mf.name.ToLowerInvariant();
                if (n.Contains("wheel") && !n.Contains("steer")) continue;
                Mesh m = mf.sharedMesh;
                if (m == null || !m.isReadable) continue;
                Vector3[] v = m.vertices; int[] tr = m.triangles; Matrix4x4 mat = mf.transform.localToWorldMatrix;
                foreach (int i in tr) tris.Add(mat.MultiplyPoint3x4(v[i]));
            }
            float roofTop = float.NegativeInfinity;
            Vector3 local0 = root.InverseTransformPoint(crown);
            foreach (Vector3 q in tris)
            {
                Vector3 l = root.InverseTransformPoint(q);
                if (Mathf.Abs(l.z - local0.z) < 0.3f && Mathf.Abs(l.x) < 0.5f && q.y > roofTop) roofTop = q.y;
            }
            float band = Mathf.Max(crown.y - 0.12f, roofTop - 0.25f);
            float gap = float.PositiveInfinity;
            for (int k = 0; k < 9; k++)
            {
                Vector3 p = crown + (k == 0 ? Vector3.zero : Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward * 0.09f);
                for (int i = 0; i + 2 < tris.Count; i += 3)
                {
                    Vector3 a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    float d1 = (p.x - b.x) * (a.z - b.z) - (a.x - b.x) * (p.z - b.z);
                    float d2 = (p.x - c.x) * (b.z - c.z) - (b.x - c.x) * (p.z - c.z);
                    float d3 = (p.x - a.x) * (c.z - a.z) - (c.x - a.x) * (p.z - a.z);
                    if ((d1 < 0f || d2 < 0f || d3 < 0f) && (d1 > 0f || d2 > 0f || d3 > 0f)) continue;
                    float den = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(den) < 1e-7f) continue;
                    float l1 = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / den;
                    float l2 = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / den;
                    float y = l1 * a.y + l2 * b.y + (1f - l1 - l2) * c.y;
                    if (y >= band) gap = Mathf.Min(gap, y - p.y);
                }
            }
            if (float.IsInfinity(gap)) return 0f;
            return seat.InverseTransformPoint(crown).y + gap - DriverRoofClearance;
        }

        // The visual and its mesh child sit at the driver seat, unrotated and unscaled (the baked mesh is in seat space).
        private static void NormaliseVisual(GameObject visual, Transform root)
        {
            if (visual == null) return;
            Transform seat = root.Find("DriverSeat");
            Transform v = visual.transform;
            if (seat != null && v.parent != seat) v.SetParent(seat, true);
            v.localPosition = Vector3.zero; v.localRotation = Quaternion.identity; v.localScale = Vector3.one;
            var filter = visual.GetComponentInChildren<MeshFilter>(true);
            if (filter != null && filter.transform != v)
            {
                filter.transform.localPosition = Vector3.zero; filter.transform.localRotation = Quaternion.identity; filter.transform.localScale = Vector3.one;
            }
            var lod = visual.GetComponent<LODGroup>();
            if (lod != null) lod.RecalculateBounds();
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
