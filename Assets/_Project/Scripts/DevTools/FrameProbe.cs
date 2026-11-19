using System.IO;
using System.Text;
using UnityEngine;

namespace OpenworldCrime.DevTools
{
    /// <summary>
    /// Dev-only per-rendered-frame log of what the eye sees: frame time, physics steps in the frame, player and camera
    /// motion in that frame, the character's screen position, yaw, hips height and head angle. Added at runtime next to a
    /// <see cref="LocomotionProbe"/>; writes a CSV after <see cref="seconds"/> and removes itself.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public class FrameProbe : MonoBehaviour
    {
        public float seconds = 3f;
        public string outputPath = "Captures/PlayerFeel/frames.csv";

        private Animator animator;
        private Camera cam;
        private float startTime;
        private int physicsSteps;
        private Vector3 lastPlayer, lastCam;
        private Vector2 lastScreen;
        private readonly StringBuilder csv = new StringBuilder("t,frameDt,physSteps,playerStep,playerSpeed,camStep,camSpeed,screenX,screenY,screenStep,yaw,camYaw,hipsY,headPitch,headYaw\n");

        private void Start()
        {
            animator = GetComponent<Animator>();
            cam = Camera.main;
            startTime = Time.unscaledTime;
            lastPlayer = animator.GetBoneTransform(HumanBodyBones.Hips).position;
            lastCam = cam.transform.position;
        }

        private void FixedUpdate() => physicsSteps++;

        private void LateUpdate()
        {
            if (cam == null) return;
            float t = Time.unscaledTime - startTime;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-5f);
            Transform hipsBone = animator.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 p = hipsBone.position, c = cam.transform.position; // what is rendered: the skeleton and the view
            float pStep = Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(lastPlayer.x, 0, lastPlayer.z));
            float cStep = Vector3.Distance(new Vector3(c.x, 0, c.z), new Vector3(lastCam.x, 0, lastCam.z));
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            Vector3 sp = cam.WorldToViewportPoint(hips.position);
            Vector2 screen = new Vector2(sp.x, sp.y);
            Vector3 headLocal = (Quaternion.Inverse(chest.rotation) * head.rotation).eulerAngles;
            csv.AppendLine(string.Join(",", t.ToString("F4"), dt.ToString("F4"), physicsSteps, pStep.ToString("F4"), (pStep / dt).ToString("F3"),
                cStep.ToString("F4"), (cStep / dt).ToString("F3"), screen.x.ToString("F4"), screen.y.ToString("F4"),
                ((screen - lastScreen).magnitude).ToString("F4"), transform.eulerAngles.y.ToString("F2"), cam.transform.eulerAngles.y.ToString("F2"),
                (hips.position.y - transform.position.y).ToString("F4"), Mathf.DeltaAngle(0, headLocal.x).ToString("F2"), Mathf.DeltaAngle(0, headLocal.y).ToString("F2")));
            lastPlayer = p; lastCam = c; lastScreen = screen; physicsSteps = 0;
            if (t > seconds)
            {
                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(outputPath, csv.ToString());
                Debug.Log("[FrameProbe] wrote " + outputPath);
                Destroy(this);
            }
        }
    }
}
