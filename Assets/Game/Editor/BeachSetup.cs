using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Game/Beach Setup: a vendor and a working order spot (FoodStall) at every beach food cart, and a few people on the beaches
// (sitting on beach benches and on the sand beside the umbrellas). Re-runnable: what it placed last time is removed first; the
// same seed gives the same people.
public static class BeachSetup
{
    const string CartsRoot = "15 Beach Food Carts";
    const string StallHolder = "Food Stall";
    const string PeopleRoot = "Beach People";
    const string ServeClip = "Assets/AudioClip 1/Cash.ogg";
    static readonly string[] Vendors = { "Man_33", "Girl_22", "Man_55", "Girl_77", "Man_88", "Girl_44", "Man_11" };
    static readonly string[] Bathers = { "Man_22", "Girl_11", "Man_44", "Girl_33", "Man_66", "Girl_55", "Man_99", "Girl_66", "Man_77", "Girl_77", "Man_111" };
    const int BenchSitters = 10;
    const int SandSitters = 10;
    const float PeopleSpacing = 25f;
    const int Seed = 2024;

    [MenuItem("Game/Beach Setup")]
    static void Run() => Debug.Log(SetUp());

    public static string SetUp() => SetUpFoodStalls() + " " + SetUpBeachPeople();

    // ---------------------------------------------------------------- food stalls

