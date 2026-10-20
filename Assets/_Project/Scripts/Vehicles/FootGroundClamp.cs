using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Keeps a character's feet from sinking below a given ground height (Animator foot IK). Used while a driver is pulled out
    /// of a car: the pull-out clip starts from a seat root that sits below the road, so without it the feet dip under the ground
    /// for a moment. Disabled (no cost) unless <see cref="SetGround"/> turned it on; must sit next to the Animator and needs
    /// "IK Pass" on the Animator's base layer.
    /// </summary>
    [DisallowMultipleComponent]
    public class FootGroundClamp : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [Tooltip("Extra height (m) kept between the ankle and the ground.")]
        [SerializeField, Min(0f)] private float ankleHeight = 0.08f;

        private float groundHeight;

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            enabled = false;
        }

        /// <summary>Starts clamping the feet above this world height.</summary>
        public void SetGround(float height)
        {
            groundHeight = height;
            enabled = animator != null;
        }

        public void Clear() => enabled = false;

        private void OnAnimatorIK(int layerIndex)
        {
            if (!enabled || layerIndex != 0) return;
            ClampFoot(AvatarIKGoal.LeftFoot);
            ClampFoot(AvatarIKGoal.RightFoot);
        }

        // Weight stays 1: a foot above the ground is simply targeted at its animated position, so nothing pops.
        private void ClampFoot(AvatarIKGoal goal)
        {
            Vector3 position = animator.GetIKPosition(goal);
            float min = groundHeight + ankleHeight;
            if (position.y < min) position.y = min;
            animator.SetIKPositionWeight(goal, 1f);
            animator.SetIKPosition(goal, position);
        }
    }
}
