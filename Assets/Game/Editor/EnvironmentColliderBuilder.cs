using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Adds collision for the environment without touching the env prefab: everything is created under one
// scene object, so running this again replaces it and deleting that object removes it all.
public static class EnvironmentColliderBuilder
{
    const string RootName = "Environment Colliders";
    const string OldBuildingBoxesName = "Building Colliders";
    const string PierName = "Water pier collision";
    const float LaneClearance = 1.3f;

    // These have purpose-built collision on separate objects, so only parts it doesn't reach need adding.
    static readonly string[] HasOwnCollision =
    {
        "Landmark Boundary Fences", "Street Sports Hub - polySurface742", "Taxi Stand - Corner 39",
        "Miami Clothing Store", "Gangfire Weapon Shop", "Garden Perimeter - polySurface47", "South Beach Bays and Docking Piers",
    };
    // Light and effect meshes have nothing solid to bump into. Whole words only: "led" must not match "detailed".
    static readonly Regex EffectName = new Regex(@"^water$|^night lamp bulbs|\b(searchlight|halo|glow|flare)\b|\bled strips?\b|\blight strips?\b|\baccent strips?\b|\bedge lights?\b|^(red|yellow|green) light\b", RegexOptions.IgnoreCase);
    static readonly Regex LodName = new Regex(@"(^|[^a-z])(lod[1-9]|mobile lod|far|distant)([^a-z]|$)", RegexOptions.IgnoreCase);
    static readonly Regex FoliageMaterial = new Regex(@"palmatlas|leaf|leaves|foliage", RegexOptions.IgnoreCase);
    static readonly Regex BarkMaterial = new Regex(@"bark", RegexOptions.IgnoreCase);

    [MenuItem("Game/Build Environment Colliders")]
    public static string Build()
    {
        var scene = SceneManager.GetActiveScene();
        var env = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "env");
        if (env == null) return "No env object in the scene";

        // The old building boxes filled whole city blocks; buildings now collide with their own meshes here.
        foreach (var old in scene.GetRootGameObjects().Where(g => g.name == RootName || g.name == OldBuildingBoxesName).ToList()) Object.DestroyImmediate(old);
        var controller = Object.FindFirstObjectByType<CharacterController>();
        if (controller != null) controller.enabled = false;
        Physics.SyncTransforms();

        var covered = new HashSet<string>();
        foreach (var name in new[] { "Walk Ground Colliders", "Traffic Road Colliders" })
        {
            var existing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
            if (existing == null) continue;
            foreach (Transform t in existing.transform) covered.Add(Key(t.position, t.name.Substring(t.name.IndexOf('_') + 1)));
        }

        var root = new GameObject(RootName);
        var seen = new HashSet<string>();
        var lodCopies = new List<MeshRenderer>();
        var flatPieces = new List<MeshRenderer>();
        int meshes = 0, trunks = 0, flatGround = 0;
        int alreadyHad = 0, lods = 0, effects = 0, flat = 0, foliage = 0, duplicates = 0, hidden = 0;

