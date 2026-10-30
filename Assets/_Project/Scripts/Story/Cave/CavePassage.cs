using System.Collections;
using System.Collections.Generic;
using ArcaneStrike.HandInput;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The way out of the Ascension temple: a bridge to the shrine, closed off by a sealed gate until the Ascension is
    /// over. <see cref="Open"/> breaks the gate (its stone tumbles aside in a cloud of dust, with a deep rumble) and the
    /// threshold in the shrine tears open, sending the monster world's light down the bridge. Walking into it (or E close by) carries the player through into <see cref="nextScene"/>.
    /// </summary>
    public class CavePassage : MonoBehaviour
    {
        [Tooltip("The gate blocking the way (a mesh that breaks into stone).")]
        public Renderer rocks;
        [Tooltip("The shimmering way through at the tunnel's far end.")]
        public RiftTear threshold;
        [Tooltip("The scene on the other side (the monster world).")]
        public string nextScene = "MonsterWorld";
        [SerializeField] Color worldLight = new Color(1f, 0.45f, 0.15f, 1f);
        [Tooltip("How close (m, across the floor) the player's head must come to the threshold to step through.")]
        [SerializeField] float enterDistance = 0.9f;

        public bool IsOpen { get; private set; }
        /// <summary>The player is stepping through (the next scene is on its way).</summary>
        public bool Entering => entering;

        Light glow;
        Transform head;
        bool entering, opening;
        float retryAt; // (the next scene missing: don't fade again every frame while standing in the threshold)
        Vector3 mouth; // the tunnel's mouth on the floor (where the pile stood)
        readonly List<GameObject> debris = new List<GameObject>();
        readonly RaycastHit[] hits = new RaycastHit[16];

        void Start()
        {
            if (threshold != null) threshold.SetOpenness(0f);
            mouth = rocks != null ? rocks.bounds.center : transform.position;
            if (threshold != null) mouth.y = threshold.transform.position.y;
        }

        /// <summary>Brings the rock pile down and opens the way through.</summary>
        public IEnumerator Open()
        {
            if (IsOpen || opening) yield break;
            opening = true;
            Debug.Log("[Cave] The sealed gate breaks: the way out opens");
            Vector3 pile = rocks != null ? rocks.bounds.center : transform.position;
            var ears = new GameObject("PassageRumble");
            ears.transform.position = pile;
            AudioSource rumble = StoryFx.CreateSource(ears, StoryFx.Rumble(), false, 0.8f);
            rumble.volume = 1f;
            rumble.maxDistance = 25f;
            rumble.Play();
            Combat.CombatFx.Rumble(null, 0.7f, 1.2f);

            // A moment of trembling and trickling dust before it goes.
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                if (rocks != null && Random.value < 0.6f) Combat.CombatFx.SmokePuff(pile + Random.insideUnitSphere * 1.2f, 0.3f, 1, 0.6f);
                yield return null;
            }
            if (rocks != null) Collapse(rocks);
            for (int i = 0; i < 6; i++) Combat.CombatFx.SmokePuff(pile + Random.insideUnitSphere * 1.0f, 1.2f, 8, 2.5f);
            Combat.CombatAudio.Play(Combat.CombatAudio.HeavySwoosh, pile, 0.8f, 0.1f);

            // The far end: the threshold tears open and its light pours down the tunnel.
            if (threshold != null)
            {
                glow = new GameObject("WorldLight").AddComponent<Light>();
                glow.transform.position = threshold.Centre;
                glow.type = LightType.Point;
                glow.color = worldLight;
                glow.range = 12f;
                glow.shadows = LightShadows.None;
                StartCoroutine(threshold.Open(2.5f));
                for (float t = 0f; t < 2.5f; t += Time.deltaTime)
                {
                    glow.intensity = Mathf.SmoothStep(0f, 4f, t / 2.5f);
                    yield return null;
                }
            }
            IsOpen = true;
            Destroy(ears, 4f);
        }

        // The pile breaks into its boulders: rock chunks that tumble down and roll aside, then settle and sink away.
        void Collapse(Renderer pile)
        {
            Bounds b = pile.bounds;
            Material stone = pile.sharedMaterial;
            pile.enabled = false;
            foreach (Collider c in pile.GetComponents<Collider>()) c.enabled = false;
            for (int i = 0; i < 18; i++)
            {
                var chunk = GameObject.CreatePrimitive(i % 3 == 0 ? PrimitiveType.Cube : PrimitiveType.Sphere);
                chunk.name = "PassageRock";
                chunk.transform.position = new Vector3(Random.Range(b.min.x, b.max.x), Random.Range(b.min.y + 0.3f, b.max.y), Random.Range(b.min.z, b.max.z));
                chunk.transform.rotation = Random.rotation;
                float s = Random.Range(0.3f, 0.75f);
                chunk.transform.localScale = new Vector3(s, s * Random.Range(0.6f, 1f), s * Random.Range(0.7f, 1.1f));
                chunk.GetComponent<Renderer>().sharedMaterial = stone;
                var body = chunk.AddComponent<Rigidbody>();
                body.mass = 50f * s;
                Vector3 away = transform.position - b.center; // roll out into the cave, not into the tunnel
                away.y = 0f;
                body.linearVelocity = away.normalized * Random.Range(0.5f, 2f) + Random.insideUnitSphere * 0.5f;
                body.angularVelocity = Random.insideUnitSphere * 2f;
                debris.Add(chunk);
            }
            StartCoroutine(SettleDebris());
        }

        // After a few seconds the rubble stops being physical (so nothing trips the walk out) and sinks into the floor.
        IEnumerator SettleDebris()
        {
            yield return new WaitForSeconds(5f);
            foreach (GameObject d in debris)
                if (d != null)
                {
                    Destroy(d.GetComponent<Rigidbody>());
                    Destroy(d.GetComponent<Collider>());
                }
            for (float t = 0f; t < 3f; t += Time.deltaTime)
            {
                foreach (GameObject d in debris)
                    if (d != null) d.transform.position += Vector3.down * Time.deltaTime * 0.12f;
                yield return null;
            }
            foreach (GameObject d in debris)
                if (d != null) Destroy(d);
        }

        void Update()
        {
            if (!IsOpen || entering || threshold == null || Time.time < retryAt) return;
            if (head == null) head = StoryFx.Head(FindAnyObjectByType<HandInputService>());
            if (head == null) return;
            if (glow != null) glow.intensity = 4f * (0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 2f, 0f)); // a flicker of firelight
            Vector3 d = head.position - threshold.transform.position;
            d.y = 0f;
            if (d.magnitude < enterDistance || (GameInput.Pressed(GameAction.Interact) && d.magnitude < 3f)) StartCoroutine(Through());
        }

        /// <summary>
        /// Leads the player out of the cave: they glide at a slow walking pace (eased, the view's edge dimmed) round
        /// whatever stands in the way, up the tunnel and into the threshold, which takes them through. In a headset
        /// the view never turns on its own; on a screen it turns gently to look down the way ahead.
        /// </summary>
        public IEnumerator WalkOut(Transform player, float speed = 1.1f)
        {
            if (threshold == null || player == null || entering) yield break;
            Transform rig = player.root;
            bool headset = player.position.y - rig.position.y > 0.3f;
            var locomotion = FindAnyObjectByType<PlayerLocomotion>();
            if (locomotion != null) locomotion.Paused = true;
            List<Vector3> path = PathOut(player.position, rig);
            float length = 0f;
            for (int i = 1; i < path.Count; i++) length += Vector3.Distance(path[i - 1], path[i]);
            Debug.Log($"[Cave] Ember leads the Weaver out: {length:F1} m through the tunnel");
            ComfortVignette.Set(0.45f);
            float eyes = headset ? 0f : 1.6f; // (on a screen the rig is at eye level)
            float y = rig.position.y;
            Vector3 was = path[0];
            for (float s = 0f, t = 0f; s < length && !entering; t += Time.deltaTime)
            {
                s += speed * Mathf.SmoothStep(0.15f, 1f, t / 1.2f) * Time.deltaTime; // eases into the walk
                Vector3 at = PointAlong(path, s, out Vector3 ahead);
                // The rig moves along the path by as much as the path moved: the player's own steps and leaning in
                // their room still count (never pulled back against them, which is what makes a headset sickening).
                Vector3 step = at - was;
                was = at;
                step.y = 0f;
                rig.position += step;
                if (FloorAt(at, rig, out float floor)) y = Mathf.Lerp(y, floor + eyes, 1f - Mathf.Exp(-8f * Time.deltaTime));
                rig.position = new Vector3(rig.position.x, y, rig.position.z);
                if (!headset && ahead.sqrMagnitude > 1e-4f)
                {
                    float turn = Vector3.SignedAngle(StoryFx.Flat(player.forward), ahead, Vector3.up);
                    rig.RotateAround(player.position, Vector3.up, turn * (1f - Mathf.Exp(-2.5f * Time.deltaTime)));
                }
                yield return null;
            }
            ComfortVignette.Set(0f);
            if (!entering && locomotion != null) locomotion.Paused = false; // (Update takes them through at the end)
        }

        // Around the altar or anything else in the way to a spot just in front of the mouth, then up the tunnel to the
        // threshold.
        List<Vector3> PathOut(Vector3 from, Transform rig)
        {
            Vector3 end = threshold.transform.position;
            Vector3 down = StoryFx.Flat(end - mouth).normalized; // down the tunnel
            Vector3 approach = mouth - down * 2f;
            Vector3 start = new Vector3(from.x, end.y, from.z);
            var path = new List<Vector3> { start };
            if (!Clear(start, approach, rig))
            {
                Vector3 across = Vector3.Cross(Vector3.up, StoryFx.Flat(approach - start).normalized);
                Vector3 middle = (start + approach) * 0.5f;
                foreach (float k in new[] { 1.5f, -1.5f, 2.5f, -2.5f, 3.5f, -3.5f })
                {
                    Vector3 round = middle + across * k;
                    if (Clear(start, round, rig) && Clear(round, approach, rig))
                    {
                        path.Add(round);
                        break;
                    }
                }
            }
            path.Add(approach);
            path.Add(mouth);
            path.Add(end);
            return path;
        }

        // Is there room for a body to walk from a to b (on the floor at the threshold's height)?
        bool Clear(Vector3 a, Vector3 b, Transform rig)
        {
            Vector3 lift = Vector3.up * 0.9f, d = b - a;
            int n = Physics.SphereCastNonAlloc(a + lift, 0.35f, d.normalized, hits, d.magnitude, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (Solid(hits[i].collider, rig) && hits[i].distance > 0f) return false;
            return true;
        }

        bool FloorAt(Vector3 at, Transform rig, out float floor)
        {
            floor = float.MinValue;
            int n = Physics.RaycastNonAlloc(at + Vector3.up * 1.5f, Vector3.down, hits, 4f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (Solid(hits[i].collider, rig) && hits[i].normal.y > 0.6f) floor = Mathf.Max(floor, hits[i].point.y);
            return floor > float.MinValue;
        }

        static bool Solid(Collider c, Transform rig) => c != null && c.enabled && c.attachedRigidbody == null && !c.transform.IsChildOf(rig);

        static Vector3 PointAlong(List<Vector3> path, float s, out Vector3 ahead)
        {
            for (int i = 1; i < path.Count; i++)
            {
                float leg = Vector3.Distance(path[i - 1], path[i]);
                if (s <= leg || i == path.Count - 1)
                {
                    ahead = StoryFx.Flat(path[i] - path[i - 1]).normalized;
                    return Vector3.Lerp(path[i - 1], path[i], leg > 0f ? Mathf.Clamp01(s / leg) : 1f);
                }
                s -= leg;
            }
            ahead = Vector3.zero;
            return path[path.Count - 1];
        }

        // Through the threshold: the view floods with the monster world's light, and the next scene loads behind it.
        IEnumerator Through()
        {
            entering = true;
            Debug.Log($"[Cave] The Weaver steps through the threshold into {nextScene}");
            var locomotion = FindAnyObjectByType<PlayerLocomotion>();
            if (locomotion != null) locomotion.Paused = true;
            yield return ScreenFade.To(StoryFx.WithAlpha(worldLight, 0.6f), 0.7f);
            if (!string.IsNullOrEmpty(nextScene) && Application.CanStreamedLevelBeLoaded(nextScene))
            {
                ScreenFade.LoadScene(nextScene, new Color(0.25f, 0.06f, 0.02f, 1f), 0.8f);
                yield break;
            }
            Debug.LogWarning($"[Cave] Scene '{nextScene}' isn't in Build Settings yet: run Arcane Strike → Create Monster World Scene.");
            yield return ScreenFade.To(new Color(0f, 0f, 0f, 0f), 1f);
            if (locomotion != null) locomotion.Paused = false;
            retryAt = Time.time + 3f;
            entering = false;
        }
    }
}
