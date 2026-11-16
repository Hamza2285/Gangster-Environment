using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CityMirageMerchDetail
{
    const string Prefab="Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Asset="Assets/Texture/City Mirage/Merchandise Detail Meshes.asset";
    const string Request="Temp/CityMirageMerchDetail.request";
    const string Status="Temp/CityMirageMerchDetail.txt";
    [Serializable] class Shape { public Vector3[] vertices; public int[] triangles; }
    static bool first;
    [InitializeOnLoadMethod] static void Schedule()
    {if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.delayCall+=Run;}
    [MenuItem("Tools/City Mirage/Refine Shoes Clothes and Hangers")]
    public static void Run()
    {
        try{File.WriteAllText(Status,Apply());File.Delete(Request);}
        catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
    }
    static string Apply()
    {
        var cloth=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/City Mirage/Clothes Atlas.mat");
        if(!cloth||!cloth.GetTexture("_BaseMap"))throw new InvalidOperationException("Clothes atlas missing.");
        var root=PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            var rs=root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
            var shoes=rs.Where(r=>r.name=="Sale table | display sneaker").ToArray();
            var garments=rs.Where(r=>r.name.StartsWith("T-shirt |")||r.name.StartsWith("Hoodie |")||
                r.name.StartsWith("Folded garment |")||r.name.StartsWith("Folded apparel | stack")).ToArray();
            var primary=rs.Where(r=>r.name.StartsWith("Hanger | shoulder and hook")||r.name.StartsWith("Wall bay | bent hanger")).ToArray();
            var secondary=rs.Where(r=>r.name.StartsWith("Hanger | right shoulder")||r.name.StartsWith("Wall bay | hanger hook")).ToList();
            if(shoes.Length<4||garments.Length<100||primary.Length<40||secondary.Count<40)
                throw new InvalidOperationException($"Unexpected merchandise counts: {shoes.Length} shoes, {garments.Length} garments, {primary.Length}/{secondary.Count} hanger parts.");
            if(shoes.All(r=>AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh)==Asset))return "DONE (already applied)";
            var shape=JsonUtility.FromJson<Shape>(File.ReadAllText("Assets/Editor/CityMirageHangerShape.json"));
            if(shape?.vertices==null||shape.triangles.Length!=702)throw new InvalidOperationException("Invalid hanger mesh.");
            var pairs=new List<(MeshRenderer,MeshRenderer,Bounds)>();
            foreach(var a in primary)
            {
                bool wall=a.name.StartsWith("Wall bay");
                var b=secondary.Where(r=>r.name.StartsWith("Wall bay")==wall)
                    .OrderBy(r=>(r.bounds.center-a.bounds.center).sqrMagnitude).FirstOrDefault();
                if(!b||(b.bounds.center-a.bounds.center).sqrMagnitude>.26f)
                    throw new InvalidOperationException("Could not match hanger halves for "+a.name);
                secondary.Remove(b);
                var bounds=a.bounds;bounds.Encapsulate(b.bounds);
                pairs.Add((a,b,bounds));
            }
            first=!AssetDatabase.LoadMainAssetAtPath(Asset);
            var shoeMeshes=new Dictionary<(Mesh,Quaternion,bool),Mesh>();
            foreach(var r in shoes)
            {
                var f=r.GetComponent<MeshFilter>();var old=f.sharedMesh;
                bool dark=old.uv[0].x>=1.5625f;
                var key=(old,r.transform.rotation,dark);
                if(!shoeMeshes.TryGetValue(key,out var shoe))
                {
                    shoe=Shoe(r,dark);Save(shoe);shoeMeshes.Add(key,shoe);
                }
                Bind(r,shoe,cloth);
            }
            var clothMeshes=new Dictionary<Mesh,Mesh>();
            foreach(var r in garments)
            {
                var old=r.GetComponent<MeshFilter>().sharedMesh;
                if(!clothMeshes.TryGetValue(old,out var mesh))
                {
                    mesh=UnityEngine.Object.Instantiate(old);
                    mesh.name=old.name+" | tailored folds";
                    Drape(r,mesh);
                    Save(mesh);clothMeshes.Add(old,mesh);
                }
                Bind(r,mesh,cloth);
            }
            var hangerMeshes=new Dictionary<(Mesh,Mesh,Quaternion),Mesh>();
            foreach(var pair in pairs)
            {
                var (a,b,bounds)=pair;
                var key=(a.GetComponent<MeshFilter>().sharedMesh,b.GetComponent<MeshFilter>().sharedMesh,a.transform.rotation);
                if(!hangerMeshes.TryGetValue(key,out var mesh))
                {
                    mesh=Hanger(a,bounds,shape);Save(mesh);hangerMeshes.Add(key,mesh);
                }
                Bind(a,mesh,cloth);
                b.enabled=false;
                if(PrefabUtility.IsPartOfPrefabInstance(b))PrefabUtility.RecordPrefabInstancePropertyModifications(b);
            }
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root,Prefab,out var saved);
            if(!saved)throw new IOException("Could not save merchandise detail.");
            return $"DONE: {shoes.Length} sneakers, {garments.Length} garment renderers, {pairs.Count} connected hangers. Shoes and cloth use the existing atlas; hanger halves disabled.";
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }

    static void Save(Mesh m)
    {if(first){AssetDatabase.CreateAsset(m,Asset);first=false;}else AssetDatabase.AddObjectToAsset(m,Asset);}
    static void Bind(MeshRenderer r,Mesh m,Material mat)
    {
        var f=r.GetComponent<MeshFilter>();
        f.sharedMesh=m;r.sharedMaterials=Enumerable.Repeat(mat,m.subMeshCount).ToArray();
        if(PrefabUtility.IsPartOfPrefabInstance(f))
        {PrefabUtility.RecordPrefabInstancePropertyModifications(f);PrefabUtility.RecordPrefabInstancePropertyModifications(r);}
    }
    static Vector3 Axis(int i){var v=Vector3.zero;v[i]=1;return v;}
    static Mesh Hanger(MeshRenderer r,Bounds b,Shape shape)
    {
        var v=shape.vertices;
        var min=v.Aggregate(Vector3.Min);var max=v.Aggregate(Vector3.Max);
        var wide=b.size.x>b.size.z?Vector3.right:Vector3.forward;
        var depth=Vector3.Cross(Vector3.up,wide);
        float w=Mathf.Max(b.size.x,b.size.z);
        float d=Mathf.Min(b.size.x,b.size.z);
        var points=v.Select(p=>r.transform.InverseTransformPoint(b.center+
            wide*((p.x-min.x)/(max.x-min.x)-.5f)*w+
            depth*((p.y-min.y)/(max.y-min.y)-.5f)*d+
            Vector3.up*((p.z-min.z)/(max.z-min.z)-.5f)*b.size.y)).ToArray();
        var m=new Mesh{name="Hanger | connected arms, lower rail and hook"};
        m.vertices=points;m.triangles=shape.triangles;
        m.uv=Enumerable.Repeat(new Vector2(1.8125f,.375f),points.Length).ToArray();
        m.RecalculateNormals();m.RecalculateBounds();return m;
    }
    static void Drape(MeshRenderer r,Mesh mesh)
    {
        bool folded=r.name.StartsWith("Folded");
        var b=mesh.bounds;
        var axes=Enumerable.Range(0,3).OrderByDescending(i=>Mathf.Abs(r.transform.TransformDirection(Axis(i)).y)).ToArray();
        int up=axes[0],width=b.size[axes[1]]>=b.size[axes[2]]?axes[1]:axes[2],depth=axes.Single(i=>i!=up&&i!=width);
        var verts=mesh.vertices;
        for(int i=0;i<verts.Length;i++)
        {
            var p=verts[i];
            float x=(p[width]-b.center[width])/Mathf.Max(.001f,b.extents[width]);
            float y=(p[up]-b.center[up])/Mathf.Max(.001f,b.extents[up]);
            float z=(p[depth]-b.center[depth])/Mathf.Max(.001f,b.extents[depth]);
            if(folded)
            {
                float edge=Mathf.Pow(Mathf.Abs(x*z),.8f);
                p[width]-=x*edge*b.size[width]*.045f;
                p[depth]-=z*edge*b.size[depth]*.045f;
                p[up]+=y*b.size[up]*(.060f*Mathf.Sin(x*4+z*2)-.035f*edge);
            }
            else
            {
                float falloff=Mathf.Clamp01(1-Mathf.Abs(x)*1.12f);
                float drape=Mathf.Sin(x*13+y*2.5f)*.021f+Mathf.Sin(x*20-y*3)*.008f;
                p[depth]+=Mathf.Sign(z)*Mathf.Clamp01(Mathf.Abs(z)*1.6f)*drape*falloff*b.size[depth];
                p[width]+=Mathf.Sin(y*2.6f)*.009f*falloff*b.size[width];
            }
            verts[i]=p;
        }
        mesh.vertices=verts;mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
    }
    static Mesh Shoe(MeshRenderer r,bool dark)
    {
        var bounds=r.bounds;
        var points=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
        // Model normalized sneaker proportions and preserve placement and the old footprint.
        Vector3 World(float x,float y,float z)=>new Vector3(bounds.center.x+x*bounds.size.x,bounds.min.y+y*bounds.size.y,bounds.center.z+z*bounds.size.z);
        Vector2 ColorUV(int surface)
        {
            if(surface==1)return new Vector2(1.518f,.385f); // outsole
            if(surface==2)return new Vector2(1.533f,.407f); // cream laces
            if(surface==3)return new Vector2(1.575f,.465f); // inner lining
            return new Vector2(dark?1.582f:1.515f,.481f);
        }
        void Vertex(float x,float y,float z,int surface)
        {points.Add(r.transform.InverseTransformPoint(World(x,y,z)));uv.Add(ColorUV(surface));}
        const int sides=20;
        // Same footprint for sole, followed by tapered upper and recessed inner collar.
        for(int layer=0;layer<9;layer++)
        for(int j=0;j<sides;j++)
        {
            float a=j*Mathf.PI*2/sides,c=Mathf.Cos(a),s=Mathf.Sin(a);
            float width=(.44f+.052f*s);
            float x,z,y;
            switch(layer)
            {
                case 0:x=c*width*.91f;z=s*.47f;y=.015f;break;
                case 1:x=c*width;z=s*.49f;y=.064f;break;
                case 2:x=c*width;z=s*.49f;y=.22f;break;
                case 3:x=c*width*.94f;z=s*.47f;y=.25f;break;
                case 4:x=c*width*.89f;z=s*.45f;y=.50f-s*.13f;break;
                case 5:x=c*.292f;z=-.15f+s*.245f;y=.87f-s*.09f;break;
                case 6:x=c*.272f;z=-.15f+s*.222f;y=.88f-s*.09f;break;
                case 7:x=c*.215f;z=-.15f+s*.18f;y=.83f-s*.08f;break;
                default:x=c*.184f;z=-.15f+s*.153f;y=.60f;break;
            }
            Vertex(x,y,z,layer<3?1:layer>6?3:0);
            if(layer==0)continue;
            int A=(layer-1)*sides+j,B=(layer-1)*sides+(j+1)%sides;
            tris.AddRange(new[]{A,A+sides,B,A+sides,B+sides,B});
        }
        for(int j=1;j<sides-1;j++){tris.AddRange(new[]{0,j,j+1});int a=8*sides;tris.AddRange(new[]{a,a+j+1,a+j});}
        for(int j=0;j<5;j++)
        {
            float z=-.05f+j*.066f;
            float y=.87f-j*.095f;
            float width=.193f-j*.013f;
            int start=points.Count;
            Vertex(-width,y,z,2);Vertex(-width,y+.012f,z+.034f,2);
            Vertex(width,y+.008f,z+.052f,2);Vertex(width,y,z+.019f,2);
            tris.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
        }
        var m=new Mesh{name=dark?"Sneaker | charcoal stitched low-top":"Sneaker | ivory stitched low-top"};
        m.SetVertices(points);m.SetUVs(0,uv);m.SetTriangles(tris,0);
        m.RecalculateNormals();m.RecalculateBounds();return m;
    }
}
