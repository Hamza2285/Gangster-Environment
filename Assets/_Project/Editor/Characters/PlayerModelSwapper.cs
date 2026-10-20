using System.Collections.Generic;
using System.Text;
using OpenworldCrime.Player;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Puts the FinalPlayer model into Player_Jason (in place of the Jason model) and into the drivable template's seated
    /// player copy. Both are Mixamo rigs of the same size, so the Invector setup carries over: the ragdoll bodies, joints, hit
    /// boxes and foot triggers on Jason's bones move to the same bones of the new model, every reference to Jason's objects is
    /// re-pointed, and the root Animator gets the new humanoid avatar. The model ships with all its outfit variants skinned in;
    /// a CharacterOutfit picks one per slot. Run Vehicles > Build Vehicles From Specs afterwards so every car gets the new copy.
    /// </summary>
    public static class PlayerModelSwapper
    {
        private const string ModelPath = "Assets/playernew/FinalPlayer.fbx";
        private const string PlayerPath = "Assets/_Project/Prefabs/Characters/Player_Jason.prefab";
        private const string TemplateDrivable = "Assets/_Project/Prefabs/Vehicles/_Templates/Template_Drivable.prefab";
        private const string NewModelName = "FinalPlayer";

        [MenuItem("Tools/OpenworldCrime/Characters/Swap Player Model")]
        public static void Run() => Debug.Log(SwapAll());

        public static string SwapAll()
        {
            var sb = new StringBuilder("[PlayerModelSwapper]\n");
            Avatar avatar = ConfigureImport(sb);
            if (avatar == null) return sb.Append("  stopped: no valid humanoid avatar").ToString();
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            int[] outfit = SwapPlayer(model, avatar, sb);
            SwapSeatedCopy(model, outfit, sb);
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        // Humanoid avatar from the model itself (Mixamo bone names map automatically).
        private static Avatar ConfigureImport(StringBuilder sb)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            if (importer == null) { sb.AppendLine($"  {ModelPath} not found"); return null; }
            if (importer.animationType != ModelImporterAnimationType.Human || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
            }
            Avatar avatar = null;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(ModelPath)) if (o is Avatar a) avatar = a;
            sb.AppendLine($"  import: humanoid avatar {(avatar != null && avatar.isValid && avatar.isHuman ? "ok" : "INVALID")}");
            return avatar != null && avatar.isValid && avatar.isHuman ? avatar : null;
        }

        private static int[] SwapPlayer(GameObject model, Avatar avatar, StringBuilder sb)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                Transform holder = root.transform.Find("3D Model");
                Transform old = holder != null ? holder.Find("Jason") : null;
                Transform fresh = holder != null ? holder.Find(NewModelName) : null;
                if (old != null)
                {
                    if (fresh != null) Object.DestroyImmediate(fresh.gameObject);
                    fresh = Instantiate(model, holder, old);

                    // Old object -> new object, for every same-named transform (bones and meshes) and its components.
                    var map = new Dictionary<Object, Object>();
                    var byName = new Dictionary<string, Transform>();
                    foreach (Transform t in fresh.GetComponentsInChildren<Transform>(true)) if (!byName.ContainsKey(t.name)) byName[t.name] = t;
                    map[old] = fresh; map[old.gameObject] = fresh.gameObject;
                    var attachments = new List<(Transform child, Transform bone)>();
                    int moved = 0;
                    foreach (Transform t in old.GetComponentsInChildren<Transform>(true))
                    {
                        if (t == old || !byName.TryGetValue(t.name, out Transform n)) continue;
                        map[t] = n; map[t.gameObject] = n.gameObject;
                        foreach (Component c in t.GetComponents<Component>())
                        {
                            if (c == null || c is Transform) continue;
                            Component same = n.GetComponent(c.GetType());
                            if (same != null && (c is Renderer || c is MeshFilter)) { map[c] = same; continue; }
                            if (c is Renderer) continue;
                            // Ragdoll / hit components living on the bone itself move with it.
                            ComponentUtility.CopyComponent(c);
                            ComponentUtility.PasteComponentAsNew(n.gameObject);
                            Component[] all = n.GetComponents(c.GetType());
                            map[c] = all[all.Length - 1];
                            moved++;
                        }
                        // Children that are not part of the model (triggers, hit boxes, holders) follow their bone.
                        foreach (Transform child in t)
                            if (!byName.ContainsKey(child.name) && child.GetComponent<Renderer>() == null) attachments.Add((child, n));
                    }
                    foreach (var (child, bone) in attachments) child.SetParent(bone, false);

                    int remapped = Remap(root, map);
                    var animator = root.GetComponent<Animator>();
                    if (animator != null) animator.avatar = avatar;
                    Object.DestroyImmediate(old.gameObject);
                    sb.AppendLine($"  player: model swapped, {moved} bone components and {attachments.Count} attachments moved, {remapped} references re-pointed");
                }
                else if (fresh == null) { sb.AppendLine("  player: no Jason or FinalPlayer model found"); return null; }
                else sb.AppendLine("  player: already swapped (outfit refreshed)");

                var outfit = fresh.GetComponent<CharacterOutfit>() ?? fresh.gameObject.AddComponent<CharacterOutfit>();
                if (outfit.SlotCount == 0) outfit.EditorSetSlots(BuildSlots(fresh, null));
                else outfit.Apply();
                int[] chosen = ReadSelection(outfit);

                // 1.68 m in game: the new model is the same skeleton as Jason with a smaller head.
                root.transform.localScale = Vector3.one * VehicleFactory.PlayerScale;
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
                sb.AppendLine($"  player: root scale {VehicleFactory.PlayerScale:F2}");
                return chosen;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // The static seated player shown in drivable cars: the new model posed like the old copy (same bone names, same pose).
        private static void SwapSeatedCopy(GameObject model, int[] outfit, StringBuilder sb)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(TemplateDrivable);
            try
            {
                Transform rig = root.transform.Find("DriverSeat/CarPlayer/JasonSeated");
                if (rig == null) { sb.AppendLine("  template: no DriverSeat/CarPlayer/JasonSeated"); return; }
                Transform old = rig.Find("Jason");
                Transform fresh = rig.Find(NewModelName);
                if (old != null)
                {
                    if (fresh != null) Object.DestroyImmediate(fresh.gameObject);
                    fresh = Instantiate(model, rig, old);
                    var byName = new Dictionary<string, Transform>();
                    foreach (Transform t in fresh.GetComponentsInChildren<Transform>(true)) if (!byName.ContainsKey(t.name)) byName[t.name] = t;
                    int posed = 0;
                    foreach (Transform t in old.GetComponentsInChildren<Transform>(true))
                    {
                        if (!t.name.StartsWith("mixamorig") || !byName.TryGetValue(t.name, out Transform n)) continue;
                        n.localRotation = t.localRotation;
                        if (t.name == "mixamorig:Hips") n.localPosition = t.localPosition;
                        posed++;
                    }
                    Object.DestroyImmediate(old.gameObject);
                    sb.AppendLine($"  template: seated copy swapped ({posed} bones posed)");
                }
                else if (fresh == null) { sb.AppendLine("  template: no seated model found"); return; }

                var copyOutfit = fresh.GetComponent<CharacterOutfit>() ?? fresh.gameObject.AddComponent<CharacterOutfit>();
                copyOutfit.EditorSetSlots(BuildSlots(fresh, outfit));
                PrefabUtility.SaveAsPrefabAsset(root, TemplateDrivable);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // The model as a nested prefab instance in place of `like` (same parent, pose and sibling order), without its own Animator.
        private static Transform Instantiate(GameObject model, Transform parent, Transform like)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            go.name = NewModelName;
            Transform t = go.transform;
            t.localPosition = like.localPosition;
            t.localRotation = like.localRotation;
            t.localScale = like.localScale;
            t.SetSiblingIndex(like.GetSiblingIndex());
            var animator = go.GetComponent<Animator>();
            if (animator != null) Object.DestroyImmediate(animator);
            return t;
        }

        // Re-points every object reference in the prefab that the map covers. Returns how many changed.
        private static int Remap(GameObject root, Dictionary<Object, Object> map)
        {
            int count = 0;
            foreach (Component c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform) continue;
                var so = new SerializedObject(c);
                SerializedProperty it = so.GetIterator();
                bool changed = false;
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                    if (!map.TryGetValue(it.objectReferenceValue, out Object replacement)) continue;
                    it.objectReferenceValue = replacement;
                    changed = true;
                    count++;
                }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
            return count;
        }

        private static int[] ReadSelection(CharacterOutfit outfit)
        {
            var so = new SerializedObject(outfit);
            SerializedProperty slots = so.FindProperty("slots");
            var result = new int[slots.arraySize];
            for (int i = 0; i < result.Length; i++) result[i] = slots.GetArrayElementAtIndex(i).FindPropertyRelative("selected").intValue;
            return result;
        }

        // Slots of the FinalPlayer model. Default look: dark jacket, jeans, white sneakers, grey quiff, silver chain.
        private static CharacterOutfit.Slot[] BuildSlots(Transform model, int[] selection)
        {
            var renderers = new Dictionary<string, Renderer>();
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true)) renderers[r.name] = r;
            CharacterOutfit.Option O(string label, params string[] parts)
            {
                var list = new List<Renderer>();
                foreach (string p in parts) if (renderers.TryGetValue(p, out Renderer r)) list.Add(r);
                return new CharacterOutfit.Option { label = label, parts = list.ToArray() };
            }
            var slots = new[]
            {
                new CharacterOutfit.Slot { name = "Body", selected = 0, options = new[] { O("Head, hands, neck", "Face", "Eyes", "hand", "Neck", "Hand ring", "Mesh_0127.rip") } },
                new CharacterOutfit.Slot { name = "Hair", selected = 0, options = new[] { O("Grey quiff", "Hair01"), O("Brown curls", "Hair"), O("Black braids", "Mesh_0014.rip"), O("Blue braids", "Mesh_0014.rip001") } },
                new CharacterOutfit.Slot { name = "Glasses", selected = -1, options = new[] { O("Glasses 1", "Eyewear", "glass"), O("Glasses 2", "Eyewear001", "GLASS001"), O("Glasses 3", "Eyewear002", "glass1"), O("Glasses 4", "Eyewear003", "glass2") } },
                new CharacterOutfit.Slot { name = "Top", selected = 0, options = new[] {
                    O("Dark jacket", "Jecket"), O("Yellow jacket", "Jecket001"), O("Blue jacket", "Jecket002"), O("White track jacket", "Tops001"),
                    O("Grey T-shirt", "Tops002", "Object001"), O("Black and red T-shirt", "Tops003", "Object001"), O("White T-shirt", "Tops004", "Object001"),
                    O("Gold vest", "Tops", "Object001"), O("Skull vest", "Tops005", "Object001") } },
                new CharacterOutfit.Slot { name = "Bottoms", selected = 0, options = new[] {
                    O("Jeans", "Bottoms"), O("Khaki trousers", "Bottom01"), O("Black shorts", "Bottoms001", "legs"), O("Brown shorts", "Bottoms002", "legs"),
                    O("Red shorts", "Bottoms003", "legs"), O("Grey shorts", "Bottoms004", "legs") } },
                new CharacterOutfit.Slot { name = "Shoes", selected = 0, options = new[] { O("White sneakers", "Ch42_Sneakers"), O("Shoes 1", "Shoes"), O("Shoes 2", "Shoes001"), O("Shoes 3", "Shoes002"), O("Shoes 4", "Shoes003"), O("Shoes 5", "Shoes004") } },
                new CharacterOutfit.Slot { name = "Necklace", selected = 0, options = new[] { O("Silver chain", "Mesh_0018.rip"), O("Thin chains", "Chain", "Chain001", "Chain002") } },
            };
            if (selection != null) for (int i = 0; i < slots.Length && i < selection.Length; i++) slots[i].selected = selection[i];
            return slots;
        }
    }
}
