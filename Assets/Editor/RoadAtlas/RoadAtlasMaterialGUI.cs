using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Source references live in an Editor-only asset. The runtime material references
// only packed atlases and scalar controls, so it doesn't load source textures twice.
public class RoadAtlasMaterialGUI : ShaderGUI {
    static readonly string[] Names={"Road","Crossing","Median","Pavement","Parking"};
    static double bakeAt;
    static Material queued;
    static RoadAtlasSources queuedSources;
    static bool subscribed;
    static bool surfaceOpen;
    static readonly System.Collections.Generic.Dictionary<Material,string> LastPacked=new System.Collections.Generic.Dictionary<Material,string>();
    public static void RecordBake(Material m,string hash) {LastPacked[m]=hash;}
    public static RoadAtlasSources Sources(Material material) {
        string id=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material));
        if(string.IsNullOrEmpty(id)) return null;
        const string dir="Assets/Editor/RoadAtlas/Settings";
        string path=dir+"/"+id+".asset";
        var settings=AssetDatabase.LoadAssetAtPath<RoadAtlasSources>(path);
        if(settings) return settings;
        Directory.CreateDirectory(dir);
        settings=ScriptableObject.CreateInstance<RoadAtlasSources>();
        for(int i=0;i<5;i++) {
            var m=AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/3D/Fbx/Materials/Road 0"+(i+1)+".mat");
            if(m) {settings.colors[i]=m.GetTexture("_BaseMap") as Texture2D;settings.surfaces[i]=m.GetTexture("_MetallicGlossMap") as Texture2D;}
        }
        AssetDatabase.CreateAsset(settings,path);
        return settings;
    }
    public override void OnGUI(MaterialEditor editor,MaterialProperty[] properties) {
        var material=(Material)editor.target;
        if(editor.targets.Length!=1) {EditorGUILayout.HelpBox("Select one road material to edit its texture slots.",MessageType.Info);return;}
        var settings=Sources(material);
        if(!settings) {EditorGUILayout.HelpBox("Save this material as an asset to edit its road textures.",MessageType.Info);return;}
        if(!LastPacked.ContainsKey(material)) LastPacked[material]=settings.bakedFingerprint;
        if(LastPacked[material]!=RoadAtlasTextureBaker.Fingerprint(settings) && !subscribed && !RoadAtlasEditableValidation.Running) Queue(material,settings);
        EditorGUILayout.LabelField("Road Surfaces",EditorStyles.boldLabel);
        EditorGUILayout.LabelField("One shared URP material",EditorStyles.miniLabel);
        for(int i=0;i<5;i++) {
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(Names[i]+" - Original Texture",EditorStyles.wordWrappedLabel,GUILayout.MinHeight(64));
            EditorGUI.BeginChangeCheck();
            var texture=(Texture2D)EditorGUILayout.ObjectField(settings.colors[i],typeof(Texture2D),false,GUILayout.Width(68),GUILayout.Height(68));
            if(EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(settings,"Change "+Names[i]+" texture");
                settings.colors[i]=texture;settings.pending=true;EditorUtility.SetDirty(settings);Queue(material,settings);
            }
            EditorGUILayout.EndHorizontal();
            var st=FindProperty("_Road"+i+"ST",properties);
            EditorGUI.BeginChangeCheck();
            var tiling=EditorGUILayout.Vector2Field("Tiling",new Vector2(st.vectorValue.x,st.vectorValue.y));
            var offset=EditorGUILayout.Vector2Field("Offset",new Vector2(st.vectorValue.z,st.vectorValue.w));
            if(EditorGUI.EndChangeCheck()) {editor.RegisterPropertyChangeUndo(Names[i]+" mapping");st.vectorValue=new Vector4(tiling.x,tiling.y,offset.x,offset.y);}
        }
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Surface Appearance",EditorStyles.boldLabel);
        for(int i=0;i<5;i++) {
            editor.ColorProperty(FindProperty("_Road"+i+"Tint",properties),Names[i]+" Tint");
            editor.RangeProperty(FindProperty("_Road"+i+"Smoothness",properties),Names[i]+" Shininess");
            editor.RangeProperty(FindProperty("_Road"+i+"Metallic",properties),Names[i]+" Metallic");
            editor.RangeProperty(FindProperty("_Road"+i+"Emission",properties),Names[i]+" Emission");
        }
        surfaceOpen=EditorGUILayout.Foldout(surfaceOpen,"Surface Maps",true);
        if(surfaceOpen) for(int i=0;i<5;i++) {
            EditorGUI.BeginChangeCheck();
            var texture=(Texture2D)EditorGUILayout.ObjectField(Names[i]+" - Metallic / Smoothness",settings.surfaces[i],typeof(Texture2D),false);
            if(EditorGUI.EndChangeCheck()) {Undo.RecordObject(settings,"Change road surface map");settings.surfaces[i]=texture;settings.pending=true;EditorUtility.SetDirty(settings);Queue(material,settings);}
        }
        EditorGUILayout.Space();
        editor.ShaderProperty(FindProperty("_EnvironmentReflections",properties),"Environment Reflections");
        if(material.GetFloat("_EnvironmentReflections")==0) material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        else material.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        if(settings.pending) EditorGUILayout.HelpBox("Updating road textures...",MessageType.Info);
        if(GUILayout.Button("Apply Texture Changes")) {try {RoadAtlasTextureBaker.Bake(material,settings);}catch(Exception e){Debug.LogException(e);}}
        EditorGUILayout.LabelField("Texture changes are packed automatically. Tiling, tint and shininess update immediately.",EditorStyles.wordWrappedMiniLabel);
    }
    static void Queue(Material m,RoadAtlasSources settings) {
        queued=m;queuedSources=settings;bakeAt=EditorApplication.timeSinceStartup+0.8;
        if(!subscribed) {EditorApplication.update+=Update;subscribed=true;}
    }
    static void Update() {
        if(EditorApplication.timeSinceStartup<bakeAt || EditorApplication.isCompiling || EditorApplication.isUpdating || RoadAtlasEditableValidation.Running) return;
        EditorApplication.update-=Update;subscribed=false;
        if(!queued || !queuedSources) return;
        try {RoadAtlasTextureBaker.Bake(queued,queuedSources);} catch(Exception e){Debug.LogException(e);}
        queued=null;queuedSources=null;
    }
}
public static class RoadAtlasTextureBaker {
    const int Size=1024;
    public static string Fingerprint(RoadAtlasSources s) {
        return string.Join("|",s.colors.Concat(s.surfaces).Select(t=>t?AssetDatabase.GetAssetPath(t)+":"+t.name+":"+AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(t)).ToString():"none"));
    }
    public static void Bake(Material material,RoadAtlasSources settings) {
        if(settings.colors.Length!=5 || settings.surfaces.Length!=5) throw new Exception("Five road surface slots are required.");
        string folder=Path.GetDirectoryName(AssetDatabase.GetAssetPath(material)).Replace('\\','/');
        // Material-specific names keep duplicated materials independent.
        string id=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material));
        string colorPath=folder+"/Road Colors "+id+".png";
        string surfacePath=folder+"/Road Surfaces "+id+".png";
        var surfaces=settings.surfaces.Distinct().ToArray();
        Pack(settings.colors,colorPath,true);
        Pack(surfaces,surfacePath,false);
        Undo.RecordObject(material,"Update road atlas");
        material.SetTexture("_RoadBaseArray",AssetDatabase.LoadAssetAtPath<Texture2DArray>(colorPath));
        material.SetTexture("_RoadSurfaceArray",AssetDatabase.LoadAssetAtPath<Texture2DArray>(surfacePath));
        for(int i=0;i<5;i++) material.SetFloat("_Road"+i+"SurfaceSlice",Array.IndexOf(surfaces,settings.surfaces[i]));
        settings.bakedFingerprint=Fingerprint(settings);
        RoadAtlasMaterialGUI.RecordBake(material,settings.bakedFingerprint);
        settings.pending=false;EditorUtility.SetDirty(settings);EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();SceneView.RepaintAll();
    }
    static void Pack(Texture2D[] sources,string path,bool srgb) {
        var strip=new Texture2D(Size*sources.Length,Size,TextureFormat.RGBA32,false,!srgb);
        var oldRT=RenderTexture.active;bool oldSRGB=GL.sRGBWrite;
        var rt=RenderTexture.GetTemporary(Size,Size,0,RenderTextureFormat.ARGB32,srgb?RenderTextureReadWrite.sRGB:RenderTextureReadWrite.Linear);
        try {
            for(int i=0;i<sources.Length;i++) {
                GL.sRGBWrite=srgb;
                // sRGB source maps are decoded by the GPU; linear surface output
                // therefore preserves the original shader's sampled values.
                Graphics.Blit(sources[i] ? sources[i] : (srgb?Texture2D.whiteTexture:Texture2D.blackTexture),rt);
                RenderTexture.active=rt;
                strip.ReadPixels(new Rect(0,0,Size,Size),i*Size,0,false);
            }
            strip.Apply(false);
            File.WriteAllBytes(path,strip.EncodeToPNG());
        } finally {GL.sRGBWrite=oldSRGB;RenderTexture.active=oldRT;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(strip);}
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        var ts=new TextureImporterSettings();importer.ReadTextureSettings(ts);
        ts.textureShape=TextureImporterShape.Texture2DArray;
        ts.flipbookColumns=sources.Length;ts.flipbookRows=1;
        importer.SetTextureSettings(ts);
        importer.sRGBTexture=srgb;importer.mipmapEnabled=true;importer.isReadable=false;
        importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=8192;
        importer.wrapMode=TextureWrapMode.Repeat;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=4;
        importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.compressionQuality=100;
        importer.alphaSource=srgb?TextureImporterAlphaSource.None:TextureImporterAlphaSource.FromInput;
        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name="Android",overridden=true,maxTextureSize=8192,format=TextureImporterFormat.ASTC_4x4,compressionQuality=100});
        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name="iPhone",overridden=true,maxTextureSize=8192,format=TextureImporterFormat.ASTC_4x4,compressionQuality=100});
        importer.SaveAndReimport();
        var array=AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
        if(!array || array.width!=Size || array.height!=Size || array.depth!=sources.Length)
            throw new Exception("Unexpected imported atlas dimensions: "+path);
    }
}

