using System;
using OpenworldCrime.Player;
using OpenworldCrime.Vehicles;
using UnityEngine;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// The outer limit of the whole map (city, airport island and a wide sea margin around them). One per scene, on the
    /// "World Boundary" object (not a manager, not a Singleton: aircraft get it by Inspector reference). Two layers:
    /// - Soft limit (<see cref="PlayArea"/> + <see cref="SoftCeiling"/>): beyond it aircraft are smoothly turned / pushed back
    ///   (<see cref="Evaluate"/>, used by <see cref="PlaneController"/> and <see cref="HelicopterController"/>), and the HUD shows
    ///   a "turn back" warning while the player is out there (<see cref="OnPlayerOutsideChanged"/>).
    /// - Hard limit: invisible walls Hard Margin further out on all four sides plus a lid at Hard Ceiling (built when the game
    ///   starts, Ignore Raycast layer like Hamza's <c>WorldBounds</c> walls: they stop bodies, cars, swimmers and aircraft, while
    ///   bullets, the camera and ground checks pass through). A last resort only: nothing normally gets that far.
    /// Set up / resized by Tools > OpenworldCrime > World > Set Up World Boundary (computes the land bounds from the scene).
    /// </summary>
    [DisallowMultipleComponent]
    public class WorldBoundary : MonoBehaviour
    {
        [Header("Soft Limit (aircraft turned back, HUD warning)")]
        [Tooltip("World x / z area that is free to fly / swim / drive in. Beyond it the turn-back starts.")]
        [SerializeField] private Rect playArea = Rect.MinMaxRect(-1365f, -1633f, 1709f, 1423f);
        [Tooltip("Distance (m) beyond the play area at which the turn-back is at full strength.")]
        [SerializeField, Min(10f)] private float pushDepth = 200f;
        [Tooltip("Aircraft are pushed down above this height (world y).")]
        [SerializeField] private float softCeiling = 700f;
        [Tooltip("Height (m) above the soft ceiling at which the push down is at full strength.")]
        [SerializeField, Min(5f)] private float ceilingPushDepth = 80f;

        [Header("Hard Limit (invisible walls, last resort)")]
        [Tooltip("The walls stand this far (m) outside the play area, on every side.")]
        [SerializeField, Min(10f)] private float hardMargin = 350f;
        [Tooltip("World y of the invisible lid over the whole map.")]
        [SerializeField] private float hardCeiling = 900f;
        [SerializeField] private float wallBottom = -100f;
        [SerializeField, Min(1f)] private float wallThickness = 20f;

        [Header("Player Warning")]
        [Tooltip("How often (s) the player's position is checked for the HUD warning.")]
        [SerializeField, Min(0.05f)] private float checkInterval = 0.25f;

        /// <summary>The played character (or the vehicle they are in) went beyond the soft limit (true) or came back (false);
        /// the second value is true when they are in an aircraft.</summary>
        public static event Action<bool, bool> OnPlayerOutsideChanged;

        public Rect PlayArea => playArea;
        public float SoftCeiling => softCeiling;
        public Rect HardArea => Rect.MinMaxRect(playArea.xMin - hardMargin, playArea.yMin - hardMargin, playArea.xMax + hardMargin, playArea.yMax + hardMargin);
        public float HardCeiling => hardCeiling;
        /// <summary>True while the played character is beyond the soft limit.</summary>
        public bool PlayerOutside { get; private set; }

        private float nextCheck;
        private bool playerFlying;

        private void Awake() => BuildWalls();

        private void OnDisable()
        {
            if (PlayerOutside) SetPlayerOutside(false, playerFlying);
        }

        /// <summary>
        /// How far a point is beyond the soft limit. <paramref name="inward"/>: flat unit direction back into the play area
        /// (zero inside); <paramref name="outside01"/>: 0 at the edge .. 1 at Push Depth beyond it; <paramref name="above01"/>:
        /// 0 at the soft ceiling .. 1 at Ceiling Push Depth above it. True when either is above 0. A few float compares.
        /// </summary>
        public bool Evaluate(Vector3 position, out Vector3 inward, out float outside01, out float above01)
        {
            float dx = 0f, dz = 0f;
            if (position.x < playArea.xMin) dx = playArea.xMin - position.x;
            else if (position.x > playArea.xMax) dx = playArea.xMax - position.x;
            if (position.z < playArea.yMin) dz = playArea.yMin - position.z;
            else if (position.z > playArea.yMax) dz = playArea.yMax - position.z;
            float depth = Mathf.Sqrt(dx * dx + dz * dz);
            inward = depth > 0.001f ? new Vector3(dx / depth, 0f, dz / depth) : Vector3.zero;
            outside01 = Mathf.Clamp01(depth / pushDepth);
            above01 = Mathf.Clamp01((position.y - softCeiling) / ceilingPushDepth);
            return outside01 > 0f || above01 > 0f;
        }

        // ------------------------------------------------------------------ player warning

        private void Update()
        {
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + checkInterval;
            IGamePlayer player = GamePlayer.Current;
            if (player == null || player.transform == null || !player.transform.gameObject.activeInHierarchy)
            {
                if (PlayerOutside) SetPlayerOutside(false, playerFlying);
                return;
            }
            Transform tracked = player.transform;
            bool flying = false;
            if (player.transform.TryGetComponent(out PlayerVehicleInteraction interaction) && interaction.CurrentVehicle != null
                && interaction.State != VehicleInteractionState.OnFoot && interaction.CurrentVehicle is Component vehicle)
            {
                tracked = vehicle.transform;
                flying = interaction.CurrentVehicle is IFlyingVehicle;
            }
            Vector3 p = tracked.position;
            // Interiors stand far outside the map (the player is teleported there): only near the map counts.
            Rect near = HardArea;
            bool nearMap = p.x > near.xMin - 500f && p.x < near.xMax + 500f && p.z < near.yMax + 500f && p.z > near.yMin - 500f;
            bool outside = nearMap && Evaluate(p, out _, out float o, out float a) && (o > 0f || (flying && a > 0f));
            if (outside != PlayerOutside || (outside && flying != playerFlying)) SetPlayerOutside(outside, flying);
        }

        private void SetPlayerOutside(bool outside, bool flying)
        {
            PlayerOutside = outside;
            playerFlying = flying;
            OnPlayerOutsideChanged?.Invoke(outside, flying);
        }

        // ------------------------------------------------------------------ hard walls

        private void BuildWalls()
        {
            Rect hard = HardArea;
            float height = hardCeiling - wallBottom, midY = (hardCeiling + wallBottom) * 0.5f, t = wallThickness;
            Wall("Wall West", new Vector3(hard.xMin - t * 0.5f, midY, hard.center.y), new Vector3(t, height, hard.height + t * 2f));
            Wall("Wall East", new Vector3(hard.xMax + t * 0.5f, midY, hard.center.y), new Vector3(t, height, hard.height + t * 2f));
            Wall("Wall South", new Vector3(hard.center.x, midY, hard.yMin - t * 0.5f), new Vector3(hard.width + t * 2f, height, t));
            Wall("Wall North", new Vector3(hard.center.x, midY, hard.yMax + t * 0.5f), new Vector3(hard.width + t * 2f, height, t));
            Wall("Lid", new Vector3(hard.center.x, hardCeiling + t * 0.5f, hard.center.y), new Vector3(hard.width + t * 2f, t, hard.height + t * 2f));
        }

        private void Wall(string wallName, Vector3 centre, Vector3 size)
        {
            var go = new GameObject(wallName) { layer = 2 }; // Ignore Raycast
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(centre, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            go.AddComponent<BoxCollider>().size = size;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => OnPlayerOutsideChanged = null;

#if UNITY_EDITOR
        public void EditorSetup(Rect area, float margin, float ceiling, float lid)
        {
            playArea = area;
            hardMargin = margin;
            softCeiling = ceiling;
            hardCeiling = lid;
        }

        private void OnDrawGizmos()
        {
            Rect hard = HardArea;
            Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.9f);
            float midSoft = (wallBottom + softCeiling) * 0.5f;
            Gizmos.DrawWireCube(new Vector3(playArea.center.x, midSoft, playArea.center.y), new Vector3(playArea.width, softCeiling - wallBottom, playArea.height));
            Gizmos.color = new Color(1f, 0.15f, 0.1f, 0.9f);
            float midHard = (wallBottom + hardCeiling) * 0.5f;
            Gizmos.DrawWireCube(new Vector3(hard.center.x, midHard, hard.center.y), new Vector3(hard.width, hardCeiling - wallBottom, hard.height));
        }
#endif
    }
}
