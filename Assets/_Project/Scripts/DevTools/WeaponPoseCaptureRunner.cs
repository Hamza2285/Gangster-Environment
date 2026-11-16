#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Invector.vCharacterController;
using Invector.vShooter;
using OpenworldCrime.Player;
using OpenworldCrime.Weapons;
using UnityEngine;

namespace OpenworldCrime.DevTools
{
    /// <summary>
    /// Editor-only QA helper: equips every catalog weapon on the player and captures hand / pose screenshots in each state
    /// (idle drawn, aiming, firing, crouch aim, walking drawn) from fixed angles, plus a hand-placement report.
    /// Started from play mode by a menu / editor call; not part of any scene or build.
    /// </summary>
    public class WeaponPoseCaptureRunner : MonoBehaviour
    {
        public string outputFolder = "Captures/GunShop/hands_v2";
        public List<string> weaponIds = new List<string>();
        public bool captureExtraStates = true;
        public bool IsDone { get; private set; }
        public string Report => report.ToString();

        private readonly System.Text.StringBuilder report = new System.Text.StringBuilder();
        private PlayerWeaponController weapons;
        private PlayerArsenal arsenal;
        private vShooterManager shooter;
        private vShooterMeleeInput input;
        private vThirdPersonController controller;
        private Animator animator;
        private bool forceWalk;

        public static WeaponPoseCaptureRunner Begin(GameObject player, IEnumerable<string> ids, bool extraStates)
        {
            var runner = new GameObject("WeaponPoseCaptureRunner").AddComponent<WeaponPoseCaptureRunner>();
            runner.weaponIds.AddRange(ids);
            runner.captureExtraStates = extraStates;
            runner.weapons = player.GetComponent<PlayerWeaponController>();
            runner.arsenal = player.GetComponent<PlayerArsenal>();
            runner.shooter = player.GetComponent<vShooterManager>();
            runner.input = player.GetComponent<vShooterMeleeInput>();
            runner.controller = player.GetComponent<vThirdPersonController>();
            runner.animator = player.GetComponent<Animator>();
            runner.StartCoroutine(runner.Run());
            return runner;
        }

        private void LateUpdate()
        {
            if (!forceWalk || controller == null) return;
            controller.input = new Vector3(0f, 0f, 1f);
        }

