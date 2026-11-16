using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds the second playable character (the girl) as a full copy of the scene's Jason player with her model swapped in,
// renders both portraits and wires the character switcher into the gameplay scene. Rerunnable.
public static class CharacterSwitchSetup
{
    const string GirlModelPath = "Assets/female character/female/Female PlayerT-Pose (2).fbx";
    const string GirlLookPrefab = "Assets/Game/Prefabs/Female Player.prefab";
    const string GirlPrefabPath = "Assets/_Project/Prefabs/Characters/Player_Girl.prefab";
    const string PortraitFolder = "Assets/Game/Characters";
    const string JasonSit = "Assets/_Project/Animations/Pedestrians/PedMan_sit_a.anim";
    const string GirlSit = "Assets/_Project/Animations/Pedestrians/PedGirl_sit_a.anim";
    const string GirlName = "Mia";
    const float GirlRootScale = 0.97f;

    [MenuItem("Game/Set Up Character Switch")]
    public static void Run() => Debug.Log(SetUp());

    public static string SetUp()
    {
        var sb = new StringBuilder("[CharacterSwitchSetup]\n");
        var jason = GameObject.Find("Player_Jason");
        if (jason == null) return sb.Append("  no Player_Jason in the open scene").ToString();

        EnsureFolder(PortraitFolder);
        var jasonCharacter = jason.GetComponent<PlayableCharacter>();
        if (jasonCharacter == null) jasonCharacter = jason.AddComponent<PlayableCharacter>();
        jasonCharacter.id = "jason";
        jasonCharacter.displayName = "Jason";
        jasonCharacter.accent = new Color(0.33f, 0.72f, 1f, 1f);
        jasonCharacter.sitClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(JasonSit);

        var girl = BuildGirl(jason, sb);
        if (girl == null) return sb.ToString();

        jasonCharacter.portrait = RenderPortrait(jason, "Jason Portrait", sb);
        var girlCharacter = girl.GetComponent<PlayableCharacter>();
        girlCharacter.portrait = RenderPortrait(girl, GirlName + " Portrait", sb);
        EditorUtility.SetDirty(girlCharacter);
        PrefabUtility.RecordPrefabInstancePropertyModifications(girlCharacter);
        girl.SetActive(false);

        var gameplay = GameObject.Find("Gameplay");
        if (gameplay == null) gameplay = new GameObject("Gameplay");
        var switcher = gameplay.GetComponent<CharacterSwitcher>();
        if (switcher == null) switcher = gameplay.AddComponent<CharacterSwitcher>();
        switcher.characters = new[] { jasonCharacter, girlCharacter };
        switcher.spots = AssetDatabase.LoadAssetAtPath<OpenworldCrime.Pedestrians.AmbientSpotData>("Assets/_Project/Data/Pedestrians/AmbientSpotData.asset");
        EditorUtility.SetDirty(switcher);
        EditorUtility.SetDirty(jasonCharacter);

        EditorSceneManager.MarkSceneDirty(jason.scene);
        EditorSceneManager.SaveScene(jason.scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("  " + BuildMiaAnimator());
        return sb.Append("  done: switcher on Gameplay with Jason and " + GirlName).ToString();
    }

    const string JasonAnimatorPath = "Assets/_Project/Animations/OWC_Shooter_Jason.overrideController";
    const string MiaAnimatorPath = "Assets/_Project/Animations/OWC_Shooter_Mia.overrideController";

    [MenuItem("Game/Give Mia Female Animations")]
    public static void RunAnimations() => Debug.Log(BuildMiaAnimator());

    // Jason's animator with Mia's own way of standing, walking, running and sprinting; aiming and weapon moves stay shared.
    public static string BuildMiaAnimator()
    {
        var jason = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(JasonAnimatorPath);
        if (jason == null) return "no " + JasonAnimatorPath;
        var mia = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(MiaAnimatorPath);
        if (mia == null)
        {
            mia = new AnimatorOverrideController(jason.runtimeAnimatorController);
            AssetDatabase.CreateAsset(mia, MiaAnimatorPath);
        }
        mia.runtimeAnimatorController = jason.runtimeAnimatorController;

        var female = new Dictionary<string, AnimationClip>
        {
            // The pedestrians' idle, imported again as Humanoid (theirs is Generic, which a humanoid animator cannot play).
            ["Josh_Idle"] = Clip("Assets/Game/Player/Mia Idle.fbx", "Mia_Idle"),
            ["Josh_Walk"] = Clip("Assets/Game/Player/FemaleWalkFlatShoes.anim", "FemaleWalkFlatShoes"),
            ["Walk"] = Clip("Assets/Game/Player/FemaleWalkFlatShoes.anim", "FemaleWalkFlatShoes"),
            ["Run"] = Clip("Assets/female character/female/Female PlayerT-Pose (2)@Running.fbx", "Running"),
            ["Sprint"] = Clip("Assets/female character/female/Ch49_nonPBR@Fast Run.fbx", "Fast Run"),
        };
        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        jason.GetOverrides(overrides);
        int changed = 0;
        for (int i = 0; i < overrides.Count; i++)
        {
            var original = overrides[i].Key;
            if (original != null && female.TryGetValue(original.name, out var clip) && clip != null)
            {
                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(original, clip);
                changed++;
            }
        }
        mia.ApplyOverrides(overrides);
        EditorUtility.SetDirty(mia);

        // On her prefab and on the scene copy.
        var contents = PrefabUtility.LoadPrefabContents(GirlPrefabPath);
        try
        {
            contents.GetComponent<Animator>().runtimeAnimatorController = mia;
            PrefabUtility.SaveAsPrefabAsset(contents, GirlPrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        int scene = 0;
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name != "Player_Girl") continue;
            var animator = root.GetComponent<Animator>();
            animator.runtimeAnimatorController = mia;
            EditorUtility.SetDirty(animator);
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            scene++;
        }
        AssetDatabase.SaveAssets();
        return "Mia's animator: " + changed + " clips swapped for female ones, on prefab and " + scene + " scene copy";
    }

    static AnimationClip Clip(string path, string name)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is AnimationClip c && c.name == name) return c;
        return null;
    }

    // A copy of the scene's Jason (with everything added to him in the scene) wearing the girl's model. The model swap runs
    // inside the prefab's own editing context, never on live scene objects: copying physics components onto bones in the
    // open scene leaves them half registered and crashes the editor when the object is switched off.
    static GameObject BuildGirl(GameObject jason, StringBuilder sb)
    {
        foreach (var root in jason.scene.GetRootGameObjects()) if (root.name == "Player_Girl") Object.DestroyImmediate(root);

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(GirlModelPath);
        var look = AssetDatabase.LoadAssetAtPath<GameObject>(GirlLookPrefab);
        Avatar avatar = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(GirlModelPath)) if (o is Avatar a && a.isHuman) avatar = a;
        if (model == null || avatar == null) { sb.AppendLine("  girl model or avatar missing at " + GirlModelPath); return null; }

        // 1. An untouched copy of Jason as a prefab of its own.
        var copy = Object.Instantiate(jason);
        copy.name = "Player_Girl";
        var spawner = copy.transform.Find("iTS Traffic Spawner");
        if (spawner != null) Object.DestroyImmediate(spawner.gameObject);
        PrefabUtility.SaveAsPrefabAsset(copy, GirlPrefabPath, out bool saved);
        Object.DestroyImmediate(copy);
        if (!saved) { sb.AppendLine("  could not save " + GirlPrefabPath); return null; }

        // 2. Her model swapped in, in the prefab's editing context.
        var contents = PrefabUtility.LoadPrefabContents(GirlPrefabPath);
        try
        {
            // One player camera for both: Invector keeps a single third-person camera in the scene and destroys any other.
            var ownCamera = contents.transform.Find("Invector Components/vThirdPersonCamera");
            if (ownCamera != null) Object.DestroyImmediate(ownCamera.gameObject);
            foreach (var tuning in contents.GetComponents<CharacterBodyTuning>()) Object.DestroyImmediate(tuning);
            // Jason's arm proportions are tuned for his model, not hers.
            foreach (var proportions in contents.GetComponentsInChildren<OpenworldCrime.Player.CharacterBoneProportions>(true)) Object.DestroyImmediate(proportions);
            foreach (var wardrobe in contents.GetComponents<PlayerWardrobe>()) Object.DestroyImmediate(wardrobe);
            contents.transform.localScale = Vector3.one * GirlRootScale;

            var holder = contents.transform.Find("3D Model");
            var old = holder != null && holder.childCount > 0 ? holder.GetChild(0) : null;
            if (old == null) { sb.AppendLine("  no model under 3D Model"); return null; }

            var freshObject = (GameObject)PrefabUtility.InstantiatePrefab(model, holder);
            PrefabUtility.UnpackPrefabInstance(freshObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var fresh = freshObject.transform;
            fresh.name = "Girl";
            fresh.localPosition = old.localPosition;
            fresh.localRotation = old.localRotation;
            fresh.localScale = old.localScale;
            fresh.SetSiblingIndex(old.GetSiblingIndex());
            var freshAnimator = fresh.GetComponent<Animator>();
            if (freshAnimator != null) Object.DestroyImmediate(freshAnimator);

            // Old object -> new object for every bone of the same name; components and attachments on Jason's bones move over.
            var byName = new Dictionary<string, Transform>();
            foreach (var t in fresh.GetComponentsInChildren<Transform>(true)) if (!byName.ContainsKey(t.name)) byName[t.name] = t;
            var map = new Dictionary<Object, Object> { [old] = fresh, [old.gameObject] = fresh.gameObject };
            var attachments = new List<(Transform child, Transform bone)>();
            int moved = 0;
            foreach (var t in old.GetComponentsInChildren<Transform>(true))
            {
                if (t == old || !t.name.StartsWith("mixamorig") || !byName.TryGetValue(t.name, out var n)) continue;
                map[t] = n;
                map[t.gameObject] = n.gameObject;
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null || c is Transform || c is Renderer) continue;
                    var twin = CopyComponent(c, n.gameObject);
                    if (twin == null) continue;
                    map[c] = twin;
                    moved++;
                }
                foreach (Transform child in t)
                    if (!byName.ContainsKey(child.name) && child.GetComponent<Renderer>() == null) attachments.Add((child, n));
            }
            foreach (var (child, bone) in attachments) child.SetParent(bone, false);
            int remapped = Remap(contents, map, old);
            contents.GetComponent<Animator>().avatar = avatar;
            Object.DestroyImmediate(old.gameObject);

            // Her look: the outfit the female character prefab wears.
            int shown = 0;
            var wear = new Dictionary<string, bool>();
            if (look != null) foreach (var r in look.GetComponentsInChildren<SkinnedMeshRenderer>(true)) wear[r.name] = r.gameObject.activeSelf;
            foreach (var r in fresh.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                bool on = wear.TryGetValue(r.name, out var w) ? w : r.gameObject.activeSelf;
                r.gameObject.SetActive(on);
                if (on) shown++;
            }

            var character = contents.GetComponent<PlayableCharacter>();
            if (character == null) character = contents.AddComponent<PlayableCharacter>();
            character.id = "girl";
            character.displayName = GirlName;
            character.accent = new Color(1f, 0.38f, 0.72f, 1f);
            character.sitClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(GirlSit);

            PrefabUtility.SaveAsPrefabAsset(contents, GirlPrefabPath, out saved);
            sb.AppendLine($"  girl: {moved} bone components and {attachments.Count} attachments moved, {remapped} references re-pointed, {shown} outfit parts shown, saved={saved}");
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        if (!saved) return null;

        // 3. Into the scene, next to Jason.
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(GirlPrefabPath);
        var girl = (GameObject)PrefabUtility.InstantiatePrefab(asset, jason.scene);
        girl.transform.SetPositionAndRotation(jason.transform.position + jason.transform.right * 2f, jason.transform.rotation);
        sb.AppendLine($"  girl: {FillMissing(jason, girl)} scene references copied from Jason");
        return girl;
    }

    // A new component of the same type with the same settings. AddComponent and a property-by-property copy, never the
    // editor's copy/paste clipboard, which leaves the object's component list out of step and crashes the editor later.
    static Component CopyComponent(Component source, GameObject target)
    {
        // A joint added first brings its own Rigidbody along; that one is reused.
        var copy = source is Rigidbody && target.GetComponent<Rigidbody>() != null ? target.GetComponent<Rigidbody>() : target.AddComponent(source.GetType());
        if (copy == null) return null;
        var from = new SerializedObject(source);
        var to = new SerializedObject(copy);
        var it = from.GetIterator();
        bool enter = true;
        while (it.NextVisible(enter))
        {
            enter = false;
            if (it.name == "m_Script") continue;
            to.CopyFromSerializedProperty(it);
        }
        to.ApplyModifiedPropertiesWithoutUndo();
        return copy;
    }

    // References to scene objects (the pedestrian spawner, ...) cannot live in a prefab, so the scene copy gets Jason's,
    // and anything that pointed inside Jason points to the same part of her.
    static int FillMissing(GameObject jason, GameObject girl)
    {
        var twins = new Dictionary<string, Transform>();
        var bones = new Dictionary<string, Transform>();
        foreach (var t in girl.GetComponentsInChildren<Transform>(true))
        {
            twins[PathOf(t, girl.transform)] = t;
            if (t.name.StartsWith("mixamorig")) bones[t.name] = t;
        }
        Transform TwinOf(Transform t)
        {
            if (twins.TryGetValue(PathOf(t, jason.transform), out var twin)) return twin;
            return t.name.StartsWith("mixamorig") && bones.TryGetValue(t.name, out twin) ? twin : null;
        }
        var sharedCamera = jason.transform.Find("Invector Components/vThirdPersonCamera");
        Object Translate(Object value)
        {
            Transform owner = value is Component c ? c.transform : value is GameObject g ? g.transform : null;
            if (owner == null || !owner.IsChildOf(jason.transform)) return value;
            if (sharedCamera != null && owner.IsChildOf(sharedCamera)) return value;
            var twin = TwinOf(owner);
            if (twin == null) return null;
            if (value is GameObject) return twin.gameObject;
            var same = twin.GetComponents(value.GetType());
            var mine = owner.GetComponents(value.GetType());
            int index = System.Array.IndexOf(mine, value);
            return index >= 0 && index < same.Length ? same[index] : null;
        }

        int count = 0;
        foreach (var t in jason.GetComponentsInChildren<Transform>(true))
        {
            var twin = TwinOf(t);
            if (twin == null) continue;
            var mine = t.GetComponents<Component>();
            foreach (var c in mine)
            {
                if (c == null || c is Transform) continue;
                var others = twin.GetComponents(c.GetType());
                var siblings = t.GetComponents(c.GetType());
                int index = System.Array.IndexOf(siblings, c);
                if (index < 0 || index >= others.Length) continue;
                var from = new SerializedObject(c);
                var to = new SerializedObject(others[index]);
                var it = from.GetIterator();
                bool changed = false;
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                    if (it.name == "m_GameObject" || it.name == "m_Script" || it.name == "m_PrefabInstance" || it.name == "m_CorrespondingSourceObject" || it.name == "m_PrefabAsset") continue;
                    var target = to.FindProperty(it.propertyPath);
                    if (target == null || target.propertyType != SerializedPropertyType.ObjectReference || target.objectReferenceValue != null) continue;
                    var value = Translate(it.objectReferenceValue);
                    if (value == null) continue;
                    target.objectReferenceValue = value;
                    changed = true;
                    count++;
                }
                if (changed) to.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        return count;
    }

    static string PathOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (var x = t; x != null && x != root; x = x.parent) parts.Add(x.parent != null && x.parent.name == "3D Model" && x.parent.parent == root ? "*" : x.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    static int Remap(GameObject root, Dictionary<Object, Object> map, Transform skip)
    {
        int count = 0;
        foreach (var c in root.GetComponentsInChildren<Component>(true))
        {
            if (c == null || c is Transform || c.transform.IsChildOf(skip)) continue;
            var so = new SerializedObject(c);
            var it = so.GetIterator();
            bool changed = false;
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                // A component's owner is not a reference to re-point: changing it corrupts the object and crashes the editor.
                if (it.name == "m_GameObject" || it.name == "m_PrefabInstance" || it.name == "m_CorrespondingSourceObject") continue;
                if (!map.TryGetValue(it.objectReferenceValue, out var replacement)) continue;
                it.objectReferenceValue = replacement;
                changed = true;
                count++;
            }
            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        }
        return count;
    }

    // Head-and-shoulders portrait on a transparent background, saved as a sprite.
    static Sprite RenderPortrait(GameObject character, string fileName, StringBuilder sb)
    {
        var animator = character.GetComponent<Animator>();
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) { sb.AppendLine("  no head bone on " + character.name); return null; }

        const int size = 512;
        var target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
        var cameraObject = new GameObject("Portrait Camera");
        var cam = cameraObject.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.fieldOfView = 24f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 10f;
        cam.targetTexture = target;
        cam.cullingMask = 1 << 30;
        var lightObject = new GameObject("Portrait Light");
        var key = lightObject.AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.3f;
        key.cullingMask = 1 << 30;

        // A loose copy of the visible meshes on a spare layer, so the city does not show behind the face.
        var layers = new Dictionary<GameObject, int>();
        foreach (var r in character.GetComponentsInChildren<Renderer>())
        {
            layers[r.gameObject] = r.gameObject.layer;
            r.gameObject.layer = 30;
        }
        Vector3 face = head.position + Vector3.up * 0.06f * character.transform.lossyScale.y;
        Vector3 forward = character.transform.forward;
        cameraObject.transform.position = face + forward * 0.95f + character.transform.right * 0.12f;
        cameraObject.transform.LookAt(face - Vector3.up * 0.05f);
        lightObject.transform.rotation = Quaternion.LookRotation(-forward + Vector3.down * 0.4f + character.transform.right * 0.5f);
        cam.Render();
        foreach (var pair in layers) pair.Key.layer = pair.Value;

        RenderTexture.active = target;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        Object.DestroyImmediate(cameraObject);
        Object.DestroyImmediate(lightObject);
        target.Release();

        string path = PortraitFolder + "/" + fileName + ".png";
        System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
