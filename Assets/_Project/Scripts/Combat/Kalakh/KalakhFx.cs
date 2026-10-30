using System.Collections;
using ArcaneStrike.Story;
using ArcaneStrike.VFX;
using UnityEngine;

namespace ArcaneStrike.Combat
{
    /// <summary>The Kalakh's effects: missiles, warning rings on the ground and what erupts from them, shockwave rings.</summary>
    public static class KalakhFx
    {
        public static readonly Color Fire = new Color(1f, 0.45f, 0.1f, 1f);
        public static readonly Color Danger = new Color(1f, 0.12f, 0.08f, 1f);
        public static readonly Color ShadowGlow = new Color(0.6f, 0.2f, 1f, 1f);

        static Material glow;
        static Material Glow => glow != null ? glow : glow = SparkHands.CreateGlowMaterial(SparkHands.CreateSoftLine(64));

        // Effects run here, not on the monster, so they finish even if it is interrupted or dies.
        class Runner : MonoBehaviour { }
        static Runner runner;
        public static Coroutine Run(IEnumerator routine)
        {
            if (runner == null) runner = new GameObject("KalakhFx").AddComponent<Runner>();
            return runner.StartCoroutine(routine);
        }

        /// <summary>A ring of light racing out over the ground (a slam, a roar, a landing).</summary>
        public static void Shockwave(MonoBehaviour host, Vector3 centre, float radius, Color color, float seconds = 0.5f, float width = 0.25f) =>
            Run(RingOut(centre + Vector3.up * 0.06f, radius, color, seconds, width));

        static IEnumerator RingOut(Vector3 centre, float radius, Color color, float seconds, float width)
        {
            var line = NewRing("Shockwave", color, width);
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = t / seconds;
                SetCircle(line, centre, Mathf.Lerp(0.2f, radius, 1f - (1f - k) * (1f - k)));
                line.widthMultiplier = width * (1f - k * 0.7f);
                line.startColor = line.endColor = StoryFx.WithAlpha(color, 1f - k);
                yield return null;
            }
            Object.Destroy(line.gameObject);
        }

