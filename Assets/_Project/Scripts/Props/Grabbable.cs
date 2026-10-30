using System;
using System.Collections.Generic;
using ArcaneStrike.HandInput;
using ArcaneStrike.Story;
using UnityEngine;

namespace ArcaneStrike.Props
{
    /// <summary>
    /// A loose object the player can pick up with their bare hands: close a fist or pinch near it, move it,
    /// open the hand to drop it, or let go mid-swing to throw it. <see cref="PullTo"/> (pointing at it, see
    /// <see cref="PointGrab"/>) flies it into a hand from a distance. It is a normal physics object otherwise,
    /// so it can be knocked over (see <see cref="HandPhysics"/>), and it knocks, clinks or thuds when it lands.
    /// Needs a Rigidbody and a collider.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Grabbable : MonoBehaviour
    {
        public enum Sound { Wood, Glass, Metal, Paper }

        [Tooltip("The hand's grip point must be this close to the object's surface (m).")]
        [SerializeField] float reach = 0.06f;
        [SerializeField] float throwBoost = 1.2f;
        [SerializeField] float maxThrowSpeed = 7f;
        [Tooltip("How quickly a held object follows the hand. Lower is smoother on a jittery webcam.")]
        [SerializeField] float followSharpness = 25f;

        [Header("Feel")]
        public Sound sound = Sound.Wood;
        [Tooltip("Old things are dusty: a puff of dust the first time it is picked up, and on hard landings.")]
        [SerializeField] bool dusty = true;

        public bool IsHeld => holder >= 0;
        public HandSide HeldBy => (HandSide)Mathf.Max(holder, 0);
        public event Action<Grabbable> OnGrabbed, OnReleased;

        Rigidbody body;
        FloatingObject floating;
        Collider[] colliders;
        int holder = -1;
        bool touched;
        Vector3 holdPos;
        Quaternion holdRot;
        float lastImpact;
        bool pointHeld;          // pulled in by pointing: held until the hand opens (it isn't a fist)
        int flyingTo = -1;       // the hand it is flying to
        float flyT;
        Vector3 flyFrom;
        Transform homeParent;    // where it was put, to bring it back if it is lost (falls through the floor)
        Vector3 homePos;
        Quaternion homeRot;
        bool hasHome;

        [Tooltip("Lost: this far (m) below where it started, it is put back (it fell through the floor or out of the set).")]
        [SerializeField] float lostBelow = 5f;

        static readonly List<Grabbable> All = new List<Grabbable>();

        public static IReadOnlyList<Grabbable> Active => All;
        /// <summary>This hand is holding (or pulling in) a Grabbable.</summary>
        public static bool IsHolding(HandSide side) => Grip.Held[(int)side] != null;

        /// <summary>Keyboard play (no tracked hand): the hand counts as closed while this is set, and holds things
        /// in front of the view. PointGrab sets it.</summary>
        public static readonly bool[] KeyboardGrip = new bool[2];

        /// <summary>Keyboard play: lets go of whatever this hand holds, moving at `velocity` (zero: just drop it).</summary>
        public static void KeyboardRelease(HandSide side, Vector3 velocity)
        {
            int hand = (int)side;
            KeyboardGrip[hand] = false;
            Grabbable g = Grip.Held[hand];
            if (g != null && g.IsHeld) g.Release(velocity / Mathf.Max(0.01f, g.throwBoost));
        }
        public bool CanBePulled => !IsHeld && flyingTo < 0 && !(floating != null && floating.IsFloating);
        public Vector3 Center => body != null ? body.worldCenterOfMass : transform.position;

        /// <summary>Flies the object into this hand (over about a third of a second), then holds it there until the hand opens.</summary>
        public void PullTo(HandSide side)
        {
            int hand = (int)side;
            if (!CanBePulled || Grip.Held[hand] != null) return;
            Grip.Held[hand] = this; // reserved while it flies
            flyingTo = hand;
            flyFrom = body.position;
            flyT = 0f;
            body.isKinematic = true;
            if (dusty && !touched) ImpactFx.Dust(body.worldCenterOfMass, 14);
            touched = true;
        }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            floating = GetComponent<FloatingObject>();
            colliders = GetComponentsInChildren<Collider>();
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        void Start()
        {
            homeParent = transform.parent;
            homePos = transform.localPosition;
            homeRot = transform.localRotation;
            hasHome = true;
        }

        Vector3 HomePosition => homeParent != null ? homeParent.TransformPoint(homePos) : homePos;

        // Fell through the floor (or off the set): back where it was found, with a puff of dust, instead of lost for good.
        void ReturnHome()
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            Quaternion rot = homeParent != null ? homeParent.rotation * homeRot : homeRot;
            body.position = HomePosition;
            body.rotation = rot;
            transform.SetPositionAndRotation(HomePosition, rot);
            if (dusty) ImpactFx.Dust(HomePosition, 8);
        }

