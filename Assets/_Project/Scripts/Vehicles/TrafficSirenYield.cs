using System.Collections.Generic;
using OpenworldCrime.Police;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>Traffic near an approaching cruiser gives way through iTS' existing lane and speed inputs.</summary>
    [DisallowMultipleComponent]
    public sealed class TrafficSirenYield : MonoBehaviour
    {
        private const float ScanInterval = 0.2f;
        private const float SirenRange = 72f;
        private const float PassHoldSeconds = 1.35f;
        private const float ApproachSpeed = 5.5f;
        private const float BlockedSpeed = 3.2f;
        private const float RecoverySpeed = 14f;
        private const int RoadMask = 1 << 0;
        private const int ObstacleMask = ~(1 << 2);

        private sealed class YieldingCar
        {
            public TrafficVehicleAdapter Adapter;
            public TSTrafficAI Ai;
            public TSSimpleCar Car;
            public Vector3 OriginalOffset;
            public Vector3 Offset;
            public Vector3 TargetOffset;
            public float SpeedLimit;
            public float TargetSpeed;
            public float YieldUntil;
            public bool CanMerge;
        }

        private readonly List<YieldingCar> yielding = new List<YieldingCar>();
        private readonly List<PoliceCarUnit> sirens = new List<PoliceCarUnit>();
        private readonly Collider[] slotHits = new Collider[32];
        private readonly RaycastHit[] sightHits = new RaycastHit[16];
        private readonly RaycastHit[] surfaceHits = new RaycastHit[12];
        private float nextScan;

        private void OnEnable() => TrafficVehicleAdapter.OnCarDisabled += OnCarDisabled;

        private void OnDisable()
        {
            TrafficVehicleAdapter.OnCarDisabled -= OnCarDisabled;
            for (int i = yielding.Count - 1; i >= 0; i--) Release(i);
        }

        private void Update()
        {
            float now = Time.time;
            if (now >= nextScan)
            {
                nextScan = now + ScanInterval;
                Scan(now);
            }
            for (int i = yielding.Count - 1; i >= 0; i--)
            {
                YieldingCar item = yielding[i];
                if (item.Adapter == null || !item.Adapter.CanLeaveTraffic)
                {
                    Release(i);
                    continue;
                }

                bool recovering = now >= item.YieldUntil;
                Vector3 target = recovering ? Vector3.zero : item.TargetOffset;
                float speed = recovering ? RecoverySpeed : item.TargetSpeed;
                if (recovering && !item.CanMerge)
                {
                    target = item.Offset;
                    speed = 0f;
                }
                float dt = Time.deltaTime;
                item.Offset = Vector3.MoveTowards(item.Offset, target, dt * (recovering ? 1.05f : 1.65f));
                item.SpeedLimit = Mathf.MoveTowards(item.SpeedLimit, speed, dt * (recovering ? 8f : 9f));
                if (recovering && item.Offset == Vector3.zero && item.SpeedLimit >= RecoverySpeed)
                {
                    Release(i);
                    continue;
                }
                item.Ai.SetPointOffset(item.OriginalOffset + item.Offset);
                item.Car.SirenSpeedLimit = item.SpeedLimit;
            }
        }

        private void Scan(float now)
        {
            sirens.Clear();
            var police = PoliceCarUnit.Active;
            for (int i = 0; i < police.Count; i++)
                if (police[i].SirenActive) sirens.Add(police[i]);

            if (sirens.Count > 0)
            {
                var traffic = TrafficVehicleAdapter.ActiveCars;
                for (int i = 0; i < traffic.Count; i++)
                {
                    var car = traffic[i];
                    if (car == null || car.IsPolice || !car.CanLeaveTraffic ||
                        !TryFindApproachingCruiser(car.transform, out bool crossing)) continue;
                    YieldingCar item = FindOrBegin(car);
                    if (item == null) continue;
                    item.YieldUntil = now + PassHoldSeconds;
                    item.CanMerge = false;
                    PlanYield(item, crossing);
                }
            }

            foreach (var item in yielding)
            {
                if (now < item.YieldUntil || item.Adapter == null) continue;
                item.CanMerge = item.Offset.sqrMagnitude < 0.16f ||
                    !TryGetLane(item, out Vector3 center, out Vector3 forward, false) ||
                    IsClearSlot(item, center, forward, 0f, 1f);
            }
        }

        private bool TryFindApproachingCruiser(Transform car, out bool crossing)
        {
            crossing = false;
            float best = SirenRange * SirenRange;
            bool found = false;
            for (int i = 0; i < sirens.Count; i++)
            {
                PoliceCarUnit cruiser = sirens[i];
                Transform source = cruiser.transform;
                Vector3 delta = car.position - source.position;
                if (Mathf.Abs(delta.y) > 4f) continue;
                delta.y = 0f;
                float distance = delta.sqrMagnitude;
                if (distance > best) continue;

                Vector3 heading = source.forward;
                heading.y = 0f;
                heading.Normalize();
                float ahead = Vector3.Dot(delta, heading);
                float side = Mathf.Abs(Vector3.Dot(delta, Vector3.Cross(Vector3.up, heading)));
                bool approachingParked = ahead > -19f && cruiser.Speed < 2f && Vector3.Dot(car.forward, -delta) > 0f;
                if ((ahead < -1f && !approachingParked) || side > Mathf.Min(12f, 6f + Mathf.Max(0f, ahead) * 0.12f)) continue;
                if (HasBuildingBetween(source.position, car.position)) continue;

                best = distance;
                crossing = Mathf.Abs(Vector3.Dot(car.forward, heading)) < 0.62f;
                found = true;
            }
            return found;
        }

        private bool HasBuildingBetween(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.1f) return false;
            int count = Physics.RaycastNonAlloc(from + Vector3.up * 1.5f, delta / distance, sightHits, distance,
                RoadMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (sightHits[i].rigidbody == null && sightHits[i].distance < distance - 0.45f) return true;
            return false;
        }

        private YieldingCar FindOrBegin(TrafficVehicleAdapter car)
        {
            foreach (var item in yielding)
                if (item.Adapter == car) return item;
            if (!car.TryGetComponent(out TSTrafficAI ai) || !ai.Initialized || ai.IsUpSideDown ||
                !car.TryGetComponent(out TSSimpleCar simple)) return null;
            var added = new YieldingCar
            {
                Adapter = car,
                Ai = ai,
                Car = simple,
                OriginalOffset = ai.PointOffset,
                SpeedLimit = Mathf.Max(ApproachSpeed, ai.CarSpeed)
            };
            yielding.Add(added);
            return added;
        }

        private void PlanYield(YieldingCar item, bool crossing)
        {
            item.TargetSpeed = crossing ? 0f : BlockedSpeed;
            item.TargetOffset = item.Offset;
            if (crossing || item.Ai.OverTaking || !TryGetLane(item, out Vector3 center, out Vector3 forward)) return;

            float lookAhead = Mathf.Clamp(item.Ai.CarSpeed * 0.5f, 3f, 9f);
            float offset = 2.25f;
            if (!IsClearSlot(item, center, forward, offset, lookAhead))
            {
                offset = 1.4f;
                if (!IsClearSlot(item, center, forward, offset, lookAhead)) return;
            }

            Vector3 left = Vector3.Cross(forward, Vector3.up);
            item.TargetOffset = left * offset;
            float movedAside = Vector3.Dot(item.Adapter.transform.position - center, left);
            item.TargetSpeed = movedAside >= offset - 0.45f ? 0f : ApproachSpeed;
        }

        private static bool TryGetLane(YieldingCar item, out Vector3 center, out Vector3 forward, bool requireAlignment = true)
        {
            center = item.Adapter.transform.position;
            forward = Vector3.zero;
            TSPoints previous = item.Ai.PreviousSteerWaypoint;
            TSPoints next = item.Ai.CurrentSteerWaypoint;
            if (previous == null || next == null) return false;
            forward = next.point - previous.point;
            forward.y = 0f;
            forward.Normalize();
            if (forward == Vector3.zero || (requireAlignment && Vector3.Dot(forward, item.Adapter.transform.forward) < 0.8f)) return false;
            Vector3 from = previous.point + item.OriginalOffset;
            from.y = center.y;
            center = from + forward * Vector3.Dot(center - from, forward);
            return true;
        }

        private bool IsClearSlot(YieldingCar item, Vector3 center, Vector3 forward, float offset, float ahead)
        {
            Vector3 left = Vector3.Cross(forward, Vector3.up);
            float halfWidth = Mathf.Clamp(item.Ai.CarWidth * 0.5f, 0.7f, 1.3f);
            float halfLength = Mathf.Clamp(item.Ai.CarDepth * 0.5f, 1.5f, 3.4f);
            Vector3 laneAhead = center + forward * ahead;
            Vector3 slot = laneAhead + left * offset;
            if (!TryRoadHeight(laneAhead, out float laneHeight)) return false;
            float slotHeight = laneHeight;
            if (offset > 0f && (!TryRoadHeight(slot, out slotHeight) || Mathf.Abs(slotHeight - laneHeight) > 0.18f ||
                !TryRoadHeight(slot + left * (halfWidth * 0.85f), out float outerHeight) ||
                Mathf.Abs(outerHeight - laneHeight) > 0.18f)) return false;

            slot.y = slotHeight + 0.9f;
            int count = Physics.OverlapBoxNonAlloc(slot, new Vector3(halfWidth + 0.18f, 0.62f, halfLength + ahead * 0.45f),
                slotHits, Quaternion.LookRotation(forward, Vector3.up), ObstacleMask, QueryTriggerInteraction.Ignore);
            if (count == slotHits.Length) return false;
            for (int i = 0; i < count; i++)
                if (!slotHits[i].transform.IsChildOf(item.Adapter.transform)) return false;
            return true;
        }

        private bool TryRoadHeight(Vector3 position, out float height)
        {
            height = 0f;
            float nearest = float.PositiveInfinity;
            int count = Physics.RaycastNonAlloc(position + Vector3.up * 1.6f, Vector3.down, surfaceHits, 3.6f,
                RoadMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = surfaceHits[i];
                if (hit.rigidbody != null || hit.normal.y < 0.86f || hit.distance >= nearest) continue;
                nearest = hit.distance;
                height = hit.point.y;
            }
            return !float.IsPositiveInfinity(nearest);
        }

        private void OnCarDisabled(TrafficVehicleAdapter car)
        {
            for (int i = yielding.Count - 1; i >= 0; i--)
                if (yielding[i].Adapter == car) Release(i);
        }

        private void Release(int index)
        {
            var item = yielding[index];
            if (item.Ai != null) item.Ai.SetPointOffset(item.OriginalOffset);
            if (item.Car != null) item.Car.SirenSpeedLimit = float.PositiveInfinity;
            yielding.RemoveAt(index);
        }
    }
}
