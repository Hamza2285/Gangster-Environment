using System;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// The street pedestrians that drive traffic cars. Each entry is one pedestrian type of the PedestrianSpawner (same index
    /// into its People Prefabs): a baked seated mesh shown at the wheel while the car drives (no character cost), and a humanoid
    /// avatar for the pedestrian's Biped rig so the real pedestrian can play the carjack clips (seated, pulled out, stumble)
    /// before it goes back to being a normal walker. Built by Tools > OpenworldCrime > Vehicles > Build Pedestrian Drivers.
    /// </summary>
    [CreateAssetMenu(menuName = "OpenworldCrime/Vehicles/Ped Driver Roster", fileName = "PedDriverRoster")]
    public class PedDriverRoster : ScriptableObject
    {
        /// <summary>How one driver sits in one car so his head clears its roof: moved down and reclined about the hips. Never scaled.</summary>
        [Serializable]
        public struct SeatFit
        {
            [Tooltip("Metres the seated driver is lowered.")]
            public float drop;
            [Tooltip("Degrees the seated driver is reclined about the hips.")]
            public float recline;
        }

        [Serializable]
        public class Entry
        {
            public string pedName;
            [Tooltip("Index into PedestrianSpawner.peoplePrefabs.")]
            public int spawnerIndex;
            [Tooltip("Humanoid avatar for the pedestrian's Biped rig (only used during the carjack clips).")]
            public Avatar humanoidAvatar;
            [Tooltip("The pedestrian baked in the seated driving pose, in driver seat space.")]
            public Mesh seatedMesh;
            [Tooltip("Where the pedestrian's root goes in driver seat space so its hips sit where the player's hips sit (smaller " +
                     "people would otherwise sink into the seat). Used for the baked mesh and the spawned pedestrian alike.")]
            public Vector3 seatOffset;
            [Tooltip("Seated hips (driver seat space): the point the driver is reclined about.")]
            public Vector3 seatedHips;
            [Tooltip("Centre of the seated head (driver seat space), where his head hit zone goes.")]
            public Vector3 seatedHeadCenter;
            [Tooltip("Top of the seated pedestrian's head (driver seat space, y).")]
            public float seatedCrownY;
            public Material[] materials = new Material[0];
            [Tooltip("The damageable pedestrian prefab this entry was baked from: the real person pulled out of the car.")]
            public GameObject pedPrefab;
            [Tooltip("Police officer: drives police cars only.")]
            public bool police;
        }

        [SerializeField] private Entry[] entries = new Entry[0];
        [Tooltip("Controller with the carjack states (Vehicle_Seated, Vehicle_PulledOut, stumbles, flinch layer).")]
        [SerializeField] private RuntimeAnimatorController carjackController;

        public int Count => entries.Length;
        public RuntimeAnimatorController CarjackController => carjackController;
        public Entry Get(int index) => index >= 0 && index < entries.Length ? entries[index] : null;
        public int RandomIndex() => entries.Length > 0 ? UnityEngine.Random.Range(0, entries.Length) : -1;

        /// <summary>A random civilian or police officer (a random anybody if the roster has none of that kind).</summary>
        public int RandomIndex(bool police)
        {
            int count = 0;
            for (int i = 0; i < entries.Length; i++) if (entries[i].police == police) count++;
            if (count == 0) return RandomIndex();
            int pick = UnityEngine.Random.Range(0, count);
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].police != police) continue;
                if (pick-- == 0) return i;
            }
            return -1;
        }

        /// <summary>The fit of entry <paramref name="index"/> in a car's list (no change when the car has none for him).</summary>
        public static SeatFit FitOf(SeatFit[] fits, int index) => fits != null && index >= 0 && index < fits.Length ? fits[index] : default;

        /// <summary>The visual's local pose for a fit: the mesh is in seat space, reclined about its hips, then lowered.</summary>
        public static void GetSeatedPose(Vector3 hips, SeatFit fit, out Vector3 position, out Quaternion rotation)
        {
            rotation = Quaternion.Euler(-fit.recline, 0f, 0f);
            position = hips - rotation * hips + Vector3.down * fit.drop;
        }

        /// <summary>Puts entry's seated mesh and materials on a driver visual (the first MeshFilter / MeshRenderer under it), full size, posed for this car's roof.</summary>
        public void ApplySeated(GameObject visual, int index, SeatFit fit = default)
        {
            Entry e = Get(index);
            if (visual == null || e == null || e.seatedMesh == null) return;
            var filter = visual.GetComponentInChildren<MeshFilter>(true);
            var renderer = filter != null ? filter.GetComponent<MeshRenderer>() : null;
            if (filter == null || renderer == null) return;
            filter.sharedMesh = e.seatedMesh;
            renderer.sharedMaterials = e.materials;
            GetSeatedPose(e.seatedHips, fit, out Vector3 position, out Quaternion rotation);
            visual.transform.localScale = Vector3.one;
            visual.transform.SetLocalPositionAndRotation(position, rotation);
        }

#if UNITY_EDITOR
        public void EditorSet(Entry[] newEntries, RuntimeAnimatorController controller)
        {
            entries = newEntries;
            carjackController = controller;
        }
#endif
    }
}
