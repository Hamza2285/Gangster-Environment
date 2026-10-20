using System;
using Invector;
using Invector.vCharacterController;
using UnityEngine;

namespace OpenworldCrime.Combat
{
    /// <summary>
    /// AI-less target dummy. Health, damage, hit reactions and hit particles are Invector's
    /// (vCharacter + vDamageReceiver per bone + vHitDamageParticle); this script only
    /// switches to Invector's vRagdoll on death, pushes it with the killing shot and
    /// re-raises the death as C# events for game systems.
    /// </summary>
    [DisallowMultipleComponent]
    public class EnemyDummy : MonoBehaviour
    {
        [Header("Invector References")]
        [SerializeField] private vCharacter character;
        [SerializeField] private vRagdoll ragdoll;

        [Header("Body")]
        [Tooltip("Root capsule used while alive. Disabled on death so the ragdoll is not pushed out of it.")]
        [SerializeField] private Collider aliveCollider;

        [Header("Death Impulse")]
        [Tooltip("Extra impulse applied to the hit limb on death, as a multiple of the killing vDamage.force. " +
                 "Invector's projectile also pushes the hit limb once, so 0 = Invector force only.")]
        [SerializeField, Min(0f)] private float extraForceMultiplier = 1f;
        [Tooltip("Impulse used when the killing damage carries no force (e.g. melee). Applied along sender -> dummy.")]
        [SerializeField, Min(0f)] private float fallbackImpulse = 20f;

        /// <summary>Raised when this dummy dies.</summary>
        public event Action<EnemyDummy> OnDied;
        /// <summary>Raised when any dummy dies (for game systems that do not reference individual dummies).</summary>
        public static event Action<EnemyDummy> OnAnyDummyDied;

        public bool IsDead => character == null || character.isDead;
        public float CurrentHealth => character != null ? character.currentHealth : 0f;
        public int MaxHealth => character != null ? character.MaxHealth : 0;
        /// <summary>Root capsule used while alive (other enemy systems toggle it during temporary ragdolls).</summary>
        public Collider AliveCollider => aliveCollider;

        private vDamage lastDamage;

        private void Awake()
        {
            if (character == null || ragdoll == null)
            {
                Debug.LogError($"[{nameof(EnemyDummy)}] Missing vCharacter or vRagdoll reference on '{name}'.", this);
                enabled = false;
                return;
            }
            // vCharacter does not initialise itself (vThirdPersonMotor/AI normally call this);
            // Init caches the animator parameters used by Invector's hit reactions.
            character.Init();
        }

        private void OnEnable()
        {
            if (character == null) return;
            character.onStartReceiveDamage.AddListener(CacheDamage);
            character.onDead.AddListener(HandleDead);
        }

        private void OnDisable()
        {
            if (character == null) return;
            character.onStartReceiveDamage.RemoveListener(CacheDamage);
            character.onDead.RemoveListener(HandleDead);
        }

        // onStartReceiveDamage fires before health is reduced, so the killing blow is cached before onDead.
        private void CacheDamage(vDamage damage) => lastDamage = damage;

        private void HandleDead(GameObject deadObject)
        {
            if (aliveCollider != null) aliveCollider.enabled = false;

            ragdoll.ActivateRagdoll();
            ApplyDeathImpulse();

            OnDied?.Invoke(this);
            OnAnyDummyDied?.Invoke(this);
        }

        private void ApplyDeathImpulse()
        {
            if (lastDamage == null) return;

            Rigidbody limb = lastDamage.receiver != null ? lastDamage.receiver.GetComponentInParent<Rigidbody>() : null;
            if (limb == null || limb.isKinematic) return;

            Vector3 impulse = lastDamage.force * extraForceMultiplier;
            if (lastDamage.force.sqrMagnitude < 0.0001f && lastDamage.sender != null)
            {
                Vector3 direction = transform.position - lastDamage.sender.position;
                direction.y = 0f;
                impulse = direction.normalized * fallbackImpulse;
            }

            if (impulse.sqrMagnitude > 0f)
            {
                limb.AddForceAtPosition(impulse, lastDamage.hitPosition, ForceMode.Impulse);
            }
        }
    }
}
