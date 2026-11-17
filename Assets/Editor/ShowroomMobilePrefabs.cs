using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

internal static class ShowroomMobilePrefabs
{
    const string ScenePath="Assets/Scenes/Car Show room.unity";
    const string Prefabs="Assets/Prefeb/Showroom";
    const string Meshes=Prefabs+"/LOD Meshes";
    const string Trigger="Showroom prefab optimization.request";
    const string Report="Showroom Prefab and LOD Verification.txt";

    [InitializeOnLoadMethod]
    static void Schedule()
    {
        EditorApplication.delayCall+=()=>
        {
            if(!File.Exists(Trigger)) return;
            File.Delete(Trigger);
            try { Apply(); }
            catch(Exception ex) { File.AppendAllText(Report,ex.ToString()); Debug.LogException(ex); }
        };
    }
    static string Family(string n)
    {
        if(n.EndsWith(" Sofa",StringComparison.Ordinal)) return "Sofa";
        if(n.EndsWith(" Armchair",StringComparison.Ordinal)) return "Armchair";
        if(n.EndsWith(" Coffee Table",StringComparison.Ordinal)) return "CoffeeTable";
        if(n=="Ground Rug"||n=="Mezzanine Rug") return "LoungeRug";
        if(n.Contains(" Display Bay ")) return "VehicleDisplayBay";
        if(n.StartsWith("Vehicle Info Stand ")) return "VehicleInfoStand";
        if(n.StartsWith("Customer Service Desk ")) return "CustomerServiceDesk";
        if(n.StartsWith("Reception Chair ")||n.StartsWith("Service Chair ")) return "OfficeChair";
        if(n.StartsWith("Desktop Planter ")) return "DesktopPlanter";
        if(n.StartsWith("Planter ")) return "FloorPlanter";
        if(n.StartsWith("Entrance Door ")) return "EntranceDoor";
        return null;
    }

    static bool WantsLod(string family)=>family is "Sofa" or "Armchair" or "CoffeeTable" or "CustomerServiceDesk" or "OfficeChair" or "FloorPlanter" or "DesktopPlanter" or "VehicleDisplayBay" or "VehicleInfoStand";
    static int Count(Mesh m)=>Enumerable.Range(0,m.subMeshCount).Sum(i=>(int)m.GetIndexCount(i)/3);

    static GameObject Room(Scene scene) => scene.GetRootGameObjects().Single(o=>
        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(o)=="Assets/Model/Show Room.fbx" ||
        (o.name=="Show Room" && o.transform.Find("Ceiling")!=null));

