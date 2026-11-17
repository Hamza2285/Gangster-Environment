using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// No metal sparks where a car hits a person (on RCC cars, next to RCC_CarControllerV3; per-instance). RCC plays its contact
    /// sparkle on every hit on a layer in its damage filter and only leaves out humanoid rigs, so street pedestrians (generic rigs),
    /// ragdoll limbs and the riders of bikes threw sparks. This notes contacts with people in OnCollisionEnter and, after the
    /// physics callbacks of that frame, stops and clears any RCC sparkle that started at such a point (blood comes from
    /// <see cref="Combat.BloodEffects"/> instead). RCC itself is not modified. No per-frame cost unless a person was hit.
    /// </summary>
    [DisallowMultipleComponent]
    public class VehicleHumanContactFx : MonoBehaviour
    {
        [Tooltip("A sparkle this close (m) to a contact with a person is removed.")]
        [SerializeField, Min(0.05f)] private float matchDistance = 0.6f;

        private readonly List<ParticleSystem> sparkles = new List<ParticleSystem>();
        private readonly List<Vector3> personContacts = new List<Vector3>(4);

        // RCC creates its sparkle pool in Awake; it is collected here once.
        private void Start()
        {
            GameObject prefab = RCC_Settings.Instance != null ? RCC_Settings.Instance.contactParticles : null;
            if (prefab == null) return;
            foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>(true))
                if (ps.name.StartsWith(prefab.name)) sparkles.Add(ps);
            enabled = false;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (sparkles.Count == 0 || collision.contactCount == 0 || !IsPerson(collision.collider)) return;
            personContacts.Add(collision.GetContact(0).point);
            enabled = true; // LateUpdate this frame only
        }

        private void LateUpdate()
        {
            float sqr = matchDistance * matchDistance;
            for (int i = 0; i < sparkles.Count; i++)
            {
                ParticleSystem ps = sparkles[i];
                if (ps == null || !ps.isPlaying) continue;
                for (int c = 0; c < personContacts.Count; c++)
                {
                    if ((ps.transform.position - personContacts[c]).sqrMagnitude > sqr) continue;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    break;
                }
            }
            personContacts.Clear();
            enabled = false;
        }

        /// <summary>A pedestrian, officer, playable character or ragdoll body part (alive or not).</summary>
        public static bool IsPerson(Collider collider)
        {
            if (collider == null) return false;
            if (collider.GetComponentInParent<Pedestrians.PedestrianDamage>() != null) return true;
            if (collider.GetComponentInParent<Invector.vCharacterController.vCharacter>() != null) return true;
            if (collider.GetComponentInParent<PedestrianWalker>() != null) return true;
            Transform root = collider.transform.root;
            return root.name == "Crashed rider";
        }
    }
}
