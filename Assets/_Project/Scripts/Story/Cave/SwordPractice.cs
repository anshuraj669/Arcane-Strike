using System;
using System.Collections;
using System.Collections.Generic;
using ArcaneStrike.Combat;
using ArcaneStrike.HandInput;
using ArcaneStrike.VFX;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Optional sword practice with Ember, in the cave before the way out opens. Ember offers two glowing stones:
    /// TRAIN WITH EMBER or ONWARD (touch one with a hand, or look at it and press E). Onward: straight on to the monster
    /// world. Train: draw the blade, and Ember conjures a practice shadow (pale smoke, it can't hurt you and can't die)
    /// and walks the player through every technique: each attack (shown with the name the game read from the swing, so
    /// the moves can be tuned on the headset), the named combos, and defending against the shadow's own blows: parry,
    /// duck, jump, breaking its guard, sending its fire back, and an Execution. N skips a lesson; the ONWARD stone stays
    /// beside the player the whole time, and touching it ends the practice at once. The sword goes back on the back after.
    /// </summary>
    public class SwordPractice : MonoBehaviour
    {
        [Tooltip("The practice shadow's body (the Kalakh). Empty: a figure of plain shapes.")]
        public GameObject shadowModel;
        [SerializeField] Color trainColor = new Color(0.55f, 0.8f, 1f, 1f);
        [SerializeField] Color onwardColor = new Color(1f, 0.55f, 0.2f, 1f);

        class Lesson
        {
            public string title, how, keys, want;
            public int count = 1;
            public string demo;       // the shadow's move to practise against (repeats until done)
            public Action setup;
        }

        public bool Practising { get; private set; }

        HandInputService input;
        Transform head;
        WeaverSword sword;
        KalakhWarrior shadow;
        Stone train, onward;
        TextMesh card;
        Lesson current;
        int progress;
        Func<string, float, IEnumerator> say;
        Coroutine talking; // the line Ember is saying now (a newer one cuts it off, so subtitles never interleave)
        int cardNumber = -1, cardProgress = -1; // what the card shows (rebuilt only when it changes)
        static readonly HandSide[] Hands = { HandSide.Left, HandSide.Right };
        const float DrawTimeout = 25f;   // tracked hands that never make the over-the-shoulder grip: draw anyway
        const float LessonTimeout = 75f; // a lesson the headset can't read (no N key there): move on

        // Ember says a line over the practice, cutting off whatever it was still saying.
        Coroutine Speak(string text, float hold)
        {
            if (talking != null) StopCoroutine(talking);
            return talking = StartCoroutine(say(text, hold));
        }

        // ---------------------------------------------------------------- the offer

        /// <summary>Ember offers the practice; returns once the player is done with it (or chose to go straight on).</summary>
        public IEnumerator Offer(Transform player, Func<string, float, IEnumerator> line)
        {
            head = player;
            say = line;
            input = FindAnyObjectByType<HandInputService>();
            Vector3 fwd = StoryFx.Flat(head.forward), right = Vector3.Cross(Vector3.up, fwd);
            // Within arm's reach, a little below the eyes: a hand can touch them without stepping forward.
            Vector3 eyes = head.position + fwd * 0.75f - Vector3.up * 0.25f;
            train = Stone.Make("TRAIN\nWITH EMBER", trainColor, eyes - right * 0.32f, head);
            onward = Stone.Make("ONWARD\nTO THE MONSTER\nWORLD", onwardColor, eyes + right * 0.32f, head);
            // Ember asks while the stones are already listening: a choice counts even mid-sentence.
            talking = StartCoroutine(Ask(line));
            offering = true;
            Stone chosen = null;
            while (chosen == null)
            {
                chosen = Chosen(train) ? train : Chosen(onward) ? onward : null;
                yield return null;
            }
            offering = false;
            yield return chosen.Pick();
            if (chosen == onward)
            {
                train.Fade();
                onward.Fade();
                Debug.Log("[Practice] Skipped: straight on to the monster world");
                yield return Speak("Straight on, then. I like your nerve.", 0.4f);
                yield break;
            }
            train.Fade();
            yield return Practice();
        }

        bool offering;

        IEnumerator Ask(Func<string, float, IEnumerator> line)
        {
            yield return line("Before we go... do you want to try that blade first? I can make you a shadow to practise on.", 0.4f);
            if (offering)
                yield return line("Touch the blue stone on the left to train with me, or the orange one on the right to go straight on. " +
                                  "(Mouse: left click for the left stone, right click for the right one.)", 0.2f);
        }

        // Touched with a hand, looked at with E pressed, or (while choosing) left click for the left stone, right click
        // for the right one; Enter / Backspace too.
        bool Chosen(Stone stone)
        {
            if (stone == null) return false;
            if (offering && stone == train && GameInput.Pressed(GameAction.SwordSlash)) return true; // left click
            if (offering && stone == onward && GameInput.Pressed(GameAction.Look)) return true;      // right click
            // A hand pressing into the stone (held a moment, so a brush doesn't count). While practising, the sword hand
            // (the right) is ignored: a swing passing the stone mustn't end the lesson.
            bool touching = false;
            if (input != null)
                foreach (HandSide side in Hands)
                {
                    if (Practising && side == HandSide.Right) continue;
                    HandFrame f = input.Get(side);
                    if (f != null && f.IsTracked && (stone.Touches(f[HandJoint.IndexTip]) || stone.Touches(f.PalmCenter))) touching = true;
                }
            if (stone.Press(touching)) return true;
            if (stone == train && GameInput.Pressed(GameAction.ChooseTrain)) return true;
            if (stone == onward && GameInput.Pressed(GameAction.ChooseOnward)) return true;
            Vector3 to = stone.transform.position - head.position;
            bool looking = Vector3.Angle(head.forward, to) < 18f;
            stone.Highlight(looking);
            return looking && GameInput.Pressed(GameAction.Interact);
        }

        // ---------------------------------------------------------------- the practice

        IEnumerator Practice()
        {
            Practising = true;
            Debug.Log("[Practice] Training with Ember (N skips a lesson; touch the ONWARD stone to stop)");
            sword = FindAnyObjectByType<WeaverSword>();
            if (sword == null) sword = gameObject.AddComponent<WeaverSword>();
            var locomotion = FindAnyObjectByType<PlayerLocomotion>();

            // The way out stays beside the player: touch it any time to stop.
            Vector3 fwd = StoryFx.Flat(head.forward), right = Vector3.Cross(Vector3.up, fwd);
            // On the left, for the free hand (the sword hand never ends the lesson by accident).
            onward.MoveTo(OnwardSpot(), "DONE?\nONWARD");

            yield return Speak("Good. First, draw it: reach over your right shoulder and grip the hilt.", 0.2f);
            yield return WaitForDraw();
            sword.Draw();
            yield return new WaitForSeconds(0.8f);

            shadow = MakeShadow();
            yield return Speak("There. A shadow of smoke. It can't hurt you, and it can't die: cut it as often as you like.", 0.6f);
            sword.OnTechnique += Technique;
            sword.OnCombo += Combo;
            shadow.OnResolved += Outcome;

            bool quit = false;
            List<Lesson> lessons = Lessons();
            for (int i = 0; i < lessons.Count && !quit; i++)
            {
                current = lessons[i];
                progress = 0;
                current.setup?.Invoke();
                Debug.Log($"[Practice] Lesson {i + 1}/{lessons.Count}: {current.title}");
                Speak(current.how, 0.3f);
                float nextDemo = Time.time + 2.5f, began = Time.time;
                while (progress < current.count)
                {
                    if (Time.time - began > LessonTimeout)
                    {
                        Debug.Log($"[Practice] '{current.title}' wasn't read in {LessonTimeout:F0} s: on to the next one");
                        break;
                    }
                    // The way out keeps within reach if the player wanders off.
                    if (new Vector2(onward.Home.x - head.position.x, onward.Home.z - head.position.z).magnitude > 1.4f)
                        onward.MoveTo(OnwardSpot(), null);
                    sword.ChargeAllPowerUps();
                    ShowCard(i + 1, lessons.Count);
                    if (current.demo != null && Time.time > nextDemo && !shadow.Busy)
                    {
                        shadow.StartCoroutine(shadow.Demo(current.demo));
                        nextDemo = Time.time + 3.5f;
                    }
                    if (current.want == "out:guardbreak" && !shadow.Busy && Time.time > nextDemo)
                    {
                        shadow.HoldGuard(3f);
                        nextDemo = Time.time + 3.5f;
                    }
                    if (GameInput.Pressed(GameAction.Skip)) break;
                    if (Chosen(onward))
                    {
                        quit = true;
                        break;
                    }
                    yield return null;
                }
                if (progress >= current.count)
                {
                    CombatFx.Popup("DONE: " + current.title, shadow.Body.Mouth + Vector3.up * 0.7f, new Color(0.6f, 1f, 0.6f), 1.1f);
                    yield return new WaitForSeconds(1.2f);
                }
                while (shadow.Busy) yield return null;
            }

            sword.OnTechnique -= Technique;
            sword.OnCombo -= Combo;
            current = null;
            if (card != null) Destroy(card.gameObject);
            cardNumber = cardProgress = -1;
            yield return Speak(quit ? "Enough for now? Then let's go." : "That's everything I know. The rest you'll learn out there.", 0.6f);
            // The shadow drifts apart into smoke; the blade goes back on the back.
            CombatFx.SmokePuff(shadow.transform.position + Vector3.up, 0.8f, 40, 0.8f);
            Destroy(shadow.gameObject);
            sword.Sheathe();
            if (locomotion != null) locomotion.Stunned = false;
            onward.Fade();
            Practising = false;
            Debug.Log("[Practice] Done");
        }

        // Beside the player on the left, a little in front, at the height of a lowered hand.
        Vector3 OnwardSpot()
        {
            Vector3 fwd = StoryFx.Flat(head.forward), right = Vector3.Cross(Vector3.up, fwd);
            return head.position - right * 0.6f + fwd * 0.3f - Vector3.up * 0.4f;
        }

        List<Lesson> Lessons()
        {
            string K(GameAction a) => GameInput.KeyName(a);
            return new List<Lesson>
            {
                new Lesson { title = "SLASH", want = "tech:Slash", count = 3, keys = K(GameAction.SwordSlash),
                             how = "Cut at it. Swing fast: the faster the blade moves, the deeper it bites." },
                new Lesson { title = "CROSS CUT", want = "combo:CROSS CUT", keys = $"{K(GameAction.SwordSlash)}, {K(GameAction.SwordSlash)}",
                             how = "Now cut, and cut straight back the other way. A cross cut." },
                new Lesson { title = "THRUST", want = "tech:Thrust", count = 2, keys = K(GameAction.SwordThrust),
                             how = "Drive the point straight at it, along the line of the blade." },
                new Lesson { title = "RISING SLASH", want = "tech:Rising", count = 2, keys = K(GameAction.SwordRising),
                             how = "Drop the blade low, by your hip... then cut upward." },
                new Lesson { title = "CLEAVE", want = "tech:Cleave", count = 2, keys = K(GameAction.SwordCleave),
                             how = "Hold the blade flat and sweep it across in front of you." },
                new Lesson { title = "LUNGE", want = "tech:Lunge", keys = K(GameAction.SwordLunge),
                             how = "A thrust so fast your whole body follows it in." },
                new Lesson { title = "OVERHEAD SMASH", want = "tech:Overhead", keys = K(GameAction.SwordOverhead),
                             how = "Lift the sword above your head... and bring it down hard." },
                new Lesson { title = "WHIRLWIND", want = "tech:Whirlwind", keys = K(GameAction.SwordWhirlwind),
                             how = "Sweep it all the way round you, as fast as you can." },
                new Lesson { title = "JYOTI WAVE", want = "tech:JyotiWave", keys = $"hold {K(GameAction.SwordCharge)}, then {K(GameAction.SwordSlash)}",
                             how = "Hold the blade upright and still until the light gathers in it... then swing." },
                new Lesson { title = "PARRY", want = "out:parry", count = 2, demo = "Rending Claw", keys = $"hold {K(GameAction.SwordGuard)}",
                             how = "Now it fights back. When it rears up, hold your blade across in front of you." },
                new Lesson { title = "DUCK", want = "out:avoid", demo = "Raking Sweep", keys = K(GameAction.Duck),
                             how = "This one swipes high, at your head. Duck under it!" },
                new Lesson { title = "JUMP", want = "out:avoid", demo = "Leg Sweep", keys = K(GameAction.Jump),
                             how = "And this one sweeps low. Jump over it!" },
                new Lesson { title = "BREAK ITS GUARD", want = "out:guardbreak", keys = $"{K(GameAction.SwordThrust)}, {K(GameAction.SwordOverhead)} or {K(GameAction.SwordRising)}",
                             how = "See it hiding behind its arms? Slashes just bounce off. Thrust, smash, or cut up through it." },
                new Lesson { title = "DEFLECT", want = "out:deflect", demo = "Shadow Bolts", keys = $"{K(GameAction.SwordSlash)} or hold {K(GameAction.SwordGuard)}",
                             how = "It throws shadow fire. Swing through it as it comes, and send it straight back.",
                             setup = () => shadow.StartCoroutine(shadow.Demo("Backstep")) },
                new Lesson { title = "HEAVEN SPLITTER", want = "combo:HEAVEN SPLITTER", keys = $"{K(GameAction.SwordRising)}, then {K(GameAction.SwordOverhead)}",
                             how = "Put them together: cut upward, then bring it straight down. Heaven Splitter.",
                             setup = () => shadow.StartCoroutine(shadow.Demo("Ash Pounce")) },
                new Lesson { title = "EXECUTION", want = "out:execution", demo = "Rending Claw", keys = $"hold {K(GameAction.SwordGuard)}, then {K(GameAction.SwordThrust)}",
                             how = "It's weak now. Parry it, and while it reels, drive the point in. Finish it.",
                             setup = () => shadow.Weaken(0.2f) },
            };
        }

        void Technique(SwordTechnique kind) => Count($"tech:{kind}");
        void Combo(string name) => Count($"combo:{name}");
        void Outcome(string outcome)
        {
            Count($"out:{outcome}");
            if (current != null && current.want == "out:execution" && shadow != null && shadow.HealthFraction > 0.25f) shadow.Weaken(0.2f); // (keep it weak)
        }

        void Count(string what)
        {
            if (current == null || what != current.want) return;
            progress++;
            CombatFx.Rumble(null, 0.4f, 0.1f);
        }

        // The lesson, over the shadow's head: what to do (with the hands), the key, and how far along.
        void ShowCard(int number, int total)
        {
            if (card == null)
            {
                var go = new GameObject("PracticeCard");
                card = go.AddComponent<TextMesh>();
                card.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                go.GetComponent<MeshRenderer>().sharedMaterial = card.font.material;
                card.anchor = TextAnchor.LowerCenter;
                card.alignment = TextAlignment.Center;
                card.fontSize = 48;
                card.characterSize = 0.007f;
            }
            card.transform.position = shadow.transform.position + Vector3.up * (shadow.Body.Head != null ? shadow.Body.Head.position.y - shadow.transform.position.y + 0.75f : 2.6f);
            card.transform.rotation = Quaternion.LookRotation(card.transform.position - head.position);
            if (number == cardNumber && progress == cardProgress) return; // (no new string, no mesh rebuild, every frame)
            cardNumber = number;
            cardProgress = progress;
            card.text = $"<size=36>LESSON {number}/{total}</size>\n<b>{current.title}</b>  {progress}/{current.count}\n<size=34>keys: {current.keys}   ·   {GameInput.KeyName(GameAction.Skip)}: skip</size>";
            card.richText = true;
            card.color = new Color(0.85f, 0.95f, 1f);
        }

        IEnumerator WaitForDraw()
        {
            float untracked = 0f;
            for (float waited = 0f; ; waited += Time.deltaTime)
            {
                HandFrame f = input != null ? input.Right : null;
                bool tracked = f != null && f.IsTracked;
                untracked = tracked ? 0f : untracked + Time.deltaTime;
                if (GameInput.Pressed(GameAction.Interact) || GameInput.Pressed(GameAction.SwordSlash) || untracked > 10f || waited > DrawTimeout) yield break;
                if (tracked)
                {
                    Vector3 rel = f.PalmCenter - head.position;
                    bool overShoulder = rel.y > -0.12f && Vector3.Dot(rel, StoryFx.Flat(head.forward)) < 0.12f &&
                                        Vector3.Dot(rel, Vector3.Cross(Vector3.up, StoryFx.Flat(head.forward))) > -0.05f;
                    if (overShoulder && StoryFx.IsFist(f)) yield break;
                }
                yield return null;
            }
        }

        // The practice shadow on open ground in front of the player (a few directions tried).
        KalakhWarrior MakeShadow()
        {
            Vector3 fwd = StoryFx.Flat(head.forward);
            float feet = head.position.y - (head.position.y - head.root.position.y > 0.3f ? head.position.y - head.root.position.y : 1.6f);
            Vector3 at = head.position + fwd * 2.6f;
            foreach (float a in new[] { 0f, -30f, 30f, -60f, 60f, -100f, 100f })
            {
                Vector3 dir = Quaternion.AngleAxis(a, Vector3.up) * fwd;
                Vector3 p = new Vector3(head.position.x, feet, head.position.z) + dir * 2.6f;
                bool blocked = Physics.SphereCast(head.position - Vector3.up * 0.6f, 0.4f, dir, out RaycastHit hit, 3.4f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.attachedRigidbody == null;
                if (!blocked)
                {
                    at = p;
                    break;
                }
            }
            at.y = KalakhFx.Ground(at, feet);
            GameObject model = shadowModel != null ? shadowModel : PlainFigure();
            var k = KalakhWarrior.Create(model, "Practice Shadow", 3, 150f, 2f, at, Quaternion.LookRotation(StoryFx.Flat(head.position - at)), false);
            if (shadowModel == null) Destroy(model);
            k.ActivateTraining(head);
            CombatFx.SmokePuff(at + Vector3.up, 0.8f, 40, 0.6f);
            for (int i = 0; i < 40; i++)
                StoryFx.Emit(CombatFx.Sparks, at + Vector3.up * Random.Range(0f, 2f) + Random.insideUnitSphere * 0.4f, Vector3.up * 0.5f, 0.02f, 1f, trainColor);
            return k;
        }

        // No Kalakh model in this scene: a figure of plain shapes to cut at.
        static GameObject PlainFigure()
        {
            var root = new GameObject("PracticeFigure");
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", new Color(0.4f, 0.55f, 0.8f));
            void Part(PrimitiveType t, Vector3 p, Vector3 s)
            {
                var go = GameObject.CreatePrimitive(t);
                DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = p;
                go.transform.localScale = s;
                go.GetComponent<Renderer>().sharedMaterial = m;
            }
            Part(PrimitiveType.Capsule, new Vector3(0f, 1f, 0f), new Vector3(0.5f, 0.6f, 0.35f));
            Part(PrimitiveType.Sphere, new Vector3(0f, 1.75f, 0.05f), Vector3.one * 0.3f);
            Part(PrimitiveType.Capsule, new Vector3(-0.35f, 1.1f, 0.1f), new Vector3(0.12f, 0.4f, 0.12f));
            Part(PrimitiveType.Capsule, new Vector3(0.35f, 1.1f, 0.1f), new Vector3(0.12f, 0.4f, 0.12f));
            Part(PrimitiveType.Capsule, new Vector3(-0.15f, 0.35f, 0f), new Vector3(0.15f, 0.35f, 0.15f));
            Part(PrimitiveType.Capsule, new Vector3(0.15f, 0.35f, 0f), new Vector3(0.15f, 0.35f, 0.15f));
            return root;
        }

        void OnDestroy()
        {
            if (sword != null)
            {
                sword.OnTechnique -= Technique;
                sword.OnCombo -= Combo;
            }
        }

        // ---------------------------------------------------------------- the stones

        /// <summary>A glowing stone tablet with a word on it, hovering in the air: touch it (or look + E) to choose.</summary>
        class Stone : MonoBehaviour
        {
            Renderer face;
            TextMesh text;
            Light glow;
            Color color;
            Transform view;
            Vector3 home;
            bool lit, fading;
            float pressed, push; // how long a hand has been pressing into it; how far it gives under the hand

            public Vector3 Home => home;

            public static Stone Make(string label, Color color, Vector3 at, Transform view)
            {
                var go = new GameObject("ChoiceStone " + label.Split('\n')[0]);
                go.transform.position = at;
                var s = go.AddComponent<Stone>();
                s.color = color;
                s.view = view;
                s.home = at;
                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(slab.GetComponent<Collider>());
                slab.transform.SetParent(go.transform, false);
                slab.transform.localScale = new Vector3(0.36f, 0.24f, 0.04f);
                var stone = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                stone.SetColor("_BaseColor", new Color(0.15f, 0.14f, 0.16f));
                stone.EnableKeyword("_EMISSION");
                stone.SetColor("_EmissionColor", color * 0.4f);
                slab.GetComponent<Renderer>().sharedMaterial = stone;
                s.face = slab.GetComponent<Renderer>();
                var t = new GameObject("Label");
                t.transform.SetParent(go.transform, false);
                t.transform.localPosition = new Vector3(0f, 0f, -0.025f);
                s.text = t.AddComponent<TextMesh>();
                s.text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                t.GetComponent<MeshRenderer>().sharedMaterial = s.text.font.material;
                s.text.text = label;
                s.text.anchor = TextAnchor.MiddleCenter;
                s.text.alignment = TextAlignment.Center;
                s.text.fontSize = 48;
                s.text.characterSize = 0.0045f;
                s.text.color = Color.Lerp(color, Color.white, 0.5f);
                s.glow = go.AddComponent<Light>();
                s.glow.type = LightType.Point;
                s.glow.color = color;
                s.glow.range = 1.5f;
                s.glow.intensity = 1f;
                s.glow.shadows = LightShadows.None;
                CombatFx.SmokePuff(at, 0.15f, 10, 0.2f);
                return s;
            }

            void Update()
            {
                if (fading) return;
                // It gives a little under a pressing hand (pushed away from the eyes), like a real slab of stone.
                push = Mathf.MoveTowards(push, pressed > 0f ? 0.025f : 0f, Time.deltaTime * 0.25f);
                Vector3 away = view != null ? (home - view.position).normalized : Vector3.zero;
                transform.position = home + Vector3.up * Mathf.Sin(Time.time * 1.5f + home.x) * 0.02f * (1f - push / 0.025f) + away * push;
                if (view != null) transform.rotation = Quaternion.LookRotation(transform.position - view.position);
                glow.intensity = Mathf.Lerp(glow.intensity, lit || pressed > 0f ? 2.5f : 1f, 1f - Mathf.Exp(-6f * Time.deltaTime));
                if (Random.value < 12f * Time.deltaTime) StoryFx.Emit(CombatFx.Sparks, transform.position + Random.insideUnitSphere * 0.2f, Vector3.up * 0.2f, 0.01f, 0.6f, color);
            }

            public void Highlight(bool on) => lit = on;

            /// <summary>A hand is (or isn't) pressing into it this frame; true once it has pressed for a moment.</summary>
            public bool Press(bool touching)
            {
                if (!touching)
                {
                    pressed = 0f;
                    return false;
                }
                if (pressed == 0f) CombatFx.Rumble(null, 0.25f, 0.05f);
                pressed += Time.deltaTime;
                return pressed > 0.12f;
            }

            public bool Touches(Vector3 point)
            {
                Vector3 local = transform.InverseTransformPoint(point);
                return Mathf.Abs(local.x) < 0.22f && Mathf.Abs(local.y) < 0.16f && Mathf.Abs(local.z) < 0.12f;
            }

            public void MoveTo(Vector3 at, string label)
            {
                home = at;
                if (label != null) text.text = label;
                CombatFx.SmokePuff(at, 0.15f, 10, 0.2f);
            }

            /// <summary>Chosen: it flares and chimes.</summary>
            public IEnumerator Pick()
            {
                CombatAudio.Play(CombatAudio.Clang, transform.position, 0.6f, 0f);
                CombatFx.Rumble(null, 0.5f, 0.15f);
                for (int i = 0; i < 30; i++)
                    StoryFx.Emit(CombatFx.Sparks, transform.position, Random.insideUnitSphere * 1.5f, 0.02f, 0.6f, color);
                for (float t = 0f; t < 0.3f; t += Time.deltaTime)
                {
                    glow.intensity = Mathf.Lerp(6f, 2f, t / 0.3f);
                    yield return null;
                }
            }

            public void Fade()
            {
                if (fading) return;
                fading = true;
                StartCoroutine(Out());
            }

            IEnumerator Out()
            {
                CombatFx.SmokePuff(transform.position, 0.2f, 12, 0.3f);
                Vector3 s = transform.localScale;
                for (float t = 0f; t < 0.4f; t += Time.deltaTime)
                {
                    transform.localScale = s * (1f - t / 0.4f);
                    glow.intensity *= 0.9f;
                    yield return null;
                }
                Destroy(gameObject);
            }
        }
    }
}
