using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ArcaneStrike.HandInput
{
    public enum StrikeType
    {
        Punch, // closed fist driven forward
        Swipe, // open hand slashed sideways
    }

    public struct StrikeEvent
    {
        public StrikeType Type;
        public HandSide Hand;
        public Vector3 Position;
        public Vector3 Direction;
        public float Speed;
        public float Time;
        public bool FromKeyboard;

        public override string ToString() => $"{Type} ({Hand}, {Speed:F1} m/s{(FromKeyboard ? ", key" : "")})";
    }

    /// <summary>
    /// Tracks how each hand MOVES (velocity, speed) and turns fast movements into melee strikes.
    /// <see cref="GestureRecognizer"/> handles spell poses; this handles punches and swipes.
    /// Like the recognizer, it only reads <see cref="HandFrame"/>, so it works with the webcam
    /// bridge and with real Quest hands.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public class HandMotion : MonoBehaviour
    {
        [SerializeField] HandInputService input;

        [Header("Velocity")]
        [Tooltip("Velocity is measured over this much recent movement. Longer = smoother but laggier.")]
        [SerializeField] float velocityWindow = 0.06f;

        [Header("Finger shape (straightness: 1 = straight)")]
        [SerializeField] float extendedThreshold = 0.85f;
        [SerializeField] float curledThreshold = 0.65f;

        [Header("Punch: fist driven forward")]
        [Tooltip("Forward speed in m/s. Webcam depth is noisy, so you may need a higher value there than on Quest.")]
        [SerializeField] float punchSpeed = 1.6f;

        [Tooltip("A hand that was a fist this recently still counts as a fist. Fast punches blur the fingers, so the fist often isn't recognised mid-punch.")]
        [SerializeField] float fistMemory = 0.3f;

        [Tooltip("Fingers that must be curled for a fist (of index, middle, ring, pinky). 3 is forgiving for webcams; 4 is strict.")]
        [Range(3, 4)]
        [SerializeField] int fistFingers = 3;

        [Header("Swipe: open hand moved sideways")]
        [SerializeField] float swipeSpeed = 1.8f;

        [Header("Shared")]
        [Tooltip("Minimum time between two strikes from the same hand.")]
        [SerializeField] float strikeCooldown = 0.35f;
        [Tooltip("Hands closer than this to the headset are ignored.")]
        [SerializeField] float ignoreNearHead = 0.15f;

        [Header("Debug")]
        [Tooltip("J = punch, K = swipe (right hand). Editor and development builds only.")]
        [SerializeField] bool keyboardFallback = true;
        [SerializeField] bool logStrikes = true;

        public event Action<StrikeEvent> OnStrike;
        public StrikeEvent? LastStrike { get; private set; }

        struct Sample { public float T; public Vector3 Palm; }

        class Track
        {
            public readonly List<Sample> History = new List<Sample>(64);
            public Vector3 Velocity;
            public float LastStrike = -10f;
            public float LastFist = -10f;
        }

        readonly Track[] tracks = { new Track(), new Track() };

        public Vector3 Velocity(HandSide side) => tracks[(int)side].Velocity;
        public float Speed(HandSide side) => tracks[(int)side].Velocity.magnitude;

        /// <summary>Time of this hand's last strike. Lets other systems ignore poses made mid-punch.</summary>
        public float LastStrikeTime(HandSide side) => tracks[(int)side].LastStrike;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
        }

        void Update()
        {
            if (input == null) return;
            Transform head = input.Head;
            if (head == null) return;

            float now = Time.time;
            for (int i = 0; i < 2; i++) UpdateHand(input.Get((HandSide)i), tracks[i], head, now);
            if (keyboardFallback && (Application.isEditor || Debug.isDebugBuild)) UpdateKeyboard(head, now);
        }

        void UpdateHand(HandFrame f, Track t, Transform head, float now)
        {
            if (!f.IsTracked || Vector3.Distance(f.Wrist, head.position) < ignoreNearHead)
            {
                t.History.Clear();
                t.Velocity = Vector3.zero;
                return;
            }

            t.History.Add(new Sample { T = now, Palm = f.PalmCenter });
            while (t.History.Count > 0 && now - t.History[0].T > 0.3f) t.History.RemoveAt(0);
            t.Velocity = MeasureVelocity(t.History, velocityWindow);
            if (IsFist(f)) t.LastFist = now;

            if (now - t.LastStrike < strikeCooldown) return;
            if (Vector3.Dot(f.PalmCenter - head.position, head.forward) < ignoreNearHead) return;

            float forward = Vector3.Dot(t.Velocity, head.forward);
            float sideways = Vector3.Dot(t.Velocity, head.right);

            if (now - t.LastFist < fistMemory && !IsOpen(f) && forward > punchSpeed)
            {
                Raise(t, StrikeType.Punch, f.Side, f.PalmCenter, t.Velocity.normalized, t.Velocity.magnitude, now);
            }
            else if (IsOpen(f) && Mathf.Abs(sideways) > swipeSpeed && Mathf.Abs(sideways) > Mathf.Abs(forward))
            {
                Vector3 dir = (head.right * Mathf.Sign(sideways)).normalized;
                Raise(t, StrikeType.Swipe, f.Side, f.PalmCenter, dir, t.Velocity.magnitude, now);
            }
        }

        bool IsFist(HandFrame f)
        {
            int curled = 0;
            if (f.Straightness(Finger.Index) < curledThreshold) curled++;
            if (f.Straightness(Finger.Middle) < curledThreshold) curled++;
            if (f.Straightness(Finger.Ring) < curledThreshold) curled++;
            if (f.Straightness(Finger.Pinky) < curledThreshold) curled++;
            return curled >= fistFingers;
        }

        bool IsOpen(HandFrame f) =>
            f.Straightness(Finger.Index) > extendedThreshold && f.Straightness(Finger.Middle) > extendedThreshold &&
            f.Straightness(Finger.Ring) > extendedThreshold && f.Straightness(Finger.Pinky) > extendedThreshold;

        static Vector3 MeasureVelocity(List<Sample> history, float window)
        {
            int last = history.Count - 1;
            if (last < 1) return Vector3.zero;
            Sample b = history[last];
            for (int i = last - 1; i >= 0; i--)
            {
                Sample a = history[i];
                if (b.T - a.T >= window || i == 0)
                    return b.T - a.T > 1e-4f ? (b.Palm - a.Palm) / (b.T - a.T) : Vector3.zero;
            }
            return Vector3.zero;
        }

        void UpdateKeyboard(Transform head, float now)
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            Track t = tracks[(int)HandSide.Right];
            Vector3 pos = head.position + head.forward * 0.4f - head.up * 0.15f;
            if (kb.jKey.wasPressedThisFrame) Raise(t, StrikeType.Punch, HandSide.Right, pos, head.forward, punchSpeed * 1.5f, now, true);
            if (kb.kKey.wasPressedThisFrame) Raise(t, StrikeType.Swipe, HandSide.Right, pos, head.right, swipeSpeed * 1.5f, now, true);
        }

        void Raise(Track t, StrikeType type, HandSide hand, Vector3 position, Vector3 direction, float speed, float now,
                   bool fromKeyboard = false)
        {
            t.LastStrike = now;
            var e = new StrikeEvent
            {
                Type = type, Hand = hand, Position = position, Direction = direction,
                Speed = speed, Time = now, FromKeyboard = fromKeyboard,
            };
            LastStrike = e;
            if (logStrikes) Debug.Log($"[Strike] {e}");
            OnStrike?.Invoke(e);
        }
    }
}
