using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The lantern's pulsing light: a point light plus a soft additive halo (no Bloom needed on Quest).
    /// Starts golden while it is cleaned; the awakening flashes it and turns it blue.
    /// </summary>
    public class LanternGlow : MonoBehaviour
    {
        [Header("Glow Settings")]
        [Tooltip("Optional. Created at runtime when empty.")]
        public Light lanternLight;
        [SerializeField] Color glowColor = new Color(1f, 0.79f, 0.29f); // Spark gold #FFC94A
        public float minIntensity = 0.5f;
        public float maxIntensity = 3f;
        public float pulseSpeed = 2f;
        [Tooltip("Candle-like flicker on top of the pulse (0 = steady).")]
        [Range(0f, 1f)] public float flicker = 0.25f;
        [SerializeField] float range = 2.5f;
        [SerializeField] float haloSize = 0.55f;
        [Tooltip("Lantern centre, relative to its origin.")]
        [SerializeField] Vector3 centerOffset = new Vector3(0f, 0.12f, 0f);
        [Tooltip("Optional candle flame mesh: it grows with the glow, flickers and takes the glow's colour.")]
        public Renderer flame;

        public bool activateGlow;

        float level, targetLevel, flash;
        Color color, targetColor, flashColor;
        ParticleSystem halo;
        Material flameMaterial;
        Vector3 flameScale;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        void Start()
        {
            color = targetColor = glowColor;
            if (lanternLight == null)
            {
                var go = new GameObject("GlowLight");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = centerOffset;
                lanternLight = go.AddComponent<Light>();
                lanternLight.type = LightType.Point;
                lanternLight.shadows = LightShadows.None;
            }
            lanternLight.range = range;
            lanternLight.intensity = 0f;
            halo = StoryFx.CreateParticles(transform, "Halo", true, 0f, 64);
            if (flame != null)
            {
                flameMaterial = flame.material; // own copy for the colour changes
                flameScale = flame.transform.localScale;
                flame.enabled = false; // unlit until the glow starts
            }
        }

        public void StartGlow()
        {
            activateGlow = true;
            targetLevel = Mathf.Max(targetLevel, 0.3f);
        }

        /// <summary>0 = off, 1 = full glow.</summary>
        public void SetLevel(float t) => targetLevel = Mathf.Clamp01(t);

        public void SetColor(Color c) => targetColor = c;

        /// <summary>A bright burst that fades over about half a second.</summary>
        public void Flash(Color c, float strength)
        {
            activateGlow = true;
            flashColor = c;
            flash = strength;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            level = Mathf.MoveTowards(level, activateGlow ? targetLevel : 0f, dt * 0.8f);
            color = Color.Lerp(color, targetColor, 1f - Mathf.Exp(-3f * dt)); // the same at 72 or 120 fps
            flash = Mathf.MoveTowards(flash, 0f, dt * 2f * Mathf.Max(1f, flash));

            float pulse = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
            float flickerNow = 1f + flicker * (Mathf.PerlinNoise(Time.time * 8f, 0.37f) * 2f - 1f);
            Color c = Color.Lerp(color, flashColor, Mathf.Clamp01(flash));
            if (lanternLight != null)
            {
                lanternLight.intensity = Mathf.Lerp(minIntensity, maxIntensity, pulse) * level * flickerNow + flash * maxIntensity;
                lanternLight.color = c;
            }

            if (flame != null)
            {
                float size = level > 0.01f ? (0.4f + 0.6f * level) * flickerNow : 0f;
                flame.enabled = size > 0f;
                flame.transform.localScale = new Vector3(flameScale.x * size, flameScale.y * size * (0.9f + 0.2f * pulse), flameScale.z * size);
                flameMaterial.SetColor(BaseColorId, Color.Lerp(c, Color.white, 0.4f));
                flameMaterial.SetColor(EmissionColorId, c * (1.5f + level + flash));
            }

            float strength = Mathf.Max(level, Mathf.Clamp01(flash));
            if (strength > 0.01f)
                StoryFx.Emit(halo, transform.TransformPoint(centerOffset), Vector3.zero,
                             haloSize * (0.7f + 0.3f * pulse) * flickerNow * (0.5f + strength) * (1f + flash * 0.5f), 0.06f,
                             StoryFx.WithAlpha(c, 0.25f + 0.5f * strength));
        }
    }
}
