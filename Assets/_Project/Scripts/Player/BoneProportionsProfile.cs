using System;
using UnityEngine;

namespace OpenworldCrime.Player
{
    /// <summary>
    /// Arm proportions for a character model, shared by every copy of it (the player and the seated driver visuals in the
    /// drivable cars), so they always match. 1 = the model as imported. Read by <see cref="CharacterBoneProportions"/>.
    /// Editing the asset in Play mode updates the characters live.
    /// </summary>
    [CreateAssetMenu(menuName = "OpenworldCrime/Characters/Bone Proportions Profile", fileName = "BoneProportionsProfile")]
    public class BoneProportionsProfile : ScriptableObject
    {
        [Header("Bone Scale (applied to both sides, 1 = original model)")]
        [Tooltip("Clavicle. Below 1 pulls the shoulder joints in toward the neck (narrower shoulders) and slims the shoulder top.")]
        [SerializeField, Range(0.6f, 1.3f)] private float shoulderScale = 0.88f;
        [Tooltip("Upper arm thickness (deltoid, biceps, sleeve).")]
        [SerializeField, Range(0.6f, 1.3f)] private float upperArmScale = 0.84f;
        [Tooltip("Forearm thickness.")]
        [SerializeField, Range(0.6f, 1.3f)] private float forearmScale = 0.92f;
        [Tooltip("Whole hand including the fingers. Held weapons keep their size.")]
        [SerializeField, Range(0.6f, 1.3f)] private float handScale = 0.86f;
        [Tooltip("Keeps the elbow and wrist where the animation and IK put them, so the arm only gets slimmer, not shorter.")]
        [SerializeField] private bool keepArmLength = true;

        public float ShoulderScale => Mathf.Max(0.01f, shoulderScale);
        public float UpperArmScale => Mathf.Max(0.01f, upperArmScale);
        public float ForearmScale => Mathf.Max(0.01f, forearmScale);
        public float HandScale => Mathf.Max(0.01f, handScale);
        public bool KeepArmLength => keepArmLength;

        /// <summary>Raised when a value is edited in the Inspector, so characters using the profile re-apply it.</summary>
        public event Action Changed;

        private void OnValidate()
        {
            Changed?.Invoke();
        }
    }
}
