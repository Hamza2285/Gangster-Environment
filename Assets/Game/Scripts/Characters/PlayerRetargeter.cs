using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// When the played character changes, everything in the scene that pointed at the old character (the traffic, the
// pedestrians, the HUD, singletons...) is pointed at the same part of the new one. Both characters are built from the same
// player, so every component of one has a twin at the same place in the other; model bones are matched by name.
public static class PlayerRetargeter
{
    const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    static readonly Dictionary<Type, FieldInfo[]> fieldsByType = new Dictionary<Type, FieldInfo[]>();
    static List<FieldInfo> staticFields;

    // Returns how many references were changed.
    public static int Retarget(GameObject from, GameObject to)
    {
        var map = BuildMap(from.transform, to.transform);
        int changed = 0;
        foreach (var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (behaviour == null) continue;
            var t = behaviour.transform;
            if (t.IsChildOf(from.transform) || t.IsChildOf(to.transform)) continue;
            // The switcher keeps its own list of who is who.
            if (behaviour is CharacterSwitcher) continue;
            if (!Refers(behaviour, map)) continue;
            // Switched off and on around the change, so whatever it subscribed to on the old character in OnEnable is
            // unsubscribed and subscribed again on the new one.
            // Only the game's own scripts: third-party ones (the iTS traffic spawner) tear down helpers in OnDisable that
            // they only build in Start.
            bool cycle = behaviour.enabled && behaviour.gameObject.activeInHierarchy && OwnScript(behaviour.GetType());
            if (cycle) behaviour.enabled = false;
            changed += RetargetObject(behaviour, map);
            if (cycle) behaviour.enabled = true;
        }
        foreach (var field in StaticObjectFields())
        {
            try
            {
                var value = field.GetValue(null);
                if (TryMap(value, map, out var replacement)) { field.SetValue(null, replacement); changed++; }
            }
            catch (Exception) { }
        }
        return changed;
    }

    // Every component and object of `from` paired with its twin in `to`.
    static Dictionary<UnityEngine.Object, UnityEngine.Object> BuildMap(Transform from, Transform to)
    {
        var map = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
        var byPath = new Dictionary<string, Transform>();
        var bonesByName = new Dictionary<string, Transform>();
        foreach (var t in to.GetComponentsInChildren<Transform>(true))
        {
            var path = PathOf(t, to);
            if (!byPath.ContainsKey(path)) byPath[path] = t;
            if (t.name.StartsWith("mixamorig") && !bonesByName.ContainsKey(t.name)) bonesByName[t.name] = t;
        }
        foreach (var t in from.GetComponentsInChildren<Transform>(true))
        {
            if (!byPath.TryGetValue(PathOf(t, from), out var twin) && !(t.name.StartsWith("mixamorig") && bonesByName.TryGetValue(t.name, out twin))) continue;
            map[t.gameObject] = twin.gameObject;
            var mine = t.GetComponents<Component>();
            var theirs = twin.GetComponents<Component>();
            var used = new HashSet<Component>();
            foreach (var c in mine)
            {
                if (c == null) continue;
                if (c is PlayableCharacter) continue;
                foreach (var d in theirs)
                {
                    if (d == null || used.Contains(d) || d.GetType() != c.GetType()) continue;
                    map[c] = d;
                    used.Add(d);
                    break;
                }
            }
        }
        return map;
    }

