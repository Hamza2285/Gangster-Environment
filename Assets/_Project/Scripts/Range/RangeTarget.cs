using System;
using UnityEngine;

namespace OpenworldCrime.Range
{
    public enum RangeHitZone
    {
        Torso = 0,
        Head = 1,
    }

    /// <summary>
    /// Pop-up range target: a hinged board that flips up / down and can slide along a rail. Hit detection lives in its
    /// <see cref="RangeTargetZone"/> colliders (real Invector bullets). Update runs only while it is moving.
    /// </summary>
    [DisallowMultipleComponent]
    public class RangeTarget : MonoBehaviour
    {
        [Tooltip("Hinge that rotates around its local X axis (0 = standing, downAngle = lying flat).")]
        [SerializeField] private Transform hinge;
        [SerializeField] private float downAngle = -88f;
        [SerializeField, Min(10f)] private float flipSpeed = 420f;

        [Header("Rail (optional)")]
        [Tooltip("Slide between the start position and this local offset while up.")]
        [SerializeField] private Vector3 railOffset = Vector3.zero;
        [SerializeField, Min(0f)] private float railSpeed = 1.2f;

        [Header("Feedback")]
        [SerializeField] private AudioSource hitAudio;

        /// <summary>Raised when a standing target is hit. Args: target, zone, hit point.</summary>
        public event Action<RangeTarget, RangeHitZone, Vector3> OnHit;

        public bool IsUp { get; private set; }
        public bool Slides => railOffset.sqrMagnitude > 0.0001f && sliding;

        private Vector3 startLocalPosition;
        private float targetAngle;
        private float currentAngle;
        private bool sliding;
        private float railTime;

        private void Awake()
        {
            if (hinge == null) hinge = transform;
            startLocalPosition = transform.localPosition;
            SetAngle(downAngle);
            targetAngle = downAngle;
            enabled = false;
        }

        private void Update()
        {
            bool flipping = !Mathf.Approximately(currentAngle, targetAngle);
            if (flipping) SetAngle(Mathf.MoveTowards(currentAngle, targetAngle, flipSpeed * Time.deltaTime));
            if (sliding && IsUp)
            {
                railTime += Time.deltaTime * railSpeed / Mathf.Max(0.01f, railOffset.magnitude);
                transform.localPosition = startLocalPosition + railOffset * Mathf.PingPong(railTime, 1f);
            }
            if (!flipping && !(sliding && IsUp)) enabled = false;
        }

        /// <summary>Raises the target; <paramref name="slide"/> moves it along its rail while up.</summary>
        public void Raise(bool slide)
        {
            IsUp = true;
            sliding = slide && railOffset.sqrMagnitude > 0.0001f;
            railTime = UnityEngine.Random.value;
            transform.localPosition = startLocalPosition;
            targetAngle = 0f;
            enabled = true;
        }

        public void Lower()
        {
            IsUp = false;
            sliding = false;
            targetAngle = downAngle;
            enabled = true;
        }

        /// <summary>Lowers and snaps back to the rail start (challenge reset).</summary>
        public void ResetDown()
        {
            Lower();
            transform.localPosition = startLocalPosition;
        }

        internal void ReportHit(RangeHitZone zone, Vector3 point)
        {
            if (!IsUp) return;
            if (hitAudio != null && hitAudio.clip != null) hitAudio.PlayOneShot(hitAudio.clip);
            OnHit?.Invoke(this, zone, point);
        }

        private void SetAngle(float angle)
        {
            currentAngle = angle;
            hinge.localRotation = Quaternion.Euler(angle, 0f, 0f);
        }
    }
}
