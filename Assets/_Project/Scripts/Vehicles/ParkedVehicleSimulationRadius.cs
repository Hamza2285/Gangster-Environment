using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Pauses what an empty, parked drivable car keeps running for nothing: its silent looping engine sounds and its idle wheel
    /// smoke / exhaust particle systems (about 15 per car, all simulated every frame). They resume when the car is used. Sleeping
    /// far cars is <see cref="TrafficTakeoverManager"/>'s job. Created at scene load for the cars placed in the scene, no scene object.
    /// </summary>
    public class ParkedVehicleSimulationRadius : MonoBehaviour
    {
        private const float CheckInterval = 0.25f;
        private const float StartDelay = 3f; // cars settle on their wheels first

        private class Parked
        {
            public RCCVehicleAdapter Adapter;
            public RCC_CarControllerV3 Car;
            public AudioSource[] Audio;
            public ParticleSystem[] Particles;
            public bool Idle;
        }

        private readonly List<Parked> cars = new List<Parked>();
        private float nextCheck;

        public int SleepingCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            CreateFor(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => CreateFor(scene);

        private static void CreateFor(Scene scene)
        {
            ParkedVehicleSimulationRadius manager = null;
            foreach (RCCVehicleAdapter adapter in FindObjectsByType<RCCVehicleAdapter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (adapter.gameObject.scene != scene) continue;
                RCC_CarControllerV3 car = adapter.GetComponent<RCC_CarControllerV3>();
                if (car == null) continue;
                if (manager == null)
                {
                    foreach (ParkedVehicleSimulationRadius existing in FindObjectsByType<ParkedVehicleSimulationRadius>(FindObjectsSortMode.None))
                        if (existing.gameObject.scene == scene) return;
                    var go = new GameObject("Parked Vehicle Simulation Radius");
                    SceneManager.MoveGameObjectToScene(go, scene);
                    manager = go.AddComponent<ParkedVehicleSimulationRadius>();
                }
                manager.cars.Add(new Parked { Adapter = adapter, Car = car });
            }
        }

        private void Awake() => nextCheck = Time.time + StartDelay;

        private void Update()
        {
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + CheckInterval;
            int sleeping = 0;
            for (int i = cars.Count - 1; i >= 0; i--)
            {
                Parked p = cars[i];
                if (p.Adapter == null)
                {
                    cars.RemoveAt(i);
                    continue;
                }
                if (!p.Adapter.isActiveAndEnabled) continue;

                bool free = !p.Adapter.IsOccupied && !p.Adapter.IsRolling && !p.Car.canControl && !p.Car.externalController;
                bool idle = free && !p.Car.engineRunning && p.Adapter.CurrentSpeed < 0.3f;
                SetIdle(p, idle || p.Adapter.IsDormant);
                if (p.Adapter.IsDormant) sleeping++;
            }
            SleepingCount = sleeping;
        }

        private static void SetIdle(Parked p, bool idle)
        {
            if (idle == p.Idle) return;
            p.Idle = idle;
            if (idle)
            {
                // RCC creates its sounds and wheel smoke in Awake / Start.
                p.Audio = p.Car.GetComponentsInChildren<AudioSource>(true);
                p.Particles = p.Car.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < p.Audio.Length; i++) if (p.Audio[i] != null && p.Audio[i].loop && p.Audio[i].isPlaying) p.Audio[i].Pause();
                for (int i = 0; i < p.Particles.Length; i++) if (p.Particles[i] != null && p.Particles[i].isPlaying && p.Particles[i].particleCount == 0) p.Particles[i].Pause(false);
                return;
            }
            for (int i = 0; i < p.Audio.Length; i++)
            {
                AudioSource a = p.Audio[i];
                if (a == null || !a.loop || !a.isActiveAndEnabled) continue;
                a.UnPause();
                if (!a.isPlaying && a.clip != null) a.Play(); // sleeping stopped it
            }
            for (int i = 0; i < p.Particles.Length; i++) if (p.Particles[i] != null && p.Particles[i].isPaused) p.Particles[i].Play(false);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < cars.Count; i++)
                if (cars[i].Adapter != null) SetIdle(cars[i], false);
        }
    }
}
