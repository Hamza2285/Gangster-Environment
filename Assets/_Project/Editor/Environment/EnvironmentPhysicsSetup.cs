using System.Collections.Generic;
using System.Text;
using OpenworldCrime.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// One-click collider / breakable setup for the city props of the active scene (scene changes only, no prefab or asset edits).
    /// Each prop "host" is a LODGroup object (colliders fitted from its LOD0 renderers only) or a MeshRenderer without a LODGroup.
    /// Hosts that already have a collider are skipped, so the tool can be run again safely. Classification is by name (first
    /// matching rule wins) plus a size guard; see <see cref="Rules"/>:
    /// - Skip: decorative or unreachable (building signs and flags, rooftop billboards, grass decals, signal lamps, leaves,
    ///   combined pine / shrub meshes).
    /// - Breakable: light street furniture gets fitted primitives + <see cref="BreakableProp"/> with per-type mass and break speed,
    ///   and loses every static flag (it moves once hit). Anything taller than 6.5 m or wider than 3.2 m stays solid.
    /// - Solid: fitted primitives (box / capsule), tree and palm trunks get a trunk capsule, boulders a convex MeshCollider,
    ///   large coastal rock meshes a static MeshCollider.
    /// All changes are undoable; the scene is not saved.
    /// In a scene built by EnvironmentColliderBuilder ("Environment Colliders" root, the city under an "env" object), solid props
    /// already collide with their exact meshes, so only the breakable ones change: their exact collider objects are removed and
    /// replaced by fitted primitives + <see cref="BreakableProp"/>. A prop with no exact collider stays without one (the builder
    /// left it out on purpose, e.g. it stood on a traffic lane).
    /// </summary>
    public static class EnvironmentPhysicsSetup
    {
        private enum Kind { Skip, Breakable, Solid, SolidPoleOnly, Trunk, Convex, Mesh }

        private struct Rule
        {
            public string Match;
            public Kind Kind;
            public float Mass;
            public float BreakSpeed;
            public Rule(string match, Kind kind, float mass = 0f, float breakSpeed = 0f)
            {
                Match = match; Kind = kind; Mass = mass; BreakSpeed = breakSpeed;
            }
        }

        private const float BreakableMaxHeight = 6.5f;
        private const float BreakableMaxWidth = 3.2f;
        private const float TrunkReach = 4f;
        private const string ExactCollidersRoot = "Environment Colliders";
        private const float ExactColliderMatchDistance = 0.05f;

        private static readonly string[] Roots =
        {
            "Roads Props", "Street tree", "Canal Bank Beach Details", "South Coast Beach Details", "15 Beach Food Carts",
            "Coastal Rocky Terrain"
        };

        // Lower-case "contains" matches on the host name, first match wins.
        private static readonly Rule[] Rules =
        {
            new Rule("rooftop billboard", Kind.Skip), new Rule("wall billboard", Kind.Skip), new Rule("grass path", Kind.Skip),
            new Rule("red light", Kind.Skip), new Rule("yellow light", Kind.Skip), new Rule("green light", Kind.Skip),
            new Rule("subway_steps", Kind.Skip), new Rule("pine_", Kind.Skip), new Rule("shrub_", Kind.Skip),

            new Rule("tree railing", Kind.Solid), new Rule("wirebox", Kind.Solid), new Rule("bus stop", Kind.Solid),
            new Rule("constraction", Kind.Solid), new Rule("tubeway", Kind.Solid), new Rule("food cart", Kind.Solid),
            new Rule("cart", Kind.Solid), new Rule("beach furniture", Kind.Solid),

            new Rule("bench", Kind.Breakable, 80f, 5f),
            new Rule("trach box", Kind.Breakable, 150f, 6f),
            new Rule("trach can", Kind.Breakable, 25f, 3f),
            new Rule("trash", Kind.Breakable, 25f, 3f),
            new Rule("mailbox", Kind.Breakable, 40f, 4f),
            new Rule("hyp", Kind.Breakable, 60f, 4f),
            new Rule("barricade", Kind.Breakable, 30f, 3f),
            new Rule("cones", Kind.Breakable, 8f, 2f),
            new Rule("bollard", Kind.Breakable, 30f, 3f),
            new Rule("railing", Kind.Breakable, 30f, 3f),
            new Rule("bicaclestand", Kind.Breakable, 40f, 4f),
            new Rule("speed limit", Kind.Breakable, 30f, 3.5f),
            new Rule("road billboards", Kind.Breakable, 30f, 3.5f),
            new Rule("streetclock", Kind.Breakable, 100f, 6f),
            new Rule("street light", Kind.Breakable, 120f, 6f),
            new Rule("metal street props", Kind.Breakable, 80f, 5f),
            new Rule("phonebooth", Kind.Breakable, 120f, 6f),

            new Rule("signal", Kind.SolidPoleOnly),
            new Rule("billboard", Kind.Solid),
            new Rule("existing curved city palm", Kind.Trunk),
            new Rule("beach granite boulder", Kind.Convex),
            new Rule("rock_", Kind.Mesh), new Rule("fractured rock", Kind.Mesh), new Rule("ground stones", Kind.Mesh),
        };

        [MenuItem("Tools/OpenworldCrime/Environment/Setup Prop Colliders And Breakables (Active Scene)")]
        private static void RunMenu() => Debug.Log(Run());

        /// <summary>Runs the setup on the active scene. Returns the report.</summary>
        public static string Run()
        {
            Scene scene = SceneManager.GetActiveScene();
            var counts = new Dictionary<string, int>();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Environment Physics Setup");
            Dictionary<string, List<Transform>> exact = FindExactColliders(scene);

            foreach (Transform root in FindPropRoots(scene))
            {
                var covered = new HashSet<Renderer>();
                foreach (LODGroup lodGroup in root.GetComponentsInChildren<LODGroup>(true))
                {
                    LOD[] lods = lodGroup.GetLODs();
                    foreach (LOD lod in lods)
                        foreach (Renderer r in lod.renderers)
                            if (r != null) covered.Add(r);
                    var lod0 = new List<MeshRenderer>();
                    if (lods.Length > 0)
                        foreach (Renderer r in lods[0].renderers)
                            if (r is MeshRenderer mr) lod0.Add(mr);
                    Count(counts, root.name.Trim(), SetupHost(root.name.Trim(), lodGroup.transform, lod0, exact));
                }
                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (covered.Contains(renderer) || renderer.GetComponentInParent<LODGroup>(true) != null) continue;
                    Count(counts, root.name.Trim(), SetupHost(root.name.Trim(), renderer.transform, new List<MeshRenderer> { renderer }, exact));
                }
            }
            Undo.CollapseUndoOperations(group);

            var sb = new StringBuilder($"[{nameof(EnvironmentPhysicsSetup)}] '{scene.name}':\n");
            var keys = new List<string>(counts.Keys);
            keys.Sort();
            foreach (string key in keys) sb.AppendLine($"  {key}: {counts[key]}");
            return sb.ToString();
        }

        // The prop groups are scene roots, or children of the "env" object in scenes built from that prefab.
        private static List<Transform> FindPropRoots(Scene scene)
        {
            var found = new List<Transform>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (System.Array.IndexOf(Roots, root.name.Trim()) >= 0) found.Add(root.transform);
                if (root.name != "env") continue;
                foreach (Transform child in root.transform)
                    if (System.Array.IndexOf(Roots, child.name.Trim()) >= 0) found.Add(child);
            }
            return found;
        }

        // Exact collider objects of the scene by name (each is named after the renderer it copies), or null if there are none.
        private static Dictionary<string, List<Transform>> FindExactColliders(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != ExactCollidersRoot) continue;
                var byName = new Dictionary<string, List<Transform>>();
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                {
                    if (!byName.TryGetValue(collider.name, out List<Transform> list)) byName[collider.name] = list = new List<Transform>();
                    list.Add(collider.transform);
                }
                return byName;
            }
            return null;
        }

        // The exact collider objects that copy the host's renderers.
        private static List<Transform> MatchExactColliders(Transform host, Dictionary<string, List<Transform>> exact)
        {
            var matches = new List<Transform>();
            foreach (MeshRenderer r in host.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!exact.TryGetValue(r.name, out List<Transform> list)) continue;
                foreach (Transform t in list)
                    if (t != null && (t.position - r.transform.position).sqrMagnitude < ExactColliderMatchDistance * ExactColliderMatchDistance) matches.Add(t);
            }
            return matches;
        }

        private static string SetupHost(string rootName, Transform host, List<MeshRenderer> lod0, Dictionary<string, List<Transform>> exact)
        {
            if (lod0.Count == 0) return "no LOD0 renderer";
            if (host.GetComponent<Collider>() != null) return "already has collider";
            foreach (MeshRenderer r in lod0) if (r.GetComponent<Collider>() != null) return "already has collider";

            string name = host.name.ToLowerInvariant();
            Rule rule = new Rule(string.Empty, Kind.Solid);
            bool matched = false;
            foreach (Rule candidate in Rules)
            {
                if (!name.Contains(candidate.Match)) continue;
                rule = candidate;
                matched = true;
                break;
            }

            // Street trees: the bark mesh gets a trunk capsule, the leaf mesh nothing.
            if (rootName == "Street tree" && !name.Contains("railing"))
            {
                bool bark = false;
                foreach (MeshRenderer r in lod0)
                    if (r.sharedMaterial != null && r.sharedMaterial.name.ToLowerInvariant().Contains("bark")) bark = true;
                rule = new Rule("street tree", bark ? Kind.Trunk : Kind.Skip);
                matched = true;
            }

            Bounds bounds = lod0[0].bounds;
            foreach (MeshRenderer r in lod0) bounds.Encapsulate(r.bounds);
            if (rule.Kind == Kind.Breakable && (bounds.size.y > BreakableMaxHeight || Mathf.Max(bounds.size.x, bounds.size.z) > BreakableMaxWidth))
                rule.Kind = Kind.Solid;

            List<Transform> exactMatches = null;
            if (exact != null)
            {
                if (rule.Kind != Kind.Breakable) return "kept exact collider";
                exactMatches = MatchExactColliders(host, exact);
                if (exactMatches.Count == 0) return "no exact collider, left without";
            }

            switch (rule.Kind)
            {
                case Kind.Skip:
                    return "skipped (decorative)";
                case Kind.Breakable:
                {
                    if (PropColliderTool.FitColliders(host, lod0, true, 0f) == 0) return "skipped (too small)";
                    BreakableProp prop = PropColliderTool.MakeBreakable(host);
                    if (prop == null) return "breakable failed";
                    var so = new SerializedObject(prop);
                    so.FindProperty("brokenMass").floatValue = rule.Mass;
                    so.FindProperty("breakSpeed").floatValue = rule.BreakSpeed;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    ClearStaticFlags(host);
                    if (exactMatches != null)
                        foreach (Transform old in exactMatches) Undo.DestroyObjectImmediate(old.gameObject);
                    return "breakable";
                }
                case Kind.SolidPoleOnly:
                    return PropColliderTool.FitColliders(host, lod0, false, 0f, true, 0f, 0.3f) > 0 ? "solid (pole)" : "skipped (too small)";
                case Kind.Trunk:
                    return PropColliderTool.FitColliders(host, lod0, false, TrunkReach, true, 0.1f, 0.25f) > 0 ? "solid (trunk capsule)" : "skipped (too small)";
                case Kind.Convex:
                case Kind.Mesh:
                {
                    int added = 0;
                    foreach (MeshRenderer r in lod0)
                    {
                        MeshFilter filter = r.GetComponent<MeshFilter>();
                        if (filter == null || filter.sharedMesh == null) continue;
                        var meshCollider = Undo.AddComponent<MeshCollider>(r.gameObject);
                        meshCollider.sharedMesh = filter.sharedMesh;
                        meshCollider.convex = rule.Kind == Kind.Convex;
                        added++;
                    }
                    return added == 0 ? "no mesh" : (rule.Kind == Kind.Convex ? "solid (convex mesh)" : "solid (static mesh)");
                }
                default:
                    return PropColliderTool.FitColliders(host, lod0, true, 0f) > 0 ? (matched ? "solid" : "solid (default)") : "skipped (too small)";
            }
        }

        // Breakable props move once hit: no static flag at all (they are still occlusion culled as dynamic objects).
        private static void ClearStaticFlags(Transform host)
        {
            foreach (Transform t in host.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) == 0) continue;
                Undo.RecordObject(t.gameObject, "Clear Static Flags");
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            }
        }

        private static void Count(Dictionary<string, int> counts, string root, string result)
        {
            string key = $"{root} / {result}";
            counts.TryGetValue(key, out int n);
            counts[key] = n + 1;
        }
    }
}
