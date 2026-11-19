using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenworldCrime.Player;
using OpenworldCrime.Police;
using OpenworldCrime.Racing;
using OpenworldCrime.UI;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Builds the street races (Tools > OpenworldCrime > Racing > Build Street Races, city scene open):
    /// - Routes: six races generated on the iTS traffic lane graph (shortest lane paths through random waypoints), so every
    ///   point is on a road the traffic drives, one way the right way: 3 sprints and 3 two-lap circuits, 2 day-only, 2 night-only,
    ///   2 always. Starts spread over the city on long straight lanes; each route is checked (ground under every few metres,
    ///   no lane twice on sprints, grid slots clear) and the report is logged.
    /// - Assets: RaceDefinition per race, start marker and checkpoint gate prefabs, materials, map icon.
    /// - Scene (our objects only): "Street Races" root with the start points and the gates, RaceManager under ///Manager///,
    ///   Race HUD in Gameplay UI.
    /// </summary>
    public static class RaceBuilder
    {
        private const string DataFolder = "Assets/_Project/Data/Racing";
        private const string ArtFolder = "Assets/_Project/Art/Racing";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Racing";
        private const float Spacing = 4f;
        private const float StartOffset = 70f; // start line this far along the start lane (the grid sits behind it)

        private class RaceSpec
        {
            public string id, title;
            public RaceAvailability when;
            public int laps;
            public float minLength, maxLength;
            public int[] rewards;
        }

        private static readonly RaceSpec[] Specs =
        {
            new RaceSpec { id = "race_sunset_sprint", title = "Sunset Sprint", when = RaceAvailability.DayOnly, laps = 1, minLength = 1900f, maxLength = 2900f, rewards = new[] { 1500, 700, 300 } },
            new RaceSpec { id = "race_harbor_loop", title = "Harbor Loop", when = RaceAvailability.DayOnly, laps = 2, minLength = 1000f, maxLength = 1500f, rewards = new[] { 1800, 850, 350 } },
            new RaceSpec { id = "race_neon_mile", title = "Neon Mile", when = RaceAvailability.NightOnly, laps = 1, minLength = 1900f, maxLength = 2900f, rewards = new[] { 2000, 900, 400 } },
            new RaceSpec { id = "race_midnight_circuit", title = "Midnight Circuit", when = RaceAvailability.NightOnly, laps = 2, minLength = 1000f, maxLength = 1500f, rewards = new[] { 2400, 1100, 450 } },
            new RaceSpec { id = "race_downtown_dash", title = "Downtown Dash", when = RaceAvailability.Always, laps = 1, minLength = 1500f, maxLength = 2400f, rewards = new[] { 1200, 550, 250 } },
            new RaceSpec { id = "race_purple_ring", title = "Purple Ring", when = RaceAvailability.Always, laps = 2, minLength = 900f, maxLength = 1400f, rewards = new[] { 1600, 750, 300 } },
        };

        private static readonly string[] OpponentCars =
        {
            "Drivable_Car1", "Drivable_Car2", "Drivable_Car3", "Drivable_Car4", "Drivable_Car5", "Drivable_NewCar1", "Drivable_NewCar3", "Drivable_Talenter"
        };

        // ------------------------------------------------------------------ lane graph

        private class Lane
        {
            public List<Vector3> points = new List<Vector3>();
            public float length;
            public List<(int next, List<Vector3> points, float length, bool straight)> connectors = new List<(int, List<Vector3>, float, bool)>();
        }

        private static List<Lane> lanes;

        private static bool LoadLanes(StringBuilder log)
        {
            var mm = Object.FindFirstObjectByType<TSMainManager>();
            if (mm == null || mm.lanes == null) { log.AppendLine("- no iTS TSMainManager in the open scene"); return false; }
            lanes = new List<Lane>();
            foreach (TSLaneInfo info in mm.lanes)
            {
                var lane = new Lane();
                foreach (TSPoints p in info.points) lane.points.Add(p.point);
                lane.length = PolyLength(lane.points);
                if (info.connectors != null)
                    foreach (TSLaneConnector c in info.connectors)
                    {
                        var cp = new List<Vector3>();
                        if (c.points != null) foreach (TSConnectorPoint p in c.points) cp.Add(p.point);
                        lane.connectors.Add((c.nextLane, cp, PolyLength(cp), c.direction.ToString() == "Straight"));
                    }
                lanes.Add(lane);
            }
            return true;
        }

        // Shortest lane path from the end of lane a to the start of lane b (lanes after a, b included). Null when unreachable.
        private static List<int> ShortestPath(int a, int b, HashSet<int> avoid)
        {
            int n = lanes.Count;
            var dist = new float[n];
            var prev = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
            foreach (var c in lanes[a].connectors)
            {
                if (c.next < 0 || c.next >= n || (avoid != null && avoid.Contains(c.next) && c.next != b)) continue;
                float d = c.length + lanes[c.next].length;
                if (d < dist[c.next]) { dist[c.next] = d; prev[c.next] = -2 - a; }
            }
            while (true)
            {
                int u = -1;
                float best = float.MaxValue;
                for (int i = 0; i < n; i++) if (!done[i] && dist[i] < best) { best = dist[i]; u = i; }
                if (u < 0) return null;
                if (u == b) break;
                done[u] = true;
                foreach (var c in lanes[u].connectors)
                {
                    if (c.next < 0 || c.next >= n || done[c.next] || (avoid != null && avoid.Contains(c.next) && c.next != b)) continue;
                    float d = dist[u] + c.length + lanes[c.next].length;
                    if (d < dist[c.next]) { dist[c.next] = d; prev[c.next] = u; }
                }
            }
            var path = new List<int>();
            for (int v = b; v >= 0; v = prev[v]) path.Add(v);
            path.Reverse();
            return path;
        }

        private static (List<Vector3> pts, float len, bool straight) Connector(int from, int to)
        {
            foreach (var c in lanes[from].connectors) if (c.next == to) return (c.points, c.length, c.straight);
            return (new List<Vector3>(), 0f, true);
        }

        // ------------------------------------------------------------------ build

        [MenuItem("Tools/OpenworldCrime/Racing/Build Street Races (city scene)")]
        public static void BuildMenu() => Debug.Log(Build(12345));

        public static string Build(int seed)
        {
            var log = new StringBuilder("[RaceBuilder]\n");
            if (!LoadLanes(log)) return log.ToString();
            MissedGround.Clear();
            EnsureFolder(DataFolder);
            EnsureFolder(ArtFolder);
            EnsureFolder(PrefabFolder);
            var random = new System.Random(seed);

            // Spread-out start lanes in order of preference; a race that finds no route takes the next unused one.
            List<int> starts = PickStarts(Specs.Length * 3, log);
            var races = new List<RaceDefinition>();
            var used = new HashSet<int>();
            for (int r = 0; r < Specs.Length; r++)
            {
                RaceDefinition race = null;
                foreach (int start in starts)
                {
                    if (used.Contains(start)) continue;
                    race = BuildRace(Specs[r], start, random, log);
                    if (race == null) continue;
                    used.Add(start);
                    break;
                }
                if (race != null) races.Add(race);
            }
            AssetDatabase.SaveAssets();

            Material markerMat = EnsureAdditiveMaterial(ArtFolder + "/RaceMarkerGlow.mat", new Color(0.62f, 0.32f, 1f, 0.22f));
            Material gateMat = EnsureAdditiveMaterial(ArtFolder + "/RaceGateGlow.mat", new Color(1f, 0.82f, 0.25f, 0.85f));
            Texture2D flagTex = EnsureFlagTexture(ArtFolder + "/RaceFlag.png");
            Material flagMat = EnsureFlagMaterial(ArtFolder + "/RaceFlag.mat", flagTex);
            Sprite icon = EnsureMapIcon(ArtFolder + "/RaceMapIcon.png");
            GameObject startPrefab = BuildStartMarkerPrefab(markerMat, flagMat);
            GameObject gatePrefab = BuildGatePrefab(gateMat);
            WireScene(races, startPrefab, gatePrefab, icon, log);
            return log.ToString();
        }

        // Long, straight lanes spread over the city (farthest-point sampling from the centre).
        private static List<int> PickStarts(int count, StringBuilder log)
        {
            var candidates = new List<int>();
            Vector3 min = Vector3.one * 1e9f, max = -min;
            for (int i = 0; i < lanes.Count; i++)
            {
                Lane l = lanes[i];
                if (l.points.Count < 2) continue;
                foreach (Vector3 p in l.points) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                if (l.length < 150f || l.connectors.Count == 0) continue;
                if (Vector3.Distance(l.points[0], l.points[l.points.Count - 1]) / l.length < 0.97f) continue;
                if (Mathf.Abs(l.points[0].y - l.points[l.points.Count - 1].y) > 3f) continue;
                candidates.Add(i);
            }
            Vector3 centre = (min + max) * 0.5f;
            var picked = new List<int>();
            int first = -1;
            float bestD = float.MaxValue;
            foreach (int c in candidates)
            {
                float d = (PointAt(lanes[c].points, StartOffset) - centre).sqrMagnitude;
                if (d < bestD) { bestD = d; first = c; }
            }
            if (first < 0) { log.AppendLine("- no start lane candidates"); return picked; }
            picked.Add(first);
            while (picked.Count < count)
            {
                int best = -1;
                float bestMin = -1f;
                foreach (int c in candidates)
                {
                    if (picked.Contains(c)) continue;
                    Vector3 p = PointAt(lanes[c].points, StartOffset);
                    float m = float.MaxValue;
                    foreach (int q in picked) m = Mathf.Min(m, (PointAt(lanes[q].points, StartOffset) - p).sqrMagnitude);
                    if (m > bestMin) { bestMin = m; best = c; }
                }
                if (best < 0) break;
                picked.Add(best);
            }
            log.AppendLine($"- {candidates.Count} start lane candidates, picked {picked.Count}");
            return picked;
        }

        private static RaceDefinition BuildRace(RaceSpec spec, int startLane, System.Random random, StringBuilder log)
        {
            bool circuit = spec.laps > 1;
            float target = (spec.minLength + spec.maxLength) * 0.5f;
            List<Vector3> best = null;
            float bestScore = float.MaxValue;
            int bestTurns = 0;
            for (int attempt = 0; attempt < 700; attempt++)
            {
                int w1 = random.Next(lanes.Count), w2 = random.Next(lanes.Count);
                if (lanes[w1].length < 25f || lanes[w2].length < 25f || w1 == startLane || w2 == startLane || w1 == w2) continue;
                Vector3 s = PointAt(lanes[startLane].points, StartOffset);
                float d1 = Vector3.Distance(Mid(w1), s), d2 = Vector3.Distance(Mid(w2), s), d12 = Vector3.Distance(Mid(w1), Mid(w2));
                if (circuit && (d1 < 150f || d2 < 150f || d12 < 150f || d1 + d2 + d12 > spec.maxLength * 1.1f)) continue;
                if (!circuit && (d2 < 500f || d1 < 200f || d1 + d12 > spec.maxLength)) continue;

                var visited = new HashSet<int> { startLane };
                List<int> p1 = ShortestPath(startLane, w1, visited);
                if (p1 == null) continue;
                foreach (int l in p1) visited.Add(l);
                List<int> p2 = ShortestPath(w1, w2, visited);
                if (p2 == null) continue;
                var laneSeq = new List<int> { startLane };
                laneSeq.AddRange(p1);
                laneSeq.AddRange(p2);
                if (circuit)
                {
                    foreach (int l in p2) visited.Add(l);
                    visited.Remove(startLane);
                    List<int> p3 = ShortestPath(w2, startLane, visited);
                    if (p3 == null) continue;
                    laneSeq.AddRange(p3);
                }
                // No lane twice (one exception: the start lane closing a circuit).
                var seen = new HashSet<int>();
                bool repeat = false;
                for (int i = 0; i < laneSeq.Count; i++)
                {
                    if (circuit && i == laneSeq.Count - 1 && laneSeq[i] == startLane) continue;
                    if (!seen.Add(laneSeq[i])) { repeat = true; break; }
                }
                if (repeat) continue;

                List<Vector3> poly = Polyline(laneSeq, circuit, out int turns);
                float len = PolyLength(poly);
                if (len < spec.minLength || len > spec.maxLength) continue;
                // Close to the target length, not too many turns, and (circuits) not doubling back on its own streets.
                float score = Mathf.Abs(len - target) / target + Mathf.Max(0, turns - (circuit ? 6 : 10)) * 0.05f + SelfCrowding(poly) * 2f;
                if (score < bestScore) { bestScore = score; best = poly; bestTurns = turns; }
            }
            if (best == null) { log.AppendLine($"- {spec.title}: no route found"); return null; }

            List<Vector3> route = Resample(best, Spacing, circuit);
            float lapLength = PolyLength(route) + (circuit ? Vector3.Distance(route[route.Count - 1], route[0]) : 0f);
            float[] speeds = AdvisedSpeeds(route, circuit);
            int[] checkpoints = Checkpoints(route, circuit, circuit ? 170f : 190f);
            BuildGrid(startLane, out Vector3[] grid, out float[] yaws);
            int groundMisses = ValidateGround(route, grid, out int gridBlocked);

            string path = $"{DataFolder}/{spec.id}.asset";
            var race = AssetDatabase.LoadAssetAtPath<RaceDefinition>(path);
            if (race == null) { race = ScriptableObject.CreateInstance<RaceDefinition>(); AssetDatabase.CreateAsset(race, path); }
            race.EditorSet(spec.id, spec.title, spec.when, spec.laps, 3, spec.rewards, route.ToArray(), speeds, checkpoints, 0, grid, yaws, lapLength);
            EditorUtility.SetDirty(race);
            log.AppendLine($"- {spec.title} ({spec.when}, {(circuit ? spec.laps + " laps" : "sprint")}): start {route[0]:F0}, lap {lapLength:F0} m, " +
                           $"{checkpoints.Length} checkpoints, {bestTurns} turns, ground misses {groundMisses}/{route.Count / 5}, grid blocked {gridBlocked}");
            return race;
        }

        private static Vector3 Mid(int lane) => PointAt(lanes[lane].points, lanes[lane].length * 0.5f);

        // Start lane from the start line, then connector + lane for each lane; circuits end on the start lane at the start line.
        private static List<Vector3> Polyline(List<int> laneSeq, bool circuit, out int turns)
        {
            turns = 0;
            var poly = new List<Vector3>();
            List<Vector3> startPts = lanes[laneSeq[0]].points;
            poly.AddRange(Slice(startPts, StartOffset, float.MaxValue));
            for (int i = 1; i < laneSeq.Count; i++)
            {
                var c = Connector(laneSeq[i - 1], laneSeq[i]);
                if (!c.straight) turns++;
                poly.AddRange(c.pts);
                bool closing = circuit && i == laneSeq.Count - 1;
                poly.AddRange(closing ? Slice(lanes[laneSeq[i]].points, 0f, StartOffset - Spacing) : lanes[laneSeq[i]].points);
            }
            // Drop duplicates.
            var clean = new List<Vector3>();
            foreach (Vector3 p in poly) if (clean.Count == 0 || (clean[clean.Count - 1] - p).sqrMagnitude > 0.04f) clean.Add(p);
            return clean;
        }

        // Share of route points that pass close to a far-away part of the same route (parallel streets / doubling back).
        private static float SelfCrowding(List<Vector3> poly)
        {
            List<Vector3> r = Resample(poly, 20f, false);
            int crowded = 0;
            for (int i = 0; i < r.Count; i++)
                for (int j = i + 8; j < r.Count; j++)
                    if ((r[i] - r[j]).sqrMagnitude < 15f * 15f && !(i < 3 && j > r.Count - 4)) { crowded++; break; }
            return r.Count > 0 ? (float)crowded / r.Count : 1f;
        }

        private static float[] AdvisedSpeeds(List<Vector3> route, bool circuit)
        {
            int n = route.Count;
            var v = new float[n];
            const int w = 3;
            for (int i = 0; i < n; i++)
            {
                int a = circuit ? (i - w + n) % n : Mathf.Max(0, i - w);
                int b = circuit ? (i + w) % n : Mathf.Min(n - 1, i + w);
                Vector3 d1 = route[i] - route[a], d2 = route[b] - route[i];
                d1.y = d2.y = 0f;
                float angle = d1.sqrMagnitude > 0.01f && d2.sqrMagnitude > 0.01f ? Vector3.Angle(d1, d2) * Mathf.Deg2Rad : 0f;
                float arc = (d1.magnitude + d2.magnitude);
                float radius = angle > 0.01f ? arc / angle : 1000f;
                v[i] = Mathf.Clamp(Mathf.Sqrt(9f * radius), 10f, 42f);
            }
            // Braking: the speed before a corner falls off at 7 m/s^2.
            for (int pass = 0; pass < 2; pass++)
                for (int i = n - 2; i >= 0; i--)
                {
                    float ds = Vector3.Distance(route[i], route[i + 1]);
                    v[i] = Mathf.Min(v[i], Mathf.Sqrt(v[i + 1] * v[i + 1] + 2f * 7f * ds));
                }
            return v;
        }

        private static int[] Checkpoints(List<Vector3> route, bool circuit, float spacing)
        {
            var list = new List<int>();
            float d = 0f, next = spacing;
            for (int i = 1; i < route.Count; i++)
            {
                d += Vector3.Distance(route[i - 1], route[i]);
                if (d >= next && i < route.Count - 12) { list.Add(i); next = d + spacing; }
            }
            list.Add(route.Count - 1); // finish (circuits: the start line, a few metres before it)
            return list.ToArray();
        }

        private static void BuildGrid(int startLane, out Vector3[] grid, out float[] yaws)
        {
            const int slots = 6;
            grid = new Vector3[slots];
            yaws = new float[slots];
            List<Vector3> pts = lanes[startLane].points;
            for (int k = 0; k < slots; k++)
            {
                float along = StartOffset - 8f - k * 7.5f;
                Vector3 p = PointAt(pts, along);
                Vector3 f = PointAt(pts, along + 2f) - PointAt(pts, along - 2f);
                f.y = 0f;
                f.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, f);
                p += right * ((k % 2 == 0) ? 0.9f : -0.9f);
                grid[k] = Ground(p);
                yaws[k] = Quaternion.LookRotation(f, Vector3.up).eulerAngles.y;
            }
        }

        private static Vector3 Ground(Vector3 p)
        {
            RaycastHit[] hits = Physics.RaycastAll(p + Vector3.up * 4f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 result = p;
            foreach (RaycastHit h in hits)
            {
                if (h.collider.attachedRigidbody != null) continue;
                if (h.distance < best) { best = h.distance; result = h.point; }
            }
            return result;
        }

        /// <summary>Route points without road under them in the last build (for inspection).</summary>
        public static readonly List<Vector3> MissedGround = new List<Vector3>();

        // Static ground (not a car or a character) within 2.5 m of the point.
        private static bool HasGround(Vector3 p)
        {
            foreach (RaycastHit h in Physics.RaycastAll(p + Vector3.up * 4f, Vector3.down, 6.5f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider.attachedRigidbody == null && Mathf.Abs(h.point.y - p.y) <= 2.5f) return true;
            return false;
        }

        private static int ValidateGround(List<Vector3> route, Vector3[] grid, out int gridBlocked)
        {
            int misses = 0;
            for (int i = 0; i < route.Count; i += 5)
            {
                if (!HasGround(route[i])) { misses++; MissedGround.Add(route[i]); }
            }
            gridBlocked = 0;
            foreach (Vector3 p in grid)
            {
                foreach (Collider c in Physics.OverlapBox(p + Vector3.up * 1.2f, new Vector3(1f, 0.8f, 2.2f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                    if (c.attachedRigidbody == null) { gridBlocked++; break; }
            }
            return misses;
        }

        // ------------------------------------------------------------------ polyline helpers

        private static float PolyLength(List<Vector3> p)
        {
            float d = 0f;
            for (int i = 1; i < p.Count; i++) d += Vector3.Distance(p[i - 1], p[i]);
            return d;
        }

        private static Vector3 PointAt(List<Vector3> p, float distance)
        {
            if (p.Count == 0) return Vector3.zero;
            if (distance <= 0f) return p[0];
            float d = 0f;
            for (int i = 1; i < p.Count; i++)
            {
                float seg = Vector3.Distance(p[i - 1], p[i]);
                if (d + seg >= distance) return Vector3.Lerp(p[i - 1], p[i], (distance - d) / Mathf.Max(seg, 0.0001f));
                d += seg;
            }
            return p[p.Count - 1];
        }

        private static List<Vector3> Slice(List<Vector3> p, float from, float to)
        {
            var result = new List<Vector3>();
            float total = PolyLength(p);
            to = Mathf.Min(to, total);
            if (to <= from) return result;
            result.Add(PointAt(p, from));
            float d = 0f;
            for (int i = 1; i < p.Count; i++)
            {
                d += Vector3.Distance(p[i - 1], p[i]);
                if (d > from && d < to) result.Add(p[i]);
            }
            result.Add(PointAt(p, to));
            return result;
        }

        private static List<Vector3> Resample(List<Vector3> p, float step, bool closed)
        {
            var result = new List<Vector3>();
            float total = PolyLength(p);
            for (float d = 0f; d <= total; d += step) result.Add(PointAt(p, d));
            return result;
        }

        // ------------------------------------------------------------------ assets

        private static Material EnsureAdditiveMaterial(string path, Color colour)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f); // additive
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetColor("_BaseColor", colour);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material EnsureFlagMaterial(string path, Texture2D tex)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Texture2D EnsureFlagTexture(string path)
        {
            if (!File.Exists(path))
            {
                var t = new Texture2D(64, 40, TextureFormat.RGBA32, false);
                for (int y = 0; y < 40; y++)
                    for (int x = 0; x < 64; x++)
                        t.SetPixel(x, y, ((x / 8) + (y / 8)) % 2 == 0 ? Color.white : new Color(0.08f, 0.05f, 0.12f));
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Chequered flag on a pole, white on transparent (the blip background gives the purple theme colour).
        private static Sprite EnsureMapIcon(string path)
        {
            const int s = 64;
            var t = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var clear = new Color(1f, 1f, 1f, 0f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    Color c = clear;
                    if (x >= 14 && x <= 18 && y >= 8 && y <= 56) c = Color.white; // pole
                    else if (x > 18 && x <= 52 && y >= 30 && y <= 56)
                    {
                        bool white = ((x - 19) / 6 + (y - 30) / 6) % 2 == 0;
                        c = white ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                    }
                    t.SetPixel(x, y, c);
                }
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static GameObject BuildStartMarkerPrefab(Material glow, Material flag)
        {
            string path = PrefabFolder + "/RaceStartMarker.prefab";
            var root = new GameObject("RaceStartMarker");
            GameObject disc = Primitive(PrimitiveType.Cylinder, "Ground Ring", root.transform, new Vector3(0f, 0.08f, 0f), new Vector3(9f, 0.04f, 9f), glow);
            GameObject beam = Primitive(PrimitiveType.Cylinder, "Beam", root.transform, new Vector3(0f, 9f, 0f), new Vector3(1.2f, 9f, 1.2f), glow);
            GameObject pole = Primitive(PrimitiveType.Cylinder, "Flag Pole", root.transform, new Vector3(-1.6f, 2.2f, 0f), new Vector3(0.08f, 2.2f, 0.08f), flag);
            GameObject cloth = Primitive(PrimitiveType.Quad, "Flag", root.transform, new Vector3(-0.6f, 3.8f, 0f), new Vector3(2f, 1.25f, 1f), flag);
            root.AddComponent<OpenworldCrime.Interiors.MarkerBob>(); // the whole marker turns slowly and bobs a little
            disc.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject BuildGatePrefab(Material glow)
        {
            string meshPath = ArtFolder + "/RaceGateRing.asset";
            Mesh ring = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (ring == null) { ring = Torus(6f, 0.35f, 48, 10); AssetDatabase.CreateAsset(ring, meshPath); }
            string path = PrefabFolder + "/RaceCheckpointGate.prefab";
            var root = new GameObject("RaceCheckpointGate");
            var visual = new GameObject("Ring");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 5.4f, 0f);
            visual.AddComponent<MeshFilter>().sharedMesh = ring;
            var mr = visual.AddComponent<MeshRenderer>();
            mr.sharedMaterial = glow;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var marker = root.AddComponent<RaceCheckpointMarker>();
            var so = new SerializedObject(marker);
            SetArray(so.FindProperty("renderers"), new Object[] { mr });
            so.ApplyModifiedPropertiesWithoutUndo();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // A ring standing upright, facing +Z.
        private static Mesh Torus(float radius, float tube, int segments, int sides)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector3 centre = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius;
                for (int j = 0; j <= sides; j++)
                {
                    float b = j * Mathf.PI * 2f / sides;
                    Vector3 normal = new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(a) * Mathf.Cos(b), Mathf.Sin(b));
                    verts.Add(centre + normal * tube);
                }
            }
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a = i * (sides + 1) + j, b = (i + 1) * (sides + 1) + j;
                    tris.AddRange(new[] { a, b, a + 1, b, b + 1, a + 1 });
                }
            var mesh = new Mesh { name = "RaceGateRing" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        // ------------------------------------------------------------------ scene

        private static void WireScene(List<RaceDefinition> races, GameObject startPrefab, GameObject gatePrefab, Sprite icon, StringBuilder log)
        {
            var scene = EditorSceneManager.GetActiveScene();
            GameObject managerRoot = FindRoot(scene, "///Manager///");
            GameObject ui = FindRoot(scene, "Gameplay UI");
            var wanted = Object.FindFirstObjectByType<WantedManager>(FindObjectsInactive.Include);
            if (managerRoot == null || ui == null || wanted == null) { log.AppendLine("- scene not wired: needs ///Manager///, Gameplay UI and the WantedManager"); return; }
            var wantedSo = new SerializedObject(wanted);
            var vehicleInteraction = wantedSo.FindProperty("vehicleInteraction").objectReferenceValue as PlayerVehicleInteraction;
            var playerManager = wantedSo.FindProperty("playerManager").objectReferenceValue as OpenworldCrime.Player.PlayerManager; // qualified: SharedAssets has a global PlayerManager
            PlayerHealth health = playerManager != null ? playerManager.GetComponentInChildren<PlayerHealth>(true) : null;

            GameObject root = FindRoot(scene, "Street Races");
            if (root != null) Object.DestroyImmediate(root);
            root = new GameObject("Street Races");
            SceneManagerMove(root, scene);

            var points = new List<RaceStartPoint>();
            foreach (RaceDefinition race in races)
            {
                var go = new GameObject("Race Start - " + race.DisplayName);
                go.transform.SetParent(root.transform, false);
                Vector3 p = race.RoutePoints[0];
                Vector3 f = race.RoutePoints[Mathf.Min(3, race.RoutePoints.Length - 1)] - p;
                f.y = 0f;
                go.transform.SetPositionAndRotation(Ground(p), Quaternion.LookRotation(f.normalized, Vector3.up));
                var marker = (GameObject)PrefabUtility.InstantiatePrefab(startPrefab, go.transform);
                marker.name = "Marker";
                var blip = go.AddComponent<MapBlip>();
                var blipSo = new SerializedObject(blip);
                blipSo.FindProperty("icon").objectReferenceValue = icon;
                blipSo.FindProperty("size").floatValue = 30f;
                blipSo.ApplyModifiedPropertiesWithoutUndo();
                var start = go.AddComponent<RaceStartPoint>();
                Assign(start, ("race", race), ("marker", marker), ("blip", blip));
                points.Add(start);
            }

            var gates = new GameObject("Race Gates");
            gates.transform.SetParent(root.transform, false);
            var next = (GameObject)PrefabUtility.InstantiatePrefab(gatePrefab, gates.transform);
            next.name = "Next Checkpoint";
            var following = (GameObject)PrefabUtility.InstantiatePrefab(gatePrefab, gates.transform);
            following.name = "Following Checkpoint";
            next.SetActive(false);
            following.SetActive(false);
            var cpBlip = new GameObject("Checkpoint Blip");
            cpBlip.transform.SetParent(gates.transform, false);
            var b = cpBlip.AddComponent<MapBlip>();
            var bSo = new SerializedObject(b);
            bSo.FindProperty("icon").objectReferenceValue = icon;
            bSo.FindProperty("iconColour").colorValue = new Color(0.1f, 0.06f, 0.15f, 1f);
            bSo.FindProperty("backgroundColour").colorValue = new Color(1f, 0.82f, 0.25f, 0.95f);
            bSo.FindProperty("size").floatValue = 26f;
            bSo.FindProperty("tracksMovement").boolValue = true;
            bSo.ApplyModifiedPropertiesWithoutUndo();
            cpBlip.SetActive(false);

            Transform existing = managerRoot.transform.Find("RaceManager");
            GameObject managerGo = existing != null ? existing.gameObject : new GameObject("RaceManager");
            managerGo.transform.SetParent(managerRoot.transform, false);
            var manager = GetOrAdd<RaceManager>(managerGo.gameObject);
            var opponentList = new List<Object>();
            foreach (string car in OpponentCars)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<RCCVehicleAdapter>($"Assets/_Project/Prefabs/Vehicles/Drivable/{car}.prefab");
                if (prefab != null) opponentList.Add(prefab);
            }
            Assign(manager, ("dayNight", Object.FindFirstObjectByType<CityDayNightController>(FindObjectsInactive.Include)),
                ("vehicleInteraction", vehicleInteraction), ("playerHealth", health), ("fader", ui.GetComponentInChildren<ScreenFader>(true)),
                ("nextMarker", next.GetComponent<RaceCheckpointMarker>()), ("followingMarker", following.GetComponent<RaceCheckpointMarker>()),
                ("checkpointBlip", b));
            var mso = new SerializedObject(manager);
            SetArray(mso.FindProperty("startPoints"), points.ToArray());
            SetArray(mso.FindProperty("opponentPrefabs"), opponentList.ToArray());
            mso.ApplyModifiedPropertiesWithoutUndo();

            BuildHud(ui.transform, manager);
            EditorSceneManager.MarkSceneDirty(scene);
            log.AppendLine($"- scene: 'Street Races' ({points.Count} starts, 2 gates, checkpoint blip), RaceManager under ///Manager///, " +
                           $"Race HUD; {opponentList.Count} opponent cars; player refs {(vehicleInteraction != null ? vehicleInteraction.name : "MISSING")}");
        }

        private static void SceneManagerMove(GameObject go, UnityEngine.SceneManagement.Scene scene)
        {
            if (go.scene != scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
        }

        // ------------------------------------------------------------------ HUD

        private static Font font;
        private static Sprite panelSprite;

        private static void BuildHud(Transform ui, RaceManager manager)
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            panelSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Transform old = ui.Find("Race HUD");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            RectTransform hud = Rect("Race HUD", ui, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var panelColour = new Color(0.1f, 0.06f, 0.16f, 0.82f);
            var accent = new Color(1f, 0.82f, 0.25f, 1f);
            var purple = new Color(0.55f, 0.3f, 0.85f, 1f);

            // Prompt (bottom centre, above the driving controls).
            RectTransform prompt = Panel("Prompt", hud, new Vector2(0.5f, 0f), new Vector2(0f, 230f), new Vector2(520f, 150f), panelColour);
            Text promptTitle = NewText("Title", prompt, 30, TextAnchor.UpperCenter, accent, new Vector2(0f, -10f), new Vector2(500f, 40f), new Vector2(0.5f, 1f));
            Text promptInfo = NewText("Info", prompt, 20, TextAnchor.UpperCenter, Color.white, new Vector2(0f, -52f), new Vector2(500f, 30f), new Vector2(0.5f, 1f));
            Button start = NewButton("Start Button", prompt, "START RACE", purple, new Vector2(0f, 14f), new Vector2(260f, 52f), new Vector2(0.5f, 0f), out Text startLabel);

            // Racing panel (top right under the minimap area: left side to stay clear of it).
            RectTransform race = Rect("Racing", hud, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            RectTransform info = Panel("Info", race, new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(300f, 130f), panelColour, new Vector2(0f, 1f));
            Text position = NewText("Position", info, 44, TextAnchor.UpperLeft, accent, new Vector2(16f, -6f), new Vector2(280f, 52f), new Vector2(0f, 1f));
            Text progress = NewText("Progress", info, 20, TextAnchor.UpperLeft, Color.white, new Vector2(16f, -60f), new Vector2(280f, 28f), new Vector2(0f, 1f));
            Text time = NewText("Time", info, 26, TextAnchor.UpperLeft, Color.white, new Vector2(16f, -90f), new Vector2(280f, 32f), new Vector2(0f, 1f));
            Button respawn = NewButton("Respawn Button", race, "RESET [BKSP]", purple, new Vector2(20f, -160f), new Vector2(170f, 46f), new Vector2(0f, 1f), out _);
            Button quit = NewButton("Quit Button", race, "QUIT [X]", new Color(0.6f, 0.15f, 0.25f, 1f), new Vector2(200f, -160f), new Vector2(120f, 46f), new Vector2(0f, 1f), out _);
            RectTransform wrong = Panel("Wrong Way", race, new Vector2(0.5f, 0.5f), new Vector2(0f, 140f), new Vector2(360f, 70f), new Color(0.75f, 0.1f, 0.15f, 0.85f));
            NewText("Text", wrong, 40, TextAnchor.MiddleCenter, Color.white, Vector2.zero, new Vector2(360f, 70f), new Vector2(0.5f, 0.5f)).text = "WRONG WAY";
            Text countdown = NewText("Countdown", hud, 140, TextAnchor.MiddleCenter, Color.white, new Vector2(0f, 60f), new Vector2(500f, 200f), new Vector2(0.5f, 0.5f));
            countdown.gameObject.AddComponent<Outline>().effectDistance = new Vector2(4f, -4f);

            // Message and results.
            RectTransform message = Panel("Message", hud, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(560f, 56f), panelColour);
            Text messageText = NewText("Text", message, 24, TextAnchor.MiddleCenter, Color.white, Vector2.zero, new Vector2(540f, 56f), new Vector2(0.5f, 0.5f));
            RectTransform results = Panel("Results", hud, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 400f), new Color(0.08f, 0.05f, 0.13f, 0.92f));
            Text resultsTitle = NewText("Title", results, 46, TextAnchor.UpperCenter, accent, new Vector2(0f, -20f), new Vector2(500f, 60f), new Vector2(0.5f, 1f));
            Text resultsBody = NewText("Body", results, 24, TextAnchor.UpperCenter, Color.white, new Vector2(0f, -90f), new Vector2(480f, 220f), new Vector2(0.5f, 1f));
            Button cont = NewButton("Continue Button", results, "CONTINUE", purple, new Vector2(0f, 24f), new Vector2(240f, 56f), new Vector2(0.5f, 0f), out _);

            var view = hud.gameObject.AddComponent<RaceHudView>();
            Assign(view, ("manager", manager), ("promptPanel", prompt.gameObject), ("promptTitle", promptTitle), ("promptInfo", promptInfo),
                ("startButton", start), ("startLabel", startLabel), ("racePanel", race.gameObject), ("positionText", position),
                ("progressText", progress), ("timeText", time), ("respawnButton", respawn), ("quitButton", quit),
                ("wrongWayPanel", wrong.gameObject), ("countdownText", countdown), ("messagePanel", message.gameObject),
                ("messageText", messageText), ("resultsPanel", results.gameObject), ("resultsTitle", resultsTitle),
                ("resultsBody", resultsBody), ("continueButton", cont));
            prompt.gameObject.SetActive(false);
            race.gameObject.SetActive(false);
            message.gameObject.SetActive(false);
            results.gameObject.SetActive(false);
            wrong.gameObject.SetActive(false);
            countdown.gameObject.SetActive(false);
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        private static RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color colour, Vector2? pivot = null)
        {
            RectTransform rt = Rect(name, parent, anchor, anchor, position, size, pivot ?? anchor);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = panelSprite;
            image.type = Image.Type.Sliced;
            image.color = colour;
            return rt;
        }

        private static Text NewText(string name, Transform parent, int size, TextAnchor anchor, Color colour, Vector2 position, Vector2 box, Vector2 pivotAnchor)
        {
            RectTransform rt = Rect(name, parent, pivotAnchor, pivotAnchor, position, box, pivotAnchor);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = colour;
            text.fontStyle = FontStyle.Bold;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Button NewButton(string name, Transform parent, string label, Color colour, Vector2 position, Vector2 size, Vector2 anchor, out Text text)
        {
            RectTransform rt = Panel(name, parent, anchor, position, size, colour, anchor);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = rt.GetComponent<Image>();
            text = NewText("Label", rt, 22, TextAnchor.MiddleCenter, Color.white, Vector2.zero, size, new Vector2(0.5f, 0.5f));
            text.text = label;
            return button;
        }

        // ------------------------------------------------------------------ utility

        private static GameObject FindRoot(UnityEngine.SceneManagement.Scene scene, string name)
        {
            foreach (GameObject go in scene.GetRootGameObjects()) if (go.name == name) return go;
            return null;
        }

        private static void Assign(Object target, params (string name, Object value)[] values)
        {
            var so = new SerializedObject(target);
            foreach (var (n, v) in values)
            {
                SerializedProperty p = so.FindProperty(n);
                if (p == null) { Debug.LogError($"[RaceBuilder] No property '{n}' on {target.GetType().Name}"); continue; }
                p.objectReferenceValue = v;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(SerializedProperty array, Object[] values)
        {
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace(Path.DirectorySeparatorChar, '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // Explicit null check: in the editor GetComponent returns a "fake null" for missing built-in components, which ?? misses.
        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }
}
