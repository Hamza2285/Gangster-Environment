using System;
using System.Collections;
using System.Collections.Generic;
using OpenworldCrime.Environment;
using UnityEngine;

namespace OpenworldCrime.Pedestrians
{
    /// <summary>
    /// Fills spots near the player with people who stand and talk or sit, GTA-style (one per scene, under ///Manager///, not a Singleton).
    /// - People are normal pooled walkers taken from <see cref="PedestrianSpawner"/> out of sight (the total count does not grow) and
    ///   given back when far and unseen; spots come from <see cref="AmbientSpotData"/>.
    /// - While held they are ordinary walkers in the spawner's Standing state, so every existing reaction applies: shots panic them,
    ///   damage, knock-downs, death and corpses all go through PedestrianDamage / PedestrianDamageBridge. When they leave the hold
    ///   for any reason, <see cref="PedestrianWalker.LeftHold"/> tells this manager, which forgets them.
    /// - Extra reactions a held person needs: a car on a course to hit them panics them, and a bench breaking throws its sitters off.
    /// Runs on two low-frequency coroutines; the per-frame cost of a held person is lower than a walker's.
    /// </summary>
    [DisallowMultipleComponent]
    public class AmbientPopulationManager : MonoBehaviour
    {
        private const string GirlName = "Girl";
        private const string ManName = "Man";
        private const float CellSize = 40f;

        private static readonly string[] HighSeatStates = { "sit_a", "sit_b", "sit_c" };
        private static readonly string[] LowSeatStates = { "sit_low_a", "sit_low_b", "sit_low_c" };
        private static readonly string[] TalkStates = { "talk1", "talk2" };

        [Header("References")]
        [SerializeField] private AmbientPopulationSettings settings;
        [SerializeField] private AmbientSpotData spotData;
        [SerializeField] private PedestrianSpawner pedestrians;
        [Tooltip("Distances are measured from this transform.")]
        [SerializeField] private Transform player;
        [Tooltip("Empty = Camera.main.")]
        [SerializeField] private Camera targetCamera;
        [Tooltip("Layers that hide a spot from the camera (buildings, walls) and that people stand on.")]
        [SerializeField] private LayerMask occluderLayers = 1;

        private sealed class Member
        {
            public PedestrianWalker Walker;
            public PedestrianDamage Damage;
            public Placement Placement;
            public Action LeftHandler;
        }

        private sealed class Placement
        {
            public int Spot;
            public bool Seated;
            public readonly List<Member> Members = new List<Member>(3);
            public float NextLineAt;
            public PedestrianWalker Speaker;
        }

        private readonly List<Placement> placements = new List<Placement>();
        private readonly List<int> nearbySpots = new List<int>();
        private readonly List<Vector3> brokenBenches = new List<Vector3>();
        private readonly Dictionary<Vector2Int, List<int>> grid = new Dictionary<Vector2Int, List<int>>();
        private readonly Collider[] carHits = new Collider[12];
        private readonly Plane[] frustum = new Plane[6];
        private float[] spotFreeAt = new float[0];
        private bool valid;

        /// <summary>Occupied talking spots and people sitting right now (debugging, tests).</summary>
        public int TalkingGroups { get; private set; }
        public int Sitters { get; private set; }

        private void Awake()
        {
            valid = settings != null && spotData != null && pedestrians != null && player != null;
            if (!valid)
            {
                Debug.LogError($"[{nameof(AmbientPopulationManager)}] Missing settings, spot data, spawner or player on '{name}'. Nobody will stand or sit.", this);
                enabled = false;
                return;
            }
            BuildGrid();
        }

        private void OnEnable()
        {
            if (!valid) return;
            BreakableProp.OnBroken += HandlePropBroken;
            BreakableProp.OnPropReset += HandlePropReset;
            StartCoroutine(PlacementRoutine());
            StartCoroutine(CarWatchRoutine());
        }

