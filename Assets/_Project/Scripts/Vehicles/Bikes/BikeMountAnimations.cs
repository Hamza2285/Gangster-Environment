using System.Collections;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Vehicles the player can get on from either side, from where he stands (bikes). <see cref="IVehicleInteractionTarget.GetEnterPoint"/>
    /// stays the default side.
    /// </summary>
    public interface IVehicleEntrySide
    {
        /// <summary>The enter point on the side nearest <paramref name="from"/>.</summary>
        Transform GetEnterPoint(Vector3 from);
    }

    /// <summary>
    /// The get-on / get-off clips of a drivable motorbike (MotoInteractionAnimsFREE, humanoid, so they play on Jason and Mia alike)
    /// and the bike's own help for them (on the bike prefab; per-instance):
    /// - mount / dismount clips for the left and right side, and the short "mounted (foot down) to riding" transitions;
    /// - Seat Hips: where the rider's hips are when seated, so the clip's end pose is placed onto this bike's seat;
    /// - <see cref="RightUp"/>: a bike lying on its side is stood up (smoothly, held kinematic) before anybody gets on.
    /// The clips are played by <c>PlayerVehicleInteraction</c> (Playables, without touching the character's Animator Controller).
    /// </summary>
    [DisallowMultipleComponent]
    public class BikeMountAnimations : MonoBehaviour
    {
        [Header("Clips (humanoid)")]
        public AnimationClip mountLeft;
        public AnimationClip mountRight;
        public AnimationClip dismountLeft;
        public AnimationClip dismountRight;
        [Tooltip("Mounted with a foot down -> riding pose (after getting on).")]
        public AnimationClip mountedToRide;
        [Tooltip("Riding pose -> mounted with a foot down (before getting off).")]
        public AnimationClip rideToMounted;
        [Tooltip("Playback speed of the clips.")]
        [Min(0.1f)] public float clipSpeed = 1f;

        [Header("Seat")]
        [Tooltip("Where the rider's hips are when seated (child of the seat, so it leans with the bike).")]
        public Transform seatHips;

        [Header("Lying Bike")]
        [Tooltip("A bike whose up axis is lower than this (0-1) counts as lying down and is stood up before mounting.")]
        [Range(0f, 1f)] public float lyingUpThreshold = 0.85f;
        [Tooltip("Seconds to stand a lying bike up.")]
        [Min(0.1f)] public float rightUpSeconds = 0.9f;

        public bool IsLyingDown => transform.up.y < lyingUpThreshold;

        /// <summary>Stands the bike up where it lies (keeps its heading, wheels on the ground), physics paused meanwhile.</summary>
        public IEnumerator RightUp()
        {
            if (!IsLyingDown)
            {
                SetSolidCollidersEnabled(true);
                yield break;
            }
            var body = GetComponent<Rigidbody>();
            bool wasKinematic = body != null && body.isKinematic;
            // A lying bike often rests against a car or a kerb: swung up kinematically with its colliders on it would shove them away.
            var solid = new System.Collections.Generic.List<Collider>();
            foreach (Collider c in GetComponentsInChildren<Collider>(true)) if (!c.isTrigger && !(c is WheelCollider)) { solid.Add(c); c.enabled = false; }
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
            Vector3 heading = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (heading.sqrMagnitude < 0.01f) heading = Vector3.ProjectOnPlane(transform.up, Vector3.up);
            Quaternion fromRotation = transform.rotation;
            Quaternion toRotation = Quaternion.LookRotation(heading.normalized, Vector3.up);
            Vector3 fromPosition = transform.position;
            Vector3 toPosition = fromPosition;
            if (Physics.Raycast(fromPosition + Vector3.up * 1.5f, Vector3.down, out RaycastHit ground, 4f, 1, QueryTriggerInteraction.Ignore))
                toPosition = ground.point + Vector3.up * 0.05f;
            for (float t = 0f; t < rightUpSeconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / rightUpSeconds);
                transform.SetPositionAndRotation(Vector3.Lerp(fromPosition, toPosition, k), Quaternion.Slerp(fromRotation, toRotation, k));
                yield return null;
            }
            transform.SetPositionAndRotation(toPosition, toRotation);
            foreach (Collider c in solid) if (c != null) c.enabled = true;
            if (body != null)
            {
                body.position = toPosition;
                body.rotation = toRotation;
                body.isKinematic = wasKinematic;
                // Anything it still overlaps is eased apart instead of being shot away.
                StartCoroutine(GentleDepenetration(body, 1.5f));
            }
        }

        private static IEnumerator GentleDepenetration(Rigidbody body, float seconds)
        {
            float depenetration = body.maxDepenetrationVelocity;
            body.maxDepenetrationVelocity = 0.5f;
            yield return new WaitForSeconds(seconds);
            if (body != null) body.maxDepenetrationVelocity = depenetration;
        }

        /// <summary>Switches the bike's solid colliders (not the wheels, not triggers), e.g. off while it is placed lying against a car.</summary>
        public void SetSolidCollidersEnabled(bool on)
        {
            foreach (Collider c in GetComponentsInChildren<Collider>(true)) if (!c.isTrigger && !(c is WheelCollider)) c.enabled = on;
        }

        /// <summary>The mount clip for a rider standing on this side of the bike.</summary>
        public AnimationClip MountClip(bool rightSide) => rightSide ? mountRight : mountLeft;
        public AnimationClip DismountClip(bool rightSide) => rightSide ? dismountRight : dismountLeft;

        /// <summary>True when <paramref name="point"/> is on the bike's right.</summary>
        public bool IsRightSide(Vector3 point) => Vector3.Dot(point - transform.position, transform.right) > 0f;
    }
}
