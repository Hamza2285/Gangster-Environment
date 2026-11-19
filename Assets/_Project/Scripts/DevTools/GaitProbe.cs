using System.IO;
using System.Text;
using UnityEngine;

namespace OpenworldCrime.DevTools
{
    /// <summary>
    /// Dev-only gait log per physics step: left / right upper-arm and forearm swing (degrees in the body's sagittal plane,
    /// + = forward), foot heights and foot ground speed (slip while a foot is planted), body speed, animator speed and the
    /// layers with weight. Added at runtime next to a <see cref="LocomotionProbe"/>.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public class GaitProbe : MonoBehaviour
    {
        public float seconds = 3.5f;
        public string outputPath = "Captures/PlayerFeel/gait.csv";

        private Animator animator;
        private Transform lFoot, rFoot, lUpper, rUpper, lLower, rLower, chest, hips;
        private Vector3 lastL, lastR, lastHips;
        private float startTime;
        private readonly StringBuilder csv = new StringBuilder("t,dt,bodySpeed,animSpeed,inputMag,lArm,rArm,lFore,rFore,lFootY,rFootY,lFootSpeed,rFootSpeed,lFootFwd,rFootFwd,layers\n");

        private void Start()
        {
            animator = GetComponent<Animator>();
            lFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot); rFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            lUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm); rUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            lLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm); rLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest); hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            lastL = lFoot.position; lastR = rFoot.position; lastHips = hips.position;
            startTime = Time.time;
        }

        // Swing of a limb segment in the body's sagittal plane: angle of the segment from straight down, + = forward.
        private float Swing(Transform from, Transform to)
        {
            Vector3 dir = to.position - from.position;
            Vector3 fwd = transform.forward, up = transform.up;
            return Mathf.Atan2(Vector3.Dot(dir, fwd), -Vector3.Dot(dir, up)) * Mathf.Rad2Deg;
        }

        // Sampled per physics step: the animator updates in the physics loop, so this is independent of the frame rate
        // (and runs after SmoothPlayerMotion has put the model back at its rest offset).
        private void FixedUpdate()
        {
            if (lFoot == null) return;
            float t = Time.time - startTime;
            float dt = Time.fixedDeltaTime;
            Vector3 dl = lFoot.position - lastL, dr = rFoot.position - lastR, dh = hips.position - lastHips;
            dl.y = dr.y = dh.y = 0f;
            var lHand = animator.GetBoneTransform(HumanBodyBones.LeftHand); var rHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var layers = new StringBuilder();
            for (int i = 1; i < animator.layerCount; i++)
            {
                float w = animator.GetLayerWeight(i);
                if (w < 0.01f) continue;
                var st = animator.GetCurrentAnimatorStateInfo(i);
                layers.Append(animator.GetLayerName(i)).Append(':').Append(w.ToString("F2")).Append('/').Append(st.shortNameHash).Append(st.length > 0.05f ? "" : "(empty)").Append(' ');
            }
            foreach (AnimatorClipInfo ci in animator.GetCurrentAnimatorClipInfo(0))
                if (ci.weight > 0.05f) layers.Append(ci.clip.name).Append('*').Append(ci.weight.ToString("F2")).Append(' ');
            float ground = transform.position.y;
            csv.AppendLine(string.Join(",", t.ToString("F3"), dt.ToString("F4"), (dh.magnitude / dt).ToString("F3"), animator.speed.ToString("F2"),
                animator.GetFloat("InputMagnitude").ToString("F2"),
                Swing(lUpper, lLower).ToString("F1"), Swing(rUpper, rLower).ToString("F1"), Swing(lLower, lHand).ToString("F1"), Swing(rLower, rHand).ToString("F1"),
                (lFoot.position.y - ground).ToString("F3"), (rFoot.position.y - ground).ToString("F3"),
                (dl.magnitude / dt).ToString("F3"), (dr.magnitude / dt).ToString("F3"),
                (Vector3.Dot(dl, transform.forward) / dt).ToString("F3"), (Vector3.Dot(dr, transform.forward) / dt).ToString("F3"), layers.ToString().Trim()));
            lastL = lFoot.position; lastR = rFoot.position; lastHips = hips.position;
            if (t > seconds)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                File.WriteAllText(outputPath, csv.ToString());
                Debug.Log("[GaitProbe] wrote " + outputPath);
                Destroy(this);
            }
        }
    }
}
