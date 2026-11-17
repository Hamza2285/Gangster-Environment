using UnityEngine;
using UnityEngine.AI;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Car-sized NavMesh obstacle set-up shared by the editor tools and the runtime fallback (vehicles whose prefabs are not ours
    /// get it from <see cref="RCCVehicleAdapter"/> at start): a box round the solid colliders, carved into the NavMesh only while
    /// the vehicle stands still, so officers on foot walk round parked cars instead of through them.
    /// </summary>
    public static class VehicleNavMeshObstacle
    {
        public static void Configure(NavMeshObstacle obstacle, GameObject vehicle)
        {
            Bounds bounds = new Bounds(vehicle.transform.position, Vector3.zero);
            bool any = false;
            foreach (Collider c in vehicle.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger || c is WheelCollider) continue;
                if (!any) { bounds = c.bounds; any = true; }
                else bounds.Encapsulate(c.bounds);
            }
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = vehicle.transform.InverseTransformPoint(bounds.center);
            obstacle.size = any ? new Vector3(bounds.size.x, Mathf.Max(1f, bounds.size.y), bounds.size.z) : new Vector3(2f, 1.5f, 4.5f);
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
            obstacle.carvingMoveThreshold = 0.3f;
            obstacle.carvingTimeToStationary = 0.5f;
        }
    }
}
