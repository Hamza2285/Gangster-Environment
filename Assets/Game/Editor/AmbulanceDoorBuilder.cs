using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Game > Build Ambulance Rear Doors. The ambulance model has its rear doors painted on the body, so they are cut out of it:
// every triangle of the back face is clipped against the door outline; what lies inside goes to two door leaves hinged at
// the outer edges (with back faces, so they show from inside too) and the rest stays on the body. A dark cargo box behind
// them is what you see when they open. Rerun after rebuilding the ambulance (FinalCarBuilder.BuildAmbulance).
public static class AmbulanceDoorBuilder
{
    const string PrefabPath = "Assets/_Project/Prefabs/Vehicles/Drivable/Drivable_Ambulance.prefab";
    const string MeshFolder = "Assets/_Project/Prefabs/Vehicles/Drivable/Ambulance Doors";
    const string ModelPath = "Assets/New Folder/Ambulance.fbm/Ambulance.fbx";
    // Door outline on the back face, vehicle space (metres), read off an orthographic render of the rear.
    const float XMin = -0.85f, XMax = 0.85f, YMin = 0.58f, YMax = 2.02f;
    const float FloorY = 0.62f;

    struct V
    {
        public Vector3 p, n;
        public Vector2 uv, uv2;
        public static V Lerp(V a, V b, float t) => new V
        {
            p = Vector3.Lerp(a.p, b.p, t), n = Vector3.Lerp(a.n, b.n, t).normalized,
            uv = Vector2.Lerp(a.uv, b.uv, t), uv2 = Vector2.Lerp(a.uv2, b.uv2, t),
        };
    }

    // A door leaf's geometry, per material.
    class Leaf
    {
        public readonly List<V> verts = new List<V>();
        public readonly Dictionary<Material, List<int>> tris = new Dictionary<Material, List<int>>();
        public void Add(Material m, List<V> polygon)
        {
            if (!tris.TryGetValue(m, out var list)) tris[m] = list = new List<int>();
            int start = verts.Count;
            verts.AddRange(polygon);
            for (int i = 1; i + 1 < polygon.Count; i++) { list.Add(start); list.Add(start + i); list.Add(start + i + 1); }
        }
    }

    [MenuItem("Game/Build Ambulance Rear Doors")]
    public static void Run() => Debug.Log(Build());

