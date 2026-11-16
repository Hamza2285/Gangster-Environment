using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CityMirageShowcase
{
    const string Prefab="Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Mat="Assets/Materials/City Mirage/";
    const string Asset="Assets/Texture/City Mirage/Showcase Detail Meshes.asset";
    const string Request="Temp/CityMirageShowcase.request";
    const string Status="Temp/CityMirageShowcase.txt";
    static bool first;
    [InitializeOnLoadMethod] static void Schedule()
    {if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.delayCall+=Run;}
    [MenuItem("Tools/City Mirage/Refine Showcase and Checkout")]
    public static void Run()
    {
        try{File.WriteAllText(Status,Apply());File.Delete(Request);}
        catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
    }
    static string Apply()
    {
        var cloth=AssetDatabase.LoadAssetAtPath<Material>(Mat+"Clothes Atlas.mat");
        var architecture=AssetDatabase.LoadAssetAtPath<Material>(Mat+"Architecture Ceiling Atlas.mat");
        var props=AssetDatabase.LoadAssetAtPath<Material>(Mat+"Props Floor Walls Atlas.mat");
        if(!cloth||!architecture||!props)throw new InvalidOperationException("Existing three atlas materials missing.");
        var root=PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            if(root.transform.Find("Reference | showcase upgrades"))return "DONE (already applied)";
            var rs=root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
            var monitor=rs.Single(r=>r.name=="POS | slim monitor shell");
            var platform=rs.Single(r=>r.name=="Mannequin display platform");
            var figures=rs.Where(r=>r.name.StartsWith("Mannequin 01 |")).ToArray();
            var leaves=rs.Single(r=>r.name=="Planter | low-poly foliage");
            var pots=rs.Where(r=>r.name=="Planter | smooth ceramic"&&r.bounds.max.y<.80f).ToArray();
            var bays=rs.Where(r=>Regex.IsMatch(r.name,@"^Wall Bay 0[1-4]$")).ToArray();
            var shirts=rs.Where(r=>r.name.StartsWith("T-shirt |")||r.name.StartsWith("Hoodie |")).ToArray();
            var shoes=rs.Where(r=>r.name=="Sale table | display sneaker").ToArray();
            if(figures.Length<5||pots.Length!=2||bays.Length!=4||shirts.Length<60||shoes.Length!=8)
                throw new InvalidOperationException($"Expected stock scene. Figures {figures.Length}, floor pots {pots.Length}, bays {bays.Length}, shirts {shirts.Length}, shoes {shoes.Length}.");
            first=!AssetDatabase.LoadMainAssetAtPath(Asset);
            var group=new GameObject("Reference | showcase upgrades").transform;
            group.SetParent(root.transform,false);
            var b=monitor.bounds;
            var dims=b.size;
            var panel=new Builder(b.center);
            float x=b.max.x-b.center.x;
            panel.Box(new Vector3(x+.007f,0,0),new Vector3(.019f,dims.y*.68f,dims.z*.68f),Swatch(1));
            panel.Box(new Vector3(x+.019f,-dims.y*.043f,0),new Vector3(.014f,.105f,.124f),Swatch(0));
            for(int i=0;i<14;i++)
                panel.Box(new Vector3(x+.020f,dims.y*.23f,(i-6.5f)*dims.z*.038f),
                    new Vector3(.007f,.034f,.006f),Swatch(0));
            foreach(float zz in new[]{-.043f,.043f})
            foreach(float yy in new[]{-.071f,.037f})
                panel.Box(new Vector3(x+.028f,yy,zz),new Vector3(.006f,.012f,.012f),Swatch(2));
            panel.Box(new Vector3(x+.023f,-dims.y*.28f,-dims.z*.18f),new Vector3(.012f,.036f,.091f),Swatch(0));
            for(int i=0;i<3;i++)
                panel.Box(new Vector3(x+.030f,-dims.y*.28f,-dims.z*.18f+(i-1)*.023f),new Vector3(.004f,.013f,.014f),Swatch(2));
            Add(group,panel.Mesh("POS | rear vents, mount and port detail"),b.center,architecture);

            var pb=platform.bounds;
            float lift=pb.size.y;
            var center=figures.Select(r=>r.bounds.center).Aggregate(Vector3.zero,(a,c)=>a+c)/figures.Length;
            float sign=Mathf.Sign(center.x-pb.center.x);
            var deckCenter=new Vector3(pb.center.x+sign*pb.size.x*.247f,pb.max.y+lift*.5f,pb.center.z);
            float width=pb.size.x*.49f,depth=pb.size.z*.92f;
            var riser=new Builder(deckCenter);
            riser.Box(Vector3.zero,new Vector3(width,lift,depth),Swatch(1));
            foreach(float e in new[]{-1f,1f})
            {
                riser.Box(new Vector3(0,lift*.40f,e*depth*.5f),new Vector3(width,.018f,.016f),Swatch(5));
                riser.Box(new Vector3(e*width*.5f,lift*.40f,0),new Vector3(.016f,.018f,depth),Swatch(5));
            }
            Add(group,riser.Mesh("Mannequin deck | raised upper tier"),deckCenter,architecture);
            foreach(var t in figures.Select(r=>r.transform).Distinct())
            {
                t.position+=Vector3.up*lift;
                if(PrefabUtility.IsPartOfPrefabInstance(t))PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            }
            foreach(var pot in pots)
            {
                var target=pot.bounds;
                var plant=new GameObject("Planter | floor foliage");
                plant.transform.SetParent(group,false);
                plant.transform.rotation=leaves.transform.rotation;
                float scale=Mathf.Clamp(target.size.x/.27f,1.05f,1.8f);
                plant.transform.localScale=leaves.transform.lossyScale*scale;
                var relative=(leaves.bounds.center-leaves.transform.position)*scale;
                plant.transform.position=new Vector3(target.center.x,target.max.y-.034f+leaves.bounds.extents.y*scale,target.center.z)-relative;
                plant.AddComponent<MeshFilter>().sharedMesh=leaves.GetComponent<MeshFilter>().sharedMesh;
                var r=plant.AddComponent<MeshRenderer>();r.sharedMaterials=leaves.sharedMaterials;r.shadowCastingMode=ShadowCastingMode.Off;
            }

            foreach(var bay in bays)
            {
                var bb=bay.bounds;
                bool wide=bb.size.x>bb.size.z;
                var horizontal=wide?Vector3.right:Vector3.forward;
                var front=wide?Vector3.forward:Vector3.left;
                float widthB=(wide?bb.size.x:bb.size.z)-.19f;
                float h=bb.size.y-.24f;
                var c=new Vector3(bb.center.x,bb.min.y+.135f+h*.5f,bb.center.z)-front*((wide?bb.extents.z:bb.extents.x)-.125f);
                var mesh=new Mesh{name=bay.name+" | textured charcoal lining"};
                var left=-horizontal*widthB*.5f;var right=-left;var up=Vector3.up*h*.5f;
                mesh.vertices=new[]{left-up,right-up,right+up,left+up};
                mesh.uv=new[]{new Vector2(1.877f,.253f),new Vector2(1.997f,.253f),new Vector2(1.997f,.497f),new Vector2(1.877f,.497f)};
                mesh.triangles=Vector3.Dot(Vector3.Cross(horizontal,Vector3.up),front)>0?new[]{0,1,2,0,2,3}:new[]{0,2,1,0,3,2};
                mesh.RecalculateNormals();
                Add(group,mesh,c,cloth);
            }
            var drape=new Dictionary<Mesh,Mesh>();
            int shirtTriangles=0;
            foreach(var r in shirts)
            {
                var f=r.GetComponent<MeshFilter>();
                if(!drape.TryGetValue(f.sharedMesh,out var m))
                {
                    m=Refine(f.sharedMesh);Save(m);drape.Add(f.sharedMesh,m);
                }
                Bind(r,m,cloth);
                shirtTriangles+=(int)m.GetIndexCount(0)/3;
            }
            var footwear=new Dictionary<(Mesh,Quaternion,bool),Mesh>();
            foreach(var r in shoes)
            {
                var old=r.GetComponent<MeshFilter>().sharedMesh;
                bool dark=old.name.Contains("charcoal");
                var key=(old,r.transform.rotation,dark);
                if(!footwear.TryGetValue(key,out var m)){m=Shoe(r,dark);Save(m);footwear.Add(key,m);}
                Bind(r,m,cloth);
            }
            var used=root.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
            if(used.Length!=3||used.Any(m=>m!=cloth&&m!=architecture&&m!=props))
                throw new InvalidOperationException("Interior no longer has exactly three atlas materials.");
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root,Prefab,out var saved);
            if(!saved)throw new IOException("Could not save showcase detail.");
            return $"DONE: LCD detail, 2-tier mannequin deck (+{lift:F2} m), {pots.Length} floor planters, {bays.Length} charcoal bays, {shirts.Length} refined hanging garments ({shirtTriangles} triangles), {shoes.Length} layered sneakers. Exactly 3 atlas materials.";
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }

    static Vector2 Swatch(int i)
    {
        int col=i<3?i:i<6?i-3:(i-6)*2,row=i<3?0:i<6?2:1;
        return new Vector2((2+(col+.5f)/3)/4,(1+(row+.5f)/3)/4);
    }
    static void Save(Mesh m)
    {if(first){AssetDatabase.CreateAsset(m,Asset);first=false;}else AssetDatabase.AddObjectToAsset(m,Asset);}
    static void Add(Transform root,Mesh m,Vector3 pos,Material mat)
    {
        Save(m);
        var o=new GameObject(m.name);o.transform.SetParent(root,false);o.transform.position=pos;
        o.AddComponent<MeshFilter>().sharedMesh=m;
        var r=o.AddComponent<MeshRenderer>();r.sharedMaterial=mat;r.shadowCastingMode=ShadowCastingMode.Off;
    }
    static void Bind(MeshRenderer r,Mesh m,Material mat)
    {
        var f=r.GetComponent<MeshFilter>();f.sharedMesh=m;r.sharedMaterial=mat;
        if(PrefabUtility.IsPartOfPrefabInstance(f))
        {PrefabUtility.RecordPrefabInstancePropertyModifications(f);PrefabUtility.RecordPrefabInstancePropertyModifications(r);}
    }
    sealed class Builder
    {
        readonly List<Vector3> v=new List<Vector3>();readonly List<Vector2> uv=new List<Vector2>();readonly List<int> t=new List<int>();
        public Builder(Vector3 center){}
        public void Box(Vector3 p,Vector3 size,Vector2 texture)
        {
            var h=size*.5f;int a=v.Count;
            for(int i=0;i<8;i++)
            {v.Add(p+new Vector3((i&1)==0?-h.x:h.x,(i&2)==0?-h.y:h.y,(i&4)==0?-h.z:h.z));uv.Add(texture);}
            int[] faces={0,2,3,0,3,1,4,5,7,4,7,6,0,1,5,0,5,4,2,6,7,2,7,3,0,4,6,0,6,2,1,3,7,1,7,5};
            t.AddRange(faces.Select(i=>i+a));
        }
        public Mesh Mesh(string n)
        {
            var m=new Mesh{name=n};m.SetVertices(v);m.SetUVs(0,uv);m.SetTriangles(t,0);m.RecalculateNormals();return m;
        }
    }
    static Mesh Refine(Mesh source)
    {
        var v=source.vertices.ToList();var n=source.normals.ToList();var uv=source.uv.ToList();
        var mid=new Dictionary<(int,int),int>();var t=new List<int>();
        float threshold=source.bounds.size.magnitude*.067f;
        int Split(int a,int b)
        {
            var key=a<b?(a,b):(b,a);
            if(mid.TryGetValue(key,out var existing))return existing;
            var dir=v[b]-v[a];
            if(dir.magnitude<threshold||Vector3.Dot(n[a],n[b])>.985f)return -1;
            var p=(v[a]+v[b])*.5f;
            var shift=(-n[a]*Vector3.Dot(p-v[a],n[a])-n[b]*Vector3.Dot(p-v[b],n[b]))*.28f;
            int index=v.Count;
            v.Add(p+Vector3.ClampMagnitude(shift,.0024f));
            n.Add((n[a]+n[b]).normalized);uv.Add((uv[a]+uv[b])*.5f);mid.Add(key,index);
            return index;
        }
        void Tri(int a,int b,int c){t.Add(a);t.Add(b);t.Add(c);}
        void Two(int a,int b,int c,int ab,int bc){Tri(b,bc,ab);Tri(a,ab,c);Tri(ab,bc,c);}
        var old=source.triangles;
        for(int i=0;i<old.Length;i+=3)
        {
            int a=old[i],b=old[i+1],c=old[i+2],ab=Split(a,b),bc=Split(b,c),ca=Split(c,a);
            int count=(ab>=0?1:0)+(bc>=0?1:0)+(ca>=0?1:0);
            if(count==0)Tri(a,b,c);
            else if(count==1)
            {
                if(ab>=0){Tri(a,ab,c);Tri(ab,b,c);}
                else if(bc>=0){Tri(b,bc,a);Tri(bc,c,a);}
                else {Tri(c,ca,b);Tri(ca,a,b);}
            }
            else if(count==2)
            {
                if(ca<0)Two(a,b,c,ab,bc);
                else if(ab<0)Two(b,c,a,bc,ca);
                else Two(c,a,b,ca,ab);
            }
            else {Tri(a,ab,ca);Tri(ab,b,bc);Tri(ca,bc,c);Tri(ab,bc,ca);}
        }
        var mesh=new Mesh{name=source.name+" | soft shoulder detail"};
        mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateTangents();return mesh;
    }
    static Mesh Shoe(MeshRenderer r,bool dark)
    {
        var bounds=r.bounds;
        var points=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
        // Model normalized sneaker proportions and preserve placement and the old footprint.
        Vector3 World(float x,float y,float z)=>new Vector3(bounds.center.x+x*bounds.size.x,bounds.min.y+y*bounds.size.y*.89f,bounds.center.z+z*bounds.size.z*1.08f);
        Vector2 ColorUV(int surface)
        {
            int swatch=surface==1?7:surface==2?(dark?5:2):surface==3?(dark?6:3):
                surface==4?(dark?5:1):surface==5?(dark?5:1):surface==6?(dark?6:3):dark?4:0;
            return new Vector2(1.5f+(swatch+.5f)/64f,.3125f);
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
            Vertex(x,y,z,layer<2?4:layer<4?1:layer>5?3:0);
            if(layer==0)continue;
            int A=(layer-1)*sides+j,B=(layer-1)*sides+(j+1)%sides;
            tris.AddRange(new[]{A,A+sides,B,A+sides,B+sides,B});
        }
        for(int j=1;j<sides-1;j++){tris.AddRange(new[]{0,j,j+1});int a=8*sides;tris.AddRange(new[]{a,a+j+1,a+j});}
        void Overlay(int j,int layer,int style)
        {
            int a=layer*sides+j,b=layer*sides+(j+1)%sides;
            float angle=(j+.5f)*2*Mathf.PI/sides;
            var offset=r.transform.InverseTransformVector(new Vector3(Mathf.Cos(angle),.18f,Mathf.Sin(angle))*.0014f);
            int i=points.Count;
            foreach(int k in new[]{a,a+sides,b+sides,b})
            {points.Add(points[k]+offset);uv.Add(ColorUV(style));}
            tris.AddRange(new[]{i,i+1,i+3,i+1,i+2,i+3});
        }
        for(int j=0;j<sides;j++)
        {
            float angle=(j+.5f)*2*Mathf.PI/sides;
            float s=Mathf.Sin(angle);
            if(s>.54f||s<-.70f)Overlay(j,3,5);
            if(Mathf.Abs(s)<.47f)Overlay(j,4,6);
        }
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


