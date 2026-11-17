using System.Collections.Generic;
using UnityEngine;

// A car on display in the showroom, with what its info card shows: name, class, price and how it drives.
public class ShowroomCar : MonoBehaviour
{
    public static readonly List<ShowroomCar> All = new List<ShowroomCar>();

    public string displayName = "Car";
    public string carClass = "Sports";
    public string drive = "AWD";
    public int price = 50000;
    [Tooltip("Top speed in km/h.")]
    public float topSpeed = 240f;
    [Tooltip("0-100 km/h in seconds.")]
    public float zeroToHundred = 4.5f;
    [Range(0f, 100f)] public float acceleration = 70f;
    [Range(0f, 100f)] public float handling = 70f;
    [Range(0f, 100f)] public float braking = 70f;
    [Range(0f, 100f)] public float speedRating = 70f;
    [Tooltip("Middle of the turntable the car stands on.")]
    public Vector3 bayCentre;
    [Tooltip("How close (from the bay's middle) the card shows.")]
    public float showRadius = 4.4f;

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }

    public Vector3 BayCentreWorld => transform.parent != null ? transform.parent.TransformPoint(bayCentre) : bayCentre;
}
