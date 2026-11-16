using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Pool of car damage effects (smoke, fire, explosion), keyed by prefab. Not a manager object: a static helper like
    /// <see cref="VehicleExplosion"/>, it creates its two holder objects in the active scene on first use. Free instances wait under an
    /// inactive holder (no Awake, no cost); a spawn moves one under the active holder, which (re)starts its particles and looping sounds.
    /// Each prefab can be capped (maxLive): over the cap the spawn returns null and the caller simply goes without the effect.
    /// </summary>
    public static class VehicleEffectPool
    {
        private static readonly Dictionary<GameObject, Stack<PooledVehicleEffect>> free = new Dictionary<GameObject, Stack<PooledVehicleEffect>>();
        private static readonly Dictionary<GameObject, int> live = new Dictionary<GameObject, int>();
        private static readonly Dictionary<GameObject, int> created = new Dictionary<GameObject, int>();
        private static Transform freeRoot;
        private static Transform liveRoot;

        /// <summary>Creates instances until the prefab has at least count of them (spread the first-use hitch over loading).</summary>
        public static void Prewarm(GameObject prefab, int count)
        {
            if (prefab == null || !EnsureRoots()) return;
            created.TryGetValue(prefab, out int made);
            for (int i = made; i < count; i++) GetFree(prefab).Push(Create(prefab));
        }

        /// <summary>An effect playing at the given position (upright), or null without a prefab or over maxLive (0 = no cap).</summary>
        public static PooledVehicleEffect Spawn(GameObject prefab, Vector3 position, int maxLive)
        {
            if (prefab == null || !EnsureRoots()) return null;
            live.TryGetValue(prefab, out int count);
            if (maxLive > 0 && count >= maxLive) return null;

            Stack<PooledVehicleEffect> stack = GetFree(prefab);
            PooledVehicleEffect effect = null;
            while (stack.Count > 0 && effect == null) effect = stack.Pop();
            if (effect == null) effect = Create(prefab);

            live[prefab] = count + 1;
            Transform t = effect.transform;
            t.SetPositionAndRotation(position, Quaternion.identity);
            effect.Activate();
            // Under the active holder: particles with Play On Awake and looping sounds start by themselves.
            t.SetParent(liveRoot, true);
            return effect;
        }

        internal static void Return(PooledVehicleEffect effect)
        {
            if (effect == null || effect.Prefab == null) return;
            if (live.TryGetValue(effect.Prefab, out int count)) live[effect.Prefab] = Mathf.Max(0, count - 1);
            if (freeRoot == null)
            {
                // The scene holding the pool is going away.
                Object.Destroy(effect.gameObject);
                return;
            }
            effect.transform.SetParent(freeRoot, false);
            GetFree(effect.Prefab).Push(effect);
        }

        private static PooledVehicleEffect Create(GameObject prefab)
        {
            GameObject instance = Object.Instantiate(prefab, freeRoot, false);
            instance.name = prefab.name;
            if (!instance.TryGetComponent(out PooledVehicleEffect effect)) effect = instance.AddComponent<PooledVehicleEffect>();
            effect.Initialize(prefab);
            created.TryGetValue(prefab, out int made);
            created[prefab] = made + 1;
            return effect;
        }

        private static Stack<PooledVehicleEffect> GetFree(GameObject prefab)
        {
            if (!free.TryGetValue(prefab, out Stack<PooledVehicleEffect> stack))
            {
                stack = new Stack<PooledVehicleEffect>();
                free.Add(prefab, stack);
            }
            return stack;
        }

        // The holders live in the scene: after a scene change the old instances are gone and the pool starts over.
        private static bool EnsureRoots()
        {
            if (freeRoot != null && liveRoot != null) return true;
            if (!Application.isPlaying) return false;
            ResetStatics();
            var freeHolder = new GameObject("[VehicleEffectPool] Free");
            freeHolder.SetActive(false);
            freeRoot = freeHolder.transform;
            liveRoot = new GameObject("[VehicleEffectPool] Live").transform;
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            free.Clear();
            live.Clear();
            created.Clear();
            freeRoot = null;
            liveRoot = null;
        }
    }
}
