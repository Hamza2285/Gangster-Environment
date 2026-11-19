using UnityEngine;

// Poses the character's right arm after the Animator has run: the hand goes to a point in the world (an order counter, the
// mouth) with a two-bone solve on upper arm and forearm, blended over the animated pose by Weight. Added on demand by whatever
// needs it (FoodStall); costs nothing while Weight is 0.
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]   // after the Animator and Invector's own LateUpdate work
public class PlayerArmReach : MonoBehaviour
{
    public float Weight;
    public Vector3 Target;
    [Tooltip("Where the elbow points, as an offset from the shoulder in the character's own space (right, up, forward).")]
    public Vector3 ElbowHint = new Vector3(0.35f, -0.45f, -0.05f);

    Transform upper, lower, hand;

    public Transform Hand => hand;

    public static PlayerArmReach For(Animator animator)
    {
        if (animator == null || !animator.isHuman) return null;
        var reach = animator.GetComponent<PlayerArmReach>();
        if (reach == null) reach = animator.gameObject.AddComponent<PlayerArmReach>();
        reach.upper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        reach.lower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        reach.hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        return reach;
    }

    void LateUpdate()
    {
        if (Weight <= 0.001f || upper == null || lower == null || hand == null) return;
        Quaternion upperBefore = upper.localRotation, lowerBefore = lower.localRotation;

        Vector3 hint = upper.position + transform.TransformDirection(ElbowHint);
        Solve(hint);

        upper.localRotation = Quaternion.Slerp(upperBefore, upper.localRotation, Weight);
        lower.localRotation = Quaternion.Slerp(lowerBefore, lower.localRotation, Weight);
    }

    // Two-bone solve: the elbow goes where the bone lengths put it (law of cosines), bent towards the hint; the upper arm turns
    // onto the elbow, then the forearm onto the target.
    void Solve(Vector3 hint)
    {
        Vector3 a = upper.position, b = lower.position;
        float lab = (b - a).magnitude, lcb = (hand.position - b).magnitude;
        if (lab < 0.01f || lcb < 0.01f) return;
        Vector3 toTarget = Target - a;
        float lat = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(lab - lcb) + 0.01f, lab + lcb - 0.002f);
        Vector3 along = toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : transform.forward;
        Vector3 bend = Vector3.ProjectOnPlane(hint - a, along);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(-transform.up, along);
        bend.Normalize();
        float x = (lab * lab - lcb * lcb + lat * lat) / (2f * lat);
        float h = Mathf.Sqrt(Mathf.Max(0f, lab * lab - x * x));
        Vector3 elbow = a + along * x + bend * h;

        upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * upper.rotation;
        Vector3 newElbow = lower.position, newHand = hand.position;
        Vector3 goal = a + along * lat;
        lower.rotation = Quaternion.FromToRotation(newHand - newElbow, goal - newElbow) * lower.rotation;
    }
}
