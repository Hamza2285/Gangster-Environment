using System.Reflection;
using UnityEngine;

namespace OpenworldCrime.Trailer
{
    /// <summary>Play-mode scouting helpers for the trailer (set the city to a time of day instantly, without saving anything).</summary>
    public static class TrailerScout
    {
        public static string SetTime(float hour, float nightBlend)
        {
            var dn = Object.FindFirstObjectByType<CityDayNightController>();
            if (dn != null) dn.SetManualBlend(nightBlend);
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb.GetType().Name != "SkyCycleController") continue;
                const BindingFlags bf = BindingFlags.NonPublic | BindingFlags.Instance;
                var t = mb.GetType();
                var mat = t.GetField("skyInstance", bf).GetValue(mb) as Material;
                mb.enabled = false;
                mat.SetVector("_CycleWeights", (Vector4)t.GetMethod("PhaseWeights", bf).Invoke(mb, new object[] { hour }));
                mat.SetVector("_SunDirection", (Vector4)t.GetMethod("SunDirection", bf).Invoke(mb, new object[] { hour }));
                mat.SetVector("_MoonDirection", (Vector4)t.GetMethod("MoonDirection", bf).Invoke(mb, null));
                RenderSettings.skybox = mat;
                return "ok";
            }
            return "no sky cycle";
        }

        public static string Teleport(Vector3 pos, float yaw)
        {
            var gp = Object.FindFirstObjectByType<OpenworldCrime.Player.InvectorGamePlayer>();
            if (gp == null) return "no player";
            gp.Teleport(TrailerCar.Ground(pos, pos.y), yaw);
            return "ok";
        }
    }
}
