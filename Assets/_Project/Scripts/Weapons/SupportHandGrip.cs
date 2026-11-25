using System.Collections.Generic;
using Invector.vCharacterController;
using Invector.vShooter;
using OpenworldCrime.Player;
using UnityEngine;

namespace OpenworldCrime.Weapons
{
    /// <summary>
    /// Puts the support (left) hand properly on the gun while Invector's support-hand IK holds it there:
    /// - Long guns: moves the IK target until the palm actually touches the handguard's underside (the gun's "SupportPalm"
    ///   point, written by the weapon builder). The target alone was set for the template character's hand, so on our
    ///   characters the palm hung 2-3 cm under / beside the handguard. Measured on the live hand, so it fits any rig and hand size.
    /// - Closes the fingers: the shooter animations leave that hand open with straight fingers (a rifle rested on fingertips,
    ///   the pistol's second hand hung open). Long guns: fingers wrap up the handguard's side. Pistols: they wrap the right fist.
    /// Runs after Invector's IK (LateUpdate) and only adds on top of the animated pose, faded with the support IK weight, so
    /// equipping, reloading, melee and unarmed poses are untouched.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(32000)]
    public class SupportHandGrip : MonoBehaviour
    {
        /// <summary>Child of a long gun marking where the support palm touches the handguard.</summary>
        public const string SupportPalmName = "SupportPalm";

        [System.Serializable]
        public struct Curl
        {
            [Tooltip("Extra bend at the knuckle, middle and tip joints (degrees, toward the palm).")]
            public float proximal, intermediate, distal;
            [Tooltip("Extra bend of the thumb's middle and tip joints toward the palm.")]
            public float thumb;
            public Curl(float proximal, float intermediate, float distal, float thumb)
            {
                this.proximal = proximal;
                this.intermediate = intermediate;
                this.distal = distal;
                this.thumb = thumb;
            }
        }

        [Tooltip("Rifles, SMGs, shotguns, sniper and launcher: fingers wrap up around the handguard.")]
        [SerializeField] private Curl longGun = new Curl(50f, 88f, 70f, 30f);
        [Tooltip("Pistols: fingers wrap over the right hand's fingers.")]
        [SerializeField] private Curl pistol = new Curl(48f, 58f, 38f, 18f);
        [Tooltip("How fast the grip closes / opens (per second).")]
        [SerializeField, Min(0.1f)] private float blendSpeed = 8f;

        [Header("Palm on the handguard")]
        [Tooltip("Moves the long guns' support IK target so the palm touches the handguard.")]
        [SerializeField] private bool fitPalmToHandguard = true;
        [Tooltip("How fast the target follows the remaining palm gap (per second).")]
        [SerializeField, Min(0.1f)] private float fitSpeed = 12f;
        [Tooltip("Largest target correction (metres); beyond it the arm cannot reach anyway.")]
        [SerializeField, Min(0f)] private float maxFit = 0.07f;
        [Tooltip("Palm thickness as a fraction of the wrist-to-knuckle length (palm skin above the bone line).")]
        [SerializeField, Range(0f, 0.3f)] private float palmThickness = 0.11f;

        private static readonly HumanBodyBones[,] Fingers =
        {
            { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal },
            { HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal },
            { HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal },
            { HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal },
        };

        private Animator animator;
        private vShooterMeleeInput input;
        private vShooterManager shooter;
        private PlayerWeaponController weapons;
        private Transform hand, thumbIntermediate, thumbDistal;
        private readonly Transform[,] bones = new Transform[4, 3];
        private float weight;
        private bool valid;

        // Per weapon instance: its original support target (weapon space) and the palm correction found for it.
        private readonly Dictionary<vShooterWeapon, Vector3> baseTargets = new Dictionary<vShooterWeapon, Vector3>();
        private readonly Dictionary<vShooterWeapon, Vector3> fits = new Dictionary<vShooterWeapon, Vector3>();

