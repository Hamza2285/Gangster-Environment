
using UnityEngine;

public class CarController : MonoBehaviour
{
    [Header("Car Movement")]
    public float speed = 10f;
    public float turnSpeed = 100f;

    [Header("Third Person Camera")]
    public Vector3 cameraOffset = new Vector3(0f, 4f, -7f);
    public float cameraFollowSpeed = 5f;
    public float cameraLookSpeed = 5f;
    public float lookHeight = 1.5f;

    private Transform cam;

    void Start()
    {
        if (Camera.main != null)
        {
            cam = Camera.main.transform;
        }
    }

    void Update()
    {
        float moveInput = Input.GetAxis("Vertical");
        float turnInput = Input.GetAxis("Horizontal");

        transform.Translate(
            Vector3.forward * moveInput * speed * Time.deltaTime
        );

        if (Mathf.Abs(moveInput) > 0.01f)
        {
            transform.Rotate(
                Vector3.up * turnInput * turnSpeed *
                Mathf.Sign(moveInput) * Time.deltaTime
            );
        }
    }

    void LateUpdate()
    {
        if (cam == null) return;

        Vector3 targetPosition =
            transform.position + transform.rotation * cameraOffset;

        cam.position = Vector3.Lerp(
            cam.position,
            targetPosition,
            cameraFollowSpeed * Time.deltaTime
        );

        Vector3 lookTarget = transform.position + Vector3.up * lookHeight;

        Quaternion targetRotation = Quaternion.LookRotation(
            lookTarget - cam.position
        );

        cam.rotation = Quaternion.Slerp(
            cam.rotation,
            targetRotation,
            cameraLookSpeed * Time.deltaTime
        );
    }
}
