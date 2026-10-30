using System;
using System.Collections;
using ArcaneStrike.HandInput;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// After cleaning, a sharp edge on the lantern glints. Touching it with a fingertip cuts the finger;
    /// blood drips, and a drop is drawn into the lantern (the blood bond that awakens it).
    /// If the player doesn't find the edge, the cut happens on its own after a while, so nobody gets stuck.
    /// </summary>
    public class FingerCutEvent : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [Tooltip("The sharp point on the lantern that cuts the finger.")]
        [SerializeField] Transform sharpEdge;
        [Tooltip("Where the blood drop flies to. Defaults to the lantern centre.")]
        [SerializeField] Transform lanternCenter;
        [Tooltip("A fingertip this close to the edge (m) gets cut.")]
        [SerializeField] float touchDistance = 0.07f;
        [Tooltip("If the player hasn't touched the edge after this long (s), the cut happens anyway.")]
        [SerializeField] float autoCutAfter = 12f;

        [Header("Blood")]
        [Tooltip("Optional effect spawned at the fingertip (like the original bloodEffect).")]
        public GameObject bloodEffect;
        [SerializeField] Color bloodColor = new Color(0.55f, 0.02f, 0.03f, 0.95f);
        [Tooltip("Seconds of dripping before the blood touches the lantern.")]
        [SerializeField] float bloodToLanternDelay = 2f;
        [SerializeField] float dropFlightTime = 0.7f;

        public event Action OnCut, OnBloodTouched;
        public bool IsCut { get; private set; }

        bool active;
        float beganAt, nextGlint;
        LanternPickup pickup;
        ParticleSystem blood, glint;

        Vector3 EdgePoint => sharpEdge != null ? sharpEdge.position : transform.position + Vector3.up * 0.24f;
        Vector3 Center => lanternCenter != null ? lanternCenter.position : transform.position + Vector3.up * 0.12f;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            pickup = GetComponent<LanternPickup>();
        }

        void Start()
        {
            blood = StoryFx.CreateParticles(transform, "Blood", false, 0.6f);
            glint = StoryFx.CreateParticles(transform, "EdgeGlint", true, 0f, 32);
        }

        /// <summary>The lantern is clean: the sharp edge starts to glint.</summary>
        public void Begin()
        {
            active = true;
            beganAt = Time.time;
        }

        void Update()
        {
            if (!active || IsCut) return;

            if (Time.time >= nextGlint)
            {
                nextGlint = Time.time + 0.35f;
                StoryFx.Emit(glint, EdgePoint, Vector3.zero, 0.05f, 0.3f, new Color(1f, 0.85f, 0.6f, 1f));
            }

            if (input != null)
                for (int i = 0; i < 2; i++)
                {
                    // The hand holding the ring sits a few cm from the edge: only the other hand's touch counts.
                    if (pickup != null && pickup.IsPickedUp && (int)pickup.HeldBy == i) continue;
                    HandFrame f = input.Get((HandSide)i);
                    if (f.IsTracked && Vector3.Distance(f[HandJoint.IndexTip], EdgePoint) < touchDistance)
                    {
                        Cut((HandSide)i);
                        return;
                    }
                }

            if (Time.time - beganAt > autoCutAfter)
            {
                Debug.Log("[Attic] (The player didn't find the edge; the lantern cuts them anyway.)");
                Cut(HandSide.Right);
            }
        }

        public void Cut(HandSide side)
        {
            if (IsCut) return;
            IsCut = true;
            active = false;
            StartCoroutine(Bleed(side));
        }

        IEnumerator Bleed(HandSide side)
        {
            Debug.Log($"[Attic] Finger cut ({side} hand)");
            Vector3 tip = Fingertip(side);
            if (bloodEffect != null) Instantiate(bloodEffect, tip, Quaternion.identity);
            StoryFx.Burst(blood, tip, 6, 0.15f, 0.012f, 0.8f, bloodColor);
            Combat.CombatFx.Rumble(side, 0.8f, 0.07f); // a sharp sting (controllers only)
            OnCut?.Invoke();

            // Drip from the fingertip for a moment.
            float until = Time.time + bloodToLanternDelay;
            float nextDrop = 0f;
            while (Time.time < until)
            {
                tip = Fingertip(side);
                if (Time.time >= nextDrop)
                {
                    nextDrop = Time.time + 0.25f;
                    StoryFx.Emit(blood, tip, Vector3.down * 0.05f, 0.015f, 1.2f, bloodColor);
                }
                yield return null;
            }

            // One drop is drawn into the lantern.
            Vector3 start = tip;
            for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(dropFlightTime, 0.01f))
            {
                Vector3 p = Vector3.Lerp(start, Center, t * t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.06f;
                StoryFx.Emit(blood, p, Vector3.zero, 0.02f, 0.25f, bloodColor);
                yield return null;
            }
            StoryFx.Burst(blood, Center, 12, 0.2f, 0.015f, 0.6f, bloodColor);
            Debug.Log("[Attic] Blood touches the lantern");
            OnBloodTouched?.Invoke();
        }

        Vector3 Fingertip(HandSide side)
        {
            HandFrame f = input != null ? input.Get(side) : null;
            return f != null && f.IsTracked ? f[HandJoint.IndexTip] : EdgePoint;
        }
    }
}
