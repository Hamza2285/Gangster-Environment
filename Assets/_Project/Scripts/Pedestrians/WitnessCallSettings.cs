using UnityEngine;

namespace OpenworldCrime.Pedestrians
{
    /// <summary>
    /// Tuning for carjack victims who phone the police (<see cref="WitnessCallManager"/>, <see cref="PedestrianPhoneCall"/>).
    /// Configuration only: nothing here changes at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "WitnessCallSettings", menuName = "OpenworldCrime/Pedestrians/Witness Call Settings")]
    public class WitnessCallSettings : ScriptableObject
    {
        [Header("Who Calls")]
        [Tooltip("Share of pulled-out drivers who phone the police once back on their feet (0-1).")]
        [Range(0f, 1f)] public float callChance = 0.5f;
        [Tooltip("Nobody calls (they run) when the player fired a gun within this radius (m) ...")]
        [Min(0f)] public float gunfirePanicRadius = 40f;
        [Tooltip("... in the last this many seconds.")]
        [Min(0f)] public float gunfirePanicSeconds = 8f;
        [Tooltip("A victim still on the ground after this many seconds forgets about calling.")]
        [Min(1f)] public float armedSeconds = 20f;

        [Header("Call")]
        [Tooltip("How far (m) the victim backs off from the player before calling.")]
        [Min(0f)] public float stepBackDistance = 0.8f;
        [Min(0.05f)] public float stepBackSeconds = 0.6f;
        [Tooltip("Hand into the pocket (s).")]
        [Min(0.05f)] public float reachSeconds = 0.45f;
        [Tooltip("Phone up to the ear (s); the same to put it away.")]
        [Min(0.05f)] public float raiseSeconds = 0.45f;
        [Tooltip("Talking time (s): a random value in this range. The police are told when it ends.")]
        public Vector2 talkSeconds = new Vector2(3f, 4f);
        [Tooltip("Turn speed toward the player while calling (deg/s).")]
        [Min(10f)] public float turnSpeed = 240f;
        [Tooltip("The call is dropped (no report) when the player gets farther than this (m).")]
        [Min(5f)] public float cancelDistance = 45f;

        [Header("Afterwards")]
        [Tooltip("On: runs off after the call (street panic). Off: walks back into the crowd.")]
        public bool fleeAfterCall = true;

        [Header("Phone Prop")]
        public Mesh phoneMesh;
        public Material phoneMaterial;
        [Tooltip("Size (m): width, height, thickness.")]
        public Vector3 phoneSize = new Vector3(0.07f, 0.14f, 0.012f);

        [Header("Arm Pose (character space, m)")]
        [Tooltip("Hand at the right trouser pocket, from the pelvis bone.")]
        public Vector3 pocketOffset = new Vector3(0.2f, -0.12f, 0.03f);
        [Tooltip("Hand at the right ear, from the head bone.")]
        public Vector3 earOffset = new Vector3(0.1f, 0.0f, 0.03f);
        [Tooltip("Where the elbow points while the phone is at the ear, from the shoulder.")]
        public Vector3 earElbowHint = new Vector3(0.45f, -0.35f, 0.15f);
        [Tooltip("Where the elbow points while the hand is in the pocket, from the shoulder.")]
        public Vector3 pocketElbowHint = new Vector3(0.35f, -0.2f, -0.35f);

        [Header("Feedback")]
        [Tooltip("HUD line when a call gets through (empty = none).")]
        public string calledMessage = "A witness called the police";
    }
}
