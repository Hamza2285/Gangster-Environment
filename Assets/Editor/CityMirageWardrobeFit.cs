using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CityMirageWardrobeFit
{
    const string Prefab="Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Asset="Assets/Texture/City Mirage/Fitted Garment and Sneaker Detail.asset";
    const string Request="Temp/CityMirageWardrobeFit.request";
    const string Status="Temp/CityMirageWardrobeFit.txt";
    static bool first;
    [InitializeOnLoadMethod] static void Schedule()
    {if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.delayCall+=Run;}
    [MenuItem("Tools/City Mirage/Fit Wardrobe Clothes and Sneakers")]
    public static void Run()
    {
        try{File.WriteAllText(Status,Apply());File.Delete(Request);}
        catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
    }
    static string Apply()
    {
        var root=PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            var rs=root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
            var shoes=rs.Where(r=>r.name=="Sale table | display sneaker").ToArray();
            var clothes=rs.Where(r=>r.name.StartsWith("T-shirt |")||r.name.StartsWith("Hoodie |")).ToArray();
            var hangers=rs.Where(r=>r.name.StartsWith("Wall bay | bent hanger")||r.name.StartsWith("Hanger | shoulder and hook")).ToArray();
            if(shoes.Length!=8||clothes.Length<60||hangers.Length<60)throw new InvalidOperationException("Merchandise layout not found.");
            if(shoes.All(r=>AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh)==Asset))return "DONE (already fitted)";
            var matched=new List<(MeshRenderer cloth,MeshRenderer hanger)>();
            var unused=hangers.ToList();
            foreach(var cloth in clothes)
            {
                bool wall=cloth.name.Contains("| wall");
                var hanger=unused.Where(h=>h.name.StartsWith("Wall bay")==wall)
                    .OrderBy(h=>Distance(cloth.bounds,h.bounds)).FirstOrDefault();
                if(!hanger||Distance(cloth.bounds,hanger.bounds)>.13f)throw new InvalidOperationException("No matching hanger for "+cloth.name);
                unused.Remove(hanger);
                matched.Add((cloth,hanger));
            }
            first=!AssetDatabase.LoadMainAssetAtPath(Asset);
            var variants=new Dictionary<(Mesh,Quaternion,bool),Mesh>();
            foreach(var r in shoes)
            {
                var source=r.GetComponent<MeshFilter>().sharedMesh;
                bool dark=source.name.Contains("charcoal");
                var key=(source,r.transform.rotation,dark);
                if(!variants.TryGetValue(key,out var mesh))
                {
                    mesh=Shoe(r,dark);Save(mesh);variants.Add(key,mesh);
                }
                Bind(r,mesh);
            }
            var drapes=new Dictionary<Mesh,Mesh>();
            foreach(var (cloth,hanger) in matched)
            {
                var f=cloth.GetComponent<MeshFilter>();
                if(!drapes.TryGetValue(f.sharedMesh,out var mesh))
                {
                    mesh=UnityEngine.Object.Instantiate(f.sharedMesh);
                    mesh.name=f.sharedMesh.name+" | fitted shoulders and drape";
                    ShapeCloth(cloth,mesh);
                    Save(mesh);drapes.Add(f.sharedMesh,mesh);
                }
                Bind(cloth,mesh);
                var c=cloth.bounds;var h=hanger.bounds;
                // The neck sits at the hanger's shoulder junction while the hook stays on the rail.
                float shoulder=h.min.y+h.size.y*.52f;
                float lift=Mathf.Clamp(shoulder-c.max.y+.014f,-.045f,.105f);
                var delta=new Vector3(h.center.x-c.center.x,lift,h.center.z-c.center.z);
                delta.x=Mathf.Clamp(delta.x,-.065f,.065f);
                delta.z=Mathf.Clamp(delta.z,-.065f,.065f);
                cloth.transform.position+=delta;
                if(PrefabUtility.IsPartOfPrefabInstance(cloth.transform))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(cloth.transform);
            }
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root,Prefab,out var saved);
            if(!saved)throw new IOException("Could not save wardrobe and footwear.");
            return $"DONE: {matched.Count} garments fitted to their own hangers, {drapes.Count} shared cloth shapes refined, {shoes.Length} sneakers with supported tongue and laces. No materials or lights added.";
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    static float Distance(Bounds cloth,Bounds hanger)
    {
        var p=cloth.center-hanger.center;
        return p.x*p.x+p.z*p.z+Mathf.Pow(cloth.max.y-(hanger.min.y+hanger.size.y*.52f),2)*.30f;
    }
    static Vector3 Axis(int i){var v=Vector3.zero;v[i]=1;return v;}
    static void ShapeCloth(MeshRenderer r,Mesh m)
    {
        var b=m.bounds;
        var axes=Enumerable.Range(0,3).OrderByDescending(i=>Mathf.Abs(r.transform.TransformDirection(Axis(i)).y)).ToArray();
        int up=axes[0],wide=b.size[axes[1]]>b.size[axes[2]]?axes[1]:axes[2],deep=axes.Single(i=>i!=up&&i!=wide);
        float sign=Mathf.Sign(r.transform.TransformDirection(Axis(up)).y);
        bool tee=r.name.StartsWith("T-shirt");
        var verts=m.vertices;
        for(int i=0;i<verts.Length;i++)
        {
            var p=verts[i];
            float x=(p[wide]-b.center[wide])/b.extents[wide];
            float y=(p[up]-b.center[up])/b.extents[up]*sign;
            float z=(p[deep]-b.center[deep])/b.extents[deep];
            float sleeve=Mathf.SmoothStep(0,1,(Mathf.Abs(x)-.52f)/.44f);
            if(tee)
            {
                p[up]-=sign*b.size[up]*.088f*sleeve*Mathf.Clamp01((y+.1f)/.55f);
                p[deep]-=z*b.size[deep]*.10f*sleeve;
            }
            float folds=Mathf.Sin(x*15+y*3)*.026f+Mathf.Sin(x*26-y*2)*.014f;
            p[deep]+=Mathf.Sign(z)*Mathf.Clamp01(Mathf.Abs(z)*2)*b.size[deep]*folds*(1-sleeve);
            verts[i]=p;
        }
        m.vertices=verts;m.RecalculateNormals();m.RecalculateTangents();m.RecalculateBounds();
    }
    static void Save(Mesh m)
    {if(first){AssetDatabase.CreateAsset(m,Asset);first=false;}else AssetDatabase.AddObjectToAsset(m,Asset);}
    static void Bind(MeshRenderer r,Mesh m)
    {
        var f=r.GetComponent<MeshFilter>();f.sharedMesh=m;
        if(PrefabUtility.IsPartOfPrefabInstance(f))PrefabUtility.RecordPrefabInstancePropertyModifications(f);
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
        float Tongue(float x,float z)=>.918f-1.12f*z-Mathf.Abs(x)*.29f;
        const int across=6;
        int tongueStart=points.Count;
        for(int j=0;j<7;j++)
        {
            float z=.104f+j*.044f;
            float width=.153f-j*.005f;
            for(int k=0;k<=across;k++)
            {
                float x=(k/(float)across*2-1)*width;
                Vertex(x,Tongue(x,z),z,0);
                if(j>0&&k>0)
                {
                    int a=tongueStart+(j-1)*(across+1)+k-1;
                    int b=a+across+1;
                    tris.AddRange(new[]{a,b,a+1,a+1,b,b+1});
                }
            }
        }
        for(int j=0;j<5;j++)
        {
            float z=.127f+j*.044f;
            float width=.132f-j*.006f;
            int start=points.Count;
            for(int k=0;k<=across;k++)
            {
                float x=(k/(float)across*2-1)*width;
                float zz=z+x*(j%2==0 ? .066f : -.066f);
                Vertex(x,Tongue(x,zz)+.014f,zz,2);
                Vertex(x,Tongue(x,zz+.013f)+.014f,zz+.013f,2);
                if(k>0)
                {
                    int a=start+2*(k-1);
                    tris.AddRange(new[]{a,a+1,a+2,a+2,a+1,a+3});
                }
            }
        }
        var m=new Mesh{name=dark?"Sneaker | charcoal stitched low-top":"Sneaker | ivory stitched low-top"};
        m.SetVertices(points);m.SetUVs(0,uv);m.SetTriangles(tris,0);
        m.RecalculateNormals();m.RecalculateBounds();return m;
    }
}

