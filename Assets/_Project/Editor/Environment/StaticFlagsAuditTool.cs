using System.Collections.Generic;
using System.Text;
using OpenworldCrime.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Audits and fixes static editor flags on the renderers of the active scene.
    /// - Moving things (breakable props, anything with a Rigidbody, Animator, Animation, ParticleSystem or Cloth above it, and
    ///   everything under the dynamic roots: player, vehicles, enemies, traffic, population, UI, cameras) get no static flags.
    /// - Static environment: Occludee and Reflection Probe Static on; Occluder Static only on large solid objects (vegetation
    ///   and small props are poor occluders and only slow the occlusion bake and its queries). Batching, Navigation and
    ///   Contribute GI are left as they are on static objects (baked data and batching choices stay the artist's).
    /// "Report" changes nothing; "Apply" is undoable and works in batches. Scene changes are not saved.
    /// </summary>
    public static class StaticFlagsAuditTool
    {
        private const float OccluderMinSize = 8f;
        private const int BatchSize = 500;
        private static readonly string[] DynamicRoots = { "Player", "Vehicles", "Enemies", "UI", "TaxiFastTravel", "TaxiDestinations", "TrafficSystem", "Population", "Cameras", "Camera", "SceneSetup", "///Manager///" };
        private static readonly string[] VegetationRoots = { "Plam Tree", "Street tree" };

        [MenuItem("Tools/OpenworldCrime/Environment/Static Flags Audit (Report)")]
        private static void ReportMenu() => Debug.Log(Run(false));

        [MenuItem("Tools/OpenworldCrime/Environment/Static Flags Audit (Apply)")]
        private static void ApplyMenu() => Debug.Log(Run(true));

        /// <summary>Audits the active scene; with apply = true the flags are fixed (undoable). Returns the report.</summary>
        public static string Run(bool apply)
        {
            Scene scene = SceneManager.GetActiveScene();
            var changes = new List<(GameObject go, StaticEditorFlags flags)>();
            int movingCleared = 0, occluderRemoved = 0, occluderAdded = 0, occludeeAdded = 0, reflectionAdded = 0, staticChecked = 0, movingChecked = 0;
            var perRoot = new Dictionary<string, int>();

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                bool dynamicRoot = System.Array.IndexOf(DynamicRoots, root.name) >= 0;
                bool vegetation = System.Array.IndexOf(VegetationRoots, root.name.Trim()) >= 0;
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    GameObject go = renderer.gameObject;
                    StaticEditorFlags current = GameObjectUtility.GetStaticEditorFlags(go);
                    StaticEditorFlags wanted;
                    if (dynamicRoot || IsMoving(renderer.transform))
                    {
                        movingChecked++;
                        wanted = 0;
                        if (current != 0) movingCleared++;
                    }
                    else
                    {
                        staticChecked++;
                        wanted = current | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
                        bool occluder = !vegetation && renderer is MeshRenderer && MaxDimension(renderer.bounds) >= OccluderMinSize;
                        if (occluder) wanted |= StaticEditorFlags.OccluderStatic;
                        else wanted &= ~StaticEditorFlags.OccluderStatic;
                        if ((current & StaticEditorFlags.OccluderStatic) != 0 && !occluder) occluderRemoved++;
                        if ((current & StaticEditorFlags.OccluderStatic) == 0 && occluder) occluderAdded++;
                        if ((current & StaticEditorFlags.OccludeeStatic) == 0) occludeeAdded++;
                        if ((current & StaticEditorFlags.ReflectionProbeStatic) == 0) reflectionAdded++;
                    }
                    if (wanted == current) continue;
                    changes.Add((go, wanted));
                    perRoot.TryGetValue(root.name, out int n);
                    perRoot[root.name] = n + 1;
                }
            }

            if (apply)
            {
                int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Static Flags Audit");
                for (int start = 0; start < changes.Count; start += BatchSize)
                {
                    int end = Mathf.Min(changes.Count, start + BatchSize);
                    var batch = new Object[end - start];
                    for (int i = start; i < end; i++) batch[i - start] = changes[i].go;
                    Undo.RecordObjects(batch, "Static Flags Audit");
                    for (int i = start; i < end; i++) GameObjectUtility.SetStaticEditorFlags(changes[i].go, changes[i].flags);
                }
                Undo.CollapseUndoOperations(group);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[{nameof(StaticFlagsAuditTool)}] {(apply ? "APPLIED" : "REPORT")} on '{scene.name}': renderers static {staticChecked}, moving/dynamic {movingChecked}; objects to change {changes.Count}.");
            sb.AppendLine($"  Moving objects with static flags cleared: {movingCleared}");
            sb.AppendLine($"  Occluder Static removed (small / vegetation): {occluderRemoved}, added (large): {occluderAdded}");
            sb.AppendLine($"  Occludee Static added: {occludeeAdded}; Reflection Probe Static added: {reflectionAdded}");
            foreach (KeyValuePair<string, int> pair in perRoot) sb.AppendLine($"    {pair.Key}: {pair.Value}");
            return sb.ToString();
        }

        private static bool IsMoving(Transform t)
        {
            return t.GetComponentInParent<BreakableProp>(true) != null || t.GetComponentInParent<Rigidbody>(true) != null
                   || t.GetComponentInParent<Animator>(true) != null || t.GetComponentInParent<Animation>(true) != null
                   || t.GetComponentInParent<ParticleSystem>(true) != null || t.GetComponentInParent<Cloth>(true) != null;
        }

        private static float MaxDimension(Bounds b) => Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
    }
}
