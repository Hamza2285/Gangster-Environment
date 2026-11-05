using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Shared tuning of vehicle damage: health, crash damage, burning stages, explosion and wreck cleanup. One asset is shared by
    /// every car (Assets/_Project/Data/Vehicles/VehicleDamageSettings.asset). Read-only at runtime: the state lives on <see cref="VehicleHealth"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "OpenworldCrime/Vehicles/Vehicle Damage Settings", fileName = "VehicleDamageSettings")]
    public class VehicleDamageSettings : ScriptableObject
    {
        [Header("Health")]
        [Min(1f)] public float maxHealth = 400f;
        [Tooltip("Share of health at which the car starts smoking.")]
        [Range(0f, 1f)] public float smokeAtHealth = 0.5f;
        [Tooltip("Share of health at which the car catches fire.")]
        [Range(0f, 1f)] public float fireAtHealth = 0.2f;
        [Tooltip("Health lost per second while on fire, so a burning car always ends up exploding.")]
        [Min(0f)] public float burnDamagePerSecond = 15f;

        [Header("Crash Damage")]
        [Tooltip("Impacts slower than this (m/s, along the contact normal) do no damage. 12 m/s = 43 km/h.")]
        [Min(0f)] public float crashMinimumSpeed = 12f;
        [Tooltip("Damage per m/s of impact speed above the minimum.")]
        [Min(0f)] public float crashDamagePerSpeed = 14f;
        [Tooltip("Seconds in which further impacts are ignored (one crash, one hit).")]
        [Min(0f)] public float crashCooldown = 0.4f;
        [Tooltip("Layers that damage a car in a crash (world and other vehicles; characters never do).")]
        public LayerMask crashLayers = (1 << 0) | (1 << 8) | (1 << 13) | (1 << 19) | (1 << 20);

        [Header("Explosion")]
        [Min(0.5f)] public float explosionRadius = 9f;
        [Tooltip("Damage to characters at the centre (falls off to 0 at the radius).")]
        [Min(0f)] public float explosionDamage = 150f;
        [Tooltip("Damage to other cars at the centre (falls off to 0 at the radius).")]
        [Min(0f)] public float explosionCarDamage = 320f;
        [Tooltip("Impulse (per kg, m/s) on the wreck and on cars in the blast.")]
        [Min(0f)] public float explosionCarSpeed = 4f;
        [Tooltip("Speed (m/s) at which the blast throws pedestrians at the centre.")]
        [Min(0f)] public float explosionThrowSpeed = 7f;
        [Tooltip("Share of the player's maximum health lost if he is inside when the car blows up (1 = dead).")]
        [Range(0f, 1f)] public float playerInsideDamage = 0.7f;
        [Tooltip("Layers searched for victims: the player, ragdoll / hitbox bones and vehicle hitboxes.")]
        public LayerMask explosionLayers = (1 << 9) | (1 << 15) | (1 << 21);

        [Header("Wreck")]
        [Tooltip("Colour multiplied onto the car's materials (cars without a destroyed model).")]
        public Color burntTint = new Color(0.16f, 0.15f, 0.14f, 1f);
        [Tooltip("Seconds a wreck stays at least.")]
        [Min(0f)] public float wreckMinimumTime = 8f;
        [Tooltip("A wreck closer than this (m) to the player is kept, so it never vanishes right behind him.")]
        [Min(0f)] public float wreckKeepDistance = 15f;
        [Tooltip("Seconds until the smoke of the wreck stops.")]
        [Min(0f)] public float wreckSmokeTime = 25f;

        [Header("Effects")]
        public GameObject smokeEffect;
        public GameObject fireEffect;
        public GameObject explosionEffect;
        [Min(0.5f)] public float explosionEffectLifetime = 8f;
        public AudioClip explosionSound;
    }
}
