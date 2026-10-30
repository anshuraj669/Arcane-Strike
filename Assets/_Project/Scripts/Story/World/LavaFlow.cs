using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Molten rock. With the ArcaneStrike/Lava shader (the monster world's lava), glowing veins creep along under a
    /// drifting dark crust that cools and cracks open again, the hottest seams burn yellow-white and the surface swells;
    /// this makes the shader's noise texture (no image needed) and sets how this piece of lava moves. With any other
    /// shader (URP Lit), it scrolls and pulses a vein texture instead. Optionally lights its surroundings with a few
    /// flickering lights (<see cref="lights"/>). Anything standing in it burns (<see cref="LavaHazard"/>).
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class LavaFlow : MonoBehaviour
    {
        [SerializeField] Color hot = new Color(1f, 0.42f, 0.08f, 1f);
        [SerializeField] Color crust = new Color(0.07f, 0.02f, 0.015f, 1f);
        [SerializeField] float glow = 2.6f;
        [Tooltip("Which way and how fast the melt creeps (world x/z, metres per second).")]
        [SerializeField] Vector2 flow = new Vector2(0.04f, 0.09f);
        [Tooltip("Noise repeats per metre.")]
        [SerializeField] float tiling = 0.18f;
        [Tooltip("How much of the surface is dark crust (0 = all molten).")]
        [Range(0f, 1f)] [SerializeField] float crustAmount = 0.5f;
        [Tooltip("How far the surface rises and falls (m).")]
        [SerializeField] float swell = 0.03f;
        [Tooltip("Flickering warm lights to drive with it (optional).")]
        public Light[] lights;
        [Tooltip("Only mark this surface as burning (LavaHazard): leave its material and look alone.")]
        public bool surfaceOnly;

        public const string ShaderName = "ArcaneStrike/Lava";

        Material material;
        bool lavaShader;
        float[] lightBase;
        static Texture2D noise, veins;
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        /// <summary>The shader's tiling noise (R veins, G and B soft).</summary>
        public static Texture2D SharedNoise => noise != null ? noise : noise = MakeNoise(128);

        /// <summary>Sets how this lava moves (before it starts, e.g. from a scene builder).</summary>
        public void Configure(Vector2 flowDirection, float crustCover, float glowStrength, float swellHeight)
        {
            flow = flowDirection;
            crustAmount = crustCover;
            glow = glowStrength;
            swell = swellHeight;
        }

        void Start()
        {
            if (surfaceOnly)
            {
                enabled = false;
                return;
            }
            var r = GetComponent<Renderer>();
            material = r.material; // own copy
            lavaShader = material.shader != null && material.shader.name == ShaderName;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (lavaShader)
            {
                material.SetTexture("_NoiseTex", SharedNoise);
                material.SetColor("_Hot", hot);
                material.SetColor("_Crust", crust);
                material.SetFloat("_Glow", glow);
                material.SetFloat("_Tiling", tiling);
                material.SetVector("_Flow", new Vector4(flow.x, flow.y, 0f, 0f));
                material.SetFloat("_CrustAmount", crustAmount);
                material.SetFloat("_Swell", swell);
            }
            else
            {
                if (veins == null) veins = MakeVeins(128);
                material.SetTexture("_BaseMap", veins);
                material.SetTexture("_EmissionMap", veins);
                material.SetTextureScale("_BaseMap", Vector2.one * tiling);
                material.SetTextureScale("_EmissionMap", Vector2.one * tiling);
                material.SetColor("_BaseColor", Color.white);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            if (lights != null)
            {
                lightBase = new float[lights.Length];
                for (int i = 0; i < lights.Length; i++) lightBase[i] = lights[i] != null ? lights[i].intensity : 0f;
            }
        }

        void Update()
        {
            if (material == null) return;
            if (!lavaShader) // (the shader animates itself)
            {
                Vector2 offset = flow * tiling * Time.time;
                material.SetTextureOffset("_BaseMap", offset);
                material.SetTextureOffset("_EmissionMap", offset);
                float pulse = 0.85f + 0.1f * Mathf.Sin(Time.time * 0.9f) + 0.05f * Mathf.Sin(Time.time * 2.3f);
                material.SetColor(EmissionColor, hot * glow * pulse);
            }
            if (lights == null) return;
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null)
                    lights[i].intensity = lightBase[i] * (0.75f + 0.35f * Mathf.PerlinNoise(Time.time * 1.5f, i * 3.7f));
        }

        // R: ridged veins (thin bright seams); G: soft noise (the crust plates); B: soft noise (the warp). Tiles.
        static Texture2D MakeNoise(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = "LavaNoise", wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    float ridge = Ridged(u, v, 0f);
                    float soft = Fbm(u, v, 31.7f), warp = Fbm(u, v, 57.1f);
                    pixels[y * size + x] = new Color(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.97f, ridge)), soft, warp, 1f);
                }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        // Bright veins on a dark, mottled crust (the URP Lit fallback).
        Texture2D MakeVeins(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "LavaVeins", wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.95f, Ridged((float)x / size, (float)y / size, 0f)));
                    pixels[y * size + x] = Color.Lerp(crust, Color.white, k);
                }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        static float Ridged(float u, float v, float seed)
        {
            float ridge = 0f, amp = 1f, total = 0f;
            for (int o = 0; o < 4; o++)
            {
                float f = 4 << o;
                ridge += (1f - Mathf.Abs(TileNoise(u * f, v * f, f, seed) * 2f - 1f)) * amp;
                total += amp;
                amp *= 0.5f;
            }
            return ridge / total;
        }

        static float Fbm(float u, float v, float seed)
        {
            float n = 0f, amp = 1f, total = 0f;
            for (int o = 0; o < 3; o++)
            {
                float f = 3 << o;
                n += TileNoise(u * f, v * f, f, seed) * amp;
                total += amp;
                amp *= 0.5f;
            }
            return Mathf.Clamp01((n / total - 0.5f) * 1.6f + 0.5f);
        }

        // Perlin noise that wraps every `period` units (so the texture tiles).
        static float TileNoise(float x, float y, float period, float seed)
        {
            float a = Mathf.PerlinNoise(x + seed, y + seed), b = Mathf.PerlinNoise(x - period + seed, y + seed);
            float c = Mathf.PerlinNoise(x + seed, y - period + seed), d = Mathf.PerlinNoise(x - period + seed, y - period + seed);
            float fx = x / period, fy = y / period;
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }
    }
}
