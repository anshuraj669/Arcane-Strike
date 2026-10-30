using System.Collections.Generic;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Life on a lava surface (pools, the rivers on the plains): bubbles of molten rock swell up out of it and burst in a
    /// splash of glowing droplets and a puff of smoke; now and then a spurt of fire jets up; embers and thin smoke rise
    /// off it; and it crackles and hisses (<see cref="sounds"/>). Only near the player, to stay cheap. Needs a collider
    /// on the lava (the bubbles sit on its surface).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class LavaBubbles : MonoBehaviour
    {
        [Tooltip("Bubbles per second (within reach of the player).")]
        [SerializeField] float rate = 1.5f;
        [Tooltip("Bubble radius range (m).")]
        [SerializeField] Vector2 size = new Vector2(0.12f, 0.35f);
        [Tooltip("Only this close to the player (m), measured across the ground.")]
        [SerializeField] float reach = 25f;
        [Tooltip("Chance a burst is a spurt of fire instead.")]
        [Range(0f, 1f)] [SerializeField] float spurts = 0.15f;
        [SerializeField] Color hot = new Color(1f, 0.5f, 0.1f, 1f);
        [Tooltip("Where it crackles and hisses (a looping 3D sound at each).")]
        public Vector3[] sounds = new Vector3[0];

        class Bubble
        {
            public Transform t;
            public Vector3 at;
            public float radius, age, life;
        }

        Collider surface;
        Material skin;
        Transform head;
        ParticleSystem splash, smoke, embers;
        readonly List<Bubble> live = new List<Bubble>();
        readonly Stack<Transform> spare = new Stack<Transform>();
        float due;
        static AudioClip crackle;

        void Start()
        {
            surface = GetComponent<Collider>();
            var r = GetComponent<Renderer>();
            skin = r != null ? r.sharedMaterial : null;
            splash = StoryFx.CreateParticles(transform, "LavaSplash", true, 1.2f, 400);
            smoke = StoryFx.CreateParticles(transform, "LavaSmoke", false, -0.02f, 200);
            embers = StoryFx.CreateParticles(transform, "LavaEmbers", true, -0.05f, 300);
            ParticleSystem.SizeOverLifetimeModule billow = smoke.sizeOverLifetime;
            billow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 2f));
            if (crackle == null) crackle = Crackle();
            foreach (Vector3 p in sounds)
            {
                var go = new GameObject("LavaCrackle");
                go.transform.SetParent(transform, false);
                go.transform.position = p;
                AudioSource a = StoryFx.CreateSource(go, crackle, true, 1f);
                a.volume = 0.35f;
                a.minDistance = 1.5f;
                a.maxDistance = 14f;
                a.rolloffMode = AudioRolloffMode.Linear;
                a.time = Random.Range(0f, crackle.length);
                a.Play();
            }
        }

        void Update()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (head == null) return;
            float dt = Time.deltaTime;

            due += rate * dt;
            while (due >= 1f)
            {
                due -= 1f;
                if (OnSurface(out Vector3 at)) StartBubble(at);
            }
            for (int i = live.Count - 1; i >= 0; i--)
            {
                Bubble b = live[i];
                b.age += dt;
                float k = b.age / b.life;
                if (k >= 1f)
                {
                    Burst(b);
                    b.t.gameObject.SetActive(false);
                    spare.Push(b.t);
                    live.RemoveAt(i);
                    continue;
                }
                // Swells up out of the melt, wobbling, stretching taller just before it bursts.
                float grow = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, k * 1.6f)) * b.radius;
                float wobble = 1f + Mathf.Sin(b.age * 17f) * 0.05f;
                float stretch = 1f + Mathf.Max(0f, k - 0.75f) * 1.6f;
                b.t.localScale = new Vector3(grow * 2f * wobble, grow * 2f * stretch * 0.75f, grow * 2f / wobble);
                b.t.position = b.at + Vector3.up * (grow * 0.15f);
            }

            // Embers and wisps of smoke rising off the surface.
            if (Random.value < rate * 1.5f * dt && OnSurface(out Vector3 e))
                StoryFx.Emit(embers, e, new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(0.6f, 1.6f), Random.Range(-0.15f, 0.15f)),
                             Random.Range(0.015f, 0.04f), Random.Range(1.5f, 3f), hot);
            if (Random.value < rate * 0.6f * dt && OnSurface(out Vector3 s))
                StoryFx.Emit(smoke, s, new Vector3(0.1f, Random.Range(0.3f, 0.7f), 0.05f), Random.Range(0.25f, 0.6f), Random.Range(2.5f, 4.5f),
                             new Color(0.2f, 0.16f, 0.15f, 0.35f));
        }

        // A random spot on this lava's surface within reach of the player (a few tries).
        bool OnSurface(out Vector3 at)
        {
            Bounds b = surface.bounds;
            Vector3 centre = new Vector3(Mathf.Clamp(head.position.x, b.min.x, b.max.x), b.max.y + 2f, Mathf.Clamp(head.position.z, b.min.z, b.max.z));
            for (int i = 0; i < 6; i++)
            {
                Vector2 r = Random.insideUnitCircle * reach;
                var from = new Vector3(Mathf.Clamp(centre.x + r.x, b.min.x, b.max.x), b.max.y + 2f, Mathf.Clamp(centre.z + r.y, b.min.z, b.max.z));
                if (surface.Raycast(new Ray(from, Vector3.down), out RaycastHit hit, b.size.y + 4f))
                {
                    at = hit.point;
                    return true;
                }
            }
            at = default;
            return false;
        }

        void StartBubble(Vector3 at)
        {
            Transform t = spare.Count > 0 ? spare.Pop() : NewBubble();
            t.gameObject.SetActive(true);
            t.localScale = Vector3.zero;
            float far = Mathf.InverseLerp(4f, reach, Vector3.Distance(at, head.position)); // bigger far off, so they show
            live.Add(new Bubble { t = t, at = at, radius = Random.Range(size.x, size.y) * (1f + far), life = Random.Range(0.8f, 1.8f) });
        }

        Transform NewBubble()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "LavaBubble";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, true);
            var r = go.GetComponent<Renderer>();
            if (skin != null) r.sharedMaterial = GetComponent<Renderer>().sharedMaterial; // the same living lava (LavaFlow's copy)
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        // The bubble bursts: droplets of glowing rock fly out and fall back, a puff of smoke; sometimes a jet of fire.
        void Burst(Bubble b)
        {
            int drops = Mathf.RoundToInt(Mathf.Lerp(6f, 18f, Mathf.InverseLerp(size.x, size.y * 2f, b.radius)));
            for (int i = 0; i < drops; i++)
            {
                Vector2 d = Random.insideUnitCircle.normalized * Random.Range(0.4f, 1.4f);
                StoryFx.Emit(splash, b.at + Vector3.up * b.radius * 0.3f, new Vector3(d.x, Random.Range(1.2f, 2.6f), d.y) * (0.6f + b.radius * 2f),
                             Random.Range(0.02f, 0.05f) * (1f + b.radius * 2f), Random.Range(0.5f, 0.9f), Color.Lerp(hot, Color.yellow, Random.value * 0.4f));
            }
            StoryFx.Emit(smoke, b.at + Vector3.up * 0.1f, Vector3.up * 0.5f, b.radius * 2.2f, Random.Range(1.5f, 2.5f), new Color(0.18f, 0.14f, 0.13f, 0.45f));
            if (Random.value < spurts)
                for (int i = 0; i < 24; i++)
                    StoryFx.Emit(splash, b.at, new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(3f, 5.5f), Random.Range(-0.5f, 0.5f)),
                                 Random.Range(0.04f, 0.1f), Random.Range(0.7f, 1.2f), Color.Lerp(hot, new Color(1f, 0.85f, 0.4f, 1f), Random.value));
        }

        // A few seconds of bubbling melt: a low roiling hiss with pops and crackles scattered through it. Loops.
        static AudioClip Crackle()
        {
            const int rate = 22050;
            int n = rate * 4;
            var data = new float[n];
            var rng = new System.Random(7);
            float low = 0f;
            for (int i = 0; i < n; i++)
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                low += (white - low) * 0.02f; // a dark rumble of hiss
                data[i] = low * 0.5f + white * 0.02f;
            }
            for (int p = 0; p < 90; p++) // pops: short bright clicks, some deeper bloops
            {
                int at = rng.Next(n - 3000);
                bool deep = rng.NextDouble() < 0.3;
                int len = deep ? 2400 : 300 + rng.Next(500);
                float amp = deep ? 0.35f : 0.15f + (float)rng.NextDouble() * 0.3f;
                for (int i = 0; i < len; i++)
                {
                    float k = 1f - (float)i / len;
                    data[at + i] += (deep ? Mathf.Sin(i * (0.02f + 0.02f * k)) : (float)rng.NextDouble() * 2f - 1f) * amp * k * k;
                }
            }
            for (int i = 0; i < 2000; i++) // a seamless loop
            {
                float k = i / 2000f;
                data[i] = data[i] * k + data[n - 2000 + i] * (1f - k);
            }
            var clip = AudioClip.Create("LavaCrackle", n - 2000, 1, rate, false);
            var trimmed = new float[n - 2000];
            System.Array.Copy(data, trimmed, trimmed.Length);
            clip.SetData(trimmed, 0);
            return clip;
        }
    }
}
