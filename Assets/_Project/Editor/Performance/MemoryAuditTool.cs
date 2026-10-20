using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Memory audit of the assets the active scene uses (textures, meshes, audio), plus a small-batch import fixer.
    /// - Report: largest assets with their import settings and in-editor memory, duplicates by name and size, readable meshes.
    ///   Written to Captures/memory_audit.txt. Read only.
    /// - Apply Next Texture Batch: reimports at most Batch Size textures that break the budget (max size above the budget for
    ///   their kind, uncompressed, or mipmaps off on a 3D texture) for Standalone. Refuses to run while the active scene has
    ///   unsaved changes, because a reimport stall or crash would lose them. Audio: long clips are set to Streaming in the same way.
    /// Budget: 2048 max size for every 3D texture; sprites / UI and Read/Write flags are never changed (reported only).
    /// </summary>
    public static class MemoryAuditTool
    {
        private const int BatchSize = 10;
        private const int TextureBudget = 2048;
        private const float StreamingClipSeconds = 20f;

        [MenuItem("Tools/OpenworldCrime/Performance/Memory Audit (Report)")]
        private static void ReportMenu() => Debug.Log(Report());

        [MenuItem("Tools/OpenworldCrime/Performance/Memory Audit - Apply Next Texture Batch (10)")]
        private static void ApplyTexturesMenu() => Debug.Log(ApplyTextureBatch(BatchSize));

        [MenuItem("Tools/OpenworldCrime/Performance/Memory Audit - Apply Next Audio Batch (10)")]
        private static void ApplyAudioMenu() => Debug.Log(ApplyAudioBatch(BatchSize));

        /// <summary>Writes the report to Captures/memory_audit.txt and returns it.</summary>
        public static string Report()
        {
            CollectSceneAssets(out List<Texture> textures, out List<Mesh> meshes, out List<AudioClip> clips);
            var sb = new StringBuilder();
            sb.AppendLine($"Memory audit of '{SceneManager.GetActiveScene().name}'  {DateTime.Now:yyyy-MM-dd HH:mm}  (editor memory; a Standalone build uses the compressed sizes)");

            long texTotal = 0;
            textures.Sort((a, b) => Profiler.GetRuntimeMemorySizeLong(b).CompareTo(Profiler.GetRuntimeMemorySizeLong(a)));
            foreach (Texture t in textures) texTotal += Profiler.GetRuntimeMemorySizeLong(t);
            sb.AppendLine($"\nTEXTURES used: {textures.Count}, total {Mb(texTotal)} MB. Largest 30:");
            for (int i = 0; i < Math.Min(30, textures.Count); i++)
            {
                Texture t = textures[i];
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter;
                string settings = importer == null ? "(not a texture importer)" :
                    $"max {importer.maxTextureSize} {importer.textureCompression} mip {importer.mipmapEnabled} crunch {importer.crunchedCompression} readable {importer.isReadable} type {importer.textureType}";
                sb.AppendLine($"  {Mb(Profiler.GetRuntimeMemorySizeLong(t)),7:F1} MB  {t.width}x{t.height}  {settings}  {AssetDatabase.GetAssetPath(t)}");
            }
            sb.AppendLine($"  Over budget (would be fixed by Apply): {CountTextureFixes(textures)}");

            long meshTotal = 0;
            int readable = 0;
            meshes.Sort((a, b) => Profiler.GetRuntimeMemorySizeLong(b).CompareTo(Profiler.GetRuntimeMemorySizeLong(a)));
            foreach (Mesh m in meshes)
            {
                meshTotal += Profiler.GetRuntimeMemorySizeLong(m);
                if (m.isReadable) readable++;
            }
            sb.AppendLine($"\nMESHES used: {meshes.Count}, total {Mb(meshTotal)} MB, Read/Write enabled {readable} (doubles their memory). Largest 20:");
            for (int i = 0; i < Math.Min(20, meshes.Count); i++)
            {
                Mesh m = meshes[i];
                sb.AppendLine($"  {Mb(Profiler.GetRuntimeMemorySizeLong(m)),7:F1} MB  verts {m.vertexCount,8}  readable {m.isReadable}  {AssetDatabase.GetAssetPath(m)} ({m.name})");
            }

            long audioTotal = 0;
            clips.Sort((a, b) => Profiler.GetRuntimeMemorySizeLong(b).CompareTo(Profiler.GetRuntimeMemorySizeLong(a)));
            foreach (AudioClip c in clips) audioTotal += Profiler.GetRuntimeMemorySizeLong(c);
            sb.AppendLine($"\nAUDIO used: {clips.Count}, total {Mb(audioTotal)} MB. Largest 15:");
            for (int i = 0; i < Math.Min(15, clips.Count); i++)
            {
                AudioClip c = clips[i];
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(c)) as AudioImporter;
                string settings = importer == null ? "" : $"{importer.defaultSampleSettings.loadType} {importer.defaultSampleSettings.compressionFormat} q{importer.defaultSampleSettings.quality:F2}";
                sb.AppendLine($"  {Mb(Profiler.GetRuntimeMemorySizeLong(c)),7:F1} MB  {c.length,6:F1} s  {settings}  {AssetDatabase.GetAssetPath(c)}");
            }

            sb.AppendLine("\nPOSSIBLE DUPLICATES (same name and size, different files):");
            var seen = new Dictionary<string, string>();
            foreach (Texture t in textures)
            {
                string key = $"{t.name}|{t.width}x{t.height}";
                string path = AssetDatabase.GetAssetPath(t);
                if (seen.TryGetValue(key, out string other) && other != path) sb.AppendLine($"  {t.name} {t.width}x{t.height}: {other}  <->  {path}");
                else seen[key] = path;
            }

            string report = sb.ToString();
            Directory.CreateDirectory("Captures");
            File.WriteAllText(Path.Combine("Captures", "memory_audit.txt"), report);
            return report;
        }

        /// <summary>Reimports at most maxCount over-budget scene textures. Refuses while the active scene is dirty.</summary>
        public static string ApplyTextureBatch(int maxCount)
        {
            if (IsSceneDirty()) return "[MemoryAudit] The active scene has unsaved changes. Save it first; nothing was reimported.";
            CollectSceneAssets(out List<Texture> textures, out _, out _);
            int done = 0;
            var sb = new StringBuilder();
            foreach (Texture t in textures)
            {
                if (done >= maxCount) break;
                string path = AssetDatabase.GetAssetPath(t);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer) || !NeedsFix(importer, t)) continue;
                Fix(importer);
                importer.SaveAndReimport();
                done++;
                sb.AppendLine("  fixed " + path);
            }
            return $"[MemoryAudit] Reimported {done} textures (run again for the next batch).\n" + sb;
        }

        /// <summary>Sets long clips to Streaming, at most maxCount per call. Refuses while the active scene is dirty.</summary>
        public static string ApplyAudioBatch(int maxCount)
        {
            if (IsSceneDirty()) return "[MemoryAudit] The active scene has unsaved changes. Save it first; nothing was reimported.";
            CollectSceneAssets(out _, out _, out List<AudioClip> clips);
            int done = 0;
            var sb = new StringBuilder();
            foreach (AudioClip c in clips)
            {
                if (done >= maxCount) break;
                string path = AssetDatabase.GetAssetPath(c);
                if (!(AssetImporter.GetAtPath(path) is AudioImporter importer) || c.length < StreamingClipSeconds) continue;
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                if (settings.loadType == AudioClipLoadType.Streaming) continue;
                settings.loadType = AudioClipLoadType.Streaming;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
                done++;
                sb.AppendLine("  streaming " + path);
            }
            return $"[MemoryAudit] Reimported {done} audio clips (run again for the next batch).\n" + sb;
        }

        private static bool IsSceneDirty()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) return true;
            return false;
        }

        private static int CountTextureFixes(List<Texture> textures)
        {
            int count = 0;
            foreach (Texture t in textures)
                if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) is TextureImporter importer && NeedsFix(importer, t)) count++;
            return count;
        }

        private static bool NeedsFix(TextureImporter importer, Texture t)
        {
            if (importer.textureType == TextureImporterType.Sprite || importer.textureType == TextureImporterType.GUI) return false;
            if (t is RenderTexture) return false;
            return importer.maxTextureSize > TextureBudget
                   || importer.textureCompression == TextureImporterCompression.Uncompressed
                   || !importer.mipmapEnabled;
        }

        private static void Fix(TextureImporter importer)
        {
            importer.maxTextureSize = Mathf.Min(importer.maxTextureSize, TextureBudget);
            if (importer.textureCompression == TextureImporterCompression.Uncompressed) importer.textureCompression = TextureImporterCompression.Compressed;
            importer.mipmapEnabled = true;
        }

        private static void CollectSceneAssets(out List<Texture> textures, out List<Mesh> meshes, out List<AudioClip> clips)
        {
            var roots = new List<Object>();
            foreach (GameObject go in SceneManager.GetActiveScene().GetRootGameObjects()) roots.Add(go);
            textures = new List<Texture>();
            meshes = new List<Mesh>();
            clips = new List<AudioClip>();
            foreach (Object o in EditorUtility.CollectDependencies(roots.ToArray()))
            {
                if (o == null || !EditorUtility.IsPersistent(o)) continue;
                if (o is Texture t) textures.Add(t);
                else if (o is Mesh m) meshes.Add(m);
                else if (o is AudioClip c) clips.Add(c);
            }
        }

        private static double Mb(long bytes) => bytes / (1024.0 * 1024.0);
    }
}