        void OnEnable() => All.Add(this);

        void OnDisable()
        {
            All.Remove(this);
            if (flyingTo >= 0 && Grip.Held[flyingTo] == this) Grip.Held[flyingTo] = null;
            flyingTo = -1;
            if (IsHeld) Release(Vector3.zero);
        }

        void Update()
        {
            Grip.Tick();
            if (floating != null && floating.IsFloating)
            {
                // The story has taken over (the awakening lifts the books): let go and stay out of physics.
                if (IsHeld) Release(Vector3.zero);
                if (flyingTo >= 0 && Grip.Held[flyingTo] == this) Grip.Held[flyingTo] = null;
                flyingTo = -1;
                body.isKinematic = true;
                return;
            }
            if (flyingTo >= 0) return;
            // Grabbed with a fist or pinch: let go when the hand opens out of it. Pulled in by pointing: when it opens fully.
            if (IsHeld && (pointHeld ? Grip.Open[holder] : !Grip.Closed[holder])) Release(Grip.Velocity(holder));
        }

        void FixedUpdate()
        {
            if (flyingTo >= 0)
            {
                Grip.Pose(flyingTo, out Vector3 to, out _);
                flyT += Time.fixedDeltaTime / 0.35f;
                body.MovePosition(Vector3.Lerp(flyFrom, to, Mathf.SmoothStep(0f, 1f, flyT)));
                if (flyT >= 1f)
                {
                    int hand = flyingTo;
                    flyingTo = -1;
                    Grab(hand);
                    pointHeld = true;
                }
                return;
            }
            if (!IsHeld)
            {
                if (hasHome && !body.isKinematic && body.position.y < HomePosition.y - lostBelow) ReturnHome();
                return;
            }
            Grip.Pose(holder, out Vector3 gripPos, out Quaternion gripRot);
            // Weight: a heavy thing (a crate, a jar of coins) trails the hand a little more than a book does.
            float k = 1f - Mathf.Exp(-followSharpness / (1f + 0.25f * body.mass) * Time.fixedDeltaTime);
            body.MovePosition(Vector3.Lerp(body.position, gripPos + gripRot * holdPos, k));
            body.MoveRotation(Quaternion.Slerp(body.rotation, gripRot * holdRot, k));
        }

        float DistanceTo(Vector3 point)
        {
            float best = float.MaxValue;
            foreach (Collider c in colliders)
                if (c != null && c.enabled)
                    best = Mathf.Min(best, Vector3.Distance(point, c.ClosestPoint(point)));
            return best;
        }

        void Grab(int hand)
        {
            holder = hand;
            pointHeld = false;
            Grip.Held[hand] = this;
            Grip.Pose(hand, out Vector3 gripPos, out Quaternion gripRot);
            Quaternion inverse = Quaternion.Inverse(gripRot);
            holdPos = inverse * (body.position - gripPos);
            holdRot = inverse * body.rotation;
            body.isKinematic = true;
            if (dusty && !touched) ImpactFx.Dust(body.worldCenterOfMass, 14);
            touched = true;
            Combat.CombatFx.Rumble((HandSide)hand, 0.2f + 0.1f * Mathf.Min(body.mass, 3f), 0.05f); // controllers only
            OnGrabbed?.Invoke(this);
        }

        void Release(Vector3 handVelocity)
        {
            if (holder >= 0 && Grip.Held[holder] == this) Grip.Held[holder] = null;
            holder = -1;
            body.isKinematic = floating != null && floating.IsFloating;
            if (!body.isKinematic)
            {
                // Heavier things leave the hand slower: the same swing throws a book further than a crate.
                float heft = 1f / Mathf.Max(1f, Mathf.Sqrt(body.mass));
                body.linearVelocity = Vector3.ClampMagnitude(handVelocity * throwBoost * heft, maxThrowSpeed);
                body.angularVelocity = UnityEngine.Random.insideUnitSphere * handVelocity.magnitude * 2f;
            }
            OnReleased?.Invoke(this);
        }

