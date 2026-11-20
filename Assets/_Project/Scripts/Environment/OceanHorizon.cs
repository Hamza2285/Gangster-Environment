using UnityEngine;
using UnityEngine.Rendering;

namespace OpenworldCrime.Environment
{
    /// <summary>
    /// Extends the city's square ocean plane to the horizon: a flat ring mesh (built at runtime, no mesh asset) that starts
    /// exactly at the edge of the existing water plane and runs out several kilometres, using the same water material and
    /// the same UV mapping continued past the plane, so the seam does not show. Distance fog then fades it into the sky.
    /// The existing water plane is found by name / water shader when <see cref="sourceWater"/> is empty.
    /// One per scene (a second one, e.g. the trailer's runtime copy when the city already has its ocean, stands down); costs one
    /// draw call. Nothing in the scene or the material asset is changed.
    /// With <see cref="oceanMaterial"/> set (the game's ocean: OpenworldCrime/OceanSurface) the whole continuous surface - the
    /// city's water plane and the open sea - uses that one material, so the two can never shade differently at the junction.
    /// </summary>
    [DisallowMultipleComponent]
    public class OceanHorizon : MonoBehaviour
    {
        [Tooltip("The existing ocean plane (MeshRenderer). Empty = the scene object named 'Water' with a water shader.")]
        [SerializeField] private MeshRenderer sourceWater;
        [Tooltip("Half size (m) of the ring's outer square, measured from the water plane's centre.")]
        [SerializeField, Min(500f)] private float outerHalfSize = 12000f;
        [Tooltip("Each ring of vertices is this much further out than the last (smaller = denser, smoother shading).")]
        [SerializeField, Range(1.01f, 1.5f)] private float ringGrowth = 1.035f;
        [Tooltip("Vertices along each side of a ring.")]
        [SerializeField, Range(8, 512)] private int segmentsPerSide = 160;
        private int rings;
        [Tooltip("Height offset from the water plane (a hair lower so the two never overlap).")]
        [SerializeField] private float heightOffset = -0.01f;
        [Tooltip("Unlit sea floor this far under the whole ocean (0 = none). The water is semi-transparent: without a floor " +
                 "the open sea shows the sky's dark lower half through it, which reads as a hard edge.")]
        [SerializeField, Min(0f)] private float seabedDepth = 0f;
        [Tooltip("Build ONE continuous ocean (the original plane's own mesh + the ring) in a single renderer and hide the " +
                 "original, so both parts share renderer state, normals and tangents and shade identically.")]
        [SerializeField] private bool replaceSource = true;
        [Tooltip("Reflection probes near the shore only cover part of the sea, which shows as a dark rectangle in the " +
                 "water. Off = the whole ocean reflects the scene's environment reflection the same way everywhere.")]
        [SerializeField] private bool useReflectionProbes = false;
        [Tooltip("Water opacity used on the ocean (runtime copy of the material; the asset is untouched). The water shader " +
                 "refracts whatever is under it, and past the camera's far plane there is nothing there, which shows as a " +
                 "bright band near the horizon. 1 = fully opaque water colour (seamless out to the horizon); < 0 = keep the " +
                 "material's own value.")]
        [SerializeField] private float oceanAlpha = -1f;
        [Tooltip("The open sea outside the city's plane uses a plain reflective Lit water material (no depth / refraction effects, which break down towards the far plane and show as bands and boxes). The city's own water keeps its shader.")]
        [SerializeField] private bool openSeaLitMaterial = true;
        [SerializeField] private Color openSeaColour = new Color(0.05f, 0.12f, 0.16f, 1f);
        [SerializeField, Range(0f, 1f)] private float openSeaSmoothness = 0.93f;
        [SerializeField] private Color seabedColour = new Color(0.09f, 0.16f, 0.19f, 1f);
        [Tooltip("One material for the whole ocean (city water + open sea, OpenworldCrime/OceanSurface). Overrides the options above.")]
        [SerializeField] private Material oceanMaterial;

        private static OceanHorizon active;

        public MeshRenderer Ring { get; private set; }

