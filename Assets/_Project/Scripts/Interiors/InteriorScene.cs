using System;
using UnityEngine;

namespace OpenworldCrime.Interiors
{
    /// <summary>
    /// Root marker of an additively loaded interior scene. Registers itself so the city's <see cref="InteriorTravelManager"/>
    /// can find the spawn point without any cross-scene reference. The interior's own lighting (lightmap, probes) stays
    /// in the interior scene.
    /// </summary>
    [DisallowMultipleComponent]
    public class InteriorScene : MonoBehaviour
    {
        [Tooltip("Where the player appears when entering (inside the front door, facing in).")]
        [SerializeField] private Transform spawnPoint;
        [Tooltip("Name shown in UI messages.")]
        [SerializeField] private string displayName = "Gun Shop";

        public static InteriorScene Current { get; private set; }
        public static event Action<InteriorScene> OnInteriorLoaded;

        public Transform SpawnPoint => spawnPoint != null ? spawnPoint : transform;
        public string DisplayName => displayName;

        private void Awake()
        {
            if (spawnPoint == null) Debug.LogWarning($"[{nameof(InteriorScene)}] '{name}' has no spawn point; using its own transform.", this);
            Current = this;
            OnInteriorLoaded?.Invoke(this);
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
            OnInteriorLoaded = null;
        }
    }
}
