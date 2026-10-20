using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Editor-only conversion for the five Road 01..05 materials in this scene.
// Original UV0 uses repeat addressing. Encode the material slice in a separate,
// integer offset in U; the shader decodes it before sampling. Other UV channels,
// vertices, normals, tangents, colliders and renderer settings are preserved.
[InitializeOnLoad]
public static class GangstarRoadAtlasBuilder
{
    const string ScenePath = "Assets/Scenes/Oasis/Gangstar City.unity";
    const string Output = "Assets/Scenes/Oasis/RoadAtlas";
    const string Report = "RoadAtlasBuildReport.txt";
    const string Backup = "RoadAtlasBackup";
    static GangstarRoadAtlasBuilder() { EditorApplication.delayCall += AutoRun; }
    static void AutoRun() {
        if (!File.Exists("RoadAtlasBuild.request") || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Move("RoadAtlasBuild.request", "RoadAtlasBuild.started-"+DateTime.UtcNow.Ticks);
        Build();
    }
    class Change {
        public MeshFilter filter; public MeshRenderer renderer;
        public Mesh before, after; public Material[] materials;
    }
    [MenuItem("Tools/Road Atlas/Build Gangstar City Atlas")]
    public static void Build()
    {
        var log = new StringBuilder();
        var changes = new List<Change>();
        bool saved = false;
        bool asyncBefore = ShaderUtil.allowAsyncCompilation;
        try {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded || scene.isDirty)
                throw new Exception("Load the saved Gangstar City scene before conversion. Unsaved edits will not be overwritten.");
            if (AssetDatabase.LoadAssetAtPath<Material>(Output + "/Road Atlas.mat"))
                throw new Exception("Atlas already exists; restore the backup before running another conversion.");
            var root = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .Single(t => t.name == "Track Roads");
            var sources = Enumerable.Range(1,5).Select(i =>
                AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/3D/Fbx/Materials/Road 0" + i + ".mat")).ToArray();
            if (sources.Any(m => !m)) throw new Exception("A source material is missing.");
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.sharedMaterials.Any(m => sources.Contains(m))).ToArray();
            foreach (var r in renderers) {
                if (r.sharedMaterials.Length != 1 || !sources.Contains(r.sharedMaterial))
                    throw new Exception("Mixed material renderer requires separate handling: " + r.name);
                var f = r.GetComponent<MeshFilter>();
                if (!f || !f.sharedMesh || f.sharedMesh.subMeshCount != 1)
                    throw new Exception("Unexpected mesh layout: " + r.name);
                var m = r.sharedMaterial;
                var st = m.GetTextureScale("_BaseMap"); var off = m.GetTextureOffset("_BaseMap");
                foreach (var uv in f.sharedMesh.uv) {
                    float u = uv.x * st.x + off.x;
                    if (u <= -15 || u >= 15) throw new Exception("UV outside safe encoding range: " + r.name);
                }
                changes.Add(new Change { filter=f, renderer=r, before=f.sharedMesh, materials=r.sharedMaterials });
            }
            if(changes.Count == 0) throw new Exception("No source roads found.");
            if (!SystemInfo.supports2DArrayTextures) throw new Exception("Texture arrays are unsupported on the current device.");
            foreach (var m in sources) {
                if(m.shader.name != "Universal Render Pipeline/Lit" ||
                   string.Join(",",m.shaderKeywords.OrderBy(x=>x)) != string.Join(",",sources[0].shaderKeywords.OrderBy(x=>x)))
                    throw new Exception("Source shaders or features differ.");
                foreach(var p in new[]{"_Smoothness","_Cull","_Cutoff","_Surface","_WorkflowMode","_AlphaClip"})
                    if(m.GetFloat(p)!=sources[0].GetFloat(p)) throw new Exception("Different property: "+p);
                foreach(var p in new[]{"_BaseColor","_EmissionColor"})
                    if(m.GetColor(p)!=sources[0].GetColor(p)) throw new Exception("Different color: "+p);
                if(m.GetTexture("_EmissionMap")!=m.GetTexture("_BaseMap"))
                    throw new Exception("Separate emission texture requires its own array.");
            }
            Directory.CreateDirectory(Backup);
            if(!File.Exists(Backup+"/Gangstar City.unity")) {
                File.Copy(ScenePath, Backup+"/Gangstar City.unity");
                File.Copy(ScenePath+".meta", Backup+"/Gangstar City.unity.meta");
            } else if(!File.ReadAllBytes(ScenePath).SequenceEqual(File.ReadAllBytes(Backup+"/Gangstar City.unity"))) throw new Exception("Saved scene differs from backup.");
            Directory.CreateDirectory(Output+"/Meshes");
            AssetDatabase.Refresh();
            ShaderUtil.allowAsyncCompilation = false;
            var baseTextures = sources.Select(m=>(Texture2D)m.GetTexture("_BaseMap")).ToArray();
            var surfaceTextures = sources.Select(m=>(Texture2D)m.GetTexture("_MetallicGlossMap")).Distinct().ToArray();
            if(surfaceTextures.Length != 2 || sources.Where((m,i)=>i!=2).Any(m=>m.GetTexture("_MetallicGlossMap")!=surfaceTextures[0]))
                throw new Exception("Unexpected surface-map arrangement.");
            log.AppendLine("Pipeline: "+(GraphicsSettings.currentRenderPipeline ? GraphicsSettings.currentRenderPipeline.name : "NONE"));
            MakeArray(baseTextures, Output+"/Road Color Atlas.asset",log);
            MakeArray(surfaceTextures, Output+"/Road Surface Atlas.asset",log);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Output+"/Shaders/RoadAtlas.shader");
            if(!shader || !shader.isSupported) throw new Exception("Atlas shader is not supported.");
            var material = new Material(sources[0]) { name="Road Atlas", shader=shader };
            material.SetTexture("_RoadBaseArray",AssetDatabase.LoadAssetAtPath<Texture2DArray>(Output+"/Road Color Atlas.asset"));
            material.SetTexture("_RoadSurfaceArray",AssetDatabase.LoadAssetAtPath<Texture2DArray>(Output+"/Road Surface Atlas.asset"));
            foreach(var p in new[]{"_BaseMap","_MainTex","_EmissionMap","_MetallicGlossMap","_SpecGlossMap"})
                material.SetTexture(p,null);
            material.SetTextureScale("_BaseMap",Vector2.one);
            material.SetTextureOffset("_BaseMap",Vector2.zero);
            material.shaderKeywords=sources[0].shaderKeywords;
            material.enableInstancing=true;
            AssetDatabase.CreateAsset(material,Output+"/Road Atlas.mat");
            // Fixed camera renders provide a repeatable before/after comparison.
            var poses = new List<Tuple<Vector3,Quaternion>>();
            if(SceneView.lastActiveSceneView) {
                var c=SceneView.lastActiveSceneView.camera;
                poses.Add(Tuple.Create(c.transform.position,c.transform.rotation));
            }
            foreach(var src in sources) {
                var r=renderers.Where(x=>x.sharedMaterial==src).OrderBy(x=>x.bounds.size.magnitude).First();
                Vector3 center=r.bounds.center;
                Vector3 pos=center+new Vector3(0,12,-10);
                poses.Add(Tuple.Create(pos,Quaternion.LookRotation(center-pos,Vector3.up)));
            }
            var before = poses.Select((p,i)=>Capture(p,Backup+"/Before-"+i+".png")).ToArray();
            var meshCache=new Dictionary<string,Mesh>();
            foreach(var c in changes) {
                int slice=Array.IndexOf(sources,c.renderer.sharedMaterial);
                string key=c.before.GetInstanceID()+":"+slice;
                if(!meshCache.TryGetValue(key,out var mesh)) {
                    mesh=UnityEngine.Object.Instantiate(c.before);
                    mesh.name=c.before.name+"_Atlas_"+slice;
                    var uv=mesh.uv;
                    var scale=sources[slice].GetTextureScale("_BaseMap");
                    var offset=sources[slice].GetTextureOffset("_BaseMap");
                    for(int i=0;i<uv.Length;i++)
                        uv[i]=Vector2.Scale(uv[i],scale)+offset+new Vector2(slice*32+16,0);
                    mesh.uv=uv;
                    AssetDatabase.CreateAsset(mesh,Output+"/Meshes/Road_"+meshCache.Count.ToString("D3")+".asset");
                    meshCache.Add(key,mesh);
                }
                c.after=mesh; c.filter.sharedMesh=mesh;
                c.renderer.sharedMaterials=new[]{material};
                PrefabUtility.RecordPrefabInstancePropertyModifications(c.filter);
                PrefabUtility.RecordPrefabInstancePropertyModifications(c.renderer);
            }
            AssetDatabase.SaveAssets();
            double worst=0;
            for(int i=0;i<poses.Count;i++) {
                var after=Capture(poses[i],Backup+"/After-"+i+".png");
                var a=before[i].GetPixels32();var b=after.GetPixels32();
                double sum=0;int changed=0;
                for(int j=0;j<a.Length;j++) {
                    int d=Math.Abs(a[j].r-b[j].r)+Math.Abs(a[j].g-b[j].g)+Math.Abs(a[j].b-b[j].b);
                    sum+=d;if(d>24)changed++;
                }
                double mae=sum/(a.Length*765.0);worst=Math.Max(worst,mae);
                log.AppendLine("View "+i+": mean RGB difference="+mae.ToString("F6")+", pixels differing by >8/255 average="+(100.0*changed/a.Length).ToString("F3")+"%");
                UnityEngine.Object.DestroyImmediate(before[i]);UnityEngine.Object.DestroyImmediate(after);
            }
            var errors=ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity.ToString()=="Error").ToArray();
            if(errors.Length>0) throw new Exception(string.Join("\n",errors.Select(e=>e.message)));
            if(worst>0.015) throw new Exception("Visual comparison exceeded 1.5% mean difference; scene not saved.");
            foreach(var c in changes) {
                if(c.after.vertexCount!=c.before.vertexCount || c.after.triangles.Length!=c.before.triangles.Length ||
                   c.after.bounds!=c.before.bounds || !c.after.uv2.SequenceEqual(c.before.uv2))
                    throw new Exception("Geometry/lightmap validation failed.");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene)) throw new Exception("Scene save failed.");
            saved=true;
            log.AppendLine("SUCCESS");
            log.AppendLine("Road renderers converted: "+changes.Count);
            log.AppendLine("Road materials: 5 -> 1. New meshes: "+meshCache.Count);
            log.AppendLine("Vertices and triangle counts unchanged; UV2, bounds, transforms, colliders and static flags preserved.");
            log.AppendLine("Original prefab and source assets retained; only scene mesh/material overrides changed.");
            log.AppendLine("Draw-call/FPS improvement requires target-device profiling; renderer count unchanged.");
            log.AppendLine("Texture arrays require GLES3/Metal/Vulkan/D3D11 or better. Current imported formats are recorded above.");
            log.AppendLine("To restore: close the scene and replace it with RoadAtlasBackup/Gangstar City.unity.");
            Selection.activeObject=material;
            Debug.Log("Road atlas conversion succeeded for "+changes.Count+" road sections.");
        } catch(Exception e) {
            log.AppendLine("FAILED: "+e);
            if(!saved) foreach(var c in changes) {
                if(c.after && c.filter) { c.filter.sharedMesh=c.before; PrefabUtility.RecordPrefabInstancePropertyModifications(c.filter); }
                if(c.after && c.renderer) { c.renderer.sharedMaterials=c.materials; PrefabUtility.RecordPrefabInstancePropertyModifications(c.renderer); }
            }
            Debug.LogException(e);
        } finally {
            ShaderUtil.allowAsyncCompilation=asyncBefore;
            File.WriteAllText(Report,log.ToString());
        }
    }
    static void MakeArray(Texture2D[] textures,string path,StringBuilder log)
    {
        if(AssetDatabase.LoadAssetAtPath<Texture2DArray>(path)) {log.AppendLine("Reuse validated array: "+path); return;}
        var first=textures[0];
        var targetFormat=first.format;
        log.AppendLine(path+": "+first.width+"x"+first.height+"x"+textures.Length+", "+first.format+", "+first.mipmapCount+" mip levels");
        if(textures.Any(t=>!t || t.width!=first.width || t.height!=first.height || t.mipmapCount!=first.mipmapCount))
            throw new Exception("Array sources differ in size or format: "+string.Join(", ",textures.Select(t=>t.name+" "+t.format)));
        var sourcePaths=textures.Select(AssetDatabase.GetAssetPath).ToArray();
        var readable=sourcePaths.Select(p=>((TextureImporter)AssetImporter.GetAtPath(p)).isReadable).ToArray();
        Texture2DArray array=null;
        try {
            bool srgb=((TextureImporter)AssetImporter.GetAtPath(sourcePaths[0])).sRGBTexture;

            array=new Texture2DArray(first.width,first.height,textures.Length,first.format,first.mipmapCount,!srgb);
            array.name=Path.GetFileNameWithoutExtension(path);
            array.wrapMode=TextureWrapMode.Repeat;
            array.filterMode=FilterMode.Trilinear;array.anisoLevel=4;
            for(int slice=0;slice<sourcePaths.Length;slice++) {
                var importer=(TextureImporter)AssetImporter.GetAtPath(sourcePaths[slice]);
                if(!importer.isReadable) {importer.isReadable=true;importer.SaveAndReimport();}
                var source=AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePaths[slice]);
                Texture2D converted=null;
                try {
                    bool sourceSRGB=((TextureImporter)AssetImporter.GetAtPath(sourcePaths[slice])).sRGBTexture;
                    if(source.format!=targetFormat || sourceSRGB!=srgb) {
                        converted=new Texture2D(source.width,source.height,TextureFormat.RGBA32,source.mipmapCount,!srgb);
                        for(int mip=0;mip<source.mipmapCount;mip++) {
                            var pixels=source.GetPixels(mip);
                            if(sourceSRGB!=srgb) for(int p=0;p<pixels.Length;p++) pixels[p]=sourceSRGB?pixels[p].linear:pixels[p].gamma;
                            converted.SetPixels(pixels,mip);
                        }
                        converted.Apply(false);
                        EditorUtility.CompressTexture(converted,targetFormat,TextureCompressionQuality.Best);
                        log.AppendLine("Surface slice normalized: "+source.name+" "+source.format+" -> "+targetFormat);
                        source=converted;
                    }
                    for(int mip=0;mip<source.mipmapCount;mip++)
                        array.SetPixelData(source.GetPixelData<byte>(mip),mip,slice);
                } finally {if(converted) UnityEngine.Object.DestroyImmediate(converted);}
            }
            // Exact imported compressed blocks and mip chains: no resampling or recompression.
            array.Apply(false,true);
            AssetDatabase.CreateAsset(array,path);array=null;
        } finally {
            for(int i=0;i<sourcePaths.Length;i++) {
                var importer=(TextureImporter)AssetImporter.GetAtPath(sourcePaths[i]);
                if(importer.isReadable!=readable[i]) {importer.isReadable=readable[i];importer.SaveAndReimport();}
            }
            if(array) UnityEngine.Object.DestroyImmediate(array);
        }
    }
    static Texture2D Capture(Tuple<Vector3,Quaternion> pose,string path)
    {
        var go=new GameObject("Road atlas validation camera"){hideFlags=HideFlags.HideAndDontSave};
        var camera=go.AddComponent<Camera>();
        var template=SceneView.lastActiveSceneView ? SceneView.lastActiveSceneView.camera : Camera.main;
        if(template) camera.CopyFrom(template);
        camera.enabled=false;
        camera.transform.SetPositionAndRotation(pose.Item1,pose.Item2);
        camera.cameraType=CameraType.Game;camera.aspect=1.6f;
        var data=go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing=false;data.antialiasing=AntialiasingMode.None;
        var rt=new RenderTexture(960,600,24,RenderTextureFormat.ARGB32);
        rt.Create();camera.targetTexture=rt;
        var old=RenderTexture.active;
        try {
            camera.Render();
            RenderTexture.active=rt;
            var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();
            File.WriteAllBytes(path,image.EncodeToPNG());return image;
        } finally {
            RenderTexture.active=old;camera.targetTexture=null;rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(go);
        }
    }
}

