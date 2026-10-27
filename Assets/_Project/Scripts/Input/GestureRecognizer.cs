using System;
using System.Collections.Generic;
using ArcaneStrike.Core;
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
        BladeStart, // Spark Blade: sword drawn from the hip, held while the fist stays closed
        BladeEnd,
        Skyhammer,  // both fists together overhead, slammed down: shockwave
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

        [Header("Firebolt: index + middle out, aim forward (or flick forward)")]
        [Tooltip("Pointing both fingers forward for this long fires one bolt. Aim away or change the pose to fire again.")]
        [SerializeField] float fireboltAimHold = 0.2f;
        [Tooltip("How straight ahead the fingers must point (1 = exactly forward, at the camera).")]
        [SerializeField] float fireboltAimFacing = 0.6f;
        [Tooltip("A forward flick faster than this fires at once, without the aim hold.")]
        [SerializeField] float fireboltSpeed = 1.2f;
        [SerializeField] float fireboltRefractory = 0.3f;

        [Header("Aegis: fist facing you, raise it, then bring it down (a warrior lifting a shield)")]
        [Tooltip("How much the palm side of the fist must face the player. Webcam hands skip this check.")]
        [SerializeField] float aegisFistFacing = 0.2f;
        [Tooltip("How far the fist must rise (m).")]
        [SerializeField] float aegisRise = 0.1f;
        [Tooltip("How far it must come back down after rising (m).")]
        [SerializeField] float aegisDrop = 0.06f;
        [Tooltip("The rise and drop must both happen within this time (s).")]
        [SerializeField] float aegisWindow = 1.5f;
        [Tooltip("The shield stays up while the fist is held; it drops this long after the fist opens or is lost.")]
        [SerializeField] float aegisReleaseGrace = 0.25f;

        [Header("Spark Blade: fist at the hip, draw it up fast like a sword from its sheath")]
        [Tooltip("The draw must start at least this far below the head (m): hip / waist height.")]
        [SerializeField] float bladeStartBelowHead = 0.4f;
        [Tooltip("How far the fist must rise during the draw (m).")]
        [SerializeField] float bladeDrawRise = 0.25f;
        [Tooltip("The rise must happen within this time (s).")]
        [SerializeField] float bladeDrawTime = 0.8f;
        [Tooltip("The sword stays while the fist is held; it vanishes this long after the fist opens or is lost.")]
        [SerializeField] float bladeReleaseGrace = 0.3f;

        [Header("Skyhammer: both fists together overhead, then slam them down")]
        [Tooltip("Fists closer than this (m) count as holding one hammer.")]
        [SerializeField] float hammerTogether = 0.2f;
        [Tooltip("Fists must be raised to at least this height relative to the head (m). Negative = below the head.")]
        [SerializeField] float hammerRaiseHeight = -0.1f;
        [Tooltip("How far the fists must come down (m), within Hammer Slam Time.")]
        [SerializeField] float hammerDrop = 0.35f;
        [SerializeField] float hammerSlamTime = 0.6f;
        [SerializeField] float hammerCooldown = 1.5f;

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
        [Tooltip("Keys 1-8 and G cast gestures without hands. Editor and development builds only.")]
        [SerializeField] bool keyboardFallback = true;
        [SerializeField] bool logGestures = true;

        public event Action<GestureEvent> OnGesture;

        bool Relaxed => relaxForWebcam && input != null && input.ActiveSource is WebcamHandSource;
        float FireboltSpeed => Relaxed ? fireboltSpeed * 0.6f : fireboltSpeed;
        float AegisRise => Relaxed ? Mathf.Min(aegisRise, 0.07f) : aegisRise;
        float BladeDrawRise => Relaxed ? Mathf.Min(bladeDrawRise, 0.2f) : bladeDrawRise;
        // A webcam can't see above your head, so the hammer only has to reach about shoulder height.
        float HammerRaiseHeight => Relaxed ? Mathf.Min(hammerRaiseHeight, -0.25f) : hammerRaiseHeight;
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
            public bool FireboltArmed = true;
            public float AimSince;
            public bool AegisTracking, AegisRose;
            public float AegisTrackStart, AegisLow, AegisHigh;
            public bool Blade, BladeTracking;
            public float BladeLostAt = -1f, BladeLowY, BladeLowAt;
            public float CircleDoneAt = -10f;
        }

        readonly HandState[] state = { new HandState(), new HandState() };
        readonly List<Vector2> circlePoints = new List<Vector2>(256);
        float lastTogether = -10f, lastSeal = -10f, lastSunwheel = -10f;
        bool keyboardAegis, keyboardGrab, keyboardBlade;

        // Both fists held together: one two-handed hammer grip, so single-fist spells (Aegis, Blade) stay quiet.
        bool hammerGrip;
        float lastHammerGrip = -10f, hammerHighY, hammerHighAt = -10f, lastSkyhammer = -10f;

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
            HandFrame l = input.Left, r = input.Right;
            hammerGrip = l.IsTracked && r.IsTracked && Classify(l) == "fist" && Classify(r) == "fist" &&
                         Vector3.Distance(l.PalmCenter, r.PalmCenter) < hammerTogether;
            if (hammerGrip) lastHammerGrip = now;

            for (int i = 0; i < 2; i++) UpdateHand(input.Get((HandSide)i), state[i], head, now);
            UpdateSeal(head, now);
            UpdateSunwheel(head, now);
            UpdateSkyhammer(head, now);
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
                if (s.Aegis) UpdateHeldSpell(f.Side, ref s.Aegis, ref s.AegisLostAt, false, aegisReleaseGrace, GestureType.AegisEnd, head.position, head, now);
                if (s.Blade) UpdateHeldSpell(f.Side, ref s.Blade, ref s.BladeLostAt, false, bladeReleaseGrace, GestureType.BladeEnd, head.position, head, now);
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
                s.AimSince = now;
                s.FistQualified = false;
            }
            bool held = now - s.PoseSince >= minPoseHold;

            // Fist: grab / Volt Burst charge (a fist holding a shield, sword or hammer is not a charge)
            bool fist = pose == "fist" && held;
            if (fist && !s.Aegis && !s.Blade && !hammerGrip) s.FistQualified = true;
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

            // Firebolt: two-finger point aimed forward for a moment (easy for webcams), or a fast forward flick
            if (pose != "point") s.FireboltArmed = true;
            else if (held && now - s.LastFirebolt > fireboltRefractory)
            {
                Vector3 dir = (f[HandJoint.IndexTip] - f[HandJoint.IndexProximal]).normalized;
                bool aimed = Vector3.Dot(dir, head.forward) > fireboltAimFacing;
                if (!aimed)
                {
                    s.FireboltArmed = true;
                    s.AimSince = now;
                }
                bool flicked = Vector3.Dot(Velocity(s, 0.08f), head.forward) > FireboltSpeed;
                if (flicked || (aimed && s.FireboltArmed && now - s.AimSince >= fireboltAimHold))
                {
                    s.LastFirebolt = now;
                    s.FireboltArmed = false;
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

            // Spark Blade: a fist drawn up fast from the hip. Checked before Aegis, whose small lift it would otherwise also make.
            if (s.Blade) UpdateHeldSpell(f.Side, ref s.Blade, ref s.BladeLostAt, fist, bladeReleaseGrace, GestureType.BladeEnd, f.PalmCenter, head, now);
            else if (fist && !s.Aegis && !hammerGrip) TrackBladeDraw(f, s, head, now);
            else s.BladeTracking = false;

            // Aegis: fist with the palm side towards you, raised, then brought down. The shield stays while the fist is held.
            if (s.Aegis) UpdateHeldSpell(f.Side, ref s.Aegis, ref s.AegisLostAt, fist, aegisReleaseGrace, GestureType.AegisEnd, f.PalmCenter, head, now);
            else if (fist && !s.Blade && !hammerGrip && AegisFistReady(f, head)) TrackAegisLift(f, s, head, now);
            else s.AegisTracking = false;

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
            s.AegisTracking = false;
            s.BladeTracking = false;
            if (s.Grab) { s.Grab = false; Raise(GestureType.GrabEnd, side, head.position, head.forward); }
            // Aegis and Blade are not dropped here: UpdateHeldSpell gives them a grace period, so a tracking flicker doesn't drop them.
        }

        void TrackBladeDraw(HandFrame f, HandState s, Transform head, float now)
        {
            float y = f.Wrist.y;
            // Remember the lowest point of the fist in the last Blade Draw Time; the draw is measured from there.
            if (!s.BladeTracking || y < s.BladeLowY || now - s.BladeLowAt > bladeDrawTime)
            {
                s.BladeTracking = true;
                s.BladeLowY = y;
                s.BladeLowAt = now;
            }

            bool startedLow = s.BladeLowY < head.position.y - bladeStartBelowHead;
            if (startedLow && y - s.BladeLowY >= BladeDrawRise)
            {
                s.Blade = true;
                s.BladeLostAt = -1f;
                s.BladeTracking = false;
                s.AegisTracking = false;
                s.FistQualified = false;
                Raise(GestureType.BladeStart, f.Side, f.PalmCenter, head.forward);
            }
        }

        bool AegisFistReady(HandFrame f, Transform head)
        {
            if (Vector3.Dot(f.PalmCenter - head.position, head.forward) <= ignoreNearHead) return false;
            if (Relaxed) return true; // webcams read palm direction badly
            return Vector3.Dot(f.PalmNormal, (head.position - f.PalmCenter).normalized) > aegisFistFacing;
        }

        void TrackAegisLift(HandFrame f, HandState s, Transform head, float now)
        {
            float y = f.Wrist.y;
            if (!s.AegisTracking || now - s.AegisTrackStart > aegisWindow)
            {
                s.AegisTracking = true;
                s.AegisRose = false;
                s.AegisTrackStart = now;
                s.AegisLow = s.AegisHigh = y;
            }

            if (!s.AegisRose && y < s.AegisLow) s.AegisLow = s.AegisHigh = y; // the lift is measured from the lowest point
            s.AegisHigh = Mathf.Max(s.AegisHigh, y);
            if (s.AegisHigh - s.AegisLow >= AegisRise) s.AegisRose = true;

            if (s.AegisRose && s.AegisHigh - y >= aegisDrop)
            {
                s.Aegis = true;
                s.AegisLostAt = -1f;
                s.AegisTracking = false;
                s.FistQualified = false;
                Raise(GestureType.AegisStart, f.Side, f.PalmCenter, head.forward);
            }
        }

        /// <summary>Keeps a held spell (Aegis, Blade) active while <paramref name="holding"/>, ending it after a grace period.</summary>
        void UpdateHeldSpell(HandSide side, ref bool active, ref float lostAt, bool holding, float grace,
                             GestureType endType, Vector3 position, Transform head, float now)
        {
            if (holding)
            {
                lostAt = -1f;
                return;
            }
            if (lostAt < 0f) lostAt = now;
            else if (now - lostAt > grace)
            {
                active = false;
                lostAt = -1f;
                Raise(endType, side, position, head.forward);
            }
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

        void UpdateSkyhammer(Transform head, float now)
        {
            // Fast slams blur on webcams and can break the grip for a few frames, so allow short gaps.
            if (now - lastHammerGrip > 0.25f || now - lastSkyhammer < hammerCooldown)
            {
                hammerHighAt = -10f;
                return;
            }
            HandFrame l = input.Left, r = input.Right;
            if (!l.IsTracked || !r.IsTracked) return;

            float y = (l.Wrist.y + r.Wrist.y) * 0.5f;
            bool raised = y > head.position.y + HammerRaiseHeight;
            if (hammerGrip && raised && (now - hammerHighAt > hammerSlamTime || y > hammerHighY))
            {
                hammerHighY = y;
                hammerHighAt = now;
            }

            if (now - hammerHighAt <= hammerSlamTime && hammerHighY - y >= hammerDrop)
            {
                lastSkyhammer = now;
                hammerHighAt = -10f;
                lastTogether = -10f; // pulling the fists apart afterwards must not count as Seal
                state[0].FistQualified = state[1].FistQualified = false;
                Vector3 centre = (l.PalmCenter + r.PalmCenter) * 0.5f;
                Raise(GestureType.Skyhammer, HandSide.Right, centre, Vector3.down);
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
            if (kb.digit8Key.wasPressedThisFrame) Raise(GestureType.Skyhammer, HandSide.Right, pos, Vector3.down, true);

            bool blade = kb.digit7Key.isPressed;
            if (blade != keyboardBlade)
            {
                keyboardBlade = blade;
                Raise(blade ? GestureType.BladeStart : GestureType.BladeEnd, HandSide.Right, pos, fwd, true);
            }

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
            if (!PowerUnlocks.Allows(type)) return; // not learned yet in this chapter

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
