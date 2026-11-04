using System;
using System.Collections;
using System.Collections.Generic;
using ArcaneStrike.Combat;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The monster world's fight: fifteen Ashhorn soldiers in waves of one, two, four and eight, then their Warlord. A
    /// wave only comes when everyone in the last one is down to a fifth of their health (or dead). Soldiers leap in from
    /// random directions all round the player, over the ledge's edge, and no two of them share an attack
    /// (<see cref="KalakhMoves.DealDecks"/>, dealt afresh every fight). Which attack comes next, and who comes in to
    /// make it, is random too (<see cref="KalakhWarrior"/>). When all fifteen are dead the Warlord leaps in over the
    /// edge in front of the player. Ember calls out each wave. Done when the Warlord falls.
    /// </summary>
    public class KalakhHunt : MonoBehaviour
    {
        [Serializable]
        public class Foe
        {
            public string title;
            [Range(1, 5)] public int tier = 1;
            public float health = 150f;
            [Tooltip("Metres, feet to the very top (antlers and horns included).")]
            public float height = 2.1f;
            public bool boss;
            [Tooltip("Which kind of Kalakh (its own body and its own signature techniques).")]
            public string breed;
            [Tooltip("Its body (empty: the hunt's model, or the boss model).")]
            public GameObject model;
            [Tooltip("The kinds of technique it reaches for more often: how it fights.")]
            public MoveKind[] favour = new MoveKind[0];
            [Tooltip("Ember, as it breaks out.")]
            [TextArea] public string[] emberLines;
        }

        [Tooltip("How many soldiers in each wave, in order.")]
        public int[] waves = { 1, 2, 4, 8 };

        public Foe soldier = new Foe { title = "Ashhorn", tier = 1, health = 100f, height = 2.9f, breed = "Ashhorn" };

        public Foe boss = new Foe { title = "Kalakh Warlord", tier = 5, health = 620f, height = 4.2f, boss = true, breed = "DemonLord",
                                    favour = new[] { MoveKind.Eruption, MoveKind.Breath, MoveKind.Spin, MoveKind.Berserk }, emberLines = new[]
            {
                "That's... their Warlord. Every one of those soldiers learned from it.",
                "Wear it down, then finish it: when it reels, drive the blade home.",
            } };

        [Tooltip("Ember, as each wave comes (one line per wave).")]
        [TextArea] public string[] waveLines =
        {
            "Something's coming... over the edge! Hold your blade across to parry, then cut it while it reels.",
            "Two more! They won't fight the same way, so watch each one.",
            "Four! Keep turning: they'll come from anywhere.",
            "Eight of them! Don't let them get behind you, Weaver!",
        };

        public GameObject model;
        public GameObject bossModel;
        [Tooltip("The next one breaks out when the one fighting is down to this share of its health.")]
        [Range(0f, 1f)] public float nextAt = 0.2f;
        [Tooltip("Each Kalakh the Weaver kills gives back this share of the health they have at that moment.")]
        [Range(0f, 1f)] public float killHeal = 0.1f;

        public int Remaining { get; private set; }
        readonly List<KalakhWarrior> summoned = new List<KalakhWarrior>();
        bool finished;
        readonly System.Text.StringBuilder status = new System.Text.StringBuilder(96);
        int total;
        float nextStatus;

        /// <summary>The whole fight. `line` is Ember saying something (and waiting for it).</summary>
        public IEnumerator Run(Transform head, Func<string, float, IEnumerator> line, RiftPlayer player)
        {
            int soldiers = 0;
            foreach (int n in waves) soldiers += n;
            total = soldiers + 1;
            Remaining = total;
            finished = false;
            List<KalakhMove>[] decks = KalakhMoves.DealDecks(soldiers);
            int dealt = 0;

            for (int w = 0; w < waves.Length; w++)
            {
                while (player != null && player.Down) { Tick(player); yield return null; }
                if (w < waveLines.Length)
                {
                    if (w == 0) yield return line(waveLines[w], 0.6f);
                    else StartCoroutine(Lines(line, new[] { waveLines[w] }));
                }
                var wave = new List<KalakhWarrior>();
                // Each from its own direction: the circle cut into a slice per soldier, a random spot in each, and the
                // slices dealt out in random order so nobody can tell where the next will land.
                int count = waves[w];
                float offset = UnityEngine.Random.Range(0f, 360f), slice = 360f / count;
                var order = new List<int>();
                for (int i = 0; i < count; i++) order.Insert(UnityEngine.Random.Range(0, order.Count + 1), i);
                int landed = 0;
                for (int i = 0; i < count; i++)
                {
                    float angle = offset + order[i] * slice + UnityEngine.Random.Range(0f, slice * 0.7f);
                    StartCoroutine(Arrive(soldier, decks[dealt++], w, angle, head, player, k => { wave.Add(k); landed++; }));
                    float gap = i == count - 1 ? 0f : UnityEngine.Random.Range(0.15f, 0.9f);
                    for (float t = 0f; t < gap; t += Time.deltaTime) { Tick(player); yield return null; }
                }
                for (float wait = 0f; landed < count && wait < 15f; wait += Time.deltaTime) { Tick(player); yield return null; }
                // The next wave comes when all of this one are nearly done for.
                while (!AllSpent(wave)) { Tick(player); yield return null; }
            }

            // At last, when every soldier is dead, the Warlord.
            while (true)
            {
                bool clear = KalakhWarrior.Alive.Count == 0;
                if (clear && (player == null || !player.Down)) break;
                Tick(player);
                yield return null;
            }
            KalakhWarrior warlord = null;
            yield return Summon(boss, head, line, player, k => warlord = k);
            while (warlord != null && !warlord.Dead) { Tick(player); yield return null; }
            finished = true;
            if (player != null) player.Status = "";
        }

        bool AllSpent(List<KalakhWarrior> wave)
        {
            foreach (KalakhWarrior k in wave)
                if (k != null && !k.Dead && k.HealthFraction > nextAt) return false;
            return true;
        }

        // Keeps the count, and the bar over the fight, a few times a second (not a new string every frame).
        void Tick(RiftPlayer player)
        {
            int dead = 0;
            foreach (KalakhWarrior k in summoned)
                if (k == null || k.Dead) dead++;
            Remaining = total - dead;
            if (player == null || Time.unscaledTime < nextStatus) return;
            nextStatus = Time.unscaledTime + 0.25f;
            status.Clear().Append("Kalakh left ").Append(Remaining).Append('/').Append(total);
            if (KalakhWarrior.Alive.Count <= 3)
                foreach (KalakhWarrior k in KalakhWarrior.Alive)
                    status.Append("  ·  ").Append(k.Title).Append(' ').Append(Mathf.CeilToInt(k.HealthFraction * 100f)).Append('%');
            player.Status = status.ToString();
        }

        KalakhWarrior Make(Foe foe, string title, int tier, float health, float height, List<KalakhMove> deck, Vector3 at, Quaternion facing, RiftPlayer player)
        {
            GameObject body = foe.model;
#if UNITY_EDITOR
            // A scene built before this kind existed has no link to its body: find it by name rather than show the wrong one.
            if (body == null && !string.IsNullOrEmpty(foe.breed))
                body = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Art/Characters/Kalakh/Breeds/{foe.breed}.prefab");
#endif
            if (body == null)
            {
                Debug.LogError($"[Hunt] {title} has no model of its own ({foe.breed}): run Arcane Strike → Create Monster World Scene. Using the old body.");
                body = foe.boss && bossModel != null ? bossModel : model;
            }
            var warrior = KalakhWarrior.Create(body, title, tier, health, height, at, facing, foe.boss, foe.breed, foe.favour, deck);
            summoned.Add(warrior);
            // A kill feeds the Weaver: a tenth more of the health they have left (and nothing once they've fallen).
            warrior.OnDied += w =>
            {
                if (player != null && !player.Down && !finished) player.Heal(player.Health * killHeal);
            };
            return warrior;
        }

        // A soldier leaps in over the ledge's edge from this direction round the player, landing a few metres off.
        IEnumerator Arrive(Foe foe, List<KalakhMove> deck, int wave, float angle, Transform head, RiftPlayer player, Action<KalakhWarrior> done)
        {
            Vector3 centre = head.position;
            float floor = KalakhFx.Ground(centre, centre.y - 1.6f);
            Vector3 dir = Vector3.forward, land = centre;
            bool found = false;
            for (int tries = 0; tries < 16 && !found; tries++)
            {
                dir = Quaternion.Euler(0f, angle + tries * 27f, 0f) * Vector3.forward;
                land = centre + dir * UnityEngine.Random.Range(4.5f, 8f);
                if (!KalakhFx.Footing(land, floor, out float ground) || Mathf.Abs(ground - floor) > 1.5f) continue;
                land.y = ground;
                found = !Blocked(land);
            }
            if (!found)
            {
                land = centre + dir * 4.5f;
                land.y = KalakhFx.Ground(land, floor);
            }
            Vector3 start = land + dir * 8f + Vector3.down * 3f; // from below the edge, out of the dark
            float height = foe.height * UnityEngine.Random.Range(0.95f, 1.12f);
            int tier = Mathf.Clamp(foe.tier + wave, 1, 4);
            var warrior = Make(foe, foe.title, tier, foe.health + wave * 20f, height, deck, start, Quaternion.LookRotation(-dir), player);
            warrior.Body.Act(KalakhMoves.Air(), 0.1f, true);
            CombatAudio.Play(CombatAudio.Growl, start, 0.9f, 0.2f); // heard before it's seen: turn round!
            float time = UnityEngine.Random.Range(0.7f, 1f);
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                float k = t / time;
                warrior.transform.position = Vector3.Lerp(start, land, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 2.6f;
                FacePlayer(warrior, head);
                yield return null;
            }
            warrior.transform.position = land;
            FacePlayer(warrior, head);
            warrior.Body.Act(KalakhMoves.Land(), 0.08f, true);
            CombatFx.SmokePuff(land, 0.5f, 10, 0.5f);
            CombatFx.Rumble(null, 0.4f, 0.2f);
            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                FacePlayer(warrior, head);
                yield return null;
            }
            warrior.Body.Act(KalakhMoves.Taunt(), 0.25f, true);
            warrior.Body.LookTarget = head;
            CombatAudio.Play(CombatAudio.Roar, warrior.Body.Mouth, 0.8f, 0.25f);
            for (float t = 0f; t < 0.45f; t += Time.deltaTime)
            {
                FacePlayer(warrior, head);
                yield return null;
            }
            if (player != null && player.Down) warrior.Triumph(); // (the Weaver fell while it was coming in)
            else warrior.Activate(head);
            done(warrior);
        }

        // The Warlord: the ground rumbles and it roars from beyond the edge, then a huge leap in over it in front of the
        // player (straight ahead if there's room, else to one side), landing in a crouch, and a roar in their face.
        IEnumerator Summon(Foe foe, Transform head, Func<string, float, IEnumerator> line, RiftPlayer player, Action<KalakhWarrior> done)
        {
            if (foe.emberLines != null && foe.emberLines.Length > 0) StartCoroutine(Lines(line, foe.emberLines));
            Vector3 centre = head.position, fwd = StoryFx.Flat(head.forward);
            float floor = KalakhFx.Ground(centre, centre.y - 1.6f);
            Vector3 dir = fwd, land = centre + fwd * 5f;
            bool found = false;
            for (int tries = 0; tries < 12 && !found; tries++)
            {
                dir = Quaternion.Euler(0f, (tries % 2 == 0 ? 1f : -1f) * ((tries + 1) / 2) * 20f, 0f) * fwd;
                land = centre + dir * (4f + foe.height * 0.4f);
                if (!KalakhFx.Footing(land, floor, out float ground) || Mathf.Abs(ground - floor) > 1.5f) continue;
                land.y = ground;
                found = !Blocked(land);
            }
            if (!found) land.y = KalakhFx.Ground(land, floor);
            Debug.Log($"[Hunt] {foe.title} (tier {foe.tier}, {foe.health} health) leaps in over the edge");
            Vector3 start = land + dir * 12f + Vector3.down * 4f;
            CombatAudio.Play(CombatAudio.Roar, start, 1f, 0.1f); // heard before it's seen
            CombatFx.Rumble(null, 0.6f, 1.2f);
            for (float t = 0f; t < 1.4f; t += Time.deltaTime) yield return null;

            var warrior = Make(foe, foe.title, foe.tier, foe.health, foe.height, null, start, Quaternion.LookRotation(-dir), player);
            warrior.Body.Act(KalakhMoves.Air(), 0.1f, true);
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                float k = t / 1.2f;
                warrior.transform.position = Vector3.Lerp(start, land, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * (3.5f + foe.height * 0.5f);
                FacePlayer(warrior, head);
                yield return null;
            }
            warrior.transform.position = land;
            FacePlayer(warrior, head);
            warrior.Body.Act(KalakhMoves.Land(), 0.08f, true);
            CombatFx.SmokePuff(land, 0.6f, 15, 0.5f);
            KalakhFx.Shockwave(this, land, 2.5f, new Color(0.9f, 0.6f, 0.4f));
            CombatFx.Rumble(null, 0.8f, 0.25f);
            CombatFx.Popup("KALAKH WARLORD", land + Vector3.up * (foe.height + 0.8f), KalakhFx.Danger, 2f);
            for (float t = 0f; t < 0.35f; t += Time.deltaTime)
            {
                FacePlayer(warrior, head);
                yield return null;
            }
            warrior.Body.Act(KalakhMoves.Taunt(), 0.25f, true);
            warrior.Body.LookTarget = head;
            CombatAudio.Play(CombatAudio.Roar, warrior.Body.Mouth, 1f, 0.15f);
            CombatFx.Rumble(null, 0.5f, 0.3f);
            for (float t = 0f; t < 0.9f; t += Time.deltaTime)
            {
                FacePlayer(warrior, head);
                yield return null;
            }
            if (player != null && player.Down) warrior.Triumph();
            else warrior.Activate(head);
            done(warrior);
        }

        // Something of the world's in the way where a Kalakh would land (a rock, a wall of the ledge).
        static bool Blocked(Vector3 at)
        {
            Collider[] near = Physics.OverlapSphere(at + Vector3.up * 1.3f, 1f, ~0, QueryTriggerInteraction.Ignore);
            foreach (Collider c in near)
                if (KalakhFx.IsWorld(c)) return true;
            return false;
        }

        IEnumerator Lines(Func<string, float, IEnumerator> line, string[] lines)
        {
            foreach (string l in lines) yield return line(l, 0.8f);
        }

        static void FacePlayer(KalakhWarrior warrior, Transform head)
        {
            if (warrior == null || head == null) return;
            Vector3 to = StoryFx.Flat(head.position - warrior.transform.position);
            if (to.sqrMagnitude > 1e-4f) warrior.transform.rotation = Quaternion.LookRotation(to);
        }
    }
}
