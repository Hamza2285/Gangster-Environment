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
            [Tooltip("Seated hips (driver seat space): a car whose roof is too low for this pedestrian scales it about this point.")]
            public Vector3 seatedHips;
            [Tooltip("Top of the seated pedestrian's head (driver seat space, y).")]
            public float seatedCrownY;
            public Material[] materials = new Material[0];
        }

        [SerializeField] private Entry[] entries = new Entry[0];
        [Tooltip("Controller with the carjack states (Vehicle_Seated, Vehicle_PulledOut, stumbles, flinch layer).")]
        [SerializeField] private RuntimeAnimatorController carjackController;

        public int Count => entries.Length;
        public RuntimeAnimatorController CarjackController => carjackController;
        public Entry Get(int index) => index >= 0 && index < entries.Length ? entries[index] : null;
        public int RandomIndex() => entries.Length > 0 ? UnityEngine.Random.Range(0, entries.Length) : -1;

        /// <summary>
        /// Scale (about the seated hips) that keeps entry's head under <paramref name="crownLimit"/> (the highest crown the car's
        /// roof allows, seat space; 0 = no limit). 1 in most cars; only tall people in low cars sit a little smaller.
        /// </summary>
        public float SeatedScale(int index, float crownLimit)
        {
            Entry e = Get(index);
            if (e == null || crownLimit <= 0f || e.seatedCrownY <= crownLimit) return 1f;
            return Mathf.Clamp((crownLimit - e.seatedHips.y) / Mathf.Max(0.1f, e.seatedCrownY - e.seatedHips.y), 0.75f, 1f);
        }

        /// <summary>Puts entry's seated mesh and materials on a driver visual (the first MeshFilter / MeshRenderer under it).</summary>
        public void ApplySeated(GameObject visual, int index, float crownLimit = 0f)
        {
            Entry e = Get(index);
            if (visual == null || e == null || e.seatedMesh == null) return;
            var filter = visual.GetComponentInChildren<MeshFilter>(true);
            var renderer = filter != null ? filter.GetComponent<MeshRenderer>() : null;
            if (filter == null || renderer == null) return;
            filter.sharedMesh = e.seatedMesh;
            renderer.sharedMaterials = e.materials;
            // The visual sits on the driver seat with the mesh in seat space: scaling it about the hips keeps them on the seat.
            float s = SeatedScale(index, crownLimit);
            visual.transform.localScale = Vector3.one * s;
            visual.transform.localPosition = e.seatedHips * (1f - s);
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
