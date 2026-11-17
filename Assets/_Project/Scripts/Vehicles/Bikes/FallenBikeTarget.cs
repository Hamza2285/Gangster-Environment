using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Makes a traffic bike lying in the road after a crash (Hamza's "Bike wreck", <see cref="BikeWreck"/>) something the player can
    /// pick up and ride (added to the wreck at runtime by <see cref="TrafficBikeAdapter"/>; per-instance). When the player gets on,
    /// the drivable twin takes the wreck's place in the same lying pose and paint; the mount stands it up first
    /// (<see cref="BikeMountAnimations.RightUp"/>). The wreck object stays (hidden) so Hamza's clean-up still removes the rider's body.
    /// </summary>
    [DisallowMultipleComponent]
    public class FallenBikeTarget : MonoBehaviour, IVehicleInteractionTarget, IVehicleEntrySide
    {
        private const float InteractionSize = 3.4f;
        private const float SideDistance = 0.95f;

        private RCCVehicleAdapter drivablePrefab;
        private Material paint;
        private Rigidbody body;
        private Transform leftPoint, rightPoint;
        private readonly List<Collider> colliders = new List<Collider>();
        private bool taken;

        /// <summary>Finds the wreck BikeCrash has just made at <paramref name="near"/> (the closest one without a target) and makes it enterable.</summary>
        public static void AttachToNewestWreck(Vector3 near, RCCVehicleAdapter drivable, Material paint)
        {
            if (drivable == null) return;
            BikeWreck closest = null;
            float best = 9f; // within 3 m
            foreach (BikeWreck wreck in FindObjectsByType<BikeWreck>(FindObjectsSortMode.None))
            {
                if (wreck.GetComponent<FallenBikeTarget>() != null) continue;
                float d = (wreck.transform.position - near).sqrMagnitude;
                if (d < best) { best = d; closest = wreck; }
            }
            if (closest != null) closest.gameObject.AddComponent<FallenBikeTarget>().Init(drivable, paint);
        }

        private void Init(RCCVehicleAdapter drivable, Material bikePaint)
        {
            drivablePrefab = drivable;
            paint = bikePaint;
            body = GetComponent<Rigidbody>();
            foreach (Collider c in GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) colliders.Add(c);

            // The player's interaction trigger finds vehicles through a trigger on the Triggers layer; it follows the wreck.
            var zone = new GameObject("InteractionZone") { layer = LayerMask.NameToLayer("Triggers") };
            zone.transform.SetParent(transform, false);
            var box = zone.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = Vector3.one * InteractionSize;
            leftPoint = new GameObject("EnterLeft").transform;
            rightPoint = new GameObject("EnterRight").transform;
            leftPoint.SetParent(transform, false);
            rightPoint.SetParent(transform, false);
            UpdatePoints();
        }

        // Beside the bike on the ground, whichever way it lies, facing it.
        private void UpdatePoints()
        {
            Vector3 heading = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (heading.sqrMagnitude < 0.01f) heading = Vector3.forward;
            heading.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, heading);
            Vector3 centre = transform.position;
            leftPoint.SetPositionAndRotation(centre - right * SideDistance, Quaternion.LookRotation(right));
            rightPoint.SetPositionAndRotation(centre + right * SideDistance, Quaternion.LookRotation(-right));
        }

        public bool CanEnter => !taken && drivablePrefab != null && isActiveAndEnabled && (body == null || body.linearVelocity.sqrMagnitude < 1f);
        public bool IsWrecked => false;
        public bool IsReadyForEntry => body == null || body.linearVelocity.sqrMagnitude < 1f;
        public VehicleEntryPrompt EntryPrompt => VehicleEntryPrompt.Enter;
        public Transform GetInteractionPoint() => transform;
        public Transform GetEnterPoint() { UpdatePoints(); return leftPoint; }

        public Transform GetEnterPoint(Vector3 from)
        {
            UpdatePoints();
            return (rightPoint.position - from).sqrMagnitude < (leftPoint.position - from).sqrMagnitude ? rightPoint : leftPoint;
        }

        public Transform GetStealPoint() => null;
        public IReadOnlyList<Collider> GetColliders() => colliders;
        public void ReserveForEntry() { }
        public void CancelEntryReservation() { }

        public IVehicleController CompleteEntry()
        {
            if (!CanEnter) return null;
            RCCVehicleAdapter drivable = TrafficVehicleAdapter.DrivableProvider != null ? TrafficVehicleAdapter.DrivableProvider(drivablePrefab) : null;
            if (drivable == null)
            {
                drivable = Instantiate(drivablePrefab);
                drivable.name = drivablePrefab.name;
            }
            // Same place, same lying pose: the mount stands it up.
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            drivable.transform.SetPositionAndRotation(position, rotation);
            if (drivable.TryGetComponent(out Rigidbody drivableBody))
            {
                drivableBody.position = position;
                drivableBody.rotation = rotation;
                drivableBody.linearVelocity = Vector3.zero;
                drivableBody.angularVelocity = Vector3.zero;
            }
            // It appears where the wreck lay, often against a car: solid only once it has been stood up (BikeMountAnimations.RightUp).
            if (drivable.TryGetComponent(out BikeMountAnimations clips)) clips.SetSolidCollidersEnabled(false);
            Transform model = drivable.transform.Find("Visual/Model");
            if (model != null && paint != null) foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = paint;

            taken = true;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (Collider c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
            if (body != null) body.isKinematic = true;
            TrafficBikeAdapter.RaiseTakenOver(null, drivable);
            return drivable;
        }
    }
}