    public static string SetUpFoodStalls()
    {
        Transform carts = Find(CartsRoot, t => t.parent != null && t.parent.name == "env") ?? Find(CartsRoot, null);
        if (carts == null) return "[BeachSetup] no '" + CartsRoot + "'.";
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ServeClip);
        int triggers = LayerMask.NameToLayer("Triggers");
        int made = 0;
        foreach (Transform cart in carts)
        {
            var old = cart.Find(StallHolder);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Bounds bounds = RendererBounds(cart);
            Vector3 front = -Flat(cart.forward);            // the sign side, where customers stand
            const float depth = 0.75f;                     // every cart is the same 1.5 m deep model

            var holder = new GameObject(StallHolder).transform;
            holder.SetParent(cart, false);
            holder.rotation = Quaternion.identity;
            Vector3 scale = cart.lossyScale;   // people keep their own size whatever the cart's scale
            holder.localScale = new Vector3(1f / Mathf.Max(0.001f, scale.x), 1f / Mathf.Max(0.001f, scale.y), 1f / Mathf.Max(0.001f, scale.z));

            // The vendor behind the cart, facing the customers.
            Vector3 vendorAt = GroundAt(bounds.center - front * (depth + 0.45f), cart);
            float yaw = Quaternion.LookRotation(front).eulerAngles.y;
            ShowroomSetup.Staff(holder, "Vendor", Vendors[made % Vendors.Length], ShowroomStaff.Duty.Stand, yaw, holder.InverseTransformPoint(vendorAt));
            var vendor = holder.childCount > 0 ? holder.GetChild(holder.childCount - 1).GetComponentInChildren<Animator>() : null;

            // Where the customer stands, with the trigger the player's interaction sensor picks up.
            Vector3 customerAt = GroundAt(bounds.center + front * (depth + 0.7f), cart);
            var order = new GameObject("Order Point");
            order.transform.SetParent(holder, false);
            order.transform.SetPositionAndRotation(customerAt, Quaternion.LookRotation(-front));
            if (triggers >= 0) order.layer = triggers;
            var trigger = order.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 1.4f;
            trigger.center = new Vector3(0f, 1f, 0f);

            var counter = new GameObject("Counter Point").transform;
            counter.SetParent(order.transform, false);
            counter.position = bounds.center + front * (depth - 0.15f);
            counter.position = new Vector3(counter.position.x, customerAt.y + 1.05f, counter.position.z);

            var stall = order.AddComponent<FoodStall>();
            stall.counterPoint = counter;
            stall.vendor = vendor;
            stall.serveClip = clip;
            stall.eatClip = EatClip();
            made++;
        }
        EditorSceneManager.MarkSceneDirty(carts.gameObject.scene);
        return "[BeachSetup] " + made + " food stalls.";
    }

    const string EatClipFile = "Assets/Female PlayerT-Pose (2)@Drinking.fbx";

    static AnimationClip EatClip()
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(EatClipFile))
            if (o is AnimationClip c && !c.name.StartsWith("__preview")) return c;
        return null;
    }

    // ---------------------------------------------------------------- beach people

    public static string SetUpBeachPeople()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var oldRoot = GameObject.Find(PeopleRoot);
        if (oldRoot != null) Object.DestroyImmediate(oldRoot);
        var root = new GameObject(PeopleRoot).transform;
        var random = new System.Random(Seed);
        var taken = new List<Vector3>();
        int person = 0;

        // Beach benches: one person each, sitting.
        var benches = new List<Transform>();
        var umbrellas = new List<Transform>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t.parent == null || !t.parent.name.Contains("sector")) continue;
            if (t.name.StartsWith("Existing City Beach Bench")) benches.Add(t);
            else if (t.name.Contains("Colorful Beach Furniture")) umbrellas.Add(t);
        }
        Shuffle(benches, random);
        Shuffle(umbrellas, random);

        int seated = 0;
        foreach (var bench in benches)
        {
            if (seated >= BenchSitters) break;
            if (!Free(bench.position, taken)) continue;
            if (!BenchSeat(bench, out Vector3 seat, out Vector3 facing)) continue;
            ShowroomSetup.Staff(root, "Bench Sitter", Bathers[person++ % Bathers.Length], ShowroomStaff.Duty.SitLow,   // beach benches are low (0.3 m)
                Quaternion.LookRotation(facing).eulerAngles.y, seat);
            taken.Add(bench.position);
            seated++;
        }

        // On the deck chairs under the umbrellas (the low-seat sitting moves fit their low seats), one per umbrella.
        int onChairs = 0;
        foreach (var umbrella in umbrellas)
        {
            if (onChairs >= SandSitters) break;
            if (!Free(umbrella.position, taken)) continue;
            var chairs = ChairSeats(umbrella);
            if (chairs.Count == 0) continue;
            var (seat, facing) = chairs[random.Next(chairs.Count)];
            ShowroomSetup.Staff(root, "Deck Chair Sitter", Bathers[person++ % Bathers.Length], ShowroomStaff.Duty.SitLow,
                Quaternion.LookRotation(facing).eulerAngles.y, seat);
            taken.Add(umbrella.position);
            onChairs++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        return "[BeachSetup] beach people: " + seated + " on benches, " + onChairs + " on deck chairs.";
    }

    // Seat top and the way a sitter faces, from the bench's shape: along its length the seat stays level, across it one side has
    // the backrest (high) and the other nothing (the ground).
    static bool BenchSeat(Transform bench, out Vector3 seat, out Vector3 facing)
    {
        seat = facing = Vector3.zero;
        var added = new List<MeshCollider>();
        foreach (var filter in bench.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
            var c = filter.gameObject.AddComponent<MeshCollider>();
            c.sharedMesh = filter.sharedMesh;
            added.Add(c);
        }
        Physics.SyncTransforms();
        try
        {
            Bounds bounds = RendererBounds(bench);
            float ground = GroundAt(bounds.center, bench).y;
            Vector3[] axes = { Flat(bench.forward).normalized, Flat(bench.right).normalized };
            float bestScore = -1f;
            foreach (var across in axes)
            {
                // Highest slat round the middle (a single ray can fall between two slats onto the frame).
                float hCenter = -1f;
                for (float u = -0.12f; u <= 0.121f; u += 0.04f)
                    for (float v = -0.3f; v <= 0.31f; v += 0.1f)
                    {
                        float h = Top(added, bounds.center + across * u + Vector3.Cross(Vector3.up, across) * v, ground);
                        if (h < 0.7f) hCenter = Mathf.Max(hCenter, h);
                    }
                float hPlus = Top(added, bounds.center + across * 0.3f, ground);
                float hMinus = Top(added, bounds.center - across * 0.3f, ground);
                if (hCenter < 0.25f || hCenter > 0.7f) continue;   // not a seat height
                // Across the bench: one side clearly higher (backrest) than the seat.
                float score = Mathf.Abs(hPlus - hMinus);
                if (score <= bestScore) continue;
                bestScore = score;
                facing = hPlus > hMinus ? -across : across;
                seat = new Vector3(bounds.center.x, ground + hCenter, bounds.center.z) + facing * 0.05f;
            }
            return bestScore > 0.15f;
        }
        finally
        {
            foreach (var c in added) Object.DestroyImmediate(c);
            Physics.SyncTransforms();
        }
    }

    // Deck chair seats round an umbrella: the umbrella's mesh is probed from just above chair height on a grid; points 0.2-0.55 m
    // above the sand are seat, grouped into chairs. A chair's raised end (backrest and towel) is behind the sitter.
    static List<(Vector3 seat, Vector3 facing)> ChairSeats(Transform umbrella)
    {
        var result = new List<(Vector3, Vector3)>();
        var added = new List<MeshCollider>();
        foreach (var filter in umbrella.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
            var c = filter.gameObject.AddComponent<MeshCollider>();
            c.sharedMesh = filter.sharedMesh;
            added.Add(c);
        }
        Physics.SyncTransforms();
        try
        {
            float ground = GroundAt(umbrella.position + Vector3.up * 0.3f, umbrella).y;
            var seatPoints = new List<Vector3>();
            for (float x = -2.6f; x <= 2.6f; x += 0.15f)
                for (float z = -2.6f; z <= 2.6f; z += 0.15f)
                {
                    Vector3 p = new Vector3(umbrella.position.x + x, 0f, umbrella.position.z + z);
                    float h = TopFrom(added, p, ground, 1.2f);
                    if (h >= 0.2f && h <= 0.55f) seatPoints.Add(new Vector3(p.x, ground + h, p.z));
                }
            // Group into chairs (points within 0.3 m of each other).
            var groups = new List<List<Vector3>>();
            foreach (var p in seatPoints)
            {
                List<Vector3> home = null;
                foreach (var g in groups)
                    foreach (var q in g)
                        if ((Flat(q - p)).sqrMagnitude < 0.09f) { home = g; break; }
                if (home == null) { home = new List<Vector3>(); groups.Add(home); }
                home.Add(p);
            }
            foreach (var g in groups)
            {
                if (g.Count < 6) continue;
                Vector3 centre = Vector3.zero;
                foreach (var q in g) centre += q;
                centre /= g.Count;
                // Long axis: the direction of the farthest point from the centre.
                Vector3 axis = Vector3.forward; float far = 0f;
                foreach (var q in g) { Vector3 d = Flat(q - centre); if (d.sqrMagnitude > far) { far = d.sqrMagnitude; axis = d.normalized; } }
                // The raised end (backrest, towel): the direction round the seat where the chair is highest.
                Vector3 back = axis; float highest = -1f;
                for (int dir = 0; dir < 8; dir++)
                {
                    Vector3 d = Quaternion.Euler(0f, dir * 45f, 0f) * Vector3.forward;
                    float h = -1f;
                    for (float r = 0.25f; r <= 0.85f; r += 0.15f) h = Mathf.Max(h, TopFrom(added, centre + d * r, ground, 1.2f));
                    if (h > highest) { highest = h; back = d; }
                }
                if (highest < 0.5f) continue;   // no backrest found: not a chair
                Vector3 seat = centre - back * 0.15f;
                seat.y = ground + Mathf.Max(0.2f, TopFrom(added, seat, ground, 1.2f));
                // Clear of the sea: the sand under the chair must be the umbrella's own level.
                if (Mathf.Abs(GroundAt(seat + Vector3.up * 0.1f, umbrella).y - ground) > 0.3f) continue;
                result.Add((seat, -back));
            }
        }
        finally
        {
            foreach (var c in added) Object.DestroyImmediate(c);
            Physics.SyncTransforms();
        }
        return result;
    }

    static float TopFrom(List<MeshCollider> mesh, Vector3 point, float ground, float from)
    {
        float best = -1f;
        var ray = new Ray(new Vector3(point.x, ground + from, point.z), Vector3.down);
        foreach (var c in mesh)
            if (c.Raycast(ray, out RaycastHit hit, from + 0.5f)) best = Mathf.Max(best, hit.point.y - ground);
        return best;
    }

    // Highest point (metres above the ground) of the bench's own mesh under a point, or -1 (the bench root carries a box
    // collider round the whole bench, so the scene is not raycast).
    static float Top(List<MeshCollider> mesh, Vector3 point, float ground)
    {
        float best = -1f;
        var ray = new Ray(new Vector3(point.x, ground + 3f, point.z), Vector3.down);
        foreach (var c in mesh)
            if (c.Raycast(ray, out RaycastHit hit, 4f)) best = Mathf.Max(best, hit.point.y - ground);
        return best;
    }

    // ---------------------------------------------------------------- helpers

    static bool Free(Vector3 point, List<Vector3> taken)
    {
        foreach (var other in taken) if ((other - point).sqrMagnitude < PeopleSpacing * PeopleSpacing) return false;
        return true;
    }

    static Vector3 GroundAt(Vector3 point, Transform ignore)
    {
        var hits = Physics.RaycastAll(point + Vector3.up * 3f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        Vector3 result = point;
        foreach (var hit in hits)
        {
            if (ignore != null && hit.collider.transform.IsChildOf(ignore)) continue;
            if (hit.collider.attachedRigidbody != null) continue;
            // The ground under the object: the lowest wide surface would be wrong on a terrace; take the highest one below the
            // object's own base.
            if (hit.point.y > point.y + 0.5f) continue;
            if (hit.point.y > best) { best = hit.point.y; result = hit.point; }
        }
        return result;
    }

    static Bounds RendererBounds(Transform t)
    {
        var renderers = t.GetComponentsInChildren<Renderer>();
        var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(t.position, Vector3.one);
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        return bounds;
    }

    static Transform Find(string name, System.Func<Transform, bool> where)
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == name && (where == null || where(t))) return t;
        return null;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    static void Shuffle<T>(List<T> list, System.Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
