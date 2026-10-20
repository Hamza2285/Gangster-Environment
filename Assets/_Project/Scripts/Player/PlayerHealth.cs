using System;
using Invector;
using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Thin wrapper around Invector's health (vThirdPersonController inherits vHealthController).
    /// It does NOT keep a second HP value - it only re-exposes Invector's health events as C# events
    /// so project systems (HUD, GameManager, missions) do not need to reference Invector directly.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerHealth : MonoBehaviour
    {
        [Header("Invector Reference")]
        [Tooltip("The Invector health owner. On the player this is the vThirdPersonController on the same GameObject.")]
        [SerializeField] private vHealthController healthController;

        /// <summary>Raised when health changes. Args: current health, max health.</summary>
        public event Action<float, float> OnHealthChanged;
        /// <summary>Raised when the player receives damage greater than zero.</summary>
        public event Action<vDamage> OnDamaged;
        /// <summary>Raised once when the player dies.</summary>
        public event Action OnDied;
        /// <summary>Raised when Invector resets the health (respawn/revive).</summary>
        public event Action OnHealthReset;

        public float CurrentHealth => healthController != null ? healthController.currentHealth : 0f;
        public float MaxHealth => healthController != null ? healthController.MaxHealth : 0f;
        public float HealthNormalized => MaxHealth > 0f ? CurrentHealth / MaxHealth : 0f;
        public bool IsDead => healthController == null || healthController.isDead;

        private void Awake()
        {
            if (healthController == null)
            {
                Debug.LogError($"[{nameof(PlayerHealth)}] Missing vHealthController reference on '{name}'. Assign the vThirdPersonController.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (healthController == null) return;
            healthController.onChangeHealth.AddListener(HandleHealthChanged);
            healthController.onReceiveDamage.AddListener(HandleDamaged);
            healthController.onDead.AddListener(HandleDead);
            healthController.onResetHealth.AddListener(HandleHealthReset);
        }

        private void OnDisable()
        {
            if (healthController == null) return;
            healthController.onChangeHealth.RemoveListener(HandleHealthChanged);
            healthController.onReceiveDamage.RemoveListener(HandleDamaged);
            healthController.onDead.RemoveListener(HandleDead);
            healthController.onResetHealth.RemoveListener(HandleHealthReset);
        }

        /// <summary>Heals the player by the given amount (ignored when dead).</summary>
        public void Heal(int amount)
        {
            if (IsDead || amount <= 0) return;
            healthController.AddHealth(amount);
        }

        private void HandleHealthChanged(float current)
        {
            OnHealthChanged?.Invoke(current, MaxHealth);
        }

        private void HandleDamaged(vDamage damage)
        {
            OnDamaged?.Invoke(damage);
        }

        private void HandleDead(GameObject deadObject)
        {
            OnDied?.Invoke();
        }

        private void HandleHealthReset()
        {
            OnHealthReset?.Invoke();
        }
    }
}
