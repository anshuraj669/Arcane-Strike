using System.Collections.Generic;
using ArcaneStrike.Combat;
using ArcaneStrike.HandInput;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArcaneStrike.VFX
{
    /// <summary>
    /// Firebolt (GDD §5.1): every Firebolt gesture launches a fireball from the fingertips. It flies where
    /// the fingers point (blended with where the player looks), curves towards the hittable target nearest
    /// the gaze, and explodes on the first thing it touches, damaging any <see cref="IHittable"/>.
    /// All fire is additive particles from one shared system, so no Bloom or lights are needed on Quest.
    /// Built at runtime, so it needs no assets. <see cref="SparkHands"/> adds it automatically.
    /// </summary>
    public class FireboltProjectiles : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] GestureRecognizer recognizer;

        [Header("Flight")]
        [SerializeField] float speed = 10f;
        [Tooltip("A bolt that hits nothing fizzles out after this many seconds.")]
        [SerializeField] float lifetime = 2.5f;
        [Tooltip("Collision radius of the fireball (m).")]
        [SerializeField] float radius = 0.07f;
        [Tooltip("0 = fly exactly where the fingers point, 1 = fly where the player looks. " +
                 "Webcam finger direction is noisy, so some gaze keeps shots on target.")]
        [Range(0f, 1f)]
        [SerializeField] float aimAssist = 0.5f;

        [Header("Homing: towards the target nearest the gaze")]
        [SerializeField] float homingRange = 15f;
        [Tooltip("Only targets within this angle of the gaze are chosen (degrees).")]
        [SerializeField] float homingCone = 25f;
        [Tooltip("How fast a bolt can turn towards its target (degrees per second).")]
        [SerializeField] float turnRate = 240f;

        [Header("Damage")]
        [SerializeField] float damage = 20f;

        [Header("Look")]
        [SerializeField] Color hotColor = new Color(1f, 0.93f, 0.65f, 1f);
        [SerializeField] Color fireColor = new Color(1f, 0.45f, 0.15f, 1f); // same orange as the Firebolt hand flash
        [SerializeField] float coreSize = 0.17f;
        [SerializeField] float sparksPerSecond = 140f;
        [SerializeField] float trailTime = 0.15f;
        [SerializeField] float trailWidth = 0.1f;
        [SerializeField] int explosionSparks = 45;
        [SerializeField] float explosionSize = 0.7f;

        class Bolt
        {
            public bool Alive;
            public Vector3 Position, Velocity;
            public float Born, SparkDebt;
            public HandSide Hand;
            public Collider Target;
            public TrailRenderer Trail;
        }

        readonly List<Bolt> bolts = new List<Bolt>();
        readonly RaycastHit[] hits = new RaycastHit[16];
        readonly Collider[] overlaps = new Collider[64];
        ParticleSystem fire;
        Material trailMaterial;

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
            fire = CreateFireSystem(SparkHands.CreateGlowMaterial(SparkHands.CreateSoftDot(64)));
            trailMaterial = SparkHands.CreateGlowMaterial(SparkHands.CreateSoftLine(64));
        }

        Transform Head() => input != null && input.Head != null ? input.Head : Camera.main != null ? Camera.main.transform : null;

        // ---------------------------------------------------------------- launch

        void HandleGesture(GestureEvent e)
        {
            if (e.Type != GestureType.Firebolt || fire == null) return;
            Transform head = Head();
            if (head == null) return;

            Vector3 aim = e.Direction.sqrMagnitude > 1e-6f ? e.Direction.normalized : head.forward;
            if (Vector3.Dot(aim, head.forward) < 0f) aim = head.forward; // never fire back at the player
            Vector3 dir = Vector3.Slerp(aim, head.forward, aimAssist).normalized;

            Bolt b = GetBolt();
            b.Alive = true;
            b.Position = e.Position + dir * 0.05f;
            b.Velocity = dir * speed;
            b.Born = Time.time;
            b.SparkDebt = 0f;
            b.Hand = e.Hand;
            b.Target = FindGazeTarget(head);

            b.Trail.transform.position = b.Position;
            b.Trail.gameObject.SetActive(true);
            b.Trail.Clear();
            b.Trail.emitting = true;

            // Muzzle flash at the fingertips.
            Burst(e.Position, 12, 0.8f, explosionSize * 0.35f);
        }

        Bolt GetBolt()
        {
            foreach (Bolt b in bolts)
                if (!b.Alive) return b;

            var trail = new GameObject("FireboltTrail").AddComponent<TrailRenderer>();
            trail.transform.SetParent(transform, false);
            trail.sharedMaterial = trailMaterial;
            trail.time = trailTime;
            trail.minVertexDistance = 0.02f;
            trail.widthMultiplier = trailWidth;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.colorGradient = TrailGradient();
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            var bolt = new Bolt { Trail = trail };
            bolts.Add(bolt);
            return bolt;
        }

        Collider FindGazeTarget(Transform head)
        {
            int count = Physics.OverlapSphereNonAlloc(head.position, homingRange, overlaps, ~0, QueryTriggerInteraction.Collide);
            Collider best = null;
            float bestAngle = homingCone;
            for (int i = 0; i < count; i++)
            {
                Collider c = overlaps[i];
                if (c.GetComponentInParent<IHittable>() == null) continue;
                float angle = Vector3.Angle(head.forward, c.bounds.center - head.position);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = c;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- flight

        void Update()
        {
            Transform head = Head();
            float dt = Time.deltaTime;
            foreach (Bolt b in bolts)
            {
                if (!b.Alive) continue;

                if (b.Target != null && b.Target.gameObject.activeInHierarchy)
                {
                    Vector3 desired = (b.Target.bounds.center - b.Position).normalized * speed;
                    b.Velocity = Vector3.RotateTowards(b.Velocity, desired, turnRate * Mathf.Deg2Rad * dt, 0f);
                }

                Vector3 step = b.Velocity * dt;
                if (FindHit(b.Position, step, head, out RaycastHit hit))
                {
                    Explode(b, hit.point, hit.collider);
                    continue;
                }
                b.Position += step;

                if (Time.time - b.Born > lifetime)
                {
                    Fizzle(b);
                    continue;
                }

                b.Trail.transform.position = b.Position;
                EmitFlame(b, dt);
            }
        }

        bool FindHit(Vector3 from, Vector3 step, Transform head, out RaycastHit best)
        {
            best = default;
            float distance = step.magnitude;
            if (distance < 1e-5f) return false;

            int count = Physics.SphereCastNonAlloc(from, radius, step / distance, hits, distance, ~0, QueryTriggerInteraction.Collide);
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider c = hits[i].collider;
                bool hittable = c.GetComponentInParent<IHittable>() != null;
                if (c.isTrigger && !hittable) continue;                         // trigger volumes aren't solid
                if (c.GetComponentInParent<HandHitbox>() != null) continue;     // the player's own hands
                if (head != null && c.transform.root == head.root) continue;    // the player's rig / body
                if (hits[i].distance < bestDistance)
                {
                    bestDistance = hits[i].distance;
                    best = hits[i];
                }
            }
            // A cast that starts inside a collider reports point zero: use the bolt's position instead.
            if (bestDistance < float.MaxValue && best.point == Vector3.zero) best.point = from;
            return bestDistance < float.MaxValue;
        }

        void Explode(Bolt b, Vector3 point, Collider hitCollider)
        {
            var target = hitCollider != null ? hitCollider.GetComponentInParent<IHittable>() : null;
            target?.TakeHit(new HitInfo
            {
                Damage = damage,
                Point = point,
                Direction = b.Velocity.normalized,
                Speed = b.Velocity.magnitude,
                Hand = b.Hand,
            });
            Burst(point, explosionSparks, 2.5f, explosionSize);
            Retire(b);
        }

        void Fizzle(Bolt b)
        {
            Burst(b.Position, explosionSparks / 3, 0.8f, explosionSize * 0.4f);
            Retire(b);
        }

        static void Retire(Bolt b)
        {
            b.Alive = false;
            b.Target = null;
            b.Trail.emitting = false;
            b.Trail.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- particles

        void EmitFlame(Bolt b, float dt)
        {
            // Core: a fresh short-lived blob every frame, so the fireball flickers like real fire.
            Emit(b.Position, Vector3.zero, coreSize * Random.Range(0.85f, 1.15f), 0.06f, hotColor);
            Emit(b.Position, Vector3.zero, coreSize * 1.8f, 0.05f, WithAlpha(fireColor, 0.6f));

            // Sparks shed behind it.
            b.SparkDebt += sparksPerSecond * dt;
            Vector3 back = -b.Velocity.normalized;
            while (b.SparkDebt >= 1f)
            {
                b.SparkDebt -= 1f;
                Vector3 v = back * Random.Range(0.3f, 1.2f) + Random.insideUnitSphere * 0.5f;
                Emit(b.Position + Random.insideUnitSphere * radius, v, coreSize * Random.Range(0.15f, 0.35f),
                     Random.Range(0.2f, 0.4f), Color.Lerp(fireColor, hotColor, Random.value * 0.6f));
            }
        }

        void Burst(Vector3 position, int sparks, float sparkSpeed, float flashSize)
        {
            if (fire == null) return;
            Emit(position, Vector3.zero, flashSize, 0.15f, hotColor);
            Emit(position, Vector3.zero, flashSize * 1.6f, 0.22f, WithAlpha(fireColor, 0.7f));
            for (int i = 0; i < sparks; i++)
            {
                Vector3 v = Random.onUnitSphere * sparkSpeed * Random.Range(0.4f, 1f);
                Emit(position, v, coreSize * Random.Range(0.15f, 0.4f), Random.Range(0.3f, 0.6f),
                     Color.Lerp(fireColor, hotColor, Random.value));
            }
        }

        void Emit(Vector3 position, Vector3 velocity, float size, float life, Color color)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startSize = size,
                startLifetime = life,
                startColor = color,
            };
            fire.Emit(p, 1);
        }

        static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        Gradient TrailGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(hotColor, 0f), new GradientColorKey(fireColor, 0.4f), new GradientColorKey(fireColor, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        ParticleSystem CreateFireSystem(Material material)
        {
            var go = new GameObject("FireboltFire");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1500;
            main.startSpeed = 0f;
            main.gravityModifier = -0.08f; // fire drifts upwards

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;

            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = fade;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ps.Play();
            return ps;
        }
    }
}
