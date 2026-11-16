using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.Interiors
{
    using PlayerManager = OpenworldCrime.Player.PlayerManager; // SharedAssets has a global PlayerManager

    /// <summary>
    /// Automatic doors: opens the assigned hinged doors (GANGFIRE package component, driven through its public Open / Close)
    /// while the player is inside this trigger volume and closes them shortly after he leaves.
    /// Update only runs while the close delay counts down.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ProximityDoorTrigger : MonoBehaviour
    {
        [SerializeField] private GangfireHingedDoor[] doors = new GangfireHingedDoor[0];
        [Tooltip("Seconds the doors stay open after the player left the volume.")]
        [SerializeField, Min(0f)] private float closeDelay = 0.75f;
        [Tooltip("Optional sound played when the doors start opening.")]
        [SerializeField] private AudioSource openAudio;

        private int insideCount;
        private float closeTimer;

        private void Awake()
        {
            if (doors.Length == 0) Debug.LogWarning($"[{nameof(ProximityDoorTrigger)}] No doors assigned on '{name}'.", this);
            for (int i = 0; i < doors.Length; i++) if (doors[i] != null) doors[i].Close();
            enabled = false;
        }

        private void Update()
        {
            closeTimer -= Time.deltaTime;
            if (closeTimer > 0f) return;
            SetDoorsOpen(false);
            enabled = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsPlayer(other)) return;
            insideCount++;
            if (insideCount != 1) return;
            enabled = false; // cancel a pending close
            SetDoorsOpen(true);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsPlayer(other)) return;
            insideCount = Mathf.Max(0, insideCount - 1);
            if (insideCount != 0) return;
            closeTimer = closeDelay;
            enabled = true;
        }

        private void SetDoorsOpen(bool open)
        {
            bool changed = false;
            for (int i = 0; i < doors.Length; i++)
            {
                GangfireHingedDoor door = doors[i];
                if (door == null || door.isOpen == open) continue;
                if (open) door.Open();
                else door.Close();
                changed = true;
            }
            if (open && changed && openAudio != null && openAudio.clip != null) openAudio.PlayOneShot(openAudio.clip);
        }

        // Only the player's own capsule counts (not his weapons' hitboxes or ragdoll parts).
        private static bool IsPlayer(Collider other) => other is CapsuleCollider && other.attachedRigidbody != null && other.attachedRigidbody.GetComponent<PlayerManager>() != null;

        private void Reset() => GetComponent<Collider>().isTrigger = true;
    }
}
