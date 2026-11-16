using System.Collections.Generic;
using UnityEngine;

// Corrects the proportions of the player model without touching the mesh: the hands are modelled about a third too big
// and the shoulders sit low and pinched in when the arms hang down.
// Hands: the hand bones are scaled down once (humanoid animation never writes bone scale). Anything a hand holds is
// scaled back up so weapons keep their size.
// Shoulders: the collarbones are lifted and opened a little every frame after the animation and before the weapon IK,
// so aiming still lines up.
[DefaultExecutionOrder(-500)]
public class CharacterBodyTuning : MonoBehaviour
{
    [Range(0.6f, 1f)] public float handScale = 0.8f;
    [Tooltip("Degrees the collarbones are lifted.")]
    [Range(0f, 20f)] public float shoulderLift = 4f;
    [Tooltip("Degrees the collarbones are drawn back, squaring the chest.")]
    [Range(0f, 15f)] public float shoulderBack = 3f;

    Animator animator;
    Transform leftHand, rightHand, leftShoulder, rightShoulder, leftArm, rightArm;
    readonly HashSet<Transform> skeleton = new HashSet<Transform>();
    readonly Dictionary<Transform, Vector3> heldScale = new Dictionary<Transform, Vector3>();
    readonly List<Transform> released = new List<Transform>();
    Quaternion leftWritten, rightWritten;
    float appliedHandScale = 1f;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) { enabled = false; return; }
        leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        leftShoulder = animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
        rightShoulder = animator.GetBoneTransform(HumanBodyBones.RightShoulder);
        leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            foreach (var b in r.bones) if (b != null) skeleton.Add(b);
    }

    void LateUpdate()
    {
        if (!Mathf.Approximately(appliedHandScale, handScale)) SetHandScale(handScale);
        KeepHeldItemsSize(leftHand);
        KeepHeldItemsSize(rightHand);
        Lift(leftShoulder, leftArm, ref leftWritten);
        Lift(rightShoulder, rightArm, ref rightWritten);
    }

    void OnDisable()
    {
        if (leftHand != null) SetHandScale(1f);
    }

    void SetHandScale(float scale)
    {
        if (leftHand != null) leftHand.localScale = Vector3.one * scale;
        if (rightHand != null) rightHand.localScale = Vector3.one * scale;
        appliedHandScale = scale;
        foreach (var pair in heldScale) if (pair.Key != null) pair.Key.localScale = pair.Value / scale;
    }

    // Weapons and handlers parented to a hand would shrink with it.
    void KeepHeldItemsSize(Transform hand)
    {
        if (hand == null) return;
        // Something that left the hand gets its own size back.
        if (heldScale.Count > 0)
        {
            released.Clear();
            foreach (var pair in heldScale)
                if (pair.Key == null || (pair.Key.parent != leftHand && pair.Key.parent != rightHand)) released.Add(pair.Key);
            foreach (var item in released)
            {
                if (item != null) item.localScale = heldScale[item];
                heldScale.Remove(item);
            }
        }
        for (int i = 0; i < hand.childCount; i++)
        {
            var child = hand.GetChild(i);
            if (skeleton.Contains(child) || heldScale.ContainsKey(child)) continue;
            heldScale[child] = child.localScale;
            child.localScale = child.localScale / appliedHandScale;
        }
    }

    // Rotates the collarbone so the upper arm moves up and back. Skipped on frames the animator did not write the bone,
    // so the offset never adds up.
    void Lift(Transform shoulder, Transform arm, ref Quaternion written)
    {
        if (shoulder == null || arm == null) return;
        if (shoulder.localRotation == written) return;
        Vector3 along = (arm.position - shoulder.position).normalized;
        Vector3 up = transform.up, back = -transform.forward;
        // Lift: turn the collarbone towards up. Back: turn it towards the character's back.
        Quaternion lift = Quaternion.AngleAxis(shoulderLift, Vector3.Cross(along, up).normalized);
        Quaternion pull = Quaternion.AngleAxis(shoulderBack, Vector3.Cross(along, back).normalized);
        shoulder.rotation = pull * lift * shoulder.rotation;
        written = shoulder.localRotation;
    }
}
