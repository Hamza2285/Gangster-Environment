using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class CityMirageWallArtwork
{
    const string Shop="Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Folder="Assets/Prefeb/Cloth Shop/Reusable/";
    const string Request="Temp/CityMirageWallArtwork.request";
    const string Status="Temp/CityMirageWallArtwork.txt";
    const string Asset="Assets/Texture/City Mirage/Wall Apparel Display Meshes.asset";
    [InitializeOnLoadMethod] static void Queue()
    {
        if (!Application.isBatchMode && File.Exists(Request)) EditorApplication.delayCall += Run;
    }

    [MenuItem("Tools/City Mirage/Add Wall Artwork and Folded Display")]
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
        var shop=PrefabUtility.LoadPrefabContents(Shop);
        try
        {
            if (shop.transform.Find("Wall artwork | left folded display")) return "DONE: artwork already present.";
            var rs=shop.GetComponentsInChildren<MeshRenderer>(true);
            var left=rs.Single(r=>r.name=="Wall Bay 02 | textured charcoal lining").bounds;
            var right=rs.Single(r=>r.name=="Wall Bay 01 | textured charcoal lining").bounds;
            var side=rs.Single(r=>r.name=="Wall Bay 04 | textured charcoal lining").bounds;
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/City Mirage/Clothes Atlas.mat");
            var shirt=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"Folded Company Shirt.prefab");
            var variants=AssetDatabase.LoadAllAssetsAtPath("Assets/Texture/City Mirage/Supplied Shirt Atlas Meshes.asset").OfType<Mesh>().ToDictionary(m=>m.name);
            if(!material || !shirt || AssetDatabase.LoadMainAssetAtPath(Asset)) throw new InvalidOperationException("Unexpected wall display asset state.");
            var module=MakePrefab(shop,material,shirt,variants,true);
            var art=MakePrefab(shop,material,shirt,variants,false);
            Place(shop,module,"Wall artwork | left folded display",new Vector3(left.min.x-1.08f,0,left.center.z-.05f),Quaternion.identity);
            Place(shop,art,"Wall artwork | right of mirror",new Vector3(right.max.x+1.03f,0,right.center.z-.05f),Quaternion.identity);
            Place(shop,art,"Wall artwork | side panel",new Vector3(side.center.x+.06f,0,side.max.z+1.05f),Quaternion.Euler(0,-90,0));
            if(shop.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy)
                .SelectMany(r=>r.sharedMaterials).Distinct().Count()!=3) throw new InvalidOperationException("Expected three shared materials.");
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(shop,Shop,out bool saved);
            if(!saved) throw new IOException("Could not save the wall displays.");
            return "DONE: three wall artwork placements and one cabinet display with 12 linked folded shirts. Three existing materials.";
        }
        finally { PrefabUtility.UnloadPrefabContents(shop); }
    }

    static void Place(GameObject shop,GameObject source,string name,Vector3 position,Quaternion rotation)
    {
        var instance=(GameObject)PrefabUtility.InstantiatePrefab(source,shop.scene);
        instance.transform.SetParent(shop.transform,false);
        instance.transform.SetPositionAndRotation(position,rotation);
        instance.name=name;
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
    }

    static GameObject MakePrefab(GameObject shop,Material material,GameObject shirt,Dictionary<string,Mesh> variants,bool cabinet)
    {
        string name=cabinet?"Apparel Artwork and Folded Display":"Framed Apparel Artwork";
        var root=new GameObject(name);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,shop.scene);
        try
        {
            var mesh=Build(cabinet);
            if(cabinet) AssetDatabase.CreateAsset(mesh,Asset); else AssetDatabase.AddObjectToAsset(mesh,Asset);
            root.AddComponent<MeshFilter>().sharedMesh=mesh;
            root.AddComponent<MeshRenderer>().sharedMaterial=material;
            if(cabinet)
            {
                for(int shelf=0;shelf<3;shelf++)
                for(int stack=0;stack<2;stack++)
                for(int layer=0;layer<2;layer++)
                {
                    var item=(GameObject)PrefabUtility.InstantiatePrefab(shirt,shop.scene);
                    item.transform.SetParent(root.transform,false);
                    item.transform.localPosition=new Vector3(stack==0?-.25f:.25f,.49f+shelf*.29f+layer*.029f,.29f);
                    item.transform.localScale=Vector3.one*.043f;
                    for(int lod=0;lod<2;lod++)
                    {
                        var f=item.transform.Find("LOD"+lod).GetComponent<MeshFilter>();
                        f.sharedMesh=variants[$"Folded Company Shirt | color {(shelf*2+stack+layer)%6+1} LOD{lod}"];
                        PrefabUtility.RecordPrefabInstancePropertyModifications(f);
                    }
                }
            }
            var result=PrefabUtility.SaveAsPrefabAsset(root,Folder+name+".prefab",out bool saved);
            if(!saved || !result) throw new IOException("Could not save "+name);
            return result;
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static Mesh Build(bool cabinet)
    {
        var p=new List<Vector3>(); var uv=new List<Vector2>(); var tris=new List<int>();
        Vector2 dark=new Vector2(1.968f,.485f), wood=new Vector2(1.814f,.375f);
        Vector2 warm=new Vector2(3f+4080f/4096f,1048f/2048f);
        void Face(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector2 lo,Vector2 hi)
        {
            int n=p.Count;
            p.AddRange(new[]{a,b,c,d});
            uv.AddRange(new[]{lo,new Vector2(hi.x,lo.y),new Vector2(lo.x,hi.y),hi});
            tris.AddRange(new[]{n,n+1,n+2,n+2,n+1,n+3,n,n+2,n+1,n+2,n+3,n+1});
        }
        void Box(float x,float y,float z,float w,float h,float d,Vector2 tone)
        {
            float a=x-w*.5f,b=x+w*.5f,c=y-h*.5f,e=y+h*.5f,f=z-d*.5f,g=z+d*.5f;
            Vector3 V(float xx,float yy,float zz)=>new Vector3(xx,yy,zz);
            Face(V(a,c,g),V(b,c,g),V(a,e,g),V(b,e,g),tone,tone);
            Face(V(a,c,f),V(a,c,g),V(a,e,f),V(a,e,g),tone,tone);
            Face(V(b,c,g),V(b,c,f),V(b,e,g),V(b,e,f),tone,tone);
            Face(V(a,e,g),V(b,e,g),V(a,e,f),V(b,e,f),tone,tone);
            Face(V(a,c,f),V(b,c,f),V(a,c,g),V(b,c,g),tone,tone);
        }
        float y0=cabinet?1.83f:1.56f;
        Box(0,y0,.09f,.80f,1.16f,.09f,dark);
        Box(0,y0,.139f,.75f,1.11f,.014f,warm);
        Face(new Vector3(-.358f,y0-.535f,.149f),new Vector3(.358f,y0-.535f,.149f),
            new Vector3(-.358f,y0+.535f,.149f),new Vector3(.358f,y0+.535f,.149f),
            new Vector2(1f+3597f/4096f,1f-859f/2048f),new Vector2(1f+3775f/4096f,1f-589f/2048f));
        if(cabinet)
        {
            Box(0,.226f,.30f,1.22f,.45f,.54f,dark);
            for(int i=0;i<3;i++)
            {
                float y=.47f+i*.29f;
                Box(0,y,.30f,1.24f,.04f,.54f,wood);
                Box(0,y-.021f,.572f,1.20f,.014f,.009f,warm);
            }
        }
        var mesh=new Mesh{name=cabinet?"Apparel cabinet and poster":"Framed streetwear poster"};
        mesh.SetVertices(p);mesh.SetUVs(0,uv);mesh.SetTriangles(tris,0);
        // Two-sided faces retain the front-face shading of the wall-mounted asset.
        mesh.SetNormals(Enumerable.Repeat(Vector3.forward,p.Count).ToList());
        mesh.RecalculateTangents();mesh.RecalculateBounds();
        return mesh;
    }
}