        private Mesh mesh;
        private bool hidSource;
        private Material seabedMaterial;
        private Material[] oceanMaterials;
        private Material openSeaMaterial;
        private Mesh seabedMesh;

        private void Awake()
        {
            if (active != null && active != this)
            {
                enabled = false; // the scene already has its ocean
                return;
            }
            active = this;
            if (oceanMaterial != null) openSeaLitMaterial = false;
            if (sourceWater == null) sourceWater = FindWater();
            if (sourceWater == null)
            {
                Debug.LogWarning($"[{nameof(OceanHorizon)}] No ocean plane found; the horizon ocean is not built.", this);
                enabled = false;
                return;
            }
            Build();
        }

        private void OnDestroy()
        {
            if (active == this) active = null;
            if (hidSource && sourceWater != null) sourceWater.enabled = true;
            if (mesh != null) Destroy(mesh);
            if (seabedMaterial != null) Destroy(seabedMaterial);
            if (oceanMaterials != null) foreach (var m in oceanMaterials) if (m != null) Destroy(m);
            if (openSeaMaterial != null) Destroy(openSeaMaterial);
            if (seabedMesh != null) Destroy(seabedMesh);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => active = null;

        private static MeshRenderer FindWater()
        {
            MeshRenderer best = null;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r.sharedMaterial == null || r.sharedMaterial.shader == null) continue;
                bool water = r.name == "Water" || r.sharedMaterial.shader.name.Contains("Water");
                if (!water) continue;
                if (best == null || r.bounds.size.x > best.bounds.size.x) best = r;
            }
            return best;
        }