        private void OnDisable()
        {
            BreakableProp.OnBroken -= HandlePropBroken;
            BreakableProp.OnPropReset -= HandlePropReset;
        }

        private void BuildGrid()
        {
            AmbientSpot[] spots = spotData.spots;
            spotFreeAt = new float[spots.Length];
            for (int i = 0; i < spots.Length; i++)
            {
                Vector2Int cell = CellOf(spots[i].position);
                if (!grid.TryGetValue(cell, out List<int> list)) grid[cell] = list = new List<int>();
                list.Add(i);
            }
        }

        private static Vector2Int CellOf(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));

        private Camera Cam
        {
            get
            {
                if (targetCamera == null || !targetCamera.isActiveAndEnabled) targetCamera = Camera.main;
                return targetCamera;
            }
        }

        private IEnumerator PlacementRoutine()
        {
            var wait = new WaitForSeconds(settings.checkInterval);
            while (true)
            {
                yield return wait;
                Camera cam = Cam;
                if (cam == null) continue;
                GeometryUtility.CalculateFrustumPlanes(cam, frustum);
                RecycleFarPlacements(cam);
                TickConversations();
                TryPlaceOne(cam);
            }
        }

        private void RecycleFarPlacements(Camera cam)
        {
            float maxSqr = settings.recycleDistance * settings.recycleDistance;
            for (int i = placements.Count - 1; i >= 0; i--)
            {
                Placement placement = placements[i];
                Vector3 position = spotData.spots[placement.Spot].position;
                if ((position - player.position).sqrMagnitude < maxSqr || !IsHidden(cam, position)) continue;
                // Going back through the pool raises LeftHold, which removes the member and, with the last one, the placement.
                for (int m = placement.Members.Count - 1; m >= 0; m--) pedestrians.ReleaseDown(placement.Members[m].Walker);
            }
        }

        private void TickConversations()
        {
            float now = Time.time;
            for (int i = 0; i < placements.Count; i++)
            {
                Placement placement = placements[i];
                if (placement.Seated || now < placement.NextLineAt) continue;
                placement.NextLineAt = now + UnityEngine.Random.Range(settings.lineSecondsMin, settings.lineSecondsMax);
                NextLine(placement);
            }
        }

        // One talks, the others listen and turn to him a little off-centre.
        private void NextLine(Placement placement)
        {
            List<Member> members = placement.Members;
            if (members.Count == 0) return;
            if (members.Count == 1)
            {
                members[0].Walker.PlayStand("idle1");
                return;
            }
            int next = UnityEngine.Random.Range(0, members.Count);
            if (members[next].Walker == placement.Speaker) next = (next + 1) % members.Count;
            PedestrianWalker speaker = members[next].Walker;
            placement.Speaker = speaker;
            Vector3 centre = spotData.spots[placement.Spot].position;
            for (int i = 0; i < members.Count; i++)
            {
                PedestrianWalker walker = members[i].Walker;
                if (walker == speaker)
                {
                    walker.PlayStand(TalkStates[UnityEngine.Random.Range(0, TalkStates.Length)]);
                    // The speaker looks around the group.
                    walker.SetHoldFacing(Jitter(walker.transform.position, centre, settings.facingJitter * 1.5f));
                    continue;
                }
                float roll = UnityEngine.Random.value;
                walker.PlayStand(roll < 0.7f || !walker.HasSecondIdle ? "listen" : roll < 0.9f ? "idle1" : "idle2");
                walker.SetHoldFacing(Jitter(walker.transform.position, speaker.transform.position, settings.facingJitter));
            }
        }

        private static Vector3 Jitter(Vector3 from, Vector3 target, float degrees)
        {
            Vector3 direction = target - from;
            direction.y = 0f;
            return from + Quaternion.Euler(0f, UnityEngine.Random.Range(-degrees, degrees), 0f) * direction;
        }

