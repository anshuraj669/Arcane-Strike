using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Plays the artist's animations on the attic's story beats, with no code. For each cue: pick the beat,
    /// drag in the Animator, and type the name of a Trigger parameter or a state in its controller.
    /// Delay waits after the beat starts. The Awakening beat runs about 9 s: whispers at 1.5 s, books lift
    /// at 3 s, the attic shakes at 6 s, the portal opens at 8.5 s.
    /// When a clip moves something the code also moves, turn the code motion off on that object
    /// (untick Animate Lid on ChestOpen for an animated lid).
    /// </summary>
    public class StoryAnimations : MonoBehaviour
    {
        [Serializable]
        public class Cue
        {
            public AtticGameManager.Beat beat;
            [Tooltip("Seconds after the beat starts.")]
            public float delay;
            public Animator animator;
            [Tooltip("A Trigger parameter, or the name of a state, in the Animator's controller.")]
            public string triggerOrState;
            [Tooltip("Anything else to start at the same time: a Timeline (PlayableDirector.Play), a sound, particles...")]
            public UnityEvent onPlay;
        }

        [SerializeField] AtticGameManager manager;
        public List<Cue> cues = new List<Cue>();

        void Awake()
        {
            if (manager == null) manager = FindAnyObjectByType<AtticGameManager>();
        }

        void OnEnable()
        {
            if (manager != null) manager.OnBeatChanged += HandleBeat;
        }

        void OnDisable()
        {
            if (manager != null) manager.OnBeatChanged -= HandleBeat;
        }

        void HandleBeat(AtticGameManager.Beat beat)
        {
            foreach (Cue cue in cues)
                if (cue.beat == beat) StartCoroutine(Play(cue));
        }

        IEnumerator Play(Cue cue)
        {
            if (cue.delay > 0f) yield return new WaitForSeconds(cue.delay);
            if (cue.animator != null && !string.IsNullOrEmpty(cue.triggerOrState)) Fire(cue.animator, cue.triggerOrState);
            cue.onPlay?.Invoke();
        }

        static void Fire(Animator animator, string name)
        {
            if (!animator.isActiveAndEnabled)
            {
                Debug.LogWarning($"[StoryAnimations] {animator.name} is switched off, so '{name}' can't play.", animator);
                return;
            }
            foreach (AnimatorControllerParameter p in animator.parameters)
                if (p.type == AnimatorControllerParameterType.Trigger && p.name == name)
                {
                    animator.SetTrigger(name);
                    return;
                }
            int hash = Animator.StringToHash(name);
            for (int layer = 0; layer < animator.layerCount; layer++)
                if (animator.HasState(layer, hash))
                {
                    animator.Play(hash, layer, 0f);
                    return;
                }
            Debug.LogWarning($"[StoryAnimations] {animator.name} has no trigger or state called '{name}'.", animator);
        }
    }
}
