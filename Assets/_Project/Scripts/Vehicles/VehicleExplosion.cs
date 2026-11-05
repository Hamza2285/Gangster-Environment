using System.Collections.Generic;
using Invector;
using OpenworldCrime.Pedestrians;
using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Area damage of an exploding car: the player on foot, pedestrians (thrown as ragdolls) and nearby cars (which can chain).
    /// Victims are found through their hitbox colliders and damaged once each, scaled by distance.
    /// </summary>
    public static class VehicleExplosion
    {
        private static readonly Collider[] hits = new Collider[128];
        private static readonly HashSet<Object> done = new HashSet<Object>();

        public static void Detonate(Vector3 center, VehicleDamageSettings settings, VehicleHealth source)
        {
            int count = Physics.OverlapSphereNonAlloc(center, settings.explosionRadius, hits, settings.explosionLayers, QueryTriggerInteraction.Collide);
            done.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider c = hits[i];
                if (c == null) continue;
                float share = 1f - Mathf.Clamp01(Vector3.Distance(center, c.bounds.center) / settings.explosionRadius);

                VehicleHealth car = c.GetComponentInParent<VehicleHealth>();
                if (car != null)
                {
                    if (car != source && done.Add(car))
                    {
                        car.ApplyDamage(settings.explosionCarDamage * share, source != null ? source.transform : null);
                        car.Shove(center, settings.explosionCarSpeed * share);
                    }
                    continue;
                }

                PedestrianDamage ped = c.GetComponentInParent<PedestrianDamage>();
                if (ped != null)
                {
                    if (done.Add(ped)) ped.ReceiveExplosion(center, settings.explosionDamage * share, settings.explosionThrowSpeed * share);
                    continue;
                }

                PlayerVehicleInteraction player = c.GetComponentInParent<PlayerVehicleInteraction>();
                if (player != null && done.Add(player) && player.State == VehicleInteractionState.OnFoot && !player.WasEjectedFrom(source))
                    player.gameObject.ApplyDamage(CreateDamage(center, settings.explosionDamage * share, source));
            }
            done.Clear();
        }

        /// <summary>Blast damage on a character: knocked over (Invector's ragdoll) and hurt, wherever it comes from.</summary>
        public static vDamage CreateDamage(Vector3 center, float value, VehicleHealth source)
        {
            return new vDamage
            {
                damageValue = Mathf.Round(value),
                sender = source != null ? source.transform : null,
                hitPosition = center,
                damageType = "Explosion",
                ignoreDefense = true,
                activeRagdoll = true,
                hitReaction = false
            };
        }
    }
}
