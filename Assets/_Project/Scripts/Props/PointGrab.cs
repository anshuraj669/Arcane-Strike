using ArcaneStrike.HandInput;
using ArcaneStrike.Story;
using ArcaneStrike.VFX;
using UnityEngine;

namespace ArcaneStrike.Props
{
    /// <summary>
    /// Point to grab: point the index finger of this hand (the other fingers curled) at something and hold it
    /// there for a moment; a faint beam shows what you're aiming at, and then it flies into your hand. Works on
    /// every <see cref="Grabbable"/> and on the lantern once the chest is open. Open the hand to drop or throw it.
    /// (The right index finger walks, see PlayerLocomotion, so this is the left hand by default.)
    /// Keyboard (no tracked hand, <see cref="GameInput"/>): look at it and press T to pull it into your hand (it
    /// sparkles while you look at it); T again drops it, Y throws it where you look.
    /// </summary>
    public class PointGrab : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] HandSide hand = HandSide.Left;
        [Tooltip("How far you can grab from (m).")]
        [SerializeField] float maxDistance = 6f;
        [Tooltip("How closely you must point at it (degrees), a little more for small things far away.")]
        [SerializeField] float aimAngle = 8f;
        [Tooltip("Keep pointing at it this long (s) to pull it in.")]
        [SerializeField] float aimTime = 0.4f;
        [SerializeField] Color beamColor = new Color(0.55f, 0.85f, 1f, 0.35f);

        LineRenderer beam;
        ParticleSystem marks;
        Object target;
        float aimSince;
        bool waitForRelease; // after a grab, stop pointing before the next one
        LanternPickup[] lanterns;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            lanterns = FindObjectsByType<LanternPickup>(FindObjectsSortMode.None);

            var go = new GameObject($"PointGrabBeam ({hand})");
            go.transform.SetParent(transform, false);
            beam = go.AddComponent<LineRenderer>();
            beam.positionCount = 2;
            beam.useWorldSpace = true;
            beam.widthCurve = AnimationCurve.Linear(0f, 0.006f, 1f, 0.002f);
            beam.sharedMaterial = SparkHands.CreateGlowMaterial(Texture2D.whiteTexture);
            beam.startColor = beamColor;
            beam.endColor = StoryFx.WithAlpha(beamColor, 0f);
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.enabled = false;
            marks = StoryFx.CreateParticles(transform, $"PointGrabMarks ({hand})", true, 0f, 120);
        }

        void Update()
        {
            HandFrame f = input != null ? input.Get(hand) : null;
            if (f == null || !f.IsTracked)
            {
                UpdateKeyboard();
                return;
            }
            bool pointing = f != null && IsPointing(f);
            if (!pointing)
            {
                beam.enabled = false;
                target = null;
                waitForRelease = false;
                return;
            }

            Vector3 origin = f[HandJoint.IndexTip];
            Vector3 direction = (f[HandJoint.IndexTip] - f[HandJoint.IndexProximal]).normalized;
            bool handFree = !Grabbable.IsHolding(hand) && !HoldingLantern();
            Object best = null;
            Vector3 bestPoint = origin + direction * 2f;
            if (handFree && !waitForRelease) best = FindTarget(origin, direction, ref bestPoint);

            beam.enabled = true;
            beam.SetPosition(0, origin);
            beam.SetPosition(1, best != null ? bestPoint : origin + direction * 1.5f);

            if (best != target)
            {
                target = best;
                aimSince = Time.time;
            }
            if (target == null) return;

            if (Random.value < 0.6f) // sparkle on what you're aiming at
                StoryFx.Emit(marks, bestPoint + Random.insideUnitSphere * 0.05f, Random.insideUnitSphere * 0.05f,
                             Random.Range(0.02f, 0.04f), 0.4f, StoryFx.WithAlpha(beamColor, 0.9f));

            if (Time.time - aimSince < aimTime) return;
            if (target is Grabbable g) g.PullTo(hand);
            else if (target is LanternPickup l) l.PickUp(hand);
            target = null;
            waitForRelease = true;
            beam.enabled = false;
        }

        // Keyboard: aim with the view; T pulls in (or drops) and Y throws.
        void UpdateKeyboard()
        {
            beam.enabled = false;
            Transform head = StoryFx.Head(input);
            if (head == null) return;
            bool holding = Grabbable.IsHolding(hand);
            if (holding && GameInput.Pressed(GameAction.Throw))
            {
                Grabbable.KeyboardRelease(hand, head.forward * 6f + Vector3.up * 1.2f);
                return;
            }
            if (holding)
            {
                if (GameInput.Pressed(GameAction.PointGrab)) Grabbable.KeyboardRelease(hand, Vector3.zero);
                return;
            }
            if (HoldingLantern()) return;
            Vector3 point = head.position + head.forward * 2f;
            Object best = FindTarget(head.position, head.forward, ref point);
            if (best == null) return;
            if (Random.value < 0.4f) // sparkle on what you're looking at
                StoryFx.Emit(marks, point + Random.insideUnitSphere * 0.05f, Random.insideUnitSphere * 0.05f,
                             Random.Range(0.02f, 0.04f), 0.4f, StoryFx.WithAlpha(beamColor, 0.9f));
            if (!GameInput.Pressed(GameAction.PointGrab)) return;
            if (best is Grabbable g)
            {
                Grabbable.KeyboardGrip[(int)hand] = true;
                g.PullTo(hand);
            }
            else if (best is LanternPickup l) l.PickUp(hand);
        }

        readonly System.Collections.Generic.List<(Object thing, Vector3 at)> candidates = new System.Collections.Generic.List<(Object, Vector3)>();

        Object FindTarget(Vector3 origin, Vector3 direction, ref Vector3 point)
        {
            candidates.Clear();
            foreach (Grabbable g in Grabbable.Active)
                if (g != null && g.CanBePulled) candidates.Add((g, g.Center));
            foreach (LanternPickup l in lanterns)
                if (l != null && l.IsAvailable && !l.IsPickedUp) candidates.Add((l, l.transform.position + Vector3.up * 0.15f));

            Object best = null;
            float bestScore = float.MaxValue;
            foreach ((Object thing, Vector3 at) in candidates)
            {
                Vector3 to = at - origin;
                float distance = to.magnitude;
                if (distance < 0.05f || distance > maxDistance) continue;
                float allowed = aimAngle + Mathf.Rad2Deg * Mathf.Atan2(0.12f, distance); // a little slack for small things
                float angle = Vector3.Angle(direction, to);
                if (angle > allowed) continue;
                float score = angle / allowed + distance * 0.02f; // closest to the line, then nearest
                if (score >= bestScore) continue;
                bestScore = score;
                best = thing;
                point = at;
            }
            return best;
        }

        bool HoldingLantern()
        {
            foreach (LanternPickup l in lanterns)
                if (l != null && l.IsPickedUp && l.HeldBy == hand) return true;
            return false;
        }

        /// <summary>Index finger straight, the other three curled.</summary>
        public static bool IsPointing(HandFrame f) =>
            f.IsTracked && f.Straightness(Finger.Index) > 0.9f && f.Straightness(Finger.Middle) < 0.55f &&
            f.Straightness(Finger.Ring) < 0.55f && f.Straightness(Finger.Pinky) < 0.6f;
    }
}
