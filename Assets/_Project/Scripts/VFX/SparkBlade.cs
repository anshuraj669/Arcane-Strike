using System.Collections.Generic;
using ArcaneStrike.Combat;
using ArcaneStrike.HandInput;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArcaneStrike.VFX
{
    /// <summary>
    /// Spark Blade: a glowing sword that appears in the fist that drew it (BladeStart), follows the fist
    /// while it stays closed, and vanishes on BladeEnd. Swinging it hits any <see cref="IHittable"/>,
    /// with damage from the blade tip's speed. Built at runtime, so it needs no assets.
    /// <see cref="SparkHands"/> adds it automatically.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public class SparkBlade : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] GestureRecognizer recognizer;

        [Header("Look")]
        [SerializeField] float length = 0.9f;
        [SerializeField] float width = 0.05f;
        [Tooltip("Where the blade starts, measured from the palm along the blade (m): just above the fist.")]
        [SerializeField] float gripOffset = 0.06f;
        [SerializeField] float guardWidth = 0.18f;
        [SerializeField] Color bladeColor = new Color(1f, 0.79f, 0.29f, 0.4f); // Spark gold #FFC94A
        [SerializeField] Color coreColor = new Color(1f, 0.92f, 0.7f, 1f);
        [Tooltip("Seconds to grow in and to vanish.")]
        [SerializeField] float popTime = 0.12f;
        [Tooltip("How quickly the blade follows the fist. Lower is smoother on a jittery webcam, higher is snappier.")]
        [SerializeField] float followSharpness = 18f;

        [Header("Damage")]
        [Tooltip("Blade tip speed (m/s) needed for a touch to count as a hit.")]
        [SerializeField] float minHitSpeed = 1.5f;
        [SerializeField] float damagePerMetrePerSecond = 12f;
        [SerializeField] float maxDamage = 60f;
        [Tooltip("The same target can't be hit again by this blade within this time.")]
        [SerializeField] float sameTargetCooldown = 0.4f;

        class Blade
        {
            public Transform Root;
            public bool Up;
            public float Grow;
            public Vector3 Position, LastTip, TipVelocity;
            public Quaternion Rotation = Quaternion.identity;
            public readonly Dictionary<IHittable, float> LastHit = new Dictionary<IHittable, float>();
        }

        readonly Blade[] blades = { new Blade(), new Blade() };
        readonly Collider[] overlaps = new Collider[16];

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            if (recognizer == null) recognizer = FindAnyObjectByType<GestureRecognizer>();
        }

        void OnEnable()
        {
            if (recognizer != null) recognizer.OnGesture += HandleGesture;
        }

        void OnDisable()
        {
            if (recognizer != null) recognizer.OnGesture -= HandleGesture;
        }

        void Start()
        {
            Material face = AegisShield.CreateFaceMaterial(bladeColor);
            Material glow = SparkHands.CreateGlowMaterial(SparkHands.CreateSoftLine(64));
            for (int h = 0; h < 2; h++)
            {
                blades[h].Root = BuildBlade(h == 0 ? "SparkBlade_Left" : "SparkBlade_Right", face, glow);
                blades[h].Root.gameObject.SetActive(false);
            }
        }

        void HandleGesture(GestureEvent e)
        {
            Blade b = blades[(int)e.Hand];
            if (e.Type == GestureType.BladeStart)
            {
                b.Up = true;
                b.Position = e.Position;
                b.LastHit.Clear();
                Transform head = Head();
                if (head != null) b.Rotation = Quaternion.LookRotation((head.up + head.forward).normalized, -head.forward);
                b.LastTip = Tip(b);
                b.TipVelocity = Vector3.zero;
            }
            else if (e.Type == GestureType.BladeEnd) b.Up = false;
        }

        Transform Head() => input != null && input.Head != null ? input.Head : Camera.main != null ? Camera.main.transform : null;

        Vector3 Tip(Blade b) => b.Position + b.Rotation * Vector3.forward * (gripOffset + length);

        void LateUpdate()
        {
            Transform head = Head();
            if (head == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);

            for (int h = 0; h < 2; h++)
            {
                Blade b = blades[h];
                if (b.Root == null) continue;
                b.Grow = Mathf.MoveTowards(b.Grow, b.Up ? 1f : 0f, dt / Mathf.Max(popTime, 0.01f));
                bool visible = b.Grow > 0f;
                if (b.Root.gameObject.activeSelf != visible) b.Root.gameObject.SetActive(visible);
                if (!visible) continue;

                // The blade leaves the fist on the thumb side: from the pinky knuckle towards the index knuckle.
                HandFrame f = input != null ? input.Get((HandSide)h) : null;
                if (f != null && f.IsTracked)
                {
                    Vector3 dir = f[HandJoint.IndexProximal] - f[HandJoint.PinkyProximal];
                    if (dir.sqrMagnitude > 1e-6f)
                    {
                        float k = 1f - Mathf.Exp(-followSharpness * dt);
                        b.Position = Vector3.Lerp(b.Position, f.PalmCenter, k);
                        b.Rotation = Quaternion.Slerp(b.Rotation, Quaternion.LookRotation(dir.normalized, f.PalmNormal), k);
                    }
                }

                b.Root.SetPositionAndRotation(b.Position, b.Rotation);
                float t = 1f - (1f - b.Grow) * (1f - b.Grow); // ease out
                b.Root.localScale = new Vector3(1f, 1f, t);

                Vector3 tip = Tip(b);
                b.TipVelocity = Vector3.Lerp(b.TipVelocity, (tip - b.LastTip) / dt, 0.5f);
                b.LastTip = tip;
                if (b.Grow >= 1f) CheckHits(b, (HandSide)h, tip);
            }
        }

        void CheckHits(Blade b, HandSide side, Vector3 tip)
        {
            float speed = b.TipVelocity.magnitude;
            if (speed < minHitSpeed) return;

            Vector3 start = b.Position + b.Rotation * Vector3.forward * gripOffset;
            int count = Physics.OverlapCapsuleNonAlloc(start, tip, width, overlaps, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var target = overlaps[i].GetComponentInParent<IHittable>();
                if (target == null) continue;
                if (b.LastHit.TryGetValue(target, out float last) && Time.time - last < sameTargetCooldown) continue;
                b.LastHit[target] = Time.time;

                target.TakeHit(new HitInfo
                {
                    Damage = Mathf.Min(maxDamage, speed * damagePerMetrePerSecond),
                    Point = overlaps[i].ClosestPoint(tip),
                    Direction = b.TipVelocity.normalized,
                    Speed = speed,
                    Hand = side,
                });
            }
        }

        Transform BuildBlade(string name, Material face, Material glow)
        {
            // The root sits at the fist and points along the blade (+Z); scaling Z grows the blade out of the fist.
            var root = new GameObject(name).transform;
            root.SetParent(transform, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(body.GetComponent<Collider>());
            body.name = "Blade";
            body.transform.SetParent(root, false);
            body.transform.localPosition = new Vector3(0f, 0f, gripOffset + length * 0.5f);
            body.transform.localScale = new Vector3(width, 0.008f, length);
            var bodyRenderer = body.GetComponent<Renderer>();
            bodyRenderer.sharedMaterial = face;
            bodyRenderer.shadowCastingMode = ShadowCastingMode.Off;
            bodyRenderer.receiveShadows = false;

            // Bright core down the middle, fading towards the tip.
            LineRenderer core = AddLine(root, glow, width * 0.6f,
                new Vector3(0f, 0f, gripOffset), new Vector3(0f, 0f, gripOffset + length));
            core.colorGradient = Fade(coreColor);

            // Crossguard.
            AddLine(root, glow, 0.03f,
                new Vector3(-guardWidth * 0.5f, 0f, gripOffset), new Vector3(guardWidth * 0.5f, 0f, gripOffset));
            return root;
        }

        LineRenderer AddLine(Transform parent, Material material, float lineWidth, Vector3 a, Vector3 b)
        {
            var line = new GameObject("Glow").AddComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, a);
            line.SetPosition(1, b);
            line.widthMultiplier = lineWidth;
            line.startColor = line.endColor = coreColor;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        static Gradient Fade(Color c)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0.2f, 1f) });
            return g;
        }
    }
}
