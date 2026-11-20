using System;
using UnityEngine;

namespace OpenworldCrime.Trailer
{
    // Trailer v2: 45 s high-end cut. Low / mid cameras only (no top-down views that show the map's edges), the ocean runs
    // to the horizon (OceanHorizon), drivers and officers sit in every moving car, per-shot depth of field / motion blur.
    public partial class TrailerDirector
    {
        private void BuildShotsV2()
        {
            V2Horizon();        //  0.0 -  2.5  open ocean at sunset, horizon
            V2Bridge();         //  2.5 -  8.5  pull back over the water to the bridge, sunset -> night
            V2Searchlights();   //  8.5 - 10.5  looking up a tower at the beams
            V2Boulevard();      // 10.5 - 12.5  neon boulevard, cars rushing past
            V2Walker();         // 12.5 - 14.5  pedestrian tracking
            V2ClubDoor();       // 14.5 - 16.5  club neon entrance
            V2ClubDancer();     // 16.5 - 19.0  club interior, dancer, shallow focus
            V2Meet();           // 19.0 - 21.5  car meet, low orbit
            V2Headlight();      // 21.5 - 23.0  macro: Challenger headlight and wheel
            V2DriftChase();     // 23.0 - 26.0  drift, chase cam
            V2DriftRamp();      // 26.0 - 28.5  drift, roadside, speed ramp into slow motion
            V2DriftFront();     // 28.5 - 30.0  drift, front, driver visible
            V2ChaseFront();     // 30.0 - 32.5  cop chase, low front
            V2CopWindow();      // 32.5 - 34.5  alongside a cruiser, officers in the window
            V2HeroWindow();     // 34.5 - 36.0  alongside the Challenger, driver in the window
            V2Crash();          // 36.0 - 41.0  near miss, cruiser crash, explosion, slow motion
            V2Finale();         // 41.0 - 45.0  the Challenger drives off toward the skyline, fade to black
        }

        private Shot V2(string name, float duration, Vector3 anchor, float warmup, Action enter)
        {
            var s = new Shot { Name = name, Duration = duration, Anchor = anchor, Warmup = warmup };
            s.Enter = () =>
            {
                if (Quality != null) { Quality.SetDof(0f); Quality.SetMotionBlur(0f); }
                Time.timeScale = 1f;
                camSmoothValid = false;
                enter?.Invoke();
            };
            shots.Add(s);
            return s;
        }

        /// <summary>Focus on a world point (call after Cam).</summary>
        private void Focus(Vector3 target, float aperture = 2.8f, float focal = 70f)
        {
            if (Quality != null) Quality.SetDof(Vector3.Distance(TrailerCamera.transform.position, target), aperture, focal);
        }

        private void Blur(float amount) { if (Quality != null) Quality.SetMotionBlur(amount); }

        // ------------------------------------------------------------------ ocean + bridge

        // 1. Sunset: skimming the open sea toward the horizon, the water running out into the sky.
        private void V2Horizon()
        {
            var s = V2("V01_Horizon", 2.5f, new Vector3(204f, Road, 480f), 3f, () => SetTimeOfDay(18.35f, 0f));
            s.Update = (lt, u) =>
            {
                SetTimeOfDay(18.35f, 0f);
                // Pull back and rise off the water: the sea runs unbroken to the horizon.
                float m = Smooth(u);
                // Out past the city's own water plane (ends at z ~853): only the open-sea surface is in view.
                Vector3 p = Lerp(new Vector3(140f, 27.2f, 1060f), new Vector3(156f, 33f, 1000f), m);
                Cam(p, p + new Vector3(-0.35f, Mathf.Lerp(-0.03f, -0.06f, m), 1f), Mathf.Lerp(40f, 48f, m), Mathf.Lerp(-1.2f, 0.4f, u));
            };
        }