        private IEnumerator Run()
        {
            Directory.CreateDirectory(outputFolder);
            typeof(PlayerWeaponController).GetField("playEquipAnimations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(weapons, false);
            foreach (WeaponDefinition w in arsenal.Catalog.Weapons) arsenal.GrantWeapon(w);
            Vector3 home = new Vector3(520f, 27.72f, 203.5f);

            foreach (string id in weaponIds)
            {
                WeaponDefinition def = arsenal.Catalog.FindById(id);
                if (def == null) continue;
                Teleport(home, 90f);
                shooter.alwaysAiming = false;
                weapons.EquipWeapon(def);
                yield return Settle(1.6f);
                yield return new WaitForEndOfFrame();
                Measure(id, "idle");
                CaptureSet(id, "idle", true);

                shooter.alwaysAiming = def.category != WeaponCategory.Melee;
                yield return Settle(2.2f);
                yield return new WaitForEndOfFrame();
                Measure(id, "aim");
                CaptureSet(id, "aim", true);

                if (!captureExtraStates) continue;

                if (shooter.CurrentWeapon != null)
                {
                    vShooterWeapon gun = shooter.CurrentWeapon;
                    gun.Shoot(gun.muzzle.position, gun.muzzle.position + gun.muzzle.forward * 30f, controller.transform);
                    yield return Settle(0.06f);
                    yield return new WaitForEndOfFrame();
                    CaptureSet(id, "fire", false);
                }

                controller.isCrouching = true;
                yield return Settle(1.6f);
                yield return new WaitForEndOfFrame();
                    CaptureSet(id, "crouchaim", false);
                controller.isCrouching = false;

                shooter.alwaysAiming = false;
                Teleport(home, 90f);
                yield return Settle(0.8f);
                input.lockMoveInput = true;
                forceWalk = true;
                yield return Settle(1.2f);
                yield return new WaitForEndOfFrame();
                    CaptureSet(id, "move", false);
                forceWalk = false;
                input.lockMoveInput = false;
                controller.input = Vector3.zero;
                yield return Settle(0.5f);
            }
            shooter.alwaysAiming = false;
            File.WriteAllText(Path.Combine(outputFolder, "report.txt"), report.ToString());
            IsDone = true;
        }

        // Waits for real time AND enough frames: the editor can run at a few fps while unfocused, and IK / pose blending is per frame.
        private static IEnumerator Settle(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            int frames = Mathf.Max(3, Mathf.RoundToInt(seconds * 30f));
            for (int i = 0; i < frames || Time.realtimeSinceStartup < end; i++) yield return null;
        }

        private void Teleport(Vector3 position, float yaw)
        {
            GetComponentInParentSafe<InvectorGamePlayer>()?.Teleport(position, yaw);
        }

        private T GetComponentInParentSafe<T>() where T : Component => controller != null ? controller.GetComponent<T>() : null;

        private void Measure(string id, string state)
        {
            vShooterWeapon gun = shooter.CurrentWeapon;
            Transform rh = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform lh = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform index = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            Transform shoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (gun == null)
            {
                report.AppendLine(id + " " + state + ": melee/none");
                return;
            }
            Transform root = gun.transform;
            Transform lhTarget = gun.handIKTargetOffset != null ? gun.handIKTargetOffset : gun.handIKTarget;
            float gap = lhTarget != null ? Vector3.Distance(lh.position, lhTarget.position) : -1f;
            report.AppendLine(string.Format("{0} {1}: lhGap={2:F3} index(local)={3} wrist(local)={4} shoulder(local)={5} aimErr={6:F1}deg",
                id, state, gap, root.InverseTransformPoint(index.position).ToString("F3"), root.InverseTransformPoint(rh.position).ToString("F3"),
                root.InverseTransformPoint(shoulder.position).ToString("F3"), Vector3.Angle(root.forward, Camera.main.transform.forward)));
        }

        /// <summary>One capture of the current pose (all views) after IK has been applied this frame.</summary>
        public static void Snap(GameObject player, string fileName, string folder)
        {
            var runner = new GameObject("WeaponPoseSnap").AddComponent<WeaponPoseCaptureRunner>();
            runner.outputFolder = folder;
            runner.shooter = player.GetComponent<vShooterManager>();
            runner.controller = player.GetComponent<vThirdPersonController>();
            runner.animator = player.GetComponent<Animator>();
            runner.StartCoroutine(runner.SnapRoutine(fileName));
        }

        private IEnumerator SnapRoutine(string fileName)
        {
            Directory.CreateDirectory(outputFolder);
            yield return new WaitForEndOfFrame();
            Measure(fileName, "snap");
            CaptureSet(fileName, "snap", true);
            File.WriteAllText(Path.Combine(outputFolder, fileName + "_snap.txt"), report.ToString());
            Destroy(gameObject);
        }

        private void CaptureSet(string id, string state, bool allViews)
        {
            Transform rh = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform lh = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform t = controller.transform;
            Vector3 center = (rh.position + lh.position) * 0.5f;
            var tex = new Texture2D(allViews ? 1280 : 960, 360, TextureFormat.RGB24, false);
            int x = 0;
            if (allViews)
            {
                RenderView(tex, ref x, 320, center + t.right * 1.1f + t.forward * 0.2f + Vector3.up * 0.1f, center + t.forward * 0.15f, 40f);
                RenderView(tex, ref x, 320, center - t.right * 0.9f + t.forward * 0.8f + Vector3.up * 0.15f, center + t.forward * 0.15f, 40f);
                RenderView(tex, ref x, 320, rh.position + t.right * 0.45f - t.up * 0.05f, rh.position + t.forward * 0.05f, 30f); // macro grip
            }
            else
            {
                RenderView(tex, ref x, 480, center + t.right * 1.4f + t.forward * 0.3f + Vector3.up * 0.1f, center + t.forward * 0.15f, 45f);
            }
            RenderCamera(tex, ref x, allViews ? 320 : 480);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(outputFolder, id + "_" + state + ".png"), tex.EncodeToPNG());
            Destroy(tex);
        }

        private static void RenderView(Texture2D target, ref int x, int width, Vector3 from, Vector3 lookAt, float fov)
        {
            var go = new GameObject("PoseCam");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.03f;
            go.transform.position = from;
            go.transform.LookAt(lookAt);
            Grab(cam, target, ref x, width);
            Destroy(go);
        }

        private static void RenderCamera(Texture2D target, ref int x, int width)
        {
            Camera cam = Camera.main;
            RenderTexture old = cam.targetTexture;
            Grab(cam, target, ref x, width);
            cam.targetTexture = old;
        }

        private static void Grab(Camera cam, Texture2D target, ref int x, int width)
        {
            var rt = RenderTexture.GetTemporary(width, 360, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            target.ReadPixels(new Rect(0, 0, width, 360), x, 0);
            RenderTexture.active = null;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            x += width;
        }
    }
}
#endif
