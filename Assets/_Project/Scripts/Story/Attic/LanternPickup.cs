using System;
using ArcaneStrike.HandInput;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Grandfather's lantern, found in the chest. Once revealed, the player picks it up by closing a fist
    /// around its top ring; it then hangs from that hand until <see cref="Release"/>.
    /// </summary>
    public class LanternPickup : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [Tooltip("The ring on top that the player grabs. Defaults to 0.25 m above the lantern's origin.")]
        [SerializeField] Transform handle;
        [Tooltip("A fist this close to the handle (m) picks the lantern up. Webcam depth is rough, so keep it generous.")]
        [SerializeField] float reach = 0.22f;
        [Tooltip("How quickly the lantern follows the hand. Lower is smoother on a jittery webcam.")]
        [SerializeField] float followSharpness = 12f;

        [Header("Reveal")]
        [Tooltip("When the chest opens, the lantern rises this far (m) out of it and hovers, turning slowly, until it is taken.")]
        [SerializeField] float revealRise = 0.2f;
        [Tooltip("Wait for the lid to swing out of the way first (s).")]
        [SerializeField] float revealDelay = 0.6f;
        [SerializeField] float revealTime = 1.2f;
        [Tooltip("Degrees per second while it hovers.")]
        [SerializeField] float revealSpin = 25f;

        public bool IsAvailable { get; private set; }
        public bool IsPickedUp { get; private set; }
        public HandSide HeldBy { get; private set; } = HandSide.Right;
        public event Action OnPickedUp;

        Vector3 handleOffset;  // handle relative to the lantern origin, captured at pickup
        Vector3 lastTarget;
        bool hasTarget;
        bool hovering;
        float revealedAt;
        Vector3 revealFrom;
        Quaternion revealRot;

        Vector3 HandlePoint => handle != null ? handle.position : transform.position + Vector3.up * 0.25f;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
        }

        /// <summary>The chest is open: the lantern can be taken.</summary>
        public void Reveal()
        {
            IsAvailable = true;
            if (IsPickedUp || hovering) return;
            hovering = true;
            revealedAt = Time.time + revealDelay;
            revealFrom = transform.localPosition;
            revealRot = transform.localRotation;
        }

        public void PickUp(HandSide side)
        {
            if (IsPickedUp) return;
            IsAvailable = true;
            IsPickedUp = true;
            HeldBy = side;
            hovering = false;
            handleOffset = HandlePoint - transform.position;
            transform.SetParent(null, true); // leave the chest, so the chest shaking later doesn't move it
            Debug.Log($"[Attic] Lantern picked up ({side} hand)");
            OnPickedUp?.Invoke();
        }

        /// <summary>Let go for good: the lantern stays where it is (the awakening makes it float) and can't be grabbed again.</summary>
        public void Release()
        {
            IsPickedUp = false;
            IsAvailable = false;
            hovering = false;
        }

        void Update()
        {
            if (hovering) Hover();
            if (!IsAvailable || input == null) return;
            if (!IsPickedUp)
            {
                for (int i = 0; i < 2; i++)
                {
                    HandFrame f = input.Get((HandSide)i);
                    if (f.IsTracked && StoryFx.IsFist(f) && Vector3.Distance(f.PalmCenter, HandlePoint) < reach)
                    {
                        PickUp((HandSide)i);
                        break;
                    }
                }
                return;
            }
            Follow();
        }

        // Found: the lantern lifts out of the chest, then bobs and turns, waiting to be taken.
        void Hover()
        {
            float t = Time.time - revealedAt;
            if (t < 0f) return;
            float u = Mathf.Clamp01(t / Mathf.Max(revealTime, 0.01f));
            float eased = 1f - (1f - u) * (1f - u) * (1f - u);
            float bob = Mathf.Sin(t * 2f) * 0.01f * u;
            transform.localPosition = revealFrom + Vector3.up * (revealRise * eased + bob);
            transform.localRotation = revealRot * Quaternion.Euler(0f, t * revealSpin * u, 0f);
        }

        void Follow()
        {
            Transform head = StoryFx.Head(input);
            HandFrame f = input.Get(HeldBy);
            if (f.IsTracked)
            {
                lastTarget = f.PalmCenter - handleOffset;
                hasTarget = true;
            }
            else if (!hasTarget && head != null)
            {
                // Picked up with the keyboard and no hands: hold it in front of the player.
                lastTarget = head.position + StoryFx.Flat(head.forward) * 0.4f + Vector3.down * 0.45f;
            }

            float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, lastTarget, k);
            if (head != null)
            {
                // Stay upright, turned towards the player.
                Quaternion face = Quaternion.LookRotation(StoryFx.Flat(head.position - transform.position));
                transform.rotation = Quaternion.Slerp(transform.rotation, face, k);
            }
        }
    }
}
