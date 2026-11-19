using System.Collections;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Police
{
    /// <summary>
    /// The officers sitting in a police pursuit car and its two front doors (on the cruiser prefab next to <see cref="PoliceCarUnit"/>;
    /// per-instance, never a Singleton).
    /// - Seated crew: each seat shows a baked seated officer from the <see cref="PedDriverRoster"/> (the same meshes the traffic police
    ///   cars use, fitted to this car's roof with the traffic police car's seat fits), so the crew is visible through the windows.
    ///   The pursuit car had lost this when it was made from the traffic police car: the seated mesh is only assigned at runtime by
    ///   TrafficVehicleAdapter, which the pursuit car does not have, so its driver seat was empty.
    /// - Doors: the door meshes are swung on a hinge made at runtime on their front edge (the model's doors have their pivot
    ///   elsewhere), opened and closed in step with the officers' exit / enter clips.
    /// - Seat poses and the ground points beside each door for <see cref="PoliceOfficerUnit.ActivateFromCar"/> and boarding.
    /// No per-frame cost (door swings are short coroutines).
    /// </summary>
    [DisallowMultipleComponent]
    public class PoliceCarCrew : MonoBehaviour
    {
        public const int DriverSeat = 0;
        public const int PassengerSeat = 1;

        [System.Serializable]
        public class Seat
        {
            [Tooltip("Seat anchor (driver seat space of the roster's seated meshes).")]
            public Transform anchor;
            [Tooltip("Seated officer mesh under the anchor.")]
            public GameObject visual;
            [Tooltip("Door mesh on this side (swung about its front edge).")]
            public Transform door;
            [Tooltip("Where an officer stands beside this door after climbing out (car space, height found on the ground).")]
            public Vector3 exitPoint;
            [Tooltip("Exit / enter clips mirrored (passenger side: the clips are made for a driver getting out to his left).")]
            public bool mirrored;
        }

        [SerializeField] private Seat[] seats = new Seat[0];
        [SerializeField] private PedDriverRoster roster;
        [Tooltip("Seat fits of the roster entries for this car's roof (copied from the traffic police car).")]
        [SerializeField] private PedDriverRoster.SeatFit[] fits = new PedDriverRoster.SeatFit[0];
        [SerializeField] private LayerMask groundLayers = 1;

        [Header("Doors")]
        [Tooltip("Degrees the front doors swing open.")]
        [SerializeField, Range(20f, 85f)] private float doorOpenAngle = 62f;
        [SerializeField, Min(0.05f)] private float doorOpenSeconds = 0.4f;
        [SerializeField, Min(0.05f)] private float doorCloseSeconds = 0.3f;

        private Collider[] carColliders = new Collider[0];
        private Transform[] hinges = new Transform[0];
        private Coroutine[] doorRoutines = new Coroutine[0];
        private int[] seatedEntry = new int[0];
        private bool[] occupied = new bool[0];

        public int SeatCount => seats.Length;
        public bool IsOccupied(int seat) => seat >= 0 && seat < occupied.Length && occupied[seat];
        public bool IsMirrored(int seat) => seats[seat].mirrored;
        /// <summary>Roster entry of the officer in the seat (picks the matching officer model), or null.</summary>
        public PedDriverRoster.Entry EntryAt(int seat) => roster != null && seat >= 0 && seat < seatedEntry.Length ? roster.Get(seatedEntry[seat]) : null;

        private void Awake()
        {
            int n = seats.Length;
            hinges = new Transform[n];
            doorRoutines = new Coroutine[n];
            seatedEntry = new int[n];
            occupied = new bool[n];
            for (int i = 0; i < n; i++)
            {
                seatedEntry[i] = -1;
                hinges[i] = MakeHinge(seats[i].door);
            }
            var solid = new System.Collections.Generic.List<Collider>();
            foreach (Collider c in GetComponentsInChildren<Collider>(true)) if (!c.isTrigger && !(c is WheelCollider)) solid.Add(c);
            carColliders = solid.ToArray();
            if (roster == null) Debug.LogWarning($"[{nameof(PoliceCarCrew)}] No driver roster on '{name}'; the crew is not shown in the car.", this);
        }

        // Pivot on the door's front edge (car space), half way across its thickness; the door mesh hangs under it.
        private Transform MakeHinge(Transform door)
        {
            if (door == null || !door.TryGetComponent(out Renderer renderer)) return null;
            Bounds b = renderer.bounds;
            Vector3 min = transform.InverseTransformPoint(b.min), max = transform.InverseTransformPoint(b.max);
            Vector3 local = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, Mathf.Max(min.z, max.z));
            var hinge = new GameObject(door.name + " Hinge").transform;
            hinge.SetParent(door.parent, false);
            hinge.SetPositionAndRotation(transform.TransformPoint(local), transform.rotation);
            door.SetParent(hinge, true);
            return hinge;
        }

        /// <summary>Puts <paramref name="count"/> officers in the seats (random police roster entries); the rest stay empty. Doors shut.</summary>
        public void Board(int count)
        {
            for (int i = 0; i < seats.Length; i++)
            {
                StopDoor(i);
                if (hinges[i] != null) hinges[i].localRotation = Quaternion.identity;
                bool sit = i < count && roster != null;
                seatedEntry[i] = sit ? roster.RandomIndex(true) : -1;
                SetSeated(i, sit);
            }
        }

        /// <summary>Shows or hides the seated officer of a seat (an officer climbing out takes over / gets back in).</summary>
        public void SetSeated(int seat, bool seated)
        {
            if (seat < 0 || seat >= seats.Length) return;
            occupied[seat] = seated && seatedEntry[seat] >= 0;
            GameObject visual = seats[seat].visual;
            if (visual == null) return;
            if (occupied[seat]) roster.ApplySeated(visual, seatedEntry[seat], PedDriverRoster.FitOf(fits, seatedEntry[seat]));
            visual.SetActive(occupied[seat]);
        }

        /// <summary>Marks a seat as taken by an officer who is getting back in (his model is the given roster entry).</summary>
        public void Reserve(int seat, int rosterIndex)
        {
            if (seat >= 0 && seat < seatedEntry.Length) seatedEntry[seat] = rosterIndex;
        }

        public int RosterIndexAt(int seat) => seat >= 0 && seat < seatedEntry.Length ? seatedEntry[seat] : -1;

        /// <summary>World pose of a humanoid officer's root sitting in the seat (start of the exit clip, end of the enter clip).</summary>
        public void GetSeatPose(int seat, out Vector3 position, out Quaternion rotation)
        {
            Transform anchor = seats[seat].anchor;
            PedDriverRoster.Entry entry = EntryAt(seat);
            Vector3 offset = entry != null ? entry.seatOffset : Vector3.zero;
            float drop = PedDriverRoster.FitOf(fits, seatedEntry[seat]).drop;
            position = anchor.TransformPoint(offset) + anchor.up * -drop;
            rotation = anchor.rotation;
        }

        /// <summary>Ground point beside the seat's door where an officer stands after getting out (and starts getting in).</summary>
        public Vector3 GetDoorGroundPoint(int seat)
        {
            Vector3 point = transform.TransformPoint(seats[seat].exitPoint);
            if (Physics.Raycast(point + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 4f, groundLayers, QueryTriggerInteraction.Ignore)) return hit.point;
            return point;
        }

        /// <summary>
        /// Officer getting out or in: his body and this car ignore each other (he starts inside the cabin, and the car must not
        /// "run him over" with the few cm/s it creeps while parked).
        /// </summary>
        public void SetCollisionIgnored(Collider[] officer, bool ignore)
        {
            if (officer == null) return;
            for (int i = 0; i < officer.Length; i++)
            {
                if (officer[i] == null) continue;
                for (int j = 0; j < carColliders.Length; j++)
                    if (carColliders[j] != null) Physics.IgnoreCollision(officer[i], carColliders[j], ignore);
            }
        }

        public void OpenDoor(int seat) => SwingDoor(seat, true);
        public void CloseDoor(int seat) => SwingDoor(seat, false);

        [Header("Door Sounds (optional)")]
        [SerializeField] private AudioClip doorOpenClip;
        [SerializeField] private AudioClip doorCloseClip;
        [SerializeField, Range(0f, 1f)] private float doorVolume = 0.9f;
        private AudioSource doorAudio;

        private void PlayDoor(int seat, AudioClip clip)
        {
            if (clip == null || !isActiveAndEnabled) return;
            if (doorAudio == null)
            {
                doorAudio = gameObject.AddComponent<AudioSource>();
                doorAudio.playOnAwake = false;
                doorAudio.spatialBlend = 1f;
                doorAudio.minDistance = 2f;
                doorAudio.maxDistance = 30f;
                doorAudio.rolloffMode = AudioRolloffMode.Linear;
            }
            doorAudio.pitch = Random.Range(0.95f, 1.05f);
            doorAudio.PlayOneShot(clip, doorVolume);
        }

        private void SwingDoor(int seat, bool open)
        {
            if (seat < 0 || seat >= hinges.Length || hinges[seat] == null || !isActiveAndEnabled) return;
            StopDoor(seat);
            float side = seats[seat].door != null && transform.InverseTransformPoint(hinges[seat].position).x < 0f ? 1f : -1f;
            Quaternion target = open ? Quaternion.AngleAxis(doorOpenAngle * side, Vector3.up) : Quaternion.identity;
            bool wasOpen = Quaternion.Angle(hinges[seat].localRotation, Quaternion.identity) > 2f;
            if (open && !wasOpen) PlayDoor(seat, doorOpenClip);
            doorRoutines[seat] = StartCoroutine(DoorRoutine(seat, target, open ? doorOpenSeconds : doorCloseSeconds));
        }

        private IEnumerator DoorRoutine(int seat, Quaternion target, float seconds)
        {
            Transform hinge = hinges[seat];
            Quaternion from = hinge.localRotation;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / seconds);
                hinge.localRotation = Quaternion.Slerp(from, target, k);
                yield return null;
            }
            // Shut: the slam.
            bool shut = Quaternion.Angle(target, Quaternion.identity) < 0.5f && Quaternion.Angle(from, Quaternion.identity) > 2f;
            hinge.localRotation = target;
            doorRoutines[seat] = null;
            if (shut) PlayDoor(seat, doorCloseClip);
        }

        private void StopDoor(int seat)
        {
            if (doorRoutines[seat] == null) return;
            StopCoroutine(doorRoutines[seat]);
            doorRoutines[seat] = null;
        }

        private void OnDisable()
        {
            for (int i = 0; i < doorRoutines.Length; i++)
            {
                doorRoutines[i] = null;
                if (hinges[i] != null) hinges[i].localRotation = Quaternion.identity;
            }
        }

#if UNITY_EDITOR
        public void EditorSetup(Seat[] newSeats, PedDriverRoster newRoster, PedDriverRoster.SeatFit[] newFits)
        {
            seats = newSeats;
            roster = newRoster;
            fits = newFits;
        }
#endif
    }
}
