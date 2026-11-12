using UnityEngine;

namespace OpenworldCrime.Interiors
{
    /// <summary>GTA-style floating marker motion (bob + spin). Runs only while one of its renderers is visible.</summary>
    public class MarkerBob : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float bobHeight = 0.12f;
        [SerializeField, Min(0f)] private float bobSpeed = 2f;
        [SerializeField] private float spinSpeed = 60f;

        private Vector3 startLocal;

        private void Awake()
        {
            startLocal = transform.localPosition;
            enabled = false;
        }

        private void OnBecameVisible() => enabled = true;
        private void OnBecameInvisible() => enabled = false;

        private void Update()
        {
            transform.localPosition = startLocal + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobHeight);
            transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);
        }
    }
}