        void OnCollisionEnter(Collision collision)
        {
            float speed = collision.relativeVelocity.magnitude;
            if (speed < 0.6f || Time.time - lastImpact < 0.12f) return;
            lastImpact = Time.time;
            Vector3 at = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            ImpactFx.Play(sound, at, Mathf.Clamp01(speed / 4f), body.mass);
            if (dusty && speed > 1.5f) ImpactFx.Dust(at, 6);
        }

        /// <summary>
        /// Both hands, worked out once per frame for every Grabbable: closed (fist or pinch, with hysteresis),
        /// the grip point and rotation, recent velocity, and which object (if any) each hand holds.
        /// </summary>
        static class Grip
        {
            public static readonly bool[] Closed = new bool[2];
            public static readonly bool[] Open = new bool[2];
            public static readonly Grabbable[] Held = new Grabbable[2];

            static HandInputService input;
            static LanternPickup lantern;
            static int frame = -1;
            static float nextSearch;
            static readonly float[] lostAt = new float[2];
            // Hand tracking drops out for a moment now and then (a hand behind the other, out of view):
            // hold on through short gaps instead of dropping what the hand carries.
            const float TrackingGrace = 0.3f;
            static readonly Vector3[] position = new Vector3[2];
            static readonly Quaternion[] rotation = { Quaternion.identity, Quaternion.identity };
            static readonly Queue<(float t, Vector3 p)>[] history = { new Queue<(float, Vector3)>(), new Queue<(float, Vector3)>() };

            public static void Pose(int hand, out Vector3 pos, out Quaternion rot)
            {
                pos = position[hand];
                rot = rotation[hand];
            }

            public static Vector3 Velocity(int hand)
            {
                var h = history[hand];
                if (h.Count < 2) return Vector3.zero;
                (float t0, Vector3 p0) = h.Peek();
                float dt = Time.time - t0;
                return dt > 1e-3f ? (position[hand] - p0) / dt : Vector3.zero;
            }

            public static void Tick()
            {
                if (frame == Time.frameCount) return;
                frame = Time.frameCount;
                // Searched at most twice a second: scenes without a lantern (or hands) mustn't scan every frame.
                if ((input == null || lantern == null) && (Time.unscaledTime >= nextSearch || nextSearch > Time.unscaledTime + 1f)) // (or a new Play session)
                {
                    nextSearch = Time.unscaledTime + 0.5f;
                    if (input == null) input = FindAnyObjectByType<HandInputService>();
                    if (lantern == null) lantern = FindAnyObjectByType<LanternPickup>();
                }
                if (input == null) return;

                for (int i = 0; i < 2; i++)
                {
                    HandFrame f = input.Get((HandSide)i);
                    bool was = Closed[i];
                    if (!f.IsTracked && KeyboardGrip[i])
                    {
                        // A hand held out in front of the view (left or right), closed while the key grip lasts.
                        Transform head = input.Head != null ? input.Head : Camera.main != null ? Camera.main.transform : null;
                        if (head != null)
                        {
                            float side = i == (int)HandSide.Right ? 1f : -1f;
                            position[i] = head.position + head.forward * 0.45f + head.right * 0.18f * side - head.up * 0.18f;
                            rotation[i] = Quaternion.LookRotation(head.forward, -head.right * side);
                        }
                        Closed[i] = true;
                        Open[i] = false;
                        history[i].Enqueue((Time.time, position[i]));
                        while (history[i].Count > 2 && Time.time - history[i].Peek().t > 0.1f) history[i].Dequeue();
                        continue;
                    }
                    if (!f.IsTracked)
                    {
                        if (was && Held[i] != null && Time.time - lostAt[i] < TrackingGrace) continue; // keep the last pose
                        Closed[i] = false;
                        Open[i] = false;
                        history[i].Clear();
                        continue;
                    }

                    lostAt[i] = Time.time;
                    float pinch = Vector3.Distance(f[HandJoint.ThumbTip], f[HandJoint.IndexTip]);
                    bool fist = StoryFx.IsFist(f);
                    Closed[i] = was ? fist || pinch < 0.045f : fist || pinch < 0.025f;
                    Open[i] = StoryFx.IsOpen(f);
                    position[i] = pinch < 0.045f && !fist
                        ? (f[HandJoint.ThumbTip] + f[HandJoint.IndexTip]) * 0.5f
                        : f.PalmCenter + f.PalmNormal * 0.035f;
                    rotation[i] = Quaternion.LookRotation(f.HandForward, f.PalmNormal);

                    var h = history[i];
                    h.Enqueue((Time.time, position[i]));
                    while (h.Count > 2 && Time.time - h.Peek().t > 0.1f) h.Dequeue();

                    // Just closed: take the nearest object in reach, unless this hand holds the lantern.
                    bool holdingLantern = lantern != null && lantern.IsPickedUp && (int)lantern.HeldBy == i;
                    if (Closed[i] && !was && Held[i] == null && !holdingLantern) TryGrab(i);
                }
            }

