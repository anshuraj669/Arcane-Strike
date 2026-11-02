using System.Collections;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>The living fire's parts: surfaces that wear the ArcaneStrike/Fire shader.</summary>
    public static class FireFx
    {
        static Texture2D noise;

        public static Material Material(float shape, float seed, float tileX, float flow = 1f)
        {
            var m = new Material(Shader.Find("ArcaneStrike/Fire")) { name = "LivingFire" };
            if (noise == null) noise = Resources.Load<Texture2D>("FX/T_FireNoise");
            m.SetTexture("_NoiseTex", noise);
            m.SetFloat("_Shape", shape);
            m.SetFloat("_Seed", seed);
            m.SetFloat("_TileX", tileX);
            m.SetFloat("_Flow", flow);
            return m;
        }

        /// <summary>An upright sheet, 1 m wide and tall, its foot at the pivot, for a column of fire.</summary>
        public static Transform Sheet(Transform parent, float seed)
        {
            var mesh = new Mesh { name = "FireSheet" };
            mesh.vertices = new[] { new Vector3(-0.5f, 0f), new Vector3(0.5f, 0f), new Vector3(-0.5f, 1f), new Vector3(0.5f, 1f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.bounds = new Bounds(Vector3.up * 0.5f, Vector3.one * 1.5f);
            return Surface(parent, "FireSheet", mesh, Material(0f, seed, 1f)).transform;
        }

        /// <summary>An open cylinder round the object's pivot (uv.x round it, uv.y up it), `height` tall from `bottom`; it narrows to `topRadius` if one is given.</summary>
        public static MeshRenderer Wall(Transform parent, string name, float radius, float bottom, float height, float tileX, float seed, float topRadius = -1f)
        {
            const int sides = 64;
            var verts = new Vector3[(sides + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var tris = new int[sides * 6];
            for (int i = 0; i <= sides; i++)
            {
                float k = (float)i / sides, a = k * Mathf.PI * 2f;
                var round = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                verts[i * 2] = round + Vector3.up * bottom;
                verts[i * 2 + 1] = round * (topRadius < 0f ? 1f : topRadius / radius) + Vector3.up * (bottom + height);
                uvs[i * 2] = new Vector2(k, 0f);
                uvs[i * 2 + 1] = new Vector2(k, 1f);
                if (i == sides) break;
                int t = i * 6, q = i * 2;
                tris[t] = q;
                tris[t + 1] = q + 1;
                tris[t + 2] = q + 2;
                tris[t + 3] = q + 2;
                tris[t + 4] = q + 1;
                tris[t + 5] = q + 3;
            }
            var mesh = new Mesh { name = name, vertices = verts, uv = uvs, triangles = tris };
            mesh.RecalculateBounds();
            return Surface(parent, name, mesh, Material(1f, seed, tileX));
        }

        static MeshRenderer Surface(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }
    }

    /// <summary>
    /// The blue fire the Ascension happens in: two crowns of thin curling flames on the floor round the body and the soul
    /// (cylinders wearing the fire shader, the inner one leaning in, so the fire has depth and fills the view seen from above), fire climbing the body itself, and sparks leaping off it. It comes
    /// up as the player is lifted and burns on through Ember's words and the suit-up. With <see cref="ClearView"/> the
    /// wall opens a window to the soul, and the fire leaves the body, so the suit can be seen going on.
    /// </summary>
    public class AscensionFire : MonoBehaviour
    {
        [Tooltip("The wall of fire's radius round the body (m): wide enough to hold the soul as well.")]
        [SerializeField] float radius = 3.8f;
        [SerializeField] float wallHeight = 7f;

        public float Target { get; set; }
        public float Intensity { get; private set; }
        /// <summary>The soul is watching the body: the wall parts both ways along the line of sight, and nothing burns on the body.</summary>
        public bool ClearView { get; set; }
        /// <summary>What it burns round (the body).</summary>
        public Transform Centre { get; set; }

        MeshRenderer wall, inner, onBody;
        Material wallMaterial, innerMaterial, bodyMaterial;
        ParticleSystem embers;
        Light glow;
        float emberDue, window, floor;
        bool burning;

        void Start()
        {
            wall = FireFx.Wall(transform, "AscensionWall", radius, 0f, wallHeight, 16f, 0.31f);
            inner = FireFx.Wall(transform, "AscensionWallInner", radius * 0.9f, 0f, wallHeight * 0.7f, 12f, 0.53f, radius * 0.72f);
            onBody = FireFx.Wall(transform, "AscensionOnBody", 0.55f, -1.6f, 2.4f, 3f, 0.71f);
            wallMaterial = wall.sharedMaterial;
            innerMaterial = inner.sharedMaterial;
            bodyMaterial = onBody.sharedMaterial;
            wall.enabled = inner.enabled = onBody.enabled = false;
            embers = StoryFx.CreateParticles(transform, "AscensionEmbers", true, 0.25f, 200);
            glow = new GameObject("AscensionFireLight").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.type = LightType.Point;
            glow.color = new Color(0.25f, 0.55f, 1f);
            glow.range = 14f;
            glow.shadows = LightShadows.None;
            glow.intensity = 0f;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            Intensity = Mathf.MoveTowards(Intensity, Target, dt * 0.45f);
            if (wall == null) return;
            bool on = Centre != null && Intensity > 0.01f;
            glow.enabled = on;
            wall.enabled = inner.enabled = on;
            window = Mathf.MoveTowards(window, ClearView ? 1f : 0f, dt * 0.8f);
            onBody.enabled = on && window < 0.99f;
            if (!on) { burning = false; return; }

            Vector3 c = Centre.position;
            if (!burning) floor = c.y - 1.6f; // (the fire stands on the floor; the body is lifted out of it)
            burning = true;
            float t = Time.time;
            wall.transform.position = inner.transform.position = new Vector3(c.x, floor, c.z);
            onBody.transform.position = c;
            Camera eyes = Camera.main;
            Vector3 toEyes = eyes != null ? StoryFx.Flat(eyes.transform.position - c) : Vector3.forward;
            if (toEyes.sqrMagnitude < 1e-4f) toEyes = Vector3.forward;
            toEyes.Normalize();
            foreach (Material m in new[] { wallMaterial, innerMaterial })
            {
                m.SetFloat("_Power", Intensity);
                m.SetVector("_Centre", c);
                m.SetVector("_WindowDir", new Vector4(toEyes.x, toEyes.z, 0f, 0f));
                m.SetFloat("_WindowOn", window);
            }
            bodyMaterial.SetFloat("_Power", Intensity * (1f - window));
            glow.transform.position = c + Vector3.up * 0.5f;
            glow.intensity = (3.5f + 1.5f * Mathf.PerlinNoise(t * 6f, 2f)) * Intensity;

            emberDue += 110f * Intensity * dt;
            while (emberDue >= 1f)
            {
                emberDue -= 1f;
                float a = Random.value * Mathf.PI * 2f;
                var round = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (window > 0.5f && Mathf.Abs(Vector3.Dot(round, toEyes)) > 0.62f) continue; // keep the window clear
                StoryFx.Emit(embers, wall.transform.position + round * radius + Vector3.up * 0.2f,
                             Vector3.up * Random.Range(2f, 5f) + Random.insideUnitSphere * 1.2f,
                             Random.Range(0.012f, 0.04f), Random.Range(0.7f, 1.5f), new Color(0.55f, 0.88f, 1f, 1f));
            }
        }
    }

    /// <summary>
    /// Blue lightning crackling outward across a surface from a point: jagged, forking bolts that leap and change
    /// every few frames, then fade. Made here from lines, gone when it's done.
    /// </summary>
    public static class GroundLightning
    {
        const int Bolts = 12, Points = 10;
        static readonly Color Bolt = new Color(0.2f, 0.6f, 1f, 1f);
        static readonly Color Hot = new Color(0.82f, 0.96f, 1f, 1f);

        /// <summary>Starts a burst at `at`, over `radius` metres, for `seconds`. `host` runs it.</summary>
        public static void Strike(MonoBehaviour host, Vector3 at, float radius, float seconds) => host.StartCoroutine(Run(at, radius, seconds));

        static IEnumerator Run(Vector3 at, float radius, float seconds)
        {
            var root = new GameObject("GroundLightning");
            var glow = new LineRenderer[Bolts];
            var core = new LineRenderer[Bolts];
            var way = new float[Bolts];
            var reach = new float[Bolts];
            for (int i = 0; i < Bolts; i++)
            {
                way[i] = (i + Random.value * 0.8f) / Bolts * Mathf.PI * 2f;
                reach[i] = radius * Random.Range(0.55f, 1f);
                glow[i] = Line(root.transform, 0.11f, Bolt);
                core[i] = Line(root.transform, 0.03f, Hot);
            }
            for (float t = 0f, next = 0f; t < seconds; t += Time.deltaTime)
            {
                float grow = Mathf.Clamp01(t / (seconds * 0.3f)), fade = Mathf.Clamp01((seconds - t) / (seconds * 0.45f));
                if (t >= next)
                {
                    next = t + 0.055f; // lightning jumps, it doesn't slide
                    for (int i = 0; i < Bolts; i++)
                    {
                        bool lit = Random.value < 0.82f;
                        glow[i].enabled = core[i].enabled = lit;
                        if (!lit) continue;
                        Jag(glow[i], core[i], at, way[i], reach[i] * Mathf.Lerp(0.15f, 1f, grow));
                    }
                }
                for (int i = 0; i < Bolts; i++)
                {
                    glow[i].startColor = glow[i].endColor = StoryFx.WithAlpha(Bolt, 0.75f * fade);
                    core[i].startColor = core[i].endColor = StoryFx.WithAlpha(Hot, fade);
                }
                yield return null;
            }
            Object.Destroy(root);
        }

        static LineRenderer Line(Transform parent, float width, Color color)
        {
            var go = new GameObject("Bolt");
            go.transform.SetParent(parent, false);
            var l = go.AddComponent<LineRenderer>();
            l.sharedMaterial = StoryFx.Additive;
            l.positionCount = Points;
            l.useWorldSpace = true;
            l.startWidth = width;
            l.endWidth = width * 0.25f;
            l.startColor = l.endColor = color;
            l.numCapVertices = 0;
            l.alignment = LineAlignment.View;
            l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            l.receiveShadows = false;
            return l;
        }

        static void Jag(LineRenderer a, LineRenderer b, Vector3 from, float angle, float length)
        {
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)), side = new Vector3(-dir.z, 0f, dir.x);
            float wander = 0f;
            for (int p = 0; p < Points; p++)
            {
                float k = (float)p / (Points - 1);
                wander += Random.Range(-1f, 1f) * length * 0.075f;
                Vector3 at = from + dir * (length * k) + side * wander * Mathf.Min(1f, k * 4f);
                a.SetPosition(p, at);
                b.SetPosition(p, at);
            }
        }
    }
}
