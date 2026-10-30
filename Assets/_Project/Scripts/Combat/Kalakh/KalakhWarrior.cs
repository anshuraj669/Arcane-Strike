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
    /// <summary>
    /// A Kalakh warrior of the monster world: a soldier in one of the waves, or the Warlord (KalakhHunt). It fights
    /// with the techniques it knows (<see cref="KalakhMoves"/>: everything up to its <see cref="Tier"/>), choosing by
    /// distance, what it hasn't used lately and a little temper: claw strikes and combinations up close; lunges, leaps
    /// and charges to close the gap; lava, bone and rock thrown from afar; eruptions under the player's feet; fire
    /// breath, roars, grabs, feints, shadow-steps behind them. Every blow is told by its wind-up (a blow glowing red
    /// can't be parried), and each has a way out: duck the high ones, jump or step back from the low ones, parry or
    /// lean aside from the rest. It defends too: it raises a guard against plain slashes (thrusts, smashes and rising
    /// cuts break it), sidesteps and backsteps out of swings, and counters. Hit, it flinches the way the blade pushed it;
    /// parried or guard-broken, it reels (a Riposte; a Thrust then can Execute it once it's weak). It never stands still:
    /// it breathes, prowls round the player, keeps its eyes on them, and its tail never rests.
    /// </summary>
    public class KalakhWarrior : MonoBehaviour, IHittable, IReeling
    {
        public static readonly List<KalakhWarrior> Alive = new List<KalakhWarrior>();
        static KalakhWarrior attacker; // one of them strikes at a time

        public string Title { get; private set; } = "Kalakh";
        public int Tier { get; private set; } = 1;
        public float Health { get; private set; }
        public float MaxHealth { get; private set; }
        public float HealthFraction => MaxHealth > 0f ? Health / MaxHealth : 0f;
        public bool Dead { get; private set; }
        public bool Reeling => Time.time < reelUntil;
        public bool Active { get; private set; }
        public bool IsBoss { get; private set; }
        public KalakhBody Body { get; private set; }
        public event Action<KalakhWarrior> OnDied;
        /// <summary>How each of its blows (or tricks) came out: "parry", "avoid", "hit", "miss", "deflect",
        /// "guardbreak", "execution" (the practice with Ember listens).</summary>
        public event Action<string> OnResolved;
        /// <summary>A practice shadow: it only moves when told to (<see cref="Demo"/>), never dies (it renews), and
        /// guards or dodges only when <see cref="AllowDefence"/>.</summary>
        public bool Training { get; private set; }
        public bool AllowDefence { get; set; }
        /// <summary>In the middle of something (a technique, a guard, reeling).</summary>
        public bool Busy => busy;

        internal void Resolved(string outcome) => OnResolved?.Invoke(outcome);

        Transform target, visual;
        float height, damageScale = 1f, speedScale = 1f, reelUntil, guardUntil, berserkUntil, lastDodge;
        readonly Dictionary<KalakhMove, float> readyAt = new Dictionary<KalakhMove, float>();
        List<KalakhMove> known;
        readonly List<KalakhMove> history = new List<KalakhMove>(); // its last few moves, most recent first
        List<Strike> blows;                                       // every single blow it knows (for improvised chains)
        float temper;                                             // how aggressive it is feeling right now (drifts)
        float drift;                                              // how far in or out of reach it hangs back for now
        Coroutine brain;
        bool busy, grabbing, superArmour;
        // Each material slot of each renderer, with its own colours (tinted red, flashed on a hit).
        readonly List<(Renderer renderer, int slot, Color tint, Color glow)> looks = new List<(Renderer, int, Color, Color)>();
        // The eyes, burning: their meshes (Eye.L / Eye.R under the head bone), the flames licking up off them.
        readonly List<Transform> eyeSockets = new List<Transform>();
        readonly List<Renderer> eyeRenderers = new List<Renderer>();
        ParticleSystem eyeFire;
        float eyeFireDue;
        Color eyeColour = new Color(1f, 0.42f, 0.06f);
        MaterialPropertyBlock block;
        float flashUntil, redUntil;
        Light eyes;
        Transform bar, fill;
        TextMesh label;
        Vector3 knock;
        float orbit = 1f;
        readonly List<GameObject> marks = new List<GameObject>(); // warning rings on the ground while a move is under way

        T Mark<T>(T thing) where T : Component
        {
            marks.Add(thing.gameObject);
            return thing;
        }

        // Whatever it was doing is cut short: its warning rings go, and a held player is let go.
        void Interrupt()
        {
            StopAllCoroutines();
            brain = null;
            foreach (GameObject m in marks)
                if (m != null) Destroy(m);
            marks.Clear();
            if (grabbing)
            {
                grabbing = false;
                ScreenFade.Set(new Color(0f, 0f, 0f, 0f));
                if (legs != null) legs.Stunned = false; // (only the hold: a stunning blow keeps its own timer)
            }
            superArmour = false;
            visual.localScale = Vector3.one;
            if (attacker == this) attacker = null;
        }
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        // ---------------------------------------------------------------- making one

        /// <summary>A Kalakh of `height` metres at `position` (still: <see cref="Activate"/> wakes it).</summary>
        /// <param name="breed">Which kind of Kalakh (its own signature techniques: <see cref="KalakhMove.breed"/>).</param>
        /// <param name="favour">The kinds of technique it reaches for more often (how this kind fights).</param>
        /// <param name="deck">Exactly the attacks it knows (a soldier's own hand: <see cref="KalakhMoves.DealDecks"/>). Empty: by tier.</param>
        public static KalakhWarrior Create(GameObject model, string title, int tier, float health, float height, Vector3 position, Quaternion facing, bool boss,
                                           string breed = null, MoveKind[] favour = null, List<KalakhMove> deck = null)
        {
            var go = new GameObject($"Kalakh ({title})");
            go.transform.SetPositionAndRotation(position, facing);
            var k = go.AddComponent<KalakhWarrior>();
            k.Setup(model, title, tier, health, height, boss, breed, favour, deck);
            return k;
        }

        /// <summary>Which kind of Kalakh it is (null: a plain one).</summary>
        public string Breed { get; private set; }
        MoveKind[] favour;

        void Setup(GameObject model, string title, int tier, float health, float bodyHeight, bool boss, string breed, MoveKind[] favoured, List<KalakhMove> deck)
        {
            Title = title;
            Breed = string.IsNullOrEmpty(breed) ? null : breed;
            favour = favoured;
            Tier = tier;
            MaxHealth = Health = health;
            height = bodyHeight;
            IsBoss = boss;
            damageScale = boss ? 1.35f : 1f + (tier - 1) * 0.08f;
            speedScale = boss ? 0.95f : 1f + (tier - 1) * 0.04f;
            block = new MaterialPropertyBlock();
            orbit = Random.value < 0.5f ? -1f : 1f;
            known = deck != null && deck.Count > 0
                ? new List<KalakhMove>(deck)
                : KalakhMoves.All.FindAll(m => m.tier <= tier && (m.breed == null || m.breed == Breed || boss)); // (the Warlord knows every kind's tricks)
            blows = new List<Strike>();
            foreach (KalakhMove m in known)
                if (m.kind == MoveKind.Melee)
                    foreach (Strike s in m.strikes)
                        if (!s.unblockable) blows.Add(s);
            temper = Random.value;

            var collider = gameObject.AddComponent<CapsuleCollider>();
            collider.isTrigger = true; // the sword finds it; walking doesn't bump into it
            collider.height = height;
            collider.radius = height * 0.2f;
            collider.center = Vector3.up * height * 0.5f;

            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            if (model != null)
            {
                GameObject m = Instantiate(model, visual);
                m.transform.localPosition = Vector3.zero;
                m.transform.localRotation = Quaternion.identity;
                FitHeight(m.transform, height);
                FaceForward(m.transform, visual, title);
                foreach (Renderer r in m.GetComponentsInChildren<Renderer>())
                {
                    if (r.name.StartsWith("Eye."))
                    {
                        eyeRenderers.Add(r);
                        eyeSockets.Add(r.transform);
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        continue;
                    }
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        looks.Add((r, i, mats[i] != null && mats[i].HasProperty(BaseColor) ? mats[i].GetColor(BaseColor) : Color.white,
                                   mats[i] != null && mats[i].HasProperty(EmissionColor) ? mats[i].GetColor(EmissionColor) : Color.black));
                }
                foreach (var smr in m.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true; // (its bones swing well outside the rest bounds)
            }
            else MakeshiftBody(); // (no model, or a breed's prefab missing: still something to see and cut)
            Body = gameObject.AddComponent<KalakhBody>();
            Body.Setup(visual, height);
            Body.OnFootfall += Footfall;
            if (!Body.Rigged) Debug.LogWarning("[Kalakh] The model has no skeleton (run Tools/blender/rig_kalakh.py, then Build Kalakh Monster): it moves as a whole.");

            // Burning eyes: the eyes on fire, and a light at the head that flares with its anger.
            if (eyeSockets.Count == 0) MakeshiftEyes();
            eyeFire = StoryFx.CreateParticles(transform, "EyeFire", true, -0.2f, 300);
            if (boss) eyeColour = new Color(1f, 0.25f, 0.04f);
            eyes = new GameObject("Eyes").AddComponent<Light>();
            eyes.transform.SetParent(Body.Head != null ? Body.Head : transform, false);
            eyes.transform.position = eyeSockets.Count > 0 ? Midpoint(eyeSockets) : Body.Mouth + Vector3.up * height * 0.03f;
            eyes.type = LightType.Point;
            eyes.color = eyeColour;
            eyes.range = height * 0.8f;
            eyes.intensity = 1.2f;
            eyes.shadows = LightShadows.None;
            BuildBar();
            Alive.Add(this);
        }

        static Vector3 Midpoint(List<Transform> ts)
        {
            Vector3 sum = Vector3.zero;
            foreach (Transform t in ts) sum += t.position;
            return sum / ts.Count;
        }

        // No model at all: a hunched black figure of rough shapes (it moves as a whole), so it is never invisible.
        void MakeshiftBody()
        {
            Debug.LogWarning($"[Kalakh] {Title}: no model (Arcane Strike → Build Kalakh Monster): a rough stand-in.");
            var skin = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "KalakhStandIn" };
            skin.SetColor(BaseColor, new Color(0.03f, 0.025f, 0.03f));
            void Part(PrimitiveType type, Vector3 at, Vector3 size, float pitch)
            {
                var go = GameObject.CreatePrimitive(type);
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(visual, false);
                go.transform.localPosition = at * height;
                go.transform.localScale = size * height;
                go.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = skin;
                looks.Add((r, 0, new Color(0.03f, 0.025f, 0.03f), Color.black));
            }
            Part(PrimitiveType.Capsule, new Vector3(0f, 0.5f, 0f), new Vector3(0.3f, 0.3f, 0.22f), 12f);  // body
            Part(PrimitiveType.Sphere, new Vector3(0f, 0.72f, 0.04f), new Vector3(0.38f, 0.14f, 0.24f), 0f); // shoulders
            Part(PrimitiveType.Sphere, new Vector3(0f, 0.86f, 0.1f), new Vector3(0.15f, 0.14f, 0.18f), 0f);  // head
            for (int side = -1; side <= 1; side += 2)
            {
                Part(PrimitiveType.Capsule, new Vector3(side * 0.2f, 0.5f, 0.06f), new Vector3(0.07f, 0.22f, 0.07f), -15f); // arms
                Part(PrimitiveType.Capsule, new Vector3(side * 0.08f, 0.16f, 0f), new Vector3(0.09f, 0.17f, 0.09f), 0f);   // legs
            }
        }

        // A model without eye meshes: two glowing beads on the head, just above the jaw.
        void MakeshiftEyes()
        {
            Transform head = Body.Head != null ? Body.Head : transform;
            var glow = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "KalakhEyes" };
            glow.SetColor(BaseColor, eyeColour * 3f);
            for (int side = -1; side <= 1; side += 2)
            {
                var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(eye.GetComponent<Collider>());
                eye.name = side < 0 ? "Eye.R" : "Eye.L";
                eye.transform.SetParent(head, true);
                eye.transform.position = Body.Mouth + Vector3.up * height * 0.035f + transform.forward * height * 0.012f + transform.right * side * height * 0.032f;
                eye.transform.localScale = Vector3.one * height * 0.018f / Mathf.Max(head.lossyScale.x, 1e-4f);
                var r = eye.GetComponent<Renderer>();
                r.sharedMaterial = glow;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                eyeSockets.Add(eye.transform);
                eyeRenderers.Add(r);
            }
        }

        // Turns the model so its face looks along the Kalakh's forward, whichever way the model was built facing:
        // its eyes are in front of its head joint (or, without eyes, its head is out in front of its hips).
        static void FaceForward(Transform model, Transform visual, string title)
        {
            Transform headBone = null, hips = null;
            Vector3 eyes = Vector3.zero;
            int eyeCount = 0;
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Head") headBone = t;
                else if (t.name == "Hips") hips = t;
            }
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("Eye."))
                {
                    eyes += r.bounds.center;
                    eyeCount++;
                }
            Vector3 from, to;
            if (headBone != null && eyeCount > 0)
            {
                from = headBone.position;
                to = eyes / eyeCount;
            }
            else if (headBone != null && hips != null)
            {
                from = hips.position;
                to = headBone.position;
            }
            else return;
            Vector3 face = visual.InverseTransformDirection(to - from);
            face.y = 0f;
            if (face.magnitude < 0.03f) return; // (too little to tell by)
            // Only a model built facing backwards is turned (a clear reading, not a sideways guess).
            if (Mathf.Abs(Vector3.SignedAngle(face, Vector3.forward, Vector3.up)) < 135f) return;
            const float turn = 180f;
            model.RotateAround(visual.position, visual.up, turn);
            Debug.Log($"[Kalakh] {title}: the model faced the wrong way, turned {turn:F0}° to face its foe");
        }

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

        // A bar of health with its name, floating over its head and turned to the player.
        void BuildBar()
        {
            bar = new GameObject("HealthBar").transform;
            bar.SetParent(transform, false);
            bar.localPosition = Vector3.up * (height + 0.35f);
            var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(back.GetComponent<Collider>());
            back.transform.SetParent(bar, false);
            back.transform.localScale = new Vector3(0.9f, 0.07f, 1f);
            back.GetComponent<Renderer>().sharedMaterial = AegisShield.CreateFaceMaterial(new Color(0.05f, 0.02f, 0.04f, 0.75f));
            var front = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(front.GetComponent<Collider>());
            front.transform.SetParent(bar, false);
            front.transform.localPosition = new Vector3(0f, 0f, -0.005f);
            front.GetComponent<Renderer>().sharedMaterial = AegisShield.CreateFaceMaterial(IsBoss ? new Color(1f, 0.25f, 0.15f, 0.95f) : new Color(0.75f, 0.35f, 1f, 0.95f));
            fill = front.transform;
            var text = new GameObject("Name");
            text.transform.SetParent(bar, false);
            text.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            label = text.AddComponent<TextMesh>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
            label.text = Title.ToUpperInvariant();
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 48;
            label.characterSize = IsBoss ? 0.008f : 0.006f;
            label.color = IsBoss ? new Color(1f, 0.5f, 0.35f) : new Color(0.85f, 0.7f, 1f);
            bar.gameObject.SetActive(false);
        }

        /// <summary>A practice shadow facing `player`: ghostly pale, still until <see cref="Demo"/>.</summary>
        public void ActivateTraining(Transform player)
        {
            target = player;
            Body.LookTarget = player;
            Active = true;
            Training = true;
            label.text = "PRACTICE SHADOW";
            bar.gameObject.SetActive(true);
            for (int i = 0; i < looks.Count; i++) looks[i] = (looks[i].renderer, looks[i].slot, new Color(0.55f, 0.75f, 1f, 1f), new Color(0.05f, 0.12f, 0.3f));
            eyeColour = new Color(0.45f, 0.75f, 1f);
            if (eyes != null) eyes.color = eyeColour;
        }

        /// <summary>Performs one technique by name (practice), then stands ready again. Start it on the shadow itself
        /// (StartCoroutine on it) so a parry or stagger can cut it short, and wait for <see cref="Busy"/> to clear.</summary>
        public IEnumerator Demo(string moveName)
        {
            KalakhMove move = KalakhMoves.All.Find(m => m.name == moveName);
            if (move == null || Dead) yield break;
            while (busy) yield return null;
            busy = true;
            attacker = this;
            if (move.kind == MoveKind.Melee || move.kind == MoveKind.Grab)
                for (float t = 0f; t < 2.5f && Distance > 1.4f && !Dead; t += Time.deltaTime) // step in to striking range
                {
                    Step(Toward * 1.5f * Time.deltaTime);
                    Face(Toward, Time.deltaTime * 8f);
                    Body.Speed = 1.5f;
                    yield return null;
                }
            Body.Speed = 0f;
            yield return Perform(move);
            busy = false;
            if (attacker == this) attacker = null;
            Body.Relax(0.4f);
        }

        /// <summary>Raises its guard for a while (practice: break it).</summary>
        public void HoldGuard(float seconds)
        {
            Body.Act(KalakhMoves.GuardCross(), 0.15f, true);
            guardUntil = Time.time + seconds;
        }

        /// <summary>Wakes it to hunt `player` (the head).</summary>
        public void Activate(Transform player)
        {
            target = player;
            Body.LookTarget = player;
            Active = true;
            bar.gameObject.SetActive(true);
            foreach (KalakhMove m in known) readyAt[m] = Time.time + Random.Range(0.5f, 3f);
            brain = StartCoroutine(Brain());
        }

        void OnDestroy()
        {
            Alive.Remove(this);
            if (attacker == this) attacker = null;
            if (grabbing) // destroyed with the player in its grip: let go (the red hold tint and the stun)
            {
                grabbing = false;
                ScreenFade.Set(new Color(0f, 0f, 0f, 0f));
                if (legs != null) legs.Stunned = false;
            }
        }

        // ---------------------------------------------------------------- the fight

        Vector3 Player => target != null ? new Vector3(target.position.x, transform.position.y, target.position.z) : transform.position + transform.forward;
        float Distance => Vector3.Distance(Player, transform.position);
        Vector3 Toward => StoryFx.Flat(Player - transform.position);

        // Nothing it does follows a script: how long it prowls, which technique, which hand, how long the tell, whether
        // it strings more blows on, fakes one, or rushes straight back in all come out differently every time, swayed by
        // its temper (which drifts: sometimes it presses, sometimes it holds off and circles).
        IEnumerator Brain()
        {
            yield return Roar(Random.Range(0.4f, 0.8f), false);
            while (!Dead)
            {
                temper = Mathf.Clamp01(temper + Random.Range(-0.25f, 0.25f));
                // Prowl: close in and circle for a while (a long while, or hardly at all), until it's its turn.
                float prowl = Random.value < 0.2f + temper * 0.25f ? Random.Range(0.05f, 0.3f) : Random.Range(0.4f, 2.4f) * (1.3f - temper * 0.6f);
                prowl /= speedScale;
                for (float t = 0f; t < prowl && !Dead; t += Time.deltaTime)
                {
                    Prowl(Time.deltaTime);
                    yield return null;
                }
                if (Dead) yield break;
                if (attacker != null && attacker != this && !attacker.Dead) continue; // the other one is attacking
                if (Crowded && !TakeTurn()) continue;
                KalakhMove move = Choose();
                if (move == null) continue;
                attacker = this;
                busy = true;
                readyAt[move] = Time.time + move.cooldown * Random.Range(0.6f, 1.4f);
                history.Insert(0, move);
                if (history.Count > 5) history.RemoveAt(5);
                yield return Perform(move);
                busy = false;
                if (attacker == this) attacker = null;
                if (turn == this) turn = null;
                Body.Relax(Random.Range(0.25f, 0.5f));
                yield return Wait(move.recover * Random.Range(0.5f, 1.5f) * (1.2f - temper * 0.5f) / speedScale);
            }
        }

        // A pack: the others hang back in a ring and watch for an opening, and one at a time, chosen at random, comes in.
        static KalakhWarrior turn;
        static float turnUntil;
        static bool Crowded => Alive.Count > 2;

        bool TakeTurn()
        {
            if (turn == this && Time.time < turnUntil) return true;
            if (turn != null && turn != this && !turn.Dead && Time.time < turnUntil) return false;
            turn = null;
            var ready = Alive.FindAll(k => k.Active && !k.Dead && !k.Reeling);
            if (ready.Count == 0) return false;
            turn = ready[Random.Range(0, ready.Count)];
            turnUntil = Time.time + Random.Range(3f, 5.5f); // long enough to walk in; then someone else has a go
            return turn == this;
        }

        // A random pick among everything that fits the distance: what it used lately is much less likely, and each
        // choice gets a roll of the dice on top (so even the odds shift from one attack to the next).
        readonly List<(KalakhMove move, float weight)> options = new List<(KalakhMove move, float weight)>();

        KalakhMove Choose()
        {
            float d = Distance, total = 0f;
            options.Clear();
            foreach (KalakhMove m in known)
            {
                if (Time.time < readyAt[m] || d < m.minRange || d > m.maxRange) continue;
                if (m.kind == MoveKind.Guard || m.kind == MoveKind.Retreat && m.then == null) continue; // (those are reactions)
                if (m.kind == MoveKind.Berserk && (Time.time < berserkUntil || HealthFraction > 0.6f)) continue;
                int ago = history.IndexOf(m);
                float recent = ago < 0 ? 1f : ago switch { 0 => 0.05f, 1 => 0.25f, 2 => 0.45f, 3 => 0.65f, _ => 0.85f };
                float w = m.weight * (m.tier == Tier ? 1.4f : 1f) * recent * Random.Range(0.4f, 1.6f);
                if (history.Count > 0 && history[0].kind == m.kind) w *= 0.5f; // and rarely the same kind twice running
                if (m.breed != null) w *= 1.6f; // its own techniques
                if (favour != null && System.Array.IndexOf(favour, m.kind) >= 0) w *= 2f; // the way its kind fights
                options.Add((m, w));
                total += w;
            }
            if (options.Count == 0) return null;
            float pick = Random.value * total;
            foreach (var (m, w) in options)
            {
                pick -= w;
                if (pick <= 0f) return m;
            }
            return options[options.Count - 1].move;
        }

        // A blow as it will actually be thrown this time: either hand, a quicker or slower tell, a longer or shorter step in.
        Strike Vary(Strike s)
        {
            bool flip = !s.fromTail && Random.value < 0.4f;
            return new Strike
            {
                wind = flip ? s.wind.Mirrored() : s.wind,
                hit = flip ? s.hit.Mirrored() : s.hit,
                windTime = Mathf.Max(MinTell, s.windTime * Random.Range(0.7f, 1.3f)),
                hitTime = s.hitTime * Random.Range(0.85f, 1.15f),
                hold = s.hold * Random.Range(0.5f, 1.5f),
                lunge = s.lunge * Random.Range(0.6f, 1.4f),
                damage = s.damage, shape = s.shape, range = s.range, angle = s.angle, reach = s.reach,
                unblockable = s.unblockable, effect = s.effect, fromTail = s.fromTail, heavy = s.heavy,
            };
        }

        // A melee technique, improvised: its blows (varied), sometimes one swapped for another it knows, sometimes a
        // fake tell first, and sometimes more blows strung on the end while the player is still in reach.
        IEnumerator Melee(KalakhMove move)
        {
            var plan = new List<Strike>();
            foreach (Strike s in move.strikes)
                plan.Add(Random.value < 0.15f && blows.Count > 0 ? blows[Random.Range(0, blows.Count)] : s);
            int extra = 0;
            while (extra < 2 && Random.value < 0.12f + Tier * 0.06f + temper * 0.1f && blows.Count > 0)
            {
                plan.Add(blows[Random.Range(0, blows.Count)]);
                extra++;
            }
            for (int i = 0; i < plan.Count && !Dead; i++)
            {
                if (i > 0 && plan.Count > move.strikes.Count && i >= move.strikes.Count && Distance > 2.6f) break; // they got away
                Strike s = Vary(plan[i]);
                if (Random.value < 0.06f * Tier) // a fake: the tell... then something else
                {
                    Body.Act(s.wind, s.windTime * 0.5f);
                    yield return Wait(s.windTime * Random.Range(0.4f, 0.7f));
                    Body.Act(KalakhBody.Idle(), 0.15f);
                    yield return Wait(Random.Range(0.1f, 0.35f));
                    s = Vary(blows.Count > 0 ? blows[Random.Range(0, blows.Count)] : plan[i]);
                    s.windTime = Mathf.Max(MinTell, s.windTime * 0.6f);
                }
                yield return Blow(s);
            }
        }

        void Prowl(float dt)
        {
            float d = Distance;
            float want = 1.6f + (Tier >= 3 ? 0.4f : 0f) + drift;
            if (Crowded && turn != this) want = 4.2f + Mathf.Abs(drift) * 0.6f; // waiting its turn
            Vector3 move = Vector3.zero;
            if (d > want + 0.4f) move += Toward;
            else if (d < want - 0.5f) move -= Toward * 0.7f;
            else move += Vector3.Cross(Vector3.up, Toward) * orbit * 0.55f; // circling
            if (Random.value < dt * (0.3f + temper * 0.6f)) orbit = -orbit; // changes direction without warning
            if (Random.value < dt * 0.4f) drift = Random.Range(-0.5f, 1.4f) * (1.2f - temper); // drifts in and out of reach
            foreach (KalakhWarrior other in Alive)
            {
                if (other == this || other.Dead) continue;
                Vector3 away = StoryFx.Flat(transform.position - other.transform.position) * 1f;
                float gap = Vector3.Distance(transform.position, other.transform.position);
                if (gap < 3f) move += away * (3f - gap); // keep apart: one at a time
            }
            float speed = 1.5f * speedScale * (Time.time < berserkUntil ? 1.4f : 1f);
            Step(Vector3.ClampMagnitude(move, 1f) * speed * dt);
            if (edged && Random.value < dt * 4f) orbit = -orbit; // at the lava's edge: round the other way
            Face(Toward, dt * 6f);
            Body.Speed = Vector3.ClampMagnitude(move, 1f).magnitude * speed;
        }

        /// <summary>The closest it comes to the player's eyes (centre to centre, across the ground): a blow lunges in to
        /// this, never through the headset. Bigger ones keep a little further off.</summary>
        float Personal => 0.5f + height * 0.17f;
        bool edged; // the last step stopped at lava or a drop
        static readonly RaycastHit[] blockers = new RaycastHit[8];

        // Moves over the ground (following its rise and fall): round rock it can't climb, never into lava or off the
        // ledge (it stops at the edge), and never into the player.
        void Step(Vector3 delta)
        {
            Vector3 here = transform.position, move = delta + knock * Time.deltaTime;
            move.y = 0f;
            Vector3 p = KeepClear(here + Unblocked(here, move));
            edged = false;
            if (!KalakhFx.Footing(p, here.y, out float ground))
            {
                if (KalakhFx.Footing(here, here.y, out ground))
                {
                    p = here; // the edge: it won't go
                    knock = Vector3.zero;
                    edged = true;
                }
                else ground = KalakhFx.Ground(p, here.y); // (already off solid rock: let it find its way back)
            }
            else if (move.sqrMagnitude > 1e-8f)
            {
                // Feet on uneven rock: settle towards the lower of the ground under its front and back feet, so neither hangs in the air.
                Vector3 along = transform.forward * height * 0.12f;
                if (KalakhFx.Footing(p + along, ground, out float front) && KalakhFx.Footing(p - along, ground, out float back))
                    ground = Mathf.Lerp(ground, Mathf.Min(ground, Mathf.Min(front, back)), 0.6f);
            }
            p.y = Mathf.Lerp(here.y, ground, 1f - Mathf.Exp(-Time.deltaTime * 20f));
            transform.position = p;
        }

        // Slides along rock too steep to walk up (a boulder, a cliff face) instead of passing through it.
        Vector3 Unblocked(Vector3 here, Vector3 move)
        {
            float length = move.magnitude;
            if (length < 1e-5f) return move;
            Vector3 dir = move / length, from = here + Vector3.up * Mathf.Min(0.6f, height * 0.3f);
            int n = Physics.RaycastNonAlloc(from, dir, blockers, length + height * 0.2f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (blockers[i].normal.y > 0.6f || !KalakhFx.IsWorld(blockers[i].collider)) continue; // (ground it can walk up)
                Vector3 wall = new Vector3(blockers[i].normal.x, 0f, blockers[i].normal.z).normalized;
                float into = Vector3.Dot(move, wall);
                return into < 0f ? move - wall * into : move;
            }
            return move;
        }

        // A point pushed back out of the player's personal space.
        Vector3 KeepClear(Vector3 p)
        {
            if (target == null || Dead) return p;
            Vector3 off = new Vector3(p.x - target.position.x, 0f, p.z - target.position.z);
            float d = off.magnitude, room = Personal;
            if (d >= room) return p;
            Vector3 away = d > 1e-3f ? off / d : -transform.forward;
            return new Vector3(target.position.x, p.y, target.position.z) + away * room;
        }

        void Update()
        {
            knock = Vector3.Lerp(knock, Vector3.zero, 1f - Mathf.Exp(-Time.deltaTime * 5f));
            if (!busy && Active && !Dead && knock.sqrMagnitude > 0.01f) Step(Vector3.zero);
            // The player walked into it: it gives way (not through the headset), unless that would take it into the lava.
            if (Active && !Dead && target != null)
            {
                Vector3 here = transform.position, clear = KeepClear(here);
                if ((clear - here).sqrMagnitude > 1e-6f && KalakhFx.Footing(clear, here.y, out _))
                    transform.position = Vector3.MoveTowards(here, clear, Time.deltaTime * 4f);
            }
            UpdateLooks();
        }

        // Dust kicked up where a foot comes down; the big ones shake the ground when they're close.
        void Footfall(bool right)
        {
            if (Dead || !Active) return;
            Vector3 foot = Body.Foot(right);
            foot.y = transform.position.y + 0.05f;
            for (int i = 0; i < 2; i++)
                StoryFx.Emit(CombatFx.Smoke, foot + Random.insideUnitSphere * 0.1f, new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0.1f, 0.3f), Random.Range(-0.3f, 0.3f)),
                             Random.Range(0.12f, 0.25f) * height * 0.5f, Random.Range(0.6f, 1f), new Color(0.32f, 0.27f, 0.25f, 0.45f));
            if (height > 2.5f && Distance < 5f) CombatFx.Rumble(null, height > 3f ? 0.35f : 0.2f, 0.06f);
        }

        void Face(Vector3 dir, float sharpness)
        {
            if (dir.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Mathf.Clamp01(sharpness));
        }

        static IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }

        // ---------------------------------------------------------------- carrying out a move

        IEnumerator Perform(KalakhMove move)
        {
            Body.Speed = 0f;
            Debug.Log($"[Kalakh] {Title}: {move.name}");
            switch (move.kind)
            {
                case MoveKind.Melee: yield return Melee(move); break;
                case MoveKind.Leap: yield return Leap(move); break;
                case MoveKind.Charge: yield return Charge(move); break;
                case MoveKind.Projectile: yield return Throw(move); break;
                case MoveKind.Eruption: yield return Eruption(move); break;
                case MoveKind.Breath: yield return Breath(move); break;
                case MoveKind.Roar: yield return RoarMove(move); break;
                case MoveKind.Grab: yield return Grab(move); break;
                case MoveKind.Spin: yield return Spin(move); break;
                case MoveKind.ShadowStep: yield return ShadowStep(); break;
                case MoveKind.Feint: yield return Feint(move); break;
                case MoveKind.Retreat: yield return Retreat(move.radius); break;
                case MoveKind.Berserk: yield return Berserk(move); break;
            }
            if (move.then != null && !Dead && Random.value < 0.8f) yield return Perform(move.then);
        }

        /// <summary>The shortest tell a blow gets (s): long enough to read and react to in a headset.</summary>
        const float MinTell = 0.42f;

        // The tell: the wind-up pose, a growl, the eyes flaring, a glint at the claws about to strike (red all over: it
        // can't be parried).
        IEnumerator WindUp(Strike s)
        {
            float wind = Mathf.Max(MinTell, s.windTime / speedScale);
            Body.Act(s.wind, wind * 0.8f);
            CombatAudio.Play(CombatAudio.Growl, Body.Mouth, s.heavy ? 1f : 0.7f, 0.3f);
            if (s.unblockable)
            {
                redUntil = Time.time + s.windTime + s.hitTime;
                CombatFx.Popup("!", Body.Mouth + Vector3.up * 0.4f, KalakhFx.Danger, 1.4f);
            }
            bool glint = false;
            for (float t = 0f; t < wind && !Dead; t += Time.deltaTime)
            {
                eyes.intensity = 1.2f + 4f * (t / wind);
                Face(Toward, Time.deltaTime * 5f);
                if (!glint && t > wind * 0.45f)
                {
                    glint = true;
                    Vector3 claws = s.fromTail ? Body.TailTip : Body.HandR.y >= Body.HandL.y ? Body.HandR : Body.HandL; // (the raised one)
                    StoryFx.Burst(CombatFx.Sparks, claws, 10, 1.2f, 0.035f, 0.3f, s.unblockable ? KalakhFx.Danger : eyeColour);
                }
                yield return null;
            }
        }

        IEnumerator Blow(Strike s)
        {
            superArmour = s.unblockable || IsBoss && s.heavy; // committed: hits won't stop it
            yield return WindUp(s);
            if (Dead) yield break;
            CombatAudio.Play(s.heavy ? CombatAudio.HeavySwoosh : CombatAudio.Swoosh, Body.Chest, 0.8f, 0.15f);
            Body.Act(s.hit, s.hitTime, true);
            float strikeTime = Mathf.Max(0.08f, s.hitTime);
            for (float t = 0f; t < strikeTime && !Dead; t += Time.deltaTime)
            {
                if (s.lunge > 0f && Distance > Personal + 0.1f) Step(transform.forward * s.lunge / strikeTime * Time.deltaTime);
                yield return null;
            }
            if (Dead) yield break;
            Land(s, s.shape == HitShape.Circle ? transform.position : Body.Chest);
            superArmour = false;
            eyes.intensity = 1.2f;
            yield return Wait(s.hold);
        }

        // Does the blow land? (the way out: duck, jump, lean aside, parry, or just not be there)
        void Land(Strike s, Vector3 from)
        {
            if (target == null) return;
            if (s.heavy)
            {
                CombatFx.Rumble(null, 0.5f, 0.15f);
                if (s.shape == HitShape.Circle) KalakhFx.Shockwave(this, transform.position, s.range, s.effect == HitEffect.Burn ? KalakhFx.Fire : new Color(0.8f, 0.6f, 0.5f));
                CombatFx.SmokePuff(transform.position + transform.forward * 0.6f, 0.4f, 10, 0.6f);
            }
            if (!InShape(s))
            {
                Resolved("miss");
                return;
            }
            Vector3 claws = s.fromTail ? Body.TailTip : Vector3.Lerp(Body.HandR, Body.HandL, 0.5f);
            // The blade has to be where the blow comes in: high at the head, low across the legs, or at the chest.
            float rise = s.reach == Reach.High ? 0.75f : s.reach == Reach.Low ? 0.3f : 0.6f;
            if (s.shape != HitShape.Circle) claws = transform.position + Vector3.up * height * rise + transform.forward * 0.5f;
            Vector3 chest = target.position - Vector3.up * (s.reach == Reach.High ? 0.15f : s.reach == Reach.Low ? 0.85f : 0.4f);
            if (!s.unblockable && s.shape != HitShape.Circle && WeaverSword.Instance != null && WeaverSword.Instance.TryParry(claws, chest))
            {
                Parried();
                Resolved("parry");
                return;
            }
            if (Avoided(s.reach))
            {
                CombatFx.Popup(s.reach == Reach.High ? "DUCKED" : s.reach == Reach.Low ? "JUMPED" : "DODGED", target.position + StoryFx.Flat(target.forward) * 0.8f, new Color(0.7f, 0.9f, 1f), 0.7f);
                Resolved("avoid");
                return;
            }
            Resolved("hit");
            if (Training) CombatFx.Popup("HIT!", target.position + StoryFx.Flat(target.forward) * 0.7f, KalakhFx.Danger, 0.8f);
            HurtPlayer(s.damage * damageScale * (Time.time < berserkUntil ? 1.3f : 1f), claws, Toward, s.effect, s.reach, this);
        }

        bool InShape(Strike s)
        {
            Vector3 rel = Player - transform.position;
            float d = rel.magnitude, reach = s.range * (height / 2.2f) * 0.5f + s.range * 0.5f; // bigger ones reach further
            switch (s.shape)
            {
                case HitShape.Circle: return d < reach + 0.25f;
                case HitShape.Line:
                {
                    float along = Vector3.Dot(rel, transform.forward), across = Mathf.Abs(Vector3.Dot(rel, transform.right));
                    return along > -0.2f && along < reach + 0.25f && across < s.angle * 0.5f + 0.3f;
                }
                default:
                    return d < reach + 0.25f && Vector3.Angle(transform.forward, rel) < s.angle * 0.5f + 15f;
            }
        }

        static DodgeDetector dodger;
        static PlayerLocomotion legs;

        // Ducking under the high ones, jumping the low ones, leaning or stepping aside from the rest.
        static bool Avoided(Reach reach)
        {
            if (dodger == null) dodger = FindAnyObjectByType<DodgeDetector>();
            if (legs == null) legs = FindAnyObjectByType<PlayerLocomotion>();
            bool duck = dodger != null && dodger.IsDodgingFrom(DodgeType.Duck);
            bool lean = dodger != null && (dodger.IsDodgingFrom(DodgeType.Left) || dodger.IsDodgingFrom(DodgeType.Right));
            bool jump = legs != null && legs.Airborne;
            return reach switch
            {
                Reach.High => duck || lean,
                Reach.Low => jump,
                _ => lean,
            };
        }

        /// <summary>The player is struck: damage, and what the blow does to them.</summary>
        public static void HurtPlayer(float damage, Vector3 from, Vector3 push, HitEffect effect, Reach reach, MonoBehaviour by)
        {
            RiftPlayer player = RiftPlayer.Instance;
            if (player == null || player.Down) return;
            if (damage > 0f) player.TakeDamage(damage, from);
            if (legs == null) legs = FindAnyObjectByType<PlayerLocomotion>();
            switch (effect)
            {
                case HitEffect.Knockback:
                    if (legs != null) player.StartCoroutine(Shove(StoryFx.Flat(push) * 0.9f, 0.25f));
                    break;
                case HitEffect.Stun:
                    CombatFx.Popup("STUNNED", Camera.main != null ? Camera.main.transform.position + Camera.main.transform.forward * 0.9f : from, new Color(1f, 0.9f, 0.4f), 0.8f);
                    if (legs != null) player.StartCoroutine(Hold(0.9f));
                    break;
                case HitEffect.Burn:
                    player.StartCoroutine(Burn(player, 2f, damage * 0.25f));
                    break;
                case HitEffect.Slow:
                    if (legs != null) player.StartCoroutine(Slow(2.5f));
                    break;
                case HitEffect.Blind:
                    player.StartCoroutine(Blind(1.8f));
                    break;
            }
        }

        static IEnumerator Shove(Vector3 total, float seconds)
        {
            for (float t = 0f; t < seconds && legs != null; t += Time.deltaTime)
            {
                legs.Shove(total * Time.deltaTime / seconds);
                yield return null;
            }
        }

        static IEnumerator Hold(float seconds)
        {
            legs.Stunned = true;
            yield return new WaitForSeconds(seconds);
            if (legs != null) legs.Stunned = false;
        }

        static IEnumerator Slow(float seconds)
        {
            legs.SpeedScale = 0.45f;
            yield return new WaitForSeconds(seconds);
            if (legs != null) legs.SpeedScale = 1f;
        }

        static IEnumerator Burn(RiftPlayer player, float seconds, float total)
        {
            for (float t = 0f; t < seconds && player != null; t += Time.deltaTime)
            {
                player.Burn(total * Time.deltaTime / seconds);
                yield return null;
            }
        }

        static IEnumerator Blind(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                if (RiftPlayer.Instance == null || RiftPlayer.Instance.Down) yield break; // (the fall's own fade takes over)
                float k = Mathf.Sin(Mathf.Clamp01(t / seconds) * Mathf.PI);
                ScreenFade.Set(new Color(0.05f, 0.03f, 0.03f, 0.85f * k));
                yield return null;
            }
            ScreenFade.Set(new Color(0f, 0f, 0f, 0f));
        }

        void Parried()
        {
            Stagger(1.4f);
            knock = -transform.forward * 2.5f;
        }

        // ---- the special techniques

        IEnumerator Leap(KalakhMove move)
        {
            Strike s = move.strikes[0];
            Body.Act(KalakhMoves.Crouch(), s.windTime * 0.8f);
            CombatAudio.Play(CombatAudio.Growl, Body.Mouth, 0.9f, 0.2f);
            yield return Wait(s.windTime);
            Vector3 start = transform.position, land = Player - Toward * Mathf.Max(0.9f, Personal + 0.05f);
            for (int i = 0; i < 4 && !KalakhFx.Footing(land, start.y, out _); i++) land = Vector3.Lerp(land, start, 0.35f); // (not into the lava)
            land.y = KalakhFx.Ground(land, start.y);
            var ring = Mark(KalakhFx.NewRing("LandingMark", KalakhFx.Danger, 0.06f));
            KalakhFx.SetCircle(ring, land + Vector3.up * 0.05f, move.radius);
            float air = 0.55f + move.speed * 0.12f, peak = move.speed;
            Body.Act(KalakhMoves.Air(), 0.2f);
            for (float t = 0f; t < air && !Dead; t += Time.deltaTime)
            {
                float k = t / air;
                transform.position = Vector3.Lerp(start, land, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * peak;
                Face(StoryFx.Flat(land - start), Time.deltaTime * 8f);
                ring.startColor = ring.endColor = StoryFx.WithAlpha(KalakhFx.Danger, 0.4f + 0.5f * Mathf.PingPong(t * 6f, 1f));
                yield return null;
            }
            Destroy(ring.gameObject);
            if (Dead) yield break;
            transform.position = land;
            Body.Act(s.hit, 0.1f, true);
            KalakhFx.Shockwave(this, land, move.radius * 1.2f, s.effect == HitEffect.Burn ? KalakhFx.Fire : new Color(0.9f, 0.7f, 0.5f), 0.6f, 0.35f);
            CombatFx.SmokePuff(land, move.radius * 0.5f, 25, 0.8f);
            CombatAudio.Play(CombatAudio.HeavySwoosh, land, 1f, 0.2f);
            CombatFx.Rumble(null, 1f, 0.3f);
            if (Vector3.Distance(Player, land) < move.radius + 0.3f && !Avoided(s.reach))
                HurtPlayer(s.damage * damageScale, land, Toward, s.effect == HitEffect.None ? HitEffect.Knockback : s.effect, s.reach, this);
            yield return Wait(0.5f);
        }

        IEnumerator Charge(KalakhMove move)
        {
            Strike s = move.strikes[0];
            Body.Act(KalakhMoves.Charging(), s.windTime);
            for (int i = 0; i < 3; i++) CombatAudio.Play(CombatAudio.Growl, Body.Mouth, 0.8f, 0.3f);
            for (float t = 0f; t < s.windTime && !Dead; t += Time.deltaTime) // pawing the ground
            {
                Face(Toward, Time.deltaTime * 8f);
                if (Random.value < Time.deltaTime * 20f) CombatFx.SmokePuff(transform.position, 0.3f, 1, 0.3f);
                yield return null;
            }
            Vector3 dir = Toward;
            float run = Distance + 2.5f;
            bool hit = false;
            for (float d = 0f; d < run && !Dead; )
            {
                float step = move.speed * speedScale * Time.deltaTime;
                Step(dir * step);
                d += step;
                Body.Speed = move.speed;
                if (Random.value < Time.deltaTime * 40f) CombatFx.SmokePuff(transform.position, 0.3f, 1, 0.4f);
                Vector3 rel = Player - transform.position;
                if (!hit && rel.magnitude < 0.9f + height * 0.15f)
                {
                    hit = true;
                    if (WeaverSword.Instance != null && WeaverSword.Instance.TryParry(Body.Chest, target.position - Vector3.up * 0.4f))
                    {
                        Parried();
                        break;
                    }
                    if (!Avoided(Reach.Mid))
                    {
                        HurtPlayer(s.damage * damageScale, Body.Chest, dir, HitEffect.Knockback, Reach.Mid, this);
                        CombatFx.SmokePuff(transform.position + dir * 0.5f, 0.5f, 12, 0.5f); // it ploughs to a stop against them
                        break;
                    }
                }
                yield return null;
            }
            Body.Speed = 0f;
            Body.Act(s.hit, 0.2f);
            yield return Wait(0.4f);
        }

        IEnumerator Throw(KalakhMove move)
        {
            Strike s = move.strikes[0];
            yield return WindUp(s);
            if (Dead) yield break;
            Body.Act(s.hit, 0.15f, true);
            float gap = move.count > 1 ? Mathf.Max(0.12f, move.duration / move.count) : 0f;
            int count = move.count > 1 ? Random.Range(Mathf.Max(1, move.count - 1), move.count + 2) : move.count;
            for (int i = 0; i < count && !Dead; i++)
            {
                Vector3 from = move.missile == MissileKind.Fire || move.missile == MissileKind.Venom || move.missile == MissileKind.Shadow ? Body.Mouth : Body.HandR;
                Vector3 offset = Vector3.zero;
                if (move.count > 1)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, Toward);
                    offset = side * Random.Range(-1.5f, 1.5f) + (move.missile == MissileKind.Fire && move.count > 3 ? Toward * Random.Range(-1f, 1f) : Vector3.zero);
                }
                KalakhMissile.Launch(this, move.missile, from, target, move.speed, s.damage * damageScale, s.effect, offset);
                CombatAudio.Play(CombatAudio.Swoosh, from, 0.7f, 0.3f);
                yield return Wait(gap);
            }
            yield return Wait(0.3f);
        }

        IEnumerator Eruption(KalakhMove move)
        {
            Strike s = move.strikes[0];
            yield return WindUp(s);
            if (Dead) yield break;
            Body.Act(s.hit, 0.15f, true);
            CombatFx.Rumble(null, 0.6f, 0.4f);
            KalakhFx.Shockwave(this, transform.position, 2f, KalakhFx.Fire);
            if (move.count < 0) // a ring of fire racing outward from it: jump it
            {
                Vector3 c = transform.position;
                var ring = Mark(KalakhFx.NewRing("InfernoRing", KalakhFx.Fire, 0.35f));
                bool done = false;
                for (float r = 0.5f; r < move.radius && !Dead; r += move.speed * Time.deltaTime)
                {
                    KalakhFx.SetCircle(ring, c + Vector3.up * 0.1f, r);
                    for (int i = 0; i < 3; i++)
                    {
                        float a = Random.value * Mathf.PI * 2f;
                        StoryFx.Emit(CombatFx.Sparks, c + new Vector3(Mathf.Cos(a) * r, 0.1f, Mathf.Sin(a) * r), Vector3.up * Random.Range(1f, 3f), 0.06f, 0.5f, KalakhFx.Fire);
                    }
                    if (!done && Mathf.Abs(Vector3.Distance(Player, c) - r) < 0.4f)
                    {
                        done = true;
                        if (!Avoided(Reach.Low)) HurtPlayer(s.damage * damageScale, Player, StoryFx.Flat(Player - c), HitEffect.Burn, Reach.Low, this);
                    }
                    yield return null;
                }
                Destroy(ring.gameObject);
                yield break;
            }
            // Glowing rings under the player's feet (and along its line), then fire and rock burst out of them.
            var spots = new List<Vector3>();
            for (int i = 0; i < move.count; i++)
            {
                Vector3 at = move.count == 1 ? Player : move.count == 2 ? Player + Vector3.Cross(Vector3.up, Toward) * (i == 0 ? -1f : 1f) * 1.2f
                           : Vector3.Lerp(transform.position + Toward * 1.5f, Player + Toward * 1f, i / (float)(move.count - 1));
                at.y = KalakhFx.Ground(at, transform.position.y);
                spots.Add(at);
            }
            var rings = new List<LineRenderer>();
            foreach (Vector3 at in spots)
            {
                var ring = Mark(KalakhFx.NewRing("EruptionMark", KalakhFx.Danger, 0.07f));
                KalakhFx.SetCircle(ring, at + Vector3.up * 0.05f, move.radius);
                rings.Add(ring);
            }
            float delay = move.delay * Random.Range(0.75f, 1.3f);
            for (float t = 0f; t < delay; t += Time.deltaTime)
            {
                foreach (var ring in rings) ring.startColor = ring.endColor = StoryFx.WithAlpha(KalakhFx.Danger, 0.3f + 0.7f * Mathf.PingPong(t * (3f + t * 6f), 1f));
                foreach (Vector3 at in spots)
                    if (Random.value < 0.3f) StoryFx.Emit(CombatFx.Sparks, at + Random.insideUnitSphere * move.radius * 0.6f, Vector3.up * 1.5f, 0.03f, 0.4f, KalakhFx.Fire);
                yield return null;
            }
            for (int i = 0; i < spots.Count; i++)
            {
                Destroy(rings[i].gameObject);
                KalakhFx.Erupt(this, spots[i], move.radius, s.effect == HitEffect.Burn);
                if (Vector3.Distance(Player, spots[i]) < move.radius + 0.2f && !Avoided(Reach.Low))
                    HurtPlayer(s.damage * damageScale, spots[i], StoryFx.Flat(Player - spots[i]), s.effect == HitEffect.None ? HitEffect.Knockback : s.effect, Reach.Low, this);
                if (move.count > 2) yield return Wait(0.18f);
            }
            yield return Wait(0.4f);
        }

        IEnumerator Breath(KalakhMove move)
        {
            Strike s = move.strikes[0];
            yield return WindUp(s);
            if (Dead) yield break;
            Body.Act(s.hit, 0.3f);
            var fire = Mark(new GameObject("BreathLight").AddComponent<Light>());
            fire.type = LightType.Point;
            fire.color = KalakhFx.Fire;
            fire.range = 6f;
            fire.shadows = LightShadows.None;
            float tick = 0f;
            for (float t = 0f; t < move.duration && !Dead; t += Time.deltaTime)
            {
                Face(Toward, Time.deltaTime * 1.2f); // it sweeps after the player, slowly: step out of it
                Vector3 mouth = Body.Mouth, dir = (transform.forward + Vector3.down * 0.25f).normalized;
                fire.transform.position = mouth + dir * 1.5f;
                fire.intensity = 4f + Random.value * 2f;
                for (int i = 0, n = Mathf.Clamp(Mathf.RoundToInt(Time.deltaTime * 300f), 1, 8); i < n; i++)
                {
                    Vector3 spread = Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(-18f, 18f), 0f) * dir;
                    StoryFx.Emit(CombatFx.Sparks, mouth, spread * Random.Range(5f, 8f), Random.Range(0.08f, 0.2f), Random.Range(0.4f, 0.7f),
                                 Color.Lerp(KalakhFx.Fire, Color.yellow, Random.value * 0.6f));
                }
                if (Random.value < Time.deltaTime * 25f) CombatFx.SmokePuff(mouth + dir * 2.5f, 0.4f, 1, 0.8f);
                tick -= Time.deltaTime;
                Vector3 rel = Player - transform.position;
                if (tick <= 0f && rel.magnitude < move.radius && Vector3.Angle(transform.forward, rel) < s.angle * 0.5f)
                {
                    tick = 0.3f;
                    HurtPlayer(s.damage * damageScale * 0.5f, mouth, Toward, HitEffect.None, Reach.Mid, this);
                    if (RiftPlayer.Instance != null) RiftPlayer.Instance.Burn(1.5f);
                }
                yield return null;
            }
            Destroy(fire.gameObject);
        }

        IEnumerator RoarMove(KalakhMove move)
        {
            Strike s = move.strikes[0];
            yield return Roar(s.windTime, true);
            if (Dead) yield break;
            KalakhFx.Shockwave(this, transform.position, move.radius, s.effect == HitEffect.Blind ? new Color(0.2f, 0.18f, 0.18f) : new Color(1f, 0.6f, 0.4f), 0.7f, 0.4f);
            if (s.effect == HitEffect.Blind)
                for (int i = 0; i < 30; i++) CombatFx.SmokePuff(transform.position + Random.insideUnitSphere * move.radius * 0.6f + Vector3.up, 0.8f, 1, 0.3f);
            if (Distance < move.radius) HurtPlayer(s.damage * damageScale, Body.Mouth, Toward, s.effect, Reach.Mid, this);
            yield return Wait(0.3f);
        }

        IEnumerator Roar(float wind, bool loud)
        {
            Body.Act(KalakhMoves.Rear(), wind);
            yield return Wait(wind);
            Body.Act(KalakhMoves.Taunt(), 0.2f, true);
            CombatAudio.Play(CombatAudio.Roar, Body.Mouth, loud ? 1f : 0.7f, 0.15f);
            CombatFx.Rumble(null, 0.5f, 0.6f);
            for (float t = 0f; t < 1f; t += Time.deltaTime)
            {
                eyes.intensity = 4f + Mathf.Sin(t * 30f) * 2f;
                yield return null;
            }
            eyes.intensity = 1.2f;
        }

        IEnumerator Grab(KalakhMove move)
        {
            Strike s = move.strikes[0];
            yield return WindUp(s);
            if (Dead) yield break;
            Body.Act(s.hit, s.hitTime, true);
            for (float t = 0f; t < s.hitTime; t += Time.deltaTime)
            {
                if (Distance > Personal + 0.1f) Step(transform.forward * s.lunge / s.hitTime * Time.deltaTime);
                yield return null;
            }
            if (!InShape(s) || Avoided(Reach.Mid)) yield break;
            if (WeaverSword.Instance != null && WeaverSword.Instance.TryParry(Body.Chest, target.position - Vector3.up * 0.4f))
            {
                Parried();
                yield break;
            }
            // Caught: held fast and squeezed until the player cuts themselves free (any hit) or it lets go.
            grabbing = true;
            if (legs == null) legs = FindAnyObjectByType<PlayerLocomotion>();
            if (legs != null) legs.Stunned = true;
            CombatFx.Popup("GRABBED! STRIKE IT!", target.position + StoryFx.Flat(target.forward) * 0.7f, KalakhFx.Danger, 0.9f);
            for (int i = 0; i < move.count && grabbing && !Dead; i++)
            {
                yield return Wait(0.7f);
                if (!grabbing || Dead) break;
                HurtPlayer(s.damage * damageScale, Body.Chest, Toward, HitEffect.None, Reach.Mid, this);
                CombatFx.Rumble(null, 1f, 0.3f);
                ScreenFade.Set(new Color(0.5f, 0f, 0.02f, 0.35f));
            }
            bool broke = !grabbing;
            grabbing = false;
            if (legs != null) legs.Stunned = false;
            ScreenFade.Set(new Color(0f, 0f, 0f, 0f));
            if (!broke) HurtPlayer(0f, Body.Chest, Toward, HitEffect.Knockback, Reach.Mid, this); // thrown off
        }

        IEnumerator Spin(KalakhMove move)
        {
            Strike s = move.strikes[0];
            yield return WindUp(s);
            for (int n = 0; n < move.count && !Dead; n++)
            {
                Body.Act(s.hit, s.hitTime * 0.4f, true);
                CombatAudio.Play(CombatAudio.HeavySwoosh, Body.Chest, 0.9f, 0.2f);
                bool landed = false;
                float start = transform.eulerAngles.y;
                for (float t = 0f; t < s.hitTime && !Dead; t += Time.deltaTime)
                {
                    float k = t / s.hitTime;
                    transform.rotation = Quaternion.Euler(0f, start + 360f * (1f - (1f - k) * (1f - k)), 0f);
                    if (s.lunge > 0f && Distance > Personal + 0.1f) Step(Toward * s.lunge * Time.deltaTime / s.hitTime);
                    if (!landed && k > 0.3f && Distance < move.radius * (height / 2.2f) * 0.5f + move.radius * 0.5f)
                    {
                        landed = true;
                        if (!Avoided(s.reach)) HurtPlayer(s.damage * damageScale, s.fromTail ? Body.TailTip : Body.HandR, Toward, HitEffect.Knockback, s.reach, this);
                    }
                    yield return null;
                }
                if (n < move.count - 1) Body.Act(s.wind, 0.15f);
            }
            Face(Toward, 1f);
        }

        IEnumerator ShadowStep()
        {
            // Into smoke... and out again behind the player's shoulder.
            CombatFx.SmokePuff(transform.position + Vector3.up * height * 0.5f, height * 0.4f, 35, 0.8f);
            CombatAudio.Play(CombatAudio.Tear, transform.position, 0.6f, 0.2f);
            for (float t = 0f; t < 0.25f; t += Time.deltaTime)
            {
                visual.localScale = Vector3.one * (1f - t / 0.25f);
                yield return null;
            }
            visual.localScale = Vector3.zero;
            yield return Wait(0.4f);
            if (target != null)
            {
                // Behind their shoulder, or round their flank if the lava (or the ledge's drop) is behind them.
                Vector3 back = StoryFx.Flat(target.forward), side = Vector3.Cross(Vector3.up, back) * (Random.value < 0.5f ? -1f : 1f);
                Vector3[] around = { -back * 1.6f + side * Random.Range(0f, 1f), -back * 1.6f - side * Random.Range(0f, 1f), side * 1.7f - back * 0.4f, -side * 1.7f - back * 0.4f };
                foreach (Vector3 offset in around)
                    if (KalakhFx.Footing(Player + offset, transform.position.y, out float ground))
                    {
                        transform.position = new Vector3(Player.x + offset.x, ground, Player.z + offset.z);
                        break;
                    }
                Face(StoryFx.Flat(Player - transform.position), 1f); // (nowhere safe: out again where it went in)
            }
            CombatFx.SmokePuff(transform.position + Vector3.up * height * 0.5f, height * 0.4f, 35, 0.8f);
            CombatAudio.Play(CombatAudio.Growl, transform.position, 1f, 0.2f);
            for (float t = 0f; t < 0.2f; t += Time.deltaTime)
            {
                visual.localScale = Vector3.one * (t / 0.2f);
                yield return null;
            }
            visual.localScale = Vector3.one;
        }

        IEnumerator Feint(KalakhMove move)
        {
            yield return WindUp(move.strikes[0]); // the tell... and nothing
            Body.Act(KalakhBody.Idle(), 0.25f);
            yield return Wait(move.delay);
        }

        IEnumerator Retreat(float distance)
        {
            // A spring back (or to the side, for a negative distance) out of reach.
            Vector3 dir = distance >= 0f ? -Toward : Vector3.Cross(Vector3.up, Toward) * (Random.value < 0.5f ? -1f : 1f);
            float d = Mathf.Abs(distance);
            Body.Act(KalakhMoves.Crouch(), 0.12f);
            yield return Wait(0.12f);
            Body.Act(KalakhMoves.Air(), 0.1f);
            Vector3 start = transform.position;
            for (float t = 0f; t < 0.4f && !Dead; t += Time.deltaTime)
            {
                float k = t / 0.4f;
                Vector3 p = start + dir * d * Mathf.SmoothStep(0f, 1f, k);
                if (!KalakhFx.Footing(p, start.y, out float ground)) break; // the edge: no further back
                p.y = ground + Mathf.Sin(k * Mathf.PI) * 0.5f;
                transform.position = p;
                Face(Toward, Time.deltaTime * 10f);
                yield return null;
            }
            Vector3 down = transform.position;
            transform.position = new Vector3(down.x, KalakhFx.Ground(down, start.y), down.z);
            Body.Act(KalakhMoves.Land(), 0.1f, true);
            CombatFx.SmokePuff(transform.position, 0.4f, 8, 0.4f);
            yield return Wait(0.2f);
        }

        IEnumerator Berserk(KalakhMove move)
        {
            yield return Roar(move.strikes[0].windTime, true);
            berserkUntil = Time.time + move.duration;
            CombatFx.Popup("BERSERK", Body.Mouth + Vector3.up * 0.5f, KalakhFx.Danger, 1.2f);
        }

        // ---------------------------------------------------------------- defence

        /// <summary>Called when the player starts a swing: maybe a guard, a sidestep, or a step back.</summary>
        void OnPlayerSwing(SwordTechnique technique)
        {
            if (!Active || Dead || busy || Reeling || target == null || Training && !AllowDefence) return;
            if (Distance > 2.6f || Time.time - lastDodge < 1.5f) return;
            if (Vector3.Angle(transform.forward, Player - transform.position) > 70f) return;
            float chance = 0.12f + Tier * 0.06f + (Breed == "DreadWarden" ? 0.2f : 0f);
            if (Random.value > chance) return;
            lastDodge = Time.time;
            Interrupt();
            busy = true;
            brain = StartCoroutine(Defend(technique));
        }

        IEnumerator Defend(SwordTechnique technique)
        {
            bool warden = Breed == "DreadWarden";
            bool guard = (Tier >= 2 || warden) && Random.value < (warden ? 0.85f : 0.55f) && technique != SwordTechnique.Thrust;
            if (guard)
            {
                Body.Act(warden ? KalakhMoves.ShieldRaise() : KalakhMoves.GuardCross(), 0.12f, true);
                guardUntil = Time.time + Random.Range(1f, 2f);
                while (Time.time < guardUntil && !Dead && !Reeling)
                {
                    Face(Toward, Time.deltaTime * 6f);
                    yield return null;
                }
                guardUntil = 0f;
                // Out of the guard into a counter, now and then.
                if (!Dead && !Reeling && Random.value < 0.4f && Distance < 2.2f)
                {
                    attacker = this;
                    KalakhMove counter = known.Find(m => m.name == (warden ? "Shield Bash" : "Rending Claw"));
                    Strike answer = counter != null ? counter.strikes[0] : blows.Count > 0 ? blows[Random.Range(0, blows.Count)] : null;
                    if (answer != null) yield return Blow(Vary(answer));
                    if (attacker == this) attacker = null;
                }
            }
            else yield return Retreat(Random.value < 0.5f ? 1.6f : -1.5f);
            busy = false;
            Body.Relax(0.3f);
            if (!Dead && !Training) brain = StartCoroutine(Brain());
        }

        // ---------------------------------------------------------------- being hit

        public void TakeHit(HitInfo hit)
        {
            if (Dead || !Active) return;
            Vector3 push = StoryFx.Flat(hit.Direction);
            bool breaker = hit.Technique == SwordTechnique.Thrust || hit.Technique == SwordTechnique.Overhead ||
                           hit.Technique == SwordTechnique.Rising || hit.Technique == SwordTechnique.Lunge || hit.Technique == SwordTechnique.Execution;
            if (Time.time < guardUntil && Vector3.Angle(transform.forward, -push) < 100f)
            {
                if (breaker)
                {
                    guardUntil = 0f;
                    CombatFx.Popup("GUARD BREAK!", hit.Point + Vector3.up * 0.3f, new Color(1f, 0.8f, 0.3f), 1f);
                    Resolved("guardbreak");
                    CombatAudio.Play(CombatAudio.Clang, hit.Point, 1f, 0.1f);
                    Stagger(1.6f);
                }
                else
                {
                    // Blocked: claws ring on the blade, the sword is knocked back.
                    CombatFx.HitSparks(hit.Point, -push, 40, Color.white);
                    CombatAudio.Play(CombatAudio.Clang, hit.Point, 0.9f, 0.2f);
                    CombatFx.Popup("BLOCKED", hit.Point + Vector3.up * 0.3f, new Color(0.75f, 0.75f, 0.8f), 0.8f);
                    if (WeaverSword.Instance != null) WeaverSword.Instance.Recoil(-push);
                    Health -= hit.Damage * 0.1f;
                    return;
                }
            }
            if (grabbing)
            {
                grabbing = false; // cut free
                CombatFx.Popup("BROKE FREE", hit.Point + Vector3.up * 0.3f, Color.white, 0.9f);
                Stagger(1.2f);
            }

            Health -= hit.Damage;
            flashUntil = Time.time + 0.1f;
            KalakhFx.Wound(transform, hit.Point, Vector3.Cross(push, Vector3.up) + Vector3.up * 0.3f, new Color(1f, 0.55f, 0.2f, 1f));
            for (int i = 0; i < 14; i++) // dark ichor
                StoryFx.Emit(CombatFx.Smoke, hit.Point, (push + Random.insideUnitSphere * 0.8f) * Random.Range(1f, 2.5f), Random.Range(0.03f, 0.07f), Random.Range(0.4f, 0.8f), new Color(0.15f, 0.02f, 0.2f, 0.9f));
            float weight = IsBoss ? 0.35f : 1f / (height / 2f);
            knock += push * Mathf.Min(3f, hit.Damage * 0.06f) * weight;
            Body.Flinch(push, Mathf.Clamp01(hit.Damage / 40f));
            Body.Hitch(Mathf.Lerp(0.04f, 0.11f, Mathf.Clamp01(hit.Damage / 40f))); // the blow lands with weight
            if (hit.Damage >= 25f) // a big one jars it on its feet: dust kicked up
                for (int i = 0; i < 6; i++)
                    StoryFx.Emit(CombatFx.Smoke, transform.position + Random.insideUnitSphere * 0.3f + Vector3.up * 0.05f, (push + Random.insideUnitSphere) * 0.6f + Vector3.up * 0.2f,
                                 Random.Range(0.15f, 0.3f) * height * 0.5f, Random.Range(0.7f, 1.1f), new Color(0.32f, 0.27f, 0.25f, 0.45f));

            // Heavy blows (or many) break its attack, unless it's committed to a big one.
            bool heavy = hit.Damage >= 35f || breaker && hit.Technique != SwordTechnique.Thrust;
            if (heavy && !superArmour && busy && Random.value < (IsBoss ? 0.35f : 0.7f)) Stagger(0.7f);
            if (hit.Technique == SwordTechnique.Execution) Resolved("execution");
            if (Training && Health <= 0f)
            {
                // A practice shadow can't be killed: it pulls itself back together.
                Health = MaxHealth;
                CombatFx.SmokePuff(transform.position + Vector3.up * height * 0.5f, height * 0.3f, 25, 0.6f);
                CombatFx.Popup("RENEWED", Body.Mouth + Vector3.up * 0.4f, new Color(0.6f, 0.85f, 1f), 0.8f);
                return;
            }
            if (Health <= 0f) Die(hit);
        }

        /// <summary>Knocked off balance: it reels (open to a Riposte; weak, to an Execution).</summary>
        public void Stagger(float seconds)
        {
            if (Dead) return;
            reelUntil = Time.time + seconds;
            guardUntil = 0f;
            Interrupt();
            busy = true;
            brain = StartCoroutine(Reel(seconds));
        }

        IEnumerator Reel(float seconds)
        {
            Body.Act(KalakhMoves.Stagger(), 0.15f, true);
            CombatAudio.Play(CombatAudio.Growl, Body.Mouth, 0.6f, 0.4f);
            for (float t = 0f; t < seconds && !Dead; t += Time.deltaTime)
            {
                Step(-transform.forward * 0.4f * Time.deltaTime);
                yield return null;
            }
            busy = false;
            Body.Relax(0.4f);
            if (!Dead && !Training) brain = StartCoroutine(Brain());
        }

        void Die(HitInfo hit)
        {
            Dead = true;
            Active = false;
            Alive.Remove(this);
            Interrupt();
            foreach (var c in GetComponents<Collider>()) c.enabled = false;
            bar.gameObject.SetActive(false);
            CombatAudio.Play(CombatAudio.Death, Body.Chest, 1f, 0.1f);
            CombatAudio.Play(CombatAudio.Roar, Body.Mouth, 0.6f, 0.3f);
            StartCoroutine(Fall(hit));
            OnDied?.Invoke(this);
        }

        // It crashes to the ground, then comes apart into smoke and embers.
        IEnumerator Fall(HitInfo hit)
        {
            Body.Speed = 0f;
            Body.LookTarget = null;
            Body.Act(KalakhMoves.Dead(), 0.9f);
            Vector3 push = StoryFx.Flat(hit.Direction);
            for (float t = 0f; t < 0.9f; t += Time.deltaTime)
            {
                Step(push * 0.8f * Time.deltaTime);
                yield return null;
            }
            CombatFx.Rumble(null, 0.6f, 0.3f);
            CombatFx.SmokePuff(transform.position, height * 0.4f, 25, 0.4f);
            eyes.intensity = 0f;
            yield return Wait(1.6f);
            for (float t = 0f; t < 1.6f; t += Time.deltaTime)
            {
                Vector3 c = transform.position + Vector3.up * height * 0.25f;
                if (Random.value < 0.9f) CombatFx.SmokePuff(c + Random.insideUnitSphere * height * 0.3f, height * 0.15f, 2, 0.8f);
                if (Random.value < 0.8f)
                    StoryFx.Emit(CombatFx.Sparks, c + Random.insideUnitSphere * height * 0.3f, Vector3.up * Random.Range(0.5f, 1.5f), 0.03f, 1f, new Color(1f, 0.55f, 0.2f));
                Body.Sink = height * 0.25f * (t / 1.6f); // (the body owns the visual's position)
                visual.localScale = Vector3.one * (1f - 0.6f * t / 1.6f);
                yield return null;
            }
            Destroy(gameObject);
        }

        /// <summary>Debug: falls at once.</summary>
        public void Kill()
        {
            if (Dead) return;
            Health = 0f;
            Die(new HitInfo { Damage = 9999f, Point = transform.position + Vector3.up, Direction = -transform.forward });
        }

        /// <summary>The Weaver has fallen: it stops fighting, rears up and roars over them.</summary>
        public void Triumph()
        {
            if (Dead) return;
            Interrupt();
            Active = false;
            Body.Act(KalakhMoves.Taunt(), 0.35f, false);
            CombatAudio.Play(CombatAudio.Roar, Body.Mouth, 1f, 0.2f + Random.value * 0.4f);
        }

        /// <summary>Takes it down to a share of its health (debug).</summary>
        public void Weaken(float toFraction) => Health = Mathf.Min(Health, MaxHealth * toFraction);

        // ---------------------------------------------------------------- looks

        void OnEnable()
        {
            if (WeaverSword.Instance != null) WeaverSword.Instance.OnTechnique += OnPlayerSwing;
        }

        void Start()
        {
            if (WeaverSword.Instance != null)
            {
                WeaverSword.Instance.OnTechnique -= OnPlayerSwing;
                WeaverSword.Instance.OnTechnique += OnPlayerSwing;
            }
        }

        void OnDisable()
        {
            if (WeaverSword.Instance != null) WeaverSword.Instance.OnTechnique -= OnPlayerSwing;
        }

        void UpdateLooks()
        {
            // The health bar, facing the player.
            if (bar != null && bar.gameObject.activeSelf && Camera.main != null)
            {
                bar.rotation = Quaternion.LookRotation(bar.position - Camera.main.transform.position);
                float k = Mathf.Clamp01(HealthFraction);
                fill.localScale = new Vector3(0.88f * k, 0.05f, 1f);
                fill.localPosition = new Vector3(-0.44f * (1f - k), 0f, -0.005f);
            }
            bool flash = Time.time < flashUntil, red = Time.time < redUntil || Time.time < berserkUntil;
            foreach (var (r, slot, tint, glow) in looks)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block, slot);
                block.SetColor(BaseColor, flash ? new Color(1f, 0.75f, 0.35f) : red ? Color.Lerp(tint, new Color(1f, 0.3f, 0.25f), 0.6f) : tint);
                block.SetColor(EmissionColor, red ? new Color(0.6f, 0.02f, 0.02f) * (0.6f + 0.4f * Mathf.Sin(Time.time * 12f)) : glow);
                r.SetPropertyBlock(block, slot);
            }
            BurnEyes();
            if (Active && !Dead && Random.value < Time.deltaTime * 10f)
                CombatFx.SmokePuff(transform.position + Vector3.up * Random.Range(0f, height * 0.5f), 0.12f * height, 1, 0.3f);
        }

        // The eyes flicker like coals and flames stream up and back off them: hotter as it winds up, out when it dies.
        void BurnEyes()
        {
            float anger = eyes != null ? Mathf.Clamp01((eyes.intensity - 1.2f) / 4f) : 0f;
            bool lit = !Dead && eyes != null && eyes.intensity > 0.01f;
            float flicker = 0.75f + 0.25f * Mathf.PerlinNoise(Time.time * 9f, transform.GetInstanceID() * 0.1f);
            foreach (Renderer r in eyeRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                Color c = lit ? eyeColour * (2.5f + 5f * anger) * flicker : Color.black;
                block.SetColor(EmissionColor, c);
                block.SetColor(BaseColor, lit ? Color.Lerp(eyeColour, Color.white, 0.3f) : new Color(0.05f, 0.03f, 0.02f));
                r.SetPropertyBlock(block);
            }
            if (!lit || eyeFire == null) return;
            float rate = 40f + 90f * anger, size = height * (0.022f + 0.02f * anger);
            Vector3 back = Body.Head != null ? -StoryFx.Flat(Body.Head.forward) : -transform.forward;
            for (eyeFireDue += Time.deltaTime * rate; eyeFireDue >= 1f; eyeFireDue -= 1f)
                foreach (Transform eye in eyeSockets)
                {
                    if (eye == null) continue;
                    Vector3 v = Vector3.up * height * Random.Range(0.12f, 0.25f) + back * height * 0.08f + Random.insideUnitSphere * height * 0.03f;
                    StoryFx.Emit(eyeFire, eye.position + Random.insideUnitSphere * size * 0.3f, v, size * Random.Range(0.6f, 1.3f), Random.Range(0.18f, 0.38f),
                                 StoryFx.WithAlpha(Color.Lerp(eyeColour, new Color(1f, 0.85f, 0.3f), Random.value * 0.5f), 0.9f));
                }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Alive.Clear();
            attacker = null;
            dodger = null;
            legs = null;
        }
    }

    /// <summary>Something that can be knocked off balance (a parry, a smash): the next hit is a Riposte.</summary>
    public interface IReeling
    {
        bool Reeling { get; }
        void Stagger(float seconds);
    }
}
