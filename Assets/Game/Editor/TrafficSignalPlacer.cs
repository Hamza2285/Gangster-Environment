using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Traffic here keeps left, but the signal poles were laid out for traffic keeping right. This gives each road
// coming into a junction one pole, on the driver's left at the stop line, facing the driver with its arm over
// their lanes. The model's arm reaches the other way, so each pole is mirrored. Safe to run again.
public static class TrafficSignalPlacer
{
    const float AlongFromLaneEnd = 4f;
    const float CurbOffset = 4.5f;        // sideways from the centre of the leftmost lane to the footpath
    const float MinAlong = -4f;
    const float MaxAlong = 7f;
    const float MinSide = 3.5f;
    const float MaxSide = 7.5f;
    const float RoadSampleBack = 8f;
    const float MinFootpathStep = 0.06f;
    const float MaxFootpathStep = 0.25f;
    const float JunctionSearch = 70f;
    const float ConnectorsMeetDistance = 2.5f;
    const float MinFromLane = 2.6f;
    const float MinBetweenPoles = 2f;
    const float SameApproachDistance = 14f;
    const float SameDirectionDegrees = 25f;
    const float CrossingDegrees = 30f;
    const float PoleSearchRadius = 70f;
    const float ThroughLaneSearch = 25f;
    const float LanePointSpacing = 8f;
    const float PoleRadius = 0.35f;
    const string MainMeshName = "Signal Enhanced";
    const string Lod1MeshName = "9b3875d0c7bcdc3469fcc0680176876f_4300000_LOD1";
    const string Lod2MeshName = "9b3875d0c7bcdc3469fcc0680176876f_4300000_LOD2";
    const string LeftDriveSuffix = " Left Drive";
    const string MeshFolder = "Assets/Game/Traffic";

    static readonly string[] SourceMeshes =
    {
        "Assets/Scenes/Oasis/TrafficSignals/" + MainMeshName + ".asset",
        "Assets/Scenes/Oasis/MobileOptimization/Meshes/" + Lod1MeshName + ".asset",
        "Assets/Scenes/Oasis/MobileOptimization/Meshes/" + Lod2MeshName + ".asset",
    };

    public sealed class Approach
    {
        public Vector3 end;        // where the leftmost lane reaches the junction
        public Vector3 direction;
        public Transform pole;
        public bool throughLane;
    }

    [MenuItem("Game/Fix Traffic Signal Placement")]
    public static void Run()
    {
        Debug.Log(Place());
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
    }