        private void TryPlaceOne(Camera cam)
        {
            bool wantTalk = TalkingGroups < settings.maxTalkingGroups;
            bool wantSit = Sitters < settings.maxSitters;
            if (!wantTalk && !wantSit) return;
            int held = Sitters;
            for (int i = 0; i < placements.Count; i++) if (!placements[i].Seated) held += placements[i].Members.Count;
            if (held + 1 > pedestrians.amount * settings.maxShareOfCrowd) return;

            CollectNearbySpots();
            if (nearbySpots.Count == 0) return;
            int start = UnityEngine.Random.Range(0, nearbySpots.Count);
            int tries = Mathf.Min(nearbySpots.Count, 10);
            float minSqr = settings.activateMinDistance * settings.activateMinDistance;
            float inViewSqr = settings.appearInViewDistance * settings.appearInViewDistance;
            float now = Time.time;
            for (int t = 0; t < tries; t++)
            {
                int index = nearbySpots[(start + t) % nearbySpots.Count];
                AmbientSpot spot = spotData.spots[index];
                bool seat = spot.kind != AmbientSpotKind.Talk;
                if (seat ? !wantSit : !wantTalk) continue;
                if (now < spotFreeAt[index]) continue;
                if ((spot.position - player.position).sqrMagnitude < minSqr) continue;
                if (IsSpotTaken(spot.position) || IsNearBrokenBench(spot)) continue;
                if ((spot.position - cam.transform.position).sqrMagnitude < inViewSqr && !IsHidden(cam, spot.position)) continue;

                bool placed = seat ? PlaceSitter(index, spot) : PlaceGroup(index, spot);
                if (placed) return;
            }
        }

