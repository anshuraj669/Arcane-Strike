using ArcaneStrike.VFX;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The monster world's sky (after monsterWorld.jpg): towering storm clouds all round the horizon, dark above and lit
    /// orange from below by the fires, drifting slowly; now and then a pulse of firelight flickers inside one. The
    /// clouds are big soft billboards far away (made here, no textures needed), facing the player.
    /// </summary>
    public class StormSky : MonoBehaviour
    {
        [SerializeField] int clouds = 22;
        [SerializeField] float distance = 380f;
        [SerializeField] Color dark = new Color(0.12f, 0.1f, 0.13f, 0.95f);
        [SerializeField] Color lit = new Color(0.95f, 0.42f, 0.18f, 0.95f);
        [Tooltip("The direction of the volcano (clouds there glow brighter).")]
        [SerializeField] Vector3 volcano = new Vector3(45f, 0f, 170f);

        Transform[] billboards;
        Material[] materials;
        Color[] baseColors;
        float[] flash, drift;
        Transform view;

        void Start()
        {
            Texture2D[] shapes = { Cloud(128, 1), Cloud(128, 2), Cloud(128, 3) };
            billboards = new Transform[clouds];
            materials = new Material[clouds];
            baseColors = new Color[clouds];
            flash = new float[clouds];
            drift = new float[clouds];
            Vector3 toVolcano = new Vector3(volcano.x, 0f, volcano.z).normalized;
            for (int i = 0; i < clouds; i++)
            {
                float a = (i + Random.value * 0.8f) / clouds * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float near = Mathf.Max(0f, Vector3.Dot(dir, toVolcano));
                float size = Random.Range(160f, 300f);
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(quad.GetComponent<Collider>());
                quad.name = "StormCloud";
                quad.transform.SetParent(transform, false);
                quad.transform.position = dir * distance * Random.Range(0.9f, 1.1f) + Vector3.up * Random.Range(40f, 120f);
                quad.transform.localScale = new Vector3(size * Random.Range(1.2f, 1.8f), size, 1f);
                Material m = AegisShield.CreateFaceMaterial(Color.white);
                m.name = "StormCloud";
                m.SetTexture("_BaseMap", shapes[i % shapes.Length]);
                m.renderQueue = 1990; // behind everything in the world
                baseColors[i] = Color.Lerp(dark, lit, Random.Range(0.15f, 0.45f) + near * 0.45f);
                m.SetColor("_BaseColor", baseColors[i]);
                var r = quad.GetComponent<Renderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                billboards[i] = quad.transform;
                materials[i] = m;
                drift[i] = Random.Range(-0.4f, 0.4f);
            }
        }

        void Update()
        {
            if (view == null && Camera.main != null) view = Camera.main.transform;
            for (int i = 0; i < billboards.Length; i++)
            {
                Transform b = billboards[i];
                if (b == null) continue;
                b.RotateAround(Vector3.zero, Vector3.up, drift[i] * Time.deltaTime);
                if (view != null) b.rotation = Quaternion.LookRotation(b.position - view.position);
                // Firelight flickering inside a cloud now and then.
                if (flash[i] <= 0f && Random.value < 0.0015f) flash[i] = 1f;
                flash[i] = Mathf.Max(0f, flash[i] - Time.deltaTime * 1.5f);
                float f = flash[i] * (0.6f + 0.4f * Mathf.Sin(Time.time * 30f));
                materials[i].SetColor("_BaseColor", Color.Lerp(baseColors[i], new Color(1f, 0.6f, 0.3f, baseColors[i].a), f * 0.6f));
            }
        }

        // A billowing cloud: soft fbm noise inside a rounded, flat-bottomed shape, lighter at the bottom (lit from below).
        static Texture2D Cloud(int size, int seed)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "StormCloud", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            float ox = seed * 17.3f, oy = seed * 5.1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size;
                    float n = 0f, amp = 0.5f, f = 3f;
                    for (int o = 0; o < 5; o++)
                    {
                        n += Mathf.PerlinNoise(ox + u * f, oy + v * f) * amp;
                        amp *= 0.5f;
                        f *= 2f;
                    }
                    float shape = 1f - Mathf.Clamp01(Mathf.Abs(u) * 1.15f) - Mathf.Clamp01((v - 0.35f) * 1.6f) + n * 0.9f - 0.35f;
                    shape *= Mathf.SmoothStep(0f, 1f, v / 0.12f); // a soft flat base
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(shape * 2.2f));
                    float light = Mathf.Lerp(1f, 0.45f, v) * (0.75f + 0.5f * n);
                    pixels[y * size + x] = new Color(light, light, light, a);
                }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
