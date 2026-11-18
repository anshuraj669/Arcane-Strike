using System;
using System.Collections;
using System.Collections.Generic;
using ArcaneStrike.Combat;
using ArcaneStrike.Core;
using ArcaneStrike.HandInput;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Chapter 1's fight: inside the Rift. The attic's rift has pulled the player through, and the Ascension
    /// (<see cref="Ascension"/>) happens first: the light lifts the player, the soul meets Ember, and the outfit and
    /// the sword form on the body. Back on the Rift's floor, the Weaver draws the sword. Then wave after wave of Kalakh shadows (dozens of them) rise out of the cracks
    /// round the player; it's the sword only (no other powers here). Between waves Ember teaches a new technique:
    /// slash, thrust and parry, overhead smash and whirlwind, the charged Jyoti Wave.
    /// When the last shadow falls, the Rift begins to close: the far tear splits open, the mini boss steps out of
    /// it and walks right up to the player, bends down face to face and roars, and is dragged back as the Rift
    /// seals in a flash. Chapter 1 complete: the sword is the player's prize (Power.WeaverSword).
    /// Falling to zero health isn't the end: Ember gets the player up and the shadows are thrown back.
    /// In the monster world (<see cref="hunt"/>) the fight is the Kalakh's soldiers in waves, then their Warlord (KalakhHunt).
    /// Keys (GameInput): N skips Ember's line, F9 destroys every shadow standing (a Kalakh: first down to a fifth of its
    /// health, then down), F10 jumps to the ending.
    /// </summary>
    public class RiftArena : MonoBehaviour
    {
        [Serializable]
        public class Wave
        {
            public string name;
            public int shades, lurkers, brutes;
            [Tooltip("Most shadows up at once in this wave.")]
            public int maxAlive = 4;
            [Tooltip("Ember's lines before the wave (the technique to try).")]
            [TextArea] public string[] emberLines;
        }

        [SerializeField] Wave[] waves =
        {
            new Wave { name = "First Blood", shades = 6, maxAlive = 3, emberLines = new[]
            {
                "Here they come. Swing hard: the faster the blade moves, the deeper it bites.",
            } },
            new Wave { name = "The Swarm", shades = 4, lurkers = 6, maxAlive = 4, emberLines = new[]
            {
                "Lurkers! Small and quick. Drive the point straight at them: a thrust goes right through.",
                "And when one rears back to strike, hold your blade across in front of you. Parry it, then cut it down while it reels.",
            } },
            new Wave { name = "Heavy Shadows", shades = 6, lurkers = 4, brutes = 2, maxAlive = 5, emberLines = new[]
            {
                "Brutes. Little cuts won't even make them flinch.",
                "Raise your blade high and bring it down hard: smash the ground. And if they surround you, sweep all the way round.",
            } },
            new Wave { name = "The Tide", shades = 8, lurkers = 6, brutes = 2, maxAlive = 6, emberLines = new[]
            {
                "This is all of them. Hold the sword upright and still... let the light gather in the blade...",
                "...then slash, and send it flying. Show them what a Weaver is.",
            } },
        };

        [Header("Shadow models (empty: placeholder shadows)")]
        [Tooltip("Swap in the real 3D models here when they're ready (feet at the pivot, facing +Z; any size).")]
        public GameObject shadeModel;
        public GameObject lurkerModel;
        public GameObject bruteModel;
        public GameObject bossModel;

        [Header("Arena")]
        [SerializeField] float spawnMin = 4.5f;
        [SerializeField] float spawnMax = 8f;
        [Tooltip("The tear the player came in by (behind them).")]
        [SerializeField] RiftTear entrance;
        [Tooltip("The tear at the far end, where the mini boss comes out.")]
        [SerializeField] RiftTear farEnd;
        [SerializeField] Color fogColor = new Color(0.06f, 0.02f, 0.09f, 1f);
        [SerializeField] float fogDensity = 0.045f;

        [Tooltip("Arriving, the Ascension runs first (unless the Weaver is already dressed).")]
        [SerializeField] bool ascensionOnArrival = true;
        [Tooltip("Give the scene the Rift's violet haze and darkness as it starts (off: the scene keeps its own sky, fog and light).")]
        [SerializeField] bool riftAtmosphere = true;
        [Tooltip("Ember's first words on arriving (after the Ascension, if one happened here).")]
        [SerializeField, TextArea] string[] arrivalLines =
        {
            "We're through. This is the Rift: the Kalakh's side of the dark.",
            "Feel how cold it is? Nothing here has seen the light in a thousand years.",
        };

        [Header("Monster world")]
        [Tooltip("Set: the fight is the soldiers' waves and the Warlord (KalakhHunt) instead of the waves of shadows, and the Warlord ends it.")]
        public KalakhHunt hunt;

        [Tooltip("Killed in the monster world's hunt: the Weaver dies and starts again from this scene (the cave, the Ascension). Empty: Ember gets them up.")]
        [SerializeField] string restartScene = "AscensionCave";

        [Header("After the chapter")]
        [Tooltip("Scene to load once chapter 1 is complete (must be in Build Settings). Empty: stay here.")]
        [SerializeField] string nextScene = "";
        [SerializeField] bool keyboardFallback = true;

        public event Action OnChapterComplete;
        public RiftTear Entrance => entrance;

        HandInputService input;
        WarriorOutfit outfit;
        WeaverSword sword;
        RiftPlayer player;
        Ascension ascension;
        Transform head;
        EmberFlame ember;
        StorySpeech emberSpeech;
        int waveIndex, killed, total;
        bool ending, dying;

        void Start()
        {
            input = FindAnyObjectByType<HandInputService>();
            outfit = FindAnyObjectByType<WarriorOutfit>();
            sword = FindAnyObjectByType<WeaverSword>();
            if (sword == null) sword = gameObject.AddComponent<WeaverSword>();
            player = FindAnyObjectByType<RiftPlayer>();
            if (player == null) player = gameObject.AddComponent<RiftPlayer>();
            player.OnDown += () =>
            {
                if (dying || ending) return; // (already falling, or the fight is won: nothing can bring them down now)
                StartCoroutine(Dies() ? Death() : GetUp());
            };
            if (ascensionOnArrival && (outfit == null || !outfit.ArrivesDressed))
            {
                ascension = FindAnyObjectByType<Ascension>();
                if (ascension == null) ascension = gameObject.AddComponent<Ascension>();
                if (ascension.entrance == null) ascension.entrance = entrance;
            }
            if (riftAtmosphere) SetUpDarkness();
            StartCoroutine(Run());
        }

        // A dark, violet otherworld: thick coloured haze and almost no light but the tears and the sword.
        void SetUpDarkness()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.16f, 0.1f, 0.25f);
            RenderSettings.ambientEquatorColor = new Color(0.08f, 0.04f, 0.12f);
            RenderSettings.ambientGroundColor = new Color(0.02f, 0.01f, 0.03f);
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = fogColor;
            }
        }

        IEnumerator Run()
        {
            yield return null; // the rig and the outfit set themselves up
            head = StoryFx.Head(input);
            var locomotion = FindAnyObjectByType<PlayerLocomotion>();
            if (entrance != null) entrance.SetOpenness(1f);
            if (farEnd != null) farEnd.SetOpenness(0f);
            if (ascension != null)
            {
                // Pulled through from the attic: the Ascension first, then Ember (out of the lantern) stays for the fight.
                player.HudHidden = true;
                yield return ascension.Run();
                player.HudHidden = false;
                ember = ascension.Ember;
                emberSpeech = ascension.EmberSpeech;
                if (ember == null) SpawnEmber();
                Debug.Log("[Rift] The Ascension is done. N skips Ember, F9 kills every shadow, F10 jumps to the ending (F1: all controls).");
                yield return Line("Welcome back, Weaver.", 0.8f);
            }
            else
            {
                StandAtArrival();
                // Fade in from whatever colour the way here ended on (the threshold's fire, the rift's violet).
                Color from = ScreenFade.Current.a > 0.5f ? ScreenFade.Current : new Color(0.05f, 0f, 0.08f, 1f);
                ScreenFade.Set(StoryFx.WithAlpha(from, 1f));
                yield return ScreenFade.To(StoryFx.WithAlpha(from, 0f), 2f);
                SpawnEmber();
                Debug.Log("[Rift] Inside the Rift. N skips Ember, F9 kills every shadow, F10 jumps to the ending (F1: all controls).");
            }
            foreach (string line in arrivalLines) yield return Line(line, 1.2f);
            yield return Line("Draw your blade, Weaver. Reach over your right shoulder for the hilt.", 0.2f);
            yield return WaitForDraw();
            if (sword != null) sword.Draw();
            yield return Line("Good. Stay close to the light, and don't let them get behind you.", 1f);

            if (hunt != null)
            {
                yield return hunt.Run(head, Line, player);
                if (dying) yield break; // (the last of them fell as the Weaver did: the fall wins, no victory over it)
                if (locomotion != null) locomotion.Paused = true;
                yield return HuntEnding();
                yield break;
            }

            total = 0;
            foreach (Wave w in waves) total += w.shades + w.lurkers + w.brutes;
            for (waveIndex = 0; waveIndex < waves.Length && !ending; waveIndex++)
            {
                Wave w = waves[waveIndex];
                if (w.emberLines != null)
                    foreach (string line in w.emberLines)
                        yield return Line(line, 1f);
                if (ending) break;
                if (head != null)
                    CombatFx.Popup($"WAVE {waveIndex + 1} / {waves.Length}\n{w.name.ToUpperInvariant()}", head.position + StoryFx.Flat(head.forward) * 2.5f, new Color(0.85f, 0.6f, 1f), 1.6f);
                Debug.Log($"[Rift] Wave {waveIndex + 1}: {w.name} ({w.shades} shades, {w.lurkers} lurkers, {w.brutes} brutes)");
                yield return Fight(w);
                if (ending) break;
                if (waveIndex < waves.Length - 1)
                {
                    yield return Line(waveIndex switch
                    {
                        0 => "Well fought! Catch your breath. More are coming.",
                        1 => "Ha! You're a natural. Don't get cocky.",
                        _ => "That's it! Hold on, just one more...",
                    }, 1f);
                }
            }
            if (locomotion != null) locomotion.Paused = true;
            yield return Ending();
        }

        // Waits for the player to reach over their right shoulder to the hilt on their back and close their hand on
        // it (or press E; without tracked hands it carries on after a while).
        IEnumerator WaitForDraw()
        {
            if (emberSpeech != null) emberSpeech.ShowText($"(Reach over your right shoulder and grip the hilt: or press {GameInput.KeyName(GameAction.Interact)})");
            float untracked = 0f;
            while (true)
            {
                HandFrame f = input != null ? input.Right : null;
                bool tracked = f != null && f.IsTracked;
                untracked = tracked ? 0f : untracked + Time.deltaTime;
                if (GameInput.Pressed(GameAction.Interact) || GameInput.Pressed(GameAction.SwordSlash) || untracked > 10f) break;
                if (tracked && head != null)
                {
                    Vector3 rel = f.PalmCenter - head.position;
                    bool overShoulder = rel.y > -0.12f && Vector3.Dot(rel, StoryFx.Flat(head.forward)) < 0.12f &&
                                        Vector3.Dot(rel, Vector3.Cross(Vector3.up, StoryFx.Flat(head.forward))) > -0.05f;
                    if (overShoulder && StoryFx.IsFist(f)) break;
                }
                yield return null;
            }
            if (emberSpeech != null) emberSpeech.ClearText();
        }

        // ---------------------------------------------------------------- the waves

        IEnumerator Fight(Wave w)
        {
            var queue = new List<ShadowMonster.Kind>();
            for (int i = 0; i < w.shades; i++) queue.Add(ShadowMonster.Kind.Shade);
            for (int i = 0; i < w.lurkers; i++) queue.Add(ShadowMonster.Kind.Lurker);
            for (int i = 0; i < queue.Count; i++) // shuffle the small ones
            {
                int j = Random.Range(i, queue.Count);
                (queue[i], queue[j]) = (queue[j], queue[i]);
            }
            for (int i = 0; i < w.brutes; i++) queue.Insert(Mathf.Min(queue.Count, (i + 1) * queue.Count / (w.brutes + 1) + i), ShadowMonster.Kind.Brute);

            float nextSpawn = 0f;
            while ((queue.Count > 0 || ShadowMonster.Alive.Count > 0) && !ending)
            {
                if (queue.Count > 0 && ShadowMonster.Alive.Count < w.maxAlive && Time.time > nextSpawn && !player.Down)
                {
                    Spawn(queue[0]);
                    queue.RemoveAt(0);
                    nextSpawn = Time.time + Random.Range(0.6f, 1.4f);
                }
                player.Status = $"Wave {waveIndex + 1}/{waves.Length}  ·  shadows left {queue.Count + ShadowMonster.Alive.Count}";
                yield return null;
            }
            player.Status = "";
        }

        void Spawn(ShadowMonster.Kind kind)
        {
            if (head == null) return;
            // Mostly where the player can see them; from all round in the later waves.
            float spread = waveIndex == 0 ? 70f : waveIndex == 1 ? 110f : 180f;
            Vector3 fwd = StoryFx.Flat(head.forward);
            float floor = PlayerFloor();
            Vector3 at = Vector3.zero;
            // On open ground in sight of the player (not inside a rock, not off a ledge): a few tries, then anywhere.
            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector3 dir = Quaternion.Euler(0f, Random.Range(-spread, spread), 0f) * fwd;
                at = new Vector3(head.position.x, floor, head.position.z) + dir * Random.Range(spawnMin, spawnMax);
                if (GroundAt(at, floor, out float ground) && ClearBetween(head.position - Vector3.up * 0.5f, at + Vector3.up * 1f))
                {
                    at.y = ground;
                    break;
                }
            }
            GameObject model = kind == ShadowMonster.Kind.Brute ? bruteModel : kind == ShadowMonster.Kind.Lurker ? lurkerModel : shadeModel;
            var m = ShadowMonster.Spawn(kind, at, head, model);
            m.OnDied += _ => killed++;
        }

        // Stepping out into the world: feet on the solid rock under the rig (the ledge, never the lava below it), eyes
        // at standing height on a screen, facing away from the way they came in.
        void StandAtArrival()
        {
            if (head == null) return;
            Transform rig = head.root;
            bool headset = head.position.y - rig.position.y > 0.3f;
            Vector3 at = rig.position;
            float feet = headset ? rig.position.y : head.position.y - (rig.position.y > 1f ? 1.6f : 0f); // (a screen rig may still be on the floor)
            float ground = float.MinValue;
            // The highest ground from just above the feet down (the ledge, not the lava far below; not the arch overhead).
            foreach (RaycastHit h in Physics.RaycastAll(new Vector3(at.x, feet + 2.5f, at.z), Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore))
                if (IsWorld(h.collider) && h.normal.y > 0.6f && h.point.y > ground) ground = h.point.y;
            if (ground == float.MinValue)
            {
                Debug.LogWarning("[Rift] No ground under the player: is the world's ground missing its collider?");
                return;
            }
            Quaternion facing = rig.rotation;
            if (entrance != null)
            {
                Vector3 away = StoryFx.Flat(at - entrance.transform.position);
                if ((at - entrance.transform.position).sqrMagnitude > 0.01f) facing = Quaternion.LookRotation(away);
            }
            rig.SetPositionAndRotation(new Vector3(at.x, ground + (headset ? 0f : 1.6f), at.z), facing);
            if (!headset) head.localRotation = Quaternion.identity;
            Physics.SyncTransforms();
            Debug.Log($"[Rift] The player stands on the rock at {ground:F2} m (eyes {head.position})");
        }

        // The floor under the player (in a headset the rig stands on it; on a screen the rig is at eye level).
        float PlayerFloor()
        {
            Transform rig = head.root;
            float guess = head.position.y - rig.position.y > 0.3f ? rig.position.y : head.position.y - 1.6f;
            return GroundAt(head.position, guess, out float ground) ? ground : guess;
        }

        // Solid ground under a point, within a step of `near` (the world's, not the player's own or a shadow's).
        bool GroundAt(Vector3 point, float near, out float ground)
        {
            ground = near;
            float best = float.MinValue;
            bool lava = false;
            foreach (RaycastHit h in Physics.RaycastAll(new Vector3(point.x, near + 2.5f, point.z), Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
                if (IsWorld(h.collider) && h.normal.y > 0.6f && Mathf.Abs(h.point.y - near) < 1.2f && h.point.y > best)
                {
                    best = h.point.y;
                    lava = h.collider.GetComponent<LavaFlow>() != null;
                }
            if (best == float.MinValue || lava) return false; // (nothing rises out of a lava pool)
            ground = best;
            return true;
        }

        bool ClearBetween(Vector3 a, Vector3 b)
        {
            foreach (RaycastHit h in Physics.RaycastAll(a, b - a, (b - a).magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (IsWorld(h.collider)) return false;
            return true;
        }

        bool IsWorld(Collider c) => c != null && c.attachedRigidbody == null && head != null && !c.transform.IsChildOf(head.root);

        // At zero health: the view darkens, Ember gets the player up, and the light throws the shadows back.
        IEnumerator GetUp()
        {
            Debug.Log("[Rift] The Weaver is down");
            yield return ScreenFade.To(new Color(0.15f, 0f, 0.02f, 0.85f), 0.4f);
            yield return Line("Get up, Weaver! The light doesn't give up, and neither do you!", 0.6f);
            if (head != null)
            {
                foreach (ShadowMonster m in ShadowMonster.Alive.ToArray())
                {
                    m.Stagger(2f);
                    m.Shove(StoryFx.Flat(m.transform.position - head.position) * 4f);
                }
                foreach (KalakhWarrior k in KalakhWarrior.Alive.ToArray()) k.Stagger(2.5f);
            }
            if (ember != null) ember.Burst(120);
            player.Revive();
            yield return ScreenFade.To(new Color(0.15f, 0f, 0.02f, 0f), 0.8f);
        }

        bool Dies() => hunt != null && !ending && !string.IsNullOrEmpty(restartScene) && Application.CanStreamedLevelBeLoaded(restartScene);

        // Killed in the hunt: the world slows, the monsters roar over the fallen Weaver, the light goes out, and it all
        // starts again from the cave: the Weaver wakes there, still suited and armed, and Ember asks again whether to
        // train first or go straight back out to the monsters (Ascension.ReturnAfterFall).
        IEnumerator Death()
        {
            ending = dying = true;
            Debug.Log($"[Rift] The Weaver has fallen: back to {restartScene}");
            player.Status = "";
            foreach (KalakhWarrior k in KalakhWarrior.Alive.ToArray()) k.Triumph();
            CombatFx.Rumble(null, 1f, 0.6f);
            Time.timeScale = 0.3f;
            yield return ScreenFade.To(new Color(0.35f, 0f, 0.02f, 0.6f), 0.25f);
            yield return new WaitForSecondsRealtime(1.4f);
            Time.timeScale = 1f;
            if (ember != null) ember.SetMood(EmberFlame.Mood.Wild);
            yield return ScreenFade.To(new Color(0.05f, 0f, 0f, 0.92f), 1.2f);
            Card("YOU HAVE FALLEN");
            yield return Line("Weaver! No... stay with me!", 0.4f);
            yield return Line("The light isn't done with you. I'll take you back to the temple. Rise again, and this time we win.", 0.8f);
            yield return ScreenFade.To(Color.black, 1.2f);
            Ascension.ReturnAfterFall = true; // (the outfit and sword stay carried: no second Ascension)
            ScreenFade.LoadScene(restartScene, Color.black, 0.1f, 1.5f);
        }

        // ---------------------------------------------------------------- the ending

        // The monster world: the Warlord is down; the sky rumbles, Ember glows, chapter complete.
        IEnumerator HuntEnding()
        {
            ending = true;
            foreach (KalakhWarrior k in KalakhWarrior.Alive.ToArray()) k.Kill();
            player.Status = "";
            Debug.Log("[Rift] The Kalakh Warlord falls: the hunt is over.");
            yield return new WaitForSeconds(1.5f);
            if (ember != null) ember.SetMood(EmberFlame.Mood.Excited);
            yield return Line("It's down. The Warlord is down!", 1f);
            CombatFx.Rumble(null, 0.6f, 1.5f);
            CombatAudio.Play(CombatAudio.Roar, head != null ? head.position + StoryFx.Flat(head.forward) * 60f : transform.position, 0.6f, 0f);
            yield return Line("Hear that? This whole world felt it. Every Kalakh in this world knows your name now, Weaver.", 1.2f);
            yield return ScreenFade.To(new Color(0f, 0f, 0f, 1f), 2f);
            var powers = FindAnyObjectByType<PowerUnlocks>();
            if (powers != null) powers.Unlock(Power.WeaverSword);
            Card("CHAPTER 1 COMPLETE\nThe Awakening\n\nPrize: the Jyoti Blade");
            yield return ScreenFade.To(new Color(0f, 0f, 0f, 0.75f), 1.5f);
            yield return Line("That blade is yours now. You earned it.", 2.5f);
            OnChapterComplete?.Invoke();
            if (!string.IsNullOrEmpty(nextScene) && Application.CanStreamedLevelBeLoaded(nextScene)) ScreenFade.LoadScene(nextScene, Color.black, 1.5f);
        }

        IEnumerator Ending()
        {
            if (hunt != null)
            {
                yield return HuntEnding();
                yield break;
            }
            ending = true;
            foreach (ShadowMonster m in ShadowMonster.Alive.ToArray()) m.Kill();
            player.Status = "";
            Debug.Log($"[Rift] The last shadow falls ({killed}/{total} destroyed). The Rift begins to close.");
            yield return Line("That's... that's the last of them. You did it!", 1f);
            CombatFx.Rumble(null, 0.6f, 1.2f);
            CombatAudio.Play(CombatAudio.Roar, head != null ? head.position + StoryFx.Flat(head.forward) * 14f : transform.position, 0.5f, 0f);
            yield return Line("Wait. The Rift... it's closing. And something is coming through the far end.", 0.6f);

            // The far tear splits open ahead; the haze thickens as the Rift starts to fold up.
            Vector3 fwd = head != null ? StoryFx.Flat(head.forward) : Vector3.forward;
            Vector3 eyesFlat = head != null ? new Vector3(head.position.x, head.root.position.y, head.position.z) : transform.position;
            if (farEnd == null)
                farEnd = RiftTear.Create(eyesFlat + fwd * 13f, Quaternion.LookRotation(-fwd), 4.5f, 2.2f);
            else
            {
                // Turn the player's attention to it: it sits ahead of wherever they're looking from now on.
                farEnd.transform.SetPositionAndRotation(eyesFlat + fwd * 13f, Quaternion.LookRotation(-fwd));
            }
            StartCoroutine(Thicken(20f));
            yield return farEnd.Open(2f);
            if (entrance != null) StartCoroutine(entrance.Close(14f));

            // The mini boss steps out and walks right up to the player.
            Vector3 toPlayer = (eyesFlat - farEnd.transform.position).normalized;
            var boss = RiftBoss.Create(farEnd.transform.position - toPlayer * 0.6f, Quaternion.LookRotation(toPlayer), bossModel);
            Debug.Log("[Rift] The mini boss comes through");
            if (ember != null)
            {
                ember.SetMood(EmberFlame.Mood.Asleep); // Ember shrinks back
                StartCoroutine(Glide(ember.transform, head.position - fwd * 0.3f - Vector3.up * 0.9f + Vector3.Cross(Vector3.up, fwd) * -0.5f, 1f));
            }
            StartCoroutine(Line("Don't. Move.", 2f));
            yield return boss.WalkTo(eyesFlat - toPlayer * 1.7f, 1.1f);
            yield return boss.Lean(head, 1.8f);
            yield return new WaitForSeconds(1.2f);
            StartCoroutine(PulseDark(2.6f));
            yield return boss.Roar();

            // It speaks.
            var voice = boss.gameObject.AddComponent<StorySpeech>();
            voice.Configure("Kalakh", new Color(1f, 0.35f, 0.4f, 1f), 0.45f, Vector3.up * 1.2f);
            yield return SayAt(voice, boss, "Weaver... So the light has found a new keeper.", 1.2f);
            yield return SayAt(voice, boss, "Keep your little blade. We will come for it... and for you.", 1.4f);

            // The Rift seals: the boss is dragged back into the far tear, which snaps shut in a flash.
            Debug.Log("[Rift] The Rift seals");
            StartCoroutine(boss.PulledBack(farEnd.transform.position - toPlayer * 2f, 1.3f));
            yield return new WaitForSeconds(0.6f);
            StartCoroutine(farEnd.Close(0.8f));
            yield return new WaitForSeconds(0.7f);
            yield return ScreenFade.To(Color.white, 0.25f);
            if (entrance != null) entrance.SetOpenness(0f);
            farEnd.SetOpenness(0f);
            yield return ScreenFade.To(new Color(0f, 0f, 0f, 1f), 1.2f);

            // Chapter complete: the sword is the prize.
            var powers = FindAnyObjectByType<PowerUnlocks>();
            if (powers != null) powers.Unlock(Power.WeaverSword);
            Debug.Log("[Rift] Chapter 1 complete. Prize: the Jyoti Blade" + (powers == null ? " (no PowerUnlocks in the scene)" : ""));
            Card("CHAPTER 1 COMPLETE\nThe Awakening\n\nPrize: the Jyoti Blade");
            yield return ScreenFade.To(new Color(0f, 0f, 0f, 0.75f), 1.5f);
            if (ember != null)
            {
                ember.SetMood(EmberFlame.Mood.Excited);
                StartCoroutine(Glide(ember.transform, head.position + StoryFx.Flat(head.forward) * 1.1f - Vector3.up * 0.75f + Vector3.Cross(Vector3.up, StoryFx.Flat(head.forward)) * -0.5f, 1.2f));
            }
            yield return Line("It's sealed... for now. And that blade is yours, Weaver. You earned it.", 2.5f);
            OnChapterComplete?.Invoke();
            if (!string.IsNullOrEmpty(nextScene) && Application.CanStreamedLevelBeLoaded(nextScene))
            {
                ScreenFade.LoadScene(nextScene, Color.black, 1.5f);
            }
            else Debug.Log("[Rift] No next scene set (RiftArena → Next Scene): staying here.");
        }

        IEnumerator SayAt(StorySpeech voice, RiftBoss boss, string line, float hold)
        {
            voice.TextOffset = boss.transform.InverseTransformPoint(boss.Eyes) + Vector3.up * 0.5f;
            yield return voice.Say(line, hold);
        }

        IEnumerator Thicken(float seconds)
        {
            float from = RenderSettings.fogDensity;
            for (float t = 0f; t < seconds && ending; t += Time.deltaTime)
            {
                RenderSettings.fogDensity = Mathf.Lerp(from, from * 2.2f, t / seconds);
                yield return null;
            }
        }

        // The roar darkens the view in throbs.
        IEnumerator PulseDark(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = Mathf.Abs(Mathf.Sin(t * 6f)) * Mathf.Sin(Mathf.Clamp01(t / seconds) * Mathf.PI);
                ScreenFade.Set(new Color(0.1f, 0f, 0.05f, 0.45f * k));
                yield return null;
            }
            ScreenFade.Set(new Color(0f, 0f, 0f, 0f));
        }

        // Big words floating in front of the eyes (over the dark).
        void Card(string text)
        {
            if (head == null) return;
            var go = new GameObject("ChapterCard");
            go.transform.SetParent(head, false);
            go.transform.localPosition = new Vector3(0f, 0.15f, 1.4f);
            var mesh = go.AddComponent<TextMesh>();
            mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mesh.font.material;
            r.sortingOrder = 100;
            mesh.text = text;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.fontSize = 64;
            mesh.characterSize = 0.012f;
            mesh.color = new Color(1f, 0.85f, 0.5f, 1f);
        }

        // ---------------------------------------------------------------- Ember

        void SpawnEmber()
        {
            if (head == null) return;
            var go = new GameObject("Ember");
            Vector3 fwd = StoryFx.Flat(head.forward), left = Vector3.Cross(fwd, Vector3.up);
            go.transform.position = head.position + left * 0.7f + fwd * 0.6f - Vector3.up * 0.75f;
            ember = go.AddComponent<EmberFlame>();
            ember.Height = 0.6f;
            emberSpeech = go.AddComponent<StorySpeech>();
            emberSpeech.TextOffset = Vector3.up * 0.9f;
            ember.LookAt(head);
            ember.SetMood(EmberFlame.Mood.Calm);
            ember.Burst(60);
        }

        // Ember drifts over to a spot (eased), rather than jumping there.
        static IEnumerator Glide(Transform t, Vector3 to, float seconds)
        {
            Vector3 from = t.position;
            for (float time = 0f; time < seconds && t != null; time += Time.deltaTime)
            {
                t.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, time / seconds));
                yield return null;
            }
            if (t != null) t.position = to;
        }

        void Update()
        {
            // Ember keeps to the player's left, just ahead, lighting the way.
            if (ember != null && head != null && !ending && (ascension == null || !ascension.IsRunning))
            {
                Vector3 fwd = StoryFx.Flat(head.forward), left = Vector3.Cross(fwd, Vector3.up);
                Vector3 spot = head.position + left * 0.75f + fwd * 0.5f - Vector3.up * 0.75f;
                ember.transform.position = Vector3.Lerp(ember.transform.position, spot, Time.deltaTime * 1.5f);
                ember.SetMood(ShadowMonster.Alive.Count > 2 || KalakhWarrior.Alive.Count > 0 ? EmberFlame.Mood.Excited : EmberFlame.Mood.Calm);
            }

            if (!keyboardFallback) return;
            if (GameInput.Pressed(GameAction.Skip))
            {
                if (ascension != null && ascension.IsRunning) ascension.Skip();
                else if (emberSpeech != null && emberSpeech.IsSpeaking) emberSpeech.SkipLine();
            }
            if (GameInput.Pressed(GameAction.DebugKillAll))
            {
                foreach (ShadowMonster m in ShadowMonster.Alive.ToArray()) m.Kill();
                foreach (KalakhWarrior k in KalakhWarrior.Alive.ToArray()) // (first down to the next one's cue, then down)
                    if (k.HealthFraction <= 0.2f) k.Kill();
                    else k.Weaken(0.15f);
            }
            // F10 (End too: on Linux the editor's GTK menu bar can swallow F10).
            var kb = Keyboard.current;
            if (GameInput.Pressed(GameAction.DebugSkipToEnd) || (kb != null && kb.endKey.wasPressedThisFrame))
                WinNow();
        }

        /// <summary>For the demo video (F10 / End, or Arcane Strike → Demo Video → Win the Fight): every monster dies at
        /// once, no more come, and the ending plays (the Warlord falls, CHAPTER 1 COMPLETE) and goes on to Chapter 2.</summary>
        public void WinNow()
        {
            if (ending || dying)
            {
                Debug.Log("[Rift] F10: the ending is already playing.");
                return;
            }
            if (ascension != null && ascension.IsRunning)
            {
                Debug.Log("[Rift] F10: wait for the Ascension to finish (N skips it).");
                return;
            }
            Debug.Log("[Rift] F10: every monster falls; on to the ending and Chapter 2.");
            ending = true;
            StopAllCoroutines();
            if (hunt != null) hunt.StopAllCoroutines(); // (no more waves leaping in)
            foreach (KalakhWarrior k in KalakhWarrior.Alive.ToArray()) k.Kill();
            foreach (ShadowMonster m in ShadowMonster.Alive.ToArray()) m.Kill();
            if (ScreenFade.Current.a > 0.01f) ScreenFade.Set(StoryFx.WithAlpha(ScreenFade.Current, 0f));
            if (ember == null) SpawnEmber();
            StartCoroutine(Ending());
        }

        void OnDestroy()
        {
            if (Time.timeScale != 1f) Time.timeScale = 1f; // (never carry the fall's slow motion into the next scene)
        }

        IEnumerator Line(string text, float hold)
        {
            if (emberSpeech == null)
            {
                Debug.Log($"[Ember] {text}");
                yield break;
            }
            yield return emberSpeech.Say(text, hold);
        }
    }
}
