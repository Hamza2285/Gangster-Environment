using UnityEngine;

// On the ambulance during a mission: reports how hard each hit was (km/h lost along the contact).
public class AmbulanceCrashSensor : MonoBehaviour
{
    public event System.Action<float> Crashed;
    float lastAt;

    void OnCollisionEnter(Collision collision)
    {
        if (collision.contactCount == 0 || Time.time - lastAt < 0.4f) return;
        if (collision.collider != null && collision.collider.isTrigger) return;
        float speed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, collision.GetContact(0).normal)) * 3.6f;
        lastAt = Time.time;
        Crashed?.Invoke(speed);
    }
}
