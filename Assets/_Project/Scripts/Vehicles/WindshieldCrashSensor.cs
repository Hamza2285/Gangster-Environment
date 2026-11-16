using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Put on the car the player drives. Remembers how fast the car was going at the start of every physics step, so on a
    /// head-on hit it knows the speed before the impact and how much of it was lost, and asks the driver's
    /// <see cref="PlayerVehicleInteraction"/> to throw him out through the windscreen (GTA IV style) when the stop was hard.
    /// Bursts the windscreen into glass as he goes.
    /// </summary>
    [DisallowMultipleComponent]
    public class WindshieldCrashSensor : MonoBehaviour
    {
        // Speed (km/h) the car must have been doing just before the hit.
        public static float MinSpeedKmh = 95f;
        // Speed (km/h) the car must lose in the hit along its forward direction.
        public static float MinSpeedLossKmh = 65f;
        // A moving thing counts as a crash only if it is at least this heavy (kg): another car, not a bin.
        public static float MinOtherMass = 400f;
        // Something fixed counts only if it is at least this big (m across): a wall, not a sign post.
        public static float MinStaticSize = 1.5f;

        private Rigidbody body;
        private PlayerVehicleInteraction driver;
        private Vector3 velocityBefore;
        private static Material glassMaterial;

        public static void Watch(GameObject vehicle, PlayerVehicleInteraction driver)
        {
            var body = vehicle.GetComponentInParent<Rigidbody>();
            if (body == null) return;
            var sensor = body.GetComponent<WindshieldCrashSensor>();
            if (sensor == null) sensor = body.gameObject.AddComponent<WindshieldCrashSensor>();
            sensor.body = body;
            if (sensor.driver != null) sensor.driver.OnWindshieldEjected -= sensor.Burst;
            sensor.driver = driver;
            driver.OnWindshieldEjected += sensor.Burst;
            sensor.velocityBefore = body.linearVelocity;
        }

        private void OnDestroy()
        {
            if (driver != null) driver.OnWindshieldEjected -= Burst;
        }

        private void FixedUpdate()
        {
            if (body != null) velocityBefore = body.linearVelocity;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (driver == null || body == null || !driver.IsInVehicle || collision.contactCount == 0) return;
            if (!IsSolid(collision)) return;

            // Head-on: the contact is in the front half of the car and the car was moving forward into it.
            Vector3 forward = transform.forward;
            Vector3 contact = collision.GetContact(0).point;
            if (transform.InverseTransformPoint(contact).z < 0f) return;
            float before = Vector3.Dot(velocityBefore, forward);
            if (before <= 0f) return;
            float after = Vector3.Dot(body.linearVelocity, forward);
            float lostKmh = (before - after) * 3.6f;
            if (before * 3.6f < MinSpeedKmh || lostKmh < MinSpeedLossKmh) return;
            driver.TryWindshieldEject(velocityBefore, lostKmh);
        }

        // Another car or something big and fixed (a wall, a building, a barrier). Not people, not props that break or
        // fall over, not small light things.
        private static bool IsSolid(Collision collision)
        {
            var other = collision.collider;
            if (other == null || other.isTrigger) return false;
            if (other.GetComponentInParent<Pedestrians.PedestrianDamage>() != null) return false;
            if (other.GetComponentInParent<Environment.BreakableProp>() != null) return false;
            if (other.GetComponentInParent<CharacterController>() != null) return false;
            if (other.GetComponentInParent<Invector.vCharacterController.vCharacter>() != null) return false;
            var otherBody = collision.rigidbody;
            if (otherBody != null && !otherBody.isKinematic) return otherBody.mass >= MinOtherMass;
            if (otherBody != null && otherBody.GetComponentInParent<IVehicleController>() != null) return true;
            Vector3 size = other.bounds.size;
            return Mathf.Max(size.x, size.y, size.z) >= MinStaticSize && size.y >= 0.6f;
        }

        // The windscreen in pieces, flying on with the driver.
        private void Burst(IVehicleController vehicle, Vector3 at, Vector3 launch)
        {
            if (glassMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                glassMaterial = new Material(shader != null ? shader : Shader.Find("Standard")) { name = "Windshield Shards" };
                glassMaterial.color = new Color(0.72f, 0.88f, 0.95f, 1f);
                if (glassMaterial.HasProperty("_Smoothness")) glassMaterial.SetFloat("_Smoothness", 0.95f);
                if (glassMaterial.HasProperty("_Metallic")) glassMaterial.SetFloat("_Metallic", 0.2f);
            }
            var holder = new GameObject("Windshield Shards");
            holder.transform.position = at;
            Vector3 right = Vector3.Cross(Vector3.up, launch.sqrMagnitude > 0.01f ? launch.normalized : transform.forward);
            for (int i = 0; i < 46; i++)
            {
                var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shard.name = "Shard";
                shard.transform.SetParent(holder.transform, false);
                shard.transform.position = at + right * Random.Range(-0.7f, 0.7f) + Vector3.up * Random.Range(-0.25f, 0.25f);
                shard.transform.rotation = Random.rotation;
                float size = Random.Range(0.025f, 0.09f);
                shard.transform.localScale = new Vector3(size, size * Random.Range(0.1f, 0.25f), size * Random.Range(0.6f, 1.4f));
                shard.GetComponent<Renderer>().sharedMaterial = glassMaterial;
                shard.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var piece = shard.AddComponent<Rigidbody>();
                piece.mass = 0.02f;
                piece.linearVelocity = launch * Random.Range(0.35f, 0.75f) + Random.insideUnitSphere * 3f + Vector3.up * Random.Range(0.5f, 3f);
                piece.angularVelocity = Random.insideUnitSphere * 25f;
                piece.collisionDetectionMode = CollisionDetectionMode.Discrete;
            }
            Destroy(holder, 4f);
        }
    }
}
