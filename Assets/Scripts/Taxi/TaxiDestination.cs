using UnityEngine;

/// <summary>
/// A fast-travel stop offered in the taxi's destination list. Place it on the pavement where the player should be
/// dropped off (the taxi pulls over at the nearest kerb lane), facing the way the player should look on arrival.
/// </summary>
[DisallowMultipleComponent]
public class TaxiDestination : MonoBehaviour
{
    [Tooltip("Name shown in the destination list. Empty = the GameObject name.")]
    [SerializeField] private string displayName;

    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.15f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.5f, 0.5f);
        Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, transform.position + Vector3.up * 0.5f + transform.forward * 1.5f);
    }
}
