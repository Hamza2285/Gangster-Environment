using System.Collections;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// Switches every car's headlights and tail lights on at dusk and off at dawn (<see cref="VehicleLights.SetNightMode"/>),
    /// following the scene's <see cref="CityDayNightController"/> (Hamza's, left untouched). That controller has no change event,
    /// so its night blend is read a few times a second; the global switch only fires when the state actually changes. Lives
    /// under ///Manager///, not a Singleton. Scenes driven by <see cref="TimeOfDayManager"/> do not need it (it sets the switch itself).
    /// </summary>
    [DisallowMultipleComponent]
    public class VehicleNightLightsSync : MonoBehaviour
    {
        [SerializeField] private CityDayNightController cityLighting;
        [Tooltip("Night blend (0 day - 1 night) at which the lights come on (dusk) ...")]
        [SerializeField, Range(0f, 1f)] private float lightsOnAt = 0.35f;
        [Tooltip("... and below which they go off again (dawn). Lower than Lights On At, so a blend hovering in between does not flicker.")]
        [SerializeField, Range(0f, 1f)] private float lightsOffAt = 0.25f;
        [SerializeField, Min(0.05f)] private float checkInterval = 0.5f;

        private void Awake()
        {
            if (cityLighting == null) Debug.LogError($"[{nameof(VehicleNightLightsSync)}] No CityDayNightController assigned on '{name}'. Car lights will not follow the time of day.", this);
        }

        private void OnEnable()
        {
            if (cityLighting != null) StartCoroutine(Watch());
        }

        private IEnumerator Watch()
        {
            var wait = new WaitForSecondsRealtime(checkInterval);
            while (true)
            {
                float blend = cityLighting.NightBlend;
                bool night = VehicleLights.NightMode ? blend > lightsOffAt : blend >= lightsOnAt;
                VehicleLights.SetNightMode(night);
                yield return wait;
            }
        }

#if UNITY_EDITOR
        public void EditorSetup(CityDayNightController controller) => cityLighting = controller;
#endif
    }
}