        // 2. The camera pulls back low over the bay and the cable-stayed bridge comes into frame; the sky runs to night.
        private void V2Bridge()
        {
            var s = V2("V02_Bridge", 6f, new Vector3(380f, Road, -440f), 1.5f, () => SetTimeOfDay(18.35f, 0f));
            s.Update = (lt, u) =>
            {
                float k = Smoother((lt - 2.3f) / 2.6f);
                SetTimeOfDay(Mathf.Lerp(18.35f, 21.4f, k), k);
                float m = Smooth(u);
                Vector3 p = Lerp(new Vector3(336f, 27.6f, -508f), new Vector3(347f, 36f, -640f), m);
                Vector3 l = Lerp(new Vector3(345f, 62f, -275f), new Vector3(338f, 58f, -270f), m);
                Cam(p, l, Mathf.Lerp(36f, 47f, m));
            };
        }

        // 3. From the street, tilting up a tower to its searchlights sweeping the sky.
        private void V2Searchlights()
        {
            var s = V2("V03_Searchlights", 2f, new Vector3(560f, Road, -200f), 2.5f, Night);
            s.Update = (lt, u) =>
            {
                float m = Smooth(u);
                Vector3 p = Lerp(new Vector3(712f, 92f, -126f), new Vector3(704f, 98f, -142f), m);
                Cam(p, new Vector3(594f, Mathf.Lerp(150f, 168f, m), -251f), 55f, Mathf.Lerp(2f, 0f, m));
            };
        }

        // ------------------------------------------------------------------ city

        // 4. Low on the boulevard median, cars blasting past both ways (drivers at the wheel).
        private void V2Boulevard()
        {
            var s = V2("V04_Boulevard", 2f, FarAnchor, 1.5f, () =>
            {
                Night(); cars.Clear(); movers.Clear();
                AddMover("Drivable_Khodge2", "RaceRed", LanePath(424), 112f, 24f);
                AddMover("Drivable_Car4", "Silver", LanePath(423), 118f, 17f);
                AddMover("Drivable_GWagon", "Black", LanePath(421), 108f, 15f);
                AddMover("Drivable_Car1", "PearlWhite", LanePath(422), 120f, 13f);
                Blur(0.45f);
            });
            s.Update = (lt, u) =>
            {
                PoseMovers(lt);
                Vector3 p = Lerp(new Vector3(633.6f, 28.4f, -118f), new Vector3(634.0f, 28.7f, -106f), Smooth(u));
                Cam(p, p + new Vector3(2.5f, 1.4f, 60f), 46f, Mathf.Lerp(-2f, 0.5f, u));
            };
        }

        // 5. Tracking alongside someone walking under the neon.
        private void V2Walker()
        {
            var s = V2("V05_Walker", 2f, new Vector3(640f, Road, -60f), 6f, BeginPedShot);
            s.Ready = () => ReadyPed(s.Anchor, 90f, 0f);
            s.Update = (lt, u) =>
            {
                if (pedTarget == null)
                {
                    if (lt == 0f) TrackPeds(s.Anchor, 100f);
                    Cam(new Vector3(630f, 29f, -90f), new Vector3(640f, 30f, -40f), 50f);
                    return;
                }
                UpdatePedTarget();
                Vector3 right = Vector3.Cross(Vector3.up, pedDir);
                Vector3 p = pedSmoothPos;
                Cam(p + right * (2.6f * pedSide) + pedDir * Mathf.Lerp(1.2f, 0.4f, u) + Vector3.up * 1.5f, p + Vector3.up * 1.35f + pedDir * 0.3f, 40f);
                Focus(p + Vector3.up * 1.4f, 2.0f, 60f);
            };
        }

        // 6. Push in on the club's neon.
        private void V2ClubDoor()
        {
            var s = V2("V06_ClubDoor", 2f, FarAnchor, 2f, Night);
            s.Update = (lt, u) =>
            {
                float m = Smooth(u);
                Cam(Lerp(new Vector3(726f, 28.6f, 179f), new Vector3(736f, 29.2f, 185f), m), new Vector3(755f, Mathf.Lerp(32.4f, 31.8f, m), 189f), Mathf.Lerp(44f, 40f, m), Mathf.Lerp(-2f, 0f, m));
            };
        }

