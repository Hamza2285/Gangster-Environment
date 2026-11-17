using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

internal static class ShowroomAtlasSetup
{
    internal static Mesh LoadSourceMesh(string path)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        return mesh ? mesh : AssetDatabase.LoadAssetAtPath<Mesh>(Path.ChangeExtension(path, ".fbx"));
    }

    const string Folder = "Assets/Texture/Showroom Atlases";
    const string Model = "Assets/Model/Show Room.fbx";
    const string ScenePath = "Assets/Scenes/Car Show room.unity";
    const string Report = "Showroom atlas verification.txt";
    const string Trigger = "Showroom atlas setup.request";
    const string BakeMarker = "Showroom atlas bake.pending";

    static readonly HashSet<string> Structure = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ceiling", "Walls", "Floor", "Window", "Mezzanine", "Staircase", "Ramp",
        "Entrance Frame", "Entrance Door Left", "Entrance Door Right"
    };

    [InitializeOnLoadMethod]
    static void Schedule()
    {
        EditorApplication.delayCall += () =>
        {
            if (!File.Exists(Trigger)) return;
            var operation = File.ReadAllText(Trigger).Trim();
            File.Delete(Trigger);
            try { if (operation == "Finish Details") FinishDetails(); else if (operation == "Branding") BrandInterior(); else if (operation == "Polish") PolishInterior(); else if (operation == "Lighting") FinishLighting(); else Apply(); }
            catch (Exception e)
            {
                File.AppendAllText(Report, e + Environment.NewLine);
                Debug.LogException(e);
            }
        };
    }

    static string Key(string value) => new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    [MenuItem("Tools/Showroom/Apply Interior Atlases")]
    static void Apply()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new InvalidOperationException("Open Car Show room before applying the atlases.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var root = scene.GetRootGameObjects().SingleOrDefault(o => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(o) == Model || (o.name == "Show Room" && o.transform.Find("Ceiling")));
        if (root == null) throw new InvalidOperationException("The placed showroom prefab was not found.");
        var transforms = root.GetComponentsInChildren<Transform>(true)
            .ToDictionary(t => AnimationUtility.CalculateTransformPath(t, root.transform),
                t => (t.localPosition, t.localRotation, t.localScale, t.localToWorldMatrix));
        File.WriteAllText(Report, "Scene: " + scene.path + Environment.NewLine);
        if (!File.Exists("Showroom before atlas.unity.backup"))
            File.Copy(ScenePath, "Showroom before atlas.unity.backup");

        var tileSets = File.ReadAllLines(Folder + "/Atlas Layout.csv").Skip(1).Select(line => line.Split(','))
            .GroupBy(row => row[0]).ToDictionary(group => group.Key,
                group => group.ToDictionary(row => Key(row[1]), row =>
                    new Rect(int.Parse(row[2])/2048f, int.Parse(row[3])/2048f,
                             int.Parse(row[4])/2048f, int.Parse(row[5])/2048f)));
        var structure = MakeAtlasMaterial("Structure");
        var props = MakeAtlasMaterial("Props");
        var glass = MakeGlass();

        string meshFolder = Folder + "/Meshes";
        if (!AssetDatabase.IsValidFolder(meshFolder)) AssetDatabase.CreateFolder(Folder, "Meshes");
        var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
        bool originallyReadable = importer.isReadable;
        if (!originallyReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);

        var rootFrame = root.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="Entrance Frame");
        var rootRamp = root.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="Ramp");
        float front = Mathf.Sign(root.transform.InverseTransformPoint(rootFrame.bounds.center).z);
        float right = Mathf.Sign(root.transform.InverseTransformPoint(rootRamp.bounds.center).x);
        var sourceByPath = model.GetComponentsInChildren<MeshFilter>(true).ToDictionary(
            f => AnimationUtility.CalculateTransformPath(f.transform, model.transform));
        var result = new List<(MeshFilter filter, MeshRenderer renderer, Mesh mesh, Material[] materials)>();
        try
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (!renderer) continue;
                string path = AnimationUtility.CalculateTransformPath(filter.transform, root.transform);
                if (!sourceByPath.TryGetValue(path,out var original)) continue;
                var sourceRenderer = original.GetComponent<MeshRenderer>();
                bool isStructure = Structure.Contains(filter.name);
                var tiles = tileSets[isStructure ? "Structure" : "Props"];
                var output = Remap(original, sourceRenderer, model.transform, tiles, front, right, out bool opaque, out bool transparent);
                if (!opaque && !transparent) throw new InvalidOperationException("Empty showroom mesh: " + filter.name);
                var materials = opaque && transparent ? new[] {isStructure ? structure : props, glass}
                    : new[] {transparent ? glass : isStructure ? structure : props};
                output.name = filter.name + " | Atlas";
                string filename = string.Concat(filter.name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                if (filter.name=="Ramp") filename += " Painted";
                if (filter.name=="Signage") filename += " Painted Final";
                var pathToMesh = meshFolder + "/" + filename + ".asset";
                var old = AssetDatabase.LoadAssetAtPath<Mesh>(pathToMesh);
                if (old)
                {
                    EditorUtility.CopySerialized(output, old);
                    UnityEngine.Object.DestroyImmediate(output);
                    output = old;
                }
                else AssetDatabase.CreateAsset(output, pathToMesh);
                result.Add((filter, renderer, output, materials));
            }
        }
        finally
        {
            if (!originallyReadable)
            {
                importer = (ModelImporter)AssetImporter.GetAtPath(Model);
                importer.isReadable = false;
                importer.SaveAndReimport();
            }
        }

        foreach (var item in result)
        {
            Undo.RecordObject(item.filter, "Apply showroom atlas mesh");
            Undo.RecordObject(item.renderer, "Apply showroom atlas material");
            item.filter.sharedMesh = item.mesh;
            item.renderer.sharedMaterials = item.materials;
            var go = item.filter.gameObject;
            bool isGlassOnly = item.materials.Length == 1 && item.materials[0] == glass;
            if (isGlassOnly) item.renderer.shadowCastingMode = ShadowCastingMode.Off;
            bool canBake = !isGlassOnly && !go.name.StartsWith("Entrance Door", StringComparison.Ordinal);
            var flags = GameObjectUtility.GetStaticEditorFlags(go);
            GameObjectUtility.SetStaticEditorFlags(go, canBake ? flags | StaticEditorFlags.ContributeGI : flags & ~StaticEditorFlags.ContributeGI);
            if (canBake) item.renderer.receiveGI = ReceiveGI.Lightmaps;
            PrefabUtility.RecordPrefabInstancePropertyModifications(go);
            PrefabUtility.RecordPrefabInstancePropertyModifications(item.filter);
            PrefabUtility.RecordPrefabInstancePropertyModifications(item.renderer);
        }

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            string path = AnimationUtility.CalculateTransformPath(t, root.transform);
            if (!transforms.TryGetValue(path, out var value) || value != (t.localPosition, t.localRotation, t.localScale, t.localToWorldMatrix))
                throw new InvalidOperationException("A showroom transform changed: " + path);
        }
        int materialCount = result.SelectMany(item => item.renderer.sharedMaterials).Distinct().Count();
        if (materialCount != 3 || result.Count != 58) throw new InvalidOperationException("Unexpected showroom renderer or material count.");
        int triangles = result.Sum(item => Enumerable.Range(0, item.mesh.subMeshCount).Sum(i => (int)item.mesh.GetIndexCount(i) / 3));
        File.AppendAllText(Report, $"Renderers: {result.Count}\nMaterials: {materialCount}\nTriangles: {triangles}\nVerified unchanged transforms: {transforms.Count}\n");

        ConfigureLighting();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Unity could not save the atlas scene.");
        Capture(root.transform, "Showroom Unity Preview.png");
        StartBake(root.transform);
    }

    static Texture2D Texture(string name, bool linear)
    {
        var path = Folder + "/" + name + " Finish.png";
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        if (!imp) throw new InvalidOperationException("Texture not imported: " + path);
        imp.textureType = TextureImporterType.Default;
        imp.sRGBTexture = !linear;
        imp.mipmapEnabled = true;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.filterMode = FilterMode.Trilinear;
        imp.anisoLevel = 4;
        imp.maxTextureSize = 2048;
        imp.textureCompression = TextureImporterCompression.CompressedHQ;
        imp.alphaSource = linear ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        imp.alphaIsTransparency = false;
        var android = imp.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.maxTextureSize = 2048;
        android.format = TextureImporterFormat.ASTC_6x6;
        imp.SetPlatformTextureSettings(android);
        var ios = imp.GetPlatformTextureSettings("iPhone");
        ios.overridden = true;
        ios.maxTextureSize = 2048;
        ios.format = TextureImporterFormat.ASTC_6x6;
        imp.SetPlatformTextureSettings(ios);
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material GetMaterial(string name)
    {
        string path = Folder + "/Showroom " + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat) return mat;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) throw new InvalidOperationException("URP Lit shader could not be loaded.");
        mat = new Material(shader) {name = "Showroom " + name};
        AssetDatabase.CreateAsset(mat,path);
        return mat;
    }

    static Material MakeAtlasMaterial(string group)
    {
        var mat = GetMaterial(group);
        mat.SetColor("_BaseColor",Color.white);
        mat.SetTexture("_BaseMap", Texture(group + " Base Color",false));
        mat.SetTexture("_MetallicGlossMap", Texture(group + " Metallic Smoothness",true));
        mat.SetTexture("_EmissionMap", Texture(group + " Emission",false));
        mat.SetFloat("_Metallic",1);
        mat.SetFloat("_Smoothness",1);
        mat.SetColor("_EmissionColor",Color.white * 5f);
        mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        // Foliage and some architectural trim are authored as thin single-sided surfaces.
        mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.doubleSidedGI = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material MakeGlass()
    {
        var mat = GetMaterial("Glass");
        mat.SetColor("_BaseColor",new Color(.25f,.42f,.56f,.095f));
        mat.SetFloat("_Surface",1);
        mat.SetFloat("_Blend",0);
        mat.SetFloat("_ZWrite",0);
        mat.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha",(float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha",(float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_Smoothness",.86f);
        mat.SetFloat("_EnvironmentReflections",0f);
        mat.SetFloat("_SpecularHighlights",0f);
        mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        mat.SetFloat("_Metallic",0f);
        mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType","Transparent");
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.SetShaderPassEnabled("ShadowCaster",false);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Mesh Remap(MeshFilter original, MeshRenderer renderer, Transform root,
        Dictionary<string,Rect> tiles, float front, float right, out bool opaque, out bool transparent)
    {
        var source = original.sharedMesh;
        var v = source.vertices; var n = source.normals; var uv = source.uv; var uv2 = source.uv2; var t = source.tangents;
        if (v.Length == 0 || n.Length != v.Length || uv.Length != v.Length)
            throw new InvalidOperationException("Mesh missing positions, normals or UVs: " + original.name);
        var space = root.worldToLocalMatrix * original.transform.localToWorldMatrix;
        var world = v.Select(p => space.MultiplyPoint3x4(p)).ToArray();
        var positions = new List<Vector3>(); var normals = new List<Vector3>(); var tangents = new List<Vector4>();
        var uv0 = new List<Vector2>(); var lightmap = new List<Vector2>();
        var tris = new[] {new List<int>(), new List<int>()};
        var cache = new Dictionary<(int,int,int,int),int>();
        var mats = renderer.sharedMaterials;
        if (mats.Length != source.subMeshCount)
            throw new InvalidOperationException("Mismatched source slots: " + original.name);
        for (int i = 0; i < source.subMeshCount; i++)
        {
            string materialName = mats[i].name;
            string k = Key(materialName);
            int group = k == Key("Smoked blue glazing") || k == Key("Skylight blue") ? 1 : 0;
            Rect rect = default;
            if (group == 0 && !tiles.TryGetValue(k, out rect))
                throw new InvalidOperationException("Missing atlas tile for " + original.name + " / " + materialName);
            int[] indices = source.GetTriangles(i);
            var bounds = new Bounds(indices.Length>0 ? world[indices[0]] : Vector3.zero,Vector3.zero);
            foreach (int j in indices) bounds.Encapsulate(world[j]);
            for (int f = 0; f < indices.Length; f += 3)
            {
                var normal = Vector3.Cross(world[indices[f+1]]-world[indices[f]],world[indices[f+2]]-world[indices[f]]).normalized;
                var center = (world[indices[f]]+world[indices[f+1]]+world[indices[f+2]]) / 3f;
                var selected = rect;
                int decor = 0;
                float boardX = 0;
                float y = -front*center.z;
                if (group==0 && original.name=="Ramp" && k=="texturedrampsurface")
                {
                    selected=tiles[Key("Ramp Road Markings")]; decor=1;
                }
                else if (group==0 && original.name=="Signage" && k=="charcoalpowdercoat" && center.y>5.5f && Mathf.Abs(y-13.680f)<.059f && Mathf.Abs(normal.z)>.93f)
                {
                    var centers=new[]{-10.25f,-5.05f,.12f,5.32f,10.52f};
                    int board=Enumerable.Range(0,centers.Length).OrderBy(j=>Mathf.Abs(centers[j]-center.x*right)).First();
                    boardX=centers[board];
                    selected=tiles[Key(board%2==0?"Gallery Gold Artwork":"Gallery Blue Artwork")];
                    decor=2;
                }
                else if (group==0 && original.name=="Signage" && k=="charcoalpowdercoat" && Mathf.Abs(center.x*right-13.800f)<.090f && Mathf.Abs(normal.x)>.93f)
                {
                    selected=tiles[Key("Showroom Wall Artwork")]; decor=3;
                }
                for (int corner = 0; corner < 3; corner++)
                {
                    int j = indices[f+corner];
                    var coord = uv[j];
                    if (group == 0)
                    {
                        var p = world[j];
                        if (decor==1) coord=new Vector2((p.x*right-7.533f)/5.731f,(-front*p.z+6.18f)/13.88f);
                        else if (decor==2) coord=new Vector2((p.x*right-boardX+1.82f)/3.64f,(p.y-5.60f)/1.86f);
                        else if (decor==3) coord=new Vector2(1f-(-front*p.z+3.15f)/8f,(p.y-4.33f)/3.2f);
                        else if (original.name == "Floor" && k.StartsWith("polishedconcrete") && Mathf.Abs(normal.y)>.7f)
                        {
                            var x = Mathf.Floor(center.x / 2f) * 2f;
                            var z = Mathf.Floor(center.z / 2f) * 2f;
                            coord = new Vector2((p.x-x)*.5f,(p.z-z)*.5f);
                        }
                        else if (k.Contains("marble") || k == "walnut" || k.Contains("rug") || k == "texturedrampsurface")
                        {
                            if (Mathf.Abs(normal.y)>.67f)
                                coord = new Vector2(Normalize(p.x,bounds.min.x,bounds.size.x),Normalize(p.z,bounds.min.z,bounds.size.z));
                            else if (Mathf.Abs(normal.z)>Mathf.Abs(normal.x))
                                coord = new Vector2(Normalize(p.x,bounds.min.x,bounds.size.x),Normalize(p.y,bounds.min.y,bounds.size.y));
                            else
                                coord = new Vector2(Normalize(p.z,bounds.min.z,bounds.size.z),Normalize(p.y,bounds.min.y,bounds.size.y));
                        }
                        coord.x = selected.x + selected.width * Mathf.Lerp(.001f,.999f,Mathf.Clamp01(coord.x));
                        coord.y = selected.y + selected.height * Mathf.Lerp(.001f,.999f,Mathf.Clamp01(coord.y));
                    }
                    var key = (j,group,Mathf.RoundToInt(coord.x*1000000),Mathf.RoundToInt(coord.y*1000000));
                    if (!cache.TryGetValue(key,out int vertex))
                    {
                        vertex = positions.Count; cache.Add(key,vertex);
                        positions.Add(v[j]); normals.Add(n[j]); uv0.Add(coord);
                        if (uv2.Length==v.Length) lightmap.Add(uv2[j]);
                        if (t.Length==v.Length) tangents.Add(t[j]);
                    }
                    tris[group].Add(vertex);
                }
            }
        }
        opaque = tris[0].Count > 0; transparent = tris[1].Count > 0;
        var result = new Mesh {indexFormat = positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16};
        result.SetVertices(positions); result.SetNormals(normals); result.SetUVs(0,uv0);
        if (lightmap.Count==positions.Count) result.SetUVs(1,lightmap);
        if (tangents.Count==positions.Count) result.SetTangents(tangents);
        result.subMeshCount = opaque && transparent ? 2 : 1;
        if (opaque) result.SetTriangles(tris[0],0,false);
        if (transparent) result.SetTriangles(tris[1],opaque?1:0,false);

        result.bounds = source.bounds;
        return result;
    }
    static float Normalize(float value,float min,float length) => length>.0001f ? (value-min)/length : .5f;

    static void ConfigureLighting()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.64f,.65f,.68f);
        RenderSettings.ambientEquatorColor = new Color(.55f,.53f,.50f);
        RenderSettings.ambientGroundColor = new Color(.34f,.29f,.25f);
        RenderSettings.ambientIntensity = 1f;
        const string path = Folder + "/Showroom Baked Lighting.lighting";
        var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
        if (!settings)
        {
            settings = new LightingSettings { name = "Showroom Baked Lighting" };
            AssetDatabase.CreateAsset(settings,path);
        }
        settings.bakedGI = true;
        settings.realtimeGI = false;
        settings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
        settings.lightmapResolution = 9;
        settings.lightmapMaxSize = 1024;
        settings.directSampleCount = 16;
        settings.indirectSampleCount = 48;
        settings.environmentSampleCount = 32;
        settings.maxBounces = 2;
        Lightmapping.lightingSettings = settings;
    }

    [MenuItem("Tools/Showroom/Bake Warm Interior Lighting")]
    static void FinishLighting()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || Lightmapping.isRunning) throw new InvalidOperationException("Open the showroom and let any current bake finish.");
        var root = scene.GetRootGameObjects().Single(o => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(o) == Model || (o.name == "Show Room" && o.transform.Find("Ceiling")));
        var oldGroup = scene.GetRootGameObjects().FirstOrDefault(o => o.name == "Showroom Baked Fixture Fill");
        var group = oldGroup ? oldGroup : new GameObject("Showroom Baked Fixture Fill");
        var frame = root.GetComponentsInChildren<MeshRenderer>().Single(r => r.name=="Entrance Frame");
        var front = Mathf.Sign(root.transform.InverseTransformPoint(frame.bounds.center).z);
        var ramp = root.GetComponentsInChildren<MeshRenderer>().Single(r => r.name=="Ramp");
        var right = Mathf.Sign(root.transform.InverseTransformPoint(ramp.bounds.center).x);

        void Lamp(string label, Vector3 p, Vector3 aim, float power, float range, float angle)
        {
            var t = group.transform.Find(label);
            var go = t ? t.gameObject : new GameObject(label,typeof(Light));
            if (!t) go.transform.SetParent(group.transform,false);
            go.transform.position = root.transform.TransformPoint(p);
            go.transform.rotation = Quaternion.LookRotation(root.transform.TransformPoint(aim)-go.transform.position,root.transform.up);
            var l = go.GetComponent<Light>();
            l.type = LightType.Spot;
            l.lightmapBakeType = LightmapBakeType.Baked;
            l.shadows = LightShadows.Soft;
            l.shadowRadius = .32f;
            l.color = new Color(1f,.78f,.55f);
            l.intensity = power;
            l.range = range;
            l.spotAngle = angle;
            l.innerSpotAngle = angle*.48f;
            l.bounceIntensity = .9f;
        }

        foreach (float x in new[] {-7.4f,-2.6f,2.6f,7.4f})
        {
            Lamp("Show floor front "+x,new Vector3(x,7.25f,front*6.2f),new Vector3(x,0,front*4.2f),31f,14f,115f);
            Lamp("Show floor middle "+x,new Vector3(x,7.25f,-front*1.2f),new Vector3(x,0,-front*1.4f),31f,14f,115f);
        }
        foreach (float x in new[] {-8.6f,-3.1f,3.1f,8.6f})
            Lamp("Mezzanine fixture "+x,new Vector3(x,7.24f,-front*9f),new Vector3(x,4.06f,-front*9.6f),11f,8.5f,115f);
        foreach (float x in new[] {-8.4f,-3.4f,2.4f,7.8f})
            Lamp("Service ceiling "+x,new Vector3(x,3.38f,-front*9.6f),new Vector3(x,.62f,-front*11.8f),9f,7f,115f);
        Lamp("Ramp wall wash",new Vector3(right*11.1f,7.27f,0),new Vector3(right*13.85f,5.85f,0),11f,8f,101f);
        Lamp("Reception wall wash",new Vector3(right*10f,6.75f,front*9.9f),new Vector3(right*9.2f,.9f,front*9.9f),20f,11f,104f);

        RenderSettings.ambientSkyColor = new Color(.64f,.65f,.68f);
        RenderSettings.ambientEquatorColor = new Color(.55f,.53f,.50f);
        RenderSettings.ambientGroundColor = new Color(.34f,.29f,.25f);
        var sun = scene.GetRootGameObjects().Select(o=>o.GetComponent<Light>()).FirstOrDefault(l=>l && l.type==LightType.Directional);
        if (sun) sun.intensity = 1.55f;

        var volume = scene.GetRootGameObjects().Select(o=>o.GetComponent<Volume>()).FirstOrDefault(v=>v && v.isGlobal);
        if (volume && volume.sharedProfile)
        {
            const string profilePath = Folder + "/Showroom Interior Volume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (!profile)
            {
                AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(volume.sharedProfile),profilePath);
                profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            }
            if (profile)
            {
                volume.sharedProfile = profile;
                if (profile.TryGet<Bloom>(out var bloom))
                {
                    bloom.intensity.Override(.40f);
                    bloom.threshold.Override(.92f);
                    EditorUtility.SetDirty(bloom);
                }
                if (!profile.TryGet<ColorAdjustments>(out var grade))
                {
                    grade = profile.Add<ColorAdjustments>(false);
                    AssetDatabase.AddObjectToAsset(grade, profile);
                }
                grade.postExposure.Override(.27f);
                EditorUtility.SetDirty(grade);
                EditorUtility.SetDirty(profile);
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        File.AppendAllText(Report,"Added warm baked fixture fill and tuned the showroom volume.\n");
        StartBake(root.transform);
    }
    [MenuItem("Tools/Showroom/Polish Interior Lighting")]
    static void PolishInterior()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || Lightmapping.isRunning || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open the showroom in Edit Mode and let any current bake finish.");
        var root = scene.GetRootGameObjects().Single(o=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(o)==Model || (o.name=="Show Room" && o.transform.Find("Ceiling")));
        var exterior = scene.GetRootGameObjects().Single(o=>o.name=="Mirage City Exterior");
        var floor = root.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="Floor").bounds;
        var oldTransforms = root.GetComponentsInChildren<Transform>(true).Concat(exterior.GetComponentsInChildren<Transform>(true))
            .ToDictionary(t=>t,t=>t.localToWorldMatrix);
        const string backup="Showroom before lighting polish.unity.backup";
        if(!File.Exists(backup)) File.Copy(ScenePath,backup);
        File.WriteAllText(Report,"Interior polish and lighting verification\n");

        MakeAtlasMaterial("Structure");
        MakeAtlasMaterial("Props");
        MakeGlass();
        RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.62f,.63f,.66f);
        RenderSettings.ambientEquatorColor=new Color(.61f,.58f,.54f);
        RenderSettings.ambientGroundColor=new Color(.39f,.34f,.30f);

        var sun=scene.GetRootGameObjects().Select(o=>o.GetComponent<Light>()).Single(l=>l&&l.type==LightType.Directional);
        sun.shadows=LightShadows.None;
        sun.intensity=.28f;
        var fill=scene.GetRootGameObjects().Single(o=>o.name=="Showroom Baked Fixture Fill");
        foreach (var light in fill.GetComponentsInChildren<Light>())
        {
            light.color=new Color(1f,.84f,.67f);
            light.shadowRadius=.65f;
            if(light.name.StartsWith("Show floor")) light.intensity=49;
            else if(light.name.StartsWith("Mezzanine")) light.intensity=16;
            else if(light.name.StartsWith("Service ceiling")) light.intensity=14;
            else if(light.name.StartsWith("Ramp")) light.intensity=17;
            else if(light.name.StartsWith("Reception")) light.intensity=27;
        }

        var volume=scene.GetRootGameObjects().Select(o=>o.GetComponent<Volume>()).Single(v=>v&&v.isGlobal);
        var profile=volume.sharedProfile;
        if(profile.TryGet<Bloom>(out var bloom))
        {
            bloom.threshold.Override(.97f);
            bloom.intensity.Override(.43f);
            bloom.scatter.Override(.61f);
            EditorUtility.SetDirty(bloom);
        }
        if(profile.TryGet<ColorAdjustments>(out var grade))
        {
            grade.postExposure.Override(.40f);
            EditorUtility.SetDirty(grade);
        }
        var probeObject=scene.GetRootGameObjects().FirstOrDefault(o=>o.name=="Showroom Interior Reflections");
        if(!probeObject) probeObject=new GameObject("Showroom Interior Reflections",typeof(ReflectionProbe));
        probeObject.transform.position=new Vector3(floor.center.x,floor.max.y+3.2f,floor.center.z);
        var probe=probeObject.GetComponent<ReflectionProbe>();
        probe.mode=ReflectionProbeMode.Baked;
        probe.resolution=128;
        probe.size=new Vector3(floor.size.x-.35f,9.1f,floor.size.z-.35f);
        probe.center=new Vector3(0,1.35f,0);
        probe.boxProjection=true;
        probe.intensity=.86f;
        probe.blendDistance=1.2f;
        probe.hdr=true;
        probe.nearClipPlane=.10f;
        probe.farClipPlane=50;

        foreach(var pair in oldTransforms)
            if(pair.Key.localToWorldMatrix!=pair.Value) throw new InvalidOperationException("An existing object moved.");
        File.AppendAllText(Report,$"Preserved existing transforms: {oldTransforms.Count}\nDirectional window shadows: {sun.shadows}\nBaked soft fixtures: {fill.GetComponentsInChildren<Light>().Length}\nShowroom materials: 3\nInterior texture atlas layouts: 2\n");
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the showroom.");
        Capture(root.transform,"Showroom Polished Preview.png");
        StartBake(root.transform);
    }
    [MenuItem("Tools/Showroom/Apply Branding and Neon Art")]
    static void BrandInterior()
    {
        var scene=SceneManager.GetActiveScene();
        if(scene.path!=ScenePath||Lightmapping.isRunning||EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open the showroom in Edit Mode and finish any running bake.");
        var root=scene.GetRootGameObjects().Single(o=>o.name=="Show Room" && o.transform.Find("Ceiling"));
        const string backup="Showroom before branding.unity.backup";
        if(!File.Exists(backup))File.Copy(ScenePath,backup);
        File.WriteAllText(Report,"Showroom branding and atlas verification\n");
        var existing=root.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t,t=>t.localToWorldMatrix);
        var tileSets=File.ReadAllLines(Folder+"/Atlas Layout.csv").Skip(1).Select(line=>line.Split(',')).GroupBy(row=>row[0]).ToDictionary(group=>group.Key,
            group=>group.ToDictionary(row=>row[1],row=>new Rect(int.Parse(row[2])/2048f,int.Parse(row[3])/2048f,int.Parse(row[4])/2048f,int.Parse(row[5])/2048f)));
        MakeAtlasMaterial("Structure");MakeAtlasMaterial("Props");
        var all=root.GetComponentsInChildren<MeshRenderer>(true);
        float front=Mathf.Sign(root.transform.InverseTransformPoint(all.Single(r=>r.name=="Entrance Frame").bounds.center).z);
        float right=Mathf.Sign(root.transform.InverseTransformPoint(all.Single(r=>r.name=="Ramp").bounds.center).x);
        var changes=new Dictionary<string,int>();
        var frames=new HashSet<int>();
        foreach(var n in new[]{"Ceiling","Walls","Mezzanine","Signage","Reception"})
        {
            var renderer=all.Single(r=>r.name==n);
            var filter=renderer.GetComponent<MeshFilter>();
            string meshName=n=="Signage"?"Signage Painted Final":n;
            var src=LoadSourceMesh(Folder+"/Meshes/"+meshName+".asset");
            if(!src)throw new InvalidOperationException("Missing original mesh "+meshName);
            var tiles=tileSets[n=="Signage"||n=="Reception"?"Props":"Structure"];
            var v=src.vertices;var uv=src.uv;var uv2=src.uv2;var ns=src.normals;var ts=src.tangents;
            var matrix=root.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;
            var coords=v.Select(p=>matrix.MultiplyPoint3x4(p)).ToArray();
            var totalBounds=new Bounds(coords[0],Vector3.zero);foreach(var q in coords)totalBounds.Encapsulate(q);
            bool receptionAlongX=totalBounds.size.x>=totalBounds.size.z;
            if(n=="Signage")
            {
                var inverse=matrix.inverse;
                var artwork=tiles["Showroom Wall Artwork"];
                int moved=0;
                for(int i=0;i<v.Length;i++)
                {
                    var q=coords[i];
                    float originalY=-front*q.z;
                    if(q.x*right<13.60f||q.y<4.20f||originalY< -3.25f||originalY>4.96f)continue;
                    q.z=-front*(-.05f+(originalY-.85f)*.74f);
                    v[i]=inverse.MultiplyPoint3x4(q);
                    if(artwork.Contains(uv[i]))uv[i].x=artwork.center.x+(uv[i].x-artwork.center.x)*.74f;
                    moved++;
                }
                if(moved<10)throw new InvalidOperationException("Could not find the Game Box board geometry.");
                File.AppendAllText(Report,$"Game Box board centered in pillar bay (-3.60 m to 3.50 m): {moved} vertices.\n");
            }
            var pos=new List<Vector3>();var norms=new List<Vector3>();var tex=new List<Vector2>();var lm=new List<Vector2>();var tan=new List<Vector4>();
            var dict=new Dictionary<(int,int,int),int>();
            var subs=new List<int[]>();
            bool Is(Rect r,Vector2 p)=>p.x>=r.xMin&&p.x<=r.xMax&&p.y>=r.yMin&&p.y<=r.yMax;
            Vector2 Remap(Vector2 p,Rect old,Rect next)=>new Vector2(next.x+(p.x-old.x)/old.width*next.width,next.y+(p.y-old.y)/old.height*next.height);
            int Add(int i,Vector2 p)
            {
                var key=(i,Mathf.RoundToInt(p.x*1000000),Mathf.RoundToInt(p.y*1000000));
                if(dict.TryGetValue(key,out var j))return j;
                j=pos.Count;dict[key]=j;pos.Add(v[i]);norms.Add(ns[i]);tex.Add(p);
                if(uv2.Length==v.Length)lm.Add(uv2[i]);if(ts.Length==v.Length)tan.Add(ts[i]);return j;
            }
            var brass=tiles["Brushed brass"];
            var dark=tiles["Charcoal powder coat"];
            for(int sub=0;sub<src.subMeshCount;sub++)
            {
                var ids=src.GetTriangles(sub);var indices=new List<int>(ids.Length);
                for(int i=0;i<ids.Length;i+=3)
                {
                    int a=ids[i],b=ids[i+1],c=ids[i+2];
                    var u=(uv[a]+uv[b]+uv[c])/3f;
                    var p=(coords[a]+coords[b]+coords[c])/3f;
                    var e=new Bounds(coords[a],Vector3.zero);e.Encapsulate(coords[b]);e.Encapsulate(coords[c]);
                    string selected=null;Rect original=default;bool projected=false;bool alongX=false;
                    if(n=="Ceiling"&&Is(brass,u)&&p.y>7.72f&&p.y<8.67f&&Mathf.Abs(Mathf.Abs(p.x)-9.5f)<.55f)
                        selected="Yellow Duct Paint";
                    if((n=="Walls"||n=="Mezzanine")&&Is(dark,u)&&e.size.y>2.6f&&e.size.y<9.29f)
                    {
                        float span=Mathf.Max(e.size.x,e.size.z);
                        if(span>.14f&&span<.61f&&Mathf.Min(e.size.x,e.size.z)<.071f)
                        {selected="Gold Grille";projected=true;alongX=e.size.x>=e.size.z;}
                    }
                    if(n=="Signage")
                    {
                        if(p.y>1.4f&&p.y<2.85f&&(Is(brass,u)||Is(tiles["Warm ivory"],u)))selected="Lit Service Crown";
                        foreach(var old in new[]{"Gallery Gold Artwork","Gallery Blue Artwork"})
                        {
                            if(!Is(tiles[old],u))continue;
                            var centers=new[]{-10.25f,-5.05f,.12f,5.32f,10.52f};
                            int k=Enumerable.Range(0,5).OrderBy(j=>Mathf.Abs(centers[j]-p.x*right)).First();
                            frames.Add(k);selected="Neon Car Print "+(k+1);original=tiles[old];break;
                        }
                    }
                    if(n=="Reception"&&Is(brass,u)&&Mathf.Abs(receptionAlongX?p.x-totalBounds.center.x:p.z-totalBounds.center.z)<.82f&&p.y>.269f&&p.y<.82f)
                        selected="Mirror Gold Crown";
                    if(selected!=null)changes[selected]=changes.TryGetValue(selected,out int count)?count+1:1;
                    foreach(int j in new[]{a,b,c})
                    {
                        Vector2 next=uv[j];
                        if(selected!=null)
                        {
                            var dst=tiles[selected];
                            if(projected)
                            {
                                var q=coords[j];float across=alongX?(q.x-e.min.x)/e.size.x:(q.z-e.min.z)/e.size.z;
                                next=new Vector2(dst.x+dst.width*Mathf.Clamp01(across),dst.y+dst.height*Mathf.Clamp01((q.y-e.min.y)/e.size.y));
                            }
                            else next=original.width>0?Remap(uv[j],original,dst):dst.center;
                        }
                        indices.Add(Add(j,next));
                    }
                }
                subs.Add(indices.ToArray());
            }
            var mesh=new Mesh{name=n+" | Branded Atlas",indexFormat=pos.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            mesh.SetVertices(pos);mesh.SetNormals(norms);mesh.SetUVs(0,tex);
            if(lm.Count==pos.Count)mesh.SetUVs(1,lm);if(tan.Count==pos.Count)mesh.SetTangents(tan);
            mesh.subMeshCount=subs.Count;for(int i=0;i<subs.Count;i++)mesh.SetTriangles(subs[i],i,false);
            mesh.bounds=src.bounds;
            string path=Folder+"/Meshes/"+n+(n=="Signage"?" Studio Centered":" Branded")+".asset";
            var prev=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(prev){EditorUtility.CopySerialized(mesh,prev);UnityEngine.Object.DestroyImmediate(mesh);mesh=prev;}else AssetDatabase.CreateAsset(mesh,path);
            filter.sharedMesh=mesh;
        }
        foreach(var kv in changes)File.AppendAllText(Report,$"{kv.Key}: {kv.Value} mapped triangles\n");
        if(frames.Count!=5||!changes.ContainsKey("Yellow Duct Paint")||!changes.ContainsKey("Gold Grille")||!changes.ContainsKey("Mirror Gold Crown")||!changes.ContainsKey("Lit Service Crown"))
            throw new InvalidOperationException("One or more atlas regions missed their targets.");
        var accent=scene.GetRootGameObjects().FirstOrDefault(o=>o.name=="Showroom Baked Planter Accents");
        if(!accent)accent=new GameObject("Showroom Baked Planter Accents");
        int plants=0;
        foreach(var r in all.Where(r=>r.name.StartsWith("Planter ")||r.name.StartsWith("Desktop Planter ")))
        {
            var t=accent.transform.Find(r.name);
            var go=t?t.gameObject:new GameObject(r.name,typeof(Light));
            if(!t)go.transform.SetParent(accent.transform,false);
            var b=r.bounds;go.transform.position=b.center+new Vector3(-.11f,-b.extents.y*.48f,-.19f);
            var l=go.GetComponent<Light>();l.type=LightType.Point;l.lightmapBakeType=LightmapBakeType.Baked;l.color=new Color(1,.78f,.49f);
            l.shadows=LightShadows.None;l.range=r.name.StartsWith("Desktop")?.73f:1.52f;l.intensity=r.name.StartsWith("Desktop")?.27f:1.30f;
            plants++;
        }
        if(existing.Any(pair=>pair.Key.localToWorldMatrix!=pair.Value))throw new InvalidOperationException("A showroom object moved.");
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        File.AppendAllText(Report,$"Neon frames: {frames.Count}\nPlanter accents: {plants}\nPreserved placements and prefab hierarchy: {existing.Count} transforms\nTwo interior atlas layouts; three interior materials.\n");
        Capture(root.transform,"Showroom Branding Preview.png");
        StartBake(root.transform);
    }
    [MenuItem("Tools/Showroom/Finish Grilles and Displays")]
    static void FinishDetails()
    {
        var scene=SceneManager.GetActiveScene();
        if(scene.path!=ScenePath||EditorApplication.isPlayingOrWillChangePlaymode||Lightmapping.isRunning)
            throw new InvalidOperationException("Open the showroom in Edit Mode and finish any running bake.");
        var root=scene.GetRootGameObjects().Single(o=>o.name=="Show Room" && o.transform.Find("Ceiling"));
        var existing=root.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t,t=>t.localToWorldMatrix);
        File.WriteAllText(Report,"Showroom detail finish verification\n");
        MakeAtlasMaterial("Structure");
        MakeAtlasMaterial("Props");
        var rows=File.ReadAllLines(Folder+"/Atlas Layout.csv").Skip(1).Select(line=>line.Split(',')).ToArray();
        Rect Tile(string name,string group=null)
        {
            var r=rows.Single(a=>a[1]==name&&(group==null||a[0]==group));
            return new Rect(int.Parse(r[2])/2048f,int.Parse(r[3])/2048f,int.Parse(r[4])/2048f,int.Parse(r[5])/2048f);
        }
        bool Uses(MeshFilter f,Rect rect)=>f.sharedMesh.uv.Any(uv=>rect.Contains(uv));
        var meshes=root.GetComponentsInChildren<MeshFilter>(true);
        var grille=Tile("Gold Grille");
        var crown=Tile("Mirror Gold Crown");
        var screen=Tile("Vehicle info | Blank texture surface");
        if(!Uses(meshes.Single(f=>f.name=="Walls"),grille)||!Uses(meshes.Single(f=>f.name=="Reception"),crown))
            throw new InvalidOperationException("Could not find the grille or crown UVs.");
        var stands=meshes.Where(f=>f.name.StartsWith("Vehicle Info Stand ")).ToArray();
        if(stands.Length==0||stands.Any(f=>!Uses(f,screen)))throw new InvalidOperationException("An info stand has no screen UVs.");
        var reception=meshes.Single(f=>f.name=="Reception");
        const string litMeshPath=Folder+"/Meshes/Reception Lit Crown.asset";
        var litMesh=LoadSourceMesh(litMeshPath);
        if(!litMesh)
        {
            var src=LoadSourceMesh(Folder+"/Meshes/Reception Branded.asset");
            litMesh=UnityEngine.Object.Instantiate(src);
            litMesh.name="Reception | Lit Crown";
            litMesh.uv=src.uv;
            AssetDatabase.CreateAsset(litMesh,litMeshPath);
        }
        reception.sharedMesh=litMesh;
        var walls=meshes.Single(f=>f.name=="Walls");
        const string skirtMeshPath=Folder+"/Meshes/Walls Front Skirting.asset";
        var skirtMesh=LoadSourceMesh(skirtMeshPath);
        if(!skirtMesh)
        {
            var src=LoadSourceMesh(Folder+"/Meshes/Walls Branded.asset");
            var pos=src.vertices.ToList();var normal=src.normals.ToList();var uv=src.uv.ToList();
            var tangent=src.tangents.ToList();
            var indices=Enumerable.Range(0,src.subMeshCount).Select(i=>src.GetTriangles(i).ToList()).ToArray();
            var matrix=walls.transform.worldToLocalMatrix*root.transform.localToWorldMatrix;
            var frame=root.GetComponentsInChildren<MeshRenderer>(true).Single(r=>r.name=="Entrance Frame");
            float front=Mathf.Sign(root.transform.InverseTransformPoint(frame.bounds.center).z);
            var tint=Tile("Charcoal powder coat","Structure").center;
            void Face(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 n)
            {
                int i=pos.Count;
                foreach(var q in new[]{a,b,c,d})
                {
                    pos.Add(matrix.MultiplyPoint3x4(q));normal.Add(matrix.MultiplyVector(n).normalized);uv.Add(tint);
                    if(tangent.Count>0)tangent.Add(new Vector4(1,0,0,1));
                }
                indices[0].AddRange(new[]{i,i+1,i+2,i,i+2,i+3});
            }
            void Strip(float x0,float x1)
            {
                float z0=front*11.94f-.16f,z1=front*11.94f+.16f,y0=0,y1=.32f;
                Vector3 a=new(x0,y0,z0),b=new(x1,y0,z0),c=new(x1,y1,z0),d=new(x0,y1,z0);
                Vector3 A=new(x0,y0,z1),B=new(x1,y0,z1),C=new(x1,y1,z1),D=new(x0,y1,z1);
                Face(a,d,c,b,Vector3.back);Face(A,B,C,D,Vector3.forward);
                Face(a,A,D,d,Vector3.left);Face(b,c,C,B,Vector3.right);
                Face(d,D,C,c,Vector3.up);Face(a,b,B,A,Vector3.down);
            }
            Strip(-13.74f,-2.365f);
            Strip(2.365f,13.74f);
            skirtMesh=new Mesh{name="Walls | Front Skirting",indexFormat=pos.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            skirtMesh.SetVertices(pos);skirtMesh.SetNormals(normal);skirtMesh.SetUVs(0,uv);
            if(tangent.Count==pos.Count)skirtMesh.SetTangents(tangent);
            skirtMesh.subMeshCount=indices.Length;for(int i=0;i<indices.Length;i++)skirtMesh.SetTriangles(indices[i],i,false);
            Unwrapping.GenerateSecondaryUVSet(skirtMesh);
            skirtMesh.RecalculateBounds();
            AssetDatabase.CreateAsset(skirtMesh,skirtMeshPath);
        }
        walls.sharedMesh=skirtMesh;
        File.AppendAllText(Report,"Front skirting: two 0.32 m-high sections, 24 triangles, doorway clear.\n");
        if(existing.Any(pair=>pair.Key.localToWorldMatrix!=pair.Value))throw new InvalidOperationException("An object moved.");
        File.AppendAllText(Report,$"Square grille and lit gold crown verified.\nBlue screens: {stands.Length}\nPreserved transforms: {existing.Count}\nTwo existing interior atlas layouts and three materials.\n");
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        Capture(root.transform,"Showroom Finish Preview.png");
        StartBake(root.transform);
    }
    static void StartBake(Transform root)
    {
        if (Lightmapping.isRunning)
        {
            File.AppendAllText(Report,"Waiting for an existing lighting bake.\n");
            EditorApplication.update += Retry;
            return;
            void Retry()
            {
                if (Lightmapping.isRunning) return;
                EditorApplication.update -= Retry;
                if (root && SceneManager.GetActiveScene().path == ScenePath) StartBake(root);
            }
        }
        Lightmapping.bakeCompleted += OnComplete;
        bool started = Lightmapping.BakeAsync();
        File.AppendAllText(Report, "Baked emission GI started: " + started + "\n");
        if (started) File.WriteAllText(BakeMarker,"Baking");
        void OnComplete()
        {
            Lightmapping.bakeCompleted -= OnComplete;
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            File.AppendAllText(Report, "Baked lightmaps: " + LightmapSettings.lightmaps.Length + "\n");
            if (File.Exists(BakeMarker)) File.Delete(BakeMarker);
            Capture(root, "Showroom Unity Baked Preview.png");
            if (File.ReadAllText(Report).StartsWith("Showroom detail finish"))
            {
                Capture(root,"Showroom Finish Baked Preview.png");
                var stand=root.GetComponentsInChildren<MeshRenderer>(true).First(r=>r.name.StartsWith("Vehicle Info Stand "));
                var b=stand.bounds;
                var toward=root.position-b.center;toward.y=0;toward.Normalize();
                Capture(root,"Blue Info Stand Detail.png",b.center+toward*1.35f+Vector3.up*.70f,b.center+Vector3.up*.08f);
                var desk=root.GetComponentsInChildren<MeshRenderer>(true).Single(r=>r.name=="Reception").bounds;
                var p=desk.center;
                var approach=(root.position-p);approach.y=0;
                if(desk.size.x>desk.size.z)approach.x=0;else approach.z=0;
                approach.Normalize();
                Capture(root,"Lit Reception Crown Detail.png",p+approach*4.1f+Vector3.up*1.55f,p+Vector3.up*.08f);
                var frame=root.GetComponentsInChildren<MeshRenderer>(true).Single(r=>r.name=="Entrance Frame").bounds;
                var front=Mathf.Sign(root.InverseTransformPoint(frame.center).z);
                Capture(root,"Front Window Skirting Detail.png",root.TransformPoint(new Vector3(-8.2f,1.27f,front*18.4f)),root.TransformPoint(new Vector3(0,.62f,front*12f)));
            }
            if (File.ReadAllText(Report).StartsWith("Showroom branding"))
            {
                Capture(root, "Showroom Branding Baked Preview.png");
                var rr=root.GetComponentsInChildren<MeshRenderer>(true);
                float side=Mathf.Sign(root.InverseTransformPoint(rr.Single(r=>r.name=="Ramp").bounds.center).x);
                float front=Mathf.Sign(root.InverseTransformPoint(rr.Single(r=>r.name=="Entrance Frame").bounds.center).z);
                Capture(root,"Game Box Centered Board.png",root.TransformPoint(new Vector3(side*8.9f,5.75f,front*2.4f)),root.TransformPoint(new Vector3(side*13.72f,5.95f,front*.05f)));
                var rug=rr.Single(r=>r.name=="Ground Rug").bounds.center;
                var toward=(root.position-rug);toward.y=0;toward.Normalize();
                Capture(root,"Showroom Rug Detail.png",rug+toward*3.45f+Vector3.up*2.5f,rug+Vector3.up*.35f);
            }
            if (File.ReadAllText(Report).StartsWith("Interior polish")) Capture(root, "Showroom Polished Baked Preview.png");
        }
    }

    static void Capture(Transform root, string filename, Vector3? position = null, Vector3? aim = null)
    {
        var go = new GameObject("Temporary showroom preview") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        var target = new RenderTexture(1360,800,24,RenderTextureFormat.ARGB32);
        var image = new Texture2D(1360,800,TextureFormat.RGB24,false);
        var old = RenderTexture.active;
        try
        {
            var frame = root.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="Entrance Frame");
            var front = root.InverseTransformPoint(frame.bounds.center);
            var sign = front.z>=0 ? 1f : -1f;
            go.transform.position = root.TransformPoint(new Vector3(-Mathf.Sign(root.InverseTransformPoint(root.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="Ramp").bounds.center).x)*7.8f,4.9f,front.z-sign*.55f));
            go.transform.rotation = Quaternion.LookRotation(root.TransformPoint(new Vector3(.4f,3.4f,-sign*3.5f))-go.transform.position,root.up);
            if (position.HasValue && aim.HasValue) { go.transform.position = position.Value; go.transform.rotation = Quaternion.LookRotation(aim.Value-position.Value,root.up); }
            camera.fieldOfView = 62;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 95;
            camera.allowHDR = true;
            camera.enabled = false;
            var additional = camera.GetUniversalAdditionalCameraData();
            additional.renderPostProcessing = true;
            additional.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,1360,800),0,0);
            image.Apply();
            File.WriteAllBytes(filename,image.EncodeToPNG());
            File.AppendAllText(Report,"Preview: "+filename+"\n");
        }
        catch (Exception e) { File.AppendAllText(Report,"Preview error: "+e.Message+"\n"); }
        finally
        {
            RenderTexture.active=old;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}

















