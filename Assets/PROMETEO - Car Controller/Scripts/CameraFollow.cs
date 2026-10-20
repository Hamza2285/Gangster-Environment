using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Car")]
    public Transform carTransform;

    [Header("Camera Position")]
    public float distance = 6f;
    public float height = 3f;

    [Header("Follow Settings")]
    [Range(1f, 20f)]
    public float followSpeed = 8f;

    [Range(1f, 20f)]
    public float rotationSpeed = 8f;

    [Header("Look Settings")]
    public float lookHeight = 1.2f;

    [Header("Camera Collision")]
    public bool enableCollision = true;
    public float collisionRadius = 0.3f;
    public LayerMask collisionLayers = -1;

    private Vector3 velocity;

    void LateUpdate()
    {
        if (carTransform == null)
            return;

        // Target position behind the car
        Vector3 targetPosition =
            carTransform.position
            - carTransform.forward * distance
            + Vector3.up * height;

        // Camera collision
        if (enableCollision)
        {
            Vector3 direction = targetPosition - carTransform.position;
            float targetDistance = direction.magnitude;

            if (Physics.SphereCast(
                carTransform.position,
                collisionRadius,
                direction.normalized,
                out RaycastHit hit,
                targetDistance,
                collisionLayers))
            {
                targetPosition =
                    carTransform.position +
                    direction.normalized * (hit.distance - collisionRadius);
            }
        }

        // Smooth camera movement
        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPosition,
            ref velocity,
            1f / followSpeed
        );

        // Look at the car
        Vector3 lookTarget =
            carTransform.position +
            Vector3.up * lookHeight;

        Vector3 lookDirection =
            lookTarget - transform.position;

        if (lookDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(
                    lookDirection,
                    Vector3.up
                );

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }
    }
}