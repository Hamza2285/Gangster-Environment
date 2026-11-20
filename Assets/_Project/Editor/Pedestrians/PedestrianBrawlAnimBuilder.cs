using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Gives the street people (Generic "Bip01" rigs) hit reactions and fist-fight moves made from Invector's Humanoid melee
    /// clips. A Humanoid avatar is built in memory for one man / one woman model of the crowd from the bone mapping of a
    /// Humanoid model with the same skeleton names (the people's FBX import settings are not changed), each clip is sampled
    /// on it and stored as an ordinary Generic clip (bone rotations; the body's travel removed: the game moves the person),
    /// and the states are added to our people controllers (PedMan_Ambient / PedGirl_Ambient).
    /// Menu: Tools > OpenworldCrime > Population > Build Brawl Animations.
    /// </summary>
    public static class PedestrianBrawlAnimBuilder
    {
        private const string ClipFolder = "Assets/_Project/Animations/Pedestrians/Brawl";
        private const string ManController = "Assets/_Project/Animations/Pedestrians/PedMan_Ambient.controller";
        private const string GirlController = "Assets/_Project/Animations/Pedestrians/PedGirl_Ambient.controller";
        private const string ManModel = "Assets/PopulationSystem/Models/man/Man_11.FBX";
        private const string GirlModel = "Assets/PopulationSystem/Models/girl/Girl_44.FBX";
        private const string MappingModel = "Assets/Player/Models/Female/casual07_f/casual07_f.FBX"; // Humanoid, same Bip01 names
        private const string MeleeSet = "Assets/Invector-3rdPersonController/Melee Combat/3DModels/Animations/Melee_CombatSet.fbx";
        private const string MotoHit = "Assets/_Project/Animations/Combat/Moto/AS_Get_Hit_";
        private const string ActionsSet = "Assets/Invector-3rdPersonController/Basic Locomotion/3D Models/Animations/Basic_Actions.fbx";
        // Ragdoll get-up states (PedestrianRagdoll plays one by how the body lies) and the seconds they take at their speed.
        private const string GetUpBackState = "getup_back";
        private const string GetUpBellyState = "getup_belly";
        private const float GetUpSeconds = 1.55f;

        private struct Job
        {
            public string State;
            public string Clip;
            public bool Loop;
            public float HoldAt;   // >= 0: a held pose from this normalized time (guard / block stance)
            public float Speed;
        }

        // Everyone: hit reactions. Men also fight.
        private static readonly Job[] HitJobs =
        {
            new Job { State = "hit_front", Clip = "Hit Small Front", HoldAt = -1f, Speed = 1.3f },
            new Job { State = "hit_left", Clip = "Hit Small Left", HoldAt = -1f, Speed = 1.3f },
            new Job { State = "hit_right", Clip = "Hit Small Right", HoldAt = -1f, Speed = 1.3f },
            new Job { State = "hit_back", Clip = "Hit Small Back", HoldAt = -1f, Speed = 1.3f },
            new Job { State = "hit_big", Clip = "Hit Big Front", HoldAt = -1f, Speed = 1.15f },
            // Heavy blows from the side / behind (Invector Hit Big) and a second, head-snapping variant per side (Moto Get_Hit),
            // so a beating is directional and never loops one clip (PedestrianBrawler picks, never the same twice in a row).
            new Job { State = "hit_big_left", Clip = "Hit Big Left", HoldAt = -1f, Speed = 1.15f },
            new Job { State = "hit_big_right", Clip = "Hit Big Right", HoldAt = -1f, Speed = 1.15f },
            new Job { State = "hit_big_back", Clip = "Hit Big Back", HoldAt = -1f, Speed = 1.15f },
            new Job { State = "hit_front_b", Clip = "AS_Get_Hit_Front", HoldAt = -1f, Speed = 1.1f },
            new Job { State = "hit_left_b", Clip = "AS_Get_Hit_Left", HoldAt = -1f, Speed = 1.1f },
            new Job { State = "hit_right_b", Clip = "AS_Get_Hit_Right", HoldAt = -1f, Speed = 1.1f },
            new Job { State = "hit_back_b", Clip = "AS_Get_Hit_Back", HoldAt = -1f, Speed = 1.1f },
        };

        private static readonly Job[] FightJobs =
        {
            new Job { State = "brawl_guard", Clip = "Defense_Unarmed", Loop = true, HoldAt = 0.45f, Speed = 1f },
            new Job { State = "brawl_block", Clip = "Defense_Unarmed", HoldAt = -1f, Speed = 1.4f },
            new Job { State = "brawl_jab", Clip = "WeakAttack_UnarmedA", HoldAt = -1f, Speed = 1f },
            new Job { State = "brawl_cross", Clip = "WeakAttack_UnarmedB", HoldAt = -1f, Speed = 1f },
            new Job { State = "brawl_hook", Clip = "StrongAttack_PunchA", HoldAt = -1f, Speed = 1.1f },
        };

        // Everyone: getting up off the ground after a knock-down (instead of the ragdoll pose snapping straight into the walk).
        private static readonly Job[] GetUpJobs =
        {
            new Job { State = GetUpBackState, Clip = "GetUpFromBack", HoldAt = -1f, Speed = 1.25f },
            new Job { State = GetUpBellyState, Clip = "GetUpFromBelly", HoldAt = -1f, Speed = 1.1f },
        };

        [MenuItem("Tools/OpenworldCrime/Population/Build Brawl Animations")]
        public static void BuildMenu() => Debug.Log(Build());

        [MenuItem("Tools/OpenworldCrime/Population/Configure Punch Reactions On People Prefabs")]
        public static void ConfigureMenu() => Debug.Log(ConfigurePrefabs());

        [MenuItem("Tools/OpenworldCrime/Population/Configure Police Punch Reactions")]
        public static void PoliceMenu() => Debug.Log(ConfigurePolice());

        private static readonly string[] PolicePrefabs =
        {
            "Assets/_Project/Prefabs/Pedestrians/Ped_Police_2.prefab", "Assets/_Project/Prefabs/Pedestrians/Ped_Police_3.prefab",
            "Assets/_Project/Prefabs/Police/PoliceOfficer_2.prefab", "Assets/_Project/Prefabs/Police/PoliceOfficer_3.prefab",
        };

        /// <summary>
        /// The police (humanoid, controllers without hit states) react to punches with <see cref="OpenworldCrime.Pedestrians.HumanoidHitReaction"/>:
        /// per side Invector's Hit Small and Moto's Get_Hit head snap, played over their controller.
        /// </summary>
        public static string ConfigurePolice()
        {
            var log = new StringBuilder("[PedestrianBrawlAnimBuilder] police").AppendLine();
            var sources = new Dictionary<string, AnimationClip>();
            foreach (string set in new[] { MeleeSet, MotoHit + "Front.fbx", MotoHit + "Back.fbx", MotoHit + "Left.fbx", MotoHit + "Right.fbx" })
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(set))
                    if (o is AnimationClip c && !c.name.StartsWith("__")) sources[c.name] = c;
            foreach (string path in PolicePrefabs)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var damage = root.GetComponent<OpenworldCrime.Pedestrians.PedestrianDamage>();
                    var animator = root.GetComponentInChildren<Animator>(true);
                    if (damage == null || animator == null) { log.AppendLine("  skipped " + path); continue; }
                    var reaction = root.GetComponent<OpenworldCrime.Pedestrians.HumanoidHitReaction>();
                    if (reaction == null) reaction = root.AddComponent<OpenworldCrime.Pedestrians.HumanoidHitReaction>();
                    var so = new SerializedObject(reaction);
                    so.FindProperty("damage").objectReferenceValue = damage;
                    so.FindProperty("animator").objectReferenceValue = animator;
                    SetClips(so.FindProperty("front"), Pick(sources, "Hit Small Front", "AS_Get_Hit_Front"));
                    SetClips(so.FindProperty("left"), Pick(sources, "Hit Small Left", "AS_Get_Hit_Left"));
                    SetClips(so.FindProperty("right"), Pick(sources, "Hit Small Right", "AS_Get_Hit_Right"));
                    SetClips(so.FindProperty("back"), Pick(sources, "Hit Small Back", "AS_Get_Hit_Back"));
                    so.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    log.AppendLine("  " + root.name + ": HumanoidHitReaction");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return log.ToString();
        }

        private static AnimationClip[] Pick(Dictionary<string, AnimationClip> sources, params string[] names)
        {
            var list = new List<AnimationClip>();
            foreach (string n in names) if (sources.TryGetValue(n, out AnimationClip c)) list.Add(c);
            return list.ToArray();
        }

        private static void SetClips(SerializedProperty array, AnimationClip[] clips)
        {
            array.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        }

        private const string PrefabFolder = "Assets/_Project/Prefabs/Pedestrians";
        private const string AudioFolder = "Assets/Invector-3rdPersonController/Melee Combat/Audio/";
        private const float MenFightBackChance = 0.3f;

        /// <summary>
        /// Our people prefab variants: men fight back now and then, women / police never; three punches knock down; hit,
        /// block and swing sounds from Invector's melee audio; feet placed on the world (not on people or vehicles).
        /// </summary>
        public static string ConfigurePrefabs()
        {
            var log = new StringBuilder("[PedestrianBrawlAnimBuilder] prefabs\n");
            AudioClip[] hits = Clips("PunchA.wav", "PunchB.wav", "Hit 1.wav", "Hit 2.wav", "Hit 3.wav");
            AudioClip[] blocks = Clips("Hit 1.wav", "Hit 2.wav");
            AudioClip[] swings = Clips("Hit 3.wav");
            int ground = ~((1 << 2) | (1 << 5) | (1 << 8) | (1 << 9) | (1 << 10) | (1 << 11) | (1 << 15) | (1 << 19) | (1 << 20) | (1 << 21));
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var bridge = root.GetComponent<OpenworldCrime.Pedestrians.PedestrianDamageBridge>();
                    var damage = root.GetComponent<OpenworldCrime.Pedestrians.PedestrianDamage>();
                    if (bridge == null || damage == null) continue;
                    bool man = root.name.StartsWith("Ped_Man");
                    var so = new SerializedObject(bridge);
                    SerializedProperty brawl = so.FindProperty("brawl");
                    // Street police always fight back (set up by Game/Police Pedestrians Fight Back): leave them as they are.
                    if (!root.name.StartsWith("Ped_Police"))
                        brawl.FindPropertyRelative("fightBackChance").floatValue = man ? MenFightBackChance : 0f;
                    brawl.FindPropertyRelative("groundLayers").intValue = ground;
                    SetClips(brawl.FindPropertyRelative("hitSounds"), hits);
                    SetClips(brawl.FindPropertyRelative("blockSounds"), blocks);
                    SetClips(brawl.FindPropertyRelative("swingSounds"), swings);
                    so.ApplyModifiedPropertiesWithoutUndo();
                    var dso = new SerializedObject(damage);
                    dso.FindProperty("meleeHitsToKnockDown").intValue = 3;
                    dso.ApplyModifiedPropertiesWithoutUndo();
                    // Generic street rigs get up with the baked clips (humanoid rigs keep their own states).
                    var ragdoll = root.GetComponent<OpenworldCrime.Pedestrians.PedestrianRagdoll>();
                    var animator = root.GetComponentInChildren<Animator>(true);
                    bool hasClips = animator != null && HasState(animator.runtimeAnimatorController, GetUpBackState);
                    if (ragdoll != null && animator != null && (animator.avatar == null || !animator.avatar.isHuman))
                    {
                        // Only rigs whose controller has the baked states (the street police variants use their own controller).
                        var rso = new SerializedObject(ragdoll);
                        rso.FindProperty("getUpFromBackState").stringValue = hasClips ? GetUpBackState : "";
                        rso.FindProperty("getUpFromBellyState").stringValue = hasClips ? GetUpBellyState : "";
                        rso.FindProperty("getUpClipSeconds").floatValue = GetUpSeconds;
                        rso.FindProperty("getUpBlendTime").floatValue = 0.4f;
                        rso.ApplyModifiedPropertiesWithoutUndo();
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    log.AppendLine($"  {root.name}: fight back {(man ? MenFightBackChance : 0f):P0}{(hasClips ? ", get-up clips" : "")}");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return log.ToString();
        }

        private static bool HasState(RuntimeAnimatorController controller, string state)
        {
            if (!(controller is AnimatorController ac) || ac.layers.Length == 0) return false;
            foreach (ChildAnimatorState s in ac.layers[0].stateMachine.states) if (s.state.name == state) return true;
            return false;
        }

        private static AudioClip[] Clips(params string[] names)
        {
            var list = new List<AudioClip>();
            foreach (string n in names)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioFolder + n);
                if (clip != null) list.Add(clip);
            }
            return list.ToArray();
        }

        private static void SetClips(SerializedProperty array, AudioClip[] clips)
        {
            array.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        }

        public static string Build()
        {
            var log = new StringBuilder("[PedestrianBrawlAnimBuilder]\n");
            EnsureFolder(ClipFolder);
            var mapping = AssetDatabase.LoadAssetAtPath<GameObject>(MappingModel).GetComponent<Animator>().avatar;
            var sources = new Dictionary<string, AnimationClip>();
            foreach (string set in new[] { MeleeSet, ActionsSet, MotoHit + "Front.fbx", MotoHit + "Back.fbx", MotoHit + "Left.fbx", MotoHit + "Right.fbx" })
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(set))
                    if (o is AnimationClip c && !c.name.StartsWith("__")) sources[c.name] = c;

            var manJobs = new List<Job>(HitJobs);
            manJobs.AddRange(FightJobs);
            manJobs.AddRange(GetUpJobs);
            var girlJobs = new List<Job>(HitJobs);
            girlJobs.AddRange(GetUpJobs);
            BuildFamily("Man", ManModel, ManController, manJobs, mapping, sources, log);
            BuildFamily("Girl", GirlModel, GirlController, girlJobs, mapping, sources, log);
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        private static void BuildFamily(string family, string modelPath, string controllerPath, List<Job> jobs, Avatar mapping,
            Dictionary<string, AnimationClip> sources, StringBuilder log)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (model == null || controller == null) { log.AppendLine("  missing model or controller for " + family); return; }

            GameObject go = Object.Instantiate(model);
            go.name = model.name;
            go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Avatar avatar = BuildAvatar(go, mapping);
                if (avatar == null || !avatar.isHuman) { log.AppendLine("  could not build a humanoid avatar for " + family); return; }
                var animator = go.GetComponent<Animator>();
                if (animator == null) animator = go.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;

                Transform root = go.transform;
                Transform bip = root.Find("Bip01");
                Transform pelvis = bip != null ? bip.Find("Bip01 Pelvis") : null;
                var bones = new List<Transform>(bip.GetComponentsInChildren<Transform>(true));
                // Rest pose (to put back between clips).
                var restRot = new Quaternion[bones.Count];
                var restPos = new Vector3[bones.Count];
                for (int i = 0; i < bones.Count; i++) { restRot[i] = bones[i].localRotation; restPos[i] = bones[i].localPosition; }

                AnimatorStateMachine sm = controller.layers[0].stateMachine;
                foreach (Job job in jobs)
                {
                    if (!sources.TryGetValue(job.Clip, out AnimationClip source)) { log.AppendLine("  missing source clip " + job.Clip); continue; }
                    for (int i = 0; i < bones.Count; i++) { bones[i].localRotation = restRot[i]; bones[i].localPosition = restPos[i]; }
                    AnimationClip baked = Bake(go, root, bip, pelvis, bones, source, job);
                    string path = $"{ClipFolder}/Ped{family}_{job.State}.anim";
                    baked.name = Path.GetFileNameWithoutExtension(path);
                    var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (existing != null) { EditorUtility.CopySerialized(baked, existing); Object.DestroyImmediate(baked); baked = existing; }
                    else AssetDatabase.CreateAsset(baked, path);

                    AnimatorState state = null;
                    foreach (ChildAnimatorState s in sm.states) if (s.state.name == job.State) state = s.state;
                    if (state == null) state = sm.AddState(job.State, new Vector3(650f, 40f * sm.states.Length, 0f));
                    state.motion = baked;
                    state.speed = job.Speed;
                    state.writeDefaultValues = true;
                    log.AppendLine($"  {family} {job.State} <- {job.Clip} ({baked.length:F2} s)");
                }
                EditorUtility.SetDirty(controller);
                Object.DestroyImmediate(avatar);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // Humanoid avatar for this skeleton using the bone-name mapping of the reference model.
        private static Avatar BuildAvatar(GameObject go, Avatar mapping)
        {
            HumanDescription reference = mapping.humanDescription;
            var names = new HashSet<string>();
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) names.Add(t.name);
            var human = new List<HumanBone>();
            foreach (HumanBone hb in reference.human) if (names.Contains(hb.boneName)) human.Add(hb);
            // The street rigs rest with their arms ~38 degrees down. Built from that pose, the avatar reads it as the T-pose and
            // every retargeted clip carried the arms that far off (swung behind the back while getting up, stiff once standing).
            // So the avatar is built from a real T-pose; the rig's own pose is put back afterwards.
            Transform[] all = go.GetComponentsInChildren<Transform>(true);
            var savedRotations = new Quaternion[all.Length];
            for (int i = 0; i < all.Length; i++) savedRotations[i] = all[i].localRotation;
            ForceTPose(go.transform, all);
            var skeleton = new List<SkeletonBone>();
            foreach (Transform t in all)
                skeleton.Add(new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
            var description = reference;
            description.human = human.ToArray();
            description.skeleton = skeleton.ToArray();
            Avatar avatar = AvatarBuilder.BuildHumanAvatar(go, description);
            for (int i = 0; i < all.Length; i++) all[i].localRotation = savedRotations[i];
            avatar.name = go.name + " Brawl Avatar";
            return avatar;
        }

        // Arms straight out to the sides, legs straight down (in the character's frame), as Unity's humanoid importer expects.
        private static void ForceTPose(Transform root, Transform[] all)
        {
            var bones = new Dictionary<string, Transform>();
            foreach (Transform t in all) if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            void Aim(string from, string to, Vector3 direction)
            {
                if (!bones.TryGetValue(from, out Transform a) || !bones.TryGetValue(to, out Transform b)) return;
                Vector3 current = b.position - a.position;
                if (current.sqrMagnitude > 1e-8f) a.rotation = Quaternion.FromToRotation(current, direction) * a.rotation;
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

        private static AnimationClip Bake(GameObject go, Transform root, Transform bip, Transform pelvis, List<Transform> bones, AnimationClip source, Job job)
        {
            const float fps = 30f;
            float length = job.HoldAt >= 0f ? 1f : source.length;
            int frames = Mathf.Max(2, Mathf.RoundToInt(length * fps) + 1);
            var rotCurves = new AnimationCurve[bones.Count, 4];
            for (int b = 0; b < bones.Count; b++) for (int k = 0; k < 4; k++) rotCurves[b, k] = new AnimationCurve();
            var bipPos = new AnimationCurve[3];
            var pelvisPos = new AnimationCurve[3];
            for (int k = 0; k < 3; k++) { bipPos[k] = new AnimationCurve(); pelvisPos[k] = new AnimationCurve(); }

            Vector3 startHips = Vector3.zero;
            for (int f = 0; f < frames; f++)
            {
                float time = f / fps;
                float sampleTime = job.HoldAt >= 0f ? job.HoldAt * source.length : Mathf.Min(time, source.length);
                source.SampleAnimation(go, sampleTime);
                // Keep the body over the root: remove the clip's horizontal travel (the game moves the person).
                Vector3 hips = root.InverseTransformPoint(pelvis.position);
                if (f == 0) startHips = hips;
                Vector3 travel = new Vector3(hips.x - startHips.x, 0f, hips.z - startHips.z);
                if (job.HoldAt < 0f) bip.position -= root.TransformVector(travel);
                // Guard stance: a light boxer's bounce.
                if (job.HoldAt >= 0f) pelvis.position += root.up * (Mathf.Sin(time * Mathf.PI * 2f * 2f) * 0.012f);

                for (int b = 0; b < bones.Count; b++)
                {
                    Quaternion q = bones[b].localRotation;
                    rotCurves[b, 0].AddKey(time, q.x); rotCurves[b, 1].AddKey(time, q.y);
                    rotCurves[b, 2].AddKey(time, q.z); rotCurves[b, 3].AddKey(time, q.w);
                }
                Vector3 bp = bip.localPosition, pp = pelvis.localPosition;
                bipPos[0].AddKey(time, bp.x); bipPos[1].AddKey(time, bp.y); bipPos[2].AddKey(time, bp.z);
                pelvisPos[0].AddKey(time, pp.x); pelvisPos[1].AddKey(time, pp.y); pelvisPos[2].AddKey(time, pp.z);
            }

            var clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(job.State), frameRate = fps };
            string[] rot = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
            string[] pos = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };
            for (int b = 0; b < bones.Count; b++)
            {
                string path = AnimationUtility.CalculateTransformPath(bones[b], root);
                for (int k = 0; k < 4; k++) clip.SetCurve(path, typeof(Transform), rot[k], rotCurves[b, k]);
            }
            string bipPath = AnimationUtility.CalculateTransformPath(bip, root);
            string pelvisPath = AnimationUtility.CalculateTransformPath(pelvis, root);
            for (int k = 0; k < 3; k++)
            {
                clip.SetCurve(bipPath, typeof(Transform), pos[k], bipPos[k]);
                clip.SetCurve(pelvisPath, typeof(Transform), pos[k], pelvisPos[k]);
            }
            clip.EnsureQuaternionContinuity();
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = job.Loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace(Path.DirectorySeparatorChar, '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
