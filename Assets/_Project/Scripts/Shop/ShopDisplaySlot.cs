using UnityEngine;

namespace OpenworldCrime.Shop
{
    /// <summary>
    /// One weapon physically on display in the gun shop (on the wall rack or the counter). Holds the catalog id of the weapon
    /// its model shows, the direction the shop camera frames it from, and the small "pulled forward" highlight of the
    /// selected weapon. Update only runs while that highlight animates.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShopDisplaySlot : MonoBehaviour
    {
        [Tooltip("WeaponDefinition.id in the WeaponCatalog.")]
        [SerializeField] private string weaponId;
        [Tooltip("The display model (child of this slot).")]
        [SerializeField] private Transform model;
        [Tooltip("Direction from the weapon to the shop camera, in this slot's local space.")]
        [SerializeField] private Vector3 viewDirection = new Vector3(0f, 0.12f, 1f);

        [Header("Highlight")]
        [Tooltip("How far the selected weapon moves toward the camera (m).")]
        [SerializeField, Min(0f)] private float highlightOffset = 0.035f;
        [SerializeField, Min(0.1f)] private float highlightSpeed = 8f;

        public string WeaponId => weaponId;
        /// <summary>World direction from the weapon toward the camera that frames it.</summary>
        public Vector3 ViewDirection => transform.TransformDirection(viewDirection).normalized;
        /// <summary>World bounds of the model at rest (cached on first use; display weapons never move).</summary>
        public Bounds Bounds
        {
            get
            {
                if (!boundsCached) CacheBounds();
                return bounds;
            }
        }

        private Vector3 modelRestPosition;
        private float highlight;
        private float highlightTarget;
        private Bounds bounds;
        private bool boundsCached;

        private void Awake()
        {
            if (model == null) Debug.LogWarning($"[{nameof(ShopDisplaySlot)}] '{name}' has no model assigned.", this);
            else modelRestPosition = model.localPosition;
            if (string.IsNullOrEmpty(weaponId)) Debug.LogWarning($"[{nameof(ShopDisplaySlot)}] '{name}' has no weapon id.", this);
            enabled = false;
        }

        private void Update()
        {
            highlight = Mathf.MoveTowards(highlight, highlightTarget, highlightSpeed * Time.unscaledDeltaTime);
            ApplyHighlight();
            if (Mathf.Approximately(highlight, highlightTarget)) enabled = false;
        }

        public void SetHighlighted(bool highlighted)
        {
            if (model == null) return;
            if (!boundsCached) CacheBounds();
            highlightTarget = highlighted ? 1f : 0f;
            enabled = !Mathf.Approximately(highlight, highlightTarget);
        }

        private void ApplyHighlight()
        {
            float eased = Mathf.SmoothStep(0f, 1f, highlight);
            model.localPosition = modelRestPosition + viewDirection.normalized * (highlightOffset * eased);
        }

        private void CacheBounds()
        {
            boundsCached = true;
            bounds = new Bounds(transform.position, Vector3.one * 0.2f);
            if (model == null) return;
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        }
    }
}
