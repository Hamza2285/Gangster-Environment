using System.Collections;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Getting in and out of a helicopter (one per helicopter): the cabin door swings open on its hinge, the helicopter's own
    /// camera shows it from the far side so the pilot's move onto the seat is hidden behind the fuselage, and the door closes
    /// again. PlayerVehicleInteraction drives the order (<see cref="IVehicleBoardingSequence"/>); this only owns the door and the
    /// boarding view. The door parts are meshes of the model, turned round a hinge line given in the helicopter's space.
    /// </summary>
    [DisallowMultipleComponent]
    public class HelicopterBoarding : MonoBehaviour, IVehicleBoardingSequence
    {
        [Header("Door")]
        [Tooltip("Model parts that make up the door (panel, window). They turn together round the hinge.")]
        [SerializeField] private Transform[] doorParts = new Transform[0];
        [Tooltip("A point on the door's hinge line, in the helicopter's space.")]
        [SerializeField] private Vector3 hingePoint = new Vector3(1.1f, 2f, 2.92f);
        [Tooltip("Hinge axis in the helicopter's space.")]
        [SerializeField] private Vector3 hingeAxis = Vector3.up;
        [Tooltip("Opening angle (deg) round the hinge axis.")]
        [SerializeField, Range(-150f, 150f)] private float openAngle = -80f;
        [SerializeField, Min(0.05f)] private float doorSeconds = 0.55f;

        [Header("Door Sound (optional)")]
        [SerializeField] private AudioSource doorAudio;
        [SerializeField] private AudioClip doorOpenClip;
        [SerializeField] private AudioClip doorCloseClip;

        [Header("Boarding View")]
        [SerializeField] private HelicopterCamera boardingCamera;
        [Tooltip("Camera point while the pilot gets in / out, in the helicopter's space (on the side away from the door).")]
        [SerializeField] private Vector3 viewPosition = new Vector3(-7.5f, 2.3f, 6.5f);
        [Tooltip("Where that camera looks, in the helicopter's space.")]
        [SerializeField] private Vector3 viewLookAt = new Vector3(0f, 1.9f, 2f);

        public float DoorSeconds => doorSeconds;

        private Vector3[] closedPositions;
        private Quaternion[] closedRotations;
        private float doorOpen01;
        private Coroutine doorRoutine;

        private void Awake()
        {
            // Closed pose of each part, in the helicopter's space.
            closedPositions = new Vector3[doorParts.Length];
            closedRotations = new Quaternion[doorParts.Length];
            for (int i = 0; i < doorParts.Length; i++)
            {
                if (doorParts[i] == null) continue;
                closedPositions[i] = transform.InverseTransformPoint(doorParts[i].position);
                closedRotations[i] = Quaternion.Inverse(transform.rotation) * doorParts[i].rotation;
            }
            if (boardingCamera == null) Debug.LogWarning($"[{nameof(HelicopterBoarding)}] No boarding camera on '{name}': getting in shows no side view.", this);
        }

        private void OnDisable()
        {
            if (doorRoutine != null) StopCoroutine(doorRoutine);
            doorRoutine = null;
            SetDoorPose(0f);
        }

        public void SetDoorOpen(bool open)
        {
            if (doorParts.Length == 0 || !isActiveAndEnabled) return;
            if (doorRoutine != null) StopCoroutine(doorRoutine);
            doorRoutine = StartCoroutine(MoveDoor(open ? 1f : 0f));
            if (doorAudio != null)
            {
                AudioClip clip = open ? doorOpenClip : doorCloseClip;
                if (clip != null) doorAudio.PlayOneShot(clip);
            }
        }

        public void ShowBoardingView(bool glide)
        {
            if (boardingCamera == null) return;
            boardingCamera.gameObject.SetActive(true);
            boardingCamera.ShowFixedView(transform, viewPosition, viewLookAt, glide);
        }

        public void EndBoardingView()
        {
            if (boardingCamera != null) boardingCamera.gameObject.SetActive(false);
        }

        private IEnumerator MoveDoor(float target)
        {
            while (!Mathf.Approximately(doorOpen01, target))
            {
                doorOpen01 = Mathf.MoveTowards(doorOpen01, target, Time.deltaTime / doorSeconds);
                SetDoorPose(Mathf.SmoothStep(0f, 1f, doorOpen01));
                yield return null;
            }
            doorRoutine = null;
        }

        private void SetDoorPose(float open01)
        {
            if (closedPositions == null) return;
            Quaternion turn = Quaternion.AngleAxis(openAngle * open01, hingeAxis);
            for (int i = 0; i < doorParts.Length; i++)
            {
                if (doorParts[i] == null) continue;
                Vector3 local = hingePoint + turn * (closedPositions[i] - hingePoint);
                doorParts[i].SetPositionAndRotation(transform.TransformPoint(local), transform.rotation * (turn * closedRotations[i]));
            }
        }

#if UNITY_EDITOR
        public void EditorSetup(Transform[] parts, Vector3 hinge, HelicopterCamera cameraRig, AudioSource audio, AudioClip openClip, AudioClip closeClip)
        {
            doorParts = parts;
            hingePoint = hinge;
            boardingCamera = cameraRig;
            doorAudio = audio;
            doorOpenClip = openClip;
            doorCloseClip = closeClip;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            Vector3 p = transform.TransformPoint(hingePoint);
            Vector3 axis = transform.TransformDirection(hingeAxis.normalized);
            Gizmos.DrawLine(p - axis, p + axis);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.TransformPoint(viewPosition), 0.3f);
        }
#endif
    }
}
