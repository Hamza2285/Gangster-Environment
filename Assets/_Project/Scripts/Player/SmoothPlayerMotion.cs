using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Removes the movement judder of the on-foot player. Invector moves the character (Rigidbody) and the third-person
    /// camera rig (a kinematic Rigidbody, MovePosition / MoveRotation) in physics steps, so both only advance when a step
    /// runs: a rendered frame that gets 0, 1 or 2 steps shows the world standing still or jumping (most visible as the
    /// character starts moving or turns).
    ///
    /// Only what is rendered changes; physics and gameplay keep the exact step poses:
    /// - the last two step poses of the character root and of the camera rig are recorded,
    /// - each rendered frame the visual children are placed between them (the character's model object and the Main
    ///   Camera, a child of the rig), with the same one-step lag Unity's Rigidbody interpolation uses, so camera and
    ///   character stay in step,
    /// - before the next Update and physics step the visual children go back to their normal local poses.
    /// The roots are never moved (this project syncs transforms into physics automatically, so moving them would move the
    /// bodies). Jumps larger than a step can explain (teleports, camera snaps) are shown directly; kinematic characters
    /// (vehicles, seats, rides, ragdolls) and a Main Camera moved by another system (vehicle / entry cameras) are left alone.
    /// Lives under ///Manager/// (not a Singleton).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public class SmoothPlayerMotion : MonoBehaviour
    {
        [Header("Camera")]
        [Tooltip("The Invector third-person camera rig (moved by physics).")]
        [SerializeField] private Transform cameraRig;
        [Tooltip("The camera that renders, a child of the rig at its local origin.")]
        [SerializeField] private Transform cameraView;

        [Header("Characters")]
        [Tooltip("Physics bodies of the playable characters.")]
        [SerializeField] private Rigidbody[] bodies = new Rigidbody[0];
        [Tooltip("Model object of each character (same order as the bodies): the visual that is placed between steps.")]
        [SerializeField] private Transform[] models = new Transform[0];

        [Header("Limits")]
        [Tooltip("A step longer than this (m) is a teleport: shown directly.")]
        [SerializeField, Min(0.1f)] private float maxStepDistance = 2f;
        [Tooltip("A step turning more than this (deg) is a snap: shown directly.")]
        [SerializeField, Range(10f, 180f)] private float maxStepAngle = 90f;

        private class Track
        {
            public Transform root;      // moved by physics, never by this component
            public Rigidbody body;      // null = always smoothed (camera rig)
            public Transform visual;    // placed between steps
            public Vector3 restLocalPosition;
            public Quaternion restLocalRotation = Quaternion.identity;
            public Vector3 previousPosition, currentPosition, stepStartPosition;
            public Quaternion previousRotation, currentRotation, stepStartRotation;
            public bool hasPose, hasStepStart, offsetApplied;
            public Vector3 appliedLocalPosition;
        }

        private Track[] tracks = new Track[0];
        private float lastFixedTime = -1f;
        private SmoothPlayerMotionRestore restore;

        private void Awake()
        {
            int pairs = Mathf.Min(bodies.Length, models.Length);
            bool hasCamera = cameraRig != null && cameraView != null;
            tracks = new Track[(hasCamera ? 1 : 0) + pairs];
            int i = 0;
            if (hasCamera)
                tracks[i++] = new Track { root = cameraRig, visual = cameraView, restLocalPosition = cameraView.localPosition, restLocalRotation = cameraView.localRotation };
            for (int b = 0; b < pairs; b++)
            {
                Transform model = models[b];
                tracks[i++] = new Track
                {
                    root = bodies[b] != null ? bodies[b].transform : null,
                    body = bodies[b],
                    visual = model,
                    restLocalPosition = model != null ? model.localPosition : Vector3.zero,
                    restLocalRotation = model != null ? model.localRotation : Quaternion.identity,
                };
            }
            if (!hasCamera) Debug.LogWarning($"[{nameof(SmoothPlayerMotion)}] Camera rig or view not assigned on '{name}'.", this);
        }

        private void OnEnable()
        {
            restore = gameObject.AddComponent<SmoothPlayerMotionRestore>();
            restore.Owner = this;
            for (int i = 0; i < tracks.Length; i++) { tracks[i].hasPose = false; tracks[i].hasStepStart = false; }
        }

        private void OnDisable()
        {
            if (restore != null) Destroy(restore);
            restore = null;
            RestoreVisuals();
        }

        private static bool Active(Track t) =>
            t.root != null && t.visual != null && t.root.gameObject.activeInHierarchy && (t.body == null || !t.body.isKinematic);

        // After Invector's FixedUpdate, before the simulation: the roots still hold the previous step's pose.
        private void FixedUpdate()
        {
            for (int i = 0; i < tracks.Length; i++)
            {
                Track t = tracks[i];
                if (!Active(t)) { t.hasPose = t.hasStepStart = false; continue; }
                t.stepStartPosition = t.root.position;
                t.stepStartRotation = t.root.rotation;
                t.hasStepStart = true;
            }
        }

        // Per rendered frame (after Invector's LateUpdate work): place the visuals between the last two step poses.
        private void LateUpdate()
        {
            bool stepped = Time.fixedTime != lastFixedTime;
            lastFixedTime = Time.fixedTime;
            float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            for (int i = 0; i < tracks.Length; i++)
            {
                Track t = tracks[i];
                if (!Active(t)) { t.hasPose = false; continue; }
                if (stepped && t.hasStepStart)
                {
                    t.previousPosition = t.stepStartPosition;
                    t.previousRotation = t.stepStartRotation;
                    t.currentPosition = t.root.position;
                    t.currentRotation = t.root.rotation;
                    t.hasPose = true;
                }
                if (!t.hasPose) continue;
                // Someone else moved the root since the step (teleport, camera snap): show it as it is.
                if ((t.root.position - t.currentPosition).sqrMagnitude > 0.0001f || Quaternion.Angle(t.root.rotation, t.currentRotation) > 0.05f) continue;
                if ((t.currentPosition - t.previousPosition).sqrMagnitude > maxStepDistance * maxStepDistance
                    || Quaternion.Angle(t.previousRotation, t.currentRotation) > maxStepAngle) continue;
                // A visual moved by another system (vehicle / entry camera blends) is left to it.
                if ((t.visual.localPosition - t.restLocalPosition).sqrMagnitude > 0.000001f || Quaternion.Angle(t.visual.localRotation, t.restLocalRotation) > 0.01f) continue;

                Vector3 rootPosition = Vector3.Lerp(t.previousPosition, t.currentPosition, alpha);
                Quaternion rootRotation = Quaternion.Slerp(t.previousRotation, t.currentRotation, alpha);
                // The visual's normal pose relative to the interpolated root.
                Vector3 worldPosition = rootPosition + rootRotation * Vector3.Scale(t.root.lossyScale, t.restLocalPosition);
                Quaternion worldRotation = rootRotation * t.restLocalRotation;
                t.visual.SetPositionAndRotation(worldPosition, worldRotation);
                t.appliedLocalPosition = t.visual.localPosition;
                t.offsetApplied = true;
            }
        }

        /// <summary>Puts the visuals back on their normal local poses (before Update and before each physics step).</summary>
        internal void RestoreVisuals()
        {
            for (int i = 0; i < tracks.Length; i++)
            {
                Track t = tracks[i];
                if (!t.offsetApplied || t.visual == null) continue;
                t.offsetApplied = false;
                // Only if nothing else moved it in between.
                if ((t.visual.localPosition - t.appliedLocalPosition).sqrMagnitude < 0.000001f)
                {
                    t.visual.localPosition = t.restLocalPosition;
                    t.visual.localRotation = t.restLocalRotation;
                }
            }
        }
    }

    /// <summary>Runs early (before Invector) and gives the smoothed visuals their normal local poses back.</summary>
    [DefaultExecutionOrder(-1000)]
    [AddComponentMenu("")]
    public class SmoothPlayerMotionRestore : MonoBehaviour
    {
        internal SmoothPlayerMotion Owner;

        private void FixedUpdate()
        {
            if (Owner != null) Owner.RestoreVisuals();
        }

        private void Update()
        {
            if (Owner != null) Owner.RestoreVisuals();
        }
    }
}
