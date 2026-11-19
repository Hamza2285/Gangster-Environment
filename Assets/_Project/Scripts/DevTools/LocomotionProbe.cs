using System.Collections.Generic;
using System.IO;
using System.Text;
using Invector.vCharacterController;
using UnityEngine;

namespace OpenworldCrime.DevTools
{
    /// <summary>
    /// Dev-only locomotion measurement. Added at runtime to the active player: drives the controller's move input from a
    /// script (after Invector read the real input, so live input cannot interfere) through a fixed script
    /// (idle, input on, input off) and logs every physics step to a CSV for analysis. Removes itself when done.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class LocomotionProbe : MonoBehaviour
    {
        public float idleSeconds = 0.6f;
        public float moveSeconds = 1.6f;
        public float stopSeconds = 1.2f;
        public float inputAmount = 1f;
        [Tooltip("Input direction relative to the camera (x = right, z = forward).")]
        public Vector3 inputDirection = Vector3.forward;
        public bool sprint;
        public string outputPath = "Captures/PlayerFeel/probe.csv";

        private vThirdPersonController cc;
        private Animator animator;
        private Rigidbody body;
        private float startTime;
        private Vector3 lastPosition;
        private readonly StringBuilder csv = new StringBuilder("t,phase,input,ccInputMag,animInputMag,speedH,posDelta,stateHash,inTransition,transDur,moveSpeed,grounded,sprinting,colliderMat\n");
        private readonly List<float> unused = new List<float>();

        public bool Done { get; private set; }

        private void Start()
        {
            cc = GetComponent<vThirdPersonController>();
            animator = GetComponent<Animator>();
            body = GetComponent<Rigidbody>();
            startTime = Time.time;
            lastPosition = transform.position;
        }

        private string Phase(float t) => t < idleSeconds ? "idle" : t < idleSeconds + moveSeconds ? "move" : "stop";

        private float WantedInput(float t) => Phase(t) == "move" ? inputAmount : 0f;

        // After vThirdPersonInput.Update (InputHandle) wrote the real input.
        private void Update()
        {
            if (Done || cc == null) return;
            float t = Time.time - startTime;
            cc.input = inputDirection.normalized * WantedInput(t);
            cc.isSprinting = sprint && Phase(t) == "move";
            if (t > idleSeconds + moveSeconds + stopSeconds) Finish();
        }

        // After Invector's FixedUpdate (motor + animator parameters).
        private void FixedUpdate()
        {
            if (Done || cc == null) return;
            float t = Time.time - startTime;
            Vector3 v = body.linearVelocity; v.y = 0f;
            Vector3 d = transform.position - lastPosition; d.y = 0f;
            lastPosition = transform.position;
            var st = animator.GetCurrentAnimatorStateInfo(0);
            bool trans = animator.IsInTransition(0);
            float transDur = trans ? animator.GetAnimatorTransitionInfo(0).duration : 0f;
            var col = cc.GetComponent<Collider>();
            csv.AppendLine(string.Join(",",
                t.ToString("F3"), Phase(t), WantedInput(t).ToString("F2"), cc.inputMagnitude.ToString("F3"),
                animator.GetFloat("InputMagnitude").ToString("F3"), v.magnitude.ToString("F3"), (d.magnitude / Time.fixedDeltaTime).ToString("F3"),
                st.shortNameHash.ToString(), trans ? "1" : "0", transDur.ToString("F2"), cc.moveSpeed.ToString("F3"),
                cc.isGrounded ? "1" : "0", cc.isSprinting ? "1" : "0", col != null && col.sharedMaterial != null ? col.sharedMaterial.name : "-", st.normalizedTime.ToString("F4")));
        }

        private void Finish()
        {
            Done = true;
            cc.input = Vector3.zero;
            cc.isSprinting = false;
            string dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(outputPath, csv.ToString());
            Debug.Log("[LocomotionProbe] wrote " + outputPath);
            Destroy(this);
        }
    }
}
