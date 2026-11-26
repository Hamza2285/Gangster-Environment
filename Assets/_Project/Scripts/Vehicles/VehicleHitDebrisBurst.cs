using System;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// One car hit burst (VFX_CarHitDebris): a few dark plastic/metal bits, sparks and a dust puff. Its particle systems do not
    /// play on their own; <see cref="Play"/> emits a number of particles per layer scaled by the hit strength, so one pooled prefab
    /// serves a bullet tick and a head-on crash. Spawned and returned by <see cref="VehicleHealth"/> through
    /// <see cref="VehicleEffectPool"/>. No Update.
    /// </summary>
    [DisallowMultipleComponent]
    public class VehicleHitDebrisBurst : MonoBehaviour
    {
        [Serializable]
        public struct Layer
        {
            public ParticleSystem system;
            [Tooltip("Particles at the weakest hit that throws debris.")]
            [Min(0)] public int minCount;
            [Tooltip("Particles at a full-strength crash.")]
            [Min(0)] public int maxCount;
        }

        [Tooltip("Bits, sparks and dust. Each emits Min..Max Count by hit strength.")]
        [SerializeField] private Layer[] layers = new Layer[0];

        /// <summary>Emits the burst. strength: 0 = lightest hit, 1 = full crash.</summary>
        public void Play(float strength)
        {
            strength = Mathf.Clamp01(strength);
            for (int i = 0; i < layers.Length; i++)
            {
                ParticleSystem system = layers[i].system;
                if (system == null) continue;
                int count = Mathf.RoundToInt(Mathf.Lerp(layers[i].minCount, layers[i].maxCount, strength));
                if (count > 0) system.Emit(count);
            }
        }

#if UNITY_EDITOR
        public void EditorSetup(Layer[] newLayers) => layers = newLayers;
#endif
    }
}
