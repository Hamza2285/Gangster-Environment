using System.Text;
using Invector;
using OpenworldCrime.Pedestrians;
using UnityEngine;

namespace OpenworldCrime.DevTools
{
    /// <summary>
    /// Dev-only: logs the damage the player receives from people (and optionally freezes time on the first / Nth hit for a
    /// screenshot). Added at runtime to the player while testing fighting peds.
    /// </summary>
    public class BrawlProbe : MonoBehaviour
    {
        public int freezeOnHitNumber = -1;
        public float freezeDelay = 0.12f;
        public readonly StringBuilder Log = new StringBuilder();
        public int HitsTaken { get; private set; }

        private vHealthController health;
        private float freezeAt = -1f;

        private void Start()
        {
            health = GetComponent<vHealthController>();
            health.onReceiveDamage.AddListener(HandleDamage);
            Log.AppendLine($"start health={health.currentHealth}");
        }

        private void OnDestroy()
        {
            if (health != null) health.onReceiveDamage.RemoveListener(HandleDamage);
        }

        private void HandleDamage(vDamage damage)
        {
            HitsTaken++;
            Log.AppendLine($"{Time.time:F2} PLAYER HIT by {(damage.sender != null ? damage.sender.name : "?")} type={damage.damageType} value={damage.damageValue} health={health.currentHealth}");
            if (HitsTaken == freezeOnHitNumber) freezeAt = Time.time + freezeDelay;
        }

        private void Update()
        {
            if (freezeAt > 0f && Time.time >= freezeAt)
            {
                freezeAt = -1f;
                Time.timeScale = 0f;
                Log.AppendLine("frozen for capture");
            }
        }
    }
}
