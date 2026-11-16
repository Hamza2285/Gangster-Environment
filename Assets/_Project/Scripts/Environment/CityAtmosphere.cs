using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// The city's atmosphere layer on top of <see cref="CityDayNightController"/> (Hamza's, untouched): distance fog and our
    /// Day / Night post-processing volumes, both blended with the controller's night amount.
    /// - Fog: Unity's built-in distance fog (a few ALU per pixel; every city shader compiles the fog variants). The controller's
    ///   presets turn fog off and its full-screen Oasis fog is not on the mobile renderer, so without this the city ends in a hard
    ///   far-clip edge on device. The controller rewrites RenderSettings fog only while it transitions, so this re-applies after
    ///   it (LateUpdate) when the night amount changed or the fog was overwritten.
    /// - Post: the Day volume stays at weight 1; the Night volume (higher priority) fades in with the night amount.
    /// Only acts while the city scene is the active scene: an additive interior brings its own render settings and grade, so the
    /// city volumes are set to weight 0 there. Lives under ///Manager/// (not a Singleton).
    /// </summary>
    [DisallowMultipleComponent]
    public class CityAtmosphere : MonoBehaviour
    {
        [Serializable]
        public class FogLook
        {
            public bool enabled = true;
            public Color color = new Color(0.93f, 0.7f, 0.58f);
            [Min(0f)] public float startDistance = 140f;
            [Min(1f)] public float endDistance = 820f;
        }

        [Header("References")]
        [SerializeField] private CityDayNightController dayNight;
        [SerializeField] private Volume dayVolume;
        [SerializeField] private Volume nightVolume;

        [Header("Fog (linear distance fog)")]
        [SerializeField] private FogLook dayFog = new FogLook();
        [SerializeField] private FogLook nightFog = new FogLook { color = new Color(0.05f, 0.08f, 0.14f), startDistance = 90f, endDistance = 650f };

        private float appliedNight = -1f;
        private bool cityActive = true;
        private Color expectedFogColor;

        private void Awake()
        {
            if (dayNight == null) Debug.LogError($"[{nameof(CityAtmosphere)}] No CityDayNightController assigned on '{name}'.", this);
            if (dayVolume == null || nightVolume == null) Debug.LogWarning($"[{nameof(CityAtmosphere)}] '{name}' is missing its Day / Night volume; post processing will not follow the time of day.", this);
        }

        private void OnEnable()
        {
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
            cityActive = SceneManager.GetActiveScene() == gameObject.scene;
            appliedNight = -1f;
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        }

        private void LateUpdate()
        {
            if (dayNight == null || !cityActive) return;
            float night = dayNight.NightBlend;
            bool fogOverwritten = RenderSettings.fogMode != FogMode.Linear || RenderSettings.fogColor != expectedFogColor;
            if (Mathf.Abs(night - appliedNight) < 0.001f && !fogOverwritten) return;
            Apply(night);
        }

        private void Apply(float night)
        {
            appliedNight = night;
            bool fogOn = night < 0.5f ? dayFog.enabled : nightFog.enabled;
            RenderSettings.fog = fogOn;
            RenderSettings.fogMode = FogMode.Linear;
            expectedFogColor = Color.Lerp(dayFog.color, nightFog.color, night);
            RenderSettings.fogColor = expectedFogColor;
            RenderSettings.fogStartDistance = Mathf.Lerp(dayFog.startDistance, nightFog.startDistance, night);
            RenderSettings.fogEndDistance = Mathf.Lerp(dayFog.endDistance, nightFog.endDistance, night);
            if (dayVolume != null) dayVolume.weight = 1f;
            if (nightVolume != null) nightVolume.weight = night;
        }

        private void HandleActiveSceneChanged(Scene previous, Scene next)
        {
            cityActive = next == gameObject.scene;
            if (dayVolume != null) dayVolume.weight = cityActive ? 1f : 0f;
            if (nightVolume != null) nightVolume.weight = cityActive && dayNight != null ? dayNight.NightBlend : 0f;
            appliedNight = -1f; // re-apply the city's fog when it becomes active again
        }

#if UNITY_EDITOR
        public void EditorSetup(CityDayNightController controller, Volume day, Volume night)
        {
            dayNight = controller;
            dayVolume = day;
            nightVolume = night;
        }
#endif
    }
}
