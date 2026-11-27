using System.Collections;
using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.Parachute
{
    /// <summary>
    /// A parachute bag lying on a rooftop (one per pickup spot, not a Singleton). The bag bobs and spins (MarkerBob on the
    /// visual), a light beam and rising sparkles make it visible from the street, and an optional map blip marks it.
    /// Walking up to the bag gives the character a parachute (<see cref="PlayerParachute.TryEquip"/>): the trigger only wakes the
    /// check, the character's root has to be within Collect Radius of the bag (horizontally), so arms / limb colliders swinging
    /// into the trigger no longer take it from a step away. A character that already carries one leaves it lying. Taken, it
    /// hides and comes back after Respawn Seconds.
    /// Placed by Tools > OpenworldCrime > Parachute > Set Up Parachutes.
    /// </summary>
    [DisallowMultipleComponent]
    public class ParachutePickup : MonoBehaviour
    {
        [Tooltip("Everything that shows while the bag is there (bag, beam, sparkles). Hidden while taken.")]
        [SerializeField] private GameObject visualRoot;
        [Tooltip("Trigger the player walks into.")]
        [SerializeField] private Collider pickupTrigger;
        [Tooltip("Optional map icon, hidden while taken.")]
        [SerializeField] private Behaviour mapBlip;
        [Tooltip("The bag (its visual). Collect Radius is measured from it; the pickup's own position when empty.")]
        [SerializeField] private Transform bagPoint;
        [Tooltip("Horizontal distance (m) from the bag to the character's root (feet) at which it is taken.")]
        [SerializeField, Min(0.2f)] private float collectRadius = 0.9f;
        [Tooltip("Height (m) above / below the bag's base within which it can be taken.")]
        [SerializeField, Min(0.2f)] private float collectHeight = 2f;
        [Tooltip("Seconds until a taken bag is back.")]
        [SerializeField, Min(1f)] private float respawnSeconds = 90f;
        [Tooltip("Optional sound when taken.")]
        [SerializeField] private AudioClip pickupClip;
        [SerializeField, Range(0f, 1f)] private float pickupVolume = 0.8f;

        public bool IsAvailable { get; private set; } = true;

        private void Awake()
        {
            if (visualRoot == null || pickupTrigger == null)
            {
                Debug.LogError($"[{nameof(ParachutePickup)}] Visual or trigger missing on '{name}'.", this);
                enabled = false;
                return;
            }
            pickupTrigger.isTrigger = true;
        }

        private void OnEnable() => SetAvailable(true);

        // Stay, not Enter: the character usually enters the trigger before reaching the bag. Only the character's root collider
        // (the one beside PlayerParachute) counts, so limb / hit-box colliders do not cost a parent search every step.
        private void OnTriggerStay(Collider other)
        {
            if (!IsAvailable || !enabled || !other.TryGetComponent(out PlayerParachute parachute)) return;
            Vector3 bag = bagPoint != null ? bagPoint.position : transform.position;
            Vector3 feet = parachute.transform.position;
            Vector2 flat = new Vector2(feet.x - bag.x, feet.z - bag.z);
            float baseY = transform.position.y;
            if (flat.sqrMagnitude > collectRadius * collectRadius || Mathf.Abs(feet.y - baseY) > collectHeight) return;
            if (!parachute.TryEquip()) return;
            if (pickupClip != null) AudioSource.PlayClipAtPoint(pickupClip, transform.position, pickupVolume);
            SetAvailable(false);
            StartCoroutine(RespawnRoutine());
        }

        private IEnumerator RespawnRoutine()
        {
            yield return new WaitForSeconds(respawnSeconds);
            SetAvailable(true);
        }

        private void SetAvailable(bool available)
        {
            IsAvailable = available;
            if (visualRoot != null) visualRoot.SetActive(available);
            if (pickupTrigger != null) pickupTrigger.enabled = available;
            if (mapBlip != null) mapBlip.enabled = available;
        }

#if UNITY_EDITOR
        public void EditorSetup(GameObject visual, Collider trigger, Behaviour blip, AudioClip clip, Transform bag)
        {
            bagPoint = bag;
            visualRoot = visual;
            pickupTrigger = trigger;
            mapBlip = blip;
            pickupClip = clip;
        }
#endif
    }
}
