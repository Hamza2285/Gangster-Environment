using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Where things go on the six "hivizcayne" towers (env/Building 01/hivizcayne ...). They are one model (a podium with two
    /// slab towers) placed at 0 / 90 / 270 degrees, so every spot is measured once on the un-rotated tower 'hivizcayne' (world
    /// x / z below) and carried over to the others through their own position and yaw. Heights always come from a ray on the
    /// tower's own collider (Environment Colliders/Building 01/...), so a tower set a little lower still gets its roof.
    /// Used by ParachuteSetup and RooftopAccessBuilder.
    /// </summary>
    public static class HivizcayneRoofLayout
    {
        public const string BuildingParent = "env/Building 01";
        public const string ColliderParent = "Environment Colliders/Building 01";
        public const string NamePrefix = "hivizcayne";

        // The un-rotated reference tower's pivot.
        private static readonly Vector2 ReferencePivot = new Vector2(-242.33f, 21.97f);

        // Spots on the reference tower (world x / z). The south slab's flat roof spans x -241 .. -202, z -14 .. -7 (y ~141.7),
        // with a centre arm out to z -19 (x -226 .. -218). Its street front (shop windows) is the wall at z -23.6.
        /// <summary>Stair-house (roof exit) centre, west end of the roof.</summary>
        public static readonly Vector2 StairHouse = new Vector2(-237.2f, -10.5f);
        /// <summary>In front of the stair-house door (faces +x): the roof's door trigger and marker.</summary>
        public static readonly Vector2 RoofDoor = new Vector2(-233.9f, -10.5f);
        /// <summary>Where the player arrives on the roof (faces +x).</summary>
        public static readonly Vector2 RoofArrival = new Vector2(-231.6f, -10.5f);
        /// <summary>Parachute bag pickup (east part of the roof).</summary>
        public static readonly Vector2 Pickup = new Vector2(-212.5f, -10.5f);
        /// <summary>
        /// Foot of the stairs up to the jump platform; the stairs climb towards -z (the street side) and end at z -17.0, just
        /// before the 4.4 m parapet (z -18.65 .. -19.25), which the platform then crosses.
        /// </summary>
        public static readonly Vector2 StairsFoot = new Vector2(-222f, -9.02f);
        /// <summary>Street front wall (z) of the reference tower.</summary>
        public const float StreetWallZ = -23.59f;
        /// <summary>Candidate x positions of the street door along the front wall (first free one wins).</summary>
        public static readonly float[] StreetDoorXs = { -222f, -226f, -214f, -230f, -210f, -234f };

        /// <summary>All hivizcayne towers in the open scene (the visible ones under env/Building 01).</summary>
        public static List<Transform> FindBuildings()
        {
            var list = new List<Transform>();
            GameObject parent = GameObject.Find(BuildingParent);
            if (parent == null) return list;
            foreach (Transform child in parent.transform)
                if (child.name.StartsWith(NamePrefix) && child.gameObject.activeInHierarchy) list.Add(child);
            return list;
        }

        /// <summary>The tower's own collider (same name under Environment Colliders), or any collider on the visible one.</summary>
        public static Collider FindCollider(Transform building)
        {
            GameObject parent = GameObject.Find(ColliderParent);
            if (parent != null)
            {
                Transform t = parent.transform.Find(building.name);
                if (t != null && t.TryGetComponent(out Collider c)) return c;
            }
            return building.GetComponentInChildren<Collider>();
        }

        public static Quaternion Yaw(Transform building) => Quaternion.Euler(0f, building.eulerAngles.y, 0f);

        /// <summary>A reference spot (x / z) on this tower, at the tower's base height.</summary>
        public static Vector3 ToWorld(Transform building, Vector2 reference)
        {
            Vector2 local = reference - ReferencePivot;
            return building.position + Yaw(building) * new Vector3(local.x, 0f, local.y);
        }

        /// <summary>A direction given on the reference tower (world), turned with this tower.</summary>
        public static Vector3 Direction(Transform building, Vector3 reference) => Yaw(building) * reference;

        /// <summary>Height of the tower's surface (roof) right above / below the point; NaN when the ray misses it.</summary>
        public static float SurfaceY(Collider buildingCollider, Vector3 point, float from = 400f)
        {
            if (buildingCollider != null && buildingCollider.Raycast(new Ray(new Vector3(point.x, from, point.z), Vector3.down), out RaycastHit hit, from + 100f))
                return hit.point.y;
            return float.NaN;
        }

        /// <summary>Ground (anything solid but the tower's roof) under a street point, from 3 m above the tower base.</summary>
        public static float GroundY(Vector3 point, float baseY)
        {
            var hits = Physics.RaycastAll(new Vector3(point.x, baseY + 3f, point.z), Vector3.down, 10f, ~(1 << 2), QueryTriggerInteraction.Ignore);
            float best = float.NaN;
            foreach (RaycastHit hit in hits)
                if (float.IsNaN(best) || hit.point.y > best) best = hit.point.y;
            return float.IsNaN(best) ? baseY : best;
        }
    }
}
