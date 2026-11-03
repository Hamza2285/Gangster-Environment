using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonPlayer : MonoBehaviour
{
    public static FirstPersonPlayer Instance { get; private set; }

    [Header("Movement")]
    public float walkSpeed = 4.5f;
    public float sprintSpeed = 8f;
    public float jumpHeight = 1.1f;
    public float gravity = -20f;

    [Header("Look")]
    public Transform cameraPivot;
    public float lookSensitivity = 1f;
    public float maxPitch = 85f;

    CharacterController controller;
    float pitch;
    float verticalVelocity;
    bool controlEnabled = true;
    Vector3 lastSafePosition;
    float lastSafeTime;
    int ignoreLookFrames;

    public bool ControlEnabled => controlEnabled;

    void Awake()
    {
        Instance = this;
        controller = GetComponent<CharacterController>();
        lastSafePosition = transform.position;
        LockCursor(true);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        HandleCursor();
        if (!controlEnabled) return;

        if (ignoreLookFrames > 0)
        {
            ignoreLookFrames--;
        }
        else if (Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 look = GameInput.Look * lookSensitivity;
            transform.Rotate(0f, look.x, 0f);
            pitch = Mathf.Clamp(pitch - look.y, -maxPitch, maxPitch);
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        Vector2 input = GameInput.Move;
        Vector3 move = (transform.right * input.x + transform.forward * input.y) * (GameInput.Sprint ? sprintSpeed : walkSpeed);

        if (controller.isGrounded)
        {
            if (verticalVelocity < 0f) verticalVelocity = -2f;
            if (GameInput.JumpDown) verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            if (Time.time - lastSafeTime > 0.5f)
            {
                lastSafePosition = transform.position;
                lastSafeTime = Time.time;
            }
        }
        verticalVelocity += gravity * Time.deltaTime;
        move.y = verticalVelocity;
        controller.Move(move * Time.deltaTime);

        // Fell off the world (water, gaps): put the player back where they last stood.
        if (transform.position.y < lastSafePosition.y - 12f)
        {
            Teleport(lastSafePosition, transform.eulerAngles.y);
        }
    }

    void HandleCursor()
    {
        if (!controlEnabled) return;
        // The Esc that just closed the map must not also free the cursor.
        var map = GameMapUI.Instance;
        if (map != null && map.ClosedThisFrame) return;
        if (GameInput.CancelDown) LockCursor(Cursor.lockState != CursorLockMode.Locked);
        else if (Cursor.lockState != CursorLockMode.Locked && GameInput.PrimaryClickDown && !IsPointerOverUI()) LockCursor(true);
    }

    static bool IsPointerOverUI()
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        return es != null && es.IsPointerOverGameObject();
    }

    public void LockCursor(bool locked)
    {
        // Locking recentres the cursor, which reads as one big mouse movement.
        if (locked && Cursor.lockState != CursorLockMode.Locked) ignoreLookFrames = 5;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    // Level view, used when arriving somewhere new.
    public void ResetLook()
    {
        pitch = 0f;
        if (cameraPivot != null) cameraPivot.localRotation = Quaternion.identity;
    }

    public void SetControl(bool enabledNow)
    {
        controlEnabled = enabledNow;
        verticalVelocity = 0f;
    }

    // While riding the player has no body: it travels with the car so traffic and the minimap follow.
    public void SetRidingPose(Vector3 position, float yaw)
    {
        controller.enabled = false;
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
    }

    public void Teleport(Vector3 feetPosition, float yaw)
    {
        controller.enabled = false;
        transform.SetPositionAndRotation(feetPosition, Quaternion.Euler(0f, yaw, 0f));
        controller.enabled = true;
        verticalVelocity = 0f;
        lastSafePosition = feetPosition;
        lastSafeTime = Time.time;
    }
}
