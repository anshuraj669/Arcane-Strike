using System;
using System.Collections.Generic;
using ArcaneStrike.Story;
using ArcaneStrike.VFX;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ArcaneStrike.Combat
{
    /// <summary>
    /// A Kalakh shadow in the Rift. It rises out of a crack in the ground, stalks towards the player, circles at
    /// arm's length and strikes: it rears back first (eyes flaring, claws raised, a snarl) so the player can parry,
    /// then lunges. Parried, it reels (a Riposte does double damage); hit, it flinches and is knocked back; killed, it
    /// comes apart into smoke and specks of light.
    /// Kinds: Shade (the common one), Lurker (small, fast, weak), Brute (big and slow, hits hard; ordinary slashes
    /// don't make it flinch, only parries and overhead smashes do).
    /// Looks: a placeholder figure built from shapes, unless a model is given (<see cref="Spawn"/>). A model with an
    /// Animator gets "Speed" (float) and "Attack", "Hit", "Die" (triggers) if it has those parameters.
    /// </summary>
    public class ShadowMonster : MonoBehaviour, IHittable, IReeling
    {
        public enum Kind { Shade, Lurker, Brute }

        class Stats
        {
            public float health, speed, height, damage, reach, windup;
        }

        static readonly Dictionary<Kind, Stats> Table = new Dictionary<Kind, Stats>
        {
            { Kind.Shade, new Stats { health = 55f, speed = 1.4f, height = 1.75f, damage = 10f, reach = 1.25f, windup = 0.75f } },
            { Kind.Lurker, new Stats { health = 30f, speed = 2.6f, height = 1.2f, damage = 6f, reach = 1.0f, windup = 0.55f } },
            { Kind.Brute, new Stats { health = 170f, speed = 0.9f, height = 2.5f, damage = 22f, reach = 1.7f, windup = 1.1f } },
        };

        /// <summary>Every shadow still standing.</summary>
        public static readonly List<ShadowMonster> Alive = new List<ShadowMonster>();
        /// <summary>How many shadows may be winding up or striking at once (so the player is pressed, not swamped).</summary>
        public static int MaxAttackers = 2;
        static int attacking;

        public Kind Type { get; private set; }
        public float Health { get; private set; }
        /// <summary>Knocked off balance (parried or smashed): the next hit is a Riposte.</summary>
        public bool Reeling => Time.time < reelUntil;
        public bool Dead { get; private set; }
        public event Action<ShadowMonster> OnDied;

        enum State { Rising, Stalk, Windup, Strike, Recover, Dying }

        Stats stats;
        State state;
        float stateTime, nextAttack, reelUntil, flinchUntil, orbit;
        Vector3 knock;
        Transform target, visual;
        Animator animator;
        readonly List<Renderer> body = new List<Renderer>();
        Transform eyeL, eyeR, armL, armR;
        Material eyeMaterial;
        MaterialPropertyBlock block;
        bool attackSlot;
        float floorY;
        float pace, gait, stride; // how fast it's moving, eased (0..1), and its step cycle (a model without animations)
        bool hasSpeed, flashing;  // (the model's Animator has a Speed float; the gold flash is showing)
        /// <summary>The closest it comes to the player's eyes across the ground (never into the headset).</summary>
        const float Personal = 0.65f;

        static Material shadowMaterial;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        /// <summary>Makes a shadow of this kind at `position` (on the floor), hunting `target` (the player's head).</summary>
        public static ShadowMonster Spawn(Kind kind, Vector3 position, Transform target, GameObject model = null)
        {
            var go = new GameObject($"Shadow ({kind})");
            go.transform.position = position;
            var m = go.AddComponent<ShadowMonster>();
            m.Setup(kind, target, model);
            return m;
        }

        void Setup(Kind kind, Transform hunt, GameObject model)
        {
            Type = kind;
            stats = Table[kind];
            Health = stats.health;
            target = hunt;
            floorY = transform.position.y;
            orbit = Random.value < 0.5f ? -1f : 1f;
            block = new MaterialPropertyBlock();
            var collider = gameObject.AddComponent<CapsuleCollider>();
            collider.isTrigger = true; // the sword finds it; walking doesn't bump into it
            collider.height = stats.height;
            collider.radius = stats.height * 0.2f;
            collider.center = Vector3.up * stats.height * 0.5f;

            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            if (model != null)
            {
                GameObject m = Instantiate(model, visual);
                m.transform.localPosition = Vector3.zero;
                m.transform.localRotation = Quaternion.identity;
                FitHeight(m.transform, stats.height);
                animator = m.GetComponentInChildren<Animator>();
                if (animator != null) // (looked up once: Animator.parameters makes a new array every call)
                    foreach (var p in animator.parameters)
                        if (p.name == "Speed" && p.type == AnimatorControllerParameterType.Float) hasSpeed = true;
                body.AddRange(m.GetComponentsInChildren<Renderer>());
            }
            else BuildPlaceholder();

            Alive.Add(this);
            state = State.Rising;
            visual.localPosition = Vector3.down * stats.height;
            CombatFx.SmokePuff(transform.position, 0.5f, 25, 1f);
            CombatAudio.Play(CombatAudio.Growl, transform.position, 0.6f, 0.25f);
        }

        // Scales a model so its bounds are `height` tall with its feet at the pivot.
        static void FitHeight(Transform model, float height)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            if (b.size.y < 1e-3f) return;
            model.localScale *= height / b.size.y;
            b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            model.position += Vector3.up * (model.parent.position.y - b.min.y);
        }

        // A hunched figure of darkness: a body tapering into smoke, a head with two burning eyes, long arms ending in
        // claws. Brutes are broad, Lurkers small and low.
        void BuildPlaceholder()
        {
            if (shadowMaterial == null)
            {
                shadowMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Shadow" };
                shadowMaterial.SetColor(BaseColor, new Color(0.03f, 0.02f, 0.04f));
                shadowMaterial.SetFloat("_Smoothness", 0.35f);
            }
            eyeMaterial = AegisShield.CreateFaceMaterial(new Color(0.85f, 0.15f, 1f, 1f));
            float h = stats.height, w = Type == Kind.Brute ? 1.5f : Type == Kind.Lurker ? 0.85f : 1f;
            Part(PrimitiveType.Capsule, new Vector3(0f, h * 0.45f, 0f), new Vector3(0.42f * w, h * 0.32f, 0.3f * w), Vector3.right * 12f);
            Part(PrimitiveType.Sphere, new Vector3(0f, h * 0.82f, 0.08f), Vector3.one * 0.26f * Mathf.Sqrt(w), Vector3.zero);
            Part(PrimitiveType.Sphere, new Vector3(0f, h * 0.68f, 0.02f), new Vector3(0.5f * w, 0.22f, 0.32f * w), Vector3.zero); // shoulders
            armL = Arm(-1f, h, w);
            armR = Arm(1f, h, w);
            eyeL = Eye(-0.05f, h);
            eyeR = Eye(0.05f, h);
        }

        Transform Part(PrimitiveType type, Vector3 position, Vector3 scale, Vector3 euler, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent != null ? parent : visual, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.transform.localEulerAngles = euler;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = shadowMaterial;
            body.Add(r);
            return go.transform;
        }

        Transform Arm(float side, float h, float w)
        {
            var shoulder = new GameObject(side < 0f ? "ArmL" : "ArmR").transform;
            shoulder.SetParent(visual, false);
            shoulder.localPosition = new Vector3(side * 0.24f * w, h * 0.68f, 0.02f);
            float length = h * 0.5f;
            Part(PrimitiveType.Capsule, new Vector3(0f, -length * 0.5f, 0f), new Vector3(0.1f * w, length * 0.5f, 0.1f * w), Vector3.zero, shoulder);
            for (int k = -1; k <= 1; k++) // three long claws
                Part(PrimitiveType.Capsule, new Vector3(k * 0.035f, -length - 0.09f, 0.03f), new Vector3(0.025f, 0.11f, 0.025f), new Vector3(20f, 0f, k * 12f), shoulder);
            shoulder.localEulerAngles = new Vector3(0f, 0f, side * 18f);
            return shoulder;
        }

        Transform Eye(float x, float h)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(visual, false);
            go.transform.localPosition = new Vector3(x * Mathf.Sqrt(h / 1.75f), h * 0.84f, 0.19f * Mathf.Sqrt(h / 1.75f));
            go.transform.localScale = Vector3.one * 0.045f;
            go.GetComponent<Renderer>().sharedMaterial = eyeMaterial;
            return go.transform;
        }

        void OnDestroy()
        {
            Alive.Remove(this);
            ReleaseSlot();
        }

        // ---------------------------------------------------------------- behaviour

        void Update()
        {
            float dt = Time.deltaTime;
            stateTime += dt;
            Vector3 player = target != null ? new Vector3(target.position.x, floorY, target.position.z) : transform.position + transform.forward;
            Vector3 to = player - transform.position;
            to.y = 0f;
            float distance = to.magnitude;
            Vector3 toward = distance > 1e-3f ? to / distance : transform.forward;

            switch (state)
            {
                case State.Rising: // up out of the crack
                    visual.localPosition = Vector3.down * stats.height * (1f - Mathf.SmoothStep(0f, 1f, stateTime / 1.1f));
                    if (Random.value < 0.5f) CombatFx.SmokePuff(transform.position, 0.3f, 1, 0.8f);
                    if (stateTime > 1.1f) Enter(State.Stalk);
                    Face(toward, dt * 4f);
                    break;

                case State.Stalk:
                {
                    // Close in to striking range, then circle; keep clear of the other shadows.
                    Vector3 move = Vector3.zero;
                    float range = stats.reach * 0.85f;
                    if (distance > range + 0.15f) move += toward;
                    else if (distance < range - 0.25f) move -= toward * 0.6f;
                    else move += Vector3.Cross(Vector3.up, toward) * orbit * 0.4f;
                    foreach (ShadowMonster other in Alive)
                    {
                        if (other == this || other.Dead) continue;
                        Vector3 away = transform.position - other.transform.position;
                        away.y = 0f;
                        float d = away.magnitude;
                        if (d < 1.1f && d > 1e-3f) move += away / d * (1.1f - d) * 2f;
                    }
                    float pace = stats.speed * (Time.time < flinchUntil ? 0.2f : 1f);
                    transform.position += Vector3.ClampMagnitude(move, 1f) * pace * dt;
                    Face(toward, dt * 6f);
                    SetSpeed(move.magnitude * pace);
                    this.pace = Vector3.ClampMagnitude(move, 1f).magnitude * pace;
                    if (distance < stats.reach + 0.2f && Time.time > nextAttack && Time.time > flinchUntil && TakeSlot()) Enter(State.Windup);
                    break;
                }

                case State.Windup: // rear back: eyes flare, claws up, a snarl. The player's moment to parry.
                {
                    float k = stateTime / stats.windup;
                    Face(toward, dt * 8f);
                    PoseArms(Mathf.Lerp(0f, -150f, Mathf.SmoothStep(0f, 1f, k)), k * 15f);
                    EyeGlow(1f + 2f * k);
                    visual.localRotation = Quaternion.Euler(-12f * k, 0f, 0f);
                    if (stateTime >= stats.windup) Enter(State.Strike);
                    break;
                }

                case State.Strike: // lunge and rake
                {
                    float k = Mathf.Clamp01(stateTime / 0.18f);
                    PoseArms(Mathf.Lerp(-150f, 40f, k), 0f);
                    visual.localRotation = Quaternion.Euler(Mathf.Lerp(-12f, 25f, k), 0f, 0f);
                    if (stateTime < 0.18f && distance > Personal + 0.1f) transform.position += toward * 2f * dt; // (lunges in, not through the headset)
                    else
                    {
                        ResolveStrike(player, distance);
                        Enter(State.Recover);
                    }
                    break;
                }

                case State.Recover:
                    PoseArms(Mathf.Lerp(40f, 0f, stateTime / 0.6f), 0f);
                    visual.localRotation = Quaternion.Slerp(visual.localRotation, Quaternion.identity, dt * 6f);
                    EyeGlow(1f);
                    if (stateTime > (Reeling ? 1.4f : 0.6f)) Enter(State.Stalk);
                    break;

                case State.Dying:
                    visual.localScale = Vector3.one * Mathf.Max(0.01f, 1f - stateTime / 0.8f);
                    visual.localPosition = Vector3.down * stats.height * 0.4f * stateTime;
                    if (Random.value < 0.8f) CombatFx.SmokePuff(transform.position + Vector3.up * stats.height * 0.5f, 0.3f * stats.height, 2, 0.6f);
                    if (stateTime > 0.8f) Destroy(gameObject);
                    return;
            }

            // Knockback slides off; reeling makes it sway. Its feet follow the floor's rise and fall, and it keeps out of
            // the player's face (pushed back if they walk into it).
            transform.position += knock * dt;
            knock = Vector3.Lerp(knock, Vector3.zero, 1f - Mathf.Exp(-dt * 6f));
            Vector3 off = new Vector3(transform.position.x - player.x, 0f, transform.position.z - player.z);
            if (target != null && off.magnitude < Personal)
                transform.position = player + (off.sqrMagnitude > 1e-6f ? off.normalized : -transform.forward) * Personal;
            floorY = Mathf.Lerp(floorY, KalakhFx.Ground(transform.position, floorY), 1f - Mathf.Exp(-dt * 15f));
            transform.position = new Vector3(transform.position.x, floorY, transform.position.z);
            if (state == State.Stalk || state == State.Recover)
            {
                float sway = Reeling ? Mathf.Sin(Time.time * 9f) * 10f : 0f;
                float bob = Mathf.Sin(Time.time * 3f + orbit) * 0.03f;
                float lean = 8f;
                if (armL == null && animator == null)
                {
                    // A model with no animations of its own: it lurches as it walks, rocking from foot to foot, each
                    // step lifting it, and hunches lower the faster it comes.
                    gait = Mathf.MoveTowards(gait, Mathf.Clamp01(pace / Mathf.Max(stats.speed, 0.01f)), dt * 3f);
                    stride += dt * (2.5f + 4f * gait) * (stats.speed / 1.4f + 0.4f);
                    sway += Mathf.Sin(stride) * 7f * gait;
                    bob += Mathf.Abs(Mathf.Cos(stride)) * 0.05f * stats.height * gait;
                    lean += 10f * gait;
                }
                visual.localPosition = Vector3.up * bob;
                visual.localRotation = Quaternion.Slerp(visual.localRotation, Quaternion.Euler(lean, Mathf.Sin(stride * 0.5f) * 6f * gait, sway), dt * 8f);
                if (state == State.Stalk) PoseArms(Mathf.Sin(Time.time * 4f + orbit) * 15f, 0f);
            }
            if (Random.value < dt * 25f) CombatFx.SmokePuff(transform.position + Vector3.up * Random.Range(0f, stats.height * 0.6f), 0.15f * stats.height, 1, 0.3f);
            Flash();
        }

        void Enter(State s)
        {
            if (state == State.Windup && s != State.Strike) ReleaseSlot();
            if (state == State.Strike) ReleaseSlot();
            state = s;
            stateTime = 0f;
            pace = 0f; // (the walk eases off; Stalk sets it again)
            if (s == State.Windup)
            {
                CombatAudio.Play(CombatAudio.Growl, transform.position + Vector3.up * stats.height, 0.8f, 0.3f);
                Trigger("Attack");
            }
            if (s == State.Recover) nextAttack = Time.time + Random.Range(1.4f, 3f);
        }

        // The claws come down: parried, or they land if the player is still within reach.
        void ResolveStrike(Vector3 player, float distance)
        {
            Vector3 claws = transform.position + Vector3.up * stats.height * 0.6f + transform.forward * 0.4f;
            Vector3 chest = target != null ? target.position - Vector3.up * 0.4f : player;
            if (WeaverSword.Instance != null && WeaverSword.Instance.TryParry(claws, chest))
            {
                Stagger(1.6f);
                knock = -transform.forward * 2.5f;
                return;
            }
            if (distance > stats.reach + 0.45f) return; // stepped back: a miss
            if (RiftPlayer.Instance != null) RiftPlayer.Instance.TakeDamage(stats.damage, claws);
        }

        /// <summary>Knocked off balance for `seconds`: it can't attack, sways, and takes Riposte damage.</summary>
        public void Stagger(float seconds)
        {
            if (Dead) return;
            reelUntil = Time.time + seconds;
            flinchUntil = reelUntil;
            if (state == State.Windup || state == State.Strike) Enter(State.Recover);
            nextAttack = Mathf.Max(nextAttack, reelUntil + 0.5f);
        }

        public void TakeHit(HitInfo hit)
        {
            if (Dead || state == State.Rising && stateTime < 0.5f) return;
            Health -= hit.Damage;
            flashUntil = Time.time + 0.12f;
            Vector3 push = hit.Direction;
            push.y = 0f;
            float weight = Type == Kind.Brute ? 0.3f : Type == Kind.Lurker ? 1.5f : 1f;
            knock += push.normalized * Mathf.Min(4f, hit.Damage * 0.08f) * weight;
            CombatFx.SmokePuff(hit.Point, 0.1f, 8, 0.2f);
            Trigger("Hit");
            // A Brute shrugs off ordinary cuts; anything else flinches (an interrupted wind-up is a small win).
            bool heavy = hit.Damage >= 40f;
            if (Type != Kind.Brute || heavy)
            {
                flinchUntil = Time.time + 0.35f;
                if (state == State.Windup) Enter(State.Recover);
            }
            if (Health <= 0f) Die(hit);
        }

        void Die(HitInfo hit)
        {
            Dead = true;
            Alive.Remove(this);
            ReleaseSlot();
            foreach (var c in GetComponents<Collider>()) c.enabled = false;
            Trigger("Die");
            state = State.Dying;
            stateTime = 0f;
            Vector3 centre = transform.position + Vector3.up * stats.height * 0.5f;
            CombatFx.SmokePuff(centre, 0.4f * stats.height, 40, 0.8f);
            for (int i = 0; i < 25; i++) // the light it had eaten, set free
                StoryFx.Emit(CombatFx.Sparks, centre + Random.insideUnitSphere * 0.3f, Random.insideUnitSphere * 1.2f + Vector3.up * 1.5f,
                             Random.Range(0.015f, 0.035f), Random.Range(0.6f, 1.2f), new Color(1f, 0.85f, 0.5f));
            CombatAudio.Play(CombatAudio.Death, centre, 0.8f, 0.2f);
            OnDied?.Invoke(this);
        }

        bool TakeSlot()
        {
            if (attacking >= MaxAttackers) return false;
            attacking++;
            attackSlot = true;
            return true;
        }

        void ReleaseSlot()
        {
            if (!attackSlot) return;
            attackSlot = false;
            attacking = Mathf.Max(0, attacking - 1);
        }

        // ---------------------------------------------------------------- looks

        float flashUntil;

        void Face(Vector3 dir, float sharpness)
        {
            if (dir.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Mathf.Clamp01(sharpness));
        }

        void PoseArms(float raise, float spread)
        {
            if (armL == null) return;
            armL.localEulerAngles = new Vector3(raise, 0f, -18f - spread);
            armR.localEulerAngles = new Vector3(raise, 0f, 18f + spread);
        }

        void EyeGlow(float level)
        {
            if (eyeL == null) return;
            float s = 0.045f * Mathf.Lerp(1f, 1.6f, Mathf.InverseLerp(1f, 3f, level));
            eyeL.localScale = eyeR.localScale = Vector3.one * s;
        }

        // A flash of gold where the light cut it.
        void Flash()
        {
            bool on = Time.time < flashUntil;
            if (!on && !flashing) return; // (nothing to change)
            flashing = on;
            foreach (Renderer r in body)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                if (on) block.SetColor(BaseColor, new Color(1f, 0.75f, 0.35f));
                else block.Clear();
                r.SetPropertyBlock(block);
            }
        }

        static readonly int SpeedParam = Animator.StringToHash("Speed");

        void SetSpeed(float speed)
        {
            if (animator != null && hasSpeed) animator.SetFloat(SpeedParam, speed);
        }

        void Trigger(string name)
        {
            if (animator == null) return;
            foreach (var p in animator.parameters)
                if (p.name == name && p.type == AnimatorControllerParameterType.Trigger) animator.SetTrigger(name);
        }

        /// <summary>Pushes it away (it slides to a stop).</summary>
        public void Shove(Vector3 velocity) => knock += velocity;

        /// <summary>Debug: falls apart at once.</summary>
        public void Kill()
        {
            if (Dead) return;
            Health = 0f;
            Die(new HitInfo { Damage = 9999f, Point = transform.position + Vector3.up, Direction = -transform.forward });
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() // play mode without a domain reload
        {
            Alive.Clear();
            attacking = 0;
        }
    }
}
