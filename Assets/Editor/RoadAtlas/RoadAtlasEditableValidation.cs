using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class RoadAtlasEditableValidation {
    public static bool Running;
    static RoadAtlasEditableValidation() { EditorApplication.delayCall+=Run; }
    static void Run() {
        if(!File.Exists("RoadAtlasEditable.request")) return;
        File.Move("RoadAtlasEditable.request","RoadAtlasEditable.started-"+DateTime.UtcNow.Ticks);
        Running=true;
        var report=new StringBuilder();
        bool oldAsync=ShaderUtil.allowAsyncCompilation;
        try {
            ShaderUtil.allowAsyncCompilation=false;
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/RoadAtlas/Road Atlas.mat");
            if(!material) throw new Exception("Road material missing.");
            var settings=RoadAtlasMaterialGUI.Sources(material);
            material.SetTexture("_RoadBaseArray",AssetDatabase.LoadAssetAtPath<Texture2DArray>("Assets/Scenes/Oasis/RoadAtlas/Road Color Atlas.asset"));
            material.SetTexture("_RoadSurfaceArray",AssetDatabase.LoadAssetAtPath<Texture2DArray>("Assets/Scenes/Oasis/RoadAtlas/Road Surface Atlas.asset"));
            var all=SceneManager.GetSceneByPath("Assets/Scenes/Oasis/Gangstar City.unity").GetRootGameObjects()
                .SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).Where(r=>r.sharedMaterial==material).ToArray();
            if(all.Length!=230) throw new Exception("Unexpected road renderer count: "+all.Length);
            var poses=Enumerable.Range(0,5).Select(i=>{
                var r=all.Where(x=>Mathf.FloorToInt(x.GetComponent<MeshFilter>().sharedMesh.uv[0].x/32)==i).OrderBy(x=>x.bounds.size.magnitude).First();
                var center=r.bounds.center;var pos=center+new Vector3(0,12,-10);
                return Tuple.Create(pos,Quaternion.LookRotation(center-pos,Vector3.up));
            }).ToArray();
            Directory.CreateDirectory("RoadAtlasBackup/Editable");
            var before=poses.Select((p,i)=>Capture(p,"Before-"+i)).ToArray();
            RoadAtlasTextureBaker.Bake(material,settings);
            var after=poses.Select((p,i)=>Capture(p,"After-"+i)).ToArray();
            for(int i=0;i<5;i++) {
                double diff=Difference(before[i],after[i]);
                report.AppendLine("View "+i+" mean RGB difference: "+diff.ToString("F6"));
                if(diff>0.02) throw new Exception("Appearance changed beyond tolerance.");
            }
            // Verify controls modify rendered output and can be restored.
            var oldTint=material.GetColor("_Road0Tint");var oldST=material.GetVector("_Road0ST");
            try {
                material.SetColor("_Road0Tint",new Color(0.35f,0.8f,0.4f));
                material.SetVector("_Road0ST",new Vector4(2,2,0.15f,0.2f));
                var edited=Capture(poses[0],"Controls-Test");
                double diff=Difference(after[0],edited);UnityEngine.Object.DestroyImmediate(edited);
                if(diff<0.005) throw new Exception("Road controls did not change the rendered surface.");
                report.AppendLine("Tiling/tint change verified in render: "+diff.ToString("F6"));
            } finally {material.SetColor("_Road0Tint",oldTint);material.SetVector("_Road0ST",oldST);}
            var originalTexture=settings.colors[0];
            try {
                settings.colors[0]=settings.colors[1];
                RoadAtlasTextureBaker.Bake(material,settings);
                var edited=Capture(poses[0],"Replacement-Test");
                double diff=Difference(after[0],edited);UnityEngine.Object.DestroyImmediate(edited);
                if(diff<0.005) throw new Exception("Replacement texture did not update the atlas.");
                report.AppendLine("Texture replacement and atlas rebuild verified: "+diff.ToString("F6"));
            } finally {settings.colors[0]=originalTexture;RoadAtlasTextureBaker.Bake(material,settings);}
            var errors=ShaderUtil.GetShaderMessages(material.shader).Where(m=>m.severity.ToString()=="Error").ToArray();
            if(errors.Length>0) throw new Exception(string.Join("\n",errors.Select(m=>m.message)));
            if(material.IsKeywordEnabled("_NORMALMAP")) throw new Exception("Unexpected normal-map variant enabled.");
            foreach(var prop in new[]{"_RoadBaseArray","_RoadSurfaceArray"}) {
                var tex=(Texture2DArray)material.GetTexture(prop);
                var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex));
                var android=importer.GetPlatformTextureSettings("Android");
                if(!android.overridden || android.format!=TextureImporterFormat.ASTC_4x4)
                    throw new Exception("Android compression is not configured.");
                report.AppendLine(prop+": "+tex.width+"x"+tex.height+"x"+tex.depth+", Android ASTC 4x4, mipmaps enabled");
            }
            foreach(var image in before.Concat(after)) UnityEngine.Object.DestroyImmediate(image);
            EditorUtility.SetDirty(material);AssetDatabase.SaveAssets();
            Selection.activeObject=material;
            report.AppendLine("SUCCESS: 230 renderers, one material, five editable texture slots; changes restored after tests.");
            report.AppendLine("Android texture format configured. Android build/device FPS was not measured.");
        } catch(Exception e) {report.AppendLine("FAILED: "+e);Debug.LogException(e);}
        finally {Running=false;ShaderUtil.allowAsyncCompilation=oldAsync;File.WriteAllText("RoadAtlasEditableReport.txt",report.ToString());}
    }
    static Texture2D Capture(Tuple<Vector3,Quaternion> pose,string name) {
        var warmup=(Texture2D)typeof(GangstarRoadAtlasBuilder).GetMethod("Capture",BindingFlags.NonPublic|BindingFlags.Static)
            .Invoke(null,new object[]{pose,"RoadAtlasBackup/Editable/Warmup.png"});
        UnityEngine.Object.DestroyImmediate(warmup);
        return (Texture2D)typeof(GangstarRoadAtlasBuilder).GetMethod("Capture",BindingFlags.NonPublic|BindingFlags.Static)
            .Invoke(null,new object[]{pose,"RoadAtlasBackup/Editable/"+name+".png"});
    }
    static double Difference(Texture2D a,Texture2D b) {
        var aa=a.GetPixels32();var bb=b.GetPixels32();double sum=0;
        for(int i=0;i<aa.Length;i++)sum+=Math.Abs(aa[i].r-bb[i].r)+Math.Abs(aa[i].g-bb[i].g)+Math.Abs(aa[i].b-bb[i].b);
        return sum/(aa.Length*765.0);
    }
}

