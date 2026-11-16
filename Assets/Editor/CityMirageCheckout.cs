using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
public static class CityMirageCheckout
{
    const string Folder="Assets/Texture/City Mirage/";
    const string ScenePath="Assets/Scenes/Cloth Shop.unity";
    const string Request="Temp/CityMirageCheckout.request";
    const string Status="Temp/CityMirageCheckout.txt";
    [InitializeOnLoadMethod] static void Schedule()
    {
        if(!Application.isBatchMode && File.Exists(Request)) EditorApplication.delayCall+=Apply;
    }
    [MenuItem("Tools/City Mirage/Apply Checkout Detail")]
    public static void Apply()
    {
        try
        {
            Finish();
            File.WriteAllText(Status,"DONE");
            File.Delete(Request);
        }
        catch(Exception e) { File.WriteAllText(Status,e.ToString()); Debug.LogException(e); }
    }
    static void Finish()
    {
        var scene=SceneManager.GetSceneByPath(ScenePath);
        if(scene.IsValid() && scene.isDirty) throw new InvalidOperationException("Save pending Cloth Shop changes first.");
        if(!scene.IsValid() || !scene.isLoaded) scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        if(scene.GetRootGameObjects().Any(o=>o.name=="Checkout | apparel detail")) return;
        var renderers=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
        var counter=renderers.Single(r=>r.name.StartsWith("Checkout | welded counter"));
        var b=counter.bounds;
        var folds=renderers.Where(r=>r.name.StartsWith("Folded garment | table")).ToArray();
        if(folds.Length==0) throw new InvalidOperationException("Folded garments not found.");
        var pot=renderers.First(r=>r.name.StartsWith("Planter"));
        var art=renderers.Where(r=>r.name.StartsWith("Checkout display housing") || r.name.StartsWith("Side wall screen housing") || r.name.StartsWith("Side wall sale sign housing")).ToArray();
        if(art.Length!=3) throw new InvalidOperationException($"Expected three checkout art panels, found {art.Length}.");
        var previous=SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(scene);
        var root=new GameObject("Checkout | apparel detail");
        for(int s=0;s<3;s++)
        {
            int desired=new[]{6,7,3}[s];
            var source=folds.OrderBy(r=>Mathf.Abs((int)(r.GetComponent<MeshFilter>().sharedMesh.uv[0].x*4)+4*(int)(r.GetComponent<MeshFilter>().sharedMesh.uv[0].y*4)-desired)).First();
            float z=s==0?b.min.z+.33f:b.max.z-.80f+(s-1)*.47f;
            for(int i=0;i<3;i++)
            {
                var obj=CopyMesh(source,$"Folded apparel | stack {s+1} layer {i+1}",root.transform);
                var local=source.bounds;
                obj.transform.position=new Vector3(b.max.x-.29f,b.max.y+.004f+i*local.size.y,z);
                obj.transform.Rotate(0,(s-1)*4+i*1.2f,0,Space.World);
            }
        }
        var planter=CopyMesh(pot,"Planter | counter ceramic pot",root.transform);
        planter.transform.position=new Vector3(b.center.x+.10f,b.max.y,b.min.z+.93f);
        planter.transform.localScale*=.62f;
        var plant=Plant();
        var leafObj=new GameObject("Planter | low-poly foliage");
        leafObj.transform.SetParent(root.transform,false);
        leafObj.transform.position=planter.transform.position+Vector3.up*(pot.bounds.size.y*.62f-.033f);
        leafObj.AddComponent<MeshFilter>().sharedMesh=plant;
        var leaf=leafObj.AddComponent<MeshRenderer>();
        leaf.sharedMaterial=LeafMaterial();
        leaf.shadowCastingMode=ShadowCastingMode.Off;

        var original=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/City Mirage/"+"Branded display panels.mat");
        var artwork=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/City Mirage/"+"Checkout Apparel Displays.mat");
        if(artwork==null)
        {
            artwork=new Material(original);
            AssetDatabase.CreateAsset(artwork,"Assets/Materials/City Mirage/"+"Checkout Apparel Displays.mat");
        }
        artwork.SetColor("_EmissionColor",Color.white*1.65f);
        artwork.EnableKeyword("_EMISSION");
        EditorUtility.SetDirty(artwork);
        foreach(var r in art) r.sharedMaterial=artwork;
        var accent=scene.GetRootGameObjects().FirstOrDefault(o=>o.name=="City Mirage | Lighting");
        if(accent)
        {
            var pool=new GameObject("Checkout | magenta display spill");
            pool.transform.SetParent(root.transform,false);
            pool.transform.position=new Vector3(b.max.x+.22f,.42f,b.center.z+.69f);
            pool.transform.LookAt(new Vector3(b.max.x+1.3f,0,b.center.z+.69f));
            var l=pool.AddComponent<Light>();
            l.type=LightType.Spot;
            l.color=new Color(.69f,.16f,.92f);
            l.range=2.9f;
            l.intensity=2.5f;
            l.spotAngle=101;
            l.innerSpotAngle=56;
            l.shadows=LightShadows.None;
        }
        AssetDatabase.SaveAssets();
        if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save checkout scene.");
        if(previous.IsValid() && previous.isLoaded && previous!=scene) SceneManager.SetActiveScene(previous);
    }
    static GameObject CopyMesh(MeshRenderer source,string name,Transform parent)
    {
        var o=new GameObject(name);
        o.transform.SetParent(parent,false);
        o.transform.rotation=source.transform.rotation;
        o.transform.localScale=source.transform.lossyScale;
        o.AddComponent<MeshFilter>().sharedMesh=source.GetComponent<MeshFilter>().sharedMesh;
        o.AddComponent<MeshRenderer>().sharedMaterials=source.sharedMaterials;
        GameObjectUtility.SetStaticEditorFlags(o,StaticEditorFlags.BatchingStatic);
        return o;
    }
    static Material LeafMaterial()
    {
        string path="Assets/Materials/City Mirage/"+"Checkout Planter Leaves.mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m!=null)return m;
        m=new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor",new Color(.23f,.40f,.046f));
        m.SetFloat("_Smoothness",.40f);
        m.SetFloat("_Cull",0);
        m.doubleSidedGI=true;
        AssetDatabase.CreateAsset(m,path);
        return m;
    }
    static Mesh Plant()
    {
        string path=Folder+"Checkout Planter Foliage.asset";
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing!=null)return existing;
        var v=new List<Vector3>(); var t=new List<int>();
        for(int i=0;i<14;i++)
        {
            float angle=i*2.399963f;
            var d=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
            var right=Vector3.Cross(Vector3.up,d);
            float rise=.15f+(i%4)*.056f, reach=.18f+(i%3)*.043f;
            int start=v.Count;
            for(int j=0;j<=6;j++)
            {
                float u=j/6f;
                float width=(.005f+.053f*Mathf.Sin(u*Mathf.PI))*Mathf.Pow(1-u,.37f);
                var middle=d*(.017f+reach*u)+Vector3.up*(.012f+rise*Mathf.Sin(u*1.94f));
                v.Add(middle-right*width);
                v.Add(middle+Vector3.up*(width*.25f));
                v.Add(middle+right*width);
                if(j==6)break;
                for(int k=0;k<2;k++)
                {
                    int a=start+j*3+k;
                    t.AddRange(new[]{a,a+3,a+1,a+1,a+3,a+4});
                }
            }
        }
        var m=new Mesh{name="Checkout planter foliage"};
        m.SetVertices(v);
        m.SetTriangles(t,0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        AssetDatabase.CreateAsset(m,path);
        return m;
    }
}
