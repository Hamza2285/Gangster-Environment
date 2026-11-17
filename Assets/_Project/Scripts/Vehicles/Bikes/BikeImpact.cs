using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Measures a motorbike collision for <see cref="BikeCrashSettings"/>: how hard it was (closing speed along the contact
    /// normal, so a glancing scrape counts little) and where it came from relative to the bike's nose (0 = ridden into something
    /// head-on, 90 = from the side, 180 = from behind). Shared by the player's and the traffic bikes.
    /// </summary>
    public static class BikeImpact
    {
        public struct Result
        {
            public float ImpactKmh;
            public float AngleDegrees;
            public Vector3 Point;
            public Vector3 Normal;
            public Vector3 OtherVelocity;
        }

        /// <summary>
        /// False when the contact is not a crash: the ground (a landing, a kerb), people and small or light things.
        /// <paramref name="bikeVelocity"/> is the bike's velocity before the physics step resolved the contact.
        /// </summary>
        public static bool Measure(Transform bike, Vector3 bikeVelocity, Collision collision, BikeCrashSettings settings, out Result result)
        {
            result = default;
            Collider other = collision.collider;
            if (other == null || other.isTrigger || collision.contactCount == 0) return false;
            if (other.GetComponentInParent<Pedestrians.PedestrianDamage>() != null) return false;
            if (other.GetComponentInParent<CharacterController>() != null) return false;
            if (other.GetComponentInParent<Invector.vCharacterController.vCharacter>() != null) return false;

            ContactPoint contact = collision.GetContact(0);
            Rigidbody otherBody = collision.rigidbody;
            bool moving = otherBody != null && !otherBody.isKinematic;
            if (moving && otherBody.mass < settings.minOtherMass) return false;
            if (!moving)
            {
                if (contact.normal.y > settings.groundNormalY) return false;
                Vector3 size = other.bounds.size;
                if (Mathf.Max(size.x, size.y, size.z) < settings.minStaticSize) return false;
            }

            // The callback comes after the physics step resolved the hit, so the bodies' velocities are already the bounced ones.
            // Static hits use the bike's velocity from before the step; moving ones the contact's relative velocity (from before
            // the solve), turned so it points from the bike towards the contact (the way the bike closed in on the other body).
            Vector3 closing;
            if (!moving) closing = bikeVelocity;
            else
            {
                closing = collision.relativeVelocity;
                if (Vector3.Dot(closing, Vector3.ProjectOnPlane(contact.point - bike.position, Vector3.up)) < 0f) closing = -closing;
            }
            Vector3 otherVelocity = bikeVelocity - closing;
            Vector3 normal = contact.normal;
            float alongNormal = Mathf.Abs(Vector3.Dot(closing, normal));
            Vector3 flat = Vector3.ProjectOnPlane(closing, Vector3.up);
            Vector3 forward = Vector3.ProjectOnPlane(bike.forward, Vector3.up);
            float angle = flat.sqrMagnitude > 0.01f && forward.sqrMagnitude > 0.01f ? Vector3.Angle(forward, flat) : 90f;

            result = new Result
            {
                ImpactKmh = alongNormal * 3.6f,
                AngleDegrees = angle,
                Point = contact.point,
                Normal = normal,
                OtherVelocity = otherVelocity
            };
            return true;
        }

        /// <summary>Rolls the bike away from the hit (degrees per second); BikeRig's upright pull brings it back.</summary>
        public static void Wobble(Rigidbody body, Transform bike, Vector3 normal, float rollDegreesPerSecond)
        {
            if (body == null || body.isKinematic || rollDegreesPerSecond <= 0f) return;
            float side = Vector3.Dot(normal, bike.right);
            float sign = Mathf.Abs(side) > 0.05f ? -Mathf.Sign(side) : (Random.value < 0.5f ? -1f : 1f);
            body.AddTorque(bike.forward * (sign * rollDegreesPerSecond * Mathf.Deg2Rad), ForceMode.VelocityChange);
        }
    }
}
