using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.Trailer
{
    // The shot list. Every shot is a function of its local time (lt seconds, u = 0-1 progress).
    public partial class TrailerDirector
    {
        private const string DrivablePath = "Assets/_Project/Prefabs/Vehicles/Drivable/";
        private const float Road = 27.7f;

        private void BuildShots()
        {
            shots.Clear();
            ShotOpening();          // 1  ocean -> bridge, sunset -> night
            ShotSearchlights();     // 2  aerial, rooftop searchlights
            ShotNeonBoulevard();    // 3  street level, neon + traffic
            ShotCrossing();         // 4  crossing panel chase lights, traffic
            ShotPedTrack();         // 5  pedestrian side tracking
            ShotPedFollow();        // 6  pedestrian follow
            ShotClubEntrance();     // 7  club neon entrance
            ShotClubWide();         // 8  club interior wide (DJ, lights)
            ShotClubFloor();        // 9  dance floor
            ShotMeetOrbit();        // 10 car meet orbit
            ShotMeetLow();          // 11 car meet low slider
            ShotDriftChase();       // 12 black Challenger drift, chase cam
            ShotDriftSide();        // 13 drift, roadside
            ShotDriftFront();       // 14 drift, front
            ShotChaseAerial();      // 15 cop chase, drone
            ShotChaseFront();       // 16 cop chase, front low
            ShotChaseCrash();       // 17 near miss + crash
            ShotFinale();           // 18 skyline + searchlights, fade out
        }

        private void Night() { SetTimeOfDay(22f, 1f); }

        // ================================================================== 1-4 city

        // 1. Ocean at sunset; the camera pulls back over the water to reveal the cable-stayed bridge and the skyline while the
        //    sky runs a fast time-lapse into night (city lights, bridge LEDs and rooftop searchlights come on).
        private void ShotOpening()
        {
            var s = new Shot { Name = "S01_Ocean", Duration = 14f, Anchor = new Vector3(380f, Road, -440f), Warmup = 4f };
            s.Enter = () => SetTimeOfDay(18.2f, 0f);
            s.Update = (lt, u) =>
            {
                float k = Smoother((lt - 3f) / 8f);
                SetTimeOfDay(Mathf.Lerp(18.2f, 21.2f, k), k);
                float m = Smooth(u);
                Vector3 p = Lerp(new Vector3(335f, 28.3f, -505f), new Vector3(345f, 52f, -690f), m);
                Vector3 l = Lerp(new Vector3(345f, 66f, -275f), new Vector3(335f, 62f, -270f), m);
                Cam(p, l, Mathf.Lerp(38f, 52f, m));
            };
            shots.Add(s);
        }

        // 2. Drone arc past a tower crown with its searchlight beams sweeping the night sky.
        private void ShotSearchlights()
        {
            var s = new Shot { Name = "S02_Searchlights", Duration = 5f, Anchor = new Vector3(640f, Road, -200f), Warmup = 2f };
            s.Enter = Night;
            s.Update = (lt, u) =>
            {
                Vector3 c = new Vector3(594f, 150f, -251f);
                float a = Mathf.Lerp(54f, 34f, Smooth(u)) * Mathf.Deg2Rad;
                float r = Mathf.Lerp(205f, 190f, u);
                Vector3 p = c + new Vector3(Mathf.Sin(a) * r, Mathf.Lerp(20f, 28f, u), Mathf.Cos(a) * r);
                Cam(p, new Vector3(594f, Mathf.Lerp(150f, 158f, u), -251f), 55f);
            };
            shots.Add(s);
        }

        // ---- staged traffic (kinematic cars on iTS lanes; the hidden player is parked far away so no AI traffic interferes)

        private class Mover { public TrailerCar Car; public TrailerPath Path; public float S0, Speed; }
        private readonly List<Mover> movers = new List<Mover>();
        private readonly Dictionary<string, TrailerPath> lanePaths = new Dictionary<string, TrailerPath>();

        private TrailerPath LanePath(params int[] lanes)
        {
            string key = string.Join(",", lanes);
            TrailerPath p;
            if (!lanePaths.TryGetValue(key, out p)) { p = new TrailerPath(TrailerPath.FromLanes(lanes), 0f, 3); lanePaths[key] = p; }
            return p;
        }

        private void AddMover(string prefab, string colour, TrailerPath path, float s0, float speed)
        {
            var car = SpawnCar(prefab, colour, path.Pos(s0), path.Yaw(s0));
            if (car != null) movers.Add(new Mover { Car = car, Path = path, S0 = s0, Speed = speed });
        }

        private void PoseMovers(float lt)
        {
            foreach (var m in movers)
            {
                float sp = m.S0 + m.Speed * lt;
                float steer = Mathf.Clamp(Mathf.Atan(2.8f * m.Path.Turn(sp) * Mathf.Deg2Rad) * Mathf.Rad2Deg, -30f, 30f);
                m.Car.Pose(m.Path.Pos(sp), m.Path.Yaw(sp), steer, 0f, 0f, FrameDt);
            }
        }

        private static readonly Vector3 FarAnchor = new Vector3(-150f, Road, -600f);

        // 3. Low dolly up the neon boulevard's median, traffic passing both ways.
        private void ShotNeonBoulevard()
        {
            var s = new Shot { Name = "S03_Boulevard", Duration = 4.5f, Anchor = FarAnchor, Warmup = 1.5f };
            s.Enter = () =>
            {
                Night(); cars.Clear(); movers.Clear();
                AddMover("Drivable_Car4", "Silver", LanePath(423), 126f, 13.5f);
                AddMover("Drivable_Khodge2", "RaceRed", LanePath(424), 98f, 18f);
                AddMover("Drivable_GWagon", "Black", LanePath(421), 62f, 12f);
                AddMover("Drivable_Car1", "PearlWhite", LanePath(422), 112f, 11f);
            };
            s.Update = (lt, u) =>
            {
                PoseMovers(lt);
                Vector3 p = Lerp(new Vector3(633.6f, 28.7f, -121f), new Vector3(634.4f, 29.5f, -92f), Smooth(u));
                Cam(p, p + new Vector3(3f, 2.0f, 60f), 52f, Mathf.Lerp(-1.5f, 1f, u));
            };
            shots.Add(s);
        }

        // 4. Crane down over an intersection: crossing panels and their chase lights, cars crossing.
        private void ShotCrossing()
        {
            var s = new Shot { Name = "S04_Crossing", Duration = 4f, Anchor = FarAnchor, Warmup = 1.5f };
            s.Enter = () =>
            {
                Night(); cars.Clear(); movers.Clear();
                AddMover("Drivable_Car3", "ElectricBlue", LanePath(1, 17), 106f, 12f);
                AddMover("Drivable_WMB", "Yellow", LanePath(26, 10), 164f, 14f);
                AddMover("Drivable_Car5", "White", LanePath(12, 23), 61f, 13f);
                AddMover("Drivable_Talenter", "MetallicPurple", LanePath(15, 2), 62f, 11f);
            };
            s.Update = (lt, u) =>
            {
                PoseMovers(lt);
                float m = Smooth(u);
                Vector3 p = Lerp(new Vector3(400f, 56f, 158f), new Vector3(410f, 42f, 170f), m);
                Cam(p, new Vector3(428f, Road, Mathf.Lerp(192f, 197f, m)), 58f);
            };
            shots.Add(s);
        }

        // ================================================================== 5-6 pedestrians

        // Pedestrians walk by script (their NavMeshAgents are off), so speed and heading come from position changes.
        private readonly Dictionary<Transform, Vector3> pedLast = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<Transform, Vector3> pedVel = new Dictionary<Transform, Vector3>();
        private Transform pedTarget;
        private Vector3 pedDir, pedSmoothPos;

        private void TrackPeds(Vector3 near, float radius)
        {
            foreach (var agent in FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None))
            {
                Transform t = agent.transform;
                if (!t.name.StartsWith("Ped_") || t.name.Contains("Police") || t.name.Contains("Cop") || (t.position - near).sqrMagnitude > radius * radius) continue;
                Vector3 last;
                if (pedLast.TryGetValue(t, out last))
                {
                    Vector3 v = (t.position - last) / FrameDt; v.y = 0f;
                    if (v.magnitude > 6f) v = Vector3.zero; // teleport / respawn
                    Vector3 old;
                    pedVel[t] = pedVel.TryGetValue(t, out old) ? Vector3.Lerp(old, v, 0.15f) : v;
                }
                pedLast[t] = t.position;
            }
        }

        private float pedSide = 1f;

        // Clear view from a camera offset (side metres to the right, back metres behind) along the walker's next seconds.
        private static bool ClearView(Vector3 pos, Vector3 vel, float side, float back)
        {
            Vector3 dir = vel.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            for (float t = 0f; t <= 4.5f; t += 0.75f)
            {
                Vector3 p = pos + vel * t + Vector3.up * 1.35f;
                Vector3 c = p + right * side - dir * back + Vector3.up * 0.15f;
                Vector3 from = p + (c - p).normalized * 0.5f;
                if (Physics.Linecast(from, c, ~0, QueryTriggerInteraction.Ignore)) return false;
            }
            return true;
        }

        private Transform PickPedestrian(Vector3 near, float radius, float back)
        {
            var list = new List<KeyValuePair<float, Transform>>();
            foreach (var kv in pedVel)
            {
                if (kv.Key == null || !kv.Key.gameObject.activeInHierarchy) continue;
                if (kv.Value.magnitude < 0.7f) continue;
                float d = Vector3.Distance(kv.Key.position, near);
                if (d <= radius) list.Add(new KeyValuePair<float, Transform>(d, kv.Key));
            }
            list.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (var kv in list)
            {
                Vector3 v = pedVel[kv.Value];
                foreach (float side in back > 0f ? new[] { 0.9f, -0.9f } : new[] { 3.0f, -3.0f })
                    if (ClearView(kv.Value.position, v, side, back)) { pedSide = Mathf.Sign(side); return kv.Value; }
            }
            if (list.Count > 0) { pedSide = 1f; Debug.LogWarning("[Trailer] No walker with a clear view; using the nearest."); return list[0].Value; }
            Debug.LogWarning("[Trailer] No walking pedestrian near " + near + " (" + pedVel.Count + " tracked)");
            return null;
        }

        private void UpdatePedTarget()
        {
            if (pedTarget == null) return;
            Vector3 last;
            if (pedLast.TryGetValue(pedTarget, out last))
            {
                Vector3 v = (pedTarget.position - last) / FrameDt; v.y = 0f;
                if (v.magnitude < 6f)
                {
                    Vector3 old;
                    pedVel[pedTarget] = pedVel.TryGetValue(pedTarget, out old) ? Vector3.Lerp(old, v, 0.15f) : v;
                }
            }
            pedLast[pedTarget] = pedTarget.position;
            Vector3 vel;
            if (pedVel.TryGetValue(pedTarget, out vel) && vel.magnitude > 0.4f) pedDir = Vector3.Slerp(pedDir, vel.normalized, 0.05f).normalized;
            pedSmoothPos = Vector3.Lerp(pedSmoothPos, pedTarget.position, 0.35f);
        }

        private void BeginPedShot()
        {
            Night(); pedLast.Clear(); pedVel.Clear(); pedTarget = null;
        }

        private void ReadyPed(Vector3 near, float radius, float back)
        {
            pedTarget = PickPedestrian(near, radius, back);
            if (pedTarget == null) return;
            pedDir = pedVel[pedTarget].normalized;
            pedSmoothPos = pedTarget.position;
        }

        // 5. Side tracking shot of someone walking past the neon facades.
        private void ShotPedTrack()
        {
            var s = new Shot { Name = "S05_PedTrack", Duration = 4.5f, Anchor = new Vector3(715f, Road, 200f), Warmup = 5f };
            s.Enter = BeginPedShot;
            s.Ready = () => ReadyPed(s.Anchor, 70f, 0f);
            s.Update = (lt, u) =>
            {
                if (pedTarget == null)
                {
                    if (lt == 0f) TrackPeds(s.Anchor, 80f);
                    Cam(new Vector3(700f, 29f, 190f), new Vector3(730f, 30f, 205f), 50f);
                    return;
                }
                UpdatePedTarget();
                Vector3 right = Vector3.Cross(Vector3.up, pedDir);
                Vector3 p = pedSmoothPos;
                Cam(p + right * (3.0f * pedSide) + pedDir * Mathf.Lerp(1.5f, 0.3f, u) + Vector3.up * 1.45f, p + Vector3.up * 1.2f + pedDir * 0.4f, 45f);
            };
            shots.Add(s);
        }

        // 6. Follow behind another walker down the lit street.
        private void ShotPedFollow()
        {
            var s = new Shot { Name = "S06_PedFollow", Duration = 3.5f, Anchor = new Vector3(640f, Road, -60f), Warmup = 5f };
            s.Enter = BeginPedShot;
            s.Ready = () => ReadyPed(s.Anchor, 60f, 3.3f);
            s.Update = (lt, u) =>
            {
                if (pedTarget == null)
                {
                    if (lt == 0f) TrackPeds(s.Anchor, 70f);
                    Cam(new Vector3(630f, 29f, -90f), new Vector3(640f, 30f, -40f), 50f);
                    return;
                }
                UpdatePedTarget();
                Vector3 right = Vector3.Cross(Vector3.up, pedDir);
                Vector3 p = pedSmoothPos;
                Cam(p - pedDir * Mathf.Lerp(3.6f, 3.0f, u) + right * (0.9f * pedSide) + Vector3.up * 1.65f, p + pedDir * 8f + Vector3.up * 1.3f, 50f);
            };
            shots.Add(s);
        }

        // ================================================================== 7-9 club

        // 7. Push in on the club's neon entrance.
        private void ShotClubEntrance()
        {
            var s = new Shot { Name = "S07_ClubDoor", Duration = 3.5f, Anchor = new Vector3(738f, Road, 168f), Warmup = 3f };
            s.Enter = Night;
            s.Update = (lt, u) =>
            {
                float m = Smooth(u);
                Cam(Lerp(new Vector3(722f, 28.8f, 176f), new Vector3(737f, 29.4f, 185f), m), new Vector3(755f, Mathf.Lerp(32.5f, 31.6f, m), 189f), Mathf.Lerp(48f, 42f, m));
            };
            shots.Add(s);
        }

        private Light cityMainLight;
        private readonly List<Behaviour> clubVolumes = new List<Behaviour>();

        private void EnterClub()
        {
            if (!clubScene.isLoaded) return;
            SceneManager.SetActiveScene(clubScene);
            if (cityMainLight == null && dayNight != null) cityMainLight = dayNight.sun;
            if (cityMainLight != null) cityMainLight.enabled = false;
            foreach (var v in clubVolumes) if (v != null) v.enabled = true;
        }

        private void ExitClub()
        {
            Scene city = dayNight != null ? dayNight.gameObject.scene : SceneManager.GetSceneAt(0);
            SceneManager.SetActiveScene(city);
            if (cityMainLight != null) cityMainLight.enabled = true;
            foreach (var v in clubVolumes) if (v != null) v.enabled = false;
            Night();
        }

        // 8. Wide across the club: DJ booth, disco lights, crowd.
        private void ShotClubWide()
        {
            var s = new Shot { Name = "S08_ClubWide", Duration = 4f, Warmup = 1.5f };
            s.Enter = EnterClub;
            s.Update = (lt, u) =>
            {
                float m = Smooth(u);
                Cam(Lerp(new Vector3(-4012f, 201.6f, 4012f), new Vector3(-4008.5f, 201.1f, 4008.5f), m), Lerp(new Vector3(-3992f, 200.5f, 3995f), new Vector3(-3990f, 201f, 3996f), m), 55f);
            };
            shots.Add(s);
        }

        // 9. Low dolly along the dance floor.
        private void ShotClubFloor()
        {
            var s = new Shot { Name = "S09_ClubFloor", Duration = 3.5f, Warmup = 0.5f };
            s.Update = (lt, u) =>
            {
                float m = Smooth(u);
                Cam(Lerp(new Vector3(-4004f, 199.35f, 3986.4f), new Vector3(-4001.6f, 199.5f, 3987.8f), m), Lerp(new Vector3(-3999.5f, 199.4f, 3991.2f), new Vector3(-3997f, 199.8f, 3993f), m), 46f);
            };
            s.Exit = ExitClub;
            shots.Add(s);
        }

        // ================================================================== 10-11 car meet

        private readonly List<TrailerCar> cars = new List<TrailerCar>();
        private GameObject smokePrefab, explosionPrefab;

        private static GameObject LoadPrefab(string path)
        {
#if UNITY_EDITOR
            var p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (p == null) Debug.LogError("[Trailer] Missing prefab " + path);
            return p;
#else
            return null;
#endif
        }

        private static int PaintIndex(GameObject prefab, string colour)
        {
            var pv = prefab != null ? prefab.GetComponent<OpenworldCrime.Vehicles.VehiclePaintVariants>() : null;
            if (pv == null) return -1;
            var f = pv.GetType().GetField("colourNames", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var names = f != null ? f.GetValue(pv) as string[] : null;
            if (names == null) return -1;
            for (int i = 0; i < names.Length; i++) if (string.Equals(names[i], colour, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private TrailerCar SpawnCar(string prefabName, string colour, Vector3 pos, float yaw, bool smoke = false, bool police = false)
        {
            var prefab = LoadPrefab(prefabName.StartsWith("Assets/") ? prefabName : DrivablePath + prefabName + ".prefab");
            if (prefab == null) return null;
            if (smoke && smokePrefab == null) smokePrefab = LoadPrefab("Assets/RealisticCarControllerV3/Prefabs/Particles/RCCWheelSlipAsphalt.prefab");
            var car = TrailerCar.Spawn(prefab, pos, yaw, PaintIndex(prefab, colour), Stage, smoke ? smokePrefab : null, police);
            car.Pose(pos, yaw, 0f, 0f, 0f, FrameDt, true);
            cars.Add(car);
            return car;
        }

        private static readonly Vector3 MeetCentre = new Vector3(738f, Road, 195f);

        private void StageMeet()
        {
            cars.Clear();
            // A row angled in front of the club's neon facade, noses to the street, and the black Challenger out front.
            string[,] row =
            {
                { "Drivable_Car3", "ElectricBlue" }, { "Drivable_Khodge2", "RaceRed" }, { "Drivable_Car1", "PearlWhite" },
                { "Drivable_WMB", "Yellow" }, { "Drivable_Talenter", "MetallicPurple" }, { "Drivable_Car4", "Silver" }, { "Drivable_GWagon", "Black" }
            };
            for (int i = 0; i < row.GetLength(0); i++)
            {
                float z = 178.5f + i * 5.4f;
                SpawnCar(row[i, 0], row[i, 1], new Vector3(743f, Road, z), 228f);
            }
            SpawnCar("Drivable_Car2", "Black", new Vector3(731.5f, Road, 192.5f), 212f);
            SpawnCar("Drivable_Car5", "Teal", new Vector3(730.5f, Road, 203.5f), 292f);
        }

        // 10. Slow orbit around the meet.
        private void ShotMeetOrbit()
        {
            var s = new Shot { Name = "S10_MeetOrbit", Duration = 5f, Anchor = new Vector3(735f, Road, 520f), Warmup = 2f };
            s.Enter = () => { Night(); StageMeet(); };
            s.Update = (lt, u) =>
            {
                float a = Mathf.Lerp(232f, 290f, Smooth(u)) * Mathf.Deg2Rad;
                float r = Mathf.Lerp(14f, 11.5f, u);
                Vector3 p = MeetCentre + new Vector3(Mathf.Sin(a) * r, Mathf.Lerp(2.8f, 1.7f, u), Mathf.Cos(a) * r);
                Cam(p, MeetCentre + new Vector3(1.5f, 0.9f, 0f), 50f);
            };
            shots.Add(s);
        }

        // 11. Low slider past the headlights.
        private void ShotMeetLow()
        {
            var s = new Shot { Name = "S11_MeetLow", Duration = 4f, Anchor = new Vector3(735f, Road, 520f), Warmup = 1f };
            s.Enter = () => { Night(); StageMeet(); };
            s.Update = (lt, u) =>
            {
                float z = Mathf.Lerp(176f, 208f, Smooth(u));
                Cam(new Vector3(735.2f, Road + 0.7f, z), new Vector3(744f, Road + 0.85f, z + 4.5f), 44f);
            };
            shots.Add(s);
        }

        // ================================================================== 12-14 drift

        private TrailerPath driftPath;
        private float[] driftS, driftAngle;
        private const float DriftClockStep = 1f / 120f;

        private void StageDrift()
        {
            cars.Clear();
            if (driftPath == null)
            {
                var pts = TrailerPath.FromLanes(408, 410);
                driftPath = new TrailerPath(pts, 0f, 3);
                // Pre-simulate speed and drift angle (pure lookup afterwards, so every shot sees the same run).
                // The beach-road bend (lane 408) starts about 175 m in; start the run 25 m before it.
                const float sCorner = 175f;
                int n = Mathf.CeilToInt(14f / DriftClockStep) + 2;
                driftS = new float[n]; driftAngle = new float[n];
                float sPos = sCorner - 25f, ang = 0f;
                for (int i = 0; i < n; i++)
                {
                    driftS[i] = sPos; driftAngle[i] = ang;
                    float turn = (driftPath.Turn(sPos + 6f) + driftPath.Turn(sPos + 12f) + driftPath.Turn(sPos + 18f)) / 3f;
                    float target = Mathf.Clamp(turn * 70f, -36f, 36f);
                    ang = Mathf.Lerp(ang, target, 1f - Mathf.Exp(-DriftClockStep * (Mathf.Abs(target) > Mathf.Abs(ang) ? 4.5f : 2.2f)));
                    float speed = Mathf.Lerp(26f, 19f, Mathf.Clamp01(Mathf.Abs(ang) / 30f));
                    sPos += speed * DriftClockStep;
                }
                var sb = new System.Text.StringBuilder();
                for (int c = 0; c <= 13; c++) { float ss, aa; DriftSample(c, out ss, out aa); sb.Append($" t{c}:s{ss:F0}/a{aa:F0}/k{driftPath.Turn(ss):F2}"); }
                Debug.Log($"[Trailer] Drift path {driftPath.Length:F0} m, corner at {sCorner:F0} m.{sb}");
            }
            SpawnCar("Drivable_Car2", "Black", driftPath.Pos(driftS[0]), driftPath.Yaw(driftS[0]), true);
        }

        private void DriftSample(float clock, out float s, out float angle)
        {
            float f = Mathf.Clamp(clock / DriftClockStep, 0f, driftS.Length - 1.001f);
            int i = (int)f; float k = f - i;
            s = Mathf.Lerp(driftS[i], driftS[i + 1], k);
            angle = Mathf.Lerp(driftAngle[i], driftAngle[i + 1], k);
        }

        private Vector3 driftCarPos; private float driftYaw;

        private void PoseDrift(float clock)
        {
            if (cars.Count == 0) return;
            DriftSample(clock, out float s, out float ang);
            Vector3 p = driftPath.Pos(s);
            float yaw = driftPath.Yaw(s);
            float steer = Mathf.Clamp(-ang * 0.55f + Mathf.Atan(2.8f * driftPath.Turn(s) * Mathf.Deg2Rad) * Mathf.Rad2Deg, -32f, 32f);
            float lean = Mathf.Clamp(-driftPath.Turn(s) * 2.2f, -3.5f, 3.5f);
            cars[0].Pose(p, yaw + ang, steer, lean, Mathf.Clamp01((Mathf.Abs(ang) - 6f) / 18f), FrameDt);
            driftCarPos = cars[0].T.position;
            driftYaw = yaw;
        }

        private Vector3 PathFrame(Vector3 origin, float yaw, Vector3 local) => origin + Quaternion.Euler(0f, yaw, 0f) * local;

        private Vector3 camSmooth; private bool camSmoothValid;
        private Vector3 SmoothCam(Vector3 target, float rate)
        {
            if (!camSmoothValid) { camSmooth = target; camSmoothValid = true; }
            else camSmooth = Vector3.Lerp(camSmooth, target, 1f - Mathf.Exp(-rate * FrameDt));
            return camSmooth;
        }

        // 12. Low chase cam behind the black Challenger as it throws it sideways into the beach-road bend.
        private void ShotDriftChase()
        {
            var s = new Shot { Name = "S12_DriftChase", Duration = 4.5f, Anchor = new Vector3(-150f, Road, -600f), Warmup = 1f };
            s.Enter = () => { Night(); StageDrift(); camSmoothValid = false; };
            s.Update = (lt, u) =>
            {
                PoseDrift(1.0f + lt);
                Vector3 want = PathFrame(driftCarPos, driftYaw, new Vector3(0.8f, 1.1f, -6.3f));
                Cam(SmoothCam(want, 14f), PathFrame(driftCarPos, driftYaw, new Vector3(0f, 0.9f, 5f)), 55f);
            };
            shots.Add(s);
        }

        // 13. Roadside, low on the outside of the bend: the car slides past in tyre smoke.
        private void ShotDriftSide()
        {
            var s = new Shot { Name = "S13_DriftSide", Duration = 4f, Anchor = new Vector3(-150f, Road, -600f), Warmup = 1f };
            Vector3 camPos = Vector3.zero;
            s.Enter = () =>
            {
                Night(); StageDrift();
                DriftSample(7.4f, out float sMid, out float _);
                Vector3 p = driftPath.Pos(sMid);
                Vector3 outside = -Vector3.Cross(Vector3.up, driftPath.Dir(sMid)) * Mathf.Sign(driftPath.Turn(sMid) == 0f ? 1f : driftPath.Turn(sMid));
                camPos = TrailerCar.Ground(p + outside * 7.5f + driftPath.Dir(sMid) * 9f, p.y) + Vector3.up * 0.9f;
                camSmoothValid = false;
            };
            s.Update = (lt, u) =>
            {
                PoseDrift(5.5f + lt);
                Cam(camPos, SmoothCam(driftCarPos + Vector3.up * 0.8f, 6f), 40f);
            };
            shots.Add(s);
        }

        // 14. Ahead of the car, looking back into the headlights as it straightens out of the slide.
        private void ShotDriftFront()
        {
            var s = new Shot { Name = "S14_DriftFront", Duration = 3f, Anchor = new Vector3(-150f, Road, -600f), Warmup = 1f };
            s.Enter = () => { Night(); StageDrift(); camSmoothValid = false; };
            s.Update = (lt, u) =>
            {
                PoseDrift(9.5f + lt);
                Vector3 want = PathFrame(driftCarPos, driftYaw, new Vector3(-2.2f, 0.9f, 10f));
                Cam(SmoothCam(want, 7f), driftCarPos + Vector3.up * 0.7f, 50f);
            };
            shots.Add(s);
        }

        // ================================================================== 15-17 cop chase

        private TrailerPath chasePath;
        private float chaseS0, chaseSMid;
        private const float HeroSpeed = 29f, CivilianSpeed = 11f, MissTime = 9.6f;
        private TrailerCar hero, cop1, cop2, civilian;
        private bool crashed;
        private float crashClock;

        private void StageChase(bool withCivilian)
        {
            cars.Clear();
            crashed = false;
            if (chasePath == null)
            {
                chasePath = new TrailerPath(TrailerPath.FromLanes(395), 0f, 3);
                chaseSMid = chasePath.Project(new Vector3(381f, 46f, -264f));
                chaseS0 = chaseSMid - 15f - HeroSpeed * MissTime;
                Debug.Log($"[Trailer] Chase path {chasePath.Length:F0} m, bridge at {chaseSMid:F0} m, start {chaseS0:F0} m.");
            }
            hero = SpawnCar("Drivable_Car2", "Black", chasePath.Pos(chaseS0), chasePath.Yaw(chaseS0), true);
            cop1 = SpawnCar("Assets/_Project/Prefabs/Police/PoliceCruiser.prefab", "", chasePath.Pos(chaseS0 - 20f), chasePath.Yaw(chaseS0), false, true);
            cop2 = SpawnCar("Assets/_Project/Prefabs/Police/PoliceCruiser.prefab", "", chasePath.Pos(chaseS0 - 34f), chasePath.Yaw(chaseS0), false, true);
            civilian = withCivilian ? SpawnCar("Drivable_Car5", "White", chasePath.Pos(chaseS0), chasePath.Yaw(chaseS0)) : null;
        }

        // Hero lane offset: weave, then the late swerve around the civilian car (lane 396 is 6 m to the left).
        private static float HeroLat(float c)
        {
            float weave = -6f * Smooth((c - 1.2f) / 1.0f) + 6f * Smooth((c - 4.0f) / 1.1f);
            float swerve = -4.8f * Smoother((c - (MissTime - 0.75f)) / 0.7f);
            return weave + swerve;
        }

        private void PoseOnChase(TrailerCar car, float s, float lat, float latVel, float c, float speed)
        {
            if (car == null || car.Dynamic) return;
            Vector3 dir = chasePath.Dir(s);
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            Vector3 p = chasePath.Pos(s) + right * lat;
            float yawSlip = Mathf.Atan2(latVel, speed) * Mathf.Rad2Deg;
            float turn = chasePath.Turn(s);
            float steer = Mathf.Clamp(Mathf.Atan(2.8f * turn * Mathf.Deg2Rad) * Mathf.Rad2Deg + yawSlip * 1.4f, -30f, 30f);
            car.Pose(p, chasePath.Yaw(s) + yawSlip * 1.15f, steer, Mathf.Clamp(-turn * 1.5f - latVel * 0.25f, -3f, 3f), Mathf.Clamp01(Mathf.Abs(yawSlip) / 10f) * 0.6f, FrameDt);
        }

        private void PoseChase(float c)
        {
            float sh = chaseS0 + HeroSpeed * c;
            float lh = HeroLat(c), lv = (HeroLat(c + 0.05f) - HeroLat(c - 0.05f)) / 0.1f;
            PoseOnChase(hero, sh, lh, lv, c, HeroSpeed);
            // Cop 1 follows the hero's line late (1.2 s behind in steering) -> clips the civilian.
            float c1 = c - 0.55f;
            PoseOnChase(cop1, chaseS0 + HeroSpeed * c1, HeroLat(c - 1.2f), (HeroLat(c - 1.15f) - HeroLat(c - 1.25f)) / 0.1f, c, HeroSpeed);
            float c2 = c - 1.0f;
            PoseOnChase(cop2, chaseS0 + HeroSpeed * c2, -6f + 6f * Smooth((c - 3.0f) / 1.2f) - 6f * Smooth((c - 7.0f) / 1.0f), 0f, c, HeroSpeed);
            if (civilian != null)
            {
                float sc = chaseS0 + HeroSpeed * MissTime - CivilianSpeed * (MissTime - c) + 1.0f;
                PoseOnChase(civilian, sc, 0.4f, 0f, c, CivilianSpeed);
                // Contact: cop 1 closes on the civilian -> both go to physics.
                if (!crashed && cop1 != null && Vector3.Distance(cop1.T.position, civilian.T.position) < 5.6f)
                {
                    crashed = true;
                    crashClock = c;
                    Vector3 dir = chasePath.Dir(chaseS0 + HeroSpeed * c1);
                    Vector3 right = Vector3.Cross(Vector3.up, dir);
                    cop1.Release(dir * HeroSpeed + right * 2.5f + Vector3.up * 1.2f, cop1.T.forward * 2.6f + Vector3.up * 1.8f);
                    civilian.Release(dir * CivilianSpeed, Vector3.up * -0.6f);
                }
            }
        }


        // 15. Drone behind the police pack chasing the black car up the bridge approach.
        private void ShotChaseAerial()
        {
            var s = new Shot { Name = "S15_ChaseDrone", Duration = 4f, Anchor = new Vector3(-150f, Road, -650f), Warmup = 1f };
            s.Enter = () => { Night(); StageChase(false); camSmoothValid = false; };
            s.Update = (lt, u) =>
            {
                float c = 0.3f + lt;
                PoseChase(c);
                float s2 = chaseS0 + HeroSpeed * (c - 1.2f);
                Vector3 back = chasePath.Pos(s2 - 26f) + Vector3.up * 17f;
                Cam(SmoothCam(back, 5f), hero.T.position + Vector3.up * 1f, 52f);
            };
            shots.Add(s);
        }

        // 16. Low, ahead of the black car, police lights filling the background.
        private void ShotChaseFront()
        {
            var s = new Shot { Name = "S16_ChaseFront", Duration = 4f, Anchor = new Vector3(-150f, Road, -650f), Warmup = 1f };
            s.Enter = () => { Night(); StageChase(false); camSmoothValid = false; };
            s.Update = (lt, u) =>
            {
                float c = 4.3f + lt;
                PoseChase(c);
                float sh = chaseS0 + HeroSpeed * c;
                Vector3 dir = chasePath.Dir(sh);
                Vector3 right = Vector3.Cross(Vector3.up, dir);
                Vector3 want = hero.T.position + dir * Mathf.Lerp(8.5f, 7f, u) + right * 2.4f + Vector3.up * 0.95f;
                Cam(SmoothCam(want, 10f), hero.T.position - dir * 6f + Vector3.up * 0.9f, 50f);
            };
            shots.Add(s);
        }

        // 17. Roadside on the bridge: the black car threads past a civilian, the lead cruiser clips it and rolls.
        private void ShotChaseCrash()
        {
            var s = new Shot { Name = "S17_Crash", Duration = 5.5f, Anchor = new Vector3(-150f, Road, -650f), Warmup = 1f };
            Vector3 camPos = Vector3.zero, impact = Vector3.zero;
            float sim = 0f;
            s.Enter = () =>
            {
                Night(); StageChase(true); camSmoothValid = false; sim = MissTime - 2.2f;
                if (explosionPrefab == null) explosionPrefab = LoadPrefab("Assets/_Project/Prefabs/Vehicles/Effects/VFX_CarExplosion.prefab");
                float sImp = chaseS0 + HeroSpeed * MissTime + 8f;
                Vector3 dir = chasePath.Dir(sImp);
                Vector3 right = Vector3.Cross(Vector3.up, dir);
                impact = chasePath.Pos(sImp);
                camPos = TrailerCar.Ground(impact + dir * 24f + right * 4.6f, impact.y) + Vector3.up * 1.1f;
                exploded = false;
            };
            s.Update = (lt, u) =>
            {
                float scale = crashed ? Mathf.Lerp(0.35f, 1f, Smooth((sim - crashClock - 1.6f) / 1.2f)) : 1f;
                Time.timeScale = scale;
                PoseChase(sim);
                if (crashed && !exploded && sim - crashClock > 0.4f && explosionPrefab != null && cop1 != null)
                {
                    exploded = true;
                    Instantiate(explosionPrefab, cop1.T.position + Vector3.up * 0.8f, Quaternion.identity, Stage);
                }
                Vector3 look = crashed && cop1 != null ? cop1.T.position : hero.T.position;
                Cam(camPos, SmoothCam(look + Vector3.up * 0.9f, 4f), 38f);
                sim += FrameDt * scale;
            };
            s.Exit = () => { Time.timeScale = 1f; };
            shots.Add(s);
        }

        private bool exploded;

        // ================================================================== 18 finale

        // 18. High over the city between the two searchlight towers, slow push; fades to black (ffmpeg).
        private void ShotFinale()
        {
            var s = new Shot { Name = "S18_Finale", Duration = 6.5f, Anchor = new Vector3(204f, Road, 480f), Warmup = 2f };
            s.Enter = () => { Night(); foreach (var c in cars) c.Destroy(); cars.Clear(); };
            s.Update = (lt, u) =>
            {
                float m = Smooth(u);
                Vector3 p = Lerp(new Vector3(196f, 76f, 842f), new Vector3(165f, 104f, 772f), m);
                Cam(p, Lerp(new Vector3(204f, 60f, 300f), new Vector3(182f, 70f, 210f), m), 60f);
            };
            shots.Add(s);
        }
    }
}
