using ArcaneStrike.HandInput;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The Jyoti Seal burned onto the back of the player's left wrist: a crest of an eternal flame (a flame within a
    /// flame, a cup of fire under it, a ring with eight rays). Its lines burn in one after another (<see cref="Reveal"/>
    /// 0 → 1), white-hot at the burning front and cooling to gold with blood-red edges, then it throbs (<see cref="Pulse"/>).
    /// It rides the tracked left hand (Quest hands or the webcam bridge); while that hand isn't tracked it floats
    /// where a raised left wrist would be, so the moment still reads. <see cref="Target"/> rings it for the prompt to
    /// touch it. Drawn by Resources/FX/SealBrand.shader; the crest is drawn from code.
    /// </summary>
    public class JyotiSealBrand : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [Tooltip("Width of the crest on the wrist (m).")]
        [SerializeField] float size = 0.055f;
        [Tooltip("From the wrist joint back along the forearm (m), and up off the skin (m).")]
        [SerializeField] float alongArm = 0.03f, offSkin = 0.021f;
        [Tooltip("How quickly it follows the wrist (per second). High: it should look painted on.")]
        [SerializeField] float follow = 30f;

        /// <summary>0 = nothing yet .. 1 = the whole crest burned in.</summary>
        public float Reveal { get; set; }
        /// <summary>Brightness: 1 normal; the director throbs it.</summary>
        public float Pulse { get; set; } = 1f;
        /// <summary>Where the crest is, and the way it faces (out of the back of the wrist).</summary>
        public Vector3 Position => transform.position;
        public Vector3 Normal => -transform.forward;
        /// <summary>It is on the real tracked hand (not floating where the hand should be).</summary>
        public bool OnHand { get; private set; }

        static readonly int RevealId = Shader.PropertyToID("_Reveal"), PulseId = Shader.PropertyToID("_Pulse"), FadeId = Shader.PropertyToID("_Fade");
        Material crest, ring;
        Renderer crestRenderer, ringRenderer;
        Transform head;
        float target;
        bool placed;

        public static JyotiSealBrand Create(HandInputService input)
        {
            var go = new GameObject("JyotiSealBrand");
            var b = go.AddComponent<JyotiSealBrand>();
            b.input = input;
            return b;
        }

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            Shader shader = Shader.Find("ArcaneStrike/SealBrand");
            crest = new Material(shader) { name = "JyotiSealBrand" };
            crest.SetTexture("_MainTex", DrawCrest(256));
            ring = new Material(shader) { name = "JyotiSealTarget" };
            ring.SetTexture("_MainTex", DrawRing(128));
            ring.SetFloat(RevealId, 1.2f);
            crestRenderer = Quad("Crest", crest, size);
            ringRenderer = Quad("Target", ring, size * 2.1f);
            ringRenderer.enabled = false;
        }

        Renderer Quad(string name, Material m, float width)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * width;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        /// <summary>Show a pulsing target ring round the Seal (touch here).</summary>
        public void Target(float amount)
        {
            target = amount;
            ringRenderer.enabled = amount > 0.01f;
        }

        void LateUpdate()
        {
            if (head == null) head = StoryFx.Head(input);
            if (head == null) return;
            Vector3 position = default;
            Quaternion rotation = Quaternion.identity;
            HandFrame left = input != null ? input.Get(HandSide.Left) : null;
            OnHand = left != null && left.IsTracked && Wrist(left.Joints, out position, out rotation);
            if (!OnHand)
            {
                // Where a raised left wrist would be: low and to the left, the back of the wrist towards the eyes.
                Vector3 f = StoryFx.Flat(head.forward), right = Vector3.Cross(Vector3.up, f);
                position = head.position + f * 0.38f - Vector3.up * 0.22f - right * 0.12f;
                rotation = Quaternion.LookRotation(position - head.position, Vector3.up);
            }
            float k = placed ? 1f - Mathf.Exp(-follow * Time.deltaTime) : 1f;
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, position, k), Quaternion.Slerp(transform.rotation, rotation, k));
            placed = true;

            crest.SetFloat(RevealId, Reveal * 1.02f);
            crest.SetFloat(PulseId, Pulse);
            if (ringRenderer.enabled)
            {
                ring.SetFloat(PulseId, target * (0.6f + 0.6f * Mathf.Sin(Time.time * 5f)));
                ringRenderer.transform.localRotation = Quaternion.Euler(0f, 0f, Time.time * 40f);
            }
        }

        // The back of the left wrist from the tracked joints: along the forearm from the wrist joint, a little off the skin.
        bool Wrist(Vector3[] j, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            Vector3 forward = j[9] - j[0], across = j[5] - j[17];
            if (forward.sqrMagnitude < 1e-6f || across.sqrMagnitude < 1e-6f) return false;
            forward.Normalize();
            Vector3 back = Vector3.Cross(forward, across).normalized; // out of the back of the left hand
            position = j[0] - forward * alongArm + back * offSkin;
            rotation = Quaternion.LookRotation(-back, forward);        // a quad shows its face along -forward
            return true;
        }

        void OnDestroy()
        {
            if (crest != null) Destroy(crest);
            if (ring != null) Destroy(ring);
        }

        // ---------------------------------------------------------------- the crest, drawn from code

        // R: the lines (soft-edged); G: when each bit is drawn (0 first .. 1 last). Linear, so G stays exact.
        static Texture2D DrawCrest(int n)
        {
            var strokes = new System.Collections.Generic.List<(Vector2[] points, float width)>();
            strokes.Add((Flame(0.5f, 0.27f, 0.52f, 0.17f, 0.045f), 0.022f));   // the outer flame
            strokes.Add((Flame(0.5f, 0.33f, 0.3f, 0.08f, 0.03f), 0.016f));     // the flame within it
            strokes.Add((Arc(0.5f, 0.42f, 0.2f, 205f, 335f), 0.016f));          // the cup of fire under it
            strokes.Add((Arc(0.5f, 0.5f, 0.4f, -90f, 270f), 0.02f));           // the ring
            for (int i = 0; i < 8; i++)
            {
                float a = (i * 45f + 22.5f) * Mathf.Deg2Rad;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                strokes.Add((new[] { new Vector2(0.5f, 0.5f) + d * 0.43f, new Vector2(0.5f, 0.5f) + d * (i % 2 == 0 ? 0.49f : 0.47f) }, 0.014f));
            }

            float total = 0f;
            foreach (var s in strokes) total += Length(s.points);
            var r = new float[n * n];
            var g = new float[n * n];
            for (int i = 0; i < g.Length; i++) g[i] = 1f;
            float done = 0f;
            foreach (var (points, width) in strokes)
            {
                for (int p = 0; p + 1 < points.Length; p++)
                {
                    Vector2 a = points[p], b = points[p + 1];
                    float length = Vector2.Distance(a, b);
                    int steps = Mathf.Max(1, Mathf.CeilToInt(length * n * 1.5f));
                    for (int k = 0; k <= steps; k++)
                    {
                        float u = (float)k / steps;
                        Stamp(r, g, n, Vector2.Lerp(a, b, u), width, (done + length * u) / total);
                    }
                    done += length;
                }
            }
            return ToTexture("JyotiSealCrest", r, g, n);
        }

        static Texture2D DrawRing(int n)
        {
            var r = new float[n * n];
            var g = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2((x + 0.5f) / n - 0.5f, (y + 0.5f) / n - 0.5f);
                    float d = Mathf.Abs(p.magnitude - 0.42f);
                    float dash = Mathf.Repeat(Mathf.Atan2(p.y, p.x) / (Mathf.PI * 2f) * 12f, 1f) < 0.6f ? 1f : 0.25f;
                    r[y * n + x] = Mathf.Clamp01(1f - d / 0.035f) * dash;
                }
            return ToTexture("JyotiSealTargetRing", r, g, n);
        }

        static void Stamp(float[] r, float[] g, int n, Vector2 c, float width, float order)
        {
            float radius = width * 0.5f * n;
            int x0 = Mathf.FloorToInt(c.x * n - radius - 1), x1 = Mathf.CeilToInt(c.x * n + radius + 1);
            int y0 = Mathf.FloorToInt(c.y * n - radius - 1), y1 = Mathf.CeilToInt(c.y * n + radius + 1);
            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(n - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(n - 1, x1); x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c * n) / radius;
                    if (d >= 1f) continue;
                    float v = 1f - d * d;
                    int i = y * n + x;
                    if (v > r[i]) r[i] = v;
                    if (v > 0.15f && order < g[i]) g[i] = order;
                }
        }

        static Texture2D ToTexture(string name, float[] r, float[] g, int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(r[i], g[i], 0f, 1f);
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // A flame outline: from the bottom up the left side to the tip, then down the right side, with a little S-sway.
        static Vector2[] Flame(float cx, float bottom, float height, float halfWidth, float sway)
        {
            const int steps = 28;
            var left = new Vector2[steps + 1];
            var right = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float h = (float)i / steps;
                float w = halfWidth * Mathf.Sin(Mathf.PI * Mathf.Pow(h, 0.7f)) * Mathf.Pow(1f - h, 0.25f);
                float s = sway * Mathf.Sin(h * Mathf.PI * 1.6f) * h;
                float y = bottom + height * h;
                left[i] = new Vector2(cx - w + s, y);
                right[i] = new Vector2(cx + w + s, y);
            }
            var all = new Vector2[steps * 2 + 2];
            for (int i = 0; i <= steps; i++) all[i] = left[i];
            for (int i = 0; i <= steps; i++) all[steps + 1 + i] = right[steps - i];
            return all;
        }

        static Vector2[] Arc(float cx, float cy, float radius, float fromDeg, float toDeg)
        {
            int steps = Mathf.Max(8, Mathf.CeilToInt(Mathf.Abs(toDeg - fromDeg) / 6f));
            var p = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, (float)i / steps) * Mathf.Deg2Rad;
                p[i] = new Vector2(cx + Mathf.Cos(a) * radius, cy + Mathf.Sin(a) * radius);
            }
            return p;
        }

        static float Length(Vector2[] p)
        {
            float l = 0f;
            for (int i = 0; i + 1 < p.Length; i++) l += Vector2.Distance(p[i], p[i + 1]);
            return l;
        }
    }
}