        public static LineRenderer NewRing(string name, Color color, float width)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.loop = true;
            line.positionCount = 48;
            line.useWorldSpace = true;
            line.sharedMaterial = Glow;
            line.widthMultiplier = width;
            line.startColor = line.endColor = color;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }

        public static void SetCircle(LineRenderer line, Vector3 centre, float radius)
        {
            for (int i = 0; i < line.positionCount; i++)
            {
                float a = i / (float)line.positionCount * Mathf.PI * 2f;
                line.SetPosition(i, centre + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }
        }

        static readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>The ground under `at` near the height `near` (world colliders only; not up onto a boulder or a ledge
        /// far above, not down a cliff far below).</summary>
        public static float Ground(Vector3 at, float near)
        {
            Probe(at, near, out float ground, out _);
            return ground;
        }

        /// <summary>Solid rock to stand on under `at` (near the height `near`): false over lava, or past an edge with
        /// nothing below within a few metres (the ledge's drop to the lava sea).</summary>
        public static bool Footing(Vector3 at, float near, out float ground) => Probe(at, near, out ground, out bool lava) && !lava;

        // The highest walkable surface under the point (true if there is one), and whether it is lava.
        static bool Probe(Vector3 at, float near, out float ground, out bool lava)
        {
            float best = float.MinValue;
            lava = false;
            Collider top = null;
            int n = Physics.RaycastNonAlloc(new Vector3(at.x, near + 0.8f, at.z), Vector3.down, hits, 4f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (hits[i].normal.y > 0.5f && hits[i].point.y > best && IsWorld(hits[i].collider))
                {
                    best = hits[i].point.y;
                    top = hits[i].collider;
                }
            if (top == null)
            {
                ground = near;
                return false;
            }
            ground = best;
            lava = top.GetComponent<LavaFlow>() != null;
            return true;
        }

        /// <summary>Part of the world (not the player, a Kalakh, or loose rubble tumbling about).</summary>
        public static bool IsWorld(Collider c)
        {
            if (c == null || c.attachedRigidbody != null) return false;
            Transform player = Camera.main != null ? Camera.main.transform.root : null;
            return (player == null || !c.transform.IsChildOf(player)) && c.GetComponentInParent<KalakhWarrior>() == null;
        }

        /// <summary>A fountain of fire and jagged rock bursting out of the ground.</summary>
        public static void Erupt(MonoBehaviour host, Vector3 at, float radius, bool fire)
        {
            for (int i = 0; i < 40; i++)
                StoryFx.Emit(CombatFx.Sparks, at + Random.insideUnitSphere * radius * 0.4f,
                             new Vector3(Random.Range(-1f, 1f), Random.Range(4f, 9f), Random.Range(-1f, 1f)), Random.Range(0.04f, 0.1f),
                             Random.Range(0.6f, 1.2f), fire ? Color.Lerp(Fire, Color.yellow, Random.value * 0.5f) : new Color(0.5f, 0.45f, 0.42f));
            CombatFx.SmokePuff(at + Vector3.up * 0.4f, radius * 0.6f, 14, 1.5f);
            Run(Spikes(at, radius, fire));
            CombatAudio.Play(CombatAudio.HeavySwoosh, at, 1f, 0.2f);
            CombatAudio.Play(CombatAudio.Hit, at, 0.6f, 0.3f);
            if (fire)
            {
                var light = new GameObject("EruptionLight").AddComponent<Light>();
                light.transform.position = at + Vector3.up;
                light.type = LightType.Point;
                light.color = Fire;
                light.range = radius * 4f;
                light.intensity = 6f;
                light.shadows = LightShadows.None;
                Run(FadeOut(light, 0.8f));
            }
        }

        static IEnumerator FadeOut(Light l, float seconds)
        {
            float from = l.intensity;
            for (float t = 0f; t < seconds && l != null; t += Time.deltaTime)
            {
                l.intensity = Mathf.Lerp(from, 0f, t / seconds);
                yield return null;
            }
            if (l != null) Object.Destroy(l.gameObject);
        }

        static Material rock;

        // Rock spikes stab up out of the ground, hold, and sink again.
        static IEnumerator Spikes(Vector3 at, float radius, bool fire)
        {
            if (rock == null)
            {
                rock = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "EruptionRock" };
                rock.SetColor("_BaseColor", new Color(0.12f, 0.1f, 0.1f));
                rock.EnableKeyword("_EMISSION");
                rock.SetColor("_EmissionColor", new Color(0.6f, 0.15f, 0.02f));
            }
            var spikes = new Transform[6];
            var heights = new float[6];
            for (int i = 0; i < spikes.Length; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(go.GetComponent<Collider>());
                go.GetComponent<Renderer>().sharedMaterial = rock;
                Vector2 r = Random.insideUnitCircle * radius * 0.6f;
                go.transform.position = at + new Vector3(r.x, 0f, r.y);
                go.transform.rotation = Quaternion.Euler(Random.Range(-18f, 18f), Random.Range(0f, 360f), Random.Range(-18f, 18f));
                spikes[i] = go.transform;
                heights[i] = Random.Range(0.6f, 1.4f) * (fire ? 0.8f : 1.2f);
            }
            for (float t = 0f; t < 1.6f; t += Time.deltaTime)
            {
                float up = t < 0.12f ? t / 0.12f : t < 1.1f ? 1f : 1f - (t - 1.1f) / 0.5f;
                for (int i = 0; i < spikes.Length; i++)
                    spikes[i].localScale = new Vector3(0.18f * (1f - 0.3f * up), heights[i] * up, 0.18f * (1f - 0.3f * up));
                yield return null;
            }
            foreach (Transform s in spikes) Object.Destroy(s.gameObject);
        }

        /// <summary>A glowing cut on the monster where the blade went in, fading over a few seconds.</summary>
        public static void Wound(Transform on, Vector3 point, Vector3 along, Color color)
        {
            var go = new GameObject("Wound");
            go.transform.SetParent(on, true);
            go.transform.position = point;
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 3;
            Vector3 d = on.InverseTransformDirection(along.sqrMagnitude > 1e-4f ? along.normalized : Vector3.right) * 0.18f / Mathf.Max(on.lossyScale.x, 0.01f);
            line.SetPosition(0, -d);
            line.SetPosition(1, Vector3.zero);
            line.SetPosition(2, d);
            line.sharedMaterial = Glow;
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));
            line.widthMultiplier = 0.035f;
            line.startColor = line.endColor = color;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<FadeLine>().life = 3f;
        }

        class FadeLine : MonoBehaviour
        {
            public float life = 3f;
            float age;
            LineRenderer line;
            Color start;

            void Start()
            {
                line = GetComponent<LineRenderer>();
                start = line.startColor;
            }

            void Update()
            {
                age += Time.deltaTime;
                line.startColor = line.endColor = StoryFx.WithAlpha(start, start.a * (1f - age / life));
                if (age >= life) Destroy(gameObject);
            }
        }
    }

    /// <summary>
    /// Something the Kalakh spits or throws at the player: lava (burns), a boulder (lobbed, knocks back), a bone javelin
    /// (fast, straight), shadow bolts (slow, they follow you), venom (slows). The sword can send it back: swing through it
    /// or hold the guard in its path and it flies back into the one who threw it.
    /// </summary>
    public class KalakhMissile : MonoBehaviour
    {
        MissileKind kind;
        Vector3 velocity;
        Transform target;
        KalakhWarrior owner;
        float damage, life, homing;
        bool returned, lobbed, ownsLight;
        HitEffect effect;
        Light glow;
        TrailRenderer trail;
        Color color;

        public static KalakhMissile Launch(KalakhWarrior owner, MissileKind kind, Vector3 from, Transform target, float speed, float damage, HitEffect effect, Vector3 aimOffset = default)
        {
            var go = new GameObject($"Missile ({kind})");
            go.transform.position = from;
            var m = go.AddComponent<KalakhMissile>();
            m.Setup(owner, kind, target, speed, damage, effect, aimOffset);
            return m;
        }

        void Setup(KalakhWarrior from, MissileKind k, Transform aimAt, float speed, float dmg, HitEffect fx, Vector3 aimOffset)
        {
            owner = from;
            kind = k;
            target = aimAt;
            damage = dmg;
            effect = fx;
            Vector3 aim = (aimAt != null ? aimAt.position - Vector3.up * 0.25f : transform.position + Vector3.forward) + aimOffset;
            lobbed = k == MissileKind.Rock || (k == MissileKind.Fire && aimOffset.sqrMagnitude > 0.01f);
            if (lobbed)
            {
                // A ballistic arc that lands on the aim point.
                Vector3 d = aim - transform.position;
                float flat = new Vector2(d.x, d.z).magnitude;
                float time = Mathf.Max(0.6f, flat / speed);
                velocity = new Vector3(d.x / time, d.y / time + 0.5f * 9.81f * time, d.z / time);
            }
            else velocity = (aim - transform.position).normalized * speed;
            homing = k == MissileKind.Shadow ? 2.2f : 0f;
            color = k switch
            {
                MissileKind.Fire => KalakhFx.Fire,
                MissileKind.Shadow => KalakhFx.ShadowGlow,
                MissileKind.Venom => new Color(0.5f, 1f, 0.2f, 1f),
                MissileKind.Bone => new Color(0.95f, 0.9f, 0.75f, 1f),
                _ => new Color(0.9f, 0.4f, 0.15f, 1f),
            };
            BuildLook();
        }

        // Shared by every missile of a kind (made once, not per throw).
        static readonly System.Collections.Generic.Dictionary<MissileKind, Material> looks = new System.Collections.Generic.Dictionary<MissileKind, Material>();
        static Material trailGlow;
        const int MaxLit = 3;
        static int lit;

        void OnDestroy()
        {
            if (ownsLight) lit = Mathf.Max(0, lit - 1);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => lit = 0;

        void BuildLook()
        {
            var body = GameObject.CreatePrimitive(kind == MissileKind.Bone ? PrimitiveType.Capsule : PrimitiveType.Sphere);
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(transform, false);
            float size = kind switch { MissileKind.Rock => 0.45f, MissileKind.Bone => 0.08f, MissileKind.Shadow => 0.22f, _ => 0.2f };
            body.transform.localScale = kind == MissileKind.Bone ? new Vector3(size, 0.6f, size) : Vector3.one * size;
            if (kind == MissileKind.Bone) body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            if (!looks.TryGetValue(kind, out Material m) || m == null)
            {
                if (kind == MissileKind.Rock || kind == MissileKind.Bone)
                {
                    m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    m.SetColor("_BaseColor", kind == MissileKind.Rock ? new Color(0.15f, 0.12f, 0.11f) : new Color(0.75f, 0.7f, 0.6f));
                    if (kind == MissileKind.Rock)
                    {
                        m.EnableKeyword("_EMISSION");
                        m.SetColor("_EmissionColor", new Color(0.8f, 0.2f, 0.03f));
                    }
                }
                else m = AegisShield.CreateFaceMaterial(color);
                looks[kind] = m;
            }
            var look = body.GetComponent<Renderer>();
            look.sharedMaterial = m;
            look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (lit < MaxLit && kind != MissileKind.Bone) // (a volley lights only its first few: every light costs on the headset)
            {
                lit++;
                ownsLight = true;
                glow = gameObject.AddComponent<Light>();
                glow.type = LightType.Point;
                glow.color = color;
                glow.range = 3f;
                glow.intensity = 2.5f;
                glow.shadows = LightShadows.None;
            }
            trail = gameObject.AddComponent<TrailRenderer>();
            if (trailGlow == null) trailGlow = SparkHands.CreateGlowMaterial(SparkHands.CreateSoftLine(64));
            trail.sharedMaterial = trailGlow;
            trail.time = 0.25f;
            trail.widthMultiplier = size * 0.8f;
            trail.startColor = color;
            trail.endColor = StoryFx.WithAlpha(color, 0f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            life += dt;
            if (lobbed && !returned) velocity += Vector3.down * 9.81f * dt;
            if (homing > 0f && target != null && !returned)
            {
                Vector3 want = (target.position - Vector3.up * 0.25f - transform.position).normalized * velocity.magnitude;
                velocity = Vector3.RotateTowards(velocity, want, homing * dt, 0f);
            }
            transform.position += velocity * dt;
            if (velocity.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(velocity);
            if (Random.value < 0.6f)
                StoryFx.Emit(CombatFx.Sparks, transform.position, -velocity * 0.1f + Random.insideUnitSphere * 0.3f, 0.03f, 0.4f, color);

            if (returned)
            {
                // Sent back: into the one who threw it.
                if (owner == null || owner.Dead)
                {
                    if (life > 4f) Destroy(gameObject);
                    return;
                }
                Vector3 to = owner.Body.Chest - transform.position;
                velocity = to.normalized * Mathf.Max(velocity.magnitude, 12f);
                if (to.magnitude < 0.5f)
                {
                    owner.TakeHit(new HitInfo { Damage = damage * 2f, Point = transform.position, Direction = velocity.normalized, Speed = 10f, Technique = SwordTechnique.Deflect });
                    Burst();
                }
                return;
            }

            if (target != null)
            {
                Vector3 head = target.position, chest = head - Vector3.up * 0.45f;
                float near = Mathf.Min(Vector3.Distance(transform.position, head), Vector3.Distance(transform.position, chest));
                // The sword can bat it back.
                if (near < 1.1f && WeaverSword.Instance != null && WeaverSword.Instance.TryDeflect(transform.position, velocity))
                {
                    returned = true;
                    life = 0f;
                    if (owner != null) owner.Resolved("deflect");
                    CombatFx.Popup("DEFLECT!", transform.position + Vector3.up * 0.3f, Color.white, 1f);
                    return;
                }
                float hitRadius = kind == MissileKind.Rock ? 0.6f : 0.35f;
                if (near < hitRadius)
                {
                    if (owner != null) owner.Resolved("hit");
                    KalakhWarrior.HurtPlayer(damage, transform.position, velocity.normalized, effect, Reach.Mid, owner);
                    Burst();
                    return;
                }
            }
            if (life > 6f || transform.position.y < KalakhFx.Ground(transform.position, transform.position.y) - 0.05f)
            {
                if (kind == MissileKind.Rock || kind == MissileKind.Fire)
                {
                    // Splashes where it lands: close enough still hurts.
                    if (target != null && Vector3.Distance(new Vector3(target.position.x, transform.position.y, target.position.z), transform.position) < 1.2f)
                        KalakhWarrior.HurtPlayer(damage * 0.6f, transform.position, velocity.normalized, effect, Reach.Low, owner);
                }
                Burst();
            }
        }

        void Burst()
        {
            CombatFx.HitSparks(transform.position, -velocity.normalized, kind == MissileKind.Rock ? 40 : 25, color);
            CombatFx.SmokePuff(transform.position, kind == MissileKind.Rock ? 0.5f : 0.2f, kind == MissileKind.Rock ? 12 : 5, 0.5f);
            CombatAudio.Play(kind == MissileKind.Rock ? CombatAudio.HeavySwoosh : CombatAudio.Hit, transform.position, 0.7f, 0.3f);
            Destroy(gameObject);
        }
    }
}
