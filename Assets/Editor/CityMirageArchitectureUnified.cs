using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
public static class CityMirageArchitectureUnified
{
    const string Prefab="Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Materials="Assets/Materials/City Mirage/";
    const string Asset="Assets/Texture/City Mirage/Architecture Shared Detail Meshes.asset";
    const string Request="Temp/CityMirageArchitectureUnified.request";
    const string Status="Temp/CityMirageArchitectureUnified.txt";
    static readonly string[] Names={"Reference - matte black stand","Reference - satin charcoal","Checkout - satin graphite",
        "Cyan counter strip","Counter - cyan edge tube","Amber display trim","Reference - satin ceramic","Checkout Planter Leaves"};
    static bool first;
    [InitializeOnLoadMethod] static void Schedule()
    {if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.delayCall+=Run;}
    [MenuItem("Tools/City Mirage/Unify Architecture Materials and Bay LEDs")]
    public static void Run()
    {
        try{File.WriteAllText(Status,Apply());File.Delete(Request);}
        catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
    }
    static string Apply()
    {
        var mat=Load("Architecture Ceiling Atlas");
        var old=Load("Architecture Atlas");
        var swatches=Names.Select(Load).ToArray();
        if(!mat||!old||swatches.Any(m=>!m)||ShaderUtil.ShaderHasError(mat.shader))
            throw new InvalidOperationException("Architecture material, source swatches, or shader missing.");
        var root=PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            if(root.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="Wall bays | continuous warm emission"))return "DONE (already consolidated)";
            var renderers=root.GetComponentsInChildren<MeshRenderer>(true);
            var bays=renderers.Where(r=>r.enabled&&Regex.IsMatch(r.name,@"^Wall Bay 0[1-4]$")).ToArray();
            if(bays.Length!=4)throw new InvalidOperationException($"Expected four wall bays, found {bays.Length}.");
            bool OldLed(MeshRenderer r)=>Regex.IsMatch(r.name,@"^Wall Bay 0[1-4] (top|upright|corner)")||r.name.StartsWith("Display light housing");
            var targets=renderers.Where(r=>r.enabled&&!OldLed(r)&&r.sharedMaterials.Any(m=>m==old||Array.IndexOf(swatches,m)>=0)).ToArray();
            foreach(var r in targets)
            {
                var m=r.sharedMaterials;
                if(!m.All(x=>x==m[0])||!r.TryGetComponent<MeshFilter>(out var f)||!f.sharedMesh)
                    throw new InvalidOperationException("Unsupported material layout on "+r.name);
            }
            first=true;
            if(AssetDatabase.LoadMainAssetAtPath(Asset))AssetDatabase.DeleteAsset(Asset);
            var cache=new Dictionary<(Mesh,int),Mesh>();
            int original=0;
            foreach(var r in targets)
            {
                var f=r.GetComponent<MeshFilter>();
                var source=r.sharedMaterial;
                int slot=Array.IndexOf(swatches,source);
                if(slot<0)
                {
                    original++;
                    Bind(f,r,f.sharedMesh,mat);
                    continue;
                }
                var key=(f.sharedMesh,slot);
                if(!cache.TryGetValue(key,out var mesh))
                {
                    mesh=UnityEngine.Object.Instantiate(f.sharedMesh);
                    mesh.name=f.sharedMesh.name+" | architecture swatch "+slot;
                    mesh.uv=Enumerable.Repeat(UV(slot),mesh.vertexCount).ToArray();
                    if(slot==7)mesh=TwoSided(mesh);
                    Save(mesh);cache.Add(key,mesh);
                }
                Bind(f,r,mesh,mat);
            }
            var group=new GameObject("Wall bays | continuous warm emission").transform;
            group.SetParent(root.transform,false);
            foreach(var bay in bays)
            {
                var b=bay.bounds;
                bool alongX=b.size.x>b.size.z;
                var axis=alongX?Vector3.right:Vector3.forward;
                var front=alongX?Vector3.forward:Vector3.left;
                float half=(alongX?b.extents.x:b.extents.z)-.095f;
                var origin=new Vector3(b.center.x,b.min.y+.14f,b.center.z)+front*((alongX?b.extents.z:b.extents.x)+.014f);
                float h=b.size.y-.27f;
                var mesh=Strip(bay.name+" | continuous LED outline",half,h,axis,front);
                Save(mesh);
                var go=new GameObject(mesh.name);
                go.transform.SetParent(group,false);go.transform.position=origin;
                go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=mat;r.shadowCastingMode=ShadowCastingMode.Off;
            }
            foreach(var r in renderers.Where(OldLed))
            {
                r.enabled=false;
                if(PrefabUtility.IsPartOfPrefabInstance(r))PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            }
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root,Prefab,out var saved);
            if(!saved)throw new IOException("Architecture prefab save failed.");
            return $"DONE: {original} old Architecture Atlas users and {targets.Length-original} swatch renderers consolidated; four connected bay LED strips. No Light or Volume edits.";
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    static Material Load(string name)=>AssetDatabase.LoadAssetAtPath<Material>(Materials+name+".mat");
    static Vector2 UV(int i)
    {
        int col=i<3?i:i<6?i-3:(i-6)*2;
        int row=i<3?0:i<6?2:1;
        return new Vector2((2+(col+.5f)/3)/4,(1+(row+.5f)/3)/4);
    }
    static void Save(Mesh m)
    {if(first){AssetDatabase.CreateAsset(m,Asset);first=false;}else AssetDatabase.AddObjectToAsset(m,Asset);}
    static void Bind(MeshFilter f,MeshRenderer r,Mesh m,Material mat)
    {
        f.sharedMesh=m;r.sharedMaterials=Enumerable.Repeat(mat,m.subMeshCount).ToArray();
        if(PrefabUtility.IsPartOfPrefabInstance(f))
        {PrefabUtility.RecordPrefabInstancePropertyModifications(f);PrefabUtility.RecordPrefabInstancePropertyModifications(r);}
    }
    static Mesh TwoSided(Mesh front)
    {
        var back=UnityEngine.Object.Instantiate(front);
        back.normals=back.normals.Select(n=>-n).ToArray();
        for(int i=0;i<back.subMeshCount;i++)
        {
            var t=back.GetTriangles(i);
            for(int j=0;j<t.Length;j+=3){int a=t[j];t[j]=t[j+1];t[j+1]=a;}
            back.SetTriangles(t,i);
        }
        var m=new Mesh{name=front.name+" | front and back"};
        m.CombineMeshes(new[]{new CombineInstance{mesh=front,transform=Matrix4x4.identity},new CombineInstance{mesh=back,transform=Matrix4x4.identity}},true,true);
        UnityEngine.Object.DestroyImmediate(front);UnityEngine.Object.DestroyImmediate(back);
        return m;
    }
    static Mesh Strip(string name,float x,float h,Vector3 axis,Vector3 front)
    {
        var path=new[]{new Vector2(-x,0),new Vector2(-x,h-.17f),new Vector2(-x+.17f,h),
            new Vector2(x-.17f,h),new Vector2(x,h-.17f),new Vector2(x,0)};
        var verts=new List<Vector3>();var tris=new List<int>();
        for(int i=0;i<path.Length;i++)
        {
            var p=path[i];
            var before=(p-path[Mathf.Max(i-1,0)]).normalized;
            var after=(path[Mathf.Min(i+1,path.Length-1)]-p).normalized;
            var d=(before+after).normalized;
            var lateral=new Vector2(-d.y,d.x);
            var side=(axis*lateral.x+Vector3.up*lateral.y)*.011f;
            var c=axis*p.x+Vector3.up*p.y;
            verts.Add(c-side-front*.010f);verts.Add(c+side-front*.010f);
            verts.Add(c+side+front*.010f);verts.Add(c-side+front*.010f);
            if(i==0)continue;
            for(int j=0;j<4;j++)
            {
                int a=(i-1)*4+j,b=(i-1)*4+(j+1)%4;
                tris.AddRange(new[]{a,b,b+4,a,b+4,a+4});
            }
        }
        int end=verts.Count-4;
        tris.AddRange(new[]{0,2,1,0,3,2,end,end+1,end+2,end,end+2,end+3});
        // Winding depends on whether the display faces across X or along Z.
        if(Vector3.Dot(Vector3.Cross(axis,Vector3.up),front)<0)
            for(int i=0;i<tris.Count;i+=3){int t=tris[i];tris[i]=tris[i+1];tris[i+1]=t;}
        var m=new Mesh{name=name};m.SetVertices(verts);m.SetTriangles(tris,0);
        m.uv=Enumerable.Repeat(UV(5),verts.Count).ToArray();
        m.RecalculateNormals();m.RecalculateBounds();return m;
    }
}
