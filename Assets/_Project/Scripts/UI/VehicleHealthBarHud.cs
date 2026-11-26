using System.Collections.Generic;
using OpenworldCrime.Player;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.UI
{
    /// <summary>
    /// Small pink health bars floating just above the roof of damaged cars. A bar appears when the player's car takes damage or the
    /// player damages another car (shots, or ramming it with his car), stays for Visible Seconds after the last hit, then fades.
    /// Listens to <see cref="VehicleHealth.OnAnyDamaged"/> (no polling of cars). The bars are a fixed pool of
    /// <see cref="VehicleHealthBarView"/> children of this object on the HUD canvas (Screen Space Overlay), placed at the car's
    /// projected roof point in LateUpdate; the component switches itself off while no bar is shown. No allocations per frame.
    /// A HUD widget script (not a manager): it only drives its own bars.
    /// </summary>
    [DisallowMultipleComponent]
    public class VehicleHealthBarHud : MonoBehaviour
    {
        private class Entry
        {
            public VehicleHealthBarView View;
            public VehicleHealth Car;
            public Transform CarTransform;
            public float RoofHeight;
            public float ShowTimer;
            public float Chip;
            public float ChipHoldTimer;
            public float LastShown;
        }

        [Header("References")]
        [Tooltip("Vehicle interaction of every playable character (character switching): tells which car is the player's and who he " +
                 "is (hits from him or his car count). Only the active one counts.")]
        [SerializeField] private PlayerVehicleInteraction[] players = new PlayerVehicleInteraction[0];
        [Tooltip("Pooled bars (children of this object). More hit cars at once than bars: the oldest bar moves to the new car.")]
        [SerializeField] private VehicleHealthBarView[] bars = new VehicleHealthBarView[0];

        [Header("Timing")]
        [Tooltip("Seconds a bar stays fully visible after the last hit.")]
        [SerializeField, Min(0.1f)] private float visibleSeconds = 2.2f;
        [Tooltip("Seconds it then takes to fade out.")]
        [SerializeField, Min(0.05f)] private float fadeSeconds = 0.5f;
        [Tooltip("Seconds the light 'chip' waits before it slides down to the new health.")]
        [SerializeField, Min(0f)] private float chipHoldSeconds = 0.35f;
        [Tooltip("Share of full health per second the chip slides down.")]
        [SerializeField, Min(0.05f)] private float chipSpeed = 0.8f;

        [Header("Placement")]
        [Tooltip("Metres above the top of the car's body.")]
        [SerializeField, Min(0f)] private float roofClearance = 0.12f;
        [Tooltip("Bars of cars further from the camera than this (m) are hidden.")]
        [SerializeField, Min(5f)] private float maxDistance = 70f;
        [Tooltip("Camera distance (m) at which the bar has its full size; further away it shrinks down to Min Scale.")]
        [SerializeField, Min(1f)] private float fullSizeDistance = 12f;
        [SerializeField, Range(0.2f, 1f)] private float minScale = 0.6f;

        private Entry[] entries = new Entry[0];
        private int visibleCount;
        private static readonly List<Collider> colliderBuffer = new List<Collider>(32);

        private void Awake()
        {
            entries = new Entry[bars.Length];
            for (int i = 0; i < bars.Length; i++)
            {
                entries[i] = new Entry { View = bars[i] };
                if (bars[i] != null) bars[i].SetVisible(false);
            }
            if (players.Length == 0)
                Debug.LogWarning($"[{nameof(VehicleHealthBarHud)}] No players (PlayerVehicleInteraction) on '{name}': no car health bars will show.", this);
            if (bars.Length == 0) Debug.LogWarning($"[{nameof(VehicleHealthBarHud)}] No bar views on '{name}'. Run Tools > OpenworldCrime > Vehicles > Car Polish > Setup HUD.", this);
            VehicleHealth.OnAnyDamaged += HandleAnyDamaged;
            enabled = false;
        }

        // Subscribed for the object's lifetime (the component itself switches off while no bar shows).
        private void OnDestroy()
        {
            VehicleHealth.OnAnyDamaged -= HandleAnyDamaged;
        }

        private void HandleAnyDamaged(VehicleHealth car, float amount, Transform sender)
        {
            // The component may be switched off (nothing to show); the HUD object itself must be on.
            if (car == null || bars.Length == 0 || !gameObject.activeInHierarchy) return;
            if (!Concerns(car, sender)) return;

            Entry entry = Find(car);
            if (entry == null)
            {
                entry = Free();
                Bind(entry, car);
            }
            entry.ShowTimer = visibleSeconds + fadeSeconds;
            entry.ChipHoldTimer = chipHoldSeconds;
            entry.LastShown = Time.time;
            enabled = true;
        }

        // The active character's own car, or a car he shot / rammed with his car.
        private bool Concerns(VehicleHealth car, Transform sender)
        {
            for (int i = 0; i < players.Length; i++)
            {
                PlayerVehicleInteraction player = players[i];
                if (player == null || !player.isActiveAndEnabled) continue;
                Transform playerCar = player.IsInVehicle && player.CurrentVehicle != null ? player.CurrentVehicle.transform : null;
                if (playerCar != null && car.transform == playerCar) return true;
                if (sender == null) continue;
                Transform playerRoot = player.transform;
                if (sender == playerRoot || sender.IsChildOf(playerRoot)) return true;
                if (playerCar != null && (sender == playerCar || sender.IsChildOf(playerCar))) return true;
            }
            return false;
        }

        private void Bind(Entry entry, VehicleHealth car)
        {
            entry.Car = car;
            entry.CarTransform = car.transform;
            entry.RoofHeight = MeasureRoof(entry.CarTransform) + roofClearance;
            entry.Chip = car.MaxHealth > 0f ? Mathf.Clamp01((car.CurrentHealth + 1f) / car.MaxHealth) : 1f;
            if (entry.View != null)
            {
                entry.View.SetAlpha(1f);
                entry.View.SetValues(car.CurrentHealth / car.MaxHealth, entry.Chip);
            }
        }

        // Once per bind (not per frame): top of the car's solid body above its root. The body colliders, not the renderers:
        // light cones, flares and hidden helper meshes reached far above the roof and lifted the bar off the car.
        private static float MeasureRoof(Transform car)
        {
            colliderBuffer.Clear();
            car.GetComponentsInChildren(false, colliderBuffer);
            float top = float.MinValue;
            for (int i = 0; i < colliderBuffer.Count; i++)
            {
                Collider c = colliderBuffer[i];
                if (c.isTrigger || c is WheelCollider || !c.enabled) continue;
                top = Mathf.Max(top, c.bounds.max.y);
            }
            colliderBuffer.Clear();
            return top > float.MinValue ? Mathf.Clamp(top - car.position.y, 0.8f, 4f) : 1.5f;
        }

        private Entry Find(VehicleHealth car)
        {
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].Car == car && entries[i].ShowTimer > 0f) return entries[i];
            return null;
        }

        private Entry Free()
        {
            Entry oldest = entries[0];
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].ShowTimer <= 0f) return entries[i];
                if (entries[i].LastShown < oldest.LastShown) oldest = entries[i];
            }
            return oldest;
        }

        private void LateUpdate()
        {
            Camera view = Camera.main;
            float dt = Time.deltaTime;
            visibleCount = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                Entry entry = entries[i];
                if (entry.ShowTimer <= 0f) continue;
                entry.ShowTimer -= dt;
                bool alive = entry.Car != null && entry.CarTransform.gameObject.activeInHierarchy && !entry.Car.IsDestroyed;
                if (!alive || entry.ShowTimer <= 0f || entry.View == null)
                {
                    Release(entry);
                    continue;
                }
                visibleCount++;

                float health = entry.Car.CurrentHealth / entry.Car.MaxHealth;
                if (entry.ChipHoldTimer > 0f) entry.ChipHoldTimer -= dt;
                else entry.Chip = Mathf.MoveTowards(entry.Chip, health, chipSpeed * dt);
                entry.View.SetValues(health, entry.Chip);
                entry.View.SetAlpha(Mathf.Clamp01(entry.ShowTimer / fadeSeconds));
                Place(entry, view);
            }
            if (visibleCount == 0) enabled = false;
        }

        private void Place(Entry entry, Camera view)
        {
            if (view == null)
            {
                entry.View.SetVisible(false);
                return;
            }
            Vector3 point = entry.CarTransform.position + Vector3.up * entry.RoofHeight;
            Vector3 screen = view.WorldToScreenPoint(point);
            bool onScreen = screen.z > 0.3f && screen.z < maxDistance;
            entry.View.SetVisible(onScreen);
            if (!onScreen) return;
            RectTransform rect = entry.View.Root;
            rect.position = new Vector3(screen.x, screen.y, 0f);
            float scale = Mathf.Lerp(1f, minScale, Mathf.InverseLerp(fullSizeDistance, maxDistance, screen.z));
            rect.localScale = new Vector3(scale, scale, 1f);
        }

        private static void Release(Entry entry)
        {
            entry.ShowTimer = 0f;
            entry.Car = null;
            entry.CarTransform = null;
            if (entry.View != null) entry.View.SetVisible(false);
        }

#if UNITY_EDITOR
        public void EditorSetup(PlayerVehicleInteraction[] interactions, VehicleHealthBarView[] views)
        {
            players = interactions;
            bars = views;
        }
#endif
    }
}
