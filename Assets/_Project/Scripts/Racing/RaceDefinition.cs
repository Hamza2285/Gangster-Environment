using UnityEngine;

namespace OpenworldCrime.Racing
{
    public enum RaceAvailability { Always, DayOnly, NightOnly }

    /// <summary>
    /// One street race (authored data, never changed at runtime): the route along the city's traffic lanes, its checkpoints,
    /// start grid, time-of-day availability and rewards. Routes are generated from the iTS lane graph by the race builder
    /// (Tools > OpenworldCrime > Racing), so every point lies on a road the traffic uses.
    /// </summary>
    [CreateAssetMenu(menuName = "OpenworldCrime/Racing/Race Definition", fileName = "Race_")]
    public class RaceDefinition : ScriptableObject
    {
        [Header("Race")]
        [SerializeField] private string raceId = "race_1";
        [SerializeField] private string displayName = "Street Race";
        [SerializeField] private RaceAvailability availability = RaceAvailability.Always;
        [Tooltip("1 = sprint from start to finish; more = laps of a circuit (the route ends where it starts).")]
        [SerializeField, Min(1)] private int laps = 1;
        [SerializeField, Range(1, 5)] private int opponents = 3;

        [Header("Rewards (cash by finishing position: 1st, 2nd, 3rd...)")]
        [SerializeField] private int[] rewards = { 1500, 750, 300 };

        [Header("Route (generated)")]
        [Tooltip("Points every few metres along the lanes, in driving order (one lap for circuits).")]
        [SerializeField] private Vector3[] routePoints = new Vector3[0];
        [Tooltip("Advised speed (m/s) at each route point, from the road's curvature (for the AI drivers).")]
        [SerializeField] private float[] routeSpeeds = new float[0];
        [Tooltip("Route point index of each checkpoint, in order. The last one is the finish line (start line for circuits).")]
        [SerializeField] private int[] checkpoints = new int[0];
        [Tooltip("Route point index of the start line.")]
        [SerializeField] private int startIndex;
        [Tooltip("Grid slots (position, yaw): slot 0 is the player's.")]
        [SerializeField] private Vector3[] gridPositions = new Vector3[0];
        [SerializeField] private float[] gridYaws = new float[0];
        [SerializeField] private float lapLength;

        public string RaceId => raceId;
        public string DisplayName => displayName;
        public RaceAvailability Availability => availability;
        public int Laps => laps;
        public bool IsCircuit => laps > 1;
        public int Opponents => opponents;
        public Vector3[] RoutePoints => routePoints;
        public float[] RouteSpeeds => routeSpeeds;
        public int[] Checkpoints => checkpoints;
        public int StartIndex => startIndex;
        public Vector3[] GridPositions => gridPositions;
        public float[] GridYaws => gridYaws;
        public float LapLength => lapLength;
        public int TotalCheckpoints => checkpoints.Length * laps;

        public bool IsAvailable(bool night) =>
            availability == RaceAvailability.Always || (availability == RaceAvailability.NightOnly) == night;

        public int RewardFor(int position) => position >= 1 && position <= rewards.Length ? rewards[position - 1] : 0;

        public string AvailabilityLabel => availability == RaceAvailability.DayOnly ? "Day only"
            : availability == RaceAvailability.NightOnly ? "Night only" : "Day and night";

#if UNITY_EDITOR
        /// <summary>Editor only: the race builder writes the generated route.</summary>
        public void EditorSet(string id, string title, RaceAvailability when, int lapCount, int opponentCount, int[] cash,
            Vector3[] points, float[] speeds, int[] checkpointIndices, int start, Vector3[] grid, float[] yaws, float length)
        {
            raceId = id; displayName = title; availability = when; laps = lapCount; opponents = opponentCount; rewards = cash;
            routePoints = points; routeSpeeds = speeds; checkpoints = checkpointIndices; startIndex = start;
            gridPositions = grid; gridYaws = yaws; lapLength = length;
        }
#endif
    }
}
