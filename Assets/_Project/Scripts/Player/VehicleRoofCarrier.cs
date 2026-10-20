using Invector.vCharacterController;
using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Lets the on-foot player ride on top of a moving vehicle. Invector has no moving-platform handling for physics bodies
    /// (its grounded movement overwrites the Rigidbody velocity every physics step, so a car would slide away under a standing
    /// character), and parenting the character to a physics vehicle is not safe. Instead, while Invector reports the character
    /// grounded on a collider of a vehicle layer, the vehicle's point velocity at the contact is added on top of the velocity
    /// Invector just set (this component runs after Invector's FixedUpdate), and the vehicle's yaw rate turns the character.
    /// Because Invector rewrites the velocity every grounded step, nothing accumulates; because the character then moves with
    /// the surface, contact friction no longer drags it. When the character leaves the vehicle into the air (jump, walking off
    /// the edge) it keeps the vehicle's horizontal velocity once. Costs nothing unless standing on a vehicle: no physics
    /// queries, it reads Invector's own ground hit.
    /// Note: relies on Invector's rigidbody locomotion (Use Root Motion off, as on Player_Jason); with root motion on,
    /// Invector moves the character in OnAnimatorMove, after this component.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public class VehicleRoofCarrier : MonoBehaviour
    {
        [SerializeField] private vThirdPersonController controller;
        [SerializeField] private Rigidbody body;
        [Tooltip("Layers whose rigidbodies carry the player (vehicle body / panels). Must also be in Invector's Ground Layer.")]
        [SerializeField] private LayerMask vehicleLayers = (1 << 19) | (1 << 20); // Vehicle, VehicleParts
        [Tooltip("Ignore vehicle speeds above this (m/s): a crash or teleport must never launch the player.")]
        [SerializeField, Min(1f)] private float maxCarrySpeed = 40f;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<vThirdPersonController>();
            if (body == null) body = GetComponent<Rigidbody>();
            if (controller == null || body == null)
            {
                Debug.LogError($"[{nameof(VehicleRoofCarrier)}] Missing vThirdPersonController or Rigidbody on '{name}'. " +
                               "The player will not ride on moving vehicles.", this);
                enabled = false;
            }
        }

        private void FixedUpdate()
        {
            // Leaving the vehicle into the air needs nothing: the velocity added last step is already in the body, so the
            // character keeps the vehicle's momentum and Invector's air control takes over smoothly.
            Rigidbody platform = GetGroundVehicle();
            if (platform == null) return;

            Vector3 velocity = platform.GetPointVelocity(controller.groundHit.point);
            velocity.y = 0f; // vertical motion is left to Invector's ground snapping
            if (velocity.sqrMagnitude > maxCarrySpeed * maxCarrySpeed) return;
            body.linearVelocity += velocity;

            float yaw = platform.angularVelocity.y * Mathf.Rad2Deg * Time.fixedDeltaTime;
            if (Mathf.Abs(yaw) > 0.0001f) body.MoveRotation(Quaternion.AngleAxis(yaw, Vector3.up) * body.rotation);
        }

        // Only while Invector's grounded locomotion owns the velocity this step (it rewrites it every such step).
        private Rigidbody GetGroundVehicle()
        {
            if (!controller.enabled || body.isKinematic || !controller.isGrounded || controller.isJumping || controller.isSliding
                || controller.ragdolled || controller.customAction || controller.lockMovement || controller.lockAnimMovement) return null;
            Collider ground = controller.groundHit.collider;
            if (ground == null || (vehicleLayers.value & (1 << ground.gameObject.layer)) == 0) return null;
            Rigidbody rb = ground.attachedRigidbody;
            return rb != null && !rb.isKinematic ? rb : null;
        }
    }
}
