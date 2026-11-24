using System.Collections;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// Hands the dusk / dawn night level to every car's lights (<see cref="VehicleLights.SetNightLevel"/>; each car switches at its own point),
    /// following the scene's <see cref="CityDayNightController"/> (Hamza's, left untouched). That controller has no change event,
    /// so its night blend is read a few times a second; the global switch only fires when the state actually changes. Lives
    /// under ///Manager///, not a Singleton. Scenes driven by <see cref="TimeOfDayManager"/> do not need it (it sets the switch itself).
    /// </summary>
    [DisallowMultipleComponent]
    public class VehicleNightLightsSync : MonoBehaviour
    {
        [SerializeField] private CityDayNightController cityLighting;
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
            var fast = new WaitForSecondsRealtime(0.1f);
            while (true)
            {
                // The level itself: each car switches at its own point of the dusk (VehicleLights).
                VehicleLights.SetNightLevel(cityLighting.NightBlend);
                yield return cityLighting.NightBlend > 0f && cityLighting.NightBlend < 1f ? fast : wait;
            }
        }

#if UNITY_EDITOR
        public void EditorSetup(CityDayNightController controller) => cityLighting = controller;
#endif
    }
}
