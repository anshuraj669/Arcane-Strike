using ArcaneStrike.Combat;
using ArcaneStrike.HandInput;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The lava burns. Nothing stops the player walking into a pool: step in and the health drains fast for as long as
    /// they stand in it (the view flushes orange, the hands rumble, it sizzles and smokes round the feet). Coming close
    /// warns them first: the heat glows at the edge of the view and the controllers buzz. If the lava takes all their
    /// health, they get up again on the last solid rock they stood on. Lava is anything with a <see cref="LavaFlow"/>
    /// and a collider.
    /// </summary>
    public class LavaHazard : MonoBehaviour
    {
        [Tooltip("Health lost per second while standing in lava (of 100).")]
        [SerializeField] float burnPerSecond = 22f;
        [Tooltip("The heat warns this close to lava (m).")]
        [SerializeField] float warnDistance = 0.6f;
        [SerializeField] Color heat = new Color(1f, 0.35f, 0.05f, 1f);

        public static bool InLava { get; private set; }

        Transform head, rig;
        HandInputService input;
        RiftPlayer player;
        ParticleSystem sizzle;
        AudioSource hiss;
        Vector3 safeSpot;
        bool hasSafeSpot, wasDown, warnedOnce;
        float burnTick, nearness;
        readonly RaycastHit[] hits = new RaycastHit[16];
        static readonly Vector3[] Around = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
                                             new Vector3(0.7f, 0f, 0.7f), new Vector3(-0.7f, 0f, 0.7f), new Vector3(0.7f, 0f, -0.7f), new Vector3(-0.7f, 0f, -0.7f) };

        void Start()
        {
            input = FindAnyObjectByType<HandInputService>();
            sizzle = StoryFx.CreateParticles(transform, "LavaSizzle", false, -0.03f, 200);
        }

        void Update()
        {
            if (head == null)
            {
                head = StoryFx.Head(input);
                if (head == null) return;
                rig = head.root;
                hiss = StoryFx.CreateSource(head.gameObject, StoryFx.Rumble(), true, 0f);
                hiss.volume = 0f;
                hiss.Play();
            }
            if (player == null) player = RiftPlayer.Instance;
            float dt = Time.deltaTime;
            bool headset = head.position.y - rig.position.y > 0.3f;
            float feet = headset ? rig.position.y : head.position.y - 1.6f;
            Vector3 under = new Vector3(head.position.x, feet, head.position.z);

            InLava = LavaUnder(under, feet, out Vector3 surface);
            if (!InLava && Grounded(under, feet))
            {
                safeSpot = rig.position;
                hasSafeSpot = true;
            }

            // Near the edge: the heat comes up at the edge of the view and the controllers buzz a little.
            float near = 0f;
            if (!InLava)
                foreach (Vector3 d in Around)
                    if (LavaUnder(under + d * warnDistance, feet, out _)) near = Mathf.Max(near, 0.5f);
            nearness = Mathf.MoveTowards(nearness, InLava ? 1f : near, dt * 3f);

            if (InLava && player != null && !player.Down)
            {
                player.Burn(burnPerSecond * dt);
                burnTick -= dt;
                if (burnTick <= 0f)
                {
                    burnTick = 0.25f;
                    CombatFx.Rumble(null, 0.8f, 0.2f);
                    for (int i = 0; i < 4; i++) // smoke and sparks round the feet
                        StoryFx.Emit(sizzle, surface + new Vector3(Random.Range(-0.3f, 0.3f), 0.05f, Random.Range(-0.3f, 0.3f)),
                                     new Vector3(0f, Random.Range(0.6f, 1.2f), 0f), Random.Range(0.15f, 0.35f), Random.Range(0.8f, 1.4f),
                                     new Color(0.25f, 0.18f, 0.15f, 0.6f));
                }
                if (!warnedOnce)
                {
                    warnedOnce = true;
                    Debug.Log("[Rift] The lava burns! Get out of it.");
                    CombatFx.Popup("HOT!", head.position + head.forward * 0.8f, heat, 1.4f);
                }
            }
            else if (nearness > 0.3f && Random.value < dt * 2f) CombatFx.Rumble(null, 0.15f, 0.08f);
            hiss.volume = Mathf.MoveTowards(hiss.volume, InLava ? 0.6f : nearness * 0.15f, dt * 2f);
            ComfortVignette.Tint(heat, nearness * (InLava ? 0.9f : 0.5f));

            // Burned down in the lava: back on their feet on the last solid rock they stood on.
            if (player != null)
            {
                if (wasDown && !player.Down && hasSafeSpot && InLava)
                {
                    rig.position = safeSpot;
                    Physics.SyncTransforms();
                    Debug.Log("[Rift] Up again on solid rock, out of the lava");
                }
                wasDown = player.Down;
            }
        }

        // Is the first thing under this point lava, at about the feet's height (standing in it)?
        bool LavaUnder(Vector3 at, float feet, out Vector3 surface)
        {
            surface = at;
            if (!FirstBelow(at, out RaycastHit hit)) return false;
            surface = hit.point;
            return hit.collider.GetComponent<LavaFlow>() != null && hit.point.y > feet - 0.6f;
        }

        bool Grounded(Vector3 at, float feet) => FirstBelow(at, out RaycastHit hit) && hit.point.y > feet - 0.4f;

        bool FirstBelow(Vector3 at, out RaycastHit first)
        {
            first = default;
            int n = Physics.RaycastNonAlloc(at + Vector3.up * 0.8f, Vector3.down, hits, 3f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Collider c = hits[i].collider;
                if (c == null || c.attachedRigidbody != null || c.transform.IsChildOf(rig)) continue;
                if (hits[i].distance < best)
                {
                    best = hits[i].distance;
                    first = hits[i];
                }
            }
            return best < float.MaxValue;
        }

        void OnDisable()
        {
            InLava = false;
            ComfortVignette.Tint(heat, 0f);
        }
    }
}
