using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Occlusion culling bake with the settings chosen for the city (large building occluders, small props only as occludees,
    /// see <see cref="StaticFlagsAuditTool"/>). Refuses to bake while the active scene has unsaved changes: a bake is long and
    /// memory hungry, and a crash during it would lose the unsaved work. The bake runs in the background (Window > Rendering >
    /// Occlusion Culling shows progress); save the scene afterwards to keep the new data.
    /// </summary>
    public static class OcclusionBakeTool
    {
        public const float SmallestOccluder = 8f;
        public const float SmallestHole = 0.5f;
        public const float BackfaceThreshold = 100f;

        [MenuItem("Tools/OpenworldCrime/Performance/Bake Occlusion Culling (saved scene only)")]
        private static void BakeMenu() => Debug.Log(Bake());

        /// <summary>Applies the bake parameters and starts a background bake. Returns what happened.</summary>
        public static string Bake()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) return "[OcclusionBake] The scene has unsaved changes. Save it first; nothing was baked.";
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "[OcclusionBake] Leave play mode first.";

            StaticOcclusionCulling.smallestOccluder = SmallestOccluder;
            StaticOcclusionCulling.smallestHole = SmallestHole;
            StaticOcclusionCulling.backfaceThreshold = BackfaceThreshold;
            bool started = StaticOcclusionCulling.GenerateInBackground();
            return started
                ? $"[OcclusionBake] Bake started (smallest occluder {SmallestOccluder} m, smallest hole {SmallestHole} m, backface {BackfaceThreshold}). Save the scene when it finishes."
                : "[OcclusionBake] The bake could not start.";
        }
    }
}
