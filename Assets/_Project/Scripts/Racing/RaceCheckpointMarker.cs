using UnityEngine;

namespace OpenworldCrime.Racing
{
    /// <summary>
    /// Visual of a race checkpoint gate (the ring the player drives through). The <see cref="RaceManager"/> keeps two of them
    /// (the next checkpoint, bright, and the one after, dim) and moves them along the route; no Update here.
    /// </summary>
    [DisallowMultipleComponent]
    public class RaceCheckpointMarker : MonoBehaviour
    {
        [SerializeField] private Renderer[] renderers = new Renderer[0];
        [SerializeField] private Color brightColour = new Color(1f, 0.82f, 0.25f, 0.85f);
        [SerializeField] private Color dimColour = new Color(0.75f, 0.45f, 1f, 0.28f);
        [SerializeField] private Color finishColour = new Color(1f, 1f, 1f, 0.9f);

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock block;

        /// <summary>Places the gate at a route point facing along the route.</summary>
        public void Show(Vector3 position, Vector3 forward, bool bright, bool finish)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = transform.forward;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward.normalized, Vector3.up));
            if (block == null) block = new MaterialPropertyBlock();
            block.SetColor(BaseColor, finish ? finishColour : bright ? brightColour : dimColour);
            for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].SetPropertyBlock(block);
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
