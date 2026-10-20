using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// Puts broken <see cref="BreakableProp"/>s back. Lives under ///Manager/// (one per scene, not a Singleton).
    /// - Learns about broken props from <see cref="BreakableProp.OnBroken"/>; intact props are never looked at, so the cost
    ///   does not grow with the number of props in the world.
    /// - While anything is broken, every Check Interval seconds the broken props are tested against the Reset Radius around
    ///   the player (squared distance only). Nothing runs while nothing is broken.
    /// - At most Max Broken Props stay broken; above that the oldest one is reset at once.
    /// Distances and the cap have setters for a central graphics/quality settings manager.
    /// </summary>
    [DisallowMultipleComponent]
    public class BreakablePropManager : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Distance reference (the player root; it follows the player into vehicles).")]
        [SerializeField] private Transform player;

        [Header("Reset")]
        [Tooltip("Broken props farther than this (m) from the player are put back.")]
        [SerializeField, Min(1f)] private float resetRadius = 70f;
        [Tooltip("A broken prop stays broken at least this long (s), even if the player is far away.")]
        [SerializeField, Min(0f)] private float minimumBrokenTime = 4f;
        [Tooltip("Seconds between distance checks of broken props.")]
        [SerializeField, Min(0.1f)] private float checkInterval = 1.5f;
        [Tooltip("Upper limit of props that are broken at the same time (the oldest is reset first).")]
        [SerializeField, Min(1)] private int maxBrokenProps = 24;

        private readonly List<BreakableProp> brokenProps = new List<BreakableProp>();
        private Coroutine checkRoutine;
        private WaitForSeconds checkWait;

        public int BrokenCount => brokenProps.Count;
        public float ResetRadius => resetRadius;
        public int MaxBrokenProps => maxBrokenProps;

        private void Awake()
        {
            if (player == null) Debug.LogWarning($"[{nameof(BreakablePropManager)}] No player assigned on '{name}': broken props only reset through the cap.", this);
            checkWait = new WaitForSeconds(checkInterval);
        }

        private void OnEnable()
        {
            BreakableProp.OnBroken += HandleBroken;
        }

        private void OnDisable()
        {
            BreakableProp.OnBroken -= HandleBroken;
            checkRoutine = null;
        }

        public void SetResetRadius(float radius) => resetRadius = Mathf.Max(1f, radius);

        public void SetMaxBrokenProps(int max)
        {
            maxBrokenProps = Mathf.Max(1, max);
            while (brokenProps.Count > maxBrokenProps) ResetAt(0);
        }

        public void SetCheckInterval(float seconds)
        {
            checkInterval = Mathf.Max(0.1f, seconds);
            checkWait = new WaitForSeconds(checkInterval);
        }

        public void SetPlayer(Transform target) => player = target;

        /// <summary>Puts every broken prop back at once (e.g. on respawn or scene restart).</summary>
        public void ResetAll()
        {
            for (int i = brokenProps.Count - 1; i >= 0; i--) ResetAt(i);
        }

        private void HandleBroken(BreakableProp prop)
        {
            if (prop == null || brokenProps.Contains(prop)) return;
            brokenProps.Add(prop);
            while (brokenProps.Count > maxBrokenProps) ResetAt(0);
            if (checkRoutine == null && isActiveAndEnabled) checkRoutine = StartCoroutine(CheckLoop());
        }

        private IEnumerator CheckLoop()
        {
            while (brokenProps.Count > 0)
            {
                yield return checkWait;
                CheckBrokenProps();
            }
            checkRoutine = null;
        }

        private void CheckBrokenProps()
        {
            if (player == null) return;
            Vector3 center = player.position;
            float radiusSqr = resetRadius * resetRadius;
            float now = Time.time;
            for (int i = brokenProps.Count - 1; i >= 0; i--)
            {
                BreakableProp prop = brokenProps[i];
                if (prop == null || !prop.IsBroken)
                {
                    brokenProps.RemoveAt(i);
                    continue;
                }
                if (now - prop.BrokenTime < minimumBrokenTime) continue;
                if ((prop.RestPosition - center).sqrMagnitude > radiusSqr) ResetAt(i);
            }
        }

        private void ResetAt(int index)
        {
            BreakableProp prop = brokenProps[index];
            brokenProps.RemoveAt(index);
            if (prop != null) prop.ResetProp();
        }
    }
}
