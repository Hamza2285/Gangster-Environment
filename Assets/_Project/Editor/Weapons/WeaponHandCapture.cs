using System.Collections.Generic;
using System.IO;
using Invector.vShooter;
using OpenworldCrime.Player;
using OpenworldCrime.Weapons;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Play-mode check of how every catalog weapon sits in the player's hands: equips each owned weapon in turn, aims it, and
    /// saves a side and a front close-up of the hands per weapon (plus the hand-to-IK-target distances) into a folder.
    /// Start with <see cref="Run"/> while in Play; it runs on the editor update and logs "[WeaponHandCapture] done" at the end.
    /// </summary>
    public static class WeaponHandCapture
    {
        private const float SettleSeconds = 3.5f;

        private static List<WeaponDefinition> queue;
        private static int index;
        private static double nextTime;
        private static bool aim;
        private static string folder;
        private static System.Text.StringBuilder report;
        private static string only;

        public static string Run(string outputFolder, bool aiming, string onlyIds = null)
        {
            if (!Application.isPlaying) return "Enter Play first.";
            var wc = Object.FindObjectOfType<PlayerWeaponController>();
            var arsenal = wc != null ? wc.GetComponent<PlayerArsenal>() : null;
            if (arsenal == null || arsenal.Catalog == null) return "No player arsenal.";
            folder = outputFolder;
            Directory.CreateDirectory(folder);
            aim = aiming;
            only = onlyIds;
            queue = new List<WeaponDefinition>();
            foreach (WeaponDefinition w in arsenal.Catalog.Weapons)
            {
                if (w.displayPrefab == null || w.category == WeaponCategory.Melee) continue;
                if (!string.IsNullOrEmpty(only) && !("," + only + ",").Contains("," + w.id + ",")) continue;
                arsenal.GrantWeapon(w);
                if (arsenal.OwnsWeapon(w)) queue.Add(w);
            }
            var sm = wc.GetComponent<vShooterManager>();
            sm.alwaysAiming = aim;
            report = new System.Text.StringBuilder();
            index = -1;
            nextTime = 0;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            return "Capturing " + queue.Count + " weapons into " + folder;
        }

        private static void Tick()
        {
            if (!Application.isPlaying) { EditorApplication.update -= Tick; return; }
            EditorApplication.QueuePlayerLoopUpdate(); // keep the game ticking while the editor is in the background
            if (EditorApplication.timeSinceStartup < nextTime) return;
            var wc = Object.FindObjectOfType<PlayerWeaponController>();
            if (index >= 0 && wc != null) Capture(wc, queue[index]);
            index++;
            if (index >= queue.Count || wc == null)
            {
                EditorApplication.update -= Tick;
                if (wc != null) wc.GetComponent<vShooterManager>().alwaysAiming = false;
                File.WriteAllText(Path.Combine(folder, "report.txt"), report.ToString());
                Debug.Log("[WeaponHandCapture] done\n" + report);
                return;
            }
            wc.EquipWeapon(queue[index]);
            nextTime = EditorApplication.timeSinceStartup + SettleSeconds;
        }

        /// <summary>Saves the close-up of the weapon in hand right now (no re-equip), e.g. after moving its model by hand.</summary>
        public static string CaptureNow(string outputFolder, string label)
        {
            var wc = Object.FindObjectOfType<PlayerWeaponController>();
            if (wc == null) return "No player.";
            folder = outputFolder;
            Directory.CreateDirectory(folder);
            report = new System.Text.StringBuilder();
            aim = true;
            var def = new WeaponDefinition { id = label };
            Capture(wc, def);
            return report.ToString();
        }

        private static void Capture(PlayerWeaponController wc, WeaponDefinition def)
        {
            var anim = wc.GetComponent<Animator>();
            Transform rh = anim.GetBoneTransform(HumanBodyBones.RightHand);
            Transform lh = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            vShooterWeapon w = wc.CurrentWeapon;
            Transform root = wc.transform;
            if (w != null)
            {
                Vector3 rhW = w.transform.InverseTransformPoint(rh.position);
                string line = def.id + "  rh(weapon space) " + rhW.ToString("F3");
                if (w.handIKTarget != null)
                    line += "  lh->target " + Vector3.Distance(lh.position, w.handIKTarget.position).ToString("F3") +
                            "  lh(weapon) " + w.transform.InverseTransformPoint(lh.position).ToString("F3") +
                            "  target(weapon) " + w.transform.InverseTransformPoint(w.handIKTarget.position).ToString("F3");
                report.AppendLine(line);
            }
            else report.AppendLine(def.id + "  not equipped");

            Vector3 centre = w != null ? Vector3.Lerp(rh.position, w.transform.position + w.transform.forward * 0.15f, 0.5f) : rh.position;
            var camGo = new GameObject("WeaponHandCaptureCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.03f;
            var rt = new RenderTexture(640, 480, 24);
            cam.targetTexture = rt;
            var tex = new Texture2D(1920, 480, TextureFormat.RGB24, false);
            Vector3[] dirs = { root.right, (root.forward + root.right * 0.5f + root.up * 0.25f), (-root.right + root.up * 0.35f + root.forward * 0.2f) };
            for (int i = 0; i < dirs.Length; i++)
            {
                cam.transform.position = centre + dirs[i].normalized * 1.05f;
                cam.transform.LookAt(centre);
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 640, 480), i * 640, 0);
            }
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(folder, def.id + (aim ? "_aim" : "_idle") + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.Destroy(camGo);
            rt.Release();
        }
    }
}
