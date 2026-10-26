using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ArcaneStrike.HandInput
{
    public enum GestureType
    {
        Firebolt,
        AegisStart,
        AegisEnd,
        Seal,
        VoltBurst,
        Lumen,
        Sunwheel,
        GrabStart, // fist closed: Tether uses this when an enemy is gaze-locked
        GrabEnd,
    }

    public struct GestureEvent
    {
        public GestureType Type;
        public HandSide Hand;
        public Vector3 Position;
        public Vector3 Direction;
        public float Time;
        public bool FromKeyboard;

        public override string ToString() => $"{Type} ({Hand}{(FromKeyboard ? ", key" : "")})";
    }

    /// <summary>
    /// Turns hand frames into spell gestures (GDD §5). Works the same for Quest hands and the
    /// webcam bridge because it only uses <see cref="HandFrame"/> geometry.
    /// Spell rules (cooldowns, Motes, combos) belong in SpellSystem, not here.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class GestureRecognizer : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [Tooltip("Optional. Used to ignore Volt Burst when the fist was a punch, not a charge.")]
        [SerializeField] HandMotion motion;

        [Header("Finger shape (straightness: 1 = straight)")]
        [SerializeField] float extendedThreshold = 0.85f;
        [SerializeField] float curledThreshold = 0.65f;

        [Header("Anti-misfire (GDD 5.3)")]
        [Tooltip("A pose must be held this long before it counts.")]
        [SerializeField] float minPoseHold = 0.08f;
        [Tooltip("Hands closer than this to the headset are ignored.")]
        [SerializeField] float ignoreNearHead = 0.15f;
        [SerializeField] float minConfidence = 0.5f;

        [Header("Firebolt: index + middle out, flick forward")]
        [SerializeField] float fireboltSpeed = 1.2f;
        [SerializeField] float fireboltRefractory = 0.3f;

        [Header("Aegis: open palm facing forward, hold")]
        [SerializeField] float aegisFacing = 0.6f;
        [SerializeField] float aegisReleaseGrace = 0.15f;

        [Header("Volt Burst: fist, then burst open")]
        [SerializeField] float voltOpenWindow = 0.35f;

        [Header("Lumen: hand raised, palm up, open")]
        [Tooltip("Wrist height above the head. Use a negative value with a webcam, which can't see above your head.")]
        [SerializeField] float lumenHeight = 0.05f;
        [SerializeField] float lumenFacing = 0.6f;

        [Header("Seal: both hands together, then pull apart")]
        [SerializeField] float sealTogether = 0.12f;
        [SerializeField] float sealApart = 0.35f;
        [SerializeField] float sealWindow = 1.0f;

        [Header("Sunwheel: both index fingers draw a circle")]
        [SerializeField] float circleMinRadius = 0.07f;
        [SerializeField] float circleMaxRadius = 0.35f;
        [SerializeField] float circleCoverageDegrees = 300f;
        [SerializeField] float circleWindow = 1.5f;
        [SerializeField] float sunwheelSync = 0.6f;

        [Header("Webcam testing")]
        [Tooltip("While the webcam bridge is the hand source, relax the gestures webcams see badly " +
                 "(pushes toward the camera, palm-up hands, fast timing). No effect with Quest hands.")]
        [SerializeField] bool relaxForWebcam = true;

        [Header("Debug")]
        [Tooltip("Keys 1-6 and G cast gestures without hands. Editor and development builds only.")]
        [SerializeField] bool keyboardFallback = true;
        [SerializeField] bool logGestures = true;

        public event Action<GestureEvent> OnGesture;

        bool Relaxed => relaxForWebcam && input != null && input.ActiveSource is WebcamHandSource;
        float FireboltSpeed => Relaxed ? fireboltSpeed * 0.6f : fireboltSpeed;
        float VoltWindow => Relaxed ? Mathf.Max(voltOpenWindow, 0.6f) : voltOpenWindow;
        float LumenFacing => Relaxed ? Mathf.Min(lumenFacing, 0.35f) : lumenFacing;
        float CircleCoverage => Relaxed ? Mathf.Min(circleCoverageDegrees, 260f) : circleCoverageDegrees;
        float SunwheelSync => Relaxed ? Mathf.Max(sunwheelSync, 1.2f) : sunwheelSync;
        public GestureEvent? LastGesture { get; private set; }

        struct Sample { public float T; public Vector3 IndexTip; }

        class HandState
        {
            public readonly List<Sample> History = new List<Sample>(256);
            public string Pose = "none";
            public float PoseSince;
            public float LastFirebolt = -10f, LastLumen = -10f, LastFistEnd = -10f;
            public bool FistQualified, Aegis, Grab;
            public float AegisLostAt = -1f;
            public float CircleDoneAt = -10f;
        }

        readonly HandState[] state = { new HandState(), new HandState() };
        readonly List<Vector2> circlePoints = new List<Vector2>(256);
        float lastTogether = -10f, lastSeal = -10f, lastSunwheel = -10f;
        bool keyboardAegis, keyboardGrab;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            if (motion == null) motion = FindAnyObjectByType<HandMotion>();
        }

        void Update()
        {
            if (input == null) return;
            Transform head = input.Head;
            if (head == null) return;

            float now = Time.time;
            for (int i = 0; i < 2; i++) UpdateHand(input.Get((HandSide)i), state[i], head, now);
            UpdateSeal(head, now);
            UpdateSunwheel(head, now);
            if (keyboardFallback && (Application.isEditor || Debug.isDebugBuild)) UpdateKeyboard(head, now);
        }

        // ---------------------------------------------------------------- one hand

        void UpdateHand(HandFrame f, HandState s, Transform head, float now)
        {
            bool usable = f.IsTracked && f.Confidence >= minConfidence &&
                          Vector3.Distance(f.Wrist, head.position) > ignoreNearHead;
            if (!usable)
            {
                Reset(f.Side, s, head, now);
                return;
            }

            s.History.Add(new Sample { T = now, IndexTip = f[HandJoint.IndexTip] });
            while (s.History.Count > 0 && now - s.History[0].T > circleWindow) s.History.RemoveAt(0);

            string pose = Classify(f);
            if (pose != s.Pose)
            {
                if (s.Pose == "fist" && s.FistQualified) s.LastFistEnd = now;
                s.Pose = pose;
                s.PoseSince = now;
                s.FistQualified = false;
            }
            bool held = now - s.PoseSince >= minPoseHold;

            // Fist: grab / Volt Burst charge
            bool fist = pose == "fist" && held;
            if (fist) s.FistQualified = true;
            if (fist != s.Grab)
            {
                s.Grab = fist;
                Raise(fist ? GestureType.GrabStart : GestureType.GrabEnd, f.Side, f.PalmCenter, f.HandForward);
            }

            // Volt Burst: open hand shortly after a real fist (but not right after a punch)
            if (pose == "open" && now - s.LastFistEnd < VoltWindow)
            {
                s.LastFistEnd = -10f;
                bool wasPunch = motion != null && now - motion.LastStrikeTime(f.Side) < VoltWindow + 0.1f;
                if (!wasPunch) Raise(GestureType.VoltBurst, f.Side, f.PalmCenter, f.HandForward);
            }

            // Firebolt: two-finger point + fast flick along the head's forward
            if (pose == "point" && held && now - s.LastFirebolt > fireboltRefractory)
            {
                Vector3 v = Velocity(s, 0.08f);
                if (Vector3.Dot(v, head.forward) > FireboltSpeed)
                {
                    s.LastFirebolt = now;
                    Vector3 dir = (f[HandJoint.IndexTip] - f[HandJoint.IndexProximal]).normalized;
                    Raise(GestureType.Firebolt, f.Side, f[HandJoint.IndexTip], dir);
                }
            }

            // Lumen: open palm up, raised high
            Vector3 n = f.PalmNormal;
            if (pose == "open" && held && now - s.LastLumen > 1f &&
                Vector3.Dot(n, Vector3.up) > LumenFacing && f.Wrist.y > head.position.y + lumenHeight)
            {
                s.LastLumen = now;
                Raise(GestureType.Lumen, f.Side, f.PalmCenter, Vector3.up);
            }

            // Aegis: open palm facing forward, in front of the body, while held
            bool aegisPose = pose == "open" && held && Vector3.Dot(n, head.forward) > aegisFacing &&
                             Vector3.Dot(f.PalmCenter - head.position, head.forward) > ignoreNearHead;
            if (aegisPose)
            {
                s.AegisLostAt = -1f;
                if (!s.Aegis)
                {
                    s.Aegis = true;
                    Raise(GestureType.AegisStart, f.Side, f.PalmCenter, n);
                }
            }
            else if (s.Aegis)
            {
                if (s.AegisLostAt < 0f) s.AegisLostAt = now;
                else if (now - s.AegisLostAt > aegisReleaseGrace)
                {
                    s.Aegis = false;
                    Raise(GestureType.AegisEnd, f.Side, f.PalmCenter, n);
                }
            }

            // Circle tracking for Sunwheel (index finger out)
            if (f.Straightness(Finger.Index) > curledThreshold && DetectCircle(s, head))
            {
                s.CircleDoneAt = now;
                s.History.Clear();
            }
        }

        void Reset(HandSide side, HandState s, Transform head, float now)
        {
            s.History.Clear();
            s.Pose = "none";
            s.FistQualified = false;
            if (s.Grab) { s.Grab = false; Raise(GestureType.GrabEnd, side, head.position, head.forward); }
            if (s.Aegis) { s.Aegis = false; Raise(GestureType.AegisEnd, side, head.position, head.forward); }
        }

        string Classify(HandFrame f)
        {
            float index = f.Straightness(Finger.Index), middle = f.Straightness(Finger.Middle);
            float ring = f.Straightness(Finger.Ring), pinky = f.Straightness(Finger.Pinky);

            if (index > extendedThreshold && middle > extendedThreshold && ring > extendedThreshold && pinky > extendedThreshold)
                return "open";
            if (index < curledThreshold && middle < curledThreshold && ring < curledThreshold && pinky < curledThreshold)
                return "fist";
            if (index > extendedThreshold && middle > extendedThreshold && ring < curledThreshold && pinky < curledThreshold)
                return "point";
            return "other";
        }

        static Vector3 Velocity(HandState s, float window)
        {
            int last = s.History.Count - 1;
            if (last < 1) return Vector3.zero;
            Sample b = s.History[last];
            for (int i = last - 1; i >= 0; i--)
            {
                Sample a = s.History[i];
                if (b.T - a.T >= window || i == 0)
                    return b.T - a.T > 1e-4f ? (b.IndexTip - a.IndexTip) / (b.T - a.T) : Vector3.zero;
            }
            return Vector3.zero;
        }

        bool DetectCircle(HandState s, Transform head)
        {
            int count = s.History.Count;
            if (count < 12) return false;

            // Project onto the plane facing the player.
            Vector3 right = head.right, up = head.up;
            Vector2 centre = Vector2.zero;
            List<Vector2> pts = circlePoints;
            pts.Clear();
            for (int i = 0; i < count; i++)
            {
                Vector3 p = s.History[i].IndexTip;
                pts.Add(new Vector2(Vector3.Dot(p, right), Vector3.Dot(p, up)));
                centre += pts[i];
            }
            centre /= count;

            float meanR = 0f;
            for (int i = 0; i < count; i++) meanR += (pts[i] - centre).magnitude;
            meanR /= count;
            if (meanR < circleMinRadius || meanR > circleMaxRadius) return false;

            float variance = 0f, sweep = 0f;
            for (int i = 0; i < count; i++)
            {
                float d = (pts[i] - centre).magnitude - meanR;
                variance += d * d;
                if (i > 0) sweep += Vector2.SignedAngle(pts[i - 1] - centre, pts[i] - centre);
            }
            float roundness = Mathf.Sqrt(variance / count) / meanR;
            return roundness < 0.35f && Mathf.Abs(sweep) >= CircleCoverage;
        }

        // ---------------------------------------------------------------- two hands

        void UpdateSeal(Transform head, float now)
        {
            HandFrame l = input.Left, r = input.Right;
            if (!l.IsTracked || !r.IsTracked) return;

            float apart = Vector3.Distance(l.PalmCenter, r.PalmCenter);
            if (apart < sealTogether) lastTogether = now;

            float sideways = Mathf.Abs(Vector3.Dot(r.PalmCenter - l.PalmCenter, head.right));
            float vertical = Mathf.Abs(Vector3.Dot(r.PalmCenter - l.PalmCenter, head.up));
            if (now - lastTogether < sealWindow && now - lastSeal > 1f && sideways > sealApart && vertical < sealApart * 0.6f)
            {
                lastSeal = now;
                lastTogether = -10f;
                Raise(GestureType.Seal, HandSide.Right, (l.PalmCenter + r.PalmCenter) * 0.5f, head.forward);
            }
        }

        void UpdateSunwheel(Transform head, float now)
        {
            float l = state[0].CircleDoneAt, r = state[1].CircleDoneAt;
            if (now - lastSunwheel > 1f && now - l < SunwheelSync && now - r < SunwheelSync)
            {
                lastSunwheel = now;
                state[0].CircleDoneAt = state[1].CircleDoneAt = -10f;
                Vector3 centre = (input.Left.IsTracked && input.Right.IsTracked)
                    ? (input.Left.PalmCenter + input.Right.PalmCenter) * 0.5f
                    : head.position + head.forward * 0.4f;
                Raise(GestureType.Sunwheel, HandSide.Right, centre, head.forward);
            }
        }

        // ---------------------------------------------------------------- keyboard fallback

        void UpdateKeyboard(Transform head, float now)
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            Vector3 pos = head.position + head.forward * 0.4f - head.up * 0.15f;
            Vector3 fwd = head.forward;

            if (kb.digit1Key.wasPressedThisFrame) Raise(GestureType.Firebolt, HandSide.Right, pos, fwd, true);
            if (kb.digit3Key.wasPressedThisFrame) Raise(GestureType.Seal, HandSide.Right, pos, fwd, true);
            if (kb.digit4Key.wasPressedThisFrame) Raise(GestureType.VoltBurst, HandSide.Right, pos, fwd, true);
            if (kb.digit5Key.wasPressedThisFrame) Raise(GestureType.Lumen, HandSide.Right, pos + Vector3.up * 0.5f, Vector3.up, true);
            if (kb.digit6Key.wasPressedThisFrame) Raise(GestureType.Sunwheel, HandSide.Right, pos, fwd, true);

            bool aegis = kb.digit2Key.isPressed;
            if (aegis != keyboardAegis)
            {
                keyboardAegis = aegis;
                Raise(aegis ? GestureType.AegisStart : GestureType.AegisEnd, HandSide.Left, pos, fwd, true);
            }

            bool grab = kb.gKey.isPressed;
            if (grab != keyboardGrab)
            {
                keyboardGrab = grab;
                Raise(grab ? GestureType.GrabStart : GestureType.GrabEnd, HandSide.Right, pos, fwd, true);
            }
        }

        void Raise(GestureType type, HandSide hand, Vector3 position, Vector3 direction, bool fromKeyboard = false)
        {
            var e = new GestureEvent
            {
                Type = type, Hand = hand, Position = position, Direction = direction,
                Time = Time.time, FromKeyboard = fromKeyboard,
            };
            LastGesture = e;
            if (logGestures) Debug.Log($"[Gesture] {e}");
            OnGesture?.Invoke(e);
        }
    }
}
