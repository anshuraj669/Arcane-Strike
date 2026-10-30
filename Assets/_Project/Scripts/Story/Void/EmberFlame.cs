using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Ember: the living flame in the cauldron. A fiery figure made of particles (a flickering body rising into a
    /// head, two bright eyes that blink and look at the player) with a warm, flickering light. Moods change its
    /// colour and size: Calm (yellow), Excited (blinding white), Wild (a spinning tornado). It can lean towards
    /// something, reach a tendril of sparks at it, and flare while it talks.
    /// Sits on an empty at the cauldron's mouth; the figure rises from there.
    /// It adapts to its surroundings: glowing (additive) flames in the dark, but solid, deep-coloured flames with
    /// dark eyes on a bright background (the white void), where glow would add nothing and vanish.
    /// </summary>
    public class EmberFlame : MonoBehaviour
    {
        public enum Mood { Asleep, Calm, Excited, Wild }

        [SerializeField] Mood mood = Mood.Asleep;
        [Tooltip("Height of the figure above the cauldron's mouth (m).")]
        [SerializeField] float height = 1.1f;
        [SerializeField] Color calmColor = new Color(1f, 0.78f, 0.3f, 1f);
        [SerializeField] Color excitedColor = new Color(1f, 0.97f, 0.85f, 1f);
        [SerializeField] Color emberColor = new Color(1f, 0.35f, 0.08f, 1f);

        public Mood CurrentMood => mood;
        /// <summary>Height of the figure (m): about a metre over the cauldron, smaller rising from the lantern.</summary>
        public float Height { get => height; set => height = value; }
        /// <summary>0..1: brighter, livelier flames while a line is being spoken.</summary>
        public float Speaking { get; set; }
        /// <summary>True when drawn for a bright background (decided on the first frame).</summary>
        public bool BrightWorld { get; private set; }

        static readonly float[] Sides = { -1f, 1f }; // the two eyes (no new array every frame)
        ParticleSystem body, eyes, sparks;
        Light glow;
        Transform lookTarget, pointTarget;
        Vector3 lean;
        float size, heat, blinkUntil, nextBlink, pointUntil;
        Color colour;

        void Awake()
        {
            colour = calmColor;
        }

        // Built on the first frame, once the scene has set its sky and haze, so it knows how bright the world is.
        void Build()
        {
            Camera cam = Camera.main;
            Color back = cam != null && cam.clearFlags == CameraClearFlags.SolidColor ? cam.backgroundColor : RenderSettings.fogColor;
            BrightWorld = back.grayscale > 0.5f;
            if (BrightWorld)
            {
                calmColor = new Color(1f, 0.5f, 0.06f, 1f);     // deep orange
                excitedColor = new Color(1f, 0.78f, 0.12f, 1f); // blazing gold
                emberColor = new Color(0.7f, 0.1f, 0.02f, 1f);  // dark red at the roots
                colour = calmColor;
            }
            bool additive = !BrightWorld; // glow only shows against the dark
            body = StoryFx.CreateParticles(transform, "EmberBody", additive, -0.15f, 1500);
            eyes = StoryFx.CreateParticles(transform, "EmberEyes", additive, 0f, 40);
            sparks = StoryFx.CreateParticles(transform, "EmberSparks", additive, -0.05f, 600);
            var lightGo = new GameObject("EmberLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = Vector3.up * 0.6f;
            glow = lightGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.shadows = LightShadows.None;
            glow.range = 7f;
        }

        public void SetMood(Mood m)
        {
            if (m != mood && m == Mood.Excited) Burst(80);
            mood = m;
        }

        /// <summary>Face (and lean a little towards) this, e.g. the player's head.</summary>
        public void LookAt(Transform target) => lookTarget = target;

        /// <summary>Reach a tendril of sparks towards this point for a few seconds.</summary>
        public void PointAt(Transform target, float seconds = 3f)
        {
            pointTarget = target;
            pointUntil = Time.time + seconds;
        }

        public void Burst(int count)
        {
            if (sparks == null) return;
            Vector3 c = transform.position + Vector3.up * height * 0.5f;
            StoryFx.Burst(sparks, c, count, 1.6f, 0.05f, 1.2f, colour);
        }

        void Update()
        {
            if (body == null) Build();
            float dt = Time.deltaTime;
            float targetSize = mood switch { Mood.Asleep => 0.25f, Mood.Calm => 1f, Mood.Excited => 1.45f, _ => 1.8f };
            Color targetColour = mood == Mood.Excited || mood == Mood.Wild ? excitedColor : calmColor;
            size = Mathf.Lerp(size, targetSize * (1f + 0.15f * Speaking), dt * 3f);
            colour = Color.Lerp(colour, targetColour, dt * 2.5f);
            heat = Mathf.PerlinNoise(Time.time * 7f, 2.3f);

            // Lean towards whoever it is looking at.
            Vector3 wantLean = Vector3.zero;
            if (lookTarget != null)
            {
                Vector3 to = lookTarget.position - transform.position;
                to.y = 0f;
                wantLean = to.normalized * 0.12f * size;
            }
            lean = Vector3.Lerp(lean, wantLean, dt * 2f);

            glow.color = colour;
            glow.intensity = (mood == Mood.Asleep ? 0.6f : 2.2f) * size * (0.85f + 0.3f * heat);

            EmitBody(dt);
            EmitEyes();
            EmitTendril(dt);
        }

        Vector3 Point(float u) // a point up the figure's spine, u = 0 (mouth) .. 1 (top of the head)
        {
            float h = height * size;
            Vector3 p = transform.position + Vector3.up * (u * h) + lean * u * u;
            if (mood == Mood.Wild)
            {
                float a = Time.time * 9f + u * 6f;
                p += new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.1f + 0.35f * u) * size;
            }
            return p;
        }

        void EmitBody(float dt)
        {
            float rate = (mood == Mood.Asleep ? 40f : 160f) * size;
            int n = Mathf.FloorToInt(rate * dt + Random.value);
            for (int i = 0; i < n; i++)
            {
                // Most flames in the body, some forming the head (a rounder bulge near the top).
                bool headPart = mood != Mood.Asleep && Random.value < 0.35f;
                float u = headPart ? Random.Range(0.72f, 0.95f) : Random.Range(0f, 0.75f);
                float width = headPart ? 0.13f : Mathf.Lerp(0.28f, 0.12f, u);
                Vector3 p = Point(u) + Random.insideUnitSphere * width * size;
                Vector3 v = Vector3.up * Random.Range(0.25f, 0.6f) * size + Random.insideUnitSphere * 0.08f;
                Color c = Color.Lerp(colour, emberColor, Random.value * (1f - u) * 0.8f);
                StoryFx.Emit(body, p, v, Random.Range(0.08f, 0.2f) * size, Random.Range(0.35f, 0.7f), StoryFx.WithAlpha(c, BrightWorld ? 0.95f : 0.8f));
            }
            if (Random.value < 0.3f * size)
                StoryFx.Emit(sparks, Point(Random.value * 0.6f), Vector3.up * Random.Range(0.6f, 1.4f) + Random.insideUnitSphere * 0.3f,
                             0.025f, Random.Range(0.8f, 1.6f), colour);
        }

        void EmitEyes()
        {
            if (mood == Mood.Asleep) return;
            if (Time.time > nextBlink)
            {
                blinkUntil = Time.time + 0.12f;
                nextBlink = Time.time + Random.Range(2.5f, 5f);
            }
            if (Time.time < blinkUntil) return;
            Vector3 head = Point(0.83f);
            Vector3 face = lookTarget != null ? lookTarget.position - head : transform.forward;
            face.y = 0f;
            face = face.sqrMagnitude > 1e-4f ? face.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, face);
            foreach (float s in Sides)
                StoryFx.Emit(eyes, head + face * 0.13f * size + side * s * 0.055f * size + Vector3.up * 0.02f, Vector3.zero,
                             0.05f * size, 0.06f, BrightWorld ? new Color(0.12f, 0.03f, 0.01f, 1f) : Color.white);
        }

        void EmitTendril(float dt)
        {
            if (pointTarget == null || Time.time > pointUntil) return;
            Vector3 from = Point(0.6f), to = pointTarget.position;
            int n = Mathf.FloorToInt(90f * dt + Random.value);
            for (int i = 0; i < n; i++)
            {
                float u = Random.value;
                Vector3 p = Vector3.Lerp(from, to, u * 0.8f) + Random.insideUnitSphere * 0.04f + Vector3.up * Mathf.Sin(u * Mathf.PI) * 0.15f;
                StoryFx.Emit(sparks, p, (to - from).normalized * 0.4f, Random.Range(0.03f, 0.07f), 0.35f, colour);
            }
        }
    }
}
