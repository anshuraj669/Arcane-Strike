using UnityEngine;

namespace ArcaneStrike.HandInput
{
    /// <summary>
    /// Hands acted out for keyboard play: when the player presses a key for something done by hand (E to open a box,
    /// say), these hands come up into view and do it (reach for the lid, grip its edge, lift), so it looks the same
    /// as with real hands. They stand in only while no real hands are tracked (<see cref="HandInputService"/> picks
    /// them then); everything that draws or reads hands sees them like any other hands.
    /// Drive them with <see cref="Hold"/> every frame (the hand eases there) and <see cref="Release"/> when done
    /// (it drops out of view and goes).
    /// </summary>
    [DefaultExecutionOrder(-120)] // before HandInputService reads its sources
    public class ActedHands : MonoBehaviour, IHandSource
    {
        static ActedHands instance;

        class Hand
        {
            public bool active, wanted;
            public Vector3 palm, velocity, target;
            public Quaternion rotation = Quaternion.identity, targetRotation = Quaternion.identity;
            public float curl, targetCurl, leaving;
        }

        readonly Hand[] hands = { new Hand(), new Hand() };
        Transform head;

        /// <summary>Seconds the hand takes to get most of the way to where it's sent.</summary>
        public static float Smooth = 0.16f;

        public static ActedHands Get()
        {
            if (instance != null) return instance;
            instance = new GameObject("ActedHands").AddComponent<ActedHands>();
            return instance;
        }

        /// <summary>Puts the hand's palm at `palm`, turned to `rotation` (forward = along the fingers, up = the back of
        /// the hand), fingers curled `curl` (0 open .. 1 a fist). Call it every frame while acting; the hand eases there.</summary>
        public static void Hold(HandSide side, Vector3 palm, Quaternion rotation, float curl)
        {
            Hand h = Get().hands[(int)side];
            if (!h.active)
            {
                // It comes up from below the view.
                h.active = true;
                h.palm = instance.Rest(side, out h.rotation);
                h.velocity = Vector3.zero;
                h.curl = 0.2f;
            }
            h.wanted = true;
            h.leaving = 0f;
            h.target = palm;
            h.targetRotation = rotation;
            h.targetCurl = curl;
        }

        /// <summary>Done with this hand: it drops out of view, then isn't there any more.</summary>
        public static void Release(HandSide side)
        {
            if (instance == null) return;
            instance.hands[(int)side].wanted = false;
        }

        public static void ReleaseBoth()
        {
            Release(HandSide.Left);
            Release(HandSide.Right);
        }

        /// <summary>Either hand is being acted out.</summary>
        public static bool Acting => instance != null && (instance.hands[0].active || instance.hands[1].active);

        public bool IsActive => hands[0].active || hands[1].active;

        void OnEnable() => HandInputService.ActedSource = this;

        void OnDisable()
        {
            if (ReferenceEquals(HandInputService.ActedSource, this)) HandInputService.ActedSource = null;
            if (instance == this) instance = null;
        }

