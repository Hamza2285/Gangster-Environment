using OpenworldCrime.Environment;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.UI
{
    /// <summary>
    /// HUD warning while the player is beyond the map's soft limit (<see cref="WorldBoundary.OnPlayerOutsideChanged"/>):
    /// "RESTRICTED AIRSPACE - TURN BACK" in an aircraft, "LEAVING THE MAP - TURN BACK" otherwise. A HUD widget: it only shows /
    /// hides its own panel. No Update.
    /// </summary>
    [DisallowMultipleComponent]
    public class BoundaryWarningView : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text label;
        [SerializeField] private string airspaceText = "RESTRICTED AIRSPACE - TURN BACK";
        [SerializeField] private string groundText = "LEAVING THE MAP - TURN BACK";

        private void Awake()
        {
            if (panel == null) Debug.LogError($"[{nameof(BoundaryWarningView)}] Missing panel on '{name}'.", this);
            else panel.SetActive(false);
        }

        private void OnEnable() => WorldBoundary.OnPlayerOutsideChanged += HandleOutside;
        private void OnDisable() => WorldBoundary.OnPlayerOutsideChanged -= HandleOutside;

        private void HandleOutside(bool outside, bool flying)
        {
            if (panel == null) return;
            if (label != null) label.text = flying ? airspaceText : groundText;
            panel.SetActive(outside);
        }

#if UNITY_EDITOR
        public void EditorSetup(GameObject root, Text text)
        {
            panel = root;
            label = text;
        }
#endif
    }
}