        private void Build()
        {
            var filter = sourceWater.GetComponent<MeshFilter>();
            Mesh src = filter != null ? filter.sharedMesh : null;
            Bounds wb = sourceWater.bounds;
            float y = wb.center.y + heightOffset;

            // uv as a linear function of the source plane's local x / z (least squares over its vertices), so the ring
            // continues the plane's tiling exactly, rotation and scale included.
            Transform st = sourceWater.transform;
            double[] fu = { 1.0 / Mathf.Max(1f, wb.size.x), 0, 0 }, fv = { 0, 1.0 / Mathf.Max(1f, wb.size.z), 0 };
            if (src != null && src.isReadable && src.uv != null && src.uv.Length == src.vertexCount && src.vertexCount >= 3)
                FitUv(src.vertices, src.uv, out fu, out fv);

            // The ring is built in the plane's own space (it is slightly rotated), starting on its outer edge.
            Bounds lb = src != null ? src.bounds : new Bounds(Vector3.zero, new Vector3(10f, 0f, 10f));
            float scale = Mathf.Max(1e-4f, Mathf.Abs(st.lossyScale.x));
            Vector2 c = new Vector2(lb.center.x, lb.center.z);
            Vector2 inner = new Vector2(lb.extents.x, lb.extents.z);
            float outerLocal = outerHalfSize / scale;
            // Dense, geometric tessellation: each ring is ringGrowth further out than the last and every side has many
            // segments. The water shader works partly per vertex (view direction / fresnel), so big triangles show their
            // edges as straight bands and boxes on the water; small ones keep it seamless with the original plane.
            var ks = new System.Collections.Generic.List<float> { 1f };
            float kMax = outerLocal / Mathf.Max(inner.x, inner.y);
            while (ks[ks.Count - 1] < kMax) ks.Add(Mathf.Min(kMax, ks[ks.Count - 1] * ringGrowth));
            rings = ks.Count - 1;
            int sides = 4, perSide = segmentsPerSide; // vertices along each side of a ring
            int ringVerts = sides * perSide;
            var verts = new Vector3[ringVerts * (rings + 1)];
            var uvs = new Vector2[verts.Length];
            var normals = new Vector3[verts.Length];
            for (int r = 0; r <= rings; r++)
            {
                // Rings spaced quadratically: dense near the shore, sparse far out.
                Vector2 half = inner * ks[r];
                for (int i = 0; i < ringVerts; i++)
                {
                    Vector2 q = SquarePoint(i / (float)ringVerts, half) + c;
                    Vector3 world = st.TransformPoint(new Vector3(q.x, lb.center.y, q.y));
                    world.y = y;
                    int idx = r * ringVerts + i;
                    verts[idx] = transform.InverseTransformPoint(world);
                    uvs[idx] = new Vector2((float)(fu[0] * q.x + fu[1] * q.y + fu[2]), (float)(fv[0] * q.x + fv[1] * q.y + fv[2]));
                    normals[idx] = transform.InverseTransformDirection(Vector3.up);
                }
            }
            var tris = new int[rings * ringVerts * 6];
            int ti = 0;
            for (int r = 0; r < rings; r++)
                for (int i = 0; i < ringVerts; i++)
                {
                    int a0 = r * ringVerts + i, a1 = r * ringVerts + (i + 1) % ringVerts;
                    int b0 = a0 + ringVerts, b1 = a1 + ringVerts;
                    tris[ti++] = a0; tris[ti++] = b0; tris[ti++] = a1;
                    tris[ti++] = a1; tris[ti++] = b0; tris[ti++] = b1;
                }
            mesh = new Mesh { name = "Ocean Horizon Ring", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = tris;
            // Upward-facing ring: make sure it winds towards +y.
            if (Vector3.Dot(Vector3.Cross(verts[tris[1]] - verts[tris[0]], verts[tris[2]] - verts[tris[0]]), Vector3.up) < 0f)
            {
                for (int i = 0; i < tris.Length; i += 3) { int tmp = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = tmp; }
                mesh.triangles = tris;
            }
            // Tangents as the original plane's (its normal map must bend the same way on both parts).
            Vector4 srcTangent = new Vector4(1f, 0f, 0f, 1f);
            if (src != null && src.tangents != null && src.tangents.Length > 0)
            {
                Vector4 t0 = src.tangents[0];
                Vector3 tw = st.TransformDirection(new Vector3(t0.x, t0.y, t0.z)).normalized;
                Vector3 tl = transform.InverseTransformDirection(tw);
                srcTangent = new Vector4(tl.x, tl.y, tl.z, t0.w * Mathf.Sign(st.lossyScale.x * st.lossyScale.y * st.lossyScale.z));
            }
            var tangents = new Vector4[verts.Length];
            for (int i = 0; i < tangents.Length; i++) tangents[i] = srcTangent;
            mesh.tangents = tangents;

            if (replaceSource && src != null && src.isReadable)
            {
                // Append the original plane's own triangles (transformed into this space) -> one continuous surface.
                Vector3[] sv = src.vertices; Vector3[] sn = src.normals; Vector4[] stg = src.tangents; Vector2[] suv = src.uv;
                int baseIndex = verts.Length;
                var allV = new Vector3[verts.Length + sv.Length];
                var allN = new Vector3[allV.Length];
                var allT = new Vector4[allV.Length];
                var allUv = new Vector2[allV.Length];
                System.Array.Copy(verts, allV, verts.Length);
                System.Array.Copy(normals, allN, normals.Length);
                System.Array.Copy(tangents, allT, tangents.Length);
                System.Array.Copy(uvs, allUv, uvs.Length);
                float flip = Mathf.Sign(st.lossyScale.x * st.lossyScale.y * st.lossyScale.z);
                for (int i = 0; i < sv.Length; i++)
                {
                    Vector3 w = st.TransformPoint(sv[i]);
                    allV[baseIndex + i] = transform.InverseTransformPoint(new Vector3(w.x, y, w.z));
                    allN[baseIndex + i] = transform.InverseTransformDirection(Vector3.up); // flat sea: one normal everywhere, no seam in shading
                    allT[baseIndex + i] = srcTangent; // same tangent frame as the ring
                    allUv[baseIndex + i] = suv != null && suv.Length == sv.Length ? suv[i] : Vector2.zero;
                }
                int[] st0 = src.triangles;
                int[] ringTris = mesh.triangles;
                var allTris = new int[ringTris.Length + st0.Length];
                System.Array.Copy(ringTris, allTris, ringTris.Length);
                for (int i = 0; i < st0.Length; i++) allTris[ringTris.Length + i] = st0[i] + baseIndex;
                mesh.Clear();
                mesh.vertices = allV;
                mesh.normals = allN;
                mesh.tangents = allT;
                mesh.uv = allUv;
                if (openSeaLitMaterial)
                {
                    // Sub-mesh 0 = open sea (ring), sub-mesh 1 = the city's water plane.
                    var srcTris = new int[st0.Length];
                    for (int i = 0; i < st0.Length; i++) srcTris[i] = st0[i] + baseIndex;
                    mesh.subMeshCount = 2;
                    mesh.SetTriangles(ringTris, 0);
                    mesh.SetTriangles(srcTris, 1);
                }
                else mesh.triangles = allTris;
                mesh.name = "Ocean Horizon (continuous)";
            }
            mesh.bounds = new Bounds(transform.InverseTransformPoint(new Vector3(wb.center.x, y, wb.center.z)), new Vector3(outerHalfSize * 2.2f, 1f, outerHalfSize * 2.2f));

            var ringGo = new GameObject("Ocean Horizon Ring");
            ringGo.transform.SetParent(transform, false);
            ringGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            Ring = ringGo.AddComponent<MeshRenderer>();
            Ring.sharedMaterials = sourceWater.sharedMaterials;
            if (oceanAlpha >= 0f)
            {
                oceanMaterials = new Material[sourceWater.sharedMaterials.Length];
                for (int i = 0; i < oceanMaterials.Length; i++)
                {
                    var src0 = sourceWater.sharedMaterials[i];
                    if (src0 == null) continue;
                    oceanMaterials[i] = new Material(src0) { name = src0.name + " (Ocean Horizon)" };
                    if (oceanMaterials[i].HasProperty("_ColorAlpha")) oceanMaterials[i].SetFloat("_ColorAlpha", oceanAlpha);
                }
                Ring.sharedMaterials = oceanMaterials;
            }
            if (openSeaLitMaterial)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit != null)
                {
                    openSeaMaterial = new Material(lit) { name = "Ocean Horizon Open Sea (runtime)" };
                    openSeaMaterial.SetColor("_BaseColor", openSeaColour);
                    openSeaMaterial.SetFloat("_Smoothness", openSeaSmoothness);
                    openSeaMaterial.SetFloat("_Metallic", 0f);
                    var waterMat = sourceWater.sharedMaterial;
                    Texture normal = waterMat != null && waterMat.HasProperty("_Normal_Map") ? waterMat.GetTexture("_Normal_Map") : null;
                    if (normal != null)
                    {
                        openSeaMaterial.SetTexture("_BumpMap", normal);
                        openSeaMaterial.EnableKeyword("_NORMALMAP");
                        openSeaMaterial.SetFloat("_BumpScale", 0.2f);
                        openSeaMaterial.SetTextureScale("_BumpMap", new Vector2(320f, 320f));
                    }
                    Material cityWater = Ring.sharedMaterials.Length > 0 ? Ring.sharedMaterials[0] : sourceWater.sharedMaterial;
                    Ring.sharedMaterials = mesh.subMeshCount > 1 ? new[] { openSeaMaterial, cityWater } : new[] { openSeaMaterial };
                }
            }
            if (oceanMaterial != null) Ring.sharedMaterials = new[] { oceanMaterial };
            Ring.shadowCastingMode = ShadowCastingMode.Off;
            Ring.receiveShadows = sourceWater.receiveShadows;
            Ring.lightProbeUsage = LightProbeUsage.Off;
            Ring.reflectionProbeUsage = useReflectionProbes ? sourceWater.reflectionProbeUsage : ReflectionProbeUsage.Off;
            ringGo.layer = sourceWater.gameObject.layer;
            Ring.probeAnchor = sourceWater.probeAnchor != null ? sourceWater.probeAnchor : sourceWater.transform;
            if (replaceSource && src != null && src.isReadable)
            {
                sourceWater.enabled = false; // put back in OnDestroy
                hidSource = true;
            }

