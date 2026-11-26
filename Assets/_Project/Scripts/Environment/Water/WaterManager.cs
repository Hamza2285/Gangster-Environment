using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// Where the water is (one owner for "is this point in water, and how high is the surface there").
    /// The city's water is flat (the ocean plane, the canal and the harbour are one surface at one height, see OceanHorizon),
    /// so a water body is just a surface height over an area - no colliders, no physics queries: a lookup is a few float
    /// compares. More bodies (a pool, a lake at another height) are added in the Inspector.
    /// Ground that lies below the water line but is dry (the sunken underpass roads) is recognised by its collider's root.
    ///
    /// Sea death: Hamza's <see cref="WorldBounds"/> kills the player 0.5 m under the sea surface. Now that the player swims,
    /// that check is only kept for the car (a sinking car still puts its driver out in the water): while the player is on foot its
    /// drown depth is set out of reach at runtime, and put back while driving. The WorldBounds script and its scene values are not
    /// changed (the original value is restored when this manager goes away). Fall-out-of-world death is untouched.
    /// Lives under ///Manager/// (not a Singleton: swimmers get it by Inspector reference).
    /// </summary>
    [DisallowMultipleComponent]
    public class WaterManager : MonoBehaviour
    {
        [Serializable]
        public class WaterBody
        {
            public string name = "Ocean";
            [Tooltip("World height of the water surface.")]
            public float surfaceHeight = 25.9f;
            [Tooltip("Water everywhere (x / z). Off = only inside Area.")]
            public bool unbounded;
            [Tooltip("Area (world x / z) covered by this water when not unbounded.")]
            public Rect area = new Rect(-830f, -1193f, 2058f, 2058f);
        }

        [Header("Water")]
        [SerializeField] private WaterBody[] bodies = { new WaterBody() };

        [Header("Dry Ground")]
        [Tooltip("Roots of colliders that are dry ground even below the water line (e.g. the sunken underpass roads).")]
        [SerializeField] private Transform[] dryGroundRoots = new Transform[0];

        [Header("Sea Death (WorldBounds)")]
        [Tooltip("Hamza's world bounds (drowns the player under the sea surface). Empty = no sea death to manage.")]
        [SerializeField] private WorldBounds worldBounds;
        [Tooltip("Drown depth used while the player is on foot (out of reach: he swims instead).")]
        [SerializeField, Min(10f)] private float onFootDrownDepth = 10000f;

        private readonly Dictionary<Collider, bool> dryCache = new Dictionary<Collider, bool>();
        private float originalDrownDepth = -1f;

        private void Awake()
        {
            if (bodies == null || bodies.Length == 0) Debug.LogWarning($"[{nameof(WaterManager)}] No water bodies on '{name}': nobody will swim.", this);
            if (worldBounds != null) originalDrownDepth = worldBounds.drownDepth;
        }

        private void OnDestroy()
        {
            if (worldBounds != null && originalDrownDepth >= 0f) worldBounds.drownDepth = originalDrownDepth;
        }

        /// <summary>
        /// The surface height of the water covering this x / z (the highest one when bodies overlap). False when there is no water here.
        /// </summary>
        public bool TryGetSurface(Vector3 point, out float surfaceHeight)
        {
            surfaceHeight = float.NegativeInfinity;
            bool found = false;
            for (int i = 0; i < bodies.Length; i++)
            {
                WaterBody body = bodies[i];
                if (!body.unbounded && !body.area.Contains(new Vector2(point.x, point.z))) continue;
                if (body.surfaceHeight > surfaceHeight) surfaceHeight = body.surfaceHeight;
                found = true;
            }
            return found;
        }

        /// <summary>True when this collider is dry ground even below the water line (part of a dry ground root).</summary>
        public bool IsDryGround(Collider ground)
        {
            if (ground == null || dryGroundRoots.Length == 0) return false;
            if (dryCache.TryGetValue(ground, out bool dry)) return dry;
            Transform t = ground.transform;
            for (int i = 0; i < dryGroundRoots.Length && !dry; i++)
                if (dryGroundRoots[i] != null && t.IsChildOf(dryGroundRoots[i])) dry = true;
            dryCache[ground] = dry;
            return dry;
        }

        /// <summary>
        /// Tells the sea death whether the player is driving (true: WorldBounds' own drown depth, so a sinking car puts him out)
        /// or on foot (false: no sea death, he swims).
        /// </summary>
        public void SetPlayerDriving(bool driving)
        {
            if (worldBounds == null || originalDrownDepth < 0f) return;
            worldBounds.drownDepth = driving ? originalDrownDepth : Mathf.Max(originalDrownDepth, onFootDrownDepth);
        }

#if UNITY_EDITOR
        public void EditorSetup(WaterBody[] waterBodies, Transform[] dryRoots, WorldBounds bounds)
        {
            bodies = waterBodies;
            dryGroundRoots = dryRoots;
            worldBounds = bounds;
        }
#endif
    }
}
