using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// This script has metadata needed for multi scene rendering and teleporting
/// It also registers the scene in the scene transition manager
/// </summary>
public class SceneMetaData : MonoBehaviour
{
    //TODO: Rewrite to use properties with correct getters and setters? Not sure if this is possible while exposing them in the editor. Maybe we need a custom editor.
    public GameObject mainLight = null;
    public Material skybox = null;
    public Cubemap reflection = null;
    [Tooltip("This is used if the scene loaded needs the player to be locked in place")]
    public Transform CameraLockTransform = null;
    public Transform WorldUpTransform = null;
    //public float FieldOfView = ;
    public Transform SpawnTransform;
    public PlayableDirector FlythroughDirector;
    public PlayableDirector SequenceDirector;
    public GameObject Root;
    public Scene Scene;
    public GameObject TerminalLoader; //TODO: Rename and make private with setter
    public bool FogEnabled;
    public bool PostProcessingEnabled;
    public bool StartActive;
    public int RendererIndex = 0;

    // Multi-scene transition setup (SceneTransitionManager / Benchmark) from the URP sample is not part of this project.
    // This component only carries the scene lighting metadata used by CityDayNightController.
}