            if (seabedDepth > 0f)
            {
                // One flat, unlit sea floor under the whole ocean (land and the city's own sea floor sit above it and
                // hide it). The water is semi-transparent and refracts what is under it; without a floor the open sea
                // shows the sky's dark lower half through the water near the camera and its bright horizon band further
                // out, which reads as a hard edge.
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null)
                {
                    seabedMaterial = new Material(shader) { name = "Ocean Horizon Seabed (runtime)" };
                    seabedMaterial.SetColor("_BaseColor", seabedColour);
                    var bedMesh = new Mesh { name = "Ocean Horizon Seabed" };
                    float h = outerHalfSize * 1.05f, by = y - seabedDepth;
                    Vector3 cw = new Vector3(wb.center.x, by, wb.center.z);
                    bedMesh.vertices = new[]
                    {
                        transform.InverseTransformPoint(cw + new Vector3(-h, 0f, -h)), transform.InverseTransformPoint(cw + new Vector3(-h, 0f, h)),
                        transform.InverseTransformPoint(cw + new Vector3(h, 0f, h)), transform.InverseTransformPoint(cw + new Vector3(h, 0f, -h))
                    };
                    bedMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                    bedMesh.RecalculateNormals();
                    bedMesh.RecalculateBounds();
                    seabedMesh = bedMesh;
                    var bed = new GameObject("Ocean Horizon Seabed");
                    bed.transform.SetParent(transform, false);
                    bed.AddComponent<MeshFilter>().sharedMesh = bedMesh;
                    var r = bed.AddComponent<MeshRenderer>();
                    r.sharedMaterial = seabedMaterial;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
            }
        }

        private static void FitUv(Vector3[] v, Vector2[] uv, out double[] fu, out double[] fv)
        {
            // Normal equations for [x z 1] -> u and -> v.
            double sxx = 0, sxz = 0, sx = 0, szz = 0, sz = 0, n = v.Length, sxu = 0, szu = 0, su = 0, sxv = 0, szv = 0, sv = 0;
            for (int i = 0; i < v.Length; i++)
            {
                double x = v[i].x, z = v[i].z;
                sxx += x * x; sxz += x * z; sx += x; szz += z * z; sz += z;
                sxu += x * uv[i].x; szu += z * uv[i].x; su += uv[i].x;
                sxv += x * uv[i].y; szv += z * uv[i].y; sv += uv[i].y;
            }
            double[,] m = { { sxx, sxz, sx }, { sxz, szz, sz }, { sx, sz, n } };
            fu = Solve3(m, new[] { sxu, szu, su });
            fv = Solve3(m, new[] { sxv, szv, sv });
        }

        private static double[] Solve3(double[,] m, double[] r)
        {
            double det = m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
            if (System.Math.Abs(det) < 1e-12) return new double[] { 0, 0, 0 };
            var x = new double[3];
            for (int c = 0; c < 3; c++)
            {
                var t = (double[,])m.Clone();
                for (int row = 0; row < 3; row++) t[row, c] = r[row];
                x[c] = (t[0, 0] * (t[1, 1] * t[2, 2] - t[1, 2] * t[2, 1]) - t[0, 1] * (t[1, 0] * t[2, 2] - t[1, 2] * t[2, 0]) + t[0, 2] * (t[1, 0] * t[2, 1] - t[1, 1] * t[2, 0])) / det;
            }
            return x;
        }

        // Point at fraction f (0-1) around the square of half extents h, centred on 0.
        private static Vector2 SquarePoint(float f, Vector2 h)
        {
            float s = f * 4f;
            int side = Mathf.Min(3, (int)s);
            float u = s - side;
            switch (side)
            {
                case 0: return new Vector2(Mathf.Lerp(-h.x, h.x, u), -h.y);
                case 1: return new Vector2(h.x, Mathf.Lerp(-h.y, h.y, u));
                case 2: return new Vector2(Mathf.Lerp(h.x, -h.x, u), h.y);
                default: return new Vector2(-h.x, Mathf.Lerp(h.y, -h.y, u));
            }
        }
    }
}
