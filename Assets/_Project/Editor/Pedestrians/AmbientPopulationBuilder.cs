using System.Collections.Generic;
using System.IO;
using System.Text;
using Invector.vCharacterController;
using OpenworldCrime.Environment;
using OpenworldCrime.Pedestrians;
using OpenworldCrime.Player;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Everything the standing and sitting people need, rerunnable:
    /// 1. Sit clips: the project has no sit animation, so they are posed here on the pedestrians' Biped rig from the standing idle
    ///    (legs and arms by two-bone IK) and written as ordinary Generic clips with a small look-around, one set for men, one for women.
    /// 2. Controllers: copies of the man / girl controllers with the sit states, assigned to the Ped_ variants (the source controllers
    ///    are not touched), so sitting costs no avatar swap and blends into running.
    /// 3. Spots: benches, low walls and ledges, and open ground beside the footpath lanes, checked against the scene's colliders,
    ///    the panic NavMesh and the traffic lanes, stored in AmbientSpotData.
    /// 4. Scene: the managers under ///Manager/// and the player's vehicle impact component.
    /// Menu: Tools > OpenworldCrime > Population.
    /// </summary>
    public static class AmbientPopulationBuilder
    {
        private const string ClipFolder = "Assets/_Project/Animations/Pedestrians";
        private const string PedFolder = "Assets/_Project/Prefabs/Pedestrians";
        private const string SpotDataPath = "Assets/_Project/Data/Pedestrians/AmbientSpotData.asset";
        private const string SettingsPath = "Assets/_Project/Data/Pedestrians/AmbientPopulationSettings.asset";
        private const string PanicSettingsPath = "Assets/_Project/Data/Vehicles/TrafficPanicSettings.asset";
        private const string SourceManController = "Assets/PopulationSystem/Animations/man controller.controller";
        private const string SourceGirlController = "Assets/PopulationSystem/Animations/girl controller.controller";
        private const string RepresentativeMan = PedFolder + "/Ped_Man_11.prefab";
        private const string RepresentativeGirl = PedFolder + "/Ped_Girl_44.prefab";

        // The lavka bench mesh in its own space: the seat, and which way a sitter faces (away from the backrest).
        private const string BenchMeshName = "Lavka_07";
        private const float BenchSeatLocalZ = 0.30f;
        private const float BenchSeatLocalY = -0.06f;
        private const float BenchSeatLocalX = 0.55f;

        private const float MinSeatHeight = 0.38f;
        private const float MinBenchSeatHeight = 0.25f;
        private const float MaxSeatHeight = 0.9f;
        private const float MinLaneDistanceSeat = 2.5f;
        private const float MinLaneDistanceTalk = 3.5f;
        private const float TalkSpacing = 16f;
        private const float LedgeSpacing = 10f;
        private const int MaxTalkSpots = 900;
        private const int MaxLedgeSpots = 300;

        private enum HandMode { Thighs, Knees, BesideHips }

        private struct SitPose
        {
            public string State;
            public float HipsHeight;
            public float Lean;
            public float FootForward;
            public float FootSide;
            public HandMode Hands;
        }

        // High seats (benches, walls): thighs sloping down. Low seats (ledges): knees up.
        private static readonly SitPose[] ManPoses =
        {
            new SitPose { State = "sit_a", HipsHeight = 0.78f, Lean = 4f, FootForward = 0.24f, FootSide = 0.17f, Hands = HandMode.Thighs },
            new SitPose { State = "sit_b", HipsHeight = 0.78f, Lean = 24f, FootForward = 0.20f, FootSide = 0.20f, Hands = HandMode.Knees },
            new SitPose { State = "sit_c", HipsHeight = 0.78f, Lean = -7f, FootForward = 0.32f, FootSide = 0.20f, Hands = HandMode.BesideHips },
            new SitPose { State = "sit_low_a", HipsHeight = 0.46f, Lean = 5f, FootForward = 0.50f, FootSide = 0.17f, Hands = HandMode.Thighs },
            new SitPose { State = "sit_low_b", HipsHeight = 0.46f, Lean = 20f, FootForward = 0.46f, FootSide = 0.20f, Hands = HandMode.Knees },
            new SitPose { State = "sit_low_c", HipsHeight = 0.46f, Lean = -6f, FootForward = 0.58f, FootSide = 0.20f, Hands = HandMode.BesideHips },
        };

        private static readonly SitPose[] GirlPoses =
        {
            new SitPose { State = "sit_a", HipsHeight = 0.78f, Lean = 3f, FootForward = 0.22f, FootSide = 0.08f, Hands = HandMode.Thighs },
            new SitPose { State = "sit_b", HipsHeight = 0.78f, Lean = 18f, FootForward = 0.18f, FootSide = 0.11f, Hands = HandMode.Knees },
            new SitPose { State = "sit_c", HipsHeight = 0.78f, Lean = -6f, FootForward = 0.30f, FootSide = 0.10f, Hands = HandMode.BesideHips },
            new SitPose { State = "sit_low_a", HipsHeight = 0.46f, Lean = 4f, FootForward = 0.48f, FootSide = 0.09f, Hands = HandMode.Thighs },
            new SitPose { State = "sit_low_b", HipsHeight = 0.46f, Lean = 16f, FootForward = 0.44f, FootSide = 0.11f, Hands = HandMode.Knees },
            new SitPose { State = "sit_low_c", HipsHeight = 0.46f, Lean = -5f, FootForward = 0.54f, FootSide = 0.11f, Hands = HandMode.BesideHips },
        };

        [MenuItem("Tools/OpenworldCrime/Population/Build Ambient Population (All)")]
        public static void BuildAllMenu() => Debug.Log(BuildAll());

        [MenuItem("Tools/OpenworldCrime/Population/Build Ambient Sit Clips")]
        public static void BuildClipsMenu() => Debug.Log(BuildClipsAndControllers());

        [MenuItem("Tools/OpenworldCrime/Population/Find Ambient Spots")]
        public static void FindSpotsMenu() => Debug.Log(FindSpots());

        [MenuItem("Tools/OpenworldCrime/Population/Wire Ambient Scene Objects")]
        public static void WireMenu() => Debug.Log(WireScene());

        public static string BuildAll() => BuildClipsAndControllers() + "\n" + FindSpots() + "\n" + WireScene();

        // ---------- clips and controllers ----------

        public static string BuildClipsAndControllers()
        {
            EnsureFolder(ClipFolder);
            var sb = new StringBuilder("[AmbientPopulationBuilder] clips\n");
            AnimatorController man = BuildFamily("Man", RepresentativeMan, SourceManController, ManPoses, "man_idle1", sb);
            AnimatorController girl = BuildFamily("Girl", RepresentativeGirl, SourceGirlController, GirlPoses, "girl_idle1", sb);
            if (man == null || girl == null) return sb.Append("FAILED: missing source prefab or controller").ToString();

            int assigned = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PedFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileNameWithoutExtension(path);
                AnimatorController controller = fileName.StartsWith("Ped_Man") ? man : fileName.StartsWith("Ped_Girl") ? girl : null;
                if (controller == null) continue;
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var animator = root.GetComponent<Animator>();
                    if (animator == null || animator.runtimeAnimatorController == controller) continue;
                    animator.runtimeAnimatorController = controller;
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    assigned++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            return sb.Append($"  controllers assigned to {assigned} prefabs (already assigned ones skipped)").ToString();
        }

        private static AnimatorController BuildFamily(string family, string prefabPath, string sourceController, SitPose[] poses, string idleClipName, StringBuilder log)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null || AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(sourceController) == null) return null;

            string controllerPath = $"{ClipFolder}/Ped{family}_Ambient.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) == null) AssetDatabase.CopyAsset(sourceController, controllerPath);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            AnimationClip idle = null;
            foreach (AnimationClip c in controller.animationClips)
                if (c != null && c.name == idleClipName) idle = c;
            if (idle == null) { log.AppendLine($"  {family}: no idle clip {idleClipName}"); return null; }

            GameObject inst = Object.Instantiate(prefab);
            inst.hideFlags = HideFlags.HideAndDontSave;
            foreach (MonoBehaviour mb in inst.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
            try
            {
                foreach (SitPose pose in poses)
                {
                    AnimationClip clip = BakeSitClip(inst, idle, pose, $"Ped{family}_{pose.State}");
                    string clipPath = $"{ClipFolder}/Ped{family}_{pose.State}.anim";
                    var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                    if (existing != null)
                    {
                        EditorUtility.CopySerialized(clip, existing);
                        clip = existing;
                    }
                    else AssetDatabase.CreateAsset(clip, clipPath);

                    AnimatorState state = null;
                    foreach (ChildAnimatorState s in machine.states) if (s.state.name == pose.State) state = s.state;
                    if (state == null) state = machine.AddState(pose.State);
                    state.motion = clip;
                    log.AppendLine($"  {family} {pose.State}: {clipPath}");
                }
            }
            finally { Object.DestroyImmediate(inst); }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static AnimationClip BakeSitClip(GameObject inst, AnimationClip idle, SitPose pose, string clipName)
        {
            var bones = new Dictionary<string, Transform>();
            foreach (Transform t in inst.GetComponentsInChildren<Transform>(true)) if (!bones.ContainsKey(t.name)) bones[t.name] = t;

            ApplyPose(inst, idle, pose, bones);

            const float loop = 12f;
            var clip = new AnimationClip { name = clipName, frameRate = 30f };
            Transform root = inst.transform;
            Transform head = bones["Bip01 Head"], neck = bones["Bip01 Neck"];
            Quaternion headBase = head.localRotation, neckBase = neck.localRotation;
            // The look-around lives in the head and neck; everything else holds still.
            float yawA = Random.Range(14f, 24f), yawB = -Random.Range(10f, 20f);
            float[] times = { 0f, 3.5f, 5.5f, 8.5f, 10f, loop };
            float[] yaws = { 0f, yawA, yawA * 0.8f, yawB, yawB * 0.7f, 0f };

            foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
            {
                if (t == root || !t.name.StartsWith("Bip01")) continue;
                string path = AnimationUtility.CalculateTransformPath(t, root);
                if (t == head || t == neck)
                {
                    float share = t == head ? 0.7f : 0.3f;
                    Quaternion rest = t == head ? headBase : neckBase;
                    var keys = new Keyframe[4][];
                    for (int c = 0; c < 4; c++) keys[c] = new Keyframe[times.Length];
                    Quaternion previous = rest;
                    for (int k = 0; k < times.Length; k++)
                    {
                        // Turn about the world up axis of the seated pose: the rotation is applied, read back and restored.
                        t.localRotation = rest;
                        t.rotation = Quaternion.AngleAxis(yaws[k] * share, root.up) * t.rotation;
                        Quaternion q = t.localRotation;
                        if (Quaternion.Dot(previous, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        previous = q;
                        keys[0][k] = new Keyframe(times[k], q.x);
                        keys[1][k] = new Keyframe(times[k], q.y);
                        keys[2][k] = new Keyframe(times[k], q.z);
                        keys[3][k] = new Keyframe(times[k], q.w);
                    }
                    t.localRotation = rest;
                    SetCurve(clip, path, "localRotation.x", keys[0]);
                    SetCurve(clip, path, "localRotation.y", keys[1]);
                    SetCurve(clip, path, "localRotation.z", keys[2]);
                    SetCurve(clip, path, "localRotation.w", keys[3]);
                    continue;
                }
                Quaternion local = t.localRotation;
                clip.SetCurve(path, typeof(Transform), "localRotation.x", AnimationCurve.Constant(0f, loop, local.x));
                clip.SetCurve(path, typeof(Transform), "localRotation.y", AnimationCurve.Constant(0f, loop, local.y));
                clip.SetCurve(path, typeof(Transform), "localRotation.z", AnimationCurve.Constant(0f, loop, local.z));
                clip.SetCurve(path, typeof(Transform), "localRotation.w", AnimationCurve.Constant(0f, loop, local.w));
                if (t.name == "Bip01")
                {
                    Vector3 p = t.localPosition;
                    clip.SetCurve(path, typeof(Transform), "localPosition.x", AnimationCurve.Constant(0f, loop, p.x));
                    clip.SetCurve(path, typeof(Transform), "localPosition.y", AnimationCurve.Constant(0f, loop, p.y));
                    clip.SetCurve(path, typeof(Transform), "localPosition.z", AnimationCurve.Constant(0f, loop, p.z));
                }
            }
            clip.EnsureQuaternionContinuity();
            AnimationClipSettings clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
            clipSettings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, clipSettings);
            return clip;
        }

        private static void SetCurve(AnimationClip clip, string path, string property, Keyframe[] keys)
        {
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
            clip.SetCurve(path, typeof(Transform), property, curve);
        }

        // From the standing idle: hips placed, torso leaned, feet on the ground and hands on their targets by two-bone IK.
        private static void ApplyPose(GameObject inst, AnimationClip idle, SitPose pose, Dictionary<string, Transform> bones)
        {
            Transform root = inst.transform;
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            idle.SampleAnimation(inst, 0f);
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            Transform bip = bones["Bip01"], pelvis = bones["Bip01 Pelvis"], spine = bones["Bip01 Spine"], spine1 = bones["Bip01 Spine1"], head = bones["Bip01 Head"];
            float ankleHeight = Mathf.Min(bones["Bip01 L Foot"].position.y, bones["Bip01 R Foot"].position.y);
            Quaternion footL = bones["Bip01 L Foot"].rotation, footR = bones["Bip01 R Foot"].rotation;

            bip.position += new Vector3(-pelvis.position.x, pose.HipsHeight - pelvis.position.y, -pelvis.position.z);

            Quaternion headBefore = head.rotation;
            Quaternion lean = Quaternion.AngleAxis(pose.Lean * 0.5f, Vector3.right);
            spine.rotation = lean * spine.rotation;
            spine1.rotation = lean * spine1.rotation;
            // Head stays nearly level.
            head.rotation = Quaternion.Slerp(head.rotation, headBefore, 0.75f);

            Vector3 up = Vector3.up, forward = Vector3.forward;
            Vector3 kneeL = SolveLeg(bones["Bip01 L Thigh"], bones["Bip01 L Calf"], bones["Bip01 L Foot"], pose, ankleHeight, footL);
            Vector3 kneeR = SolveLeg(bones["Bip01 R Thigh"], bones["Bip01 R Calf"], bones["Bip01 R Foot"], pose, ankleHeight, footR);

            Vector3 hipL = bones["Bip01 L Thigh"].position, hipR = bones["Bip01 R Thigh"].position;
            SolveArm(bones["Bip01 L UpperArm"], bones["Bip01 L Forearm"], bones["Bip01 L Hand"], HandTarget(pose, hipL, kneeL, up, forward), forward);
            SolveArm(bones["Bip01 R UpperArm"], bones["Bip01 R Forearm"], bones["Bip01 R Hand"], HandTarget(pose, hipR, kneeR, up, forward), forward);
        }

        private static Vector3 SolveLeg(Transform thigh, Transform calf, Transform foot, SitPose pose, float ankleHeight, Quaternion footRotation)
        {
            float side = Mathf.Sign(thigh.position.x);
            Vector3 target = new Vector3(side * pose.FootSide, ankleHeight, pose.FootForward);
            Vector3 knee = SolveTwoBone(thigh, calf, foot, target, Vector3.forward + Vector3.up + Vector3.right * side * 0.15f);
            foot.rotation = footRotation;
            return knee;
        }

        private static Vector3 HandTarget(SitPose pose, Vector3 hip, Vector3 knee, Vector3 up, Vector3 forward)
        {
            float side = Mathf.Sign(hip.x);
            switch (pose.Hands)
            {
                case HandMode.Knees: return knee + up * 0.06f - forward * 0.12f - Vector3.right * side * 0.03f;
                case HandMode.BesideHips: return hip + Vector3.right * side * 0.20f - up * 0.10f - forward * 0.10f;
                default: return Vector3.Lerp(hip, knee, 0.62f) + up * 0.09f;
            }
        }

        private static void SolveArm(Transform upper, Transform fore, Transform hand, Vector3 target, Vector3 forward)
        {
            float side = Mathf.Sign(upper.position.x);
            SolveTwoBone(upper, fore, hand, target, Vector3.right * side * 0.8f - forward * 0.7f - Vector3.up * 0.3f);
        }

        // Places a -> b -> c so that c reaches target with the joint b bent toward pole. Returns where b ended up.
        private static Vector3 SolveTwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole)
        {
            float lengthA = Vector3.Distance(a.position, b.position);
            float lengthB = Vector3.Distance(b.position, c.position);
            Vector3 toTarget = target - a.position;
            float distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(lengthA - lengthB) + 0.001f, lengthA + lengthB - 0.001f);
            Vector3 direction = toTarget.normalized;
            float along = (lengthA * lengthA - lengthB * lengthB + distance * distance) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, lengthA * lengthA - along * along));
            Vector3 bend = (pole - direction * Vector3.Dot(pole, direction)).normalized;
            Vector3 joint = a.position + direction * along + bend * height;

            a.rotation = Quaternion.FromToRotation(b.position - a.position, joint - a.position) * a.rotation;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, a.position + direction * distance - b.position) * b.rotation;
            return joint;
        }

        // ---------- spots ----------

        public static string FindSpots()
        {
            var spawner = Object.FindFirstObjectByType<PedestrianSpawner>(FindObjectsInactive.Include);
            var roads = Object.FindFirstObjectByType<TSMainManager>(FindObjectsInactive.Include);
            if (spawner == null || roads == null || spawner.panicNavMesh == null) return "[AmbientPopulationBuilder] Needs a PedestrianSpawner with a panic NavMesh and a TSMainManager in the open scene.";
            var network = spawner.GetComponent<FootpathLaneNetwork>();

            RejectCounts.Clear();
            NavMeshDataInstance navMesh = NavMesh.AddNavMeshData(spawner.panicNavMesh);
            var spots = new List<AmbientSpot>();
            var log = new StringBuilder("[AmbientPopulationBuilder] spots\n");
            try
            {
                var laneGrid = BuildLaneGrid(roads);
                Transform roadRoot = FindSceneRoot("Traffic Road Colliders");
                int benches = AddBenchSpots(spots, laneGrid, roadRoot, log);
                int ledges = AddLedgeSpots(spots, network, laneGrid, roadRoot, log);
                int talks = AddTalkSpots(spots, network, laneGrid, roadRoot, log);
                log.AppendLine($"  {benches} bench seats, {ledges} ledge seats, {talks} talk spots");
            }
            finally { navMesh.Remove(); }

            EnsureFolder(Path.GetDirectoryName(SpotDataPath).Replace('\\', '/'));
            var data = AssetDatabase.LoadAssetAtPath<AmbientSpotData>(SpotDataPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<AmbientSpotData>();
                AssetDatabase.CreateAsset(data, SpotDataPath);
            }
            data.spots = spots.ToArray();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        private static Dictionary<Vector2Int, List<Vector3>> BuildLaneGrid(TSMainManager roads)
        {
            var grid = new Dictionary<Vector2Int, List<Vector3>>();
            void Add(Vector3 p)
            {
                var cell = new Vector2Int(Mathf.FloorToInt(p.x / 8f), Mathf.FloorToInt(p.z / 8f));
                if (!grid.TryGetValue(cell, out List<Vector3> list)) grid[cell] = list = new List<Vector3>();
                list.Add(p);
            }
            foreach (TSLaneInfo lane in roads.lanes)
            {
                for (int i = 0; i < lane.points.Length; i++)
                {
                    Add(lane.points[i].point);
                    // Lane points can be far apart: fill the gaps.
                    if (i + 1 < lane.points.Length)
                    {
                        float length = Vector3.Distance(lane.points[i].point, lane.points[i + 1].point);
                        for (float d = 1.5f; d < length; d += 1.5f) Add(Vector3.Lerp(lane.points[i].point, lane.points[i + 1].point, d / length));
                    }
                }
                foreach (TSLaneConnector connector in lane.connectors)
                    foreach (TSPoints p in connector.points) Add(p.point);
            }
            return grid;
        }

        private static float LaneDistance(Vector3 position, Dictionary<Vector2Int, List<Vector3>> grid, float limit)
        {
            float best = limit * limit;
            var center = new Vector2Int(Mathf.FloorToInt(position.x / 8f), Mathf.FloorToInt(position.z / 8f));
            for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                if (!grid.TryGetValue(center + new Vector2Int(x, z), out List<Vector3> list)) continue;
                foreach (Vector3 p in list)
                {
                    float dx = p.x - position.x, dz = p.z - position.z;
                    float sqr = dx * dx + dz * dz;
                    if (sqr < best) best = sqr;
                }
            }
            return Mathf.Sqrt(best);
        }

        private static int AddBenchSpots(List<AmbientSpot> spots, Dictionary<Vector2Int, List<Vector3>> laneGrid, Transform roadRoot, StringBuilder log)
        {
            int count = 0, rejected = 0, otherMeshes = 0;
            foreach (BreakableProp prop in Object.FindObjectsByType<BreakableProp>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!prop.name.Contains("Bench")) continue;
                MeshFilter filter = prop.GetComponentInChildren<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.name.StartsWith(BenchMeshName)) { otherMeshes++; continue; }
                Transform t = filter.transform;
                Vector3 forward = -t.up;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.5f || t.forward.y < 0.9f) { rejected++; continue; }
                forward.Normalize();
                float yaw = Quaternion.LookRotation(forward).eulerAngles.y;
                foreach (float side in new[] { -BenchSeatLocalX, BenchSeatLocalX })
                {
                    Vector3 seat = t.TransformPoint(side, BenchSeatLocalY, BenchSeatLocalZ);
                    if (ValidateSeat(seat, forward, prop.transform, laneGrid, roadRoot, out float seatHeight))
                    {
                        spots.Add(new AmbientSpot { kind = AmbientSpotKind.Bench, position = seat, yaw = yaw, seatHeight = seatHeight });
                        count++;
                    }
                    else rejected++;
                }
            }
            log.AppendLine($"  benches: {count} seats kept, {rejected} rejected, {otherMeshes} benches with another mesh skipped");
            foreach (var pair in RejectCounts) log.AppendLine($"    rejected: {pair.Key} x{pair.Value}");
            RejectCounts.Clear();
            return count;
        }

        // Ground in front, nothing where the legs and body go, on the walkable NavMesh side, away from traffic lanes.
        private static bool ValidateSeat(Vector3 seat, Vector3 forward, Transform own, Dictionary<Vector2Int, List<Vector3>> laneGrid, Transform roadRoot, out float seatHeight)
        {
            seatHeight = 0f;
            Vector3 feet = seat + forward * 0.5f;
            if (!TryGround(feet, seat.y + 0.1f, 2f, own, out Vector3 ground)) return Reject("no ground in front");
            seatHeight = seat.y - ground.y;
            if (seatHeight < MinBenchSeatHeight || seatHeight > MaxSeatHeight) return Reject("seat height");
            if (!IsWalkable(ground, roadRoot)) return Reject("feet on the road");
            if (LaneDistance(seat, laneGrid, MinLaneDistanceSeat + 1f) < MinLaneDistanceSeat) return Reject("traffic lane close");
            // He has to be able to run: a clear dash to the panic NavMesh, the reach PedestrianWalker allows a held person.
            if (!NavMesh.SamplePosition(ground, out NavMeshHit nav, PedestrianWalker.HoldNavMeshReach, NavMesh.AllAreas)) return Reject("no NavMesh");
            if (LineBlocked(ground + Vector3.up * 0.9f, nav.position + Vector3.up * 0.9f, own)) return Reject("wall before NavMesh");

            Quaternion rotation = Quaternion.LookRotation(forward);
            if (Blocked(Physics.OverlapBox(feet + Vector3.up * (seat.y - ground.y) * 0.5f + Vector3.up * 0.1f, new Vector3(0.3f, (seat.y - ground.y) * 0.4f, 0.28f), rotation, ~(1 << 2), QueryTriggerInteraction.Ignore), own, ground.y)) return Reject("legs blocked");
            if (Blocked(Physics.OverlapCapsule(seat + Vector3.up * 0.35f, seat + Vector3.up * 1.0f, 0.2f, ~(1 << 2), QueryTriggerInteraction.Ignore), own, ground.y)) return Reject("body blocked");
            // Nothing right overhead (inside a building shell).
            if (Physics.Raycast(seat + Vector3.up * 1.1f, Vector3.up, 2.2f, ~(1 << 2), QueryTriggerInteraction.Ignore)) return Reject("roof overhead");
            return true;
        }

        private static bool LineBlocked(Vector3 from, Vector3 to, Transform own)
        {
            foreach (RaycastHit hit in Physics.RaycastAll(from, (to - from).normalized, Vector3.Distance(from, to), ~(1 << 2), QueryTriggerInteraction.Ignore))
                if (own == null || !hit.collider.transform.IsChildOf(own)) return true;
            return false;
        }

        private static readonly Dictionary<string, int> RejectCounts = new Dictionary<string, int>();

        private static bool Reject(string reason)
        {
            RejectCounts.TryGetValue(reason, out int count);
            RejectCounts[reason] = count + 1;
            return false;
        }

        private static bool Blocked(Collider[] hits, Transform own, float groundY)
        {
            foreach (Collider c in hits)
            {
                if (own != null && c.transform.IsChildOf(own)) continue;
                // Ground itself and flat kerbs under the feet do not count.
                if (c.bounds.max.y <= groundY + 0.12f) continue;
                return true;
            }
            return false;
        }

        private static bool TryGround(Vector3 at, float fromY, float depth, Transform ignore, out Vector3 ground)
        {
            ground = at;
            var origin = new Vector3(at.x, fromY, at.z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, depth, ~(1 << 2), QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            foreach (RaycastHit hit in hits)
            {
                if (ignore != null && hit.collider.transform.IsChildOf(ignore)) continue;
                // Benches and bins have tall box colliders: not the ground, and not a ledge to sit on.
                if (hit.collider.GetComponentInParent<BreakableProp>() != null) continue;
                if (hit.distance >= best || hit.normal.y < 0.85f) continue;
                best = hit.distance;
                ground = hit.point;
                found = true;
            }
            return found;
        }

        // Along each footpath lane, looks sideways for a step up to a flat top at sitting height.
        private static int AddLedgeSpots(List<AmbientSpot> spots, FootpathLaneNetwork network, Dictionary<Vector2Int, List<Vector3>> laneGrid, Transform roadRoot, StringBuilder log)
        {
            int count = 0;
            var taken = new HashSet<Vector2Int>();
            foreach (var (point, forward) in LaneSamples(network, 4f))
            {
                if (count >= MaxLedgeSpots) break;
                Vector3 left = new Vector3(-forward.z, 0f, forward.x);
                foreach (float sign in new[] { 1f, -1f })
                {
                    Vector3 dir = left * sign;
                    if (!TryGround(point, point.y + 1f, 2f, null, out Vector3 walk)) continue;
                    if (roadRoot != null && !IsWalkable(walk, roadRoot)) continue;
                    for (float d = 1f; d <= 5f; d += 0.25f)
                    {
                        Vector3 q = walk + dir * d;
                        if (!TryGround(q, walk.y + 1.3f, 2.2f, null, out Vector3 top)) break;
                        float rise = top.y - walk.y;
                        if (rise < MinSeatHeight - 0.2f && rise > -0.25f) continue;      // same level (kerbs, slopes)
                        if (rise < MinSeatHeight || rise > MaxSeatHeight) break;          // a drop, a real wall or too small a step
                        // A flat top a seat deep, with the edge just in front of it.
                        if (!TryGround(q + dir * 0.35f, top.y + 0.5f, 1f, null, out Vector3 back) || Mathf.Abs(back.y - top.y) > 0.04f) break;
                        Vector3 seat = top + dir * 0.18f;
                        Vector3 facing = -dir;
                        var cell = new Vector2Int(Mathf.FloorToInt(seat.x / LedgeSpacing), Mathf.FloorToInt(seat.z / LedgeSpacing));
                        if (taken.Contains(cell)) break;
                        if (!ValidateSeat(seat, facing, null, laneGrid, roadRoot, out float seatHeight)) break;
                        taken.Add(cell);
                        spots.Add(new AmbientSpot { kind = AmbientSpotKind.Ledge, position = seat, yaw = Quaternion.LookRotation(facing).eulerAngles.y, seatHeight = seatHeight });
                        count++;
                        break;
                    }
                }
            }
            log.AppendLine($"  ledges: {count} seats (cap {MaxLedgeSpots})");
            return count;
        }

        private static int AddTalkSpots(List<AmbientSpot> spots, FootpathLaneNetwork network, Dictionary<Vector2Int, List<Vector3>> laneGrid, Transform roadRoot, StringBuilder log)
        {
            int count = 0, rejected = 0;
            var taken = new HashSet<Vector2Int>();
            foreach (var (point, forward) in LaneSamples(network, 7f))
            {
                if (count >= MaxTalkSpots) break;
                Vector3 left = new Vector3(-forward.z, 0f, forward.x);
                foreach (float offset in new[] { 1.6f, -1.6f, 2.6f, -2.6f })
                {
                    Vector3 centre = point + left * offset;
                    var cell = new Vector2Int(Mathf.FloorToInt(centre.x / TalkSpacing), Mathf.FloorToInt(centre.z / TalkSpacing));
                    if (taken.Contains(cell)) break;
                    if (!ValidateTalkSpot(centre, point.y, laneGrid, roadRoot, out Vector3 ground)) { rejected++; continue; }
                    taken.Add(cell);
                    spots.Add(new AmbientSpot { kind = AmbientSpotKind.Talk, position = ground });
                    count++;
                    break;
                }
            }
            log.AppendLine($"  talk: {count} spots, {rejected} candidates rejected");
            return count;
        }

        private static bool ValidateTalkSpot(Vector3 centre, float laneY, Dictionary<Vector2Int, List<Vector3>> laneGrid, Transform roadRoot, out Vector3 ground)
        {
            if (!TryGround(centre, laneY + 1.2f, 2.4f, null, out ground)) return false;
            if (Mathf.Abs(ground.y - laneY) > 0.3f || !IsWalkable(ground, roadRoot)) return false;
            if (LaneDistance(ground, laneGrid, MinLaneDistanceTalk + 1f) < MinLaneDistanceTalk) return false;
            if (!NavMesh.SamplePosition(ground, out NavMeshHit nav, 0.6f, NavMesh.AllAreas) || Mathf.Abs(nav.position.y - ground.y) > 0.3f) return false;
            // Room for the group: nothing standing in a circle around the middle, on level ground.
            for (int i = 0; i < 6; i++)
            {
                Vector3 around = ground + Quaternion.Euler(0f, i * 60f, 0f) * Vector3.forward * 0.9f;
                if (!TryGround(around, ground.y + 0.8f, 1.6f, null, out Vector3 g) || Mathf.Abs(g.y - ground.y) > 0.07f) return false;
            }
            if (Blocked(Physics.OverlapCapsule(ground + Vector3.up * 0.3f, ground + Vector3.up * 1.7f, 1.15f, ~(1 << 2), QueryTriggerInteraction.Ignore), null, ground.y + 0.15f)) return false;
            return !Physics.Raycast(ground + Vector3.up * 2f, Vector3.up, 2f, ~(1 << 2), QueryTriggerInteraction.Ignore);
        }

        private static bool IsWalkable(Vector3 ground, Transform roadRoot)
        {
            if (roadRoot == null) return true;
            return !(Physics.Raycast(ground + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 1f, ~(1 << 2), QueryTriggerInteraction.Ignore) && hit.collider.transform.IsChildOf(roadRoot));
        }

        private static IEnumerable<(Vector3 point, Vector3 forward)> LaneSamples(FootpathLaneNetwork network, float step)
        {
            for (int l = 0; l < network.lanes.Count; l++)
            {
                float length = network.lanes[l].Length;
                for (float d = 0f; d < length; d += step)
                {
                    Vector3 point = network.Evaluate(l, d, out Vector3 forward);
                    forward.y = 0f;
                    if (forward.sqrMagnitude < 1e-4f) continue;
                    yield return (point, forward.normalized);
                }
            }
        }

        // ---------- scene ----------

        public static string WireScene()
        {
            var managerRoot = FindSceneRoot("///Manager///");
            var spawner = Object.FindFirstObjectByType<PedestrianSpawner>(FindObjectsInactive.Include);
            var roads = Object.FindFirstObjectByType<TSMainManager>(FindObjectsInactive.Include);
            if (managerRoot == null || spawner == null || spawner.player == null) return "[AmbientPopulationBuilder] Needs ///Manager/// and a PedestrianSpawner with a player in the open scene.";
            Transform player = spawner.player;

            var settings = LoadOrCreate<AmbientPopulationSettings>(SettingsPath);
            var panic = LoadOrCreate<TrafficPanicSettings>(PanicSettingsPath);
            var spots = AssetDatabase.LoadAssetAtPath<AmbientSpotData>(SpotDataPath);

            var ambient = GetOrCreateManager<AmbientPopulationManager>(managerRoot, "AmbientPopulationManager");
            ambient.EditorSetup(settings, spots, spawner, player);
            EditorUtility.SetDirty(ambient);

            var panicManager = GetOrCreateManager<TrafficPanicManager>(managerRoot, "TrafficPanicManager");
            panicManager.gameObject.name = "TrafficPanicManager";
            panicManager.EditorSetup(panic, player.GetComponent<PlayerCombatController>(), player.GetComponent<vShooterMeleeInput>(), player, spawner, roads);
            EditorUtility.SetDirty(panicManager);

            var impact = player.GetComponent<PlayerVehicleImpact>();
            if (impact == null) impact = player.gameObject.AddComponent<PlayerVehicleImpact>();
            impact.EditorSetup(player.GetComponent<vThirdPersonController>(), player.GetComponent<vRagdoll>(), player.GetComponent<OpenworldCrime.Player.PlayerManager>());
            EditorUtility.SetDirty(impact);

            // Without this nobody hears a gunshot: it was missing from the gameplay scene.
            var threat = player.GetComponent<PedestrianThreatSource>();
            if (threat == null) threat = player.gameObject.AddComponent<PedestrianThreatSource>();
            threat.EditorSetup(player.GetComponent<PlayerCombatController>(), player.GetComponent<vShooterMeleeInput>(), spawner);
            EditorUtility.SetDirty(threat);

            EditorSceneManager.MarkSceneDirty(managerRoot.gameObject.scene);
            AssetDatabase.SaveAssets();
            return $"[AmbientPopulationBuilder] scene wired: AmbientPopulationManager ({(spots != null ? spots.spots.Length : 0)} spots), TrafficPanicManager, PlayerVehicleImpact and PedestrianThreatSource on '{player.name}'.";
        }

        private static T GetOrCreateManager<T>(Transform parent, string objectName) where T : Component
        {
            T existing = parent.GetComponentInChildren<T>(true);
            if (existing != null) return existing;
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            return go.AddComponent<T>();
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static Transform FindSceneRoot(string rootName)
        {
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == rootName) return root.transform;
            return null;
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
