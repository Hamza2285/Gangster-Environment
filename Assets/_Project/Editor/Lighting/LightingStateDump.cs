using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenworldCrime.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Writes the open scene's lighting state as text (render settings, sun, lighting systems, volumes, lightmap use, URP asset),
    /// to compare before / after a set up and restore. Read only.
    /// </summary>
    public static class LightingStateDump
    {
        public static string Write(string path)
        {
            string text = Dump();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            return path;
        }

        public static string Dump()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"ambient {RenderSettings.ambientMode} {RenderSettings.ambientSkyColor} {RenderSettings.ambientEquatorColor} {RenderSettings.ambientGroundColor} {RenderSettings.ambientIntensity}");
            sb.AppendLine($"fog {RenderSettings.fog} {RenderSettings.fogMode} {RenderSettings.fogColor} {RenderSettings.fogStartDistance} {RenderSettings.fogEndDistance} {RenderSettings.fogDensity}");
            sb.AppendLine($"sky {AssetDatabase.GetAssetPath(RenderSettings.skybox)} reflection {RenderSettings.defaultReflectionMode} {RenderSettings.reflectionIntensity} {RenderSettings.reflectionBounces} {AssetDatabase.GetAssetPath(RenderSettings.customReflectionTexture)}");
            sb.AppendLine($"lighting data {AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset)} settings {AssetDatabase.GetAssetPath(Lightmapping.lightingSettings)}");
            foreach (LightmapData m in LightmapSettings.lightmaps) sb.AppendLine("lightmap " + AssetDatabase.GetAssetPath(m.lightmapColor));
            sb.AppendLine($"light probes {(LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : 0)}");
            int mapped = 0;
            double hash = 0;
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.lightmapIndex < 0 || r.lightmapIndex >= 65534) continue;
                mapped++;
                Vector4 so = r.lightmapScaleOffset;
                hash += (r.lightmapIndex + 1) * (so.x * 3 + so.y * 5 + so.z * 7 + so.w * 11);
            }
            sb.AppendLine($"lightmapped renderers {mapped} layout {hash:0.0000}");

            var controller = Object.FindFirstObjectByType<CityDayNightController>(FindObjectsInactive.Include);
            var targets = new List<Object> { controller, controller.sun, controller.sun.transform, controller.presets };
            foreach (System.Type type in new[] { typeof(CityAtmosphere), typeof(SkyCycleController), typeof(BakedLightingSwitcher), typeof(ReflectionProbeDayNight), typeof(SkyClouds), typeof(NightGlowFader) })
                targets.Add(Object.FindFirstObjectByType(type, FindObjectsInactive.Include));
            targets.AddRange(Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None).OrderBy(v => v.name));
            targets.Add(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset"));
            foreach (Object target in targets) if (target != null) Append(sb, target);
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, Object target)
        {
            sb.AppendLine($"## {target.name} ({target.GetType().Name})");
            SerializedProperty p = new SerializedObject(target).GetIterator();
            bool enter = true;
            while (p.NextVisible(enter))
            {
                enter = p.propertyType == SerializedPropertyType.Generic && !p.isArray;
                string value = null;
                switch (p.propertyType)
                {
                    case SerializedPropertyType.Float: value = p.floatValue.ToString("R"); break;
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.Enum: value = p.intValue.ToString(); break;
                    case SerializedPropertyType.Boolean: value = p.boolValue.ToString(); break;
                    case SerializedPropertyType.Color: value = p.colorValue.ToString("F5"); break;
                    case SerializedPropertyType.Vector3: value = p.vector3Value.ToString("F4"); break;
                    case SerializedPropertyType.Quaternion: value = p.quaternionValue.ToString("F5"); break;
                    case SerializedPropertyType.ObjectReference:
                        value = p.objectReferenceValue != null ? p.objectReferenceValue.name + " " + AssetDatabase.GetAssetPath(p.objectReferenceValue) : "none";
                        break;
                }
                if (value != null) sb.AppendLine($"{p.propertyPath} = {value}{(p.prefabOverride ? " (override)" : string.Empty)}");
            }
        }
    }
}
