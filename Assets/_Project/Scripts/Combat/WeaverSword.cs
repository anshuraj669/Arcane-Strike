using System;
using System.Collections;
using System.Collections.Generic;
using ArcaneStrike.HandInput;
using ArcaneStrike.Story;
using ArcaneStrike.VFX;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ArcaneStrike.Combat
{
    /// <summary>The sword fighting techniques, told apart by how the blade moves.</summary>
    public enum SwordTechnique { None, Slash, Thrust, Overhead, Whirlwind, JyotiWave, Rising, Cleave, Lunge, Execution, Deflect }

    /// <summary>
    /// The Weaver's sword in the player's hand (the katana from the outfit, <see cref="WarriorOutfit.DrawSword"/>), and
    /// sword fighting with it. <see cref="Draw"/> pulls it from the sheath on the back: it slides up out of the sheath
    /// behind the right shoulder and swings over into the hand. The blade follows the tracked right hand (it leaves the fist on the thumb side, like
    /// holding a real hilt), and every fast swing is read as one of these techniques:
    /// - Slash: any quick cut. Damage grows with the blade's speed.
    /// - Thrust: driving the point straight ahead (the blade moves along itself). Reaches further, hits hard.
    /// - Overhead smash: blade raised above the head, then brought down hard. Heavy hit, and a shockwave on the ground
    ///   that staggers everything nearby.
    /// - Whirlwind: one sweep most of the way round you. Hits everything within reach.
    /// - Jyoti Wave: hold the sword upright and still until it glows (charged), then slash: a crescent of light flies
    ///   out and cuts through everything in its path.
    /// - Rising slash: up from low. Breaks a guard and knocks the enemy off balance.
    /// - Cleave: a wide, flat sweep at chest height: hits everything in front of you.
    /// - Lunge: a thrust so fast it carries you in with it (a short dash).
    /// - Parry: shadows telegraph their strikes; hold the blade across their claws (or be swinging through them) and
    ///   the strike is turned aside and the shadow reels. Hitting a reeling shadow is a Riposte (double damage); a
    ///   Thrust or Lunge into a reeling enemy that's badly hurt is an Execution.
    /// - Deflect: swing through (or guard against) a missile and it flies back at whoever threw it.
    /// - Blocks: an enemy's guard stops plain slashes (the blade rebounds); Thrust, Overhead, Rising and Lunge break it.
    /// - Combo: three hits in quick succession or more add a bonus, and some chains are named techniques: Cross Cut
    ///   (two slashes from opposite sides), Tri-Fang (slash, slash, thrust), Heaven Splitter (rising then overhead),
    ///   Dragon's Tail (slash then rising), Storm Bloom (cleave then whirlwind), Flash Step (lunge then cleave),
    ///   Hundred Needles (three thrusts). Heavy blows land with a jolt (a split second of slow motion).
    /// Without a tracked hand the sword rests at the right of the view and the keyboard and mouse swing it
    /// (<see cref="GameInput"/>): left click slash, F thrust, Q overhead smash, V whirlwind, B rising slash, U cleave,
    /// I lunge, hold Shift to guard (parry), hold R to charge.
    /// </summary>
    [DefaultExecutionOrder(220)] // after the hands and the outfit
    public class WeaverSword : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] WarriorOutfit outfit;
        [SerializeField] HandSide hand = HandSide.Right;

        [Header("Blade (the sword model: grip at the pivot, blade along +Y)")]
        [SerializeField] float bladeStart = 0.17f; // the sword: guard 0.155 m above the grip's middle
        [SerializeField] float bladeEnd = 1.01f;   // (the point: build_weaver.py's SWORD_TIP - SWORD_GRIP)
        [Tooltip("Seconds the draw takes: out of the sheath on the back and over the shoulder into the hand.")]
        [SerializeField] float drawTime = 0.6f;
        [SerializeField] float bladeRadius = 0.06f;
        [Tooltip("How quickly the blade follows the hand. Lower is smoother on jittery tracking, higher is snappier.")]
        [SerializeField] float followSharpness = 30f;
        [Tooltip("Seconds the blade holds still where the hand was when hand tracking drops out (a few lost frames " +
                 "shouldn't throw the sword to its rest pose and back).")]
        [SerializeField] float trackingGrace = 0.3f;

        [Header("Swings")]
        [Tooltip("Blade tip speed (m/s) that starts a swing.")]
        [SerializeField] float swingSpeed = 2.2f;
        [Tooltip("Below this tip speed the swing is over.")]
        [SerializeField] float swingEndSpeed = 1.1f;
        [SerializeField] Vector2 slashDamage = new Vector2(16f, 34f);
        [Tooltip("A tracked swing's damage is scaled by how fast the blade meets the target: x at the end of a swing " +
                 "(a slow touch), y at full speed (7 m/s and up).")]
        [SerializeField] Vector2 speedPower = new Vector2(0.5f, 1.15f);
        [SerializeField] float thrustDamage = 40f;
        [SerializeField] float thrustReach = 0.35f;
        [SerializeField] float overheadDamage = 50f;
        [SerializeField] float shockwaveDamage = 18f;
        [SerializeField] float shockwaveRadius = 2.2f;
        [SerializeField] float whirlwindDamage = 30f;
        [SerializeField] float whirlwindRadius = 2.4f;
        [Tooltip("Degrees the tip must sweep round the player within 0.6 s for a whirlwind.")]
        [SerializeField] float whirlwindSweep = 170f;
        [SerializeField] float waveDamage = 40f;
        [SerializeField] float risingDamage = 30f;
        [SerializeField] float cleaveDamage = 26f;
        [SerializeField] float cleaveRadius = 2.2f;
        [SerializeField] float cleaveAngle = 150f;
        [SerializeField] float lungeDamage = 45f;
        [Tooltip("How far a Lunge carries the player in (m).")]
        [SerializeField] float lungeDistance = 1.1f;
        [Tooltip("An enemy reeling at or below this share of its health can be Executed (Thrust or Lunge).")]
        [SerializeField] float executeBelow = 0.25f;
        [SerializeField] float executeDamage = 150f;

        [Header("Power-ups")]
        [Tooltip("Seconds the Overhead Smash takes to power up: after the sword is drawn, and again after each smash. " +
                 "Until then a downward swing is an ordinary slash.")]
        [SerializeField] float overheadPowerUp = 5f;
        [Tooltip("Seconds the Whirlwind takes to power up: after the sword is drawn, and again after each whirlwind.")]
        [SerializeField] float whirlwindPowerUp = 8f;
        [SerializeField] float waveRange = 11f;
        [SerializeField] float waveSpeed = 11f;
        [Tooltip("Seconds of holding the sword upright and still to charge it.")]
        [SerializeField] float chargeTime = 1.2f;
        [SerializeField] float riposteMultiplier = 2f;
        [SerializeField] float comboWindow = 1.3f;
        [SerializeField] float comboBonus = 0.15f;

        [Header("Look")]
        [SerializeField] Color trailColor = new Color(1f, 0.8f, 0.4f, 0.8f);
        [SerializeField] Color chargedColor = new Color(1f, 0.95f, 0.6f, 1f);
        [Tooltip("Mouse and keys (GameInput) swing the sword when the hand isn't tracked.")]
        [SerializeField] bool keyboardFallback = true;

        public static WeaverSword Instance { get; private set; }
        public bool Drawn { get; private set; }
        /// <summary>The sword itself, while it is out (<see cref="WeaverArms"/> closes a hand on it).</summary>
        public Transform Held => sword;
        public bool Charged => charged;
        /// <summary>How far the Overhead Smash has powered up (0 .. 1 = ready).</summary>
        public float OverheadPower => PowerOf(overheadReadyAt, overheadPowerUp);
        /// <summary>How far the Whirlwind has powered up (0 .. 1 = ready).</summary>
        public float WhirlwindPower => PowerOf(whirlwindReadyAt, whirlwindPowerUp);
        public bool OverheadReady => Time.time >= overheadReadyAt;
        public bool WhirlwindReady => Time.time >= whirlwindReadyAt;
        /// <summary>The swing in progress (None between swings).</summary>
        public SwordTechnique Current => swinging ? kind : SwordTechnique.None;
        /// <summary>Every swing that starts, with its technique.</summary>
        public event Action<SwordTechnique> OnTechnique;
        /// <summary>Every hit that lands: the target, the damage, and the technique.</summary>
        public event Action<IHittable, float, SwordTechnique> OnHit;
        /// <summary>A strike was parried.</summary>
        public event Action OnParry;
        /// <summary>A named chain landed (Cross Cut, Heaven Splitter...).</summary>
        public event Action<string> OnCombo;

        /// <summary>Practice: the Overhead Smash and the Whirlwind ready at once, every time.</summary>
        public void ChargeAllPowerUps()
        {
            overheadReadyAt = whirlwindReadyAt = Time.time;
            overheadAnnounced = whirlwindAnnounced = true; // (no announcements while practising)
        }

        Transform sword, head;
        float drawnAt = -1f;
        bool fromSheath;
        TrailRenderer trail;
        Light chargeLight;
        Vector3 grip, lastTip, tipVelocity;
        Quaternion rotation = Quaternion.identity;
        bool swinging, charged, shockwaveDone;
        SwordTechnique kind;
        float slowSince, raisedUntil, stillTime, chargedAt, lastHitTime;
        int combo;
        float overheadReadyAt, whirlwindReadyAt;
        bool overheadAnnounced, whirlwindAnnounced;
        readonly HashSet<IHittable> hitThisSwing = new HashSet<IHittable>();
        readonly Collider[] overlaps = new Collider[24];
        readonly Queue<(float time, float angle)> sweep = new Queue<(float, float)>();

        // Tracking: a dropout holds the blade still; re-acquiring (or settling back to rest) reads no swing off the jump.
        bool wasTracked, holding;
        float lastTrackedAt = -1f, settleUntil, grazeAt, slowedTo;
        Vector3 lastBase, prevBase, prevTip;

        // Named chains: the last few swings (technique, which side it came from, when).
        readonly List<(SwordTechnique kind, int side, float time)> chain = new List<(SwordTechnique, int, float)>();
        string chainName;
        float chainBonus = 1f, lowUntil, recoilUntil;
        Vector3 recoil;

        // Keyboard swings: a scripted path for the blade over a short time.
        SwordTechnique scripted;
        float scriptedTime, scriptedLength;
        int slashSide = 1;
        bool guarding;

        Vector3 Tip => grip + recoil + rotation * Vector3.up * bladeEnd;
        Vector3 Base => grip + recoil + rotation * Vector3.up * bladeStart;
        Vector3 BladeDirection => rotation * Vector3.up;

        void Awake()
        {
            Instance = this;
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            if (outfit == null) outfit = FindAnyObjectByType<WarriorOutfit>();
            // The clips are made from code: build them now, not with a hitch on the first blow.
            _ = CombatAudio.Swoosh; _ = CombatAudio.HeavySwoosh; _ = CombatAudio.Hit; _ = CombatAudio.Clang; _ = CombatAudio.Wave;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>The sword comes out of its scabbard into the hand, with a flash.</summary>
        public void Draw()
        {
            if (Drawn) return;
            head = StoryFx.Head(input);
            sword = outfit != null ? outfit.DrawSword() : null;
            if (sword == null) sword = PlaceholderSword();
            Drawn = true;
            overheadReadyAt = Time.time + overheadPowerUp; // both power up from the moment the sword is out
            whirlwindReadyAt = Time.time + whirlwindPowerUp;
            overheadAnnounced = whirlwindAnnounced = false;
            Pose(true);
            lastTip = prevTip = Tip;
            lastBase = prevBase = Base;
            drawnAt = Time.time;
            fromSheath = outfit != null && outfit.SheathedPose(out _, out _);
            if (!fromSheath) sword.SetPositionAndRotation(grip, rotation);
            AddTrail();
            CombatAudio.Play(CombatAudio.Swoosh, head != null ? head.position : grip, 0.6f, 0.1f); // the long hiss of the draw
            Debug.Log("[Sword] Drawn. Keys: left click slash, F thrust, Q overhead smash, V whirlwind, hold Shift guard, hold R charge (F1: all controls).");
        }

        public void Sheathe()
        {
            if (!Drawn) return;
            Drawn = false;
            if (swinging)
            {
                shockwaveDone = true;
                EndSwing();
            }
            if (charged) SetCharged(false);
            guarding = false;
            if (outfit != null) outfit.SheatheSword();
            else if (sword != null) sword.gameObject.SetActive(false);
            if (trail != null) Destroy(trail.gameObject);
        }

        // Without the Weaver model: a plain blade, so fights still work.
        Transform PlaceholderSword()
        {
            var root = new GameObject("PlaceholderSword").transform;
            var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(blade.GetComponent<Collider>());
            blade.transform.SetParent(root, false);
            blade.transform.localPosition = Vector3.up * (bladeStart + bladeEnd) * 0.5f;
            blade.transform.localScale = new Vector3(0.045f, bladeEnd - bladeStart, 0.008f);
            blade.GetComponent<Renderer>().sharedMaterial = AegisShield.CreateFaceMaterial(new Color(0.9f, 0.92f, 1f, 1f));
            return root;
        }

        void AddTrail()
        {
            var go = new GameObject("SwordTrail");
            go.transform.SetParent(sword, false);
            go.transform.localPosition = Vector3.up * (bladeEnd - 0.05f);
            trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = SparkHands.CreateGlowMaterial(SparkHands.CreateSoftLine(64));
            trail.time = 0.18f;
            trail.minVertexDistance = 0.02f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.12f), new Keyframe(1f, 0f));
            trail.emitting = false;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetTrailColour(trailColor);
        }

        void SetTrailColour(Color c)
        {
            if (trail == null) return;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                      new[] { new GradientAlphaKey(c.a, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = g;
        }

        void LateUpdate()
        {
            if (!Drawn || sword == null) return;
            if (head == null) head = StoryFx.Head(input);
            if (head == null) return;
            // Real time: the blade follows the real hand at the real hand's speed, even through a hit-stop.
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);

            Pose(false);
            if (DrawingFromSheath())
            {
                HoldInHand();
                lastTip = Tip;
                lastBase = Base;
                return; // (no swings until it is in the hand)
            }
            recoil = Vector3.Lerp(recoil, Vector3.zero, 1f - Mathf.Exp(-10f * dt));
            sword.SetPositionAndRotation(grip + recoil, rotation);
            HoldInHand();
            Vector3 tip = Tip;
            Vector3 raw = (tip - lastTip) / dt;
            if (raw.sqrMagnitude > 30f * 30f) Settle(); // no hand moves a blade that fast: a tracking jump
            prevTip = lastTip;
            prevBase = lastBase;
            lastTip = tip;
            lastBase = Base;
            bool settling = Time.unscaledTime < settleUntil;
            tipVelocity = settling ? Vector3.zero : Vector3.Lerp(tipVelocity, raw, 1f - Mathf.Exp(-66f * dt)); // (0.6 a frame at 72 Hz)
            float speed = tipVelocity.magnitude;

            if (tip.y > head.position.y + 0.1f) raisedUntil = Time.time + 0.7f;
            if (tip.y < head.position.y - 0.55f) lowUntil = Time.time + 0.5f;
            AnnouncePowerUps();
            TrackSweep(tip);
            UpdateCharge(speed, dt);

            if (!swinging && speed > swingSpeed && Time.time > recoilUntil && !settling) BeginSwing(speed);
            if (!swinging && !settling) Graze(speed);
            if (swinging)
            {
                if (trail != null) trail.widthMultiplier = Mathf.Lerp(0.6f, 1.5f, Mathf.InverseLerp(swingEndSpeed, 8f, speed)); // a harder swing, a bolder streak
                if (kind == SwordTechnique.Slash && WhirlwindReady && Swept() > whirlwindSweep) Whirlwind();
                CheckHits(speed);
                if (kind == SwordTechnique.Overhead && !shockwaveDone && tip.y < head.position.y - 0.9f) Shockwave(tip);
                bool done = scripted != SwordTechnique.None ? Time.time > scriptedTime + scriptedLength : speed < swingEndSpeed;
                slowSince = done ? slowSince + dt : 0f;
                if (slowSince > 0.06f) EndSwing();
            }
        }

        // ---------------------------------------------------------------- holding

        // Without a tracked hand nothing moves the glove: it follows the sword's grip through the keyboard swings.
        void HoldInHand()
        {
            HandFrame f = input != null ? input.Get(hand) : null;
            if (outfit != null && hand == HandSide.Right && (f == null || !f.IsTracked)) outfit.HoldSword(sword.position, sword.rotation);
        }

        // The draw: first straight up out of the sheath on the back (along the blade), then over the shoulder into the
        // hand, turning as it comes. Smooth start to smooth stop, so it reads clearly in a headset.
        bool DrawingFromSheath()
        {
            if (!fromSheath || drawnAt < 0f) return false;
            float t = (Time.time - drawnAt) / Mathf.Max(drawTime, 0.05f);
            if (t >= 1f || outfit == null || !outfit.SheathedPose(out Vector3 sheathed, out Quaternion hang))
            {
                fromSheath = false;
                sword.SetPositionAndRotation(grip, rotation);
                CombatFx.HitSparks(Tip, BladeDirection, 30, trailColor);
                CombatAudio.Play(CombatAudio.Clang, grip, 0.35f, 0.15f); // the ring of the blade as it clears
                return false;
            }
            const float slide = 0.4f; // the first part of the draw: out of the sheath
            Vector3 outOfSheath = sheathed - hang * Vector3.up * (bladeEnd - bladeStart + 0.05f);
            if (t < slide)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / slide);
                sword.SetPositionAndRotation(Vector3.Lerp(sheathed, outOfSheath, k), hang);
            }
            else
            {
                float k = Mathf.SmoothStep(0f, 1f, (t - slide) / (1f - slide));
                Vector3 arc = Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.15f;
                sword.SetPositionAndRotation(Vector3.Lerp(outOfSheath, grip, k) + arc, Quaternion.Slerp(hang, rotation, k));
            }
            return true;
        }

        void Pose(bool snap)
        {
            HandFrame f = input != null ? input.Get(hand) : null;
            bool tracked = f != null && f.IsTracked;
            if (tracked && !wasTracked && lastTrackedAt >= 0f && scripted == SwordTechnique.None)
            {
                // Back after a blink mid-swing (fast hands drop a frame or two): carry on. Back after longer, or far
                // from where it was lost: the blade is in the hand at once, not swept across, and nothing is hit.
                bool blink = Time.unscaledTime - lastTrackedAt < 0.12f && (f.PalmCenter - grip).sqrMagnitude < 0.36f;
                if (!blink)
                {
                    snap = true;
                    Settle();
                }
            }
            wasTracked = tracked;
            if (tracked) lastTrackedAt = Time.unscaledTime;
            // A dropout of a few frames: the blade stays where the hand was, then (still lost) settles to rest.
            bool hold = !tracked && scripted == SwordTechnique.None && lastTrackedAt >= 0f && Time.unscaledTime - lastTrackedAt < trackingGrace;
            if (holding && !hold && !tracked) Settle(); // (lost for good: off to the rest pose)
            holding = hold;
            if (hold && !snap) return;
            Vector3 wantGrip;
            Quaternion want;
            float sharpness = followSharpness;
            if (tracked && scripted == SwordTechnique.None)
            {
                // Out of the fist on the thumb side, flats facing out of the palm.
                Vector3 dir = f[HandJoint.IndexProximal] - f[HandJoint.PinkyProximal];
                if (dir.sqrMagnitude < 1e-6f) return;
                wantGrip = f.PalmCenter;
                want = Quaternion.LookRotation(dir.normalized, f.PalmNormal) * Quaternion.Euler(90f, 0f, 0f);
                if (f.Confidence < 0.5f) sharpness *= 0.4f; // low-confidence joints jitter: follow more softly
            }
            else
            {
                if (head == null) return;
                KeyboardPose(out wantGrip, out Vector3 bladeDir, out Vector3 flat);
                want = Quaternion.LookRotation(bladeDir, -flat) * Quaternion.Euler(90f, 0f, 0f);
                snap |= scripted != SwordTechnique.None; // scripted swings are exact
            }
            float k = snap ? 1f : 1f - Mathf.Exp(-sharpness * Time.unscaledDeltaTime);
            grip = Vector3.Lerp(grip, wantGrip, k);
            rotation = Quaternion.Slerp(rotation, want, k);
        }

        // The blade is about to jump (tracking lost or found): read no speed off it for a moment, and end a swing in progress.
        void Settle()
        {
            settleUntil = Time.unscaledTime + 0.2f;
            tipVelocity = Vector3.zero;
            if (swinging && scripted == SwordTechnique.None)
            {
                shockwaveDone = true; // (a lost hand is no overhead smash)
                EndSwing();
            }
        }

        // The blade drawn slowly through an enemy (no swing): no damage, but it doesn't pass through silently either.
        void Graze(float speed)
        {
            if (speed < 0.4f || Time.time < grazeAt) return;
            int n = Physics.OverlapCapsuleNonAlloc(Base, Tip, bladeRadius, overlaps, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                if (overlaps[i].GetComponentInParent<IHittable>() == null) continue;
                Vector3 at = overlaps[i].ClosestPoint(Vector3.Lerp(Base, Tip, 0.7f));
                CombatFx.HitSparks(at, -tipVelocity.normalized, 6, trailColor);
                CombatAudio.Play(CombatAudio.Clang, at, 0.15f, 0.3f);
                CombatFx.Rumble(hand, 0.25f, 0.04f);
                grazeAt = Time.time + 0.3f;
                return;
            }
        }

        // The sword in the view's frame: at rest low on the right pointing ahead, or partway through a keyboard swing.
        void KeyboardPose(out Vector3 gripAt, out Vector3 bladeDir, out Vector3 flat)
        {
            Transform h = head;
            Vector3 right = h.right, up = h.up, fwd = h.forward;
            Vector3 Local(float x, float y, float z) => h.position + right * x + up * y + fwd * z;
            Vector3 Dir(float x, float y, float z) => (right * x + up * y + fwd * z).normalized;

            float t = scriptedLength > 0f ? Mathf.Clamp01((Time.time - scriptedTime) / scriptedLength) : 1f;
            float e = t * t * (3f - 2f * t);
            switch (scripted)
            {
                case SwordTechnique.Slash:
                case SwordTechnique.JyotiWave:
                {
                    // A diagonal cut across the view, alternating sides.
                    float a = Mathf.Lerp(70f, -70f, e) * slashSide;
                    Quaternion arc = Quaternion.AngleAxis(a, up) * Quaternion.AngleAxis(Mathf.Lerp(-25f, 20f, e), right);
                    gripAt = Local(0.08f * slashSide * (1f - 2f * e), -0.25f, 0.35f);
                    bladeDir = arc * fwd;
                    flat = up;
                    return;
                }
                case SwordTechnique.Rising:
                {
                    // Up from low on the right to high on the left, edge leading.
                    float pitch = Mathf.Lerp(-55f, 80f, e);
                    gripAt = Local(Mathf.Lerp(0.18f, -0.06f, e), Mathf.Lerp(-0.5f, 0.12f, e), 0.35f);
                    bladeDir = Quaternion.AngleAxis(Mathf.Lerp(25f, -20f, e), up) * (Quaternion.AngleAxis(-pitch, right) * fwd);
                    flat = right;
                    return;
                }
                case SwordTechnique.Cleave:
                {
                    // A wide, flat sweep from far right to far left at chest height.
                    float a = Mathf.Lerp(105f, -105f, e);
                    gripAt = Local(Mathf.Lerp(0.25f, -0.2f, e), -0.18f, 0.35f);
                    bladeDir = Quaternion.AngleAxis(a, up) * fwd;
                    flat = up;
                    return;
                }
                case SwordTechnique.Lunge:
                case SwordTechnique.Thrust:
                {
                    float reach = t < 0.5f ? Mathf.SmoothStep(0f, 1f, t * 2f) : Mathf.SmoothStep(1f, 0f, (t - 0.5f) * 2f);
                    gripAt = Local(0.1f, -0.22f, Mathf.Lerp(0.15f, scripted == SwordTechnique.Lunge ? 0.75f : 0.6f, reach));
                    bladeDir = fwd;
                    flat = up;
                    return;
                }
                case SwordTechnique.Overhead:
                {
                    // Up over the head (first 40%), then down hard in front.
                    float raise = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t / 0.4f));
                    float chop = Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, (t - 0.4f) / 0.6f));
                    float pitch = Mathf.Lerp(Mathf.Lerp(10f, 120f, raise), -60f, chop);
                    gripAt = Local(0.05f, Mathf.Lerp(Mathf.Lerp(-0.25f, 0.25f, raise), -0.35f, chop), 0.3f);
                    bladeDir = Quaternion.AngleAxis(-pitch, right) * fwd;
                    flat = right;
                    return;
                }
                case SwordTechnique.Whirlwind:
                {
                    // A full turn round the player at arm's length.
                    float a = 360f * e - 60f;
                    Vector3 flatFwd = StoryFx.Flat(fwd);
                    Vector3 outward = Quaternion.AngleAxis(a, Vector3.up) * flatFwd;
                    gripAt = h.position + outward * 0.45f - Vector3.up * 0.35f;
                    bladeDir = (outward + Vector3.up * 0.1f).normalized;
                    flat = Vector3.up;
                    return;
                }
            }
            if (guarding)
            {
                // Blade across in front of the chest, edge out.
                gripAt = Local(0.2f, -0.2f, 0.35f);
                bladeDir = Dir(-1f, 0.15f, 0.05f);
                flat = fwd;
                return;
            }
            if (GameInput.Held(GameAction.SwordCharge) || charged)
            {
                // Upright before the face, gathering light.
                gripAt = Local(0.12f, -0.3f, 0.32f);
                bladeDir = Vector3.up;
                flat = fwd;
                return;
            }
            gripAt = Local(0.22f, -0.32f, 0.3f);
            bladeDir = Dir(-0.05f, 0.55f, 0.85f);
            flat = right;
        }

        void Update()
        {
            if (!Drawn || !keyboardFallback || head == null) return;
            HandFrame f = input != null ? input.Get(hand) : null;
            if (f != null && f.IsTracked)
            {
                guarding = false; // (a guard held on the keys doesn't stay up once the hand takes the sword)
                return;
            }
            guarding = GameInput.Held(GameAction.SwordGuard);
            if (scripted != SwordTechnique.None || Time.time < recoilUntil) return;
            if (GameInput.Pressed(GameAction.SwordSlash)) StartScripted(SwordTechnique.Slash, 0.24f);
            else if (GameInput.Pressed(GameAction.SwordThrust)) StartScripted(SwordTechnique.Thrust, 0.32f);
            else if (GameInput.Pressed(GameAction.SwordRising)) StartScripted(SwordTechnique.Rising, 0.3f);
            else if (GameInput.Pressed(GameAction.SwordCleave)) StartScripted(SwordTechnique.Cleave, 0.38f);
            else if (GameInput.Pressed(GameAction.SwordLunge)) StartScripted(SwordTechnique.Lunge, 0.36f);
            else if (GameInput.Pressed(GameAction.SwordOverhead))
            {
                if (OverheadReady) StartScripted(SwordTechnique.Overhead, 0.55f);
                else NotReady(SwordTechnique.Overhead, overheadReadyAt);
            }
            else if (GameInput.Pressed(GameAction.SwordWhirlwind))
            {
                if (WhirlwindReady) StartScripted(SwordTechnique.Whirlwind, 0.5f);
                else NotReady(SwordTechnique.Whirlwind, whirlwindReadyAt);
            }
        }

        void StartScripted(SwordTechnique technique, float seconds)
        {
            scripted = technique;
            scriptedTime = Time.time;
            scriptedLength = seconds;
            slashSide = -slashSide;
            if (technique == SwordTechnique.Overhead) raisedUntil = Time.time + 1f;
            BeginSwing(swingSpeed, technique);
        }

        // ---------------------------------------------------------------- power-ups

        static float PowerOf(float readyAt, float duration) =>
            duration <= 0f ? 1f : Mathf.Clamp01(1f - (readyAt - Time.time) / duration);

        void SpendOverhead()
        {
            overheadReadyAt = Time.time + overheadPowerUp;
            overheadAnnounced = false;
        }

        void SpendWhirlwind()
        {
            whirlwindReadyAt = Time.time + whirlwindPowerUp;
            whirlwindAnnounced = false;
        }

        // A move has finished powering up: say so, with a flare along the blade.
        void AnnouncePowerUps()
        {
            if (!overheadAnnounced && OverheadReady)
            {
                overheadAnnounced = true;
                Announce("OVERHEAD SMASH READY", new Color(1f, 0.65f, 0.3f));
            }
            if (!whirlwindAnnounced && WhirlwindReady)
            {
                whirlwindAnnounced = true;
                Announce("WHIRLWIND READY", new Color(0.55f, 0.85f, 1f));
            }
        }

        void Announce(string text, Color color)
        {
            CombatFx.Popup(text, head.position + head.forward * 1.3f + Vector3.up * 0.4f, color, 0.8f);
            for (int i = 0; i < 25; i++)
                StoryFx.Emit(CombatFx.Sparks, Vector3.Lerp(Base, Tip, Random.value), Random.insideUnitSphere * 0.4f, 0.02f, 0.5f, color);
            CombatAudio.Play(CombatAudio.Clang, grip, 0.25f, 0f);
            CombatFx.Rumble(hand, 0.3f, 0.1f);
        }

        // A key for a move still powering up: how long it has left.
        void NotReady(SwordTechnique technique, float readyAt)
        {
            CombatFx.Popup($"{Name(technique)}: {Mathf.CeilToInt(readyAt - Time.time)}s", head.position + head.forward * 1.2f + Vector3.up * 0.2f,
                           new Color(0.7f, 0.7f, 0.75f), 0.7f);
        }

        // ---------------------------------------------------------------- reading the swing

        void BeginSwing(float speed, SwordTechnique forced = SwordTechnique.None)
        {
            swinging = true;
            slowSince = 0f;
            shockwaveDone = false;
            hitThisSwing.Clear();
            kind = forced != SwordTechnique.None ? forced : Classify();
            if (charged && (kind == SwordTechnique.Slash || kind == SwordTechnique.Whirlwind)) kind = SwordTechnique.JyotiWave;
            // The big moves need their power-up: until then the swing is an ordinary slash. Using one starts it again.
            if (kind == SwordTechnique.Overhead)
            {
                if (OverheadReady) SpendOverhead();
                else kind = SwordTechnique.Slash;
            }
            if (kind == SwordTechnique.Whirlwind)
            {
                if (WhirlwindReady) SpendWhirlwind();
                else kind = SwordTechnique.Slash;
            }
            if (trail != null)
            {
                trail.emitting = true;
                SetTrailColour(kind == SwordTechnique.JyotiWave ? chargedColor : trailColor);
            }
            bool heavy = kind == SwordTechnique.Overhead || kind == SwordTechnique.Whirlwind;
            CombatAudio.Play(heavy ? CombatAudio.HeavySwoosh : CombatAudio.Swoosh, grip, 0.7f);
            if (kind != SwordTechnique.Slash) CombatFx.Popup(Name(kind), head.position + head.forward * 1.2f + Vector3.up * 0.25f, trailColor, 0.9f);
            ReadChain();
            if (kind == SwordTechnique.JyotiWave) ReleaseWave();
            if (kind == SwordTechnique.Whirlwind) Whirlwind(true);
            if (kind == SwordTechnique.Lunge) StartCoroutine(Dash(StoryFx.Flat(head.forward) * lungeDistance, 0.16f));
            if (kind == SwordTechnique.Cleave) Cleave();
            OnTechnique?.Invoke(kind);
        }

        SwordTechnique Classify()
        {
            Vector3 v = tipVelocity;
            float speed = v.magnitude, flat = new Vector2(v.x, v.z).magnitude;
            if (Vector3.Dot(v.normalized, BladeDirection) > 0.72f) // moving along itself: the point leads
                return speed > 5.5f ? SwordTechnique.Lunge : SwordTechnique.Thrust;
            if (v.y < -2.5f && Time.time < raisedUntil) return SwordTechnique.Overhead;
            if (v.y > 2.5f && v.y > flat * 0.8f && Time.time < lowUntil) return SwordTechnique.Rising;
            if (Mathf.Abs(BladeDirection.y) < 0.4f && Mathf.Abs(v.y) < speed * 0.35f && speed > 3.2f) return SwordTechnique.Cleave;
            return SwordTechnique.Slash;
        }

        // Which side a swing came from (+1 right to left, -1 left to right): two slashes from opposite sides cross.
        int Side() => scripted != SwordTechnique.None ? slashSide : Vector3.Dot(tipVelocity, head.right) < 0f ? 1 : -1;

        // The swing so far and the ones before it: does it complete a named chain?
        void ReadChain()
        {
            chain.RemoveAll(c => Time.time - c.time > 1.3f);
            chain.Add((kind, Side(), Time.time));
            chainName = null;
            chainBonus = 1f;
            int n = chain.Count;
            SwordTechnique Ago(int back) => n - 1 - back >= 0 ? chain[n - 1 - back].kind : SwordTechnique.None;
            if (n >= 3 && Ago(0) == SwordTechnique.Thrust && Ago(1) == SwordTechnique.Thrust && Ago(2) == SwordTechnique.Thrust) Named("HUNDRED NEEDLES", 1.5f);
            else if (n >= 3 && Ago(0) == SwordTechnique.Thrust && Ago(1) == SwordTechnique.Slash && Ago(2) == SwordTechnique.Slash) Named("TRI-FANG", 1.5f);
            else if (Ago(0) == SwordTechnique.Overhead && Ago(1) == SwordTechnique.Rising) Named("HEAVEN SPLITTER", 1.7f);
            else if (Ago(0) == SwordTechnique.Rising && Ago(1) == SwordTechnique.Slash) Named("DRAGON'S TAIL", 1.3f);
            else if (Ago(0) == SwordTechnique.Whirlwind && Ago(1) == SwordTechnique.Cleave) Named("STORM BLOOM", 1.4f);
            else if (Ago(0) == SwordTechnique.Cleave && Ago(1) == SwordTechnique.Lunge) Named("FLASH STEP", 1.4f);
            else if (n >= 2 && Ago(0) == SwordTechnique.Slash && Ago(1) == SwordTechnique.Slash && chain[n - 1].side != chain[n - 2].side) Named("CROSS CUT", 1.3f);
        }

        void Named(string name, float bonus)
        {
            chainName = name;
            chainBonus = bonus;
            OnCombo?.Invoke(name);
            CombatFx.Popup(name, head.position + head.forward * 1.3f + Vector3.up * 0.45f, new Color(1f, 0.85f, 0.35f), 1.2f);
            CombatFx.Rumble(hand, 0.6f, 0.12f);
            for (int i = 0; i < 20; i++)
                StoryFx.Emit(CombatFx.Sparks, Vector3.Lerp(Base, Tip, Random.value), Random.insideUnitSphere * 0.8f, 0.025f, 0.5f, new Color(1f, 0.85f, 0.35f));
            if (name == "HEAVEN SPLITTER" || name == "TRI-FANG") chain.Clear(); // (finishers end the chain)
        }

        // The Lunge carries the player in.
        IEnumerator Dash(Vector3 total, float seconds)
        {
            var legs = FindAnyObjectByType<PlayerLocomotion>();
            if (legs == null) yield break;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                legs.Carry(total * Time.deltaTime / seconds); // (the player chose it: carried in full, edge of the view narrowed)
                yield return null;
            }
        }

        // The cleave's wide arc: everything in front, within reach.
        void Cleave()
        {
            Vector3 centre = head.position - Vector3.up * 0.5f, fwd = StoryFx.Flat(head.forward);
            for (int k = 0; k < 30; k++)
            {
                float a = Mathf.Lerp(-cleaveAngle * 0.5f, cleaveAngle * 0.5f, k / 29f);
                Vector3 dir = Quaternion.AngleAxis(a, Vector3.up) * fwd;
                StoryFx.Emit(CombatFx.Sparks, centre + dir * 0.7f, dir * 3.5f, 0.03f, 0.3f, trailColor);
            }
            int n = Physics.OverlapSphereNonAlloc(centre, cleaveRadius, overlaps, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var target = overlaps[i].GetComponentInParent<IHittable>();
                if (target == null || hitThisSwing.Contains(target)) continue;
                Vector3 point = overlaps[i].ClosestPoint(centre);
                if (Vector3.Angle(fwd, StoryFx.Flat(point - centre)) > cleaveAngle * 0.5f) continue;
                hitThisSwing.Add(target);
                Hit(target, cleaveDamage, point, Vector3.Cross(Vector3.up, fwd) * -1f, 5f);
            }
        }

        /// <summary>The blade rebounds off a guard: knocked back the way it came, the swing over for a moment.</summary>
        public void Recoil(Vector3 direction)
        {
            recoil = StoryFx.Flat(direction) * 0.18f + Vector3.up * 0.05f;
            recoilUntil = Time.time + 0.35f;
            if (swinging) EndSwing();
            CombatFx.Rumble(hand, 1f, 0.12f);
        }

        /// <summary>A missile at `at`, flying with `velocity`: if the blade is in its path (swinging or guarding), it's
        /// batted back. True if deflected.</summary>
        public bool TryDeflect(Vector3 at, Vector3 velocity)
        {
            if (!Drawn || head == null || DrawingFromSheath()) return false;
            Vector3 step = velocity * Mathf.Max(Time.deltaTime, 0.02f) * 2f;
            float gap = SegmentDistance(Base, Tip, at - step, at + step);
            bool ready = swinging || guarding || Mathf.Abs(BladeDirection.y) < 0.75f;
            if (gap > 0.28f || !ready) return false;
            CombatFx.HitSparks(at, -velocity.normalized, 45, Color.white);
            CombatAudio.Play(CombatAudio.Clang, at, 1f, 0.1f);
            CombatFx.Rumble(hand, 1f, 0.15f);
            OnParry?.Invoke();
            return true;
        }

        // A heavy blow lands with a jolt: a split second of slow motion.
        // Only the world slows: the head and the blade in the hand keep real time (safe in a headset).
        IEnumerator HitStop(float seconds, float scale)
        {
            if (Time.timeScale < 0.99f) yield break; // already slowed (another hit-stop, or the arena's own slow motion)
            Time.timeScale = slowedTo = scale;
            yield return new WaitForSecondsRealtime(seconds);
            if (Mathf.Approximately(Time.timeScale, scale)) Time.timeScale = 1f; // (unless something else has set it since)
            slowedTo = 0f;
        }

        void OnDisable()
        {
            if (slowedTo > 0f && Mathf.Approximately(Time.timeScale, slowedTo)) Time.timeScale = 1f; // only undo our own
            slowedTo = 0f;
        }

        void EndSwing()
        {
            swinging = false;
            if (kind == SwordTechnique.Overhead && !shockwaveDone) Shockwave(Tip);
            if (trail != null) trail.emitting = false;
            scripted = SwordTechnique.None;
        }

        static string Name(SwordTechnique t) => t switch
        {
            SwordTechnique.Thrust => "THRUST",
            SwordTechnique.Overhead => "OVERHEAD SMASH",
            SwordTechnique.Whirlwind => "WHIRLWIND",
            SwordTechnique.JyotiWave => "JYOTI WAVE",
            SwordTechnique.Rising => "RISING SLASH",
            SwordTechnique.Cleave => "CLEAVE",
            SwordTechnique.Lunge => "LUNGE",
            _ => "SLASH",
        };

        // How far round the player the tip has swept in the last 0.6 s (degrees).
        void TrackSweep(Vector3 tip)
        {
            Vector3 d = tip - head.position;
            sweep.Enqueue((Time.time, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg));
            while (sweep.Count > 0 && sweep.Peek().time < Time.time - 0.6f) sweep.Dequeue();
        }

        float Swept()
        {
            float total = 0f, previous = float.NaN;
            int direction = 0;
            foreach (var (_, angle) in sweep)
            {
                if (!float.IsNaN(previous))
                {
                    float step = Mathf.DeltaAngle(previous, angle);
                    int s = step > 0f ? 1 : -1;
                    if (direction != 0 && s != direction) total = 0f; // turned back: a new sweep
                    direction = s;
                    total += Mathf.Abs(step);
                }
                previous = angle;
            }
            return total;
        }

        void UpdateCharge(float speed, float dt)
        {
            if (charged)
            {
                if (Time.time - chargedAt > 8f) SetCharged(false); // the light fades if it isn't used
                else if (Random.value < 0.6f)
                    StoryFx.Emit(CombatFx.Sparks, Vector3.Lerp(Base, Tip, Random.value), Random.insideUnitSphere * 0.1f + Vector3.up * 0.2f, 0.02f, 0.4f, chargedColor);
                return;
            }
            bool upright = BladeDirection.y > 0.8f && speed < 0.7f && !swinging;
            bool keyHeld = GameInput.Held(GameAction.SwordCharge) && (input == null || !input.Get(hand).IsTracked);
            stillTime = upright || keyHeld ? stillTime + dt : 0f;
            if (stillTime > 0.3f && Random.value < stillTime / chargeTime) // light gathering into the blade
            {
                Vector3 at = Vector3.Lerp(Base, Tip, Random.value);
                StoryFx.Emit(CombatFx.Sparks, at + Random.insideUnitSphere * 0.25f, Random.insideUnitSphere * 0.05f, 0.015f, 0.3f, chargedColor);
            }
            if (stillTime >= chargeTime) SetCharged(true);
        }

        void SetCharged(bool on)
        {
            charged = on;
            stillTime = 0f;
            if (on)
            {
                chargedAt = Time.time;
                CombatFx.HitSparks(Tip, Vector3.up, 50, chargedColor);
                CombatAudio.Play(CombatAudio.Wave, grip, 0.4f, 0f);
                CombatFx.Popup("CHARGED", Tip + Vector3.up * 0.15f, chargedColor, 0.7f);
                CombatFx.Rumble(hand, 0.4f, 0.2f);
            }
            if (chargeLight == null && sword != null)
            {
                var go = new GameObject("ChargeLight");
                go.transform.SetParent(sword, false);
                go.transform.localPosition = Vector3.up * 0.5f;
                chargeLight = go.AddComponent<Light>();
                chargeLight.type = LightType.Point;
                chargeLight.range = 2.5f;
                chargeLight.color = chargedColor;
                chargeLight.shadows = LightShadows.None;
            }
            if (chargeLight != null) chargeLight.intensity = on ? 2.5f : 0f;
        }

        // ---------------------------------------------------------------- hits

        void CheckHits(float speed)
        {
            Vector3 reach = kind == SwordTechnique.Thrust || kind == SwordTechnique.Lunge ? BladeDirection * thrustReach : Vector3.zero;
            HitAlong(Base, Tip + reach, speed);
            // A fast blade covers more than its own width in a frame: check halfway too, so it can't skip through a thin enemy.
            if ((Tip - prevTip).sqrMagnitude > 4f * bladeRadius * bladeRadius)
                HitAlong(Vector3.Lerp(prevBase, Base, 0.5f), Vector3.Lerp(prevTip, Tip, 0.5f) + reach, speed);
        }

        // How hard a tracked blade lands, from its speed at contact (keyboard swings always land in full).
        float Power(float speed) => scripted != SwordTechnique.None ? 1f
            : Mathf.Lerp(speedPower.x, speedPower.y, Mathf.InverseLerp(swingEndSpeed, 7f, speed));

        void HitAlong(Vector3 from, Vector3 to, float speed)
        {
            int n = Physics.OverlapCapsuleNonAlloc(from, to, bladeRadius, overlaps, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var target = overlaps[i].GetComponentInParent<IHittable>();
                if (target == null || hitThisSwing.Contains(target)) continue;
                hitThisSwing.Add(target);
                float damage = kind switch
                {
                    SwordTechnique.Thrust => thrustDamage,
                    SwordTechnique.Overhead => overheadDamage,
                    SwordTechnique.Whirlwind => whirlwindDamage,
                    SwordTechnique.JyotiWave => waveDamage,
                    SwordTechnique.Rising => risingDamage,
                    SwordTechnique.Cleave => cleaveDamage,
                    SwordTechnique.Lunge => lungeDamage,
                    _ => scripted != SwordTechnique.None ? Mathf.Lerp(slashDamage.x, slashDamage.y, Mathf.InverseLerp(swingSpeed, 7f, speed))
                         : Mathf.Lerp(slashDamage.x * 0.35f, slashDamage.y, Mathf.InverseLerp(swingEndSpeed, 7f, speed)), // a slow touch barely cuts
                };
                if (kind != SwordTechnique.Slash) damage *= Power(speed);
                bool landed = Hit(target, damage, overlaps[i].ClosestPoint(to), tipVelocity.normalized, speed);
                // A real full-speed cut bites: two or three frames of a slower world (never the head, never the blade).
                if (landed && scripted == SwordTechnique.None && Mathf.InverseLerp(swingEndSpeed, 7f, speed) > 0.8f) StartCoroutine(HitStop(0.035f, 0.35f));
            }
        }

        // True if it landed (not stopped by a guard).
        bool Hit(IHittable target, float damage, Vector3 point, Vector3 direction, float speed)
        {
            string label = null;
            SwordTechnique technique = kind;
            bool heavyHit = kind == SwordTechnique.Overhead || chainBonus >= 1.5f;
            if (target is KalakhWarrior k && k.Reeling && k.HealthFraction <= executeBelow &&
                (kind == SwordTechnique.Thrust || kind == SwordTechnique.Lunge))
            {
                // Execution: the blade driven home into a beaten enemy.
                damage = Mathf.Max(damage * riposteMultiplier, executeDamage);
                technique = SwordTechnique.Execution;
                label = "EXECUTION";
                heavyHit = true;
                CombatFx.HitSparks(point, direction, 120, new Color(1f, 0.85f, 0.4f));
                CombatAudio.Play(CombatAudio.Wave, point, 1f, 0f);
                StartCoroutine(HitStop(0.25f, 0.25f));
            }
            else if (target is IReeling r && r.Reeling)
            {
                damage *= riposteMultiplier;
                label = "RIPOSTE";
            }
            if (chainName != null)
            {
                damage *= chainBonus;
                label = label ?? chainName;
                if (chainName == "HEAVEN SPLITTER" && target is IReeling staggered) staggered.Stagger(1.2f);
            }
            if (kind == SwordTechnique.Rising && target is IReeling launched) launched.Stagger(0.8f);
            // How hard it landed (0 .. 1), for the sparks, the sound and the jolt.
            float impact = scripted != SwordTechnique.None ? 0.75f : Mathf.InverseLerp(swingEndSpeed, 7f, speed);
            if (heavyHit) impact = 1f;
            combo = Time.time - lastHitTime < comboWindow ? combo + 1 : 1;
            lastHitTime = Time.time;
            if (combo >= 3)
            {
                damage *= 1f + comboBonus * (combo - 2);
                label = label ?? $"COMBO x{combo}";
            }
            float recoiledAt = recoilUntil;
            target.TakeHit(new HitInfo { Damage = damage, Point = point, Direction = direction, Speed = speed, Hand = hand, Technique = technique });
            if (recoilUntil != recoiledAt)
            {
                combo = 0; // blocked: the guard made its own clang and sparks, and the blade rebounds
                OnHit?.Invoke(target, damage, kind);
                return false;
            }
            if (heavyHit && technique != SwordTechnique.Execution) StartCoroutine(HitStop(0.06f, 0.15f));
            CombatFx.HitSparks(point, direction, Mathf.RoundToInt(Mathf.Lerp(8f, kind == SwordTechnique.Slash ? 30f : 45f, impact)), trailColor);
            if (impact > 0.6f) StoryFx.Emit(CombatFx.Sparks, point, Vector3.zero, Mathf.Lerp(0.12f, 0.3f, impact), 0.07f, Color.white); // the flash of the bite
            CombatAudio.Play(CombatAudio.Hit, point, Mathf.Lerp(0.4f, 1f, impact));
            if (impact > 0.6f) CombatAudio.Play(CombatAudio.Clang, point, 0.25f * impact, 0.25f); // the edge rings on a real cut
            CombatFx.Rumble(hand, Mathf.Lerp(0.35f, 1f, impact), Mathf.Lerp(0.05f, 0.12f, impact));
            if (label != null) CombatFx.Popup(label, point + Vector3.up * 0.3f, new Color(1f, 0.55f, 0.3f), 0.8f);
            OnHit?.Invoke(target, damage, kind);
            return true;
        }

        // Every shadow within reach round the player.
        void Whirlwind(bool already = false)
        {
            if (!already)
            {
                SpendWhirlwind();
                kind = SwordTechnique.Whirlwind;
                CombatFx.Popup(Name(kind), head.position + head.forward * 1.2f + Vector3.up * 0.25f, trailColor, 0.9f);
                OnTechnique?.Invoke(kind);
            }
            Vector3 centre = head.position - Vector3.up * 0.6f;
            for (int k = 0; k < 40; k++)
            {
                float a = k * Mathf.PI * 2f / 40f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                StoryFx.Emit(CombatFx.Sparks, centre + dir * 0.6f, dir * 4f, 0.03f, 0.35f, trailColor);
            }
            AreaHit(centre, whirlwindRadius, whirlwindDamage, false);
        }

        // The overhead smash meets the ground: a ring of light and dust; everything nearby is hit and staggered.
        void Shockwave(Vector3 tip)
        {
            shockwaveDone = true;
            float floor = head.root.position.y;
            Vector3 centre = new Vector3(tip.x, floor + 0.05f, tip.z);
            for (int k = 0; k < 60; k++)
            {
                float a = k * Mathf.PI * 2f / 60f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                StoryFx.Emit(CombatFx.Sparks, centre, dir * 5f + Vector3.up * 0.4f, 0.04f, 0.45f, trailColor);
            }
            CombatFx.SmokePuff(centre, 0.5f, 15, 0.8f);
            CombatAudio.Play(CombatAudio.HeavySwoosh, centre, 1f, 0.3f);
            CombatFx.Rumble(null, 1f, 0.25f);
            AreaHit(centre, shockwaveRadius, shockwaveDamage, true);
        }

        void AreaHit(Vector3 centre, float radius, float damage, bool stagger)
        {
            int n = Physics.OverlapSphereNonAlloc(centre, radius, overlaps, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var target = overlaps[i].GetComponentInParent<IHittable>();
                if (target == null || hitThisSwing.Contains(target)) continue;
                hitThisSwing.Add(target);
                Vector3 point = overlaps[i].ClosestPoint(centre);
                Vector3 away = StoryFx.Flat(point - centre);
                Hit(target, damage, point, away, 5f);
                if (stagger && target is IReeling m) m.Stagger(1f);
            }
        }

        // A crescent of light flies from the blade along the swing, flat over the ground.
        void ReleaseWave()
        {
            SetCharged(false);
            Vector3 dir = StoryFx.Flat(scripted != SwordTechnique.None ? head.forward : tipVelocity.sqrMagnitude > 0.1f ? tipVelocity : head.forward);
            Vector3 start = head.position - Vector3.up * 0.4f + dir * 0.5f;
            var wave = new GameObject("JyotiWave").AddComponent<JyotiWave>();
            wave.Launch(start, dir, waveSpeed, waveRange, waveDamage, chargedColor, this);
            CombatAudio.Play(CombatAudio.Wave, start, 1f, 0.05f);
            CombatFx.Rumble(hand, 1f, 0.3f);
        }

        internal void WaveHit(IHittable target, Vector3 point, Vector3 direction, float damage) => Hit(target, damage, point, direction, waveSpeed);

        // ---------------------------------------------------------------- parrying

        /// <summary>A shadow's strike from `from` (its claws) at `target` (the player's chest): turned aside if the blade
        /// is across its path, or swinging through it. True if parried.</summary>
        public bool TryParry(Vector3 from, Vector3 target)
        {
            if (!Drawn || head == null || DrawingFromSheath()) return false;
            Vector3 toAttacker = StoryFx.Flat(from - head.position);
            bool facing = Vector3.Dot(StoryFx.Flat(head.forward), toAttacker) > 0.2f;
            bool parried = guarding && facing; // keyboard guard
            if (!parried)
            {
                float gap = SegmentDistance(Base, Tip, from, target);
                bool across = Mathf.Abs(BladeDirection.y) < 0.75f; // a blade held flat-ish blocks; one pointing at them doesn't
                parried = gap < 0.3f && (across || swinging) && facing;
            }
            if (!parried) return false;
            Vector3 at = Vector3.Lerp(Base, Tip, 0.6f);
            CombatFx.HitSparks(at, toAttacker, 50, Color.white);
            CombatAudio.Play(CombatAudio.Clang, at, 1f);
            CombatFx.Popup("PARRY!", at + Vector3.up * 0.25f, Color.white, 1f);
            CombatFx.Rumble(hand, 1f, 0.15f);
            OnParry?.Invoke();
            return true;
        }

        static float SegmentDistance(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
        {
            // Closest distance between two segments, sampled (accurate enough at blade scale).
            float best = float.MaxValue;
            for (int i = 0; i <= 8; i++)
            {
                Vector3 p = Vector3.Lerp(a0, a1, i / 8f);
                Vector3 ab = b1 - b0;
                float t = Mathf.Clamp01(Vector3.Dot(p - b0, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                best = Mathf.Min(best, Vector3.Distance(p, b0 + ab * t));
            }
            return best;
        }
    }

    /// <summary>The Jyoti Wave: a crescent of light flying low over the ground, cutting every shadow it passes once.</summary>
    public class JyotiWave : MonoBehaviour
    {
        LineRenderer arc;
        Vector3 direction;
        float speed, range, damage, travelled;
        WeaverSword sword;
        Color colour;
        readonly HashSet<IHittable> hit = new HashSet<IHittable>();
        readonly Collider[] overlaps = new Collider[16];

        public void Launch(Vector3 start, Vector3 dir, float speed, float range, float damage, Color color, WeaverSword from)
        {
            transform.SetPositionAndRotation(start, Quaternion.LookRotation(dir));
            direction = dir;
            this.speed = speed;
            this.range = range;
            this.damage = damage;
            colour = color;
            sword = from;
            arc = gameObject.AddComponent<LineRenderer>();
            arc.sharedMaterial = SparkHands.CreateGlowMaterial(SparkHands.CreateSoftLine(64));
            arc.useWorldSpace = false;
            arc.positionCount = 13;
            for (int i = 0; i < 13; i++)
            {
                float a = Mathf.Lerp(-70f, 70f, i / 12f) * Mathf.Deg2Rad;
                arc.SetPosition(i, new Vector3(Mathf.Sin(a) * 0.9f, Mathf.Sin(a) * 0.15f, Mathf.Cos(a) * 0.35f - 0.35f));
            }
            arc.widthCurve = new AnimationCurve(new Keyframe(0f, 0.02f), new Keyframe(0.5f, 0.16f), new Keyframe(1f, 0.02f));
            arc.startColor = arc.endColor = color;
            var light = gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = 4f;
            light.intensity = 3f;
            light.shadows = LightShadows.None;
        }

        void Update()
        {
            float step = speed * Time.deltaTime;
            transform.position += direction * step;
            travelled += step;
            transform.localScale = Vector3.one * (1f + travelled * 0.12f); // spreads as it flies
            if (Random.value < 0.8f)
                StoryFx.Emit(CombatFx.Sparks, transform.TransformPoint(new Vector3(Random.Range(-0.9f, 0.9f), 0f, -0.2f)),
                             -direction * 0.5f + Random.insideUnitSphere * 0.3f, 0.03f, 0.4f, colour);
            int n = Physics.OverlapBoxNonAlloc(transform.position, new Vector3(1f, 0.9f, 0.4f) * transform.localScale.x,
                                               overlaps, transform.rotation, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var target = overlaps[i].GetComponentInParent<IHittable>();
                if (target == null || hit.Contains(target)) continue;
                hit.Add(target);
                if (sword != null) sword.WaveHit(target, overlaps[i].ClosestPoint(transform.position), direction, damage);
                else target.TakeHit(new HitInfo { Damage = damage, Point = transform.position, Direction = direction, Speed = speed });
            }
            float fade = Mathf.Clamp01((range - travelled) / 2f);
            arc.startColor = arc.endColor = new Color(colour.r, colour.g, colour.b, colour.a * fade);
            if (travelled > range) Destroy(gameObject);
        }
    }
}