        // 7. Inside: a dancer under the disco ball, background melting into bokeh.
        private void V2ClubDancer()
        {
            var s = V2("V07_ClubDancer", 2.5f, Vector3.zero, 1.5f, EnterClub);
            s.Update = (lt, u) =>
            {
                float m = Smooth(u);
                Cam(Lerp(new Vector3(-4004.2f, 199.3f, 3986.6f), new Vector3(-4002.2f, 199.5f, 3988.0f), m), Lerp(new Vector3(-3999.8f, 199.4f, 3991.0f), new Vector3(-3998f, 199.7f, 3992.2f), m), 42f);
                Focus(new Vector3(-4001f, 199.5f, 3991.2f), 1.8f, 85f);
            };
            s.Exit = ExitClub;
        }

        // ------------------------------------------------------------------ car meet

        // 8. Low orbit through the meet, cars and neon facade.
        private void V2Meet()
        {
            var s = V2("V08_Meet", 2.5f, new Vector3(735f, Road, 520f), 1.5f, () => { Night(); StageMeet(); });
            s.Update = (lt, u) =>
            {
                float a = Mathf.Lerp(236f, 272f, Smooth(u)) * Mathf.Deg2Rad;
                float r = Mathf.Lerp(11f, 9.5f, u);
                Vector3 c = MeetCentre + new Vector3(-4f, 0f, -2f);
                Vector3 p = c + new Vector3(Mathf.Sin(a) * r, Mathf.Lerp(1.2f, 0.9f, u), Mathf.Cos(a) * r);
                Cam(p, c + new Vector3(0f, 0.7f, 0f), 42f);
                if (cars.Count > 7) Focus(cars[7].T.position + Vector3.up * 0.8f, 2.4f, 70f);
            };
        }

        // 9. Macro slide across the black Challenger's headlight and wheel.
        private void V2Headlight()
        {
            var s = V2("V09_Headlight", 1.5f, new Vector3(735f, Road, 520f), 1f, () => { Night(); StageMeet(); });
            s.Update = (lt, u) =>
            {
                if (cars.Count < 8) return;
                Transform hero = cars[7].T;
                float m = Smooth(u);
                Vector3 p = hero.TransformPoint(Lerp(new Vector3(-1.7f, 0.62f, 2.9f), new Vector3(-1.95f, 0.55f, 1.6f), m));
                Vector3 l = hero.TransformPoint(Lerp(new Vector3(-0.62f, 0.68f, 2.2f), new Vector3(-0.85f, 0.38f, 1.45f), m));
                Cam(p, l, 34f);
                Focus(l, 1.6f, 85f);
            };
        }

        // ------------------------------------------------------------------ drift

        // 10. Chase cam, low behind the Challenger as it kicks sideways into the beach-road bend.
        private void V2DriftChase()
        {
            var s = V2("V10_DriftChase", 3f, FarAnchor, 1f, () => { Night(); StageDrift(); Blur(0.5f); });
            s.Update = (lt, u) =>
            {
                PoseDrift(1.8f + lt);
                Vector3 want = PathFrame(driftCarPos, driftYaw, new Vector3(1.0f, 0.95f, -5.6f));
                Cam(SmoothCam(want, 14f), PathFrame(driftCarPos, driftYaw, new Vector3(0f, 0.8f, 5f)), 56f, -2f);
            };
        }