            static void TryGrab(int hand)
            {
                Grabbable best = null;
                float bestDistance = float.MaxValue;
                foreach (Grabbable g in All)
                {
                    if (!g.CanBePulled) continue;
                    float d = g.DistanceTo(position[hand]);
                    if (d < g.reach && d < bestDistance)
                    {
                        best = g;
                        bestDistance = d;
                    }
                }
                if (best != null) best.Grab(hand);
            }
        }
    }

    /// <summary>Landing sounds and dust for physical props: placeholder sounds made from code.</summary>
    public static class ImpactFx
    {
        const int Rate = 44100;
        static readonly Dictionary<Grabbable.Sound, AudioClip> Clips = new Dictionary<Grabbable.Sound, AudioClip>();
        static ParticleSystem dust;

        public static void Play(Grabbable.Sound sound, Vector3 at, float strength, float mass)
        {
            if (!Clips.TryGetValue(sound, out AudioClip clip)) Clips[sound] = clip = Make(sound);
            var go = new GameObject($"Impact {sound}");
            go.transform.position = at;
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.spatialBlend = 1f;
            source.minDistance = 0.3f;
            source.maxDistance = 8f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.volume = Mathf.Lerp(0.15f, 1f, strength);
            source.pitch = Mathf.Clamp(1.15f - mass * 0.15f, 0.7f, 1.3f) * UnityEngine.Random.Range(0.92f, 1.08f);
            source.Play();
            UnityEngine.Object.Destroy(go, clip.length / source.pitch + 0.1f);
        }

        public static void Dust(Vector3 at, int count)
        {
            if (dust == null) dust = StoryFx.CreateParticles(null, "PropDust", false, -0.01f, 400);
            for (int i = 0; i < count; i++)
                StoryFx.Emit(dust, at + UnityEngine.Random.insideUnitSphere * 0.05f,
                             UnityEngine.Random.insideUnitSphere * 0.12f + Vector3.up * 0.05f,
                             UnityEngine.Random.Range(0.03f, 0.07f), UnityEngine.Random.Range(1.2f, 2.5f),
                             new Color(0.75f, 0.68f, 0.56f, 0.35f));
        }

        // A struck object: a short noise burst through a resonance, plus ringing partials for glass and metal.
        static AudioClip Make(Grabbable.Sound sound)
        {
            var rng = new System.Random((int)sound + 11);
            float length = sound == Grabbable.Sound.Glass ? 0.5f : sound == Grabbable.Sound.Metal ? 0.6f : 0.25f;
            var data = new float[(int)(Rate * length)];
            float resonance = sound switch { Grabbable.Sound.Wood => 420f, Grabbable.Sound.Paper => 260f, _ => 900f };
            float[] partials = sound switch
            {
                Grabbable.Sound.Glass => new[] { 2100f, 3350f, 5200f },
                Grabbable.Sound.Metal => new[] { 1250f, 2470f, 3900f },
                _ => new float[0],
            };
            float y1 = 0f, y2 = 0f;
            float w = 2f * Mathf.PI * resonance / Rate, r = 0.985f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / Rate;
                float noise = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * (sound == Grabbable.Sound.Paper ? 60f : 90f));
                float y = noise + 2f * r * Mathf.Cos(w) * y1 - r * r * y2; // two-pole resonator
                y2 = y1;
                y1 = y;
                float s = y * 0.08f;
                for (int p = 0; p < partials.Length; p++)
                    s += Mathf.Sin(2f * Mathf.PI * partials[p] * t) * Mathf.Exp(-t * (6f + p * 4f)) * 0.25f / (p + 1);
                data[i] = Mathf.Clamp(s, -1f, 1f) * Mathf.Min(1f, t * 2000f);
            }
            AudioClip clip = AudioClip.Create($"Impact {sound} (placeholder)", data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
