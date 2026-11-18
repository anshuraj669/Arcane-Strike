using ArcaneStrike.Story;
using UnityEngine;

namespace ArcaneStrike.HandInput
{
    /// <summary>
    /// Point to go there: point the LEFT index finger (the other fingers curled) at something (a box, the chest, a
    /// door, a spot on the floor) and hold it on it for a moment. A beam shows what you're pointing at and a ring
    /// fills up; then the player walks there by themselves, a little faster than normal walking, and stops one
    /// arm's length from it (on the spot itself for the floor). Moving yourself (stick, WASD) or pointing at
    /// something else takes over.
    /// Keyboard: look at it and press M (<see cref="GameAction.WalkToTarget"/>); M again while walking stops.
    /// Added to the player by <see cref="PlayerLocomotion"/>; it does the walking (<see cref="PlayerLocomotion.WalkTo"/>).
    /// </summary>
    public class PointToWalk : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] HandSide hand = HandSide.Left;
        [Tooltip("How far away you can point at something (m).")]
        [SerializeField] float maxDistance = 10f;
        [Tooltip("Keep pointing at it this long (s) before setting off: reaching for things doesn't send you walking.")]
        [SerializeField] float aimTime = 0.6f;
        [Tooltip("Where you stop: this far (m) from what you pointed at, about one arm's length.")]
        [SerializeField] float armsLength = 0.55f;
        [SerializeField] Color beamColor = new Color(1f, 0.78f, 0.35f, 0.45f);

        PlayerLocomotion locomotion;
        LineRenderer beam;
        Transform ring;
        Material ringMaterial;
        Collider aimed;
        Vector3 aimPoint;
        float aimSince = -1f;
        bool waitForRelease;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            locomotion = GetComponent<PlayerLocomotion>();
            if (locomotion == null) locomotion = FindAnyObjectByType<PlayerLocomotion>();

            var go = new GameObject("PointToWalkBeam");
            go.transform.SetParent(transform, false);
            beam = go.AddComponent<LineRenderer>();
            beam.positionCount = 2;
            beam.useWorldSpace = true;
            beam.widthCurve = AnimationCurve.Linear(0f, 0.005f, 1f, 0.002f);
            beam.sharedMaterial = VFX.SparkHands.CreateGlowMaterial(Texture2D.whiteTexture);
            beam.startColor = beamColor;
            beam.endColor = StoryFx.WithAlpha(beamColor, 0.15f);
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.enabled = false;

            // The target ring: a flat disc where you point, growing as the aim holds.
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "PointToWalkRing";
            Destroy(disc.GetComponent<Collider>());
            disc.transform.SetParent(transform, false);
            ringMaterial = VFX.SparkHands.CreateGlowMaterial(Texture2D.whiteTexture);
            ringMaterial.color = beamColor;
            var r = disc.GetComponent<Renderer>();
            r.sharedMaterial = ringMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ring = disc.transform;
            ring.gameObject.SetActive(false);
        }

        void Update()
        {
            if (locomotion == null || input == null) return;
            Transform head = input.Head != null ? input.Head : Camera.main != null ? Camera.main.transform : null;
            if (head == null) return;

            if (GameInput.Pressed(GameAction.WalkToTarget)) Keyboard(head);

            HandFrame f = input.Get(hand);
            if (locomotion.Paused || f == null || !f.IsTracked || !Props.PointGrab.IsPointing(f))
            {
                Clear();
                waitForRelease = false;
                return;
            }
            if (waitForRelease) return; // set off already: stop pointing (or point again) for the next one

            Vector3 origin = f[HandJoint.IndexTip];
            Vector3 direction = (f[HandJoint.IndexTip] - f[HandJoint.IndexProximal]).normalized;
            if (!Aim(origin, direction, head, out Collider hit, out Vector3 point, out float stop))
            {
                Clear();
                beam.enabled = true;
                beam.SetPosition(0, origin);
                beam.SetPosition(1, origin + direction * 1.2f);
                return;
            }
            if (hit != aimed)
            {
                aimed = hit;
                aimSince = Time.time;
            }
            aimPoint = Vector3.Lerp(aimPoint == Vector3.zero ? point : aimPoint, point, 0.3f);
            float held = Mathf.Clamp01((Time.time - aimSince) / aimTime);
            beam.enabled = true;
            beam.SetPosition(0, origin);
            beam.SetPosition(1, aimPoint);
            ShowRing(aimPoint, held);
            if (held < 1f) return;

            Go(aimPoint, stop, hit.name);
            waitForRelease = true;
            Clear();
        }

        // M: go to what's in the middle of the view (M again stops).
        void Keyboard(Transform head)
        {
            if (locomotion.WalkingTo)
            {
                locomotion.CancelWalkTo();
                return;
            }
            if (Aim(head.position, head.forward, head, out Collider hit, out Vector3 point, out float stop))
            {
                Go(point, stop, hit.name);
                ShowRing(point, 1f);
                Invoke(nameof(HideRing), 0.6f);
            }
            else Debug.Log($"[Move] Nothing to walk to there (look at something within {maxDistance:F0} m that's further than arm's length).");
        }

        void Go(Vector3 point, float stop, string what)
        {
            locomotion.WalkTo(point, stop);
            Debug.Log($"[Move] Walking to '{what}' (stopping {stop:F2} m from it).");
        }

        // What's along the line: anything solid in the world (not the player's own hands or what they hold). The floor
        // means "walk to this spot"; anything else, "walk up to it". Nothing when it's already within reach.
        bool Aim(Vector3 origin, Vector3 direction, Transform head, out Collider hit, out Vector3 point, out float stop)
        {
            hit = null;
            point = default;
            stop = armsLength;
            Transform rig = head.root;
            var hits = Physics.SphereCastAll(origin, 0.04f, direction, maxDistance, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            RaycastHit nearest = default;
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null || h.distance <= 0f || h.collider.transform.IsChildOf(rig)) continue;
                var held = h.collider.GetComponentInParent<Props.Grabbable>();
                if (held != null && held.IsHeld) continue; // (what's in your own hand)
                if (h.distance < best)
                {
                    best = h.distance;
                    nearest = h;
                }
            }
            if (best == float.MaxValue) return false;
            hit = nearest.collider;
            point = nearest.point;
            Vector3 eyes = head.position;
            bool floor = nearest.normal.y > 0.7f && point.y < eyes.y - 1.0f;
            if (floor) stop = 0.1f;
            Vector3 flat = point - eyes;
            flat.y = 0f;
            return flat.magnitude > stop + 0.15f; // already there: nothing to do
        }

        void ShowRing(Vector3 at, float filled)
        {
            ring.gameObject.SetActive(true);
            ring.position = at + Vector3.up * 0.01f;
            ring.rotation = Quaternion.identity;
            float size = Mathf.Lerp(0.05f, 0.22f, filled);
            ring.localScale = new Vector3(size, 0.002f, size);
            ringMaterial.color = StoryFx.WithAlpha(beamColor, Mathf.Lerp(0.25f, 0.9f, filled));
        }

        void HideRing() => ring.gameObject.SetActive(false);

        void Clear()
        {
            beam.enabled = false;
            HideRing();
            aimed = null;
            aimPoint = Vector3.zero;
        }

        void OnDestroy()
        {
            if (ringMaterial != null) Destroy(ringMaterial);
        }
    }
}
