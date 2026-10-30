using System.Collections.Generic;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Life in the monster world's sky: flocks of black carrion birds wheeling over the ledge on the hot air (flapping,
    /// then gliding, calling now and then), and winged Kalakh beasts circling the volcano far off, one of them now and
    /// then sweeping low over the fight with a screech. Built from templates (a body and two wings, each wing pivoting at
    /// its shoulder: rotating it about its own forward axis flaps it).
    /// </summary>
    public class Flyers : MonoBehaviour
    {
        [Header("Carrion birds")]
        public Transform birdBody;
        public Transform birdWingL, birdWingR;
        [SerializeField] int birds = 22;
        [Tooltip("How big the birds are (the model is a crow's size: these are the world's great carrion birds).")]
        [SerializeField] Vector2 birdScale = new Vector2(2.2f, 3.2f);
        [Header("Winged beasts")]
        public Transform wyvernBody;
        public Transform wyvernWingL, wyvernWingR;
        [SerializeField] int wyverns = 4;
        [SerializeField] Vector2 wyvernScale = new Vector2(1.4f, 2f);
        public Vector3 volcano = new Vector3(45f, 62f, 170f);
        [Tooltip("Seconds between beasts sweeping over the fight.")]
        [SerializeField] Vector2 swoopEvery = new Vector2(14f, 26f);

        class Flyer
        {
            public Transform root, wingL, wingR;
            public Quaternion restL, restR;
            public Vector3 centre;
            public float radius, height, angle, speed, flapRate, flapAmp, phase, glideUntil, nextCall, scale;
            public bool beast;
            // A swoop: a curve from where it is, low over the fight, and away.
            public bool swooping;
            public Vector3 a, b, c;
            public float swoopT, swoopTime;
            public Vector3 last;
            public AudioSource voice;
        }

        readonly List<Flyer> flock = new List<Flyer>();
        float nextSwoop;
        Transform view;
        static AudioClip caw, screech;

        void Start()
        {
            if (caw == null) caw = Call("Caw", 0.35f, 900f, 1400f, 0.6f);
            if (screech == null) screech = Call("Screech", 1.4f, 500f, 2600f, 0.9f);
            if (birdBody != null)
                for (int i = 0; i < birds; i++)
                {
                    // Three flocks: low over the ledge (in sight above the fight), out over the lava sea at the height of
                    // the horizon (black against its glow), and high round the volcano.
                    int group = i % 3;
                    var f = Make(birdBody, birdWingL, birdWingR, Random.Range(birdScale.x, birdScale.y));
                    Vector3 sea = new Vector3(volcano.x, 0f, volcano.z) * 0.3f;
                    f.centre = group == 0 ? new Vector3(Random.Range(-4f, 4f), 0f, Random.Range(14f, 18f))
                             : group == 1 ? sea + new Vector3(Random.Range(-20f, 20f), 0f, Random.Range(-10f, 10f))
                             : new Vector3(volcano.x, 0f, volcano.z) + new Vector3(Random.Range(-15f, 15f), 0f, Random.Range(-15f, 15f));
                    f.radius = group == 0 ? Random.Range(8f, 14f) : group == 1 ? Random.Range(15f, 30f) : Random.Range(25f, 45f);
                    f.height = group == 0 ? Random.Range(8f, 14f) : group == 1 ? Random.Range(5f, 14f) : volcano.y + Random.Range(-15f, 10f);
                    f.speed = Random.Range(0.25f, 0.4f) * (Random.value < 0.2f ? -1f : 1f) * (group == 0 ? 1f : 0.7f);
                    f.flapRate = Random.Range(7f, 10f);
                    f.flapAmp = Random.Range(28f, 36f);
                    f.nextCall = Time.time + Random.Range(3f, 20f);
                    flock.Add(f);
                }
            if (wyvernBody != null)
                for (int i = 0; i < wyverns; i++)
                {
                    var f = Make(wyvernBody, wyvernWingL, wyvernWingR, Random.Range(wyvernScale.x, wyvernScale.y));
                    f.beast = true;
                    // Circling between the ledge and the volcano: near enough to make out, far enough not to crowd.
                    f.centre = new Vector3(volcano.x, 0f, volcano.z) * Random.Range(0.35f, 0.6f) + new Vector3(Random.Range(-15f, 15f), 0f, Random.Range(-15f, 15f));
                    f.radius = Random.Range(30f, 55f);
                    f.height = Random.Range(20f, 40f);
                    f.speed = Random.Range(0.08f, 0.14f) * (i % 2 == 0 ? 1f : -1f);
                    f.flapRate = Random.Range(2.2f, 3f);
                    f.flapAmp = Random.Range(30f, 38f);
                    f.nextCall = Time.time + Random.Range(8f, 25f);
                    f.voice = StoryFx.CreateSource(f.root.gameObject, screech, false, 1f);
                    f.voice.maxDistance = 400f;
                    f.voice.rolloffMode = AudioRolloffMode.Linear;
                    flock.Add(f);
                }
            nextSwoop = Time.time + Random.Range(8f, 12f);
            Debug.Log($"[Flyers] {flock.FindAll(x => !x.beast).Count} carrion birds, {flock.FindAll(x => x.beast).Count} winged beasts in the sky.");
            foreach (Transform t in new[] { birdBody, birdWingL, birdWingR, wyvernBody, wyvernWingL, wyvernWingR })
                if (t != null) t.gameObject.SetActive(false); // (the templates)
        }

        // One flyer: copies of the body and wings under a new root, wings at their shoulders.
        Flyer Make(Transform body, Transform wingL, Transform wingR, float scale)
        {
            var root = new GameObject(body.name.Replace("_Body", "")).transform;
            root.SetParent(transform, false);
            Transform Copy(Transform part)
            {
                if (part == null) return null;
                var t = Instantiate(part.gameObject, root).transform;
                t.gameObject.SetActive(true);
                t.localPosition = Quaternion.Inverse(body.rotation) * (part.position - body.position);
                t.localRotation = Quaternion.Inverse(body.rotation) * part.rotation;
                t.localScale = part.lossyScale;
                foreach (var r in t.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return t;
            }
            Copy(body);
            var f = new Flyer { root = root, wingL = Copy(wingL), wingR = Copy(wingR), scale = scale };
            if (f.wingL != null) f.restL = f.wingL.localRotation;
            if (f.wingR != null) f.restR = f.wingR.localRotation;
            root.localScale = Vector3.one * scale;
            f.angle = Random.value * Mathf.PI * 2f;
            f.phase = Random.value * 10f;
            return f;
        }

        void Update()
        {
            if (view == null && Camera.main != null) view = Camera.main.transform;
            float dt = Time.deltaTime, time = Time.time;

            // Now and then something sweeps low over the fight: a beast off its circle (screeching), or a bird.
            if (time > nextSwoop && view != null)
            {
                nextSwoop = time + Random.Range(swoopEvery.x, swoopEvery.y);
                bool big = Random.value < 0.6f;
                List<Flyer> free = flock.FindAll(x => x.beast == big && !x.swooping);
                Flyer beast = free.Count > 0 ? free[Random.Range(0, free.Count)] : null;
                if (beast != null)
                {
                    Vector3 over = new Vector3(view.position.x, view.position.y + (big ? Random.Range(9f, 14f) : Random.Range(3.5f, 6f)), view.position.z) + Random.insideUnitSphere * (big ? 6f : 3f);
                    Vector3 dir = (over - beast.root.position).normalized;
                    beast.swooping = true;
                    beast.a = beast.root.position;
                    beast.b = over;
                    beast.c = over + new Vector3(dir.x, 0.3f, dir.z) * 90f;
                    beast.swoopT = 0f;
                    beast.swoopTime = Vector3.Distance(beast.a, beast.b) / 16f + 5f;
                    if (beast.voice != null) beast.voice.Play();
                }
            }

            foreach (Flyer f in flock)
            {
                Vector3 pos;
                if (f.swooping)
                {
                    f.swoopT += dt / f.swoopTime;
                    float k = f.swoopT;
                    pos = (1 - k) * (1 - k) * f.a + 2 * (1 - k) * k * f.b + k * k * f.c; // a curve through the sweep
                    if (k >= 1f)
                    {
                        f.swooping = false;
                        Vector3 back = pos - f.centre;
                        f.angle = Mathf.Atan2(back.z, back.x);
                    }
                    if (k > 0.6f) // climbing back to its circle
                    {
                        Vector3 circle = f.centre + new Vector3(Mathf.Cos(f.angle), 0f, Mathf.Sin(f.angle)) * f.radius + Vector3.up * f.height;
                        pos = Vector3.Lerp(pos, circle, (k - 0.6f) / 0.4f);
                        if (k >= 1f) pos = circle;
                    }
                }
                else
                {
                    f.angle += f.speed * dt * (12f / Mathf.Max(f.radius, 1f)) * 2f;
                    pos = f.centre + new Vector3(Mathf.Cos(f.angle), 0f, Mathf.Sin(f.angle)) * f.radius +
                          Vector3.up * (f.height + Mathf.Sin(time * 0.3f + f.phase) * 3f) +
                          new Vector3(Mathf.PerlinNoise(time * 0.1f, f.phase) - 0.5f, 0f, Mathf.PerlinNoise(f.phase, time * 0.1f) - 0.5f) * 6f;
                }
                Vector3 vel = (pos - f.last) / Mathf.Max(dt, 1e-4f);
                f.last = pos;
                f.root.position = pos;
                if (vel.sqrMagnitude > 0.01f)
                {
                    Vector3 fwd = vel.normalized;
                    float turn = Vector3.SignedAngle(StoryFx.Flat(f.root.forward), StoryFx.Flat(fwd), Vector3.up);
                    Quaternion want = Quaternion.LookRotation(fwd) * Quaternion.Euler(0f, 0f, Mathf.Clamp(-turn * 8f, -40f, 40f)); // banking into turns
                    f.root.rotation = Quaternion.Slerp(f.root.rotation, want, dt * 3f);
                }

                // Flap, then glide on the hot air, wings held up a little.
                if (time > f.glideUntil && Random.value < dt * (f.beast ? 0.15f : 0.25f)) f.glideUntil = time + Random.Range(1.5f, 4f);
                bool gliding = time < f.glideUntil && !(f.swooping && f.swoopT > 0.5f);
                float flap = gliding ? 8f + Mathf.Sin(time * 2f + f.phase) * 3f : Mathf.Sin((time + f.phase) * f.flapRate) * f.flapAmp + 8f;
                if (f.wingR != null) f.wingR.localRotation = f.restR * Quaternion.Euler(0f, 0f, flap);
                if (f.wingL != null) f.wingL.localRotation = f.restL * Quaternion.Euler(0f, 0f, -flap);

                if (time > f.nextCall && view != null)
                {
                    f.nextCall = time + (f.beast ? Random.Range(15f, 35f) : Random.Range(6f, 22f));
                    if (f.beast && f.voice != null) f.voice.Play();
                    else if (!f.beast && Vector3.Distance(pos, view.position) < 45f)
                    {
                        AudioSource s = StoryFx.CreateSource(f.root.gameObject, caw, false, 1f);
                        s.maxDistance = 60f;
                        s.pitch = Random.Range(0.85f, 1.2f);
                        s.Play();
                        Destroy(s, 1f);
                    }
                }
            }
        }

        // A harsh bird call or a beast's screech: noise through a sweeping resonance, in a few stutters.
        static AudioClip Call(string name, float seconds, float lowHz, float highHz, float rough)
        {
            const int rate = 22050;
            int n = Mathf.CeilToInt(seconds * rate);
            var data = new float[n];
            var rng = new System.Random(name.GetHashCode());
            float phase = 0f, low = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float f = Mathf.Lerp(highHz, lowHz, t) * (1f + 0.1f * Mathf.Sin(t * 60f));
                phase += f / rate * Mathf.PI * 2f;
                float noise = (float)rng.NextDouble() * 2f - 1f;
                low += (noise - low) * 0.3f;
                float stutter = name == "Caw" ? Mathf.Clamp01(Mathf.Sin(t * Mathf.PI * 2f) * 2f) : 1f;
                float env = Mathf.Sin(t * Mathf.PI) * stutter;
                data[i] = (Mathf.Sign(Mathf.Sin(phase)) * 0.3f * (1f - rough) + Mathf.Sin(phase) * 0.4f + low * rough) * env * 0.5f;
            }
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
