using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// Keeps the realtime sun shadow affordable on phones: renderers of the city smaller than Min Caster Size stop casting
    /// shadows when the scene starts (kerb stones, bollards, signs, bins: thousands of draws for shadows nobody sees).
    /// Runtime only: prefabs and the scene keep their own settings, and disabling this puts every renderer back.
    /// Not a Singleton.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShadowCasterBudget : MonoBehaviour
    {
        [SerializeField] private Transform[] roots = new Transform[0];
        [Tooltip("Renderers with a smaller bounding diagonal (m) cast no shadow.")]
        [SerializeField, Min(0f)] private float minCasterSize = 2.5f;

        private readonly List<Renderer> changed = new List<Renderer>();

        public int SuppressedCount => changed.Count;

        private void OnEnable()
        {
            changed.Clear();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null) continue;
                foreach (MeshRenderer r in roots[i].GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.shadowCastingMode != ShadowCastingMode.On || r.bounds.size.magnitude >= minCasterSize) continue;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    changed.Add(r);
                }
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < changed.Count; i++)
                if (changed[i] != null) changed[i].shadowCastingMode = ShadowCastingMode.On;
            changed.Clear();
        }

#if UNITY_EDITOR
        public void EditorSetup(Transform[] newRoots) => roots = newRoots;
#endif
    }
}