        private void CollectNearbySpots()
        {
            nearbySpots.Clear();
            Vector3 center = player.position;
            float radius = settings.activateMaxDistance;
            float radiusSqr = radius * radius;
            Vector2Int min = CellOf(center - new Vector3(radius, 0f, radius));
            Vector2Int max = CellOf(center + new Vector3(radius, 0f, radius));
            for (int x = min.x; x <= max.x; x++)
            for (int z = min.y; z <= max.y; z++)
            {
                if (!grid.TryGetValue(new Vector2Int(x, z), out List<int> list)) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    Vector3 d = spotData.spots[list[i]].position - center;
                    d.y = 0f;
                    if (d.sqrMagnitude <= radiusSqr) nearbySpots.Add(list[i]);
                }
            }
        }

        private bool IsSpotTaken(Vector3 position)
        {
            float minSqr = settings.minSpotSeparation * settings.minSpotSeparation;
            for (int i = 0; i < placements.Count; i++)
                if ((spotData.spots[placements[i].Spot].position - position).sqrMagnitude < minSqr) return true;
            return false;
        }

        private bool IsNearBrokenBench(AmbientSpot spot)
        {
            if (spot.kind != AmbientSpotKind.Bench) return false;
            float maxSqr = settings.brokenBenchRadius * settings.brokenBenchRadius;
            for (int i = 0; i < brokenBenches.Count; i++)
                if ((brokenBenches[i] - spot.position).sqrMagnitude < maxSqr) return true;
            return false;
        }

        // Off screen, or a building stands between the camera and the spot.
        private bool IsHidden(Camera cam, Vector3 position)
        {
            Vector3 head = position + Vector3.up;
            if (!GeometryUtility.TestPlanesAABB(frustum, new Bounds(head, Vector3.one * 2.5f))) return true;
            return Physics.Linecast(cam.transform.position, head, occluderLayers, QueryTriggerInteraction.Ignore);
        }

        private bool PlaceSitter(int index, AmbientSpot spot)
        {
            PedestrianWalker walker = pedestrians.TakeFromCrowd(UnityEngine.Random.value < 0.5f ? GirlName : ManName);
            if (walker == null) return false;

            var placement = new Placement { Spot = index, Seated = true };
            Vector3 forward = Quaternion.Euler(0f, spot.yaw, 0f) * Vector3.forward;
            walker.BeginHold(spot.position, spot.position + forward * 3f);
            string[] states = spot.seatHeight < settings.lowSeatHeight ? LowSeatStates : HighSeatStates;
            walker.PlayStand(states[UnityEngine.Random.Range(0, states.Length)]);
            Member member = AddMember(placement, walker);
            if (member.Damage != null)
            {
                // On a very low seat the hips stay a little higher, so the feet do not sink into the ground.
                float hipsUp = Mathf.Max(settings.hipsAboveSeat, settings.minHipsAboveGround - spot.seatHeight);
                SeatOnSpot(walker, member.Damage, spot.position + Vector3.up * hipsUp);
                member.Damage.SetSeatedBody(true, spot.seatHeight + settings.seatedBodyHeight);
            }
            walker.SetVisible(true);
            placements.Add(placement);
            Sitters++;
            return true;
        }

        // The clip knows how high the hips are above the root; the root moves so the hips land exactly on the seat.
        private static void SeatOnSpot(PedestrianWalker walker, PedestrianDamage damage, Vector3 hipsTarget)
        {
            if (!walker.TryGetComponent(out Animator animator)) return;
            AnimatorCullingMode culling = animator.cullingMode;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Update(0f);
            walker.MoveHold(walker.transform.position + (hipsTarget - damage.BodyPosition));
            animator.cullingMode = culling;
        }

        private bool PlaceGroup(int index, AmbientSpot spot)
        {
            int size = UnityEngine.Random.value < settings.threePersonChance ? 3 : 2;
            float radius = size == 2 ? settings.pairRadius : settings.trioRadius;
            float angle = UnityEngine.Random.Range(0f, 360f);
            var placement = new Placement { Spot = index, NextLineAt = Time.time };
            // Mixed: the first person is either, the next the opposite when the crowd has one out of sight.
            bool girl = UnityEngine.Random.value < 0.5f;
            for (int i = 0; i < size; i++)
            {
                PedestrianWalker walker = pedestrians.TakeFromCrowd(girl ? GirlName : ManName) ?? pedestrians.TakeFromCrowd(girl ? ManName : GirlName);
                if (walker == null) break;
                girl = !girl;
                Vector3 position = spot.position + Quaternion.Euler(0f, angle + i * 360f / size, 0f) * Vector3.forward * radius;
                if (Physics.Raycast(position + Vector3.up, Vector3.down, out RaycastHit ground, 2f, occluderLayers, QueryTriggerInteraction.Ignore)) position.y = ground.point.y + 0.02f;
                walker.BeginHold(position, spot.position);
                walker.PlayStand("idle1");
                walker.SetVisible(true);
                AddMember(placement, walker);
            }
            if (placement.Members.Count == 0) return false;
            placements.Add(placement);
            TalkingGroups++;
            return true;
        }

        private Member AddMember(Placement placement, PedestrianWalker walker)
        {
            var member = new Member { Walker = walker, Placement = placement, Damage = walker.GetComponent<PedestrianDamage>() };
            member.LeftHandler = () => HandleMemberLeft(member);
            walker.LeftHold += member.LeftHandler;
            placement.Members.Add(member);
            return member;
        }

        // Panic, knock-down, death or recycling: he is an ordinary walker again (or gone).
        private void HandleMemberLeft(Member member)
        {
            member.Walker.LeftHold -= member.LeftHandler;
            if (member.Damage != null) member.Damage.SetSeatedBody(false, 0f);
            Placement placement = member.Placement;
            placement.Members.Remove(member);
            if (placement.Speaker == member.Walker) placement.Speaker = null;
            if (placement.Seated) Sitters = Mathf.Max(0, Sitters - 1);
            if (placement.Members.Count > 0)
            {
                placement.NextLineAt = 0f;
                return;
            }
            placements.Remove(placement);
            if (!placement.Seated) TalkingGroups = Mathf.Max(0, TalkingGroups - 1);
            spotFreeAt[placement.Spot] = Time.time + settings.spotCooldown;
        }

        private IEnumerator CarWatchRoutine()
        {
            var wait = new WaitForSeconds(settings.carCheckInterval);
            while (true)
            {
                yield return wait;
                float maxSqr = settings.carCheckPlayerDistance * settings.carCheckPlayerDistance;
                for (int i = placements.Count - 1; i >= 0; i--)
                {
                    if (i >= placements.Count) continue;
                    Placement placement = placements[i];
                    Vector3 position = spotData.spots[placement.Spot].position;
                    if ((position - player.position).sqrMagnitude > maxSqr) continue;
                    if (!FindIncomingCar(position, out Vector3 carPosition)) continue;
                    // Panicking raises LeftHold, which shrinks these lists.
                    for (int m = placement.Members.Count - 1; m >= 0; m--)
                        pedestrians.TriggerPanicFor(placement.Members[m].Walker, carPosition);
                }
            }
        }

        private bool FindIncomingCar(Vector3 position, out Vector3 carPosition)
        {
            carPosition = position;
            int count = Physics.OverlapSphereNonAlloc(position, settings.carScanRadius, carHits, settings.vehicleLayers, QueryTriggerInteraction.Collide);
            float minSpeedSqr = settings.carMinSpeed * settings.carMinSpeed;
            float passSqr = settings.carPassDistance * settings.carPassDistance;
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = carHits[i].attachedRigidbody;
                if (body == null || body.isKinematic) continue;
                Vector3 velocity = body.linearVelocity;
                velocity.y = 0f;
                if (velocity.sqrMagnitude < minSpeedSqr) continue;
                Vector3 toSpot = position - body.position;
                toSpot.y = 0f;
                float speed = velocity.magnitude;
                float along = Vector3.Dot(toSpot, velocity / speed);
                if (along < 0f || along / speed > settings.carWarningSeconds) continue;
                if (toSpot.sqrMagnitude - along * along > passSqr) continue;
                carPosition = body.position;
                return true;
            }
            return false;
        }

        // A bench goes down with its sitters; they land as ragdolls, get up and run (through PedestrianDamage).
        private void HandlePropBroken(BreakableProp prop)
        {
            if (prop == null || !prop.name.Contains("Bench")) return;
            brokenBenches.Add(prop.RestPosition);
            float maxSqr = settings.brokenBenchRadius * settings.brokenBenchRadius;
            for (int i = placements.Count - 1; i >= 0; i--)
            {
                if (i >= placements.Count) continue;
                Placement placement = placements[i];
                if (!placement.Seated) continue;
                if ((spotData.spots[placement.Spot].position - prop.RestPosition).sqrMagnitude > maxSqr) continue;
                for (int m = placement.Members.Count - 1; m >= 0; m--)
                {
                    Member member = placement.Members[m];
                    if (member.Damage == null) continue;
                    Vector3 away = member.Walker.transform.position - prop.transform.position;
                    away.y = 0f;
                    away = away.sqrMagnitude > 0.01f ? away.normalized : member.Walker.transform.forward;
                    member.Damage.KnockDownBy(away * settings.benchThrowSpeed + Vector3.up * 0.4f, Vector3.Cross(Vector3.up, away) * 1.5f, settings.benchGroundTime, prop.RestPosition);
                }
            }
        }

        private void HandlePropReset(BreakableProp prop)
        {
            if (prop == null) return;
            Vector3 position = prop.RestPosition;
            for (int i = brokenBenches.Count - 1; i >= 0; i--)
                if ((brokenBenches[i] - position).sqrMagnitude < 1f) brokenBenches.RemoveAt(i);
        }

#if UNITY_EDITOR
        public void EditorSetup(AmbientPopulationSettings newSettings, AmbientSpotData newSpots, PedestrianSpawner spawner, Transform playerTransform)
        {
            settings = newSettings;
            spotData = newSpots;
            pedestrians = spawner;
            player = playerTransform;
        }
#endif
    }
}
