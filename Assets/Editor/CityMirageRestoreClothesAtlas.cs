using System;
using System.IO;
using UnityEditor;
using UnityEngine;

static class CityMirageRestoreClothesAtlas
{
    const string Request = "Temp/CityMirageRestoreClothesAtlas.request";
    const string Status = "Temp/CityMirageRestoreClothesAtlas.txt";

    [InitializeOnLoadMethod]
    static void Schedule()
    {
        if (File.Exists(Request)) EditorApplication.delayCall += Restore;
    }

    static void Restore()
    {
        try
        {
            const string path = "Assets/Texture/City Mirage/Clothes Atlas Packed.png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/City Mirage/Clothes Atlas.mat");
            if (!texture || !material) throw new InvalidOperationException("Clothes Atlas material or Base Color failed to load.");
            material.SetTexture("_BaseMap", texture);
            material.SetTexture("_MainTex", texture);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();
            File.WriteAllText(Status, $"DONE: shared Clothes Atlas Base Color loaded and assigned ({texture.width} × {texture.height}).");
            File.Delete(Request);
        }
        catch (Exception e)
        {
            File.WriteAllText(Status, e.ToString());
            Debug.LogException(e);
        }
    }
}
