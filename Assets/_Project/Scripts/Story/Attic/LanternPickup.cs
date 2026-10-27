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

        public bool IsAvailable { get; private set; }
        public bool IsPickedUp { get; private set; }
        public HandSide HeldBy { get; private set; } = HandSide.Right;
        public event Action OnPickedUp;

        Vector3 handleOffset;  // handle relative to the lantern origin, captured at pickup
        Vector3 lastTarget;
        bool hasTarget;

        Vector3 HandlePoint => handle != null ? handle.position : transform.position + Vector3.up * 0.25f;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
        }

        /// <summary>The chest is open: the lantern can be taken.</summary>
        public void Reveal() => IsAvailable = true;

        public void PickUp(HandSide side)
        {
            if (IsPickedUp) return;
            IsAvailable = true;
            IsPickedUp = true;
            HeldBy = side;
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
        }

        void Update()
        {
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
