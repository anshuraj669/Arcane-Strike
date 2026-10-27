using System.Collections.Generic;
using ArcaneStrike.Combat;
using ArcaneStrike.HandInput;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArcaneStrike.VFX
{
    /// <summary>
    /// Skyhammer: when both fists slam down, a ring of light bursts across the floor below them and
    /// every <see cref="IHittable"/> inside the radius takes a hit. Built at runtime, so it needs no assets.
    /// <see cref="SparkHands"/> adds it automatically.
    /// </summary>
    public class SkyhammerShockwave : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] GestureRecognizer recognizer;

        [Header("Look")]
        [SerializeField] float radius = 2.5f;
        [SerializeField] float duration = 0.6f;
        [SerializeField] Color ringColor = new Color(0.31f, 0.89f, 1f, 1f); // Volt cyan #4FE3FF
        [SerializeField] float ringWidth = 0.08f;
        [Tooltip("Used when no floor collider is found below the slam: floor height below the head (m).")]
        [SerializeField] float floorBelowHead = 1.4f;

        [Header("Damage")]
        [SerializeField] float damage = 35f;

        class Wave
        {
            public Transform Root;
            public LineRenderer[] Rings;
            public float Start;
        }

        const int RingPoints = 64;
        readonly List<Wave> waves = new List<Wave>();
        readonly HashSet<IHittable> hitThisSlam = new HashSet<IHittable>();
        readonly Collider[] overlaps = new Collider[32];
        Material glow;

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

        void HandleGesture(GestureEvent e)
        {
            if (e.Type != GestureType.Skyhammer) return;
            Vector3 centre = FloorBelow(e.Position);
            SpawnWave(centre);
            DealDamage(centre, e.Hand);
        }

        Vector3 FloorBelow(Vector3 from)
        {
            Transform head = input != null && input.Head != null ? input.Head : Camera.main != null ? Camera.main.transform : null;
            float fallbackY = head != null ? head.position.y - floorBelowHead : from.y - 1f;
            // Ignore hittable colliders (an enemy under the fists is not the floor).
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore) &&
                hit.collider.GetComponentInParent<IHittable>() == null)
                return hit.point;
            return new Vector3(from.x, fallbackY, from.z);
        }

        void DealDamage(Vector3 centre, HandSide hand)
        {
            hitThisSlam.Clear();
            int count = Physics.OverlapSphereNonAlloc(centre + Vector3.up * 0.8f, radius, overlaps, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var target = overlaps[i].GetComponentInParent<IHittable>();
                if (target == null || !hitThisSlam.Add(target)) continue;

                Vector3 point = overlaps[i].ClosestPoint(centre);
                Vector3 away = point - centre;
                away.y = 0f;
                target.TakeHit(new HitInfo
                {
                    Damage = damage,
                    Point = point,
                    Direction = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.up,
                    Speed = 0f,
                    Hand = hand,
                });
            }
        }

        void SpawnWave(Vector3 centre)
        {
            if (glow == null) glow = SparkHands.CreateGlowMaterial(SparkHands.CreateSoftLine(64));

            var root = new GameObject("SkyhammerWave").transform;
            root.SetParent(transform, false);
            root.position = centre + Vector3.up * 0.02f; // just above the floor so it doesn't z-fight
            var wave = new Wave { Root = root, Start = Time.time, Rings = new LineRenderer[2] };
            for (int r = 0; r < 2; r++)
            {
                var line = new GameObject("Ring").AddComponent<LineRenderer>();
                line.transform.SetParent(root, false);
                line.transform.localScale = Vector3.one * (r == 0 ? 1f : 0.7f); // inner ring trails the outer one
                line.sharedMaterial = glow;
                line.useWorldSpace = false;
                line.loop = true;
                line.positionCount = RingPoints;
                line.widthMultiplier = r == 0 ? ringWidth : ringWidth * 0.6f;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                for (int i = 0; i < RingPoints; i++)
                {
                    float a = i * Mathf.PI * 2f / RingPoints;
                    line.SetPosition(i, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
                }
                wave.Rings[r] = line;
            }
            waves.Add(wave);
        }

        void Update()
        {
            for (int w = waves.Count - 1; w >= 0; w--)
            {
                Wave wave = waves[w];
                float t = (Time.time - wave.Start) / Mathf.Max(duration, 0.01f);
                if (t >= 1f)
                {
                    Destroy(wave.Root.gameObject);
                    waves.RemoveAt(w);
                    continue;
                }

                float r = radius * (1f - (1f - t) * (1f - t)); // ease out
                wave.Root.localScale = new Vector3(r, 1f, r);
                Color c = ringColor;
                c.a *= 1f - t;
                foreach (LineRenderer ring in wave.Rings) ring.startColor = ring.endColor = c;
            }
        }
    }
}
