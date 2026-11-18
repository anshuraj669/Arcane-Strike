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
        [Tooltip("It hangs from the ring and swings as the hand moves (0 = held rigidly upright).")]
        [Range(0f, 1f)] [SerializeField] float swing = 1f;

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
        Vector3 lastTarget;    // where the hand holds the ring
        Vector3 grip, gripVelocity, tilt, tiltVelocity;
        Quaternion facing;
        bool hasTarget;
        bool hovering;
        bool actedHolding;
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
            grip = lastTarget = HandlePoint;
            gripVelocity = tilt = tiltVelocity = Vector3.zero;
            facing = Quaternion.LookRotation(StoryFx.Flat(transform.forward));
            transform.SetParent(null, true); // leave the chest, so the chest shaking later doesn't move it
            Combat.CombatFx.Rumble(side, 0.3f, 0.06f); // its weight settling into the hand (controllers only)
            Debug.Log($"[Attic] Lantern picked up ({side} hand)");
            OnPickedUp?.Invoke();
        }

        /// <summary>Let go for good: the lantern stays where it is (the awakening makes it float) and can't be grabbed again.</summary>
        public void Release()
        {
            if (actedHolding) ActedHands.Release(HeldBy);
            actedHolding = false;
            IsPickedUp = false;
            IsAvailable = false;
            hovering = false;
        }

        void Update()
        {
            if (hovering) Hover();
            if (!IsAvailable) return;
            if (!IsPickedUp)
            {
                if (input == null) return;
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
            HandFrame f = input != null ? input.Get(HeldBy) : null;
            // (The acted hand below is ours: following it would chase our own tail.)
            bool acted = input != null && input.ActiveSource != null && ReferenceEquals(input.ActiveSource, HandInputService.ActedSource);
            if (f != null && f.IsTracked && !acted)
            {
                lastTarget = f.PalmCenter;
                hasTarget = true;
            }
            else if ((!hasTarget || acted) && head != null)
            {
                // Picked up with the keyboard and no hands: the hand comes up and holds it by the ring, in front of
                // the player and a little to that side.
                float side = HeldBy == HandSide.Right ? 0.17f : -0.17f;
                Vector3 forward = StoryFx.Flat(head.forward);
                lastTarget = head.position + forward * 0.38f + StoryFx.Flat(head.right) * side + Vector3.down * 0.3f;
                ActedHands.Hold(HeldBy, lastTarget + Vector3.up * 0.025f - forward * 0.03f,
                    Quaternion.LookRotation(Vector3.Slerp(forward, Vector3.down, 0.15f), Vector3.up), 0.85f);
                actedHolding = true;
            }

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float k = 1f - Mathf.Exp(-followSharpness * dt);
            Vector3 was = grip;
            grip = Vector3.Lerp(grip, lastTarget, k);

            // It hangs from the ring like a real lantern: when the hand speeds up the body lags behind and swings,
            // then settles. A damped pendulum on the hand's sideways acceleration (tilt = a / g, about 1 Hz swing).
            Vector3 velocity = (grip - was) / dt;
            Vector3 accel = Vector3.ClampMagnitude((velocity - gripVelocity) / dt, 25f); // tracking jitter stays small
            gripVelocity = velocity;
            accel.y = 0f;
            tiltVelocity += ((accel / 9.81f * swing) - tilt) * (40f * dt);
            tiltVelocity *= Mathf.Exp(-3.5f * dt);
            tilt = Vector3.ClampMagnitude(tilt + tiltVelocity * dt, 0.7f);
            Quaternion hang = Quaternion.FromToRotation(Vector3.up, (Vector3.up + tilt).normalized);

            if (head != null)
            {
                // Turned towards the player.
                Quaternion face = Quaternion.LookRotation(StoryFx.Flat(head.position - grip));
                facing = Quaternion.Slerp(facing, face, k);
            }
            transform.rotation = hang * facing;
            transform.position = grip - hang * handleOffset; // swings about the ring, not its middle
        }
    }
}
