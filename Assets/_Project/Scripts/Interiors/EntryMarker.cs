using UnityEngine;

namespace OpenworldCrime.Interiors
{
    /// <summary>
    /// The shop marker on the ground where the player walks in (shop doors) or stands to use something inside (the clothes
    /// fitting spot, the gun counter, the way out): a glowing ring on the floor, a soft light column and a floating arrow
    /// that bobs and always faces the camera. Visual only: the interactable next to it does the work.
    /// Built and placed by EntryMarkerBuilder (Tools > OpenworldCrime > Interiors > Place Entry Markers).
    /// </summary>
    [DisallowMultipleComponent]
    public class EntryMarker : MonoBehaviour
    {
        [SerializeField] private Transform ring;
        [SerializeField] private Transform column;
        [SerializeField] private Transform arrow;
        [SerializeField, Min(0.1f)] private float pulseSeconds = 1.6f;
        [SerializeField, Range(0f, 0.3f)] private float pulseAmount = 0.08f;
        [SerializeField] private float ringSpinDegrees = 25f;
        [SerializeField] private float arrowHeight = 2.15f;
        [SerializeField] private float arrowBob = 0.18f;

        [Tooltip("The whole marker hides while the player stands on it (horizontal metres).")]
        [SerializeField] private float hideWhenPlayerWithin = 1.4f;
        [Tooltip("The arrow hides when the camera is this close (horizontal metres), so it never fills the view.")]
        [SerializeField] private float hideArrowWhenCameraWithin = 4.5f;

        private Vector3 ringScale, columnScale;
        private Camera cachedCamera;
        private Transform player;
        private float nextPlayerLookup;
        private Renderer[] renderers;
        private Renderer arrowRenderer;

        private void Awake()
        {
            if (ring != null) ringScale = ring.localScale;
            if (column != null) columnScale = column.localScale;
            renderers = GetComponentsInChildren<Renderer>(true);
            if (arrow != null) arrowRenderer = arrow.GetComponent<Renderer>();
        }

        private static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }

        private void LateUpdate()
        {
            // Looked up again after a character switch (the other character is the active "Player" then).
            if ((player == null || !player.gameObject.activeInHierarchy) && Time.unscaledTime >= nextPlayerLookup)
            {
                nextPlayerLookup = Time.unscaledTime + 1f;
                var tagged = GameObject.FindWithTag("Player");
                if (tagged != null) player = tagged.transform;
            }
            if (cachedCamera == null || !cachedCamera.isActiveAndEnabled) cachedCamera = Camera.main;
            bool standingOn = player != null && player.gameObject.activeInHierarchy && Flat(player.position, transform.position) < hideWhenPlayerWithin
                              && Mathf.Abs(player.position.y - transform.position.y) < 2f;
            bool arrowOn = !standingOn && (cachedCamera == null || Flat(cachedCamera.transform.position, transform.position) > hideArrowWhenCameraWithin);
            foreach (var r in renderers) if (r != null && r != arrowRenderer && r.enabled == standingOn) r.enabled = !standingOn;
            if (arrowRenderer != null && arrowRenderer.enabled != arrowOn) arrowRenderer.enabled = arrowOn;
            if (standingOn) return;

            float t = Time.time;
            float k = 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI * 2f / pulseSeconds);
            if (ring != null)
            {
                ring.localScale = ringScale * (1f + pulseAmount * k);
                ring.localRotation = Quaternion.Euler(90f, t * ringSpinDegrees, 0f);
            }
            if (column != null) column.localScale = new Vector3(columnScale.x, columnScale.y * (1f - 0.12f * k), columnScale.z);
            if (arrow != null)
            {
                arrow.localPosition = new Vector3(0f, arrowHeight + arrowBob * Mathf.Sin(t * Mathf.PI * 2f / pulseSeconds), 0f);
                if (cachedCamera != null)
                {
                    Vector3 to = cachedCamera.transform.position - arrow.position;
                    to.y = 0f;
                    if (to.sqrMagnitude > 0.001f) arrow.rotation = Quaternion.LookRotation(-to.normalized, Vector3.up);
                }
            }
        }
    }
}