        // 11. Roadside on the outside of the bend; the slide drops into slow motion as it passes, then snaps back.
        private void V2DriftRamp()
        {
            Vector3 camPos = Vector3.zero;
            float sim = 0f;
            var s = V2("V11_DriftRamp", 2.5f, FarAnchor, 1f, () =>
            {
                Night(); StageDrift(); Blur(0.35f); sim = 5.0f;
                DriftSample(6.0f, out float sMid, out float _);
                Vector3 p = driftPath.Pos(sMid);
                float turn = driftPath.Turn(sMid);
                Vector3 outside = -Vector3.Cross(Vector3.up, driftPath.Dir(sMid)) * Mathf.Sign(turn == 0f ? 1f : turn);
                camPos = TrailerCar.Ground(p + outside * 6.5f + driftPath.Dir(sMid) * 7f, p.y) + Vector3.up * 0.55f;
            });
            s.Update = (lt, u) =>
            {
                float speed = Mathf.Lerp(1f, 0.3f, Smooth((lt - 0.35f) / 0.4f)) * Mathf.Lerp(1f, 3.3f, Smooth((lt - 1.9f) / 0.35f));
                speed = Mathf.Clamp(speed, 0.3f, 1f);
                Time.timeScale = speed;
                PoseDrift(sim);
                Cam(camPos, SmoothCam(driftCarPos + Vector3.up * 0.75f, 7f), 36f);
                Focus(driftCarPos + Vector3.up * 0.8f, 2.8f, 70f);
                sim += FrameDt * speed;
            };
            s.Exit = () => { Time.timeScale = 1f; };
        }

        // 12. Ahead of the car looking back into the headlights; the driver at the wheel, counter-steering.
        private void V2DriftFront()
        {
            var s = V2("V12_DriftFront", 1.5f, FarAnchor, 1f, () => { Night(); StageDrift(); Blur(0.3f); });
            s.Update = (lt, u) =>
            {
                PoseDrift(7.6f + lt);
                Vector3 want = PathFrame(driftCarPos, driftYaw, new Vector3(-1.4f, 1.05f, 6.2f));
                Cam(SmoothCam(want, 12f), driftCarPos + Vector3.up * 0.95f, 44f);
                Focus(cars.Count > 0 ? cars[0].T.TransformPoint(new Vector3(-0.39f, 1.0f, 0f)) : driftCarPos, 2.2f, 70f);
            };
        }

        // ------------------------------------------------------------------ chase

        // 13. Low in front of the black car, both cruisers' light bars filling the background.
        private void V2ChaseFront()
        {
            var s = V2("V13_ChaseFront", 2.5f, FarAnchor, 1f, () => { Night(); StageChase(false); Blur(0.5f); });
            s.Update = (lt, u) =>
            {
                float c = 4.6f + lt;
                PoseChase(c);
                float sh = chaseS0 + HeroSpeed * c;
                Vector3 dir = chasePath.Dir(sh);
                Vector3 right = Vector3.Cross(Vector3.up, dir);
                Vector3 want = hero.T.position + dir * Mathf.Lerp(7.5f, 6.3f, u) + right * 2.0f + Vector3.up * 0.8f;
                Cam(SmoothCam(want, 10f), hero.T.position - dir * 7f + Vector3.up * 0.9f, 48f, 1.5f);
            };
        }

        // 14. Alongside the lead cruiser at window height: officers inside, the bar strobing.
        private void V2CopWindow()
        {
            var s = V2("V14_CopWindow", 2f, FarAnchor, 1f, () => { Night(); StageChase(false); Blur(0.55f); });
            s.Update = (lt, u) =>
            {
                float c = 2.2f + lt;
                PoseChase(c);
                Transform cop = cop1.T;
                Vector3 want = cop.TransformPoint(new Vector3(-2.4f, 1.3f, Mathf.Lerp(0.9f, 0.1f, u)));
                Vector3 head = cop.TransformPoint(new Vector3(-0.55f, 1.15f, 0.25f));
                Cam(want, head + cop.forward * 0.15f, 40f, -1.5f);
                Focus(head, 2.4f, 60f);
            };
        }