    public static string Place()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var env = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "env");
        var manager = Object.FindFirstObjectByType<TSMainManager>(FindObjectsInactive.Include);
        if (env == null || manager == null) return "No env or iTS manager in the scene";

        var controller = Object.FindFirstObjectByType<CharacterController>();
        if (controller != null) controller.enabled = false;
        Physics.SyncTransforms();

        var poles = env.GetComponentsInChildren<MeshFilter>(true)
            .Where(f => f.sharedMesh != null && f.sharedMesh.name.StartsWith(MainMeshName))
            .Select(f => f.transform).ToList();
        var lanePoints = new List<Vector3>();
        foreach (var lane in manager.lanes)
        {
            lanePoints.AddRange(lane.points.Select(p => p.point));
            foreach (var connector in lane.connectors) lanePoints.AddRange(connector.points.Select(p => p.point));
        }

        var approaches = FindApproaches(manager);
        var free = new List<Transform>(poles);

        // The pole already nearest and facing the right way moves least.
        foreach (var approach in approaches)
        {
            approach.pole = free.Where(p => Flat(p.position - approach.end).magnitude < PoleSearchRadius)
                .OrderBy(p => Flat(p.position - approach.end).magnitude + Vector3.Angle(FaceOf(p), -approach.direction) * 0.25f)
                .FirstOrDefault();
            if (approach.pole != null) free.Remove(approach.pole);
        }

        // Poles left over at crossings the traffic system drives straight through still have a road to serve.
        int throughLanes = 0;
        foreach (var pole in free.ToList())
        {
            var through = FindThroughLane(pole, manager, approaches);
            if (through == null) continue;
            through.pole = pole;
            approaches.Add(through);
            free.Remove(pole);
            throughLanes++;
        }

        int moved = 0, added = 0, nowhere = 0;
        var template = poles[0];
        var taken = new List<Vector3>();
        var meshes = ReadableSignMeshes();
        foreach (var approach in approaches)
        {
            if (!FindSpot(approach, lanePoints, taken, out Vector3 spot))
            {
                nowhere++;
                if (approach.pole != null) free.Add(approach.pole);
                approach.pole = null;
                continue;
            }
            if (approach.pole == null && free.Count > 0)
            {
                approach.pole = free[free.Count - 1];
                free.RemoveAt(free.Count - 1);
            }
            if (approach.pole == null)
            {
                var copy = Object.Instantiate(template.gameObject, template.parent);
                copy.name = "Signal (added " + (++added) + ")";
                approach.pole = copy.transform;
            }
            Mount(approach.pole, spot, approach.direction, meshes);
            taken.Add(spot);
            moved++;
        }

        // Nothing left for these to serve: a signal with no road in front of it only misleads.
        foreach (var pole in free)
        {
            pole.gameObject.SetActive(false);
            EditorUtility.SetDirty(pole.gameObject);
        }

        if (controller != null) controller.enabled = true;
        return "Signals: approaches " + approaches.Count + " (through lanes " + throughLanes + "), mounted " + moved
            + " (added " + added + "), no room for " + nowhere + ", spare poles switched off " + free.Count;
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    // The lamps look along the pole's negative up axis.
    static Vector3 FaceOf(Transform pole)
    {
        return Flat(-pole.up).normalized;
    }

    // Every road coming into a real junction, with parallel lanes counted once as its leftmost lane.
    public static List<Approach> FindApproaches(TSMainManager manager)
    {
        var lanes = manager.lanes;
        var found = new List<Approach>();
        for (int l = 0; l < lanes.Length; l++)
        {
            var points = lanes[l].points;
            if (points.Length < 2 || !CrossesOtherTraffic(lanes, l)) continue;
            Vector3 end = points[points.Length - 1].point;
            Vector3 direction = EndDirection(points);
            Vector3 left = new Vector3(-direction.z, 0f, direction.x);

            var same = found.FirstOrDefault(a => Vector3.Angle(a.direction, direction) < SameDirectionDegrees
                && Mathf.Abs(a.end.y - end.y) < 3f
                && Flat(a.end - end).magnitude < SameApproachDistance);
            if (same == null)
            {
                found.Add(new Approach { end = end, direction = direction });
            }
            else if (Vector3.Dot(end - same.end, left) > 0f)
            {
                same.end = end;
            }
        }
        return found;
    }

    static Vector3 EndDirection(TSPoints[] points)
    {
        return Flat(points[points.Length - 1].point - points[Mathf.Max(0, points.Length - 3)].point).normalized;
    }

    // A fork or a bend has no one to take turns with, so it gets no signal. iTS only works out which connectors
    // meet once the game runs, so here it comes from where they actually go.
    static bool CrossesOtherTraffic(TSLaneInfo[] lanes, int index)
    {
        var mine = lanes[index];
        Vector3 end = mine.points[mine.points.Length - 1].point;
        Vector3 direction = EndDirection(mine.points);
        for (int o = 0; o < lanes.Length; o++)
        {
            var other = lanes[o];
            if (o == index || other.points.Length < 2) continue;
            Vector3 otherEnd = other.points[other.points.Length - 1].point;
            if (Mathf.Abs(otherEnd.y - end.y) > 3f || Flat(otherEnd - end).magnitude > JunctionSearch) continue;
            if (Vector3.Angle(EndDirection(other.points), direction) < CrossingDegrees) continue;
            if (ConnectorsMeet(mine, other)) return true;
        }
        return false;
    }

    static bool ConnectorsMeet(TSLaneInfo a, TSLaneInfo b)
    {
        float limit = ConnectorsMeetDistance * ConnectorsMeetDistance;
        foreach (var ca in a.connectors)
        {
            foreach (var cb in b.connectors)
            {
                foreach (var pa in ca.points)
                {
                    foreach (var pb in cb.points)
                    {
                        if ((pa.point - pb.point).sqrMagnitude < limit) return true;
                    }
                }
            }
        }
        return false;
    }

    static Approach FindThroughLane(Transform pole, TSMainManager manager, List<Approach> approaches)
    {
        Vector3 face = FaceOf(pole);
        Vector3 best = Vector3.zero, bestDirection = Vector3.zero;
        float bestDistance = ThroughLaneSearch;
        foreach (var lane in manager.lanes)
        {
            var points = lane.points;
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 p = points[i].point;
                float distance = Flat(p - pole.position).magnitude;
                if (distance >= bestDistance || Mathf.Abs(p.y - pole.position.y) > 3f) continue;
                Vector3 direction = Flat(p - points[i - 1].point);
                if (direction.sqrMagnitude < 1e-4f || Vector3.Angle(direction, -face) > SameDirectionDegrees) continue;
                best = p;
                bestDirection = direction.normalized;
                bestDistance = distance;
            }
        }
        if (bestDistance >= ThroughLaneSearch) return null;
        if (approaches.Any(a => Vector3.Angle(a.direction, bestDirection) < SameDirectionDegrees && Flat(a.end - best).magnitude < SameApproachDistance * 2f)) return null;

        // The curb is beyond the leftmost lane going this way, which needn't be the one nearest the old pole.
        Vector3 left = new Vector3(-bestDirection.z, 0f, bestDirection.x);
        float furthest = 0f;
        Vector3 leftmost = best;
        foreach (var lane in manager.lanes)
        {
            var points = lane.points;
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 toPoint = Flat(points[i].point - best);
                float sideways = Vector3.Dot(toPoint, left);
                if (sideways <= furthest || sideways > SameApproachDistance) continue;
                if (Mathf.Abs(Vector3.Dot(toPoint, bestDirection)) > LanePointSpacing) continue;
                if (Vector3.Angle(Flat(points[i].point - points[i - 1].point), bestDirection) > SameDirectionDegrees) continue;
                furthest = sideways;
                leftmost = best + left * sideways;
            }
        }
        return new Approach { end = leftmost, direction = bestDirection, throughLane = true };
    }

    static bool FindSpot(Approach approach, List<Vector3> lanePoints, List<Vector3> taken, out Vector3 spot)
    {
        spot = approach.end;
        if (!RoadHeight(approach, out float roadY)) return false;
        Vector3 left = new Vector3(-approach.direction.z, 0f, approach.direction.x);

        // Best first: just beyond the zebra crossing, close to the curb, so a waiting driver can still see the lamps.
        var tries = new List<Vector2>();
        for (float along = MinAlong; along <= MaxAlong; along += 1f)
        {
            for (float side = MinSide; side <= MaxSide; side += 0.5f) tries.Add(new Vector2(along, side));
        }
        tries.Sort((a, b) => TryCost(a).CompareTo(TryCost(b)));

        foreach (var attempt in tries)
        {
            Vector3 at = approach.end + approach.direction * attempt.x + left * attempt.y;
            if (!FindFootpath(at, roadY, out Vector3 ground)) continue;
            if (!ClearOfLanes(ground, lanePoints)) continue;
            if (!RoomForPole(ground)) continue;
            if (taken.Any(t => (t - ground).sqrMagnitude < MinBetweenPoles * MinBetweenPoles)) continue;
            spot = ground;
            return true;
        }
        return false;
    }

    static float TryCost(Vector2 attempt)
    {
        return Mathf.Abs(attempt.x - AlongFromLaneEnd) + 1.5f * Mathf.Abs(attempt.y - CurbOffset);
    }

    // The lane's end can be on a raised zebra crossing, and a little before it can be up a bridge ramp, so the lower.
    static bool RoadHeight(Approach approach, out float roadY)
    {
        bool atEnd = TopOfGround(approach.end + Vector3.up * 2f, out float endY);
        bool before = TopOfGround(approach.end - approach.direction * RoadSampleBack + Vector3.up * 2f, out float beforeY);
        roadY = Mathf.Min(atEnd ? endY : float.MaxValue, before ? beforeY : float.MaxValue);
        return atEnd || before;
    }

    static bool TopOfGround(Vector3 from, out float y)
    {
        y = float.MinValue;
        foreach (var hit in Physics.RaycastAll(from, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!IsSignal(hit.collider) && hit.point.y > y) y = hit.point.y;
        }
        return y != float.MinValue;
    }

    static bool IsSignal(Collider c)
    {
        return c.name.StartsWith("Signal") || c.transform.parent != null && c.transform.parent.name == "Road signal";
    }

    static bool FindFootpath(Vector3 at, float roadY, out Vector3 ground)
    {
        ground = at;
        var hits = Physics.RaycastAll(new Vector3(at.x, roadY + 1.5f, at.z), Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        foreach (var hit in hits)
        {
            if (IsSignal(hit.collider)) continue;
            if (hit.point.y > best) best = hit.point.y;
        }
        // The footpath is a low step up from the road. Anything level is road, anything taller is a prop.
        float step = best - roadY;
        if (best == float.MinValue || step < MinFootpathStep || step > MaxFootpathStep) return false;
        ground = new Vector3(at.x, best, at.z);
        return true;
    }

    static bool ClearOfLanes(Vector3 ground, List<Vector3> lanePoints)
    {
        float limit = MinFromLane * MinFromLane;
        for (int i = 0; i < lanePoints.Count; i++)
        {
            Vector3 d = lanePoints[i] - ground;
            if (Mathf.Abs(d.y) < 3f && d.x * d.x + d.z * d.z < limit) return false;
        }
        return true;
    }

    static bool RoomForPole(Vector3 ground)
    {
        var hits = Physics.OverlapCapsule(ground + Vector3.up * 0.6f, ground + Vector3.up * 4f, PoleRadius, ~0, QueryTriggerInteraction.Ignore);
        return hits.All(IsSignal);
    }

    // Mirroring a pole mirrors the writing on its signs too, so the poles use copies of the model with that put right.
    static Dictionary<string, Mesh> ReadableSignMeshes()
    {
        var meshes = new Dictionary<string, Mesh>();
        foreach (var source in SourceMeshes)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(source);
            string path = MeshFolder + "/" + name + LeftDriveSuffix + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            meshes[name] = mesh != null ? mesh : CreateReadableCopy(source, path);
        }
        return meshes;
    }

    static Mesh CreateReadableCopy(string source, string path)
    {
        if (!AssetDatabase.IsValidFolder(MeshFolder)) AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(MeshFolder).Replace('\\', '/'), System.IO.Path.GetFileName(MeshFolder));

        var original = AssetDatabase.LoadAssetAtPath<Mesh>(source);
        var settings = new SerializedObject(original);
        var readable = settings.FindProperty("m_IsReadable");
        bool wasReadable = readable.boolValue;
        readable.boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();

        var copy = Object.Instantiate(original);
        copy.name = System.IO.Path.GetFileNameWithoutExtension(path);

        readable.boolValue = wasReadable;
        settings.ApplyModifiedPropertiesWithoutUndo();

        var uv = copy.uv;
        foreach (var panel in TextPanels) FlipPanelWriting(copy.vertices, copy.normals, uv, panel);
        copy.uv = uv;

        AssetDatabase.CreateAsset(copy, path);
        return copy;
    }

    // The model's sign panels: where they are on the pole and where their writing is in the texture.
    struct TextPanel
    {
        public Rect place;   // local x and z
        public Rect texture;
    }

    static readonly TextPanel[] TextPanels =
    {
        new TextPanel { place = Rect.MinMaxRect(5.55f, 4.38f, 6.65f, 4.75f), texture = Rect.MinMaxRect(0.455f, 0.295f, 0.487f, 0.392f) }, // street name
        new TextPanel { place = Rect.MinMaxRect(2.2f, 4.3f, 2.75f, 5.05f), texture = Rect.MinMaxRect(0.331f, 0.261f, 0.386f, 0.340f) },   // stop on red
    };

    static void FlipPanelWriting(Vector3[] vertices, Vector3[] normals, Vector2[] uv, TextPanel panel)
    {
        var inside = new List<int>();
        for (int i = 0; i < vertices.Length; i++)
        {
            if (normals[i].y > -0.7f) continue;
            if (!panel.place.Contains(new Vector2(vertices[i].x, vertices[i].z))) continue;
            if (panel.texture.Contains(uv[i])) inside.Add(i);
        }
        if (inside.Count < 3) return;

        float left = inside.Min(i => vertices[i].x), right = inside.Max(i => vertices[i].x);

        // Texture coordinates vary evenly across a flat panel, so three corners give them everywhere on it.
        int a = inside.OrderBy(i => vertices[i].x + vertices[i].z).First();
        int b = inside.OrderBy(i => -vertices[i].x + vertices[i].z).First();
        int c = inside.OrderBy(i => vertices[i].x - vertices[i].z).First();
        float width = vertices[b].x - vertices[a].x;
        float height = vertices[c].z - vertices[a].z;
        if (Mathf.Abs(width) < 0.01f || Mathf.Abs(height) < 0.01f) return;
        Vector2 perX = (uv[b] - uv[a]) / width;
        Vector2 perZ = (uv[c] - uv[a]) / height;
        Vector2 origin = uv[a];

        foreach (int i in inside)
        {
            float mirroredX = left + right - vertices[i].x;
            uv[i] = origin + perX * (mirroredX - vertices[a].x) + perZ * (vertices[i].z - vertices[a].z);
        }
    }

    static void Mount(Transform pole, Vector3 ground, Vector3 direction, Dictionary<string, Mesh> meshes)
    {
        pole.gameObject.SetActive(true);
        UseMesh(pole, meshes[MainMeshName]);
        UseMesh(pole.Find("Mobile LOD1"), meshes[Lod1MeshName]);
        UseMesh(pole.Find("Mobile LOD2"), meshes[Lod2MeshName]);
        // Yaw 0 looks along +Z: the lamps look back at the oncoming driver.
        float yaw = Mathf.Atan2(-direction.x, -direction.z) * Mathf.Rad2Deg;
        pole.SetPositionAndRotation(ground, Quaternion.Euler(270f, yaw, 0f));
        Vector3 scale = pole.localScale;
        scale.x = -Mathf.Abs(scale.x);
        pole.localScale = scale;
        PrefabUtility.RecordPrefabInstancePropertyModifications(pole);
        EditorUtility.SetDirty(pole);
    }

    static void UseMesh(Transform part, Mesh mesh)
    {
        if (part == null) return;
        var filter = part.GetComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
        EditorUtility.SetDirty(filter);
    }
}
