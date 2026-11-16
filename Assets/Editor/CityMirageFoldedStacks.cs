using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class CityMirageFoldedStacks
{
    const string Shop = "Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Source = "Assets/Prefeb/Cloth Shop/Reusable/Folded Company Shirt.prefab";
    const string Request = "Temp/CityMirageFoldedStacks.request";
    const string Status = "Temp/CityMirageFoldedStacks.txt";

    [InitializeOnLoadMethod] static void Queue()
    {
        if (!Application.isBatchMode && File.Exists(Request)) EditorApplication.delayCall += Run;
    }

    [MenuItem("Tools/City Mirage/Seat Folded Shirt Stacks")]
    public static void Run()
    {
        try
        {
            File.WriteAllText(Status, Apply());
            if (File.Exists(Request)) File.Delete(Request);
        }
        catch (Exception e) { File.WriteAllText(Status,e.ToString()); Debug.LogException(e); }
    }

    static string Apply()
    {
        var root = PrefabUtility.LoadPrefabContents(Shop);
        try
        {
            var folded = root.GetComponentsInChildren<LODGroup>(true)
                .Where(g => AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(g.gameObject)) == Source)
                .Select(g => (group:g, bounds:g.GetLODs()[0].renderers[0].bounds)).ToArray();
            if (folded.Length != 134) throw new InvalidOperationException($"Expected 134 folded prefab instances, found {folded.Length}.");
            var stacks = new List<List<(LODGroup group,Bounds bounds)>>();
            foreach (var shirt in folded.OrderBy(s => s.bounds.min.y))
            {
                var nearest = stacks.Where(stack => stack.Count < 5 &&
                    shirt.bounds.min.y - stack.Last().bounds.min.y > .003f &&
                    shirt.bounds.min.y - stack.Last().bounds.min.y < .105f &&
                    PlanarDistance(shirt.bounds.center, stack[0].bounds.center) < .052f)
                    .OrderBy(stack => PlanarDistance(shirt.bounds.center,stack[0].bounds.center))
                    .ThenBy(stack => shirt.bounds.min.y-stack.Last().bounds.min.y).FirstOrDefault();
                if (nearest == null) stacks.Add(new List<(LODGroup,Bounds)> {shirt});
                else nearest.Add(shirt);
            }
            var multi = stacks.Where(s => s.Count > 1).ToArray();
            if (multi.Sum(s => s.Count) < 100)
                throw new InvalidOperationException($"Only {multi.Sum(s => s.Count)} shirts matched to stacks.");
            int moved = 0;
            foreach (var stack in multi)
            {
                float support = stack[0].bounds.min.y + stack[0].bounds.size.y*.62f;
                for (int i=1; i<stack.Count; i++)
                {
                    var shirt = stack[i];
                    float gap = shirt.bounds.min.y-support;
                    float drop = gap > .003f ? gap + Mathf.Min(.003f,shirt.bounds.size.y*.12f) : 0f;
                    if (drop > 0)
                    {
                        shirt.group.transform.position -= Vector3.up*drop;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(shirt.group.transform);
                        moved++;
                    }
                    support = shirt.bounds.min.y-drop + shirt.bounds.size.y*.62f;
                }
            }
            if (moved>0)
            {
                PrefabUtility.SaveAsPrefabAsset(root,Shop,out bool saved);
                if (!saved) throw new IOException("Could not save folded shirt placements.");
            }
            return $"DONE: Checked {folded.Length} linked folded shirts. Seated {moved} floating layers across {multi.Length} stacks. {stacks.Count-multi.Length} single placements.";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static float PlanarDistance(Vector3 a,Vector3 b) => Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));
}