    // The path below the root, with the model's own name left out ("3D Model/*/mixamorig:Hips/...").
    static string PathOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (var x = t; x != null && x != root; x = x.parent) parts.Add(x.parent != null && x.parent.name == "3D Model" && x.parent.parent == root ? "*" : x.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    // Plain data classes are not looked into: the traffic and pedestrian systems hold huge graphs of them and walking
    // those froze the editor. Nothing the switch needs lives there (the traffic spawner follows the shared camera).
    const int MaxDepth = 0;

    static bool OwnScript(Type type) => type.Namespace != null && type.Namespace.StartsWith("OpenworldCrime");

    static int RetargetObject(object target, Dictionary<UnityEngine.Object, UnityEngine.Object> map) => Walk(target, map, true, 0);

    static bool Refers(object target, Dictionary<UnityEngine.Object, UnityEngine.Object> map) => Walk(target, map, false, 0) > 0;

    // Looks through the fields of an object, the lists in it and the plain [Serializable] data classes it holds (two levels
    // down, e.g. a spawner's list of "check" entries each holding a transform). Changes them when apply is set; otherwise
    // only says whether anything would change.
    static int Walk(object target, Dictionary<UnityEngine.Object, UnityEngine.Object> map, bool apply, int depth)
    {
        int changed = 0;
        for (var type = target.GetType(); type != null && type != typeof(MonoBehaviour) && type != typeof(object); type = type.BaseType)
        {
            foreach (var field in FieldsOf(type))
            {
                object value;
                try { value = field.GetValue(target); } catch (Exception) { continue; }
                if (value == null) continue;
                if (value is UnityEngine.Object)
                {
                    if (!TryMap(value, map, out var replacement)) continue;
                    if (!apply) return 1;
                    if (field.IsInitOnly) continue;
                    try { field.SetValue(target, replacement); changed++; } catch (Exception) { }
                }
                else if (value is IList list && !(value is string))
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var item = list[i];
                        if (item == null) continue;
                        if (item is UnityEngine.Object)
                        {
                            if (!TryMap(item, map, out var replacement)) continue;
                            if (!apply) return 1;
                            if (list.IsReadOnly && !(list is Array)) continue;
                            try { list[i] = replacement; changed++; } catch (Exception) { }
                        }
                        else if (depth < MaxDepth && IsData(item.GetType()))
                        {
                            int n = Walk(item, map, apply, depth + 1);
                            if (!apply && n > 0) return 1;
                            changed += n;
                        }
                    }
                }
                else if (depth < MaxDepth && IsData(value.GetType()))
                {
                    int n = Walk(value, map, apply, depth + 1);
                    if (!apply && n > 0) return 1;
                    changed += n;
                }
            }
        }
        return changed;
    }

    // A plain serialized data class (not a struct, a delegate, a string or a Unity object).
    static bool IsData(Type type) =>
        type.IsClass && type != typeof(string) && !typeof(Delegate).IsAssignableFrom(type) && !typeof(UnityEngine.Object).IsAssignableFrom(type)
        && type.IsDefined(typeof(SerializableAttribute), false) && type.Assembly != typeof(object).Assembly;

    static bool TryMap(object value, Dictionary<UnityEngine.Object, UnityEngine.Object> map, out UnityEngine.Object replacement)
    {
        replacement = null;
        return value is UnityEngine.Object unityObject && unityObject != null && map.TryGetValue(unityObject, out replacement);
    }

    // Fields that can hold a Unity object, or a list or array of them.
    static FieldInfo[] FieldsOf(Type type)
    {
        if (fieldsByType.TryGetValue(type, out var fields)) return fields;
        var list = new List<FieldInfo>();
        foreach (var field in type.GetFields(InstanceFields))
        {
            if (HoldsObjects(field.FieldType)) list.Add(field);
        }
        fields = list.ToArray();
        fieldsByType[type] = fields;
        return fields;
    }

    static bool HoldsObjects(Type type)
    {
        if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return true;
        if (type.IsArray) return typeof(UnityEngine.Object).IsAssignableFrom(type.GetElementType());
        if (type.IsGenericType && typeof(IList).IsAssignableFrom(type))
        {
            var args = type.GetGenericArguments();
            return args.Length == 1 && typeof(UnityEngine.Object).IsAssignableFrom(args[0]);
        }
        return false;
    }

    // Singletons such as "instance" fields, in the game's own code (Invector included).
    static List<FieldInfo> StaticObjectFields()
    {
        if (staticFields != null) return staticFields;
        staticFields = new List<FieldInfo>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = assembly.GetName().Name;
            if (!name.StartsWith("Assembly-CSharp") && name.IndexOf("Invector", StringComparison.OrdinalIgnoreCase) < 0) continue;
            Type[] types;
            try { types = assembly.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types; }
            foreach (var type in types)
            {
                if (type == null || type.ContainsGenericParameters) continue;
                foreach (var field in type.GetFields(StaticFields))
                    if (!field.IsLiteral && !field.IsInitOnly && typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)) staticFields.Add(field);
            }
        }
        return staticFields;
    }
}
