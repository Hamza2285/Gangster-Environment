using System;
using System.Collections;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>Damage stage of a car.</summary>
    public enum VehicleDamageStage { Healthy, Smoking, HeavySmoke, Burning, Destroyed }

    /// <summary>
    /// Health of a car (drivable, traffic and police alike; lives on the root next to the Rigidbody). Bullets reach it through
    /// <see cref="VehicleHitZone"/>, crashes through the collision callback. Stages: light smoke, heavy dark smoke, then engine fire
    /// (which keeps burning the car down, without raising <see cref="OnDamaged"/>), then the explosion (<see cref="VehicleExplosion"/>);
    /// the fire stays briefly on the wreck, then dies down to smoke. Effects come from <see cref="VehicleEffectPool"/> and only follow
    /// the effect anchor (never parented to the car). A destroyed car swaps to its destroyed model if it has
    /// one, otherwise its materials are darkened (per renderer, no material copies); it can no longer be driven or entered and is
    /// cleaned up once off camera (<see cref="OnWreckCleanup"/>: the owner decides what that means, traffic returns to the iTS pool).
    /// Resets itself when re-enabled (iTS pooling, pre-warmed drivables). Nothing runs per frame: coroutines only while burning or wrecked.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleHealth : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BurntId = Shader.PropertyToID("_Burnt");

        [SerializeField] private VehicleDamageSettings settings;
        [Tooltip("Destroyed version of this car (visual only). Empty = the car's own model is darkened and kept as the wreck.")]
        [SerializeField] private GameObject destroyedModelPrefab;
        [Tooltip("Where smoke and fire sit (the engine). Empty = the root.")]
        [SerializeField] private Transform effectAnchor;
        [Tooltip("Lights, exhausts and the like, switched off on the wreck.")]
        [SerializeField] private GameObject[] disableWhenWrecked = new GameObject[0];

        /// <summary>Raised once when a car blows up (any car). Used by the player's side to get out of it.</summary>
        public static event Action<VehicleHealth> OnAnyDestroyed;
        /// <summary>Raised when this car takes damage. Args: amount, who did it (may be null).</summary>
        public event Action<float, Transform> OnDamaged;
        /// <summary>Raised when the stage changes (never for the reset of a pooled car).</summary>
        public event Action<VehicleDamageStage> OnStageChanged;
        /// <summary>Raised once when this car blows up.</summary>
        public event Action OnDestroyedWreck;
        /// <summary>Raised when the wreck should go: off camera and old enough. Without a listener the object is removed.</summary>
        public event Action OnWreckCleanup;

        public float CurrentHealth { get; private set; }
        public VehicleDamageStage Stage { get; private set; }
        public bool IsDestroyed => Stage == VehicleDamageStage.Destroyed;

        /// <summary>Takes no damage while set (e.g. the hero car during a cutscene, beside a blast it is part of).</summary>
        public bool Invulnerable { get; set; }
        public float MaxHealth => settings != null ? settings.maxHealth : 1f;
        public VehicleDamageSettings Settings => settings;
        /// <summary>
        /// The last one who hurt this car from outside: a shooter, or a car that rammed it (never the car itself, its fire or a static
        /// wall). Its explosion blames him, so the damage it does can be reported as his (chains of cars included).
        /// </summary>
        public Transform LastAttacker { get; private set; }

        // Cars in the Burning stage right now (Settings' Max Burning Cars).
        private static int burningCars;

        private Rigidbody body;
        private AudioSource explosionAudio;
        private Renderer[] renderers = new Renderer[0];
        private bool[] rendererWasEnabled = new bool[0];
        private PooledVehicleEffect smokeEffect;
        private PooledVehicleEffect fireEffect;
        private bool countedBurning;
        private GameObject destroyedInstance;
        private Coroutine burnRoutine;
        private Coroutine wreckRoutine;
        private float lastCrashTime = -100f;
        private bool darkened;
        private bool valid;
        private MaterialPropertyBlock block;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            valid = settings != null;
            if (!valid)
            {
                Debug.LogError($"[{nameof(VehicleHealth)}] No Vehicle Damage Settings on '{name}'. It cannot be damaged.", this);
                return;
            }
            renderers = GetComponentsInChildren<Renderer>(true);
            rendererWasEnabled = new bool[renderers.Length];
            VehicleEffectPool.Prewarm(settings.smokeEffect, settings.prewarmCount);
            VehicleEffectPool.Prewarm(settings.heavySmokeEffect, settings.prewarmCount);
            VehicleEffectPool.Prewarm(settings.fireEffect, settings.prewarmCount);
            VehicleEffectPool.Prewarm(settings.explosionEffect, settings.prewarmCount);
        }

        // iTS re-enables pooled cars and the takeover manager re-enables pre-warmed ones: each use starts as a fresh car.
        private void OnEnable()
        {
            if (!valid) return;
            ResetState();
        }

        // Pooled away (iTS, police) or destroyed: the effects go back at once, a burning car stops counting.
        private void OnDisable()
        {
            burnRoutine = null;
            wreckRoutine = null;
            SetBurningCounted(false);
            ReleaseEffect(ref smokeEffect);
            ReleaseEffect(ref fireEffect);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!valid || IsDestroyed || ((1 << collision.gameObject.layer) & settings.crashLayers) == 0) return;
            if (Time.time - lastCrashTime < settings.crashCooldown || collision.contactCount == 0) return;

            // The speed lost along the contact normal: scraping along a wall or a kerb does not count.
            float speed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, collision.GetContact(0).normal));
            if (speed < settings.crashMinimumSpeed) return;
            lastCrashTime = Time.time;
            LastDamageWasCrash = true;
            // Only a moving body (another car) is someone to blame; walls and kerbs are not.
            if (collision.rigidbody != null) RememberAttacker(collision.transform);
            ApplyHealthDamage((speed - settings.crashMinimumSpeed) * settings.crashDamagePerSpeed, collision.transform);
        }

        /// <summary>True while <see cref="OnDamaged"/> reports crash damage (a collision), false for bullets, fire and explosions.</summary>
        public bool LastDamageWasCrash { get; private set; }

        /// <summary>Takes damage (bullets, other explosions). Crashes come through the collision callback.</summary>
        public void ApplyDamage(float amount, Transform sender)
        {
            LastDamageWasCrash = false;
            RememberAttacker(sender);
            ApplyHealthDamage(amount, sender);
        }

        private void RememberAttacker(Transform sender)
        {
            if (!valid || IsDestroyed || sender == null || sender == transform || sender.IsChildOf(transform)) return;
            LastAttacker = sender;
        }

        private void ApplyHealthDamage(float amount, Transform sender)
        {
            if (!valid || IsDestroyed || amount <= 0f || Invulnerable) return;
            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
            OnDamaged?.Invoke(amount, sender);
            UpdateStage();
        }

        /// <summary>Carries a taken-over car's damage to the drivable car that replaces it.</summary>
        public void CopyDamageFrom(VehicleHealth other)
        {
            if (!valid || other == null || !other.valid || other.IsDestroyed) return;
            CurrentHealth = Mathf.Min(CurrentHealth, other.CurrentHealth);
            if (other.LastAttacker != null) LastAttacker = other.LastAttacker;
            UpdateStage();
        }

        /// <summary>Pushes the car away from an explosion.</summary>
        public void Shove(Vector3 center, float speed)
        {
            if (body == null || body.isKinematic || speed <= 0f) return;
            Vector3 direction = transform.position - center;
            direction.y = 0f;
            direction = (direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward) + Vector3.up * 0.5f;
            body.AddForce(direction.normalized * speed, ForceMode.VelocityChange);
            body.AddTorque(UnityEngine.Random.onUnitSphere * speed * 0.2f, ForceMode.VelocityChange);
        }

        private void UpdateStage()
        {
            float share = CurrentHealth / settings.maxHealth;
            if (CurrentHealth <= 0f)
            {
                Explode();
                return;
            }
            VehicleDamageStage wanted = share <= settings.fireAtHealth ? VehicleDamageStage.Burning
                : share <= settings.heavySmokeAtHealth ? VehicleDamageStage.HeavySmoke
                : share <= settings.smokeAtHealth ? VehicleDamageStage.Smoking : VehicleDamageStage.Healthy;
            if (wanted <= Stage) return;
            SetStage(wanted);
        }

        private void SetStage(VehicleDamageStage stage)
        {
            if (stage == VehicleDamageStage.Burning && !countedBurning && burningCars >= settings.maxBurningCars)
            {
                // Enough cars burning down already: this one goes up at once instead of adding another fire.
                CurrentHealth = 0f;
                Explode();
                return;
            }
            Stage = stage;
            if (stage == VehicleDamageStage.Smoking) SwapSmoke(settings.smokeEffect);
            if (stage == VehicleDamageStage.HeavySmoke) SwapSmoke(HeavySmokePrefab);
            if (stage == VehicleDamageStage.Burning)
            {
                // The fire brings its own smoke column; without a free fire effect the heavy smoke stays.
                if (SpawnEffect(ref fireEffect, settings.fireEffect, settings.maxFireEffects)) FadeEffect(ref smokeEffect);
                else SwapSmoke(HeavySmokePrefab);
                SetBurningCounted(true);
                if (burnRoutine == null && isActiveAndEnabled) burnRoutine = StartCoroutine(BurnRoutine());
            }
            OnStageChanged?.Invoke(stage);
        }

        private GameObject HeavySmokePrefab => settings.heavySmokeEffect != null ? settings.heavySmokeEffect : settings.smokeEffect;

        // Self-inflicted: it lowers the health but is no attack (no OnDamaged, the last attacker stays who lit the fire).
        private IEnumerator BurnRoutine()
        {
            const float interval = 0.25f;
            var wait = new WaitForSeconds(interval);
            while (!IsDestroyed)
            {
                yield return wait;
                if (IsDestroyed) break;
                CurrentHealth = Mathf.Max(0f, CurrentHealth - settings.burnDamagePerSecond * interval);
                UpdateStage();
            }
            burnRoutine = null;
        }

        private void SetBurningCounted(bool counted)
        {
            if (counted == countedBurning) return;
            countedBurning = counted;
            burningCars = Mathf.Max(0, burningCars + (counted ? 1 : -1));
        }

        private void Explode()
        {
            Stage = VehicleDamageStage.Destroyed;
            SetBurningCounted(false);
            Vector3 center = effectAnchor != null ? effectAnchor.position : body.worldCenterOfMass;

            PooledVehicleEffect blast = VehicleEffectPool.Spawn(settings.explosionEffect, center, settings.maxExplosionEffects);
            if (blast != null)
            {
                blast.PlayOneShot(settings.explosionSound);
                blast.FadeOutAfter(settings.explosionEffectLifetime, settings.effectFadeTime);
            }
            ExplosionCameraShake.Shake(center, settings.shakeAmplitude, settings.shakeRadius, settings.shakeDuration);

            ShowWreck();
            // The wreck burns on for a while (Wreck Fire Time), then smokes: see WreckRoutine.
            if (fireEffect == null) SpawnEffect(ref fireEffect, settings.fireEffect, settings.maxFireEffects);
            if (fireEffect != null) FadeEffect(ref smokeEffect);
            else SwapSmoke(HeavySmokePrefab);

            // The player gets out first (OnAnyDestroyed), so the blast below finds him on foot and only the inside damage counts.
            OnStageChanged?.Invoke(Stage);
            OnDestroyedWreck?.Invoke();
            OnAnyDestroyed?.Invoke(this);

            Hop();
            if (isActiveAndEnabled)
            {
                // A frame later, so a chain of cars never recurses inside one physics callback.
                StartCoroutine(DetonateNextFrame(center));
                wreckRoutine = StartCoroutine(WreckRoutine());
            }
        }

        // The blast lifts its own car: up with a little sideways drift and a random tilt, so it hops and lands as a wreck.
        private void Hop()
        {
            if (body == null || body.isKinematic) return;
            Vector2 drift = UnityEngine.Random.insideUnitCircle * 0.25f;
            body.AddForce(new Vector3(drift.x, 1f, drift.y) * settings.explosionHopSpeed, ForceMode.VelocityChange);
            Vector3 spin = transform.right * UnityEngine.Random.Range(-1f, 1f) + transform.forward * UnityEngine.Random.Range(-1f, 1f);
            body.AddTorque(spin * settings.explosionSpin, ForceMode.VelocityChange);
        }

        private IEnumerator DetonateNextFrame(Vector3 center)
        {
            yield return null;
            VehicleExplosion.Detonate(center, settings, this, LastAttacker);
        }

        private void ShowWreck()
        {
            for (int i = 0; i < disableWhenWrecked.Length; i++)
            {
                if (disableWhenWrecked[i] != null) disableWhenWrecked[i].SetActive(false);
            }

            if (destroyedModelPrefab != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null || renderers[i] is ParticleSystemRenderer) continue;
                    rendererWasEnabled[i] = renderers[i].enabled;
                    renderers[i].enabled = false;
                }
                destroyedInstance = Instantiate(destroyedModelPrefab, transform);
                destroyedInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                // The destroyed models are dented but keep their paint: burnt the same way as the other wrecks.
                foreach (Renderer part in destroyedInstance.GetComponentsInChildren<Renderer>(true)) TintBurnt(part);
                return;
            }
            SetDarkened(true);
        }

        // Per-material block: keeps each material's own colour and alpha, tinted down to burnt metal.
        private void SetDarkened(bool on)
        {
            if (on == darkened) return;
            darkened = on;
            if (block == null) block = new MaterialPropertyBlock();
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                if (renderer == null || renderer is ParticleSystemRenderer) continue;
                if (!on)
                {
                    renderer.SetPropertyBlock(null);
                    for (int m = 0; m < renderer.sharedMaterials.Length; m++) renderer.SetPropertyBlock(null, m);
                    continue;
                }
                TintBurnt(renderer);
            }
        }

        private void TintBurnt(Renderer renderer)
        {
            if (block == null) block = new MaterialPropertyBlock();
            Material[] materials = renderer.sharedMaterials;
            for (int m = 0; m < materials.Length; m++)
            {
                if (materials[m] == null || !materials[m].HasProperty(BaseColorId)) continue;
                Color color = materials[m].GetColor(BaseColorId);
                color.r *= settings.burntTint.r;
                color.g *= settings.burntTint.g;
                color.b *= settings.burntTint.b;
                renderer.GetPropertyBlock(block, m);
                block.SetColor(BaseColorId, color);
                // Car paint keeps its clear coat otherwise: a burnt car must not stay glossy.
                if (materials[m].HasProperty(BurntId)) block.SetFloat(BurntId, 1f);
                renderer.SetPropertyBlock(block, m);
            }
        }

        // Low-frequency: the smoke dies down, and the wreck goes once it is old enough, off camera and not right next to the player.
        private IEnumerator WreckRoutine()
        {
            var wait = new WaitForSeconds(1f);
            float age = 0f;
            while (true)
            {
                yield return wait;
                age += 1f;
                if (fireEffect != null && age >= settings.wreckFireTime)
                {
                    FadeEffect(ref fireEffect);
                    if (age < settings.wreckSmokeTime) SwapSmoke(HeavySmokePrefab);
                }
                if (smokeEffect != null && age >= settings.wreckSmokeTime) FadeEffect(ref smokeEffect);
                if (age >= settings.wreckMinimumTime && IsOutOfSight(transform.position, settings.wreckKeepDistance)) break;
            }
            wreckRoutine = null;
            if (OnWreckCleanup != null) OnWreckCleanup.Invoke();
            else Destroy(gameObject);
        }

        /// <summary>True when the point is off the main camera's screen and at least keepDistance (m) from it.</summary>
        public static bool IsOutOfSight(Vector3 point, float keepDistance)
        {
            Camera camera = Camera.main;
            if (camera == null) return true;
            if ((camera.transform.position - point).sqrMagnitude < keepDistance * keepDistance) return false;
            Vector3 viewport = camera.WorldToViewportPoint(point);
            const float margin = 0.2f;
            return viewport.z <= 0f || viewport.x < -margin || viewport.x > 1f + margin || viewport.y < -margin || viewport.y > 1f + margin;
        }

        // Light smoke -> heavy smoke: the old one fades where it is while the new one starts.
        private void SwapSmoke(GameObject prefab)
        {
            if (smokeEffect != null && smokeEffect.InUse && smokeEffect.Prefab == prefab) return;
            FadeEffect(ref smokeEffect);
            SpawnEffect(ref smokeEffect, prefab, settings.maxSmokeEffects);
        }

        private bool SpawnEffect(ref PooledVehicleEffect effect, GameObject prefab, int maxLive)
        {
            if (effect != null && effect.InUse) return true;
            Transform anchor = effectAnchor != null ? effectAnchor : transform;
            effect = VehicleEffectPool.Spawn(prefab, anchor.position, maxLive);
            if (effect == null) return false;
            effect.Follow(anchor, settings.effectOffset);
            return true;
        }

        private void FadeEffect(ref PooledVehicleEffect effect)
        {
            if (effect != null && effect.InUse) effect.FadeOut(settings.effectFadeTime);
            effect = null;
        }

        private static void ReleaseEffect(ref PooledVehicleEffect effect)
        {
            if (effect != null && effect.InUse) effect.Release();
            effect = null;
        }

        private void ResetState()
        {
            CurrentHealth = settings.maxHealth;
            bool wasWrecked = Stage == VehicleDamageStage.Destroyed;
            Stage = VehicleDamageStage.Healthy;
            lastCrashTime = -100f;
            LastAttacker = null;
            SetBurningCounted(false);
            ReleaseEffect(ref smokeEffect);
            ReleaseEffect(ref fireEffect);
            for (int i = 0; wasWrecked && i < disableWhenWrecked.Length; i++)
            {
                if (disableWhenWrecked[i] != null) disableWhenWrecked[i].SetActive(true);
            }
            if (destroyedInstance != null)
            {
                Destroy(destroyedInstance);
                destroyedInstance = null;
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null && !(renderers[i] is ParticleSystemRenderer)) renderers[i].enabled = rendererWasEnabled[i];
                }
            }
            SetDarkened(false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            burningCars = 0;
            OnAnyDestroyed = null;
        }
    }
}