        // 15. Alongside the Challenger at wheel height, city lights streaking past.
        private void V2HeroWindow()
        {
            var s = V2("V15_HeroWindow", 1.5f, FarAnchor, 1f, () => { Night(); StageChase(false); Blur(0.6f); });
            s.Update = (lt, u) =>
            {
                float c = 0.8f + lt;
                PoseChase(c);
                Transform h = hero.T;
                // Side profile at wheel height, the city streaking past behind.
                Vector3 want = h.TransformPoint(new Vector3(-3.4f, 0.55f, Mathf.Lerp(1.4f, -0.6f, u)));
                Vector3 look = h.TransformPoint(new Vector3(0f, 0.6f, Mathf.Lerp(0.9f, 0.1f, u)));
                Cam(want, look, 38f, 1.5f);
                Focus(look, 2.6f, 60f);
            };
        }

        // 16. On the bridge: the Challenger threads past a civilian, the lead cruiser clips it and goes up; slow motion.
        private void V2Crash()
        {
            Vector3 camPos = Vector3.zero;
            float sim = 0f;
            var s = V2("V16_Crash", 5f, FarAnchor, 1f, () =>
            {
                Night(); StageChase(true); sim = MissTime - 1.8f; exploded = false;
                if (explosionPrefab == null) explosionPrefab = LoadPrefab("Assets/_Project/Prefabs/Vehicles/Effects/VFX_CarExplosion.prefab");
                float sImp = chaseS0 + HeroSpeed * MissTime + 8f;
                Vector3 dir = chasePath.Dir(sImp);
                Vector3 right = Vector3.Cross(Vector3.up, dir);
                Vector3 impact = chasePath.Pos(sImp);
                camPos = TrailerCar.Ground(impact + dir * 22f + right * 4.4f, impact.y) + Vector3.up * 0.8f;
                Blur(0.4f);
            });
            s.Update = (lt, u) =>
            {
                float scale = crashed ? Mathf.Lerp(0.28f, 1f, Smooth((sim - crashClock - 1.3f) / 1.0f)) : 1f;
                Time.timeScale = scale;
                PoseChase(sim);
                if (crashed && !exploded && sim - crashClock > 0.35f && explosionPrefab != null && cop1 != null)
                {
                    exploded = true;
                    Instantiate(explosionPrefab, cop1.T.position + Vector3.up * 0.8f, Quaternion.identity, Stage);
                }
                Vector3 look = crashed && cop1 != null ? cop1.T.position : hero.T.position;
                Cam(camPos, SmoothCam(look + Vector3.up * 0.9f, 4f), 36f);
                Focus(look, 4f, 50f);
                sim += FrameDt * scale;
            };
            s.Exit = () => { Time.timeScale = 1f; };
        }

        // 17. The Challenger pulls away up the bridge toward the skyline and searchlights; fade to black.
        private void V2Finale()
        {
            var s = V2("V17_Finale", 4f, FarAnchor, 1f, () =>
            {
                Night(); StageChase(false); Blur(0.25f);
                // The pursuit is over: only the Challenger in frame.
                if (cop1 != null) { cop1.Destroy(); cars.Remove(cop1); cop1 = null; }
                if (cop2 != null) { cop2.Destroy(); cars.Remove(cop2); cop2 = null; }
            });
            s.Update = (lt, u) =>
            {
                float c = 11.2f + lt * 0.85f;
                PoseChase(c);
                float sCam = chaseS0 + HeroSpeed * (11.2f - 0.45f) + 22f * lt;
                Vector3 dir = chasePath.Dir(sCam);
                Vector3 right = Vector3.Cross(Vector3.up, dir);
                Vector3 p = chasePath.Pos(sCam) + right * 1.5f + Vector3.up * Mathf.Lerp(0.7f, 2.2f, Smooth(u));
                Cam(p, hero.T.position + Vector3.up * 1.2f + dir * 30f, 40f);
            };
        }
    }
}
