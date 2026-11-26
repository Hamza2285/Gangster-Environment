using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// Street lighting of the realtime-lit city (scene system under ///Manager///, not a Singleton), driven by the night blend of
    /// <see cref="CityDayNightController"/> (Hamza's, untouched). Replaces the baked lamp pools:
    /// - every lamp: a soft fake light pool on the ground (one mesh per city cell, "OpenworldCrime/Street Light Pool"), on only
    ///   while it is dark;
    /// - the few lamps nearest the camera: pooled realtime point lights (no shadows) that light people, cars and walls. A light
    ///   fades out before it moves to another lamp and fades in there, so nothing pops. Their number stays within URP's
    ///   per-object limit. The controller's own four nearby lights are off (its presets' Nearby Street Lights is 0).
    /// By day: one compare per frame.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(610)]
    public class NightStreetLights : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CityDayNightController dayNight;
        [Tooltip("Pooled realtime lights (4 at most: URP's per-object limit on mobile).")]
        [SerializeField] private Light[] lights = new Light[0];
        [Tooltip("Ground pool meshes (built by Tools > OpenworldCrime > Lighting > Set Up Realtime Bright Lighting).")]
        [SerializeField] private Renderer[] pools = new Renderer[0];

        [Header("Look")]
        [SerializeField] private Color lampColor = new Color(1f, 0.74f, 0.42f);
        [Tooltip("Ground pool tint; stronger than the lamp colour because it multiplies the blue night ground.")]
        [SerializeField] private Color poolColor = new Color(1f, 0.62f, 0.18f);
        [Tooltip("Brightness of the ground pools at full night.")]
        [SerializeField, Range(0f, 6f)] private float poolStrength = 2.2f;
        [SerializeField, Min(0f)] private float lightIntensity = 22f;
        [SerializeField, Min(1f)] private float lightRange = 18f;
        [Tooltip("Night blend window over which the lamps come on (0 day - 1 night).")]
        [SerializeField] private Vector2 onWindow = new Vector2(0.3f, 0.8f);

        [Header("Nearest lamps")]
        [SerializeField, Min(5f)] private float maxDistance = 55f;
        [Tooltip("Seconds a light takes to fade in / out when it changes lamp.")]
        [SerializeField, Min(0.05f)] private float fadeSeconds = 0.5f;
        [SerializeField, Min(0.05f)] private float searchInterval = 0.25f;

        private static readonly int PoolId = Shader.PropertyToID("_OWCStreetPool");

        private int[] slotLamp;      // lamp a light stands at (-1 none)
        private float[] slotWeight;
        private int[] wanted;        // nearest lamps (-1 none)
        private float[] wantedDistance;
        private float appliedNight = -1f;
        private float nextSearch;
        private bool lit;
        private bool cityActive = true;
        private Transform cam;

        private void Awake()
        {
            if (dayNight == null)
            {
                Debug.LogError($"[{nameof(NightStreetLights)}] No CityDayNightController on '{name}'.", this);
                enabled = false;
                return;
            }
            slotLamp = new int[lights.Length];
            slotWeight = new float[lights.Length];
            wanted = new int[lights.Length];
            wantedDistance = new float[lights.Length];
            for (int i = 0; i < slotLamp.Length; i++) slotLamp[i] = wanted[i] = -1;
        }

        private void OnEnable()
        {
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
            cityActive = SceneManager.GetActiveScene() == gameObject.scene;
            appliedNight = -1f;
            SetLit(false);
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
            SetLit(false);
        }

        private void HandleActiveSceneChanged(Scene previous, Scene next)
        {
            cityActive = next == gameObject.scene;
            appliedNight = -1f;
        }

        private void LateUpdate()
        {
            float night = cityActive ? On(dayNight.NightBlend) : 0f;
            if (Mathf.Abs(night - appliedNight) > 0.001f || (night <= 0f && lit))
            {
                appliedNight = night;
                if (lit != night > 0f) SetLit(night > 0f);
                Color c = poolColor * (poolStrength * night);
                Shader.SetGlobalVector(PoolId, new Vector4(c.r, c.g, c.b, 1f));
            }
            if (!lit) return;
            if (Time.unscaledTime >= nextSearch)
            {
                nextSearch = Time.unscaledTime + searchInterval;
                FindNearest();
            }
            float step = Time.unscaledDeltaTime / fadeSeconds;
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] == null) continue;
                bool keep = slotLamp[i] >= 0 && IndexOf(wanted, slotLamp[i]) >= 0;
                slotWeight[i] = Mathf.MoveTowards(slotWeight[i], keep ? 1f : 0f, step);
                if (!keep && slotWeight[i] <= 0f) TakeFreeLamp(i);
                lights[i].intensity = lightIntensity * night * slotWeight[i];
                bool on = slotLamp[i] >= 0 && slotWeight[i] > 0f;
                if (lights[i].enabled != on) lights[i].enabled = on;
            }
        }

        private float On(float nightBlend)
        {
            float k = Mathf.InverseLerp(onWindow.x, onWindow.y, nightBlend);
            return k * k * (3f - 2f * k);
        }

        private void TakeFreeLamp(int slot)
        {
            slotLamp[slot] = -1;
            for (int w = 0; w < wanted.Length; w++)
            {
                if (wanted[w] < 0 || IndexOf(slotLamp, wanted[w]) >= 0) continue;
                slotLamp[slot] = wanted[w];
                lights[slot].transform.position = dayNight.streetLightPositions[wanted[w]];
                return;
            }
        }

        private void FindNearest()
        {
            for (int i = 0; i < wanted.Length; i++)
            {
                wanted[i] = -1;
                wantedDistance[i] = maxDistance * maxDistance;
            }
            Vector3[] lamps = dayNight.streetLightPositions;
            if (lamps == null) return;
            if (cam == null || !cam.gameObject.activeInHierarchy)
            {
                Camera c = Camera.main;
                cam = c != null ? c.transform : null;
                if (cam == null) return;
            }
            Vector3 centre = cam.position + Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized * 8f;
            for (int l = 0; l < lamps.Length; l++)
            {
                float d = (lamps[l] - centre).sqrMagnitude;
                if (d >= maxDistance * maxDistance) continue;
                // A lamp already lit counts as a little nearer, so two equally near lamps do not swap back and forth.
                if (IndexOf(slotLamp, l) >= 0) d *= 0.8f;
                for (int i = 0; i < wanted.Length; i++)
                {
                    if (d >= wantedDistance[i]) continue;
                    for (int j = wanted.Length - 1; j > i; j--)
                    {
                        wanted[j] = wanted[j - 1];
                        wantedDistance[j] = wantedDistance[j - 1];
                    }
                    wanted[i] = l;
                    wantedDistance[i] = d;
                    break;
                }
            }
        }

        private static int IndexOf(int[] array, int value)
        {
            for (int i = 0; i < array.Length; i++) if (array[i] == value) return i;
            return -1;
        }

        private void SetLit(bool on)
        {
            lit = on;
            for (int i = 0; i < pools.Length; i++)
                if (pools[i] != null) pools[i].enabled = on;
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] == null) continue;
                lights[i].color = lampColor;
                lights[i].range = lightRange;
                if (on) continue;
                lights[i].enabled = false;
                if (slotWeight == null) continue;
                slotWeight[i] = 0f;
                slotLamp[i] = -1;
            }
        }

#if UNITY_EDITOR
        public void EditorSetup(CityDayNightController controller, Light[] pooledLights, Renderer[] poolRenderers)
        {
            dayNight = controller;
            lights = pooledLights;
            pools = poolRenderers;
        }
#endif
    }
}
