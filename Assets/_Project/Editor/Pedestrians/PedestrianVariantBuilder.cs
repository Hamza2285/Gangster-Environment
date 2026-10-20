using System.Collections.Generic;
using System.IO;
using Invector;
using Invector.vCharacterController;
using OpenworldCrime.Pedestrians;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Builds damageable prefab variants of the PopulationSystem street characters (the source prefabs are never modified) and
    /// points the Population prefab's PedestrianSpawner at them. Each variant gets the enemy hit setup adapted to the generic
    /// Biped rig: kinematic 70 kg root body + capsule on the Enemy layer, Invector vCharacter health and vHitDamageParticle
    /// (copied from the EnemyDummy prefab), and an 11-bone ragdoll on the BodyPart layer (Rigidbody, collider, CharacterJoint,
    /// vDamageReceiver per bone; joint limits as Unity's ragdoll wizard) driven by <see cref="PedestrianRagdoll"/> and
    /// <see cref="PedestrianDamage"/>. Re-running updates the existing variants in place (GUIDs and designer tweaks on other
    /// components are kept; the generated values listed here are re-applied).
    /// </summary>
    public static class PedestrianVariantBuilder
    {
        private const string PopulationPrefabPath = "Assets/Prefabs/Population/Population.prefab";
        private const string VariantFolder = "Assets/Prefabs/Population/Pedestrians";
        private const string EnemyPrefabPath = "Assets/_Project/Prefabs/Characters/EnemyDummy.prefab";
        private const string VariantPrefix = "Ped_";

        private const int MaxHealth = 60;
        private const float RootMass = 70f;
        private const float HeadDamageMultiplier = 2f;

        private class Bone
        {
            public Transform transform;
            public Rigidbody body;
        }

        [MenuItem("Tools/OpenworldCrime/Population/Build Pedestrian Damage Variants")]
        public static void BuildAll()
        {
            GameObject population = PrefabUtility.LoadPrefabContents(PopulationPrefabPath);
            try
            {
                var spawner = population.GetComponentInChildren<PedestrianSpawner>(true);
                if (spawner == null)
                {
                    Debug.LogError($"[PedestrianVariantBuilder] No PedestrianSpawner in '{PopulationPrefabPath}'.");
                    return;
                }
                var enemy = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
                EnsureFolder(VariantFolder);

                var built = new List<GameObject>();
                foreach (GameObject entry in spawner.peoplePrefabs)
                {
                    if (entry == null) continue;
                    GameObject source = entry.GetComponent<PedestrianDamage>() != null
                        ? PrefabUtility.GetCorrespondingObjectFromSource(entry)
                        : entry;
                    GameObject variant = BuildVariant(source, enemy);
                    built.Add(variant != null ? variant : entry);
                }

                spawner.peoplePrefabs = built.ToArray();
                EditorUtility.SetDirty(spawner);
                PrefabUtility.SaveAsPrefabAsset(population, PopulationPrefabPath);
                Debug.Log($"[PedestrianVariantBuilder] {built.Count} pedestrian variants built in '{VariantFolder}' and assigned to the PedestrianSpawner.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(population);
            }
            AssetDatabase.SaveAssets();
        }

        private static GameObject BuildVariant(GameObject source, GameObject enemy)
        {
            string path = $"{VariantFolder}/{VariantPrefix}{source.name}.prefab";
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
            var previewScene = EditorSceneManager.NewPreviewScene();
            GameObject root = exists
                ? PrefabUtility.LoadPrefabContents(path)
                : (GameObject)PrefabUtility.InstantiatePrefab(source, previewScene);
            try
            {
                if (!Configure(root, enemy)) return null;
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        private static bool Configure(GameObject root, GameObject enemy)
        {
            Transform t = root.transform;
            var animator = root.GetComponent<Animator>();
            Transform pelvis = Find(t, "Pelvis"), spine = Find(t, "Spine1"), neck = Find(t, "Neck"), head = Find(t, "Head"), headNub = Find(t, "HeadNub");
            Transform lThigh = Find(t, "L Thigh"), lCalf = Find(t, "L Calf"), lFoot = Find(t, "L Foot");
            Transform rThigh = Find(t, "R Thigh"), rCalf = Find(t, "R Calf"), rFoot = Find(t, "R Foot");
            Transform lArm = Find(t, "L UpperArm"), lFore = Find(t, "L Forearm"), lHand = Find(t, "L Hand");
            Transform rArm = Find(t, "R UpperArm"), rFore = Find(t, "R Forearm"), rHand = Find(t, "R Hand");
            Transform[] required = { pelvis, spine, neck, head, lThigh, lCalf, lFoot, rThigh, rCalf, rFoot, lArm, lFore, lHand, rArm, rFore, rHand };
            if (animator == null || System.Array.IndexOf(required, null) >= 0)
            {
                Debug.LogError($"[PedestrianVariantBuilder] '{root.name}' is missing its Animator or a Biped bone (Bip01 Pelvis/Spine1/Neck/Head/Thigh/Calf/Foot/UpperArm/Forearm/Hand). Skipped.", root);
                return false;
            }

            int enemyLayer = LayerMask.NameToLayer("Enemy");
            int bodyPartLayer = LayerMask.NameToLayer("BodyPart");
            root.layer = enemyLayer;

            // Root body: kinematic, moved by the walker; blocks the player and is what cars hit (VehicleCharacterImpact).
            var capsule = GetOrAdd<CapsuleCollider>(root);
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.radius = 0.3f;
            capsule.height = 1.8f;
            capsule.isTrigger = false;
            var rootBody = GetOrAdd<Rigidbody>(root);
            rootBody.mass = RootMass;
            rootBody.isKinematic = true;
            rootBody.useGravity = false;
            rootBody.interpolation = RigidbodyInterpolation.None;

            Vector3 right = t.right, up = t.up, forward = t.forward;
            float scale = 1f / Mathf.Max(0.001f, t.lossyScale.y);

            // Ragdoll bodies (masses add up to ~70 kg). Joint axes/limits follow Unity's ragdoll wizard.
            var hips = AddBody(pelvis, bodyPartLayer, 13f);
            var chest = AddBody(spine, bodyPartLayer, 18f);
            var headB = AddBody(head, bodyPartLayer, 5.5f);
            var lThighB = AddBody(lThigh, bodyPartLayer, 7f);
            var lCalfB = AddBody(lCalf, bodyPartLayer, 4f);
            var rThighB = AddBody(rThigh, bodyPartLayer, 7f);
            var rCalfB = AddBody(rCalf, bodyPartLayer, 4f);
            var lArmB = AddBody(lArm, bodyPartLayer, 2.5f);
            var lForeB = AddBody(lFore, bodyPartLayer, 1.5f);
            var rArmB = AddBody(rArm, bodyPartLayer, 2.5f);
            var rForeB = AddBody(rFore, bodyPartLayer, 1.5f);

            PelvisBox(pelvis, lThigh, rThigh, spine, right, up);
            ChestBox(spine, neck, lArm, rArm, right);
            HeadSphere(head, headNub, neck, scale);
            LimbCapsule(lThigh, lCalf, 0.3f, 1f);
            LimbCapsule(rThigh, rCalf, 0.3f, 1f);
            LimbCapsule(lCalf, lFoot, 0.25f, 1f);
            LimbCapsule(rCalf, rFoot, 0.25f, 1f);
            LimbCapsule(lArm, lFore, 0.25f, 1f);
            LimbCapsule(rArm, rFore, 0.25f, 1f);
            LimbCapsule(lFore, lHand, 0.2f, 1.25f);
            LimbCapsule(rFore, rHand, 0.2f, 1.25f);

            Joint(lThighB, hips, right, forward, -20f, 70f, 30f);
            Joint(rThighB, hips, right, forward, -20f, 70f, 30f);
            Joint(lCalfB, lThighB, right, forward, -80f, 0f, 0f);
            Joint(rCalfB, rThighB, right, forward, -80f, 0f, 0f);
            Joint(chest, hips, right, forward, -20f, 20f, 10f);
            Joint(headB, chest, right, forward, -40f, 25f, 25f);
            Joint(lArmB, chest, up, forward, -70f, 10f, 50f);
            Joint(rArmB, chest, up, forward, -70f, 10f, 50f);
            Joint(lForeB, lArmB, forward, up, -90f, 0f, 0f);
            Joint(rForeB, rArmB, forward, up, -90f, 0f, 0f);

            // Invector health + per-bone damage receivers (same as the enemies).
            var character = GetOrAdd<vCharacter>(root);
            var so = new SerializedObject(character);
            so.FindProperty("_maxHealth").intValue = MaxHealth;
            so.FindProperty("_currentHealth").floatValue = MaxHealth;
            so.FindProperty("_isDead").boolValue = false;
            so.FindProperty("fillHealthOnStart").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            Rigidbody[] bodies = { hips.body, chest.body, headB.body, lThighB.body, lCalfB.body, rThighB.body, rCalfB.body, lArmB.body, lForeB.body, rArmB.body, rForeB.body };
            foreach (Rigidbody b in bodies)
            {
                var receiver = GetOrAdd<vDamageReceiver>(b.gameObject);
                receiver.damageMultiplier = b == headB.body ? HeadDamageMultiplier : 1f;
            }

            var particles = GetOrAdd<vHitDamageParticle>(root);
            var enemyParticles = enemy != null ? enemy.GetComponent<vHitDamageParticle>() : null;
            if (enemyParticles != null) EditorUtility.CopySerialized(enemyParticles, particles);

            var ragdoll = GetOrAdd<PedestrianRagdoll>(root);
            var rso = new SerializedObject(ragdoll);
            rso.FindProperty("animator").objectReferenceValue = animator;
            rso.FindProperty("hipsBody").objectReferenceValue = hips.body;
            SerializedProperty list = rso.FindProperty("bodies");
            list.arraySize = bodies.Length;
            for (int i = 0; i < bodies.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = bodies[i];
            rso.FindProperty("groundLayers").intValue = 1 << 0;
            rso.ApplyModifiedPropertiesWithoutUndo();

            var damage = GetOrAdd<PedestrianDamage>(root);
            var dso = new SerializedObject(damage);
            dso.FindProperty("character").objectReferenceValue = character;
            dso.FindProperty("ragdoll").objectReferenceValue = ragdoll;
            dso.FindProperty("aliveCollider").objectReferenceValue = capsule;
            dso.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static Transform Find(Transform root, string boneName)
        {
            foreach (Transform c in root.GetComponentsInChildren<Transform>(true))
            {
                if (c.name == boneName || c.name.EndsWith(" " + boneName)) return c;
            }
            return null;
        }

        private static Bone AddBody(Transform bone, int layer, float mass)
        {
            bone.gameObject.layer = layer;
            var body = GetOrAdd<Rigidbody>(bone.gameObject);
            body.mass = mass;
            body.isKinematic = true;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.linearDamping = 0f;
            body.angularDamping = 0.05f;
            return new Bone { transform = bone, body = body };
        }

        // Capsule from the bone to its child bone; radius as a share of the length (Unity's ragdoll wizard scales).
        private static void LimbCapsule(Transform bone, Transform child, float radiusScale, float lengthScale)
        {
            RemoveOtherColliders<CapsuleCollider>(bone.gameObject);
            var c = GetOrAdd<CapsuleCollider>(bone.gameObject);
            Vector3 local = bone.InverseTransformPoint(child.position);
            int axis = LargestAxis(local);
            float length = Mathf.Abs(local[axis]) * lengthScale;
            c.direction = axis;
            c.height = length;
            c.radius = Mathf.Abs(local[axis]) * radiusScale;
            Vector3 center = Vector3.zero;
            center[axis] = local[axis] * 0.5f * lengthScale;
            c.center = center;
            c.isTrigger = true;
        }

        private static void PelvisBox(Transform pelvis, Transform lThigh, Transform rThigh, Transform spine, Vector3 right, Vector3 up)
        {
            RemoveOtherColliders<BoxCollider>(pelvis.gameObject);
            var box = GetOrAdd<BoxCollider>(pelvis.gameObject);
            Vector3 l = pelvis.InverseTransformPoint(lThigh.position), r = pelvis.InverseTransformPoint(rThigh.position);
            Vector3 s = pelvis.InverseTransformPoint(spine.position);
            int wAxis = LargestAxis(pelvis.InverseTransformDirection(right));
            int uAxis = LargestAxis(pelvis.InverseTransformDirection(up));
            int dAxis = 3 - wAxis - uAxis;
            float width = Mathf.Abs((l - r)[wAxis]) * 1.9f;
            float height = Mathf.Max(0.12f, Mathf.Abs(s[uAxis] - ((l + r) * 0.5f)[uAxis]) * 1.4f);
            Vector3 size = Vector3.zero;
            size[wAxis] = width;
            size[uAxis] = height;
            size[dAxis] = width * 0.75f;
            box.size = size;
            box.center = ((l + r) * 0.5f + s) * 0.5f;
            box.isTrigger = true;
        }

        private static void ChestBox(Transform spine, Transform neck, Transform lArm, Transform rArm, Vector3 right)
        {
            RemoveOtherColliders<BoxCollider>(spine.gameObject);
            var box = GetOrAdd<BoxCollider>(spine.gameObject);
            Vector3 n = spine.InverseTransformPoint(neck.position);
            Vector3 l = spine.InverseTransformPoint(lArm.position), r = spine.InverseTransformPoint(rArm.position);
            int uAxis = LargestAxis(n);
            int wAxis = LargestAxis(spine.InverseTransformDirection(right));
            if (wAxis == uAxis) wAxis = (uAxis + 1) % 3;
            int dAxis = 3 - wAxis - uAxis;
            float width = Mathf.Abs((l - r)[wAxis]);
            Vector3 size = Vector3.zero;
            size[uAxis] = Mathf.Abs(n[uAxis]) * 1.1f;
            size[wAxis] = width;
            size[dAxis] = width * 0.6f;
            box.size = size;
            box.center = n * 0.5f;
            box.isTrigger = true;
        }

        private static void HeadSphere(Transform head, Transform headNub, Transform neck, float scale)
        {
            RemoveOtherColliders<SphereCollider>(head.gameObject);
            var sphere = GetOrAdd<SphereCollider>(head.gameObject);
            Vector3 top = headNub != null
                ? head.InverseTransformPoint(headNub.position)
                : head.InverseTransformDirection(head.position - neck.position).normalized * 0.22f * scale;
            sphere.radius = Mathf.Max(0.09f * scale, top.magnitude * 0.5f);
            sphere.center = top * 0.5f;
            sphere.isTrigger = true;
        }

        private static void Joint(Bone bone, Bone parent, Vector3 twistAxis, Vector3 swingAxis, float lowTwist, float highTwist, float swing)
        {
            var joint = GetOrAdd<CharacterJoint>(bone.transform.gameObject);
            joint.connectedBody = parent.body;
            joint.autoConfigureConnectedAnchor = true;
            joint.anchor = Vector3.zero;
            joint.axis = SnapAxis(bone.transform.InverseTransformDirection(twistAxis));
            joint.swingAxis = SnapAxis(bone.transform.InverseTransformDirection(swingAxis));
            joint.lowTwistLimit = new SoftJointLimit { limit = lowTwist };
            joint.highTwistLimit = new SoftJointLimit { limit = highTwist };
            joint.swing1Limit = new SoftJointLimit { limit = swing };
            joint.swing2Limit = new SoftJointLimit { limit = swing };
            joint.enableProjection = true;
            joint.enablePreprocessing = false;
        }

        private static int LargestAxis(Vector3 v)
        {
            Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            return a.x >= a.y && a.x >= a.z ? 0 : a.y >= a.z ? 1 : 2;
        }

        private static Vector3 SnapAxis(Vector3 v)
        {
            int axis = LargestAxis(v);
            Vector3 r = Vector3.zero;
            r[axis] = Mathf.Sign(v[axis]);
            return r;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            return go.TryGetComponent(out T c) ? c : go.AddComponent<T>();
        }

        // A bone keeps exactly one collider of the generated type (re-runs never stack colliders).
        private static void RemoveOtherColliders<T>(GameObject go) where T : Collider
        {
            foreach (Collider c in go.GetComponents<Collider>())
            {
                if (!(c is T)) Object.DestroyImmediate(c, true);
            }
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
