using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace OpenworldCrime.Pedestrians
{
    /// <summary>
    /// Punch reactions for humanoid people whose animator controller has no hit states of its own (the police: Hamza's
    /// PoliceOfficer controller and our PoliceOfficerCombat): when <see cref="PedestrianDamage"/> reports a punch that did not
    /// knock him down (OnStaggered), a directional hit clip (front / left / right / back, never the same twice in a row) is
    /// played over whatever the controller is doing, through a small PlayableGraph output on the same Animator, faded in and
    /// out. The controller and the scripts driving it carry on untouched underneath; a knock-down or death stops it at once
    /// (the ragdoll takes over). Per-instance; Update only runs while a reaction plays.
    /// </summary>
    [DisallowMultipleComponent]
    public class HumanoidHitReaction : MonoBehaviour
    {
        [SerializeField] private PedestrianDamage damage;
        [SerializeField] private Animator animator;
        [Tooltip("Hit from the front (head snap / body blow variants).")]
        [SerializeField] private AnimationClip[] front = new AnimationClip[0];
        [SerializeField] private AnimationClip[] left = new AnimationClip[0];
        [SerializeField] private AnimationClip[] right = new AnimationClip[0];
        [SerializeField] private AnimationClip[] back = new AnimationClip[0];
        [SerializeField, Min(0.1f)] private float speed = 1.15f;
        [SerializeField, Min(0.01f)] private float blendIn = 0.07f;
        [SerializeField, Min(0.01f)] private float blendOut = 0.25f;
        [Tooltip("Share of the clip played before blending back out.")]
        [SerializeField, Range(0.3f, 1f)] private float playShare = 0.85f;

        private PlayableGraph graph;
        private AnimationPlayableOutput output;
        private AnimationClipPlayable playing;
        private AnimationClip last;
        private float time;
        private float length;
        private readonly List<AnimationClip> choices = new List<AnimationClip>(4);

        private void Awake()
        {
            if (damage == null) damage = GetComponent<PedestrianDamage>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (damage == null || animator == null)
            {
                Debug.LogError($"[{nameof(HumanoidHitReaction)}] Missing PedestrianDamage / Animator on '{name}'.", this);
                enabled = false;
                return;
            }
            damage.OnStaggered += HandleStaggered;
            damage.OnKnockedDown += HandleStop;
            damage.OnDied += HandleStop;
            enabled = false;
        }

        private void OnDestroy()
        {
            if (damage != null)
            {
                damage.OnStaggered -= HandleStaggered;
                damage.OnKnockedDown -= HandleStop;
                damage.OnDied -= HandleStop;
            }
            DestroyGraph();
        }

        private void OnDisable() => DestroyGraph();

        // push: from the attacker through the person (horizontal).
        private void HandleStaggered(Vector3 push)
        {
            if (!gameObject.activeInHierarchy) return;
            AnimationClip clip = Pick(-push);
            if (clip == null) return;
            DestroyGraph();
            graph = PlayableGraph.Create(name + " Hit");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            output = AnimationPlayableOutput.Create(graph, "Hit", animator);
            playing = AnimationClipPlayable.Create(graph, clip);
            playing.SetApplyFootIK(true);
            playing.SetSpeed(speed);
            output.SetSourcePlayable(playing);
            output.SetWeight(0f);
            graph.Play();
            time = 0f;
            length = clip.length / speed * playShare;
            enabled = true;
        }

        private void HandleStop(Vector3 _) => DestroyGraph();

        private void Update()
        {
            if (!graph.IsValid()) { enabled = false; return; }
            time += Time.deltaTime;
            float weight = Mathf.Min(Mathf.Clamp01(time / blendIn), Mathf.Clamp01((length - time) / blendOut));
            output.SetWeight(weight);
            if (time >= length) DestroyGraph();
        }

        private AnimationClip Pick(Vector3 toAttacker)
        {
            toAttacker.y = 0f;
            AnimationClip[] set = front;
            if (toAttacker.sqrMagnitude > 0.0001f)
            {
                float angle = Vector3.SignedAngle(transform.forward, toAttacker, Vector3.up);
                if (Mathf.Abs(angle) > 130f) set = back;
                else if (angle > 50f) set = right;
                else if (angle < -50f) set = left;
            }
            if (set == null || set.Length == 0) set = front;
            choices.Clear();
            foreach (AnimationClip c in set) if (c != null && c != last) choices.Add(c);
            if (choices.Count == 0) foreach (AnimationClip c in set) if (c != null) choices.Add(c);
            if (choices.Count == 0) return null;
            last = choices[Random.Range(0, choices.Count)];
            return last;
        }

        private void DestroyGraph()
        {
            if (graph.IsValid()) graph.Destroy();
            enabled = false;
        }
    }
}
