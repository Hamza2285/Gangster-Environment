using System;
using UnityEngine;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// Fades night-only glow renderers in and out with the day/night blend instead of popping on (scene system, per city; not a
    /// Singleton). <see cref="CityDayNightController"/> (Hamza's, untouched) shows its lamp-bulb mesh as soon as the dusk starts,
    /// at full brightness; this scales the colour of each entry with the controller's night blend over its own window of the
    /// dusk (entries can be staggered so the city lights up gradually), through a MaterialPropertyBlock: the material assets are
    /// never changed, and at full night the colour is exactly the material's. LateUpdate, after the controller; only works when
    /// the blend changed (nothing per frame by day or by night).
    /// Cost: a property block takes these few renderers out of the SRP Batcher (one extra SetPass each while they are visible).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(600)]
    public class NightGlowFader : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            public Renderer renderer;
            [Tooltip("Colour property of its material (HDR glow colour).")]
            public string colorProperty = "_Color";
            [Tooltip("Night blend window over which it comes up (0 day - 1 night).")]
            public Vector2 window = new Vector2(0.12f, 0.6f);
            [Tooltip("Colour by day / at the start of the window, as a share of the night colour (a dark unlit bulb).")]
            [Range(0f, 1f)] public float offShare = 0.03f;
            [NonSerialized] public Color nightColor;
            [NonSerialized] public int propertyId;
            [NonSerialized] public bool ready;
        }

        [SerializeField] private CityDayNightController dayNight;
        [SerializeField] private Entry[] entries = new Entry[0];

        private MaterialPropertyBlock block;
        private float applied = -1f;

        private void Awake()
        {
            if (dayNight == null) { Debug.LogError($"[{nameof(NightGlowFader)}] No CityDayNightController on '{name}'.", this); enabled = false; return; }
            block = new MaterialPropertyBlock();
            foreach (Entry e in entries)
            {
                if (e.renderer == null || e.renderer.sharedMaterial == null) continue;
                e.propertyId = Shader.PropertyToID(e.colorProperty);
                if (!e.renderer.sharedMaterial.HasProperty(e.propertyId)) { Debug.LogWarning($"[{nameof(NightGlowFader)}] '{e.renderer.name}' has no {e.colorProperty}.", this); continue; }
                e.nightColor = e.renderer.sharedMaterial.GetColor(e.propertyId);
                e.ready = true;
            }
        }

        private void OnDisable()
        {
            // Back to the material's own colour.
            foreach (Entry e in entries) if (e.ready && e.renderer != null) e.renderer.SetPropertyBlock(null);
            applied = -1f;
        }

        private void LateUpdate()
        {
            float night = dayNight.NightBlend;
            if (Mathf.Abs(night - applied) < 0.002f) return;
            applied = night;
            foreach (Entry e in entries)
            {
                if (!e.ready || e.renderer == null) continue;
                float k = Mathf.InverseLerp(e.window.x, e.window.y, night);
                k = k * k * (3f - 2f * k);
                float share = Mathf.Lerp(e.offShare, 1f, k);
                if (share >= 0.999f) { e.renderer.SetPropertyBlock(null); continue; } // full night: the material itself (batched)
                e.renderer.GetPropertyBlock(block);
                block.SetColor(e.propertyId, e.nightColor * share);
                e.renderer.SetPropertyBlock(block);
            }
        }

#if UNITY_EDITOR
        public void EditorSetup(CityDayNightController controller, Entry[] items)
        {
            dayNight = controller;
            entries = items;
        }
#endif
    }
}