        foreach (Transform group in env.transform)
        {
            var ownCollision = HasOwnCollision.Contains(group.name) ? group.GetComponentsInChildren<Collider>(true) : new Collider[0];
            Transform parent = null;
            foreach (var renderer in group.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                // Something the game never draws must not become an invisible wall.
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    hidden++;
                    continue;
                }
                // A few terrain chunks have vertices but no triangles: they draw nothing and can't collide.
                if (Enumerable.Range(0, filter.sharedMesh.subMeshCount).All(s => filter.sharedMesh.GetIndexCount(s) == 0)) continue;
                Bounds b = renderer.bounds;
                if (renderer.GetComponent<Collider>() != null || covered.Contains(Key(renderer.transform.position, renderer.name))
                    || ownCollision.Any(c => c.bounds.Intersects(b)))
                {
                    alreadyHad++;
                    continue;
                }
                if (LodName.IsMatch(renderer.name))
                {
                    lodCopies.Add(renderer);
                    continue;
                }
                if (EffectName.IsMatch(renderer.name))
                {
                    effects++;
                    continue;
                }

                float wide = Mathf.Max(b.size.x, b.size.z);
                if (b.size.y < 0.25f && wide < 60f)
                {
                    flatPieces.Add(renderer);
                    continue;
                }
                string material = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "";
                if (FoliageMaterial.IsMatch(material))
                {
                    foliage++;
                    continue;
                }
                if (!seen.Add(Key(renderer.transform.position, filter.sharedMesh.name)))
                {
                    duplicates++;
                    continue;
                }

                if (parent == null)
                {
                    parent = new GameObject(group.name).transform;
                    parent.SetParent(root.transform, false);
                }
                var go = new GameObject(renderer.name);
                go.transform.SetParent(parent, false);
                go.isStatic = true;

                if (BarkMaterial.IsMatch(material))
                {
                    go.transform.position = TrunkBase(renderer, filter.sharedMesh, b);
                    var trunk = go.AddComponent<CapsuleCollider>();
                    trunk.radius = 0.4f;
                    trunk.height = Mathf.Min(b.size.y, 4f);
                    trunk.center = Vector3.up * trunk.height * 0.5f;
                    trunks++;
                    continue;
                }

                AddMeshCollider(go, renderer, filter.sharedMesh);
                meshes++;
            }
        }

        // A LOD-named mesh is usually a copy of something already solid, but some are the only mesh at that spot.
        Physics.SyncTransforms();
        var lodParent = new GameObject("LOD meshes with no near copy").transform;
        lodParent.SetParent(root.transform, false);
        foreach (var renderer in lodCopies)
        {
            Bounds b = renderer.bounds;
            string material = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "";
            if (b.size.y < 0.25f || FoliageMaterial.IsMatch(material) || EffectName.IsMatch(renderer.name)
                || Physics.CheckBox(b.center, b.extents + Vector3.one * 0.05f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            {
                lods++;
                continue;
            }
            var go = new GameObject(renderer.name);
            go.transform.SetParent(lodParent, false);
            go.isStatic = true;
            AddMeshCollider(go, renderer, renderer.GetComponent<MeshFilter>().sharedMesh);
            meshes++;
        }

        // Most flat pieces are paint on top of something solid, but some are the only floor at that spot.
        Physics.SyncTransforms();
        var floorParent = new GameObject("Flat ground with nothing under it").transform;
        floorParent.SetParent(root.transform, false);
        foreach (var renderer in flatPieces)
        {
            if (HasFloorUnder(renderer.bounds))
            {
                flat++;
                continue;
            }
            var go = new GameObject(renderer.name);
            go.transform.SetParent(floorParent, false);
            go.isStatic = true;
            AddMeshCollider(go, renderer, renderer.GetComponent<MeshFilter>().sharedMesh);
            flatGround++;
        }

        Physics.SyncTransforms();
        int piersDisabled = DisablePiersOnLanes(env);
        Physics.SyncTransforms();
        var removed = ClearLanes(root.transform, out int removedCount);
        Physics.SyncTransforms();
        string walkReport = CheckFootpaths();
        if (controller != null) controller.enabled = true;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "Added: mesh " + meshes + ", flat ground " + flatGround + ", tree trunks " + trunks
            + " | Skipped: hidden " + hidden + ", already had collision " + alreadyHad + ", distant LOD copies " + lods + ", lights/effects " + effects
            + ", flat decals " + flat + ", leaves " + foliage + ", duplicates " + duplicates
            + " | Invisible piers on lanes switched off: " + piersDisabled
            + " | Removed for blocking lanes: " + removedCount + " [" + removed + "] | " + walkReport;
    }

    // Collision is the object's own mesh, so it is exactly the size and shape that is drawn.
    static void AddMeshCollider(GameObject go, Renderer renderer, Mesh mesh)
    {
        go.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
        go.transform.localScale = renderer.transform.lossyScale;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    // Checks the middle and the corners, since a piece can hang over the edge of what is below it.
    static bool HasFloorUnder(Bounds b)
    {
        float[] xs = { b.center.x, b.min.x + b.extents.x * 0.2f, b.max.x - b.extents.x * 0.2f };
        float[] zs = { b.center.z, b.min.z + b.extents.z * 0.2f, b.max.z - b.extents.z * 0.2f };
        foreach (float x in xs)
        {
            foreach (float z in zs)
            {
                var from = new Vector3(x, b.max.y + 0.3f, z);
                if (!Physics.Raycast(from, Vector3.down, b.size.y + 1.5f, ~0, QueryTriggerInteraction.Ignore)) return false;
            }
        }
        return true;
    }

    static string Key(Vector3 position, string name)
    {
        return position.ToString("F2") + "|" + name;
    }

    // Where the trunk meets the ground. Bark meshes include the frond stems, so the bounds centre can be off the trunk.
    static Vector3 TrunkBase(Renderer renderer, Mesh mesh, Bounds b)
    {
        if (!mesh.isReadable) return new Vector3(b.center.x, b.min.y, b.center.z);
        var low = mesh.vertices.Select(v => renderer.transform.TransformPoint(v)).Where(p => p.y < b.min.y + 0.5f).ToList();
        if (low.Count == 0) return new Vector3(b.center.x, b.min.y, b.center.z);
        return new Vector3(low.Average(p => p.x), b.min.y, low.Average(p => p.z));
    }

    // Some of the prefab's pier boxes stand right across a lane, with no pier to be seen there: an invisible wall
    // that stops a car dead and backs up all the traffic behind it. Those are switched off.
    static int DisablePiersOnLanes(GameObject env)
    {
        var manager = Object.FindFirstObjectByType<TSMainManager>(FindObjectsInactive.Include);
        if (manager == null) return 0;
        int disabled = 0;
        foreach (var pier in env.GetComponentsInChildren<BoxCollider>(true).Where(b => b.name == PierName && b.enabled))
        {
            Bounds b = pier.bounds;
            b.Expand(new Vector3(LaneClearance * 2f, 0f, LaneClearance * 2f));
            bool onLane = manager.lanes.Any(lane => lane.points.Any(p => b.Contains(p.point))
                || lane.connectors.Any(c => c.points.Any(p => b.Contains(p.point))));
            if (!onLane) continue;
            pier.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(pier);
            EditorUtility.SetDirty(pier);
            disabled++;
        }
        return disabled;
    }

    // Drive a car-sized box down every lane and connector; a new collider in the way would stop traffic.
    static string ClearLanes(Transform root, out int count)
    {
        var offenders = new Dictionary<Collider, Vector3>();
        var manager = Object.FindFirstObjectByType<TSMainManager>(FindObjectsInactive.Include);
        var hits = new Collider[16];
        void Sweep(TSPoints[] points)
        {
            for (int i = 1; i < points.Length; i += 2)
            {
                Vector3 p = points[i].point;
                Vector3 dir = points[i].point - points[i - 1].point;
                dir.y = 0f;
                Quaternion rotation = dir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(dir) : Quaternion.identity;
                int n = Physics.OverlapBoxNonAlloc(p + Vector3.up * 1.2f, new Vector3(1.05f, 0.75f, 1.5f), hits, rotation, ~0, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < n; h++)
                {
                    if (hits[h].transform.IsChildOf(root) && !offenders.ContainsKey(hits[h])) offenders[hits[h]] = p;
                }
            }
        }
        if (manager != null)
        {
            foreach (var lane in manager.lanes)
            {
                Sweep(lane.points);
                foreach (var connector in lane.connectors) Sweep(connector.points);
            }
        }
        count = offenders.Count;
        var names = offenders.Keys.GroupBy(c => c.transform.parent.name + "/" + c.name).OrderByDescending(g => g.Count()).Take(12)
            .Select(g => g.Key + " x" + g.Count());
        string summary = string.Join(", ", names);
        foreach (var c in offenders.Keys.ToList()) Object.DestroyImmediate(c.gameObject);
        return summary;
    }

    // How much of the footpath can a person-sized capsule still stand on?
    static string CheckFootpaths()
    {
        var network = Object.FindFirstObjectByType<FootpathLaneNetwork>(FindObjectsInactive.Include);
        if (network == null) return "no footpath lanes";
        int total = 0, blocked = 0;
        foreach (var lane in network.lanes)
        {
            for (int i = 0; i < lane.points.Length; i += 3)
            {
                total++;
                Vector3 g = lane.points[i];
                if (Physics.CheckCapsule(g + Vector3.up * 0.6f, g + Vector3.up * 1.5f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) blocked++;
            }
        }
        return "Footpath points blocked " + blocked + "/" + total;
    }
}
