using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class RooftopSearchlightScenePreview
{
    static RooftopSearchlightRotator[] Beams = new RooftopSearchlightRotator[0];
    static readonly MaterialPropertyBlock PreviewProperties = new MaterialPropertyBlock();
    static double nextRepaint;

    static RooftopSearchlightScenePreview()
    {
        PreviewProperties.SetFloat(Shader.PropertyToID("_RooftopBeamVisibility"), 1f);
        SceneView.duringSceneGui -= DrawPreview;
        SceneView.duringSceneGui += DrawPreview;
        EditorApplication.update -= RepaintAnimatedPreview;
        EditorApplication.update += RepaintAnimatedPreview;
        EditorApplication.hierarchyChanged -= RefreshBeams;
        EditorApplication.hierarchyChanged += RefreshBeams;
        RefreshBeams();
    }

    static void RefreshBeams() => Beams = Object.FindObjectsOfType<RooftopSearchlightRotator>();

    static void RepaintAnimatedPreview()
    {
        if (EditorApplication.isPlaying || Beams.Length == 0) return;
        double now = EditorApplication.timeSinceStartup;
        if (now < nextRepaint) return;
        nextRepaint = now + 1.0 / 24.0;
        SceneView.RepaintAll();
    }

    static void DrawPreview(SceneView view)
    {
        if (EditorApplication.isPlaying || Event.current.type != EventType.Repaint || !view.camera) return;
        double time = EditorApplication.timeSinceStartup;
        foreach (var beam in Beams)
        {
            if (!beam || !beam.isActiveAndEnabled || !beam.showEditorPreview) continue;
            var meshFilter = beam.GetComponent<MeshFilter>();
            var meshRenderer = beam.GetComponent<MeshRenderer>();
            if (!meshFilter || !meshFilter.sharedMesh || !meshRenderer || !meshRenderer.sharedMaterial) continue;

            var rotation = Quaternion.AngleAxis((float)(time * beam.rotationSpeed), Vector3.up) * beam.transform.rotation;
            var matrix = Matrix4x4.TRS(beam.transform.position, rotation, beam.transform.lossyScale);
            Graphics.DrawMesh(meshFilter.sharedMesh, matrix, meshRenderer.sharedMaterial, beam.gameObject.layer,
                view.camera, 0, PreviewProperties, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
        }
    }
}