    public static string Build()
    {
        Directory.CreateDirectory(MeshFolder);
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var rootT = root.transform;
            // A rerun starts again from the model's own meshes.
            var old = rootT.Find("Rear Doors");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var originals = new Dictionary<string, Mesh>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ModelPath)) if (o is Mesh m) originals[m.name] = m;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.name.EndsWith(" (doors cut)")) continue;
                string name = mf.sharedMesh.name.Substring(0, mf.sharedMesh.name.Length - " (doors cut)".Length);
                if (originals.TryGetValue(name, out var original)) mf.sharedMesh = original;
            }

            // Body meshes: the car's own visible meshes (not wheels, lamps or the seated driver).
            var bodies = new List<MeshFilter>();
            var seat = rootT.Find("DriverSeat");
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<MeshRenderer>() == null) continue;
                if (mf.name.StartsWith("Wheel") || mf.GetComponentInParent<RCC_Exhaust>() != null) continue;
                if (seat != null && mf.transform.IsChildOf(seat)) continue;
                bodies.Add(mf);
            }
            // The door face: the rearmost body point inside the door outline (the bumper below sticks out further).
            float rearZ = 0f;
            foreach (var mf in bodies)
            {
                var m = rootT.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = m.MultiplyPoint3x4(v);
                    if (p.x > XMin + 0.05f && p.x < XMax - 0.05f && p.y > YMin + 0.05f && p.y < YMax - 0.05f) rearZ = Mathf.Min(rearZ, p.z);
                }
            }

            var left = new Leaf();
            var right = new Leaf();
            int cut = 0;
            foreach (var mf in bodies) cut += CutBody(mf, rootT, rearZ, left, right);
            if (cut == 0) return "no back-face triangles found inside the door outline";

            // Hinges at the outer edges of the doors, on the back face.
            var doors = new GameObject("Rear Doors").transform;
            doors.SetParent(rootT, false);
            var leftHinge = new GameObject("Left Hinge").transform;
            leftHinge.SetParent(rootT, false);
            leftHinge.localPosition = new Vector3(XMin, 0f, rearZ);
            leftHinge.SetParent(doors, true);
            var rightHinge = new GameObject("Right Hinge").transform;
            rightHinge.SetParent(rootT, false);
            rightHinge.localPosition = new Vector3(XMax, 0f, rearZ);
            rightHinge.SetParent(doors, true);
            MakeLeaf("Left Door", left, leftHinge, rootT, root.layer);
            MakeLeaf("Right Door", right, rightHinge, rootT, root.layer);
            MakeCargoBox(doors, rootT, rearZ, root.layer);

            var rear = root.GetComponent<AmbulanceRearDoors>() ?? root.AddComponent<AmbulanceRearDoors>();
            rear.leftHinge = leftHinge;
            rear.rightHinge = rightHinge;
            rear.rearZ = rearZ;
            rear.floorY = FloorY;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            return $"rear doors cut: {cut} back-face triangles clipped, back face at z {rearZ:F2}";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // Splits one body mesh: back-face triangles are clipped into door parts and body parts; the body keeps the rest.
    static int CutBody(MeshFilter mf, Transform root, float rearZ, Leaf left, Leaf right)
    {
        var mesh = mf.sharedMesh;
        var toRoot = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
        var toMesh = toRoot.inverse;
        var positions = mesh.vertices;
        var normals = mesh.normals;
        var uvs = mesh.uv;
        var uv2s = mesh.uv2;
        var materials = mf.GetComponent<MeshRenderer>().sharedMaterials;

        var outVerts = new List<V>();
        var outTris = new List<List<int>>();
        int clipped = 0;
        V Read(int i) => new V
        {
            p = toRoot.MultiplyPoint3x4(positions[i]),
            n = normals.Length > i ? toRoot.MultiplyVector(normals[i]).normalized : Vector3.back,
            uv = uvs.Length > i ? uvs[i] : Vector2.zero,
            uv2 = uv2s.Length > i ? uv2s[i] : Vector2.zero,
        };
        void Emit(List<int> list, List<V> polygon)
        {
            if (polygon.Count < 3) return;
            int start = outVerts.Count;
            outVerts.AddRange(polygon);
            for (int i = 1; i + 1 < polygon.Count; i++) { list.Add(start); list.Add(start + i); list.Add(start + i + 1); }
        }

        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var list = new List<int>();
            outTris.Add(list);
            var tris = mesh.GetTriangles(s);
            var material = s < materials.Length ? materials[s] : materials[materials.Length - 1];
            for (int t = 0; t < tris.Length; t += 3)
            {
                var a = Read(tris[t]); var b = Read(tris[t + 1]); var c = Read(tris[t + 2]);
                // Facing backwards by the mesh's own normals (winding order differs between exporters).
                Vector3 facing = a.n + b.n + c.n;
                bool back = a.p.z < rearZ + 0.06f && b.p.z < rearZ + 0.06f && c.p.z < rearZ + 0.06f
                            && facing.sqrMagnitude > 1e-6f && facing.normalized.z < -0.6f;
                var poly = new List<V> { a, b, c };
                if (!back) { Emit(list, poly); continue; }

                // Door leaves.
                var l = Clip(Clip(Clip(Clip(poly, 0, XMin, true), 0, 0f, false), 1, YMin, true), 1, YMax, false);
                var r = Clip(Clip(Clip(Clip(poly, 0, 0f, true), 0, XMax, false), 1, YMin, true), 1, YMax, false);
                if (l.Count >= 3) left.Add(material, l);
                if (r.Count >= 3) right.Add(material, r);
                // Body around them: left strip, right strip, below and above the doors.
                Emit(list, Clip(poly, 0, XMin, false));
                Emit(list, Clip(poly, 0, XMax, true));
                var middle = Clip(Clip(poly, 0, XMin, true), 0, XMax, false);
                Emit(list, Clip(middle, 1, YMin, false));
                Emit(list, Clip(middle, 1, YMax, true));
                clipped++;
            }
        }
        if (clipped == 0) return 0;

        var body = new Mesh { name = mesh.name + " (doors cut)" };
        body.indexFormat = outVerts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        var p = new Vector3[outVerts.Count]; var n = new Vector3[outVerts.Count]; var u = new Vector2[outVerts.Count]; var u2 = new Vector2[outVerts.Count];
        for (int i = 0; i < outVerts.Count; i++)
        {
            p[i] = toMesh.MultiplyPoint3x4(outVerts[i].p);
            n[i] = toMesh.MultiplyVector(outVerts[i].n).normalized;
            u[i] = outVerts[i].uv;
            u2[i] = outVerts[i].uv2;
        }
        body.vertices = p; body.normals = n; body.uv = u;
        if (uv2s.Length > 0) body.uv2 = u2;
        body.subMeshCount = outTris.Count;
        for (int s = 0; s < outTris.Count; s++) body.SetTriangles(outTris[s], s);
        body.RecalculateBounds();
        body.RecalculateTangents();
        string path = MeshFolder + "/" + Safe(mf.name) + " Body.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(body, path);
        mf.sharedMesh = body;
        return clipped;
    }

    // Keeps the part of a convex polygon on one side of an axis line (axis 0 = x, 1 = y; keepAbove = keep >= value).
    static List<V> Clip(List<V> poly, int axis, float value, bool keepAbove)
    {
        var result = new List<V>();
        if (poly.Count == 0) return result;
        for (int i = 0; i < poly.Count; i++)
        {
            V cur = poly[i], next = poly[(i + 1) % poly.Count];
            float dc = (axis == 0 ? cur.p.x : cur.p.y) - value, dn = (axis == 0 ? next.p.x : next.p.y) - value;
            if (!keepAbove) { dc = -dc; dn = -dn; }
            if (dc >= 0f) result.Add(cur);
            if ((dc >= 0f) != (dn >= 0f)) result.Add(V.Lerp(cur, next, dc / (dc - dn)));
        }
        return result;
    }

    static void MakeLeaf(string name, Leaf leaf, Transform hinge, Transform root, int layer)
    {
        var go = new GameObject(name);
        go.layer = layer;
        go.transform.SetParent(hinge, false);
        var toHinge = hinge.worldToLocalMatrix * root.localToWorldMatrix;
        var mesh = new Mesh { name = name };
        var p = new List<Vector3>(); var n = new List<Vector3>(); var u = new List<Vector2>(); var u2 = new List<Vector2>();
        // Outer face, then the same again turned round for the inside of the door.
        for (int pass = 0; pass < 2; pass++)
            foreach (var v in leaf.verts)
            {
                p.Add(toHinge.MultiplyPoint3x4(v.p));
                Vector3 normal = toHinge.MultiplyVector(v.n).normalized;
                n.Add(pass == 0 ? normal : -normal);
                u.Add(v.uv);
                u2.Add(v.uv2);
            }
        mesh.SetVertices(p); mesh.SetNormals(n); mesh.SetUVs(0, u); mesh.SetUVs(1, u2);
        var materials = new List<Material>();
        mesh.subMeshCount = leaf.tris.Count;
        int s = 0;
        int offset = leaf.verts.Count;
        foreach (var pair in leaf.tris)
        {
            var tris = new List<int>(pair.Value);
            for (int i = 0; i < pair.Value.Count; i += 3) { tris.Add(pair.Value[i] + offset); tris.Add(pair.Value[i + 2] + offset); tris.Add(pair.Value[i + 1] + offset); }
            mesh.SetTriangles(tris, s++);
            materials.Add(pair.Key);
        }
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        string path = MeshFolder + "/" + name + ".asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
    }

    // A dark box behind the doors, seen from inside (faces point inwards).
    static void MakeCargoBox(Transform parent, Transform root, float rearZ, int layer)
    {
        float x = 0.97f, y0 = FloorY - 0.02f, y1 = 2.05f, z0 = rearZ + 0.03f, z1 = rearZ + 2.2f;
        var c = new[]
        {
            new Vector3(-x, y0, z0), new Vector3(x, y0, z0), new Vector3(x, y1, z0), new Vector3(-x, y1, z0),
            new Vector3(-x, y0, z1), new Vector3(x, y0, z1), new Vector3(x, y1, z1), new Vector3(-x, y1, z1),
        };
        // Floor, ceiling, left, right, front wall; each quad wound to face into the box.
        int[][] quads = { new[] { 0, 4, 5, 1 }, new[] { 3, 2, 6, 7 }, new[] { 0, 3, 7, 4 }, new[] { 1, 5, 6, 2 }, new[] { 4, 7, 6, 5 } };
        var p = new List<Vector3>(); var tris = new List<int>();
        foreach (var q in quads)
        {
            int start = p.Count;
            foreach (int i in q) p.Add(c[i]);
            tris.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
        var mesh = new Mesh { name = "Cargo Box" };
        mesh.SetVertices(p);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        string path = MeshFolder + "/Cargo Box.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);

        string matPath = MeshFolder + "/Cargo Interior.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.22f, 0.24f, 0.26f));
            mat.SetFloat("_Smoothness", 0.3f);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        var go = new GameObject("Cargo Box");
        go.layer = layer;
        go.transform.SetParent(parent, false);
        go.transform.position = root.position;
        go.transform.rotation = root.rotation;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    static string Safe(string name)
    {
        foreach (char ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
        return name;
    }
}
