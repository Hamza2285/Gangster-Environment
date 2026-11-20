using Invector;
using Invector.vCharacterController;
using OpenworldCrime.Player;
using UnityEngine;

// The edge of the playable world, on the "World Bounds" object (Game/World Bounds Setup places it).
// - Invisible walls stand along the edge of the map, so nobody walks, drives or flies off it.
// - In the city the player dies at once in the sea (below the water surface, on foot or in a car) or when falling out of
//   the world. Interiors (far outside the map) are left alone.
//   The underpass road dips below the water line, so ground that belongs to the road / walkway colliders is dry land.
// The walls are made when the game starts from Area, so changing the area here is all it takes.
[DisallowMultipleComponent]
public class WorldBounds : MonoBehaviour
{
    [Tooltip("Playable area on the ground (x / z, world). The walls stand on its edges.")]
    public Rect area = Rect.MinMaxRect(-810f, -1173f, 1208f, 845f);
    [Tooltip("Height of the invisible walls (world y of their top).")]
    public float wallTop = 400f;
    public float wallBottom = -100f;
    public float wallThickness = 4f;

    [Header("Death")]
    [Tooltip("World height of the sea surface.")]
    public float waterLevel = 26f;
    [Tooltip("How far (m) below the surface the player's feet (or car) may go before he drowns.")]
    public float drownDepth = 0.5f;
    [Tooltip("Anyone below this height has fallen out of the world.")]
    public float killFloor = -60f;
    [Tooltip("Roots of the colliders that are dry ground even below the water line (roads and walkways).")]
    public string[] dryGroundRoots = { "Traffic Road Colliders", "Walk Ground Colliders" };

    static readonly RaycastHit[] hits = new RaycastHit[16];
    Transform[] dryRoots;
    vThirdPersonController killed;
    float nextCheck;

    void Awake()
    {
        BuildWalls();
        dryRoots = new Transform[dryGroundRoots.Length];
        for (int i = 0; i < dryGroundRoots.Length; i++)
        {
            var go = GameObject.Find(dryGroundRoots[i]);
            if (go != null) dryRoots[i] = go.transform;
        }
    }

    void BuildWalls()
    {
        float height = wallTop - wallBottom, midY = (wallTop + wallBottom) * 0.5f;
        float t = wallThickness;
        Wall("Wall West", new Vector3(area.xMin - t * 0.5f, midY, area.center.y), new Vector3(t, height, area.height + t * 2f));
        Wall("Wall East", new Vector3(area.xMax + t * 0.5f, midY, area.center.y), new Vector3(t, height, area.height + t * 2f));
        Wall("Wall South", new Vector3(area.center.x, midY, area.yMin - t * 0.5f), new Vector3(area.width + t * 2f, height, t));
        Wall("Wall North", new Vector3(area.center.x, midY, area.yMax + t * 0.5f), new Vector3(area.width + t * 2f, height, t));
    }

    void Wall(string wallName, Vector3 center, Vector3 size)
    {
        var go = new GameObject(wallName);
        go.transform.SetParent(transform, false);
        go.transform.position = center;
        // Ignore Raycast: blocks bodies and cars, but bullets, the camera and ground checks pass through.
        go.layer = 2;
        go.AddComponent<BoxCollider>().size = size;
    }

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.1f;

        var player = GamePlayer.Current;
        var controller = player != null ? player.transform.GetComponent<vThirdPersonController>() : null;
        // Driving switches the controller off, so only the object has to be live.
        if (controller == null || !controller.gameObject.activeInHierarchy) return;
        if (controller.currentHealth <= 0f) return;
        if (killed == controller) killed = null; // alive again (hospital respawn)

        var interaction = controller.GetComponent<PlayerVehicleInteraction>();
        Transform vehicle = interaction != null && interaction.CurrentVehicle != null && interaction.State != VehicleInteractionState.OnFoot
            ? ((Component)interaction.CurrentVehicle).transform : null;
        Vector3 point = vehicle != null ? vehicle.position : controller.transform.position;

        // Shop, club and showroom interiors stand far outside the map (the player is teleported there): only the city is checked.
        // Walking or driving out of it is what the walls stop.
        if (!area.Contains(new Vector2(point.x, point.z))) return;
        bool fellOut = point.y < killFloor;
        bool drowned = point.y < waterLevel - drownDepth && !OnDryGround(point, vehicle != null ? vehicle : controller.transform);
        if (!fellOut && !drowned) return;

        // The death and the hospital respawn need him on foot: a driver is taken out of the car first, where the car is (in
        // the water, just below the surface), and dies on the next check.
        if (interaction != null && interaction.State == VehicleInteractionState.InVehicle)
        {
            Vector3 at = drowned ? new Vector3(point.x, Mathf.Min(point.y, waterLevel - drownDepth - 0.3f), point.z) : point;
            Vector3 facing = Vector3.ProjectOnPlane(vehicle.forward, Vector3.up);
            interaction.ForceExitAt(at, facing.sqrMagnitude > 0.01f ? Quaternion.LookRotation(facing) : Quaternion.identity);
            return;
        }
        if (interaction != null && interaction.State != VehicleInteractionState.OnFoot) return; // getting in / out: next check
        Kill(controller);
    }

    // A road / walkway collider just under the point (the visible road mesh often lies exactly on it) means dry land.
    bool OnDryGround(Vector3 point, Transform self)
    {
        int n = Physics.RaycastNonAlloc(point + Vector3.up * 1f, Vector3.down, hits, 4f, ~(1 << 2), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Transform hit = hits[i].collider.transform;
            if (hit.IsChildOf(self)) continue;
            foreach (Transform root in dryRoots)
                if (root != null && hit.IsChildOf(root)) return true;
        }
        return false;
    }

    void Kill(vThirdPersonController controller)
    {
        if (killed == controller) return;
        killed = controller;
        controller.TakeDamage(new vDamage
        {
            damageValue = Mathf.CeilToInt(controller.MaxHealth * 10f),
            ignoreDefense = true,
            hitReaction = false,
            damageType = "Drowned",
            hitPosition = controller.transform.position,
        });
        // In a car the controller can be switched off for damage: zero health is what the hospital respawn watches.
        if (controller.currentHealth > 0f) controller.ChangeHealth(0);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.6f, 0.8f);
        Vector3 c = new Vector3(area.center.x, (wallTop + wallBottom) * 0.5f, area.center.y);
        Gizmos.DrawWireCube(c, new Vector3(area.width, wallTop - wallBottom, area.height));
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.5f);
        Gizmos.DrawWireCube(new Vector3(area.center.x, waterLevel, area.center.y), new Vector3(area.width, 0.1f, area.height));
    }
}
