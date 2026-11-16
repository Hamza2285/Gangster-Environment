using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CityMirageCeilingGroup
{
    const string Prefab="Assets/Prefeb/Cloth Shop/Cloth Shop Environment.prefab";
    const string Request="Temp/CityMirageCeilingGroup.request";
    const string Status="Temp/CityMirageCeilingGroup.txt";
    [InitializeOnLoadMethod] static void Schedule()
    {
        if(!Application.isBatchMode&&File.Exists(Request))EditorApplication.delayCall+=Run;
    }

    [MenuItem("Tools/City Mirage/Group Ceiling Structure")]
    public static void Run()
    {
        try{File.WriteAllText(Status,Apply());File.Delete(Request);}
        catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
    }

    static string Apply()
    {
        var root=PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            var all=root.GetComponentsInChildren<Transform>(true);
            var lighting=all.Single(t=>t.name=="City Mirage | Lighting");
            if(lighting.Find("Ceiling Structure"))return "DONE (already grouped)";
            var selected=all.Where(IsCeiling).ToArray();
            if(selected.Length<30||!selected.Any(t=>t.name=="Ceiling deck"))
                throw new InvalidOperationException($"Found only {selected.Length} ceiling objects.");
            var top=selected.Where(t=>!selected.Any(parent=>parent!=t&&t.IsChildOf(parent))).ToArray();
            foreach(var t in top)
            {
                var nearest=PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);
                if(nearest&&nearest!=t.gameObject)throw new InvalidOperationException($"Cannot move a child out of nested prefab {nearest.name}.");
            }
            var prior=top.ToDictionary(t=>t,t=>t.localToWorldMatrix);
            var group=new GameObject("Ceiling Structure").transform;
            group.SetParent(lighting,false);
            foreach(var t in top)t.SetParent(group,true);
            foreach(var t in top)
                for(int i=0;i<16;i++)
                    if(Mathf.Abs(prior[t][i]-t.localToWorldMatrix[i])>.0002f)
                        throw new InvalidOperationException($"World placement changed for {t.name}.");
            PrefabUtility.SaveAsPrefabAsset(root,Prefab,out bool saved);
            if(!saved)throw new IOException("Could not save the environment prefab.");
            return $"DONE: {top.Length} objects grouped, including roof, beams, conduits, fixtures and {group.GetComponentsInChildren<Light>(true).Length} ceiling lights.";
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }

    static bool IsCeiling(Transform t)
    {
        string n=t.name;
        return n=="Ceiling deck" || n=="Ceiling | ambient bounce accents" ||
            n.StartsWith("Ceiling | cross beam") || n.StartsWith("Ceiling | longitudinal rail") ||
            n.StartsWith("Ceiling | conduit") || n.StartsWith("Ceiling | perimeter conduit") ||
            n.StartsWith("Ceiling | pendant stem") || n.StartsWith("Ceiling | suspended rectangular fixture") ||
            n.StartsWith("Ceiling | fixture diffuser") || n.StartsWith("Ceiling | track spotlight housing") ||
            n.StartsWith("Ceiling | back-wall spotlight track") || n.StartsWith("Ceiling - warm lens rim") ||
            n.StartsWith("Ceiling | CLOTH SHOP lightbox") || n.StartsWith("Sign suspension") ||
            n=="Fixture | charcoal powder coat" || n=="Fixture | satin reflector" || n=="Fixture | warm opal lens";
    }
}