    sealed class State
    {
        public GameObject Object;
        public Mesh Mesh;
        public Material[] Materials;
        public int Lightmap;
        public Vector4 LightmapTransform;
        public Transform Parent;
        public int Sibling;
        public Vector3 Position,Scale;
        public Quaternion Rotation;
        public Matrix4x4 World;
        public State(GameObject go)
        {
            Object=go;
            var t=go.transform; var r=go.GetComponent<MeshRenderer>();
            Mesh=go.GetComponent<MeshFilter>().sharedMesh; Materials=r.sharedMaterials;
            Lightmap=r.lightmapIndex;LightmapTransform=r.lightmapScaleOffset;
            Parent=t.parent;Sibling=t.GetSiblingIndex();Position=t.localPosition;Scale=t.localScale;
            Rotation=t.localRotation;World=t.localToWorldMatrix;
        }
        public void Verify(bool meshes=true)
        {
            var t=Object.transform;
            if(t.parent!=Parent||t.GetSiblingIndex()!=Sibling||!t.localPosition.Equals(Position)||!t.localScale.Equals(Scale)||!t.localRotation.Equals(Rotation)||!t.localToWorldMatrix.Equals(World))
                throw new InvalidOperationException("Placement changed: "+Object.name);
            var f=Object.GetComponent<MeshFilter>(); var r=Object.GetComponent<MeshRenderer>();
            if(meshes&&f.sharedMesh!=Mesh||!r.sharedMaterials.SequenceEqual(Materials))
                throw new InvalidOperationException("Visual source changed: "+Object.name);
        }
    }
    static Mesh Cluster(Mesh src,int divisions,bool protectTexture)
    {
        var v=src.vertices; var uv=src.uv;var uv2=src.uv2;var n=src.normals;var tangents=src.tangents;
        var b=src.bounds;float span=Mathf.Max(b.size.x,Mathf.Max(b.size.y,b.size.z));
        if(v.Length==0||uv.Length!=v.Length||n.Length!=v.Length||span<.001f) return null;
        float inv=divisions/span;
        int chart=protectTexture?44:8;
        var keyToIndex=new Dictionary<(int,int,int,int,int,int,int,int,int),int>();
        var vs=new List<Vector3>();var ns=new List<Vector3>();var us=new List<Vector2>();var ls=new List<Vector2>();var ts=new List<Vector4>();
        var weights=new List<int>();
        var results=new List<int[]>();
        for(int s=0;s<src.subMeshCount;s++)
        {
            var indices=src.GetTriangles(s);
            int Map(int i)
            {
                var p=(v[i]-b.min)*inv;
                var key=(s,Mathf.FloorToInt(p.x),Mathf.FloorToInt(p.y),Mathf.FloorToInt(p.z),
                    Mathf.FloorToInt(uv[i].x*chart),Mathf.FloorToInt(uv[i].y*chart),
                    Mathf.RoundToInt(n[i].x*1.25f),Mathf.RoundToInt(n[i].y*1.25f),Mathf.RoundToInt(n[i].z*1.25f));
                if(keyToIndex.TryGetValue(key,out var j))
                {
                    vs[j]+=v[i];ns[j]+=n[i];us[j]+=uv[i];weights[j]++;
                    if(uv2.Length==v.Length)ls[j]+=uv2[i];
                    if(tangents.Length==v.Length)ts[j]+=tangents[i];
                    return j;
                }
                j=vs.Count;keyToIndex[key]=j;vs.Add(v[i]);ns.Add(n[i]);us.Add(uv[i]);weights.Add(1);
                if(uv2.Length==v.Length)ls.Add(uv2[i]);
                if(tangents.Length==v.Length)ts.Add(tangents[i]);
                return j;
            }
            var used=new Dictionary<int,int>();
            int Lookup(int i){if(!used.TryGetValue(i,out var j))used[i]=j=Map(i);return j;}
            var triangles=new List<int>(indices.Length);
            var unique=new HashSet<(int,int,int)>();
            for(int i=0;i<indices.Length;i+=3)
            {
                int a=Lookup(indices[i]),c=Lookup(indices[i+1]),d=Lookup(indices[i+2]);
                if(a==c||a==d||c==d) continue;
                int lo=Math.Min(a,Math.Min(c,d)),hi=Math.Max(a,Math.Max(c,d));
                if(!unique.Add((lo,a+c+d-lo-hi,hi)))continue;
                triangles.Add(a);triangles.Add(c);triangles.Add(d);
            }
            results.Add(triangles.ToArray());
        }
        for(int i=0;i<vs.Count;i++)
        {
            vs[i]/=weights[i];ns[i]=ns[i].normalized;us[i]/=weights[i];
            if(ls.Count==vs.Count)ls[i]/=weights[i];
            if(ts.Count==vs.Count)
            {
                var v3=new Vector3(ts[i].x,ts[i].y,ts[i].z).normalized;
                ts[i]=new Vector4(v3.x,v3.y,v3.z,ts[i].w>=0?1:-1);
            }
        }
        var m=new Mesh{indexFormat=vs.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
        m.SetVertices(vs);m.SetNormals(ns);m.SetUVs(0,us);
        if(ls.Count==vs.Count)m.SetUVs(1,ls);
        if(ts.Count==vs.Count)m.SetTangents(ts);
        m.subMeshCount=results.Count;
        for(int i=0;i<results.Count;i++)m.SetTriangles(results[i],i,false);
        m.bounds=b;
        return m;
    }
    static Mesh MakeLod(Mesh source,string name,int level,bool textured)
    {
        int original=Count(source);
        float target=level==1?.63f:.34f;
        int[] divisions=level==1?new[]{28,22,18,15,12,10,8,6}:new[]{16,13,10,8,6,5,4,3};
        Mesh best=null;float score=float.MaxValue;
        foreach(int d in divisions)
        {
            var mesh=Cluster(source,d,textured);
            if(!mesh)continue;
            float ratio=Count(mesh)/(float)original;
            if(ratio<.15f||ratio>=.87f){UnityEngine.Object.DestroyImmediate(mesh);continue;}
            float cost=Mathf.Abs(ratio-target);
            if(cost<score)
            {
                if(best)UnityEngine.Object.DestroyImmediate(best);
                best=mesh;score=cost;
            }
            else UnityEngine.Object.DestroyImmediate(mesh);
        }
        if(!best)return null;
        string safe=new string(name.Select(c=>char.IsLetterOrDigit(c)?c:'_').ToArray());
        best.name=safe+"_LOD"+level;
        string path=Meshes+"/"+best.name+".asset";
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing)
        {
            EditorUtility.CopySerialized(best,existing);
            UnityEngine.Object.DestroyImmediate(best);
            return existing;
        }
        AssetDatabase.CreateAsset(best,path);
        return best;
    }
    static (int l1,int l2) AddLods(GameObject go,string family,Mesh source,Material[] mats)
    {
        if(!WantsLod(family)||Count(source)<260)return (0,0);
        var l1=MakeLod(source,go.name,1,family=="CustomerServiceDesk");
        if(!l1)return (0,0);
        var l2=Count(source)>=550?MakeLod(source,go.name,2,family=="CustomerServiceDesk"):null;
        if(l2&&Count(l2)>=Count(l1)*.88f)l2=null;
        var group=go.GetComponent<LODGroup>();
        if(!group) group=go.AddComponent<LODGroup>();
        MeshRenderer Lower(string name,Mesh mesh)
        {
            var child=go.transform.Find(name);
            var obj=child?child.gameObject:new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
            if(!child)obj.transform.SetParent(go.transform,false);
            obj.layer=go.layer;
            obj.GetComponent<MeshFilter>().sharedMesh=mesh;
            var r=obj.GetComponent<MeshRenderer>();
            r.sharedMaterials=mats;
            r.shadowCastingMode=ShadowCastingMode.Off;
            r.receiveShadows=false;
            r.lightProbeUsage=LightProbeUsage.BlendProbes;
            r.reflectionProbeUsage=ReflectionProbeUsage.BlendProbes;
            r.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;
            return r;
        }
        var main=go.GetComponent<MeshRenderer>();
        var low=Lower("LOD1",l1);
        // These are indoor assets: keep a readable silhouette even from the opposite end of the showroom.
        group.fadeMode=LODFadeMode.None;
        group.SetLODs(l2?new[]{new LOD(.14f,new Renderer[]{main}),new LOD(.043f,new Renderer[]{low}),new LOD(.002f,new Renderer[]{Lower("LOD2",l2)})}
                       :new[]{new LOD(.13f,new Renderer[]{main}),new LOD(.002f,new Renderer[]{low})});
        group.RecalculateBounds();
        return (Count(l1),l2?Count(l2):0);
    }
    static void Link(GameObject go,GameObject template)
    {
        PrefabUtility.ConvertToPrefabInstance(go,template,new ConvertToPrefabInstanceSettings{
            objectMatchMode=ObjectMatchMode.ByHierarchy,
            changeRootNameToAssetName=false,
            recordPropertyOverridesOfMatches=true,
            componentsNotMatchedBecomesOverride=true,
            gameObjectsNotMatchedBecomesOverride=true
        },InteractionMode.AutomatedAction);
    }
    static void AddProbes(Scene scene,GameObject root)
    {
        var holder=scene.GetRootGameObjects().FirstOrDefault(o=>o.name=="Showroom LOD Light Probes");
        if(!holder)holder=new GameObject("Showroom LOD Light Probes",typeof(LightProbeGroup));
        var room=root.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="Floor").bounds;
        var locations=new List<Vector3>();
        foreach(float x in new[]{.15f,.5f,.85f})
        foreach(float z in new[]{.16f,.39f,.64f,.85f})
        foreach(float y in new[]{1.05f,2.65f,5.05f})
            locations.Add(holder.transform.InverseTransformPoint(new Vector3(Mathf.Lerp(room.min.x,room.max.x,x),room.max.y+y,Mathf.Lerp(room.min.z,room.max.z,z))));
        holder.GetComponent<LightProbeGroup>().probePositions=locations.ToArray();
    }
    [MenuItem("Tools/Showroom/Build Reusable Props and Mobile LODs")]
    static void Apply()
    {
        var scene=SceneManager.GetActiveScene();
        if(scene.path!=ScenePath||EditorApplication.isPlayingOrWillChangePlaymode||Lightmapping.isRunning)
            throw new InvalidOperationException("Open the showroom in Edit Mode and finish any existing light bake.");
        var root=Room(scene);
        if(!File.Exists("Showroom before prefab optimization.unity.backup"))File.Copy(ScenePath,"Showroom before prefab optimization.unity.backup");
        var states=root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.GetComponent<MeshRenderer>()&&!f.name.StartsWith("LOD")).Select(f=>new State(f.gameObject)).ToArray();
        var families=states.Where(s=>Family(s.Object.name)!=null).GroupBy(s=>Family(s.Object.name)).Where(g=>g.Count()>1).ToArray();
        if(states.Length!=58||families.Length!=11)throw new InvalidOperationException($"Unexpected showroom layout: {states.Length} renderers, {families.Length} repeated families.");
        File.WriteAllText(Report,$"Original placed objects: {states.Length}\n");
        if(!AssetDatabase.IsValidFolder(Prefabs))AssetDatabase.CreateFolder("Assets/Prefeb","Showroom");
        if(!AssetDatabase.IsValidFolder(Meshes))AssetDatabase.CreateFolder(Prefabs,"LOD Meshes");
        if(PrefabUtility.IsOutermostPrefabInstanceRoot(root))PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        foreach(var state in states)state.Verify();
        int instances=0,lodGroups=0,lod0=0,lod1=0,lod2=0;
        foreach(var family in families)
        {
            var group=family.OrderBy(s=>s.Object.name).ToArray();
            foreach(var state in group)
            {
                var counts=AddLods(state.Object,family.Key,state.Mesh,state.Materials);
                if(counts.l1>0)
                {
                    lodGroups++;lod0+=Count(state.Mesh);lod1+=counts.l1;lod2+=counts.l2>0?counts.l2:counts.l1;
                }
            }
            string assetName="PF_Showroom_"+family.Key;
            string path=Prefabs+"/"+assetName+".prefab";
            var template=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(!template)
            {
                var copy=UnityEngine.Object.Instantiate(group[0].Object);
                copy.name=assetName;
                copy.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);copy.transform.localScale=Vector3.one;
                foreach(var r in copy.GetComponentsInChildren<MeshRenderer>(true)){r.lightmapIndex=-1;r.lightmapScaleOffset=new Vector4(1,1,0,0);}
                template=PrefabUtility.SaveAsPrefabAsset(copy,path);
                UnityEngine.Object.DestroyImmediate(copy);
            }
            if(!template)throw new InvalidOperationException("Could not save "+path);
            foreach(var state in group)
            {
                if(!PrefabUtility.IsPartOfPrefabInstance(state.Object))Link(state.Object,template);
                state.Verify();
                if(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(state.Object)!=path)
                    throw new InvalidOperationException("Prefab linkage failed: "+state.Object.name);
                instances++;
            }
            File.AppendAllText(Report,$"{assetName}: {group.Length} placed instances\n");
        }
        var fixedNames=new HashSet<string>{"Ceiling","Walls","Floor","Window","Mezzanine","Staircase","Ramp","Entrance Frame"};
        foreach(var state in states)
        {
            state.Verify();
            var go=state.Object;
            if(fixedNames.Contains(go.name)) GameObjectUtility.SetStaticEditorFlags(go,GameObjectUtility.GetStaticEditorFlags(go)|StaticEditorFlags.BatchingStatic);
            var r=go.GetComponent<MeshRenderer>();
            if(state.Lightmap>=0&&state.Lightmap<65534)
            {
                r.lightmapIndex=state.Lightmap;
                r.lightmapScaleOffset=state.LightmapTransform;
            }
            if(PrefabUtility.IsPartOfPrefabInstance(r))PrefabUtility.RecordPrefabInstancePropertyModifications(r);
        }
        AddProbes(scene,root);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save the optimized scene.");
        File.AppendAllText(Report,$"Verified unchanged placements and full-detail meshes: {states.Length}\nPrefab families: {families.Length}\nPrefab instances: {instances}\nLOD groups: {lodGroups}\nLOD prop triangles at LOD0: {lod0}\nSame props at LOD1: {lod1}\nSame props at farthest LOD: {lod2}\n");
        Lightmapping.bakeCompleted+=BakeDone;
        if(!Lightmapping.BakeAsync()){Lightmapping.bakeCompleted-=BakeDone;File.AppendAllText(Report,"Could not start lighting verification bake.\n");}
        void BakeDone()
        {
            Lightmapping.bakeCompleted-=BakeDone;
            foreach(var state in states)state.Verify();
            EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            File.AppendAllText(Report,$"Completed lighting bake: {LightmapSettings.lightmaps.Length} lightmaps.\n");
        }
    }
}

