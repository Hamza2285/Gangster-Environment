using System.Collections.Generic;
using UnityEngine;

// What the map, the taxi and the route line need from whoever is playing.
public interface IGamePlayer
{
    Transform transform { get; }
    Transform CameraPivot { get; }
    bool ControlEnabled { get; }
    float FacingYaw { get; }
    void SetControl(bool enabledNow);
    void LockCursor(bool locked);
    void ResetLook();
    void SetRidingPose(Vector3 position, float yaw);
    void Teleport(Vector3 feetPosition, float yaw);
}

// A controller can implement IGamePlayer and register itself. Without one, these systems work with any object tagged
// Player (or the first CharacterController), so any character controller can be used without changing them.
public static class GamePlayer
{
    static IGamePlayer registered;
    static StandInPlayer standIn;

    // A full-screen menu is up, so the map and the taxi should keep out of the way.
    public static bool MenuOpen { get; set; }

    // The player is driving, so nothing should whistle for a taxi.
    public static bool InVehicle { get; set; }

    // Extra keys for the on-screen hint, from features this project may or may not have.
    public static string ExtraKeysHint { get; set; } = "";

    public static IGamePlayer Current
    {
        get
        {
            if (registered is Object alive && alive != null) return registered;
            if (standIn != null && standIn.IsAlive) return standIn;
            var found = FindPlayerObject();
            standIn = found != null ? new StandInPlayer(found) : null;
            return standIn;
        }
    }

    public static void Register(IGamePlayer player)
    {
        registered = player;
    }

    public static void Unregister(IGamePlayer player)
    {
        if (registered == player) registered = null;
    }

    static Transform FindPlayerObject()
    {
        var tagged = GameObject.FindWithTag("Player");
        if (tagged != null) return tagged.transform;
        var controller = Object.FindFirstObjectByType<CharacterController>();
        return controller != null ? controller.transform : null;
    }

    sealed class StandInPlayer : IGamePlayer
    {
        readonly Transform root;
        readonly CharacterController controller;
        readonly List<Behaviour> paused = new List<Behaviour>();
        readonly List<Renderer> hidden = new List<Renderer>();
        bool controlEnabled = true;

        public StandInPlayer(Transform root)
        {
            this.root = root;
            controller = root.GetComponent<CharacterController>();
        }

        public bool IsAlive => root != null;
        public Transform transform => root;
        public Transform CameraPivot => Camera.main != null ? Camera.main.transform : null;
        public bool ControlEnabled => controlEnabled;
        public float FacingYaw => root.eulerAngles.y;

        // Pauses the scripts on the player object, which is where a controller lives.
        public void SetControl(bool enabledNow)
        {
            if (controlEnabled == enabledNow) return;
            controlEnabled = enabledNow;
            if (enabledNow)
            {
                foreach (var behaviour in paused)
                {
                    if (behaviour != null) behaviour.enabled = true;
                }
                paused.Clear();
                return;
            }
            foreach (var behaviour in root.GetComponents<MonoBehaviour>())
            {
                if (!behaviour.enabled) continue;
                behaviour.enabled = false;
                paused.Add(behaviour);
            }
        }

        public void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        public void ResetLook()
        {
        }

        public void SetRidingPose(Vector3 position, float yaw)
        {
            MoveTo(position, yaw);
            foreach (var part in root.GetComponentsInChildren<Renderer>())
            {
                if (!part.enabled) continue;
                part.enabled = false;
                hidden.Add(part);
            }
        }

        public void Teleport(Vector3 feetPosition, float yaw)
        {
            MoveTo(feetPosition, yaw);
            if (controller != null) controller.enabled = true;
            foreach (var part in hidden)
            {
                if (part != null) part.enabled = true;
            }
            hidden.Clear();
        }

        void MoveTo(Vector3 position, float yaw)
        {
            if (controller != null) controller.enabled = false;
            root.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }
    }
}
