using UnityEngine;

// The ambulance's two rear doors, swinging out on hinges at the outer edges, and the points a patient walks through to get
// in: behind the doors, the sill and inside the cargo box. Built by Game > Build Ambulance Rear Doors.
public class AmbulanceRearDoors : MonoBehaviour
{
    public Transform leftHinge, rightHinge;
    [Tooltip("How far each door swings open (degrees).")]
    public float openAngle = 105f;
    public float seconds = 0.7f;
    [Tooltip("Vehicle space: the rear face (z) and the cargo floor height (y).")]
    public float rearZ = -2.8f, floorY = 0.62f;
    [Tooltip("Vehicle space: the top of the door opening (y). Anyone getting in keeps their head below it.")]
    public float doorTopY = 2.02f;

    public float DoorTopWorldY => transform.TransformPoint(new Vector3(0f, doorTopY, rearZ)).y;

    // True when a point is at the doorway or inside the cargo box.
    public bool IsInDoorway(Vector3 world)
    {
        Vector3 local = transform.InverseTransformPoint(world);
        return local.z > rearZ - 0.45f && local.z < rearZ + 3.5f && Mathf.Abs(local.x) < 1.1f;
    }

    [SerializeField] private AudioClip openClip;
    [SerializeField] private AudioClip closeClip;

    private AudioSource doorAudio;
    float open, target;

    public bool IsOpen => open >= 0.999f;
    public bool IsClosed => open <= 0.001f;

    // Behind the open doors, on the road.
    public Vector3 BehindPoint => transform.TransformPoint(new Vector3(0f, 0f, rearZ - 1.4f));
    // On the edge of the cargo floor.
    public Vector3 SillPoint => transform.TransformPoint(new Vector3(0f, floorY, rearZ + 0.15f));
    // Inside, out of sight.
    public Vector3 InsidePoint => transform.TransformPoint(new Vector3(0f, floorY, rearZ + 1.5f));

    public void Open()
    {
        if (target == 1f) return;
        target = 1f;
        if (!IsOpen) PlayDoorSound(openClip);
    }
    public void Close() => target = 0f;

    void Update()
    {
        if (Mathf.Approximately(open, target)) return;
        open = Mathf.MoveTowards(open, target, Time.deltaTime / Mathf.Max(0.05f, seconds));
        float eased = open * open * (3f - 2f * open);
        // The left door's leaf runs to +x from its hinge and swings back (+y turns +x towards -z); the right one mirrors it.
        if (leftHinge != null) leftHinge.localRotation = Quaternion.Euler(0f, openAngle * eased, 0f);
        if (rightHinge != null) rightHinge.localRotation = Quaternion.Euler(0f, -openAngle * eased, 0f);
        if (open == 0f) PlayDoorSound(closeClip);
    }

    private void PlayDoorSound(AudioClip clip)
    {
        if (clip == null || leftHinge == null) return;
        if (doorAudio == null)
        {
            doorAudio = leftHinge.gameObject.AddComponent<AudioSource>();
            doorAudio.playOnAwake = false;
            doorAudio.spatialBlend = 1f;
            doorAudio.minDistance = 2f;
            doorAudio.maxDistance = 30f;
            doorAudio.rolloffMode = AudioRolloffMode.Linear;
        }
        doorAudio.PlayOneShot(clip);
    }
}
