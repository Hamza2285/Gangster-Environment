using System;
using System.Linq;
using OpenworldCrime.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Repairs the drivable RCC car in Assets/carfix.unity without touching traffic vehicles.</summary>
public static class RCCCarfixRepair
{
    public static void FixCarfixScene()
    {
        const string scenePath = "Assets/carfix.unity";
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var controllers = UnityEngine.Object.FindObjectsByType<RCC_CarControllerV3>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (controllers.Length != 1)
            throw new InvalidOperationException($"Expected exactly one RCC car in {scenePath}, found {controllers.Length}.");

        var controller = controllers[0];
        var root = controller.transform.root;
        var body = root.GetComponent<Rigidbody>();
        if (body == null)
            throw new InvalidOperationException($"'{root.name}' is missing its required Rigidbody.");
        if (root.GetComponentsInChildren<RCC_WheelCollider>(true).Length < 4)
            throw new InvalidOperationException($"'{root.name}' has fewer than four RCC wheel colliders; wheel rigging must be fixed manually.");

        // An accidentally disabled controller or kinematic body makes an otherwise configured RCC car immovable.
        controller.enabled = true;
        body.isKinematic = false;

        var adapter = root.GetComponent<RCCVehicleAdapter>();
        if (adapter == null)
            adapter = root.gameObject.AddComponent<RCCVehicleAdapter>();

        var serializedAdapter = new SerializedObject(adapter);
        SetIfMissing(serializedAdapter, "carController", controller);
        SetIfMissing(serializedAdapter, "driverSeat", Find(root, "DriverSeat"));
        SetIfMissing(serializedAdapter, "enterPoint", Find(root, "EnterPoint"));
        SetIfMissing(serializedAdapter, "exitPoint", Find(root, "ExitPoint") ?? Find(root, "EjectPoint"));
        SetIfMissing(serializedAdapter, "interactionPoint", Find(root, "EnterPoint"));
        serializedAdapter.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(body);
        EditorUtility.SetDirty(adapter);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[RCC Carfix] Repaired '{root.name}': controller enabled, dynamic Rigidbody, {root.GetComponentsInChildren<RCC_WheelCollider>(true).Length} RCC wheels, adapter configured.");
    }

    private static Transform Find(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase));

    private static void SetIfMissing(SerializedObject owner, string propertyName, UnityEngine.Object value)
    {
        if (value == null) return;
        var property = owner.FindProperty(propertyName);
        if (property != null && property.objectReferenceValue == null)
            property.objectReferenceValue = value;
    }
}
