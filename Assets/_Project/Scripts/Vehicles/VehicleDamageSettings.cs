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
        [Min(1f)] public float maxHealth = 1200f;
        [Tooltip("Share of health at which the car starts smoking.")]
        [Range(0f, 1f)] public float smokeAtHealth = 0.5f;
        [Tooltip("Share of health at which the light smoke turns thick and dark (between Smoke At Health and Fire At Health).")]
        [Range(0f, 1f)] public float heavySmokeAtHealth = 0.35f;
        [Tooltip("Share of health at which the car catches fire.")]
        [Range(0f, 1f)] public float fireAtHealth = 0.2f;
        [Tooltip("Health lost per second while on fire, so a burning car always ends up exploding.")]
        [Min(0f)] public float burnDamagePerSecond = 30f;

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
        [Min(0f)] public float explosionCarDamage = 600f;
        [Tooltip("Impulse (per kg, m/s) on the wreck and on cars in the blast.")]
        [Min(0f)] public float explosionCarSpeed = 4f;
        [Tooltip("Speed (m/s) at which the blast throws pedestrians at the centre.")]
        [Min(0f)] public float explosionThrowSpeed = 7f;
        [Tooltip("Share of the player's maximum health lost if he is inside when the car blows up (1 = dead).")]
        [Range(0f, 1f)] public float playerInsideDamage = 0.7f;
        [Tooltip("Layers searched for victims: the player, ragdoll / hitbox bones and vehicle hitboxes.")]
        public LayerMask explosionLayers = (1 << 9) | (1 << 15) | (1 << 21);
        [Tooltip("Upward speed (m/s) the exploding car itself is thrown with, so it hops and lands as a wreck. ~4.5 = about 1 m.")]
        [Min(0f)] public float explosionHopSpeed = 4.5f;
        [Tooltip("Random spin (rad/s) around the car's length and width when it blows up, so it lands tilted.")]
        [Min(0f)] public float explosionSpin = 1.2f;
        [Tooltip("Camera shake (m) for a blast right at the camera; falls off to nothing at Shake Radius.")]
        [Min(0f)] public float shakeAmplitude = 0.35f;
        [Min(0f)] public float shakeRadius = 45f;
        [Min(0f)] public float shakeDuration = 0.7f;

        [Header("Wreck")]
        [Tooltip("Colour multiplied onto the car's materials (cars without a destroyed model).")]
        public Color burntTint = new Color(0.16f, 0.15f, 0.14f, 1f);
        [Tooltip("Seconds a wreck stays at least.")]
        [Min(0f)] public float wreckMinimumTime = 8f;
        [Tooltip("A wreck closer than this (m) to the player is kept, so it never vanishes right behind him.")]
        [Min(0f)] public float wreckKeepDistance = 15f;
        [Tooltip("Seconds the fire keeps burning on the wreck before it dies down to smoke.")]
        [Min(0f)] public float wreckFireTime = 6f;
        [Tooltip("Seconds until the smoke of the wreck stops.")]
        [Min(0f)] public float wreckSmokeTime = 25f;

        [Header("Effects (pooled, see VehicleEffectPool)")]
        [Tooltip("Offset of smoke and fire from the car's effect anchor, in the car's space (up, and back from the front onto the hood).")]
        public Vector3 effectOffset = new Vector3(0f, 0.1f, -0.35f);
        [Tooltip("Light grey smoke from the engine (Smoke At Health).")]
        public GameObject smokeEffect;
        [Tooltip("Thick dark smoke (Heavy Smoke At Health) and the smoke of the wreck once its fire is out. Empty = Smoke Effect.")]
        public GameObject heavySmokeEffect;
        [Tooltip("Engine fire with its own smoke column (Fire At Health and briefly on the wreck).")]
        public GameObject fireEffect;
        public GameObject explosionEffect;
        [Tooltip("Seconds the explosion effect plays (its smoke column keeps rising) before it fades out over Effect Fade Time.")]
        [Min(0.5f)] public float explosionEffectLifetime = 5f;
        [Tooltip("Played through the explosion effect's PooledVehicleEffect one-shot AudioSource.")]
        public AudioClip explosionSound;
        [Tooltip("Seconds a stopped smoke or fire keeps its last particles before going back to the pool.")]
        [Min(0f)] public float effectFadeTime = 3f;

        [Header("Performance")]
        [Tooltip("Cars burning down at once. Another car reaching the fire stage over this explodes straight away.")]
        [Min(1)] public int maxBurningCars = 3;
        [Tooltip("Fire effects at once (burning cars and burning wrecks). Over this a car or wreck only smokes.")]
        [Min(1)] public int maxFireEffects = 4;
        [Tooltip("Smoke effects at once, light and heavy each. Over this a car simply does not smoke.")]
        [Min(1)] public int maxSmokeEffects = 6;
        [Tooltip("Explosion effects at once. Over this a blast still does its damage, without the effect.")]
        [Min(1)] public int maxExplosionEffects = 3;
        [Tooltip("Instances of each effect created while the scene loads, so the first fire does not hitch.")]
        [Min(0)] public int prewarmCount = 1;

        private void OnValidate()
        {
            heavySmokeAtHealth = Mathf.Clamp(heavySmokeAtHealth, fireAtHealth, smokeAtHealth);
        }
    }
}
