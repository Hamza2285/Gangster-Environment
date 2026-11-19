using System.Text;
using Invector;
using Invector.vCharacterController;
using Invector.vMelee;
using OpenworldCrime.Pedestrians;
using UnityEngine;

namespace OpenworldCrime.DevTools
{
    /// <summary>
    /// Dev-only melee test: places the target pedestrian in front of the player, makes the player throw weak attacks and logs
    /// what happens per frame (attack state, active hitboxes, damage received by the ped, its state). Removes itself when done.
    /// </summary>
    public class MeleeHitProbe : MonoBehaviour
    {
        public PedestrianDamage target;
        public int punches = 3;
        public float interval = 0.9f;
        public float distance = 0.9f;
        public bool replaceEachPunch;
        [Tooltip("Freeze time (timeScale 0) this long after the first hit lands (< 0 = never), for screenshots.")]
        public float freezeAfterHit = -1f;
        private float firstHitTime = -1f;
        public string result;

        private vMeleeCombatInput input;
        private vMeleeManager melee;
        private float startTime;
        private int thrown;
        private readonly StringBuilder log = new StringBuilder();

        public bool Done { get; private set; }

        private void Start()
        {
            input = GetComponent<vMeleeCombatInput>();
            melee = GetComponent<vMeleeManager>();
            startTime = Time.time;
            PedestrianDamage.OnAnyDamaged += HandleDamaged;
            Place();
            log.AppendLine($"probe start: input={(input != null)} melee={(melee != null)} target={(target != null ? target.name : "none")}");
            if (melee != null)
            {
                foreach (var m in melee.Members)
                    log.AppendLine($"  member {m.bodyPart} hitboxes={m.attackObject.hitBoxes?.Count ?? -1} tags='{string.Join(",", m.attackObject.damage.damageType)}'");
                var so = melee.hitProperties;
                log.AppendLine($"  hitDamageTags=[{string.Join(",", so.hitDamageTags)}] hitRecoilLayer={so.hitRecoilLayer.value}");
            }
            if (target != null) log.AppendLine($"  target layer={LayerMask.LayerToName(target.gameObject.layer)} tag={target.tag}");
        }

        private void OnDestroy() => PedestrianDamage.OnAnyDamaged -= HandleDamaged;

        private void HandleDamaged(PedestrianDamage ped, vDamage damage, bool fatal)
        {
            if (firstHitTime < 0f) firstHitTime = Time.time;
            log.AppendLine($"{Time.time - startTime:F2} DAMAGE {ped.name} value={damage.damageValue} sender={(damage.sender != null ? damage.sender.name : "null")} type={damage.damageType} fatal={fatal}");
        }

        private void Place()
        {
            if (target == null) return;
            Vector3 front = transform.position + transform.forward * distance;
            var agent = target.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null && agent.enabled) agent.Warp(front);
            else target.transform.position = front;
            target.transform.rotation = Quaternion.LookRotation(-transform.forward);
        }

        private void Update()
        {
            if (Done) return;
            if (freezeAfterHit >= 0f && firstHitTime >= 0f && Time.time - firstHitTime >= freezeAfterHit)
            {
                Time.timeScale = 0f;
                freezeAfterHit = -1f;
                log.AppendLine("frozen for capture");
            }
            float t = Time.time - startTime;
            if (thrown < punches && t > 0.3f + thrown * interval)
            {
                if (replaceEachPunch) Place();
                transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up));
                input.TriggerWeakAttack();
                thrown++;
                log.AppendLine($"{t:F2} PUNCH {thrown} ped tag={target.tag} layer={LayerMask.LayerToName(target.gameObject.layer)} down={target.IsDown}");
            }
            if (melee != null)
            {
                int active = 0;
                foreach (var m in melee.Members) if (m.attackObject != null && m.attackObject.canApplyDamage) active++;
                if (active > 0) log.AppendLine($"{t:F2}   active attack objects={active} dist={Vector3.Distance(transform.position, target.transform.position):F2}");
            }
            if (t > 0.3f + punches * interval + 1.5f)
            {
                log.AppendLine($"end: ped dead={target.IsDead} down={target.IsDown} health={target.GetComponent<vHealthController>()?.currentHealth}");
                result = log.ToString();
                Debug.Log("[MeleeHitProbe]\n" + result);
                Done = true;
                Destroy(this);
            }
        }
    }
}
