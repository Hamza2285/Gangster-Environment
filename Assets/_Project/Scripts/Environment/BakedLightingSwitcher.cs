using System;
using UnityEngine;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// Holds two baked lightmap sets for the city (day: sun and sky bounce; night: moon bounce plus the street lamps' light pools)
    /// and switches the scene between them. The sun/moon stays a realtime light for direct light and shadows (Baked Indirect
    /// mode), so the time-of-day cycle can move it freely; only the soft bounce light and the lamp light come from the bake.
    /// Filled by Tools > OpenworldCrime > Lighting > Bake Day And Night; switched by CityDayNightController with the sky.
    /// </summary>
    public class BakedLightingSwitcher : MonoBehaviour
    {
        [Serializable]
        public class LightmapSet
        {
            public Texture2D[] color = new Texture2D[0];
            public Texture2D[] directional = new Texture2D[0];
            public int[] rendererIndex = new int[0];
            public Vector4[] rendererScaleOffset = new Vector4[0];
        }

        [SerializeField] private Renderer[] bakedRenderers = new Renderer[0];
        [SerializeField] private LightmapSet day = new LightmapSet();
        [SerializeField] private LightmapSet night = new LightmapSet();
        [Tooltip("Set used when the scene starts (the cycle switches it right after).")]
        [SerializeField] private bool startAtNight;

        public bool HasData => day.color.Length > 0 && night.color.Length > 0;
        public bool IsNight { get; private set; }

        private bool applied;

        private void Start()
        {
            if (!applied) Apply(startAtNight);
        }

        /// <summary>Switches the scene's lightmaps to the night or day bake (no-op if already showing it).</summary>
        public void Apply(bool atNight)
        {
            if (!HasData || (applied && atNight == IsNight)) return;
            LightmapSet set = atNight ? night : day;
            var maps = new LightmapData[set.color.Length];
            for (int i = 0; i < maps.Length; i++)
            {
                maps[i] = new LightmapData
                {
                    lightmapColor = set.color[i],
                    lightmapDir = i < set.directional.Length ? set.directional[i] : null,
                };
            }
            LightmapSettings.lightmaps = maps;
            int count = Mathf.Min(bakedRenderers.Length, Mathf.Min(set.rendererIndex.Length, set.rendererScaleOffset.Length));
            for (int i = 0; i < count; i++)
            {
                Renderer r = bakedRenderers[i];
                if (r == null) continue;
                r.lightmapIndex = set.rendererIndex[i];
                r.lightmapScaleOffset = set.rendererScaleOffset[i];
            }
            IsNight = atNight;
            applied = true;
        }

#if UNITY_EDITOR
        public void EditorSetRenderers(Renderer[] renderers) => bakedRenderers = renderers;
        public void EditorSetSet(bool atNight, LightmapSet set) { if (atNight) night = set; else day = set; }
        public Renderer[] EditorRenderers => bakedRenderers;
#endif
    }
}