        private void Awake()
        {
            animator = GetComponent<Animator>();
            input = GetComponent<vShooterMeleeInput>();
            shooter = GetComponent<vShooterManager>();
            weapons = GetComponent<PlayerWeaponController>();
            if (animator == null || !animator.isHuman || input == null || shooter == null) return;
            hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            thumbIntermediate = animator.GetBoneTransform(HumanBodyBones.LeftThumbIntermediate);
            thumbDistal = animator.GetBoneTransform(HumanBodyBones.LeftThumbDistal);
            valid = hand != null;
            for (int f = 0; f < 4; f++)
                for (int j = 0; j < 3; j++)
                {
                    bones[f, j] = animator.GetBoneTransform(Fingers[f, j]);
                    if (bones[f, j] == null) valid = false;
                }
        }

        private void LateUpdate()
        {
            if (!valid) return;
            vShooterWeapon weapon = shooter.CurrentWeapon;
            bool holding = weapon != null && weapon.handIKTarget != null && input.IsSupportHandIKEnabled && !input.isReloading;
            float target = holding ? Mathf.Clamp01(input.supportIKWeight) : 0f;
            weight = Mathf.MoveTowards(weight, target, blendSpeed * Time.deltaTime);
            if (weight <= 0.001f || weapon == null) return;

            WeaponDefinition definition = weapons != null ? weapons.GetCurrentWeaponDefinition() : null;
            bool isPistol = definition != null ? definition.category == WeaponCategory.Pistol : weapon.weaponCategory.ToLowerInvariant().Contains("pistol");
            Curl curl = isPistol ? pistol : longGun;

            // Palm side of the left hand from its own bones: fingers along, index-to-little across (mirror of the right hand).
            Vector3 knuckleIndex = bones[0, 0].position, knuckleLittle = bones[3, 0].position;
            Vector3 knuckles = (knuckleIndex + knuckleLittle) * 0.5f;
            Vector3 along = bones[1, 0].position - hand.position;
            Vector3 palm = -Vector3.Cross(along, knuckleIndex - knuckleLittle).normalized;

            if (fitPalmToHandguard && !isPistol && holding) FitPalm(weapon, knuckles, palm, along.magnitude);

            for (int f = 0; f < 4; f++)
            {
                Bend(bones[f, 0], bones[f, 1].position - bones[f, 0].position, palm, curl.proximal);
                Bend(bones[f, 1], bones[f, 2].position - bones[f, 1].position, palm, curl.intermediate);
                Bend(bones[f, 2], bones[f, 2].position - bones[f, 1].position, palm, curl.distal);
            }
            if (thumbIntermediate != null && thumbDistal != null)
            {
                Bend(thumbIntermediate, thumbDistal.position - thumbIntermediate.position, palm, curl.thumb);
                Bend(thumbDistal, thumbDistal.position - thumbIntermediate.position, palm, curl.thumb);
            }
        }

        // Moves the support IK target (used by Invector's IK next frame) by the gap left between the palm and the handguard.
        private void FitPalm(vShooterWeapon weapon, Vector3 knuckles, Vector3 palm, float handLength)
        {
            Transform contact = weapon.transform.Find(SupportPalmName);
            if (contact == null) return;
            Transform gun = weapon.transform;
            if (!baseTargets.TryGetValue(weapon, out Vector3 baseTarget))
            {
                baseTarget = gun.InverseTransformPoint(weapon.handIKTarget.position);
                baseTargets[weapon] = baseTarget;
                fits[weapon] = Vector3.zero;
            }
            Vector3 fit = fits[weapon];
            if (weight > 0.9f)
            {
                Vector3 palmSurface = Vector3.Lerp(hand.position, knuckles, 0.55f) + palm * (handLength * palmThickness);
                Vector3 gap = gun.InverseTransformVector(contact.position - palmSurface);
                fit = Vector3.ClampMagnitude(fit + gap * Mathf.Clamp01(fitSpeed * Time.deltaTime), maxFit);
                fits[weapon] = fit;
            }
            weapon.handIKTarget.position = gun.TransformPoint(baseTarget + fit);
        }

        // Turns a finger joint so its bone direction moves toward the palm side by the given angle (scaled by the blend).
        private void Bend(Transform bone, Vector3 direction, Vector3 palm, float degrees)
        {
            if (degrees == 0f || direction.sqrMagnitude < 1e-8f) return;
            Vector3 axis = Vector3.Cross(direction, palm);
            if (axis.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.AngleAxis(degrees * weight, axis.normalized) * bone.rotation;
        }
    }
}
