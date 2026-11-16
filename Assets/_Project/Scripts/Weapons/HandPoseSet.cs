using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Weapons
{
    /// <summary>How far one finger bends at each joint (degrees, toward the palm side) and how far it fans sideways.</summary>
    [Serializable]
    public class FingerCurl
    {
        public float proximal;
        public float intermediate;
        public float distal;
        [Tooltip("Sideways fan of the finger at the knuckle (+ toward the index side).")]
        public float spread;

        public FingerCurl(float proximal, float intermediate, float distal, float spread = 0f)
        {
            this.proximal = proximal;
            this.intermediate = intermediate;
            this.distal = distal;
            this.spread = spread;
        }
    }

    /// <summary>One authored hand shape. Directions are rig-independent: "palm side" is toward the gun part the hand holds.</summary>
    [Serializable]
    public class HandPose
    {
        public FingerCurl index = new FingerCurl(50f, 60f, 35f);
        public FingerCurl middle = new FingerCurl(55f, 65f, 40f);
        public FingerCurl ring = new FingerCurl(58f, 65f, 40f);
        public FingerCurl little = new FingerCurl(60f, 65f, 40f);
        [Tooltip("Thumb base direction in the hand frame: x toward the index side, y toward the palm side, z along the fingers.")]
        public Vector3 thumbDirection = new Vector3(0.45f, 0.55f, 0.7f);
        public FingerCurl thumb = new FingerCurl(0f, 20f, 15f);
    }

    public enum SupportWrapTarget
    {
        /// <summary>Left fingers wrap the handguard (bore line).</summary>
        Handguard,
        /// <summary>Left fingers wrap the grip / the right hand (pistol cup).</summary>
        Grip
    }

    [Serializable]
    public class WeaponHandPoses
    {
        public string name;
        public List<WeaponCategory> categories = new List<WeaponCategory>();
        [Tooltip("Right hand round the grip, index finger straight beside the trigger guard.")]
        public HandPose rightReady = new HandPose();
        [Tooltip("Right hand while firing: index finger on the trigger.")]
        public HandPose rightFire = new HandPose();
        public HandPose leftSupport = new HandPose();
        public SupportWrapTarget leftWrap = SupportWrapTarget.Handguard;
    }

    /// <summary>
    /// Finger poses per weapon category for <see cref="OpenworldCrime.Player.WeaponHandPose"/>. Data only; runtime state
    /// (blend weights) lives on the component.
    /// </summary>
    [CreateAssetMenu(menuName = "OpenworldCrime/Weapons/Hand Pose Set", fileName = "HandPoseSet")]
    public class HandPoseSet : ScriptableObject
    {
        [Tooltip("Grip centre in weapon space (where the right palm wraps); the right fingers curl toward it.")]
        public Vector3 gripPoint = new Vector3(0.033f, -0.03f, 0.13f);
        [Tooltip("Blend speed (weight per second) in and out.")]
        public float blendSpeed = 6f;
        [Tooltip("The support hand is posed only when it is this close to its IK target (m).")]
        public float supportReach = 0.08f;
        public List<WeaponHandPoses> poses = new List<WeaponHandPoses>();

        public WeaponHandPoses Find(WeaponCategory category)
        {
            for (int i = 0; i < poses.Count; i++)
                if (poses[i] != null && poses[i].categories.Contains(category)) return poses[i];
            return null;
        }
    }
}