        void Update()
        {
            if (head == null) head = Camera.main != null ? Camera.main.transform : null;
            float dt = Time.deltaTime;
            for (int i = 0; i < 2; i++)
            {
                Hand h = hands[i];
                if (!h.active) continue;
                if (!h.wanted)
                {
                    // Down out of view, then gone.
                    h.target = Rest((HandSide)i, out h.targetRotation);
                    h.targetCurl = 0.3f;
                    h.leaving += dt;
                    if (h.leaving > 0.6f)
                    {
                        h.active = false;
                        continue;
                    }
                }
                h.palm = Vector3.SmoothDamp(h.palm, h.target, ref h.velocity, Smooth, 4f, dt);
                float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.05f, Smooth * 0.8f));
                h.rotation = Quaternion.Slerp(h.rotation, h.targetRotation, k);
                h.curl = Mathf.Lerp(h.curl, h.targetCurl, k);
            }
        }

        // Low and a little ahead, out of the bottom of the view.
        Vector3 Rest(HandSide side, out Quaternion rotation)
        {
            Transform v = head != null ? head : (Camera.main != null ? Camera.main.transform : transform);
            Vector3 flat = Vector3.ProjectOnPlane(v.forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, flat);
            rotation = Quaternion.LookRotation(Vector3.Lerp(flat, Vector3.up, 0.3f).normalized, Vector3.up);
            return v.position + flat * 0.25f + Vector3.down * 0.75f + right * (side == HandSide.Right ? 0.2f : -0.2f);
        }

        public bool TryGetHand(HandSide side, HandFrame frame)
        {
            Hand h = hands[(int)side];
            if (!h.active) return false;
            Pose(side, h.palm, h.rotation, h.curl, frame.Joints);
            frame.Confidence = 1f;
            frame.Time = Time.time;
            return true;
        }

        // ---------------------------------------------------------------- the hand's shape

        // Knuckles across the back of the hand (x: towards the thumb is -x on the right hand), and the finger bones.
        static readonly float[] KnuckleX = { 0.024f, 0.003f, -0.017f, -0.035f };
        static readonly float[] KnuckleZ = { 0.088f, 0.09f, 0.085f, 0.076f };
        static readonly float[][] Bones =
        {
            new[] { 0.040f, 0.025f, 0.021f }, // index
            new[] { 0.045f, 0.028f, 0.022f }, // middle
            new[] { 0.042f, 0.026f, 0.021f }, // ring
            new[] { 0.033f, 0.020f, 0.019f }, // pinky
        };
        static readonly float[] Bend = { 65f, 85f, 55f }; // degrees each joint bends at curl 1

        /// <summary>The 21 joints of a hand whose palm centre is at `palm`, turned to `rotation`, curled `curl`.</summary>
        public static void Pose(HandSide side, Vector3 palm, Quaternion rotation, float curl, Vector3[] joints)
        {
            float thumbSide = side == HandSide.Right ? -1f : 1f; // the thumb is on this side of x (palm down)
            Vector3 wrist = palm - rotation * new Vector3(0f, 0f, 0.062f);
            joints[0] = wrist;
            Vector3 W(Vector3 local) => wrist + rotation * local;

            // The four fingers: from the knuckle, each bone bent further down towards the palm.
            for (int f = 0; f < 4; f++)
            {
                var p = new Vector3(KnuckleX[f] * thumbSide, 0f, KnuckleZ[f]); // (the index on the thumb side)
                int b = 5 + f * 4;
                joints[b] = W(p);
                float pitch = 0f;
                Vector3 dir;
                for (int s = 0; s < 3; s++)
                {
                    pitch += Bend[s] * curl * (f == 3 ? 1.08f : 1f);
                    dir = Quaternion.Euler(pitch, 0f, 0f) * Vector3.forward; // bends down (towards -y, the palm)
                    p += dir * Bones[f][s];
                    joints[b + 1 + s] = W(p);
                }
            }

            // The thumb: out from the base of the palm, round towards the index finger as the hand closes.
            float t = curl;
            Vector3 c0 = new Vector3(thumbSide * 0.022f, -0.012f, 0.022f);
            Vector3 c1 = new Vector3(thumbSide * 0.042f, -0.02f, 0.045f);
            Vector3 c2 = Vector3.Lerp(new Vector3(thumbSide * 0.055f, -0.022f, 0.072f), new Vector3(thumbSide * 0.035f, -0.04f, 0.075f), t);
            Vector3 c3 = Vector3.Lerp(new Vector3(thumbSide * 0.062f, -0.024f, 0.098f), new Vector3(thumbSide * 0.018f, -0.05f, 0.09f), t);
            joints[1] = W(c0);
            joints[2] = W(c1);
            joints[3] = W(c2);
            joints[4] = W(c3);
        }
    }
}
