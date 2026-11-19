using System.Text;
using UnityEngine;

namespace OpenworldCrime.DevTools
{
    /// <summary>Dev-only: logs trigger contacts on this collider (added at runtime while testing melee hits).</summary>
    public class TriggerLogProbe : MonoBehaviour
    {
        public readonly StringBuilder Log = new StringBuilder();

        private void OnTriggerEnter(Collider other) => Log.AppendLine($"{Time.time:F2} ENTER {other.name} ({other.transform.root.name}) enabled={other.enabled}");
        private void OnTriggerExit(Collider other) => Log.AppendLine($"{Time.time:F2} EXIT {other.name}");
    }
}
