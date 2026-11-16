using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CityMirageCheckoutReturn
{
    const string Shop = "Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Asset = "Assets/Texture/City Mirage/Checkout L Return.asset";
    const string Request = "Temp/CityMirageCheckoutReturn.request";
    const string Status = "Temp/CityMirageCheckoutReturn.txt";

    [InitializeOnLoadMethod] static void Queue()
    {
        if (!Application.isBatchMode && File.Exists(Request)) EditorApplication.delayCall += Run;
    }

    [MenuItem("Tools/City Mirage/Extend Checkout to Wall")]
    public static void Run()
    {
        try
        {
            File.WriteAllText(Status,Apply());
            if (File.Exists(Request)) File.Delete(Request);
        }
        catch(Exception e) { File.WriteAllText(Status,e.ToString()); Debug.LogException(e); }
    }

    static string Apply()
    {
        var root = PrefabUtility.LoadPrefabContents(Shop);
        try
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            var counter = renderers.Single(r => r.name == "Checkout | welded counter");
            if (counter.transform.Find("Checkout | wall return")) return "DONE: wall return already present.";
            var door = renderers.Single(r => r.name == "Stockroom | door leaf with vision cutout");
            var b = counter.bounds;
            var d = door.bounds;
            bool alongZ = b.size.z > b.size.x;
            var lengthAxis = alongZ ? Vector3.forward : Vector3.right;
            var depthAxis = alongZ ? Vector3.right : Vector3.forward;
            float end = Mathf.Sign(Vector3.Dot(d.center-b.center,lengthAxis));
            float towardWall = Mathf.Sign(Vector3.Dot(d.center-b.center,depthAxis));
            float length = Vector3.Dot(b.size,lengthAxis);
            float depth = Vector3.Dot(b.size,depthAxis);
            float wallGap = Mathf.Abs(Vector3.Dot(d.center-b.center,depthAxis)) - depth*.5f;
            if (length < depth*1.5f || wallGap < .2f || wallGap > 5f)
                throw new InvalidOperationException($"Unexpected counter layout: length {length:F2}, depth {depth:F2}, wall gap {wallGap:F2}.");
            const float seam = .004f;
            float returnLength = wallGap + seam;
            var origin = b.center + lengthAxis*(end*(length-depth)*.5f) +
                depthAxis*(towardWall*(depth*.5f + (wallGap-seam)*.5f));
            if (Mathf.Abs(Vector3.Dot(origin-d.center,lengthAxis)) < (depth+Vector3.Dot(d.size,lengthAxis))*.5f+.05f)
                throw new InvalidOperationException("The return would overlap the stockroom doorway.");
            var existing = counter.GetComponent<MeshFilter>().sharedMesh;
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Asset);
            if (!mesh)
            {
                mesh = UnityEngine.Object.Instantiate(existing);
                mesh.name = "Checkout | wall return";
                float rotation = alongZ ? 90f : -90f;
                var turn = Quaternion.AngleAxis(rotation,Vector3.up);
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                for (int i=0;i<vertices.Length;i++)
                {
                    var point = counter.transform.TransformPoint(vertices[i])-b.center;
                    point += lengthAxis*Vector3.Dot(point,lengthAxis)*(returnLength/length-1f);
                    vertices[i] = counter.transform.InverseTransformPoint(origin+turn*point);
                    if (normals.Length == vertices.Length)
                    {
                        var normal = counter.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(normals[i]);
                        normal += lengthAxis*Vector3.Dot(normal,lengthAxis)*(length/returnLength-1f);
                        normals[i] = counter.transform.localToWorldMatrix.transpose.MultiplyVector(turn*normal).normalized;
                    }
                }
                mesh.vertices = vertices;
                if (normals.Length == vertices.Length) mesh.normals = normals;
                else mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh,Asset);
            }
            var part = new GameObject("Checkout | wall return");
            part.layer = counter.gameObject.layer;
            part.transform.SetParent(counter.transform,false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = counter.sharedMaterials;
            renderer.shadowCastingMode = counter.shadowCastingMode;
            renderer.receiveShadows = counter.receiveShadows;
            GameObjectUtility.SetStaticEditorFlags(part,GameObjectUtility.GetStaticEditorFlags(counter.gameObject));
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root,Shop,out bool saved);
            if (!saved) throw new IOException("Could not save the checkout return.");
            return $"DONE: {returnLength:F2} m checkout return at the stockroom end, with the existing atlas materials.";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
