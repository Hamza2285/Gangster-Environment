using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Solid box colliders just under the two canal roads. Their collision is a single paper-thin, one-sided sheet with a void
/// below: a car pushed into it had nothing to push it back out. The boxes' tops sit a few centimetres under the surface, so
/// normal driving never touches them; only a car sinking into the road does. Game/Canal Road Foundations rebuilds them.
/// </summary>
public static class CanalRoadFoundations
{
    const string RootName = "Canal Road Foundations", Source = "Traffic Road Colliders";
    const float Cell = 3f, Drop = 0.05f, Thick = 3f, Thin = 0.5f;
    static readonly Rect[] Areas = { Rect.MinMaxRect(60f, -740f, 112f, 560f), Rect.MinMaxRect(262f, -740f, 345f, 560f) };   // x, z

    class Patch { public int x0, x1, z0, z1; public float y, slope; public bool thin; }

    [MenuItem("Game/Canal Road Foundations")]
    public static void Menu() => Debug.Log(Build());

    public static string Build()
    {
        var source = GameObject.Find(Source);
        if (source == null) return "No " + Source;
        var old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);
        Physics.SyncTransforms();
        var root = new GameObject(RootName) { isStatic = true };
        int boxes = 0, roads = 0;
        foreach (var road in source.GetComponentsInChildren<MeshCollider>())
        {
            Bounds b = road.bounds;
            if (b.size.y > 8f || !System.Array.Exists(Areas, a => a.Contains(new Vector2(b.center.x, b.center.z)))) continue;
            roads++;
            foreach (Patch p in Patches(road, b))
            {
                float length = (p.z1 - p.z0) * Cell, width = (p.x1 - p.x0) * Cell, height = p.thin ? Thin : Thick;
                var go = new GameObject("Slab " + road.name) { isStatic = true, layer = road.gameObject.layer };
                go.transform.SetParent(root.transform);
                Quaternion tilt = Quaternion.LookRotation(new Vector3(0f, p.slope, 1f));
                Vector3 top = new Vector3(b.min.x + (p.x0 + p.x1) * 0.5f * Cell, p.y, b.min.z + (p.z0 + p.z1) * 0.5f * Cell);
                go.transform.SetPositionAndRotation(top - tilt * Vector3.up * (Drop + height * 0.5f), tilt);
                go.AddComponent<BoxCollider>().size = new Vector3(width, height, length * Mathf.Sqrt(1f + p.slope * p.slope));
                boxes++;
            }
        }
        EditorSceneManager.MarkSceneDirty(root.scene);
        return "[CanalRoadFoundations] " + roads + " road pieces, " + boxes + " boxes";
    }

    // Cells of the road that are wholly on it, merged into strips across (x), then along (z) while they are flat.
    static List<Patch> Patches(MeshCollider road, Bounds b)
    {
        int nx = Mathf.FloorToInt(b.size.x / Cell), nz = Mathf.FloorToInt(b.size.z / Cell);
        var done = new List<Patch>();
        var open = new List<Patch>();
        for (int z = 0; z < nz; z++)
        {
            var row = new List<Patch>();
            Patch run = null;
            for (int x = 0; x < nx; x++)
            {
                Patch c = Sample(road, b, x, z);
                if (c == null) { run = null; continue; }
                if (run != null && run.thin == c.thin && Mathf.Abs(run.y - c.y) < 0.02f && Mathf.Abs(run.slope - c.slope) < 0.004f) { run.x1 = x + 1; continue; }
                row.Add(run = c);
            }
            var next = new List<Patch>();
            foreach (Patch r in row)
            {
                Patch over = Mathf.Abs(r.slope) > 0.002f ? null
                    : open.Find(o => o.x0 == r.x0 && o.x1 == r.x1 && o.thin == r.thin && Mathf.Abs(o.slope) <= 0.002f && Mathf.Abs(o.y - r.y) < 0.02f);
                if (over != null) { over.z1 = z + 1; next.Add(over); }
                else { done.Add(r); next.Add(r); }
            }
            open = next;
        }
        return done;
    }

    static Patch Sample(MeshCollider road, Bounds b, int x, int z)
    {
        float x0 = b.min.x + x * Cell, z0 = b.min.z + z * Cell;
        var h = new float[9];
        for (int i = 0; i < 9; i++)
        {
            var from = new Vector3(x0 + Cell * (i % 3) * 0.5f, b.max.y + 1f, z0 + Cell * (i / 3) * 0.5f);
            if (!road.Raycast(new Ray(from, Vector3.down), out RaycastHit hit, b.size.y + 3f) || hit.normal.y < 0.8f) return null;
            h[i] = hit.point.y;
        }
        float slope = (h[7] - h[1]) / Cell, y = h[4];
        // Plane check: anything that bends inside the cell gets no slab (it could poke out of the surface).
        for (int i = 0; i < 9; i++) if (Mathf.Abs(h[i] - (y + slope * Cell * (i / 3 - 1) * 0.5f)) > Drop * 0.6f) return null;
        // A road or path passing close below (underpass): keep the slab thin.
        bool thin = false;
        foreach (RaycastHit below in Physics.RaycastAll(new Vector3(x0 + Cell * 0.5f, y - 0.3f, z0 + Cell * 0.5f), Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore))
            if (below.normal.y > 0.5f) thin = true;
        return new Patch { x0 = x, x1 = x + 1, z0 = z, z1 = z + 1, y = y, slope = slope, thin = thin };
    }
}
