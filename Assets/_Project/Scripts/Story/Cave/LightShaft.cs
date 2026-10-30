using ArcaneStrike.VFX;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The shaft of blue light falling through the hole in the cave's roof onto the altar (as in ascension.jpg): two
    /// soft nested cones of light, dust drifting slowly in them, and a spotlight. <see cref="Flare"/> brightens it
    /// for a moment (the shockwave, the Ascension); <see cref="Intensity"/> sets how bright it stays.
    /// </summary>
    public class LightShaft : MonoBehaviour
    {
        [SerializeField] Color color = new Color(0.45f, 0.65f, 1f, 1f);
        [Tooltip("The floor where the light lands (this object sits at the roof's hole).")]
        [SerializeField] float length = 6.5f;
        [SerializeField] float topRadius = 1.0f;
        [SerializeField] float bottomRadius = 1.6f;
        [SerializeField] float spotIntensity = 9f;

        /// <summary>How bright it is (1 = as built).</summary>
        public float Intensity { get; set; } = 1f;

        Material inner, outer;
        Light spot;
        ParticleSystem dust;
        float flare;

        /// <summary>A burst of brightness that fades over `seconds`.</summary>
        public void Flare(float amount, float seconds)
        {
            flare = Mathf.Max(flare, amount);
            flareFade = amount / Mathf.Max(seconds, 0.05f);
        }

        float flareFade = 1f;
        const float dustRate = 40f; // motes a second
        float dustDue;

        void Start()
        {
            Texture2D fade = Gradient(64);
            outer = Cone("ShaftOuter", topRadius * 1.25f, bottomRadius * 1.3f, fade);
            inner = Cone("ShaftInner", topRadius * 0.75f, bottomRadius * 0.8f, fade);
            spot = new GameObject("ShaftLight").AddComponent<Light>();
            spot.transform.SetParent(transform, false);
            spot.transform.localPosition = Vector3.up * 2f;
            spot.transform.localRotation = Quaternion.LookRotation(Vector3.down);
            spot.type = LightType.Spot;
            spot.color = color;
            spot.range = length + 4f;
            spot.spotAngle = Mathf.Atan2(bottomRadius * 1.4f, length + 2f) * Mathf.Rad2Deg * 2f;
            spot.shadows = LightShadows.Hard; // the cave's one shadowed light: hard shadows are the cheap kind on a Quest
            dust = StoryFx.CreateParticles(transform, "ShaftDust", true, 0f, 400);
        }

        void Update()
        {
            flare = Mathf.MoveTowards(flare, 0f, flareFade * Time.deltaTime);
            float k = Intensity + flare;
            float shimmer = 1f + Mathf.Sin(Time.time * 0.7f) * 0.06f + Mathf.Sin(Time.time * 1.9f) * 0.03f;
            if (inner != null) inner.SetColor("_BaseColor", StoryFx.WithAlpha(color, 0.16f * k * shimmer));
            if (outer != null) outer.SetColor("_BaseColor", StoryFx.WithAlpha(color, 0.07f * k * shimmer));
            if (spot != null) spot.intensity = spotIntensity * k;
            // Dust motes drifting slowly down through the light.
            if (dust != null) dustDue += dustRate * Time.deltaTime; // (as many motes a second at 72 fps as at 90)
            for (; dustDue >= 1f; dustDue -= 1f)
            {
                float y = Random.Range(0.2f, length);
                float r = Mathf.Lerp(bottomRadius, topRadius, y / length) * Mathf.Sqrt(Random.value) * 0.9f;
                float a = Random.value * Mathf.PI * 2f;
                Vector3 p = transform.position + new Vector3(Mathf.Cos(a) * r, -length + y, Mathf.Sin(a) * r);
                StoryFx.Emit(dust, p, new Vector3(Random.Range(-0.03f, 0.03f), -0.05f, Random.Range(-0.03f, 0.03f)),
                             Random.Range(0.008f, 0.02f), Random.Range(3f, 6f), StoryFx.WithAlpha(Color.Lerp(color, Color.white, 0.5f), 0.7f * Mathf.Min(1f, k)));
            }
        }

        // An open cone from the roof down to the floor; its texture fades out at the top and the bottom.
        Material Cone(string name, float r0, float r1, Texture2D fade)
        {
            const int sides = 32;
            var vertices = new Vector3[(sides + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var tris = new int[sides * 6];
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                vertices[i * 2] = dir * r0;
                vertices[i * 2 + 1] = dir * r1 + Vector3.down * length;
                uv[i * 2] = new Vector2(i / (float)sides, 1f);
                uv[i * 2 + 1] = new Vector2(i / (float)sides, 0f);
            }
            for (int i = 0; i < sides; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                tris[i * 6] = a; tris[i * 6 + 1] = c; tris[i * 6 + 2] = b;
                tris[i * 6 + 3] = b; tris[i * 6 + 4] = c; tris[i * 6 + 5] = d;
            }
            var mesh = new Mesh { name = name, vertices = vertices, uv = uv, triangles = tris };
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            Material m = SparkHands.CreateGlowMaterial(fade);
            m.name = name;
            m.SetFloat("_Cull", 0f); // both sides: seen from outside and from standing in it
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return m;
        }

        // Bright in the middle of its height, fading to nothing at the roof and just above the floor.
        static Texture2D Gradient(int size)
        {
            var tex = new Texture2D(4, size, TextureFormat.RGBA32, false) { name = "ShaftFade", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
            {
                float v = y / (size - 1f);
                float a = Mathf.SmoothStep(0f, 1f, v / 0.15f) * Mathf.SmoothStep(0f, 1f, (1f - v) / 0.25f);
                for (int x = 0; x < 4; x++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            return tex;
        }
    }
}
