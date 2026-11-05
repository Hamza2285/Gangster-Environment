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
    ///    seat), with each driver's seat height and recline for that car's roof.
    /// Menu: Tools > OpenworldCrime > Vehicles > Build Pedestrian Drivers. Re-runnable.
    /// </summary>
    public static class PedDriverBuilder
    {
        private const string OutFolder = "Assets/_Project/Characters/PedDrivers";
        private const string RosterPath = "Assets/_Project/Data/Vehicles/PedDriverRoster.asset";
        private const string CarjackControllerPath = "Assets/_Project/Animations/PedCarjack.controller";
        private const string StumbleClipsPath = "Assets/Invector-3rdPersonController/Melee Combat/3DModels/Animations/Melee_CombatSet.fbx";
        private const string IdleClipsPath = "Assets/Invector-3rdPersonController/Basic Locomotion/3D Models/Animations/Basic_FreeMovement.fbx";
        private const string SeatedClipPath = "Assets/CustomAnimations/Vehicle/Exiting Car.fbx";
        private const float SeatedLeanDegrees = 23f;
        private const float MaxLeanDegrees = 50f;
        // The fitted seated player's crown above his hips over his standing height (0.705 m / 1.76 m).
        private const float PlayerSeatedCrownRatio = 0.40f;
        // The hips of the CarPlayer driver figure in driver seat space (measured on the final cars; every seat is fitted to it).
        internal static readonly Vector3 PlayerSeatedHips = new Vector3(0f, 0.87f, -0.11f);
        private const float HeadLevelShare = 0.55f;
        // The seated player's crown relative to his hips (VehicleFactory's fitted rig), front to back.
        private const float PlayerCrownBehindHips = -0.217f * VehicleFactory.PlayerScale;

        // Humanoid bone -> Biped bone names (first match wins).
        private static readonly (string human, string[] biped)[] BoneMap =
        {
            ("Hips", new[] { "Bip01 Pelvis", "mixamorig:Hips" }),
            ("Spine", new[] { "Bip01 Spine", "mixamorig:Spine" }),
            ("Chest", new[] { "Bip01 Spine1", "mixamorig:Spine1" }),
            ("UpperChest", new[] { "Bip01 Spine2", "mixamorig:Spine2" }),
            ("Neck", new[] { "Bip01 Neck", "mixamorig:Neck" }),
            ("Head", new[] { "Bip01 Head", "mixamorig:Head" }),
            ("LeftShoulder", new[] { "Bip01 L Clavicle", "mixamorig:LeftShoulder" }),
            ("LeftUpperArm", new[] { "Bip01 L UpperArm", "mixamorig:LeftArm" }),
            ("LeftLowerArm", new[] { "Bip01 L Forearm", "mixamorig:LeftForeArm" }),
            ("LeftHand", new[] { "Bip01 L Hand", "mixamorig:LeftHand" }),
            ("RightShoulder", new[] { "Bip01 R Clavicle", "mixamorig:RightShoulder" }),
            ("RightUpperArm", new[] { "Bip01 R UpperArm", "mixamorig:RightArm" }),
            ("RightLowerArm", new[] { "Bip01 R Forearm", "mixamorig:RightForeArm" }),
            ("RightHand", new[] { "Bip01 R Hand", "mixamorig:RightHand" }),
            ("LeftUpperLeg", new[] { "Bip01 L Thigh", "mixamorig:LeftUpLeg" }),
            ("LeftLowerLeg", new[] { "Bip01 L Calf", "mixamorig:LeftLeg" }),
            ("LeftFoot", new[] { "Bip01 L Foot", "mixamorig:LeftFoot" }),
            ("LeftToes", new[] { "Bip01 L Toe0", "mixamorig:LeftToeBase" }),
            ("RightUpperLeg", new[] { "Bip01 R Thigh", "mixamorig:RightUpLeg" }),
            ("RightLowerLeg", new[] { "Bip01 R Calf", "mixamorig:RightLeg" }),
            ("RightFoot", new[] { "Bip01 R Foot", "mixamorig:RightFoot" }),
            ("RightToes", new[] { "Bip01 R Toe0", "mixamorig:RightToeBase" }),
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
            if (seated == null) return "[PedDriverBuilder] Missing the seated clip.";
            RuntimeAnimatorController controller = BuildCarjackController(seated);
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

        // Seated (first frame of the exit clip), pulled out (the clip), then an idle and four directional stumbles.
        private static RuntimeAnimatorController BuildCarjackController(AnimationClip seated)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(CarjackControllerPath) != null) AssetDatabase.DeleteAsset(CarjackControllerPath);
            UnityEditor.Animations.AnimatorController controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(CarjackControllerPath);
            UnityEditor.Animations.AnimatorStateMachine machine = controller.layers[0].stateMachine;
            UnityEditor.Animations.AnimatorState seatedState = machine.AddState("Vehicle_Seated");
            seatedState.motion = seated;
            seatedState.speed = 0f;
            machine.defaultState = seatedState;
            machine.AddState("Vehicle_PulledOut").motion = seated;
            machine.AddState("Idle").motion = FindClip(IdleClipsPath, "Idle") ?? seated;
            machine.AddState("Carjack_StumbleForward").motion = FindClip(StumbleClipsPath, "Hit Big Back") ?? FindClip(StumbleClipsPath, "Back") ?? seated;
            machine.AddState("Carjack_StumbleBackward").motion = FindClip(StumbleClipsPath, "Hit Big Front") ?? FindClip(StumbleClipsPath, "Front") ?? seated;
            machine.AddState("Carjack_Stumble").motion = FindClip(StumbleClipsPath, "Hit Big Right") ?? FindClip(StumbleClipsPath, "Right") ?? seated;
            machine.AddState("Carjack_StumbleRight").motion = FindClip(StumbleClipsPath, "Hit Big Left") ?? FindClip(StumbleClipsPath, "Left") ?? seated;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static AnimationClip FindClip(string path, string nameContains)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is AnimationClip c && !c.name.StartsWith("__preview") && c.name.Contains(nameContains)) return c;
            return null;
        }

        private static string BipedOrMixamo(Dictionary<string, Transform> bones, string biped, string mixamo) => bones.ContainsKey(biped) ? biped : mixamo;

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
                bones.TryGetValue(BipedOrMixamo(bones, "Bip01 Pelvis", "mixamorig:Hips"), out Transform pelvisBone);
                bones.TryGetValue(BipedOrMixamo(bones, "Bip01 Head", "mixamorig:Head"), out Transform headBone);
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
                Vector3 seatOffset = bones.TryGetValue(BipedOrMixamo(bones, "Bip01 Pelvis", "mixamorig:Hips"), out Transform pelvis) ? PlayerSeatedHips - pelvis.position : Vector3.zero;
                seatOffset.x = 0f;

                var baked = new Mesh { name = prefab.name + "_Seated" };
                smr.BakeMesh(baked, false);
                // Unscaled bake = renderer space: through the renderer's matrix to world, then into the root's space at scale 1.
                Vector3[] v = baked.vertices; Vector3[] n = baked.normals;
                Matrix4x4 toWorld = smr.transform.localToWorldMatrix;
                Quaternion rootInverse = Quaternion.Inverse(inst.transform.rotation);
                for (int i = 0; i < v.Length; i++) v[i] = rootInverse * (toWorld.MultiplyPoint3x4(v[i]) - inst.transform.position) + seatOffset;
                for (int i = 0; i < n.Length; i++) n[i] = (rootInverse * toWorld.MultiplyVector(n[i])).normalized;
                // Pedestrians sit taller than the player (more upright, a little bigger); each car seats them lower and more
                // reclined about these hips as its roof requires (ComputeFits).
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
                    pedPrefab = prefab,
                    police = prefab.name.Contains("Police"),
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
            string B(string biped, string mixamo) => BipedOrMixamo(bones, biped, mixamo);
            Aim(B("Bip01 L UpperArm", "mixamorig:LeftArm"), B("Bip01 L Forearm", "mixamorig:LeftForeArm"), -right);
            Aim(B("Bip01 L Forearm", "mixamorig:LeftForeArm"), B("Bip01 L Hand", "mixamorig:LeftHand"), -right);
            Aim(B("Bip01 R UpperArm", "mixamorig:RightArm"), B("Bip01 R Forearm", "mixamorig:RightForeArm"), right);
            Aim(B("Bip01 R Forearm", "mixamorig:RightForeArm"), B("Bip01 R Hand", "mixamorig:RightHand"), right);
            Aim(B("Bip01 L Thigh", "mixamorig:LeftUpLeg"), B("Bip01 L Calf", "mixamorig:LeftLeg"), down);
            Aim(B("Bip01 L Calf", "mixamorig:LeftLeg"), B("Bip01 L Foot", "mixamorig:LeftFoot"), down);
            Aim(B("Bip01 R Thigh", "mixamorig:RightUpLeg"), B("Bip01 R Calf", "mixamorig:RightLeg"), down);
            Aim(B("Bip01 R Calf", "mixamorig:RightLeg"), B("Bip01 R Foot", "mixamorig:RightFoot"), down);
        }

        // Torso rotated back about the hips over the spine chain; the head turns part of it back to keep looking ahead.
        private static void Lean(Transform root, Dictionary<string, Transform> bones, float degrees)
        {
            string[] spine = bones.ContainsKey("Bip01 Spine") ? new[] { "Bip01 Spine", "Bip01 Spine1", "Bip01 Spine2" } : new[] { "mixamorig:Spine", "mixamorig:Spine1", "mixamorig:Spine2" };
            int links = 0;
            foreach (string s in spine) if (bones.ContainsKey(s)) links++;
            if (links == 0) return;
            foreach (string s in spine)
                if (bones.TryGetValue(s, out Transform t)) t.rotation = Quaternion.AngleAxis(-degrees / links, root.right) * t.rotation;
            if (bones.TryGetValue(BipedOrMixamo(bones, "Bip01 Head", "mixamorig:Head"), out Transform head)) head.rotation = Quaternion.AngleAxis(degrees * HeadLevelShare, root.right) * head.rotation;
            // The arms keep reaching forward to the wheel instead of rising with the reclined torso.
            foreach (string arm in new[] { BipedOrMixamo(bones, "Bip01 L UpperArm", "mixamorig:LeftArm"), BipedOrMixamo(bones, "Bip01 R UpperArm", "mixamorig:RightArm") })
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

        /// <summary>Re-points freshly built car prefabs at the existing roster and fits its drivers to their roofs (called after a build).</summary>
        public static string RepatchPrefabs()
        {
            var roster = AssetDatabase.LoadAssetAtPath<PedDriverRoster>(RosterPath);
            return roster != null && roster.Count > 0 ? "[PedDriverBuilder]\n" + PatchPrefabs(roster) : "[PedDriverBuilder] no roster yet (run Build Pedestrian Drivers)";
        }

        // Every car prefab uses the roster. Each driver is seated full size: per car and per driver, the smallest change of seat height
        // and recline that clears the actual roof mesh, measured from the baked head against the car's triangles.
        private static string PatchPrefabs(PedDriverRoster roster)
        {
            var sb = new StringBuilder();
            SetHeadCenters(roster);
            AssetDatabase.SaveAssets();

            var fitsByCar = new Dictionary<string, PedDriverRoster.SeatFit[]>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs/Vehicles/Drivable" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Transform floor = root.transform.Find("CabinFloor");
                    string car = root.name.Replace("Drivable_", "");
                    fitsByCar[car] = ComputeFits(root.transform, root.transform.Find("DriverSeat"), floor != null ? floor.position.y : float.NegativeInfinity, roster, false, car, sb);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            int traffic = 0, drivable = 0;
            foreach (string folder in new[] { "Assets/_Project/Prefabs/Vehicles/Traffic", "Assets/_Project/Prefabs/Vehicles/Drivable" })
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        bool changed = false;
                        fitsByCar.TryGetValue(root.name.Replace("Traffic_", "").Replace("Drivable_", ""), out PedDriverRoster.SeatFit[] fits);
                        var adapter = root.GetComponent<TrafficVehicleAdapter>();
                        if (adapter != null)
                        {
                            // The police car has no drivable twin: it is measured here, with officers only.
                            if (fits == null) fits = ComputeFits(root.transform, adapter.EditorDriverVisual.transform.parent, float.NegativeInfinity, roster, true, root.name, sb);
                            adapter.EditorSetDriverRoster(roster, fits);
                            NormaliseVisual(adapter.EditorDriverVisual, root.transform);
                            traffic++;
                            changed = true;
                        }
                        var driver = root.GetComponent<TrafficVehicleDriver>();
                        if (driver != null)
                        {
                            driver.EditorSetDriverFits(fits);
                            NormaliseVisual(driver.EditorSeatedVisual, root.transform);
                            drivable++;
                            changed = true;
                        }
                        if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
            sb.AppendLine($"  prefabs: {traffic} traffic, {drivable} drivable use pedestrian drivers, full size");
            return sb.ToString();
        }

        // Where each driver's head zone goes: the middle of the top of his baked mesh, in seat space.
        private static void SetHeadCenters(PedDriverRoster roster)
        {
            for (int i = 0; i < roster.Count; i++)
            {
                PedDriverRoster.Entry e = roster.Get(i);
                if (e == null || e.seatedMesh == null) continue;
                Vector3 sum = Vector3.zero;
                int count = 0;
                foreach (Vector3 v in e.seatedMesh.vertices)
                {
                    if (v.y < e.seatedCrownY - HeadHeight) continue;
                    sum += v;
                    count++;
                }
                if (count > 0) e.seatedHeadCenter = sum / count;
            }
            EditorUtility.SetDirty(roster);
        }

        private const float HeadHeight = 0.24f;
        private const float RoofClearance = 0.02f;
        private const float MaxReclineDegrees = 16f;
        private const float MaxDrop = 0.18f;
        private const float MinHipsAboveFloor = 0.04f;
        private const float RoofGridHalfSize = 0.5f;
        private const float RoofGridStep = 0.03f;
        // Hands on the wheel relative to the seated hips (the cabin fit): what recline and drop should disturb least.
        private static readonly Vector3 HandsFromHips = new Vector3(0f, 0.32f, 0.47f);

        private static PedDriverRoster.SeatFit[] ComputeFits(Transform root, Transform seat, float floorY, PedDriverRoster roster, bool police, string carName, StringBuilder log)
        {
            var fits = new PedDriverRoster.SeatFit[roster.Count];
            RoofGrid grid = BuildRoofGrid(root, seat);
            float worstGap = float.PositiveInfinity, largestDrop = 0f, largestRecline = 0f, largestHands = 0f;
            int failed = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                PedDriverRoster.Entry e = roster.Get(i);
                if (e == null || e.seatedMesh == null || e.police != police) continue;
                float hipsY = seat.TransformPoint(e.seatedHips).y;
                float maxDrop = Mathf.Clamp(hipsY - floorY - MinHipsAboveFloor, 0f, MaxDrop);
                List<Vector3> head = HeadPoints(e);
                Vector3 hands = e.seatedHips + HandsFromHips;

                var best = default(PedDriverRoster.SeatFit);
                float bestHands = float.PositiveInfinity, bestGap = float.NegativeInfinity;
                PedDriverRoster.SeatFit fallback = default;
                for (float recline = 0f; recline <= MaxReclineDegrees; recline += 2f)
                    for (float drop = 0f; drop <= maxDrop + 1e-4f; drop += 0.01f)
                    {
                        var fit = new PedDriverRoster.SeatFit { drop = drop, recline = recline };
                        PedDriverRoster.GetSeatedPose(e.seatedHips, fit, out Vector3 position, out Quaternion rotation);
                        Matrix4x4 toWorld = seat.localToWorldMatrix * Matrix4x4.TRS(position, rotation, Vector3.one);
                        float gap = grid.Clearance(head, toWorld);
                        if (gap > bestGap) { bestGap = gap; fallback = fit; }
                        if (gap < RoofClearance) continue;
                        float handMove = (position + rotation * hands - hands).magnitude;
                        if (handMove < bestHands) { bestHands = handMove; best = fit; }
                    }
                bool ok = !float.IsPositiveInfinity(bestHands);
                if (!ok) { failed++; best = fallback; }
                fits[i] = best;
                worstGap = Mathf.Min(worstGap, ok ? RoofClearance : bestGap);
                largestDrop = Mathf.Max(largestDrop, best.drop);
                largestRecline = Mathf.Max(largestRecline, best.recline);
                if (ok) largestHands = Mathf.Max(largestHands, bestHands);
            }
            log.AppendLine($"  {carName}: drop up to {largestDrop:F2} m, recline up to {largestRecline:F0} deg, hands move up to {largestHands:F2} m, worst head gap {worstGap:F2} m{(failed > 0 ? $", {failed} DRIVERS DO NOT FIT" : "")}");
            return fits;
        }

        private static List<Vector3> HeadPoints(PedDriverRoster.Entry e)
        {
            var points = new List<Vector3>();
            Vector3[] vertices = e.seatedMesh.vertices;
            foreach (Vector3 v in vertices) if (v.y > e.seatedCrownY - HeadHeight && (v - e.seatedHeadCenter).sqrMagnitude < 0.09f) points.Add(v);
            if (points.Count <= 120) return points;
            var thinned = new List<Vector3>(120);
            int step = points.Count / 120 + 1;
            for (int i = 0; i < points.Count; i += step) thinned.Add(points[i]);
            return thinned;
        }

        // Underside of the roof (world y) on a grid around the driver's head; infinite where nothing covers the column.
        private sealed class RoofGrid
        {
            public Vector3 Origin;
            public Vector3 AxisX, AxisZ;
            public float[,] Heights;

            public float Clearance(List<Vector3> head, Matrix4x4 toWorld)
            {
                float gap = float.PositiveInfinity;
                int size = Heights.GetLength(0);
                foreach (Vector3 v in head)
                {
                    Vector3 p = toWorld.MultiplyPoint3x4(v);
                    Vector3 d = p - Origin;
                    int ix = Mathf.RoundToInt(Vector3.Dot(d, AxisX) / RoofGridStep), iz = Mathf.RoundToInt(Vector3.Dot(d, AxisZ) / RoofGridStep);
                    if (ix < 0 || iz < 0 || ix >= size || iz >= size) continue;
                    gap = Mathf.Min(gap, Heights[ix, iz] - p.y);
                }
                return gap;
            }
        }

        private static RoofGrid BuildRoofGrid(Transform root, Transform seat)
        {
            var triangles = new List<Vector3>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.transform.IsChildOf(seat) || IsWheelOrLamp(filter.transform, root)) continue;
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;
                Vector3[] vertices = mesh.vertices;
                int[] indices = mesh.triangles;
                Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
                foreach (int index in indices) triangles.Add(toWorld.MultiplyPoint3x4(vertices[index]));
            }

            int size = Mathf.RoundToInt(2f * RoofGridHalfSize / RoofGridStep) + 1;
            Vector3 center = seat.TransformPoint(PlayerSeatedHips) + Vector3.up * 0.7f;
            var grid = new RoofGrid { AxisX = Vector3.right, AxisZ = Vector3.forward, Heights = new float[size, size] };
            grid.Origin = center - Vector3.right * RoofGridHalfSize - Vector3.forward * RoofGridHalfSize;
            float minY = center.y - 0.35f;

            var crossings = new List<float>[size, size];
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                Vector3 a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                if (a.y < minY && b.y < minY && c.y < minY) continue;
                int x0 = Mathf.CeilToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - grid.Origin.x) / RoofGridStep), x1 = Mathf.FloorToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - grid.Origin.x) / RoofGridStep);
                int z0 = Mathf.CeilToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - grid.Origin.z) / RoofGridStep), z1 = Mathf.FloorToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - grid.Origin.z) / RoofGridStep);
                float den = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(den) < 1e-7f) continue;
                for (int ix = Mathf.Max(0, x0); ix <= x1 && ix < size; ix++)
                    for (int iz = Mathf.Max(0, z0); iz <= z1 && iz < size; iz++)
                    {
                        float x = grid.Origin.x + ix * RoofGridStep, z = grid.Origin.z + iz * RoofGridStep;
                        float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / den;
                        float l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / den;
                        float l3 = 1f - l1 - l2;
                        if (l1 < 0f || l2 < 0f || l3 < 0f) continue;
                        float y = l1 * a.y + l2 * b.y + l3 * c.y;
                        if (y < minY) continue;
                        if (crossings[ix, iz] == null) crossings[ix, iz] = new List<float>();
                        crossings[ix, iz].Add(y);
                    }
            }
            for (int ix = 0; ix < size; ix++)
                for (int iz = 0; iz < size; iz++)
                {
                    List<float> column = crossings[ix, iz];
                    if (column == null) { grid.Heights[ix, iz] = float.PositiveInfinity; continue; }
                    float top = float.NegativeInfinity;
                    foreach (float y in column) top = Mathf.Max(top, y);
                    float under = top;
                    foreach (float y in column) if (y >= top - 0.08f && y < under) under = y;
                    grid.Heights[ix, iz] = under;
                }
            return grid;
        }

        private static bool IsWheelOrLamp(Transform part, Transform root)
        {
            for (Transform t = part; t != null && t != root; t = t.parent)
            {
                string n = t.name.ToLowerInvariant();
                if (n.StartsWith("wheel") || n == "tire" || n.StartsWith("flash") || n.Contains("light")) return true;
            }
            return false;
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
