using OpenworldCrime.Player;
using UnityEngine;

/// <summary>
/// Keeps AI cars (traffic, police, taxis, fleeing drivers) from running over the player on foot: a car whose path (along its
/// nose, bent by its current turn) meets the player brakes as hard as it takes to stop short.
/// </summary>
public static class PlayerCarGuard
{
    const float Margin = 0.55f;      // clear space (m) wanted beside the car
    const float Gap = 1.8f;          // ... and in front of it
    const float MaxBrake = 14f;      // m/s2

    static GameObject player;
    static PlayerVehicleInteraction interaction;

    /// <summary>Where the player stands, when on foot.</summary>
    public static bool OnFoot(out Vector3 position)
    {
        position = Vector3.zero;
        var current = GamePlayer.Current;
        if (current == null) return false;
        if (player != current.transform.gameObject)
        {
            player = current.transform.gameObject;
            interaction = player.GetComponent<PlayerVehicleInteraction>();
        }
        position = player.transform.position;
        return interaction == null || interaction.State != VehicleInteractionState.InVehicle;
    }

    /// <summary>Free metres between the car's nose and the player in its way; infinite when nobody is in its way.</summary>
    public static float Distance(Transform car, Rigidbody body, float halfLength, float halfWidth, float range)
    {
        if (!OnFoot(out Vector3 at)) return float.PositiveInfinity;
        Vector3 to = at - car.position;
        if (Mathf.Abs(to.y) > 3f || to.sqrMagnitude > (range + halfLength) * (range + halfLength)) return float.PositiveInfinity;
        Vector3 forward = car.forward;
        float along = Vector3.Dot(to, forward);
        if (along < halfLength * 0.6f) return float.PositiveInfinity;
        float speed = Vector3.Dot(body.linearVelocity, forward);
        // Where the car's middle will be when it gets that far (a car turning into the player counts, one turning away does not).
        float curve = speed > 1.5f ? Mathf.Clamp(body.angularVelocity.y / speed, -0.15f, 0.15f) : 0f;
        float side = Vector3.Dot(to, car.right);
        float bent = side - 0.5f * curve * along * along;
        float wide = halfWidth + Margin;
        bool near = along < halfLength + 8f && Mathf.Abs(side) < wide;
        if (Mathf.Abs(bent) > wide && !near) return float.PositiveInfinity;
        return Mathf.Max(0f, along - halfLength);
    }

    /// <summary>Brakes the car's body when its own brakes are not going to be enough. Call every physics step.</summary>
    public static float Brake(Transform car, Rigidbody body, float halfLength, float halfWidth)
    {
        float speed = Vector3.Dot(body.linearVelocity, car.forward);
        float distance = Distance(car, body, halfLength, halfWidth, 6f + Mathf.Max(0f, speed) * 2.2f);
        if (float.IsPositiveInfinity(distance) || speed < 0.2f) return distance;
        float needed = speed * speed / (2f * Mathf.Max(0.15f, distance - Gap));
        if (needed > 3f)
        {
            float step = Mathf.Min(Mathf.Min(needed, MaxBrake) * Time.fixedDeltaTime, speed);
            body.AddForce(-car.forward * step, ForceMode.VelocityChange);
        }
        return distance;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { player = null; interaction = null; }
}
