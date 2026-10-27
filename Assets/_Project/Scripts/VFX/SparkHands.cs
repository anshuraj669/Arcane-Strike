using ArcaneStrike.HandInput;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArcaneStrike.VFX
{
    /// <summary>
    /// The player's glowing "Spark" hands: soft gold bones, glowing joints, a palm halo and sparkles
    /// that stream from the fingertips (more when the hand moves fast). Flashes the spell's colour on
    /// every gesture and bursts on every punch/swipe.
    /// Reads <see cref="HandInputService"/>, so it draws over the webcam hands in the editor and over
    /// the real hands (seen through passthrough) on Quest. Everything is additive, no Bloom needed.
    /// </summary>
    public enum HandSkin
    {
        None,   // glow skeleton only
        Spirit, // see-through glowing hand: on Quest it sits over your real hands seen through passthrough
        Solid,  // opaque grey hand, like the hand model in the Scene view (editor testing; hides real hands on Quest)
    }

    [DefaultExecutionOrder(100)]
    public class SparkHands : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] HandMotion motion;
        [SerializeField] GestureRecognizer recognizer;

        [Tooltip("Additive material for joints, halo and sparkles. Arcane Strike → Add Spark Hands creates it. " +
                 "Leave empty to build one at runtime (fine in the editor; device builds need the asset).")]
        [SerializeField] Material glowMaterial;
        [Tooltip("Additive material for the bones. Created together with Glow Material.")]
        [SerializeField] Material lineMaterial;

        [Header("Hand skin (a full hand around the glow)")]
        [SerializeField] HandSkin skin = HandSkin.Spirit;
        [Tooltip("Material for the Spirit skin (Arcane Strike → Add Spark Hands creates it, so device builds include it). " +
                 "Solid always uses URP Lit.")]
        [SerializeField] Material skinMaterial;
        [SerializeField] Color spiritColor = new Color(1f, 0.86f, 0.55f, 0.28f);
        [SerializeField] Color solidColor = new Color(0.8f, 0.8f, 0.82f, 1f);
        [Tooltip("Finger thickness (radius, m). The thumb is a bit thicker and the pinky thinner.")]
        [SerializeField] float fingerRadius = 0.0095f;

        [Header("Look")]
        [SerializeField] Color sparkColor = new Color(1f, 0.79f, 0.29f, 0.9f); // Spark gold #FFC94A
        [SerializeField] float boneWidth = 0.007f;
        [SerializeField] float jointSize = 0.02f;
        [SerializeField] float tipSize = 0.03f;
        [SerializeField] float palmGlowSize = 0.12f;
        [Tooltip("Gentle breathing of the glow, in pulses per second.")]
        [SerializeField] float pulseSpeed = 1.2f;
        [Range(0f, 1f)]
        [SerializeField] float pulseAmount = 0.2f;
        [Tooltip("How fast the hands fade in/out when tracking is found/lost.")]
        [SerializeField] float fadeSpeed = 6f;

        [Header("Sparkles")]
        [SerializeField] float idleSparklesPerSecond = 8f;
        [Tooltip("Extra sparkles per second for every 1 m/s of hand speed.")]
        [SerializeField] float sparklesPerSpeed = 45f;
        [SerializeField] float maxSparklesPerSecond = 150f;
        [SerializeField] float sparkleLifetime = 0.6f;
        [SerializeField] float sparkleSize = 0.012f;
        [SerializeField] int strikeBurst = 30;

        [Header("Flash on gestures and strikes")]
        [Tooltip("How fast a flash fades (per second).")]
        [SerializeField] float flashDecay = 3f;

        // Joint chains drawn as bones (indices are HandJoint values). The last one is the knuckle line.
        static readonly int[][] Chains =
        {
            new[] { 0, 1, 2, 3, 4 }, new[] { 0, 5, 6, 7, 8 }, new[] { 0, 9, 10, 11, 12 },
            new[] { 0, 13, 14, 15, 16 }, new[] { 0, 17, 18, 19, 20 }, new[] { 5, 9, 13, 17 },
        };
        static readonly int[] Tips = { 4, 8, 12, 16, 20 };

        // Finger segments drawn as rounded capsules: (from joint, to joint, radius multiplier).
        static readonly (int a, int b, float r)[] Segments =
        {
            (1, 2, 1.25f), (2, 3, 1.15f), (3, 4, 1.0f),     // thumb
            (5, 6, 1.0f), (6, 7, 0.92f), (7, 8, 0.82f),     // index
            (9, 10, 1.05f), (10, 11, 0.95f), (11, 12, 0.85f), // middle
            (13, 14, 1.0f), (14, 15, 0.9f), (15, 16, 0.8f), // ring
            (17, 18, 0.85f), (18, 19, 0.78f), (19, 20, 0.7f), // pinky
        };
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        class HandRig
        {
            public LineRenderer[] Lines;
            public float Alpha, Flash, SparkleDebt;
            public Color FlashColor = Color.white;
            public Transform[] SkinParts; // Segments, then the palm
            public Renderer[] SkinRenderers;
        }

        MaterialPropertyBlock skinBlock;

        readonly HandRig[] rigs = { new HandRig(), new HandRig() };
        readonly ParticleSystem.Particle[] dotBuffer = new ParticleSystem.Particle[2 * (HandFrame.JointCount + 1)];
        ParticleSystem dots, sparkles;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            if (motion == null) motion = FindAnyObjectByType<HandMotion>();
            if (recognizer == null) recognizer = FindAnyObjectByType<GestureRecognizer>();
            // Summoned spell effects belong with the hands; adding them here gives existing scenes them without re-running setup.
            if (GetComponent<AegisShield>() == null) gameObject.AddComponent<AegisShield>();
            if (GetComponent<SparkBlade>() == null) gameObject.AddComponent<SparkBlade>();
            if (GetComponent<SkyhammerShockwave>() == null) gameObject.AddComponent<SkyhammerShockwave>();
        }

        void OnEnable()
        {
            if (recognizer != null) recognizer.OnGesture += HandleGesture;
            if (motion != null) motion.OnStrike += HandleStrike;
        }

        void OnDisable()
        {
            if (recognizer != null) recognizer.OnGesture -= HandleGesture;
            if (motion != null) motion.OnStrike -= HandleStrike;
        }

        void Start()
        {
            if (glowMaterial == null) glowMaterial = CreateGlowMaterial(CreateSoftDot(64));
            if (lineMaterial == null) lineMaterial = CreateGlowMaterial(CreateSoftLine(64));

            for (int h = 0; h < 2; h++)
            {
                var root = new GameObject(h == 0 ? "SparkHand_Left" : "SparkHand_Right").transform;
                root.SetParent(transform, false);
                rigs[h].Lines = new LineRenderer[Chains.Length];
                for (int c = 0; c < Chains.Length; c++)
                {
                    var line = new GameObject($"Bone{c}").AddComponent<LineRenderer>();
                    line.transform.SetParent(root, false);
                    line.sharedMaterial = lineMaterial;
                    line.useWorldSpace = true;
                    line.positionCount = Chains[c].Length;
                    line.numCapVertices = 4;
                    line.numCornerVertices = 2;
                    line.alignment = LineAlignment.View;
                    line.textureMode = LineTextureMode.Stretch;
                    line.shadowCastingMode = ShadowCastingMode.Off;
                    line.receiveShadows = false;
                    line.enabled = false;
                    rigs[h].Lines[c] = line;
                }
            }

            if (skin != HandSkin.None) BuildSkin();

            dots = MakeParticleSystem("SparkJoints", dotBuffer.Length, false);
            sparkles = MakeParticleSystem("SparkSparkles", 1000, true);
        }

        void LateUpdate()
        {
            if (input == null || dots == null) return;

            float dt = Time.deltaTime;
            float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f) * pulseAmount;
            int dotCount = 0;
            for (int h = 0; h < 2; h++) UpdateHand((HandSide)h, rigs[h], pulse, dt, ref dotCount);
            dots.SetParticles(dotBuffer, dotCount);
        }

        void UpdateHand(HandSide side, HandRig rig, float pulse, float dt, ref int dotCount)
        {
            HandFrame f = input.Get(side);
            rig.Alpha = Mathf.MoveTowards(rig.Alpha, f.IsTracked ? 1f : 0f, fadeSpeed * dt);
            rig.Flash = Mathf.MoveTowards(rig.Flash, 0f, flashDecay * dt);

            bool visible = rig.Alpha > 0.01f;
            foreach (LineRenderer line in rig.Lines)
                if (line.enabled != visible) line.enabled = visible;
            UpdateSkin(f, rig, visible);
            if (!visible) return;

            Color c = Color.Lerp(sparkColor, rig.FlashColor, rig.Flash);
            float alpha = sparkColor.a * rig.Alpha;
            float glow = pulse + rig.Flash * 1.5f;

            // Bones
            Color boneColor = WithAlpha(c, alpha * 0.8f);
            for (int i = 0; i < Chains.Length; i++)
            {
                LineRenderer line = rig.Lines[i];
                int[] chain = Chains[i];
                for (int p = 0; p < chain.Length; p++) line.SetPosition(p, f.Joints[chain[p]]);
                line.widthMultiplier = boneWidth * (1f + rig.Flash);
                line.startColor = line.endColor = boneColor;
            }

            // Joints, fingertips and the palm halo
            for (int j = 0; j < HandFrame.JointCount; j++)
            {
                bool tip = j % 4 == 0 && j > 0;
                dotBuffer[dotCount++] = Dot(f.Joints[j], (tip ? tipSize : jointSize) * glow, WithAlpha(c, alpha * (tip ? 1f : 0.7f)));
            }
            dotBuffer[dotCount++] = Dot(f.PalmCenter, palmGlowSize * glow, WithAlpha(c, alpha * 0.35f));

            // Sparkles: a few at rest, a stream when the hand moves fast
            if (!f.IsTracked) return;
            Vector3 handVelocity = motion != null ? motion.Velocity(side) : Vector3.zero;
            float rate = Mathf.Min(maxSparklesPerSecond, idleSparklesPerSecond + handVelocity.magnitude * sparklesPerSpeed);
            rig.SparkleDebt += rate * rig.Alpha * dt;
            while (rig.SparkleDebt >= 1f)
            {
                rig.SparkleDebt -= 1f;
                EmitSparkle(f.Joints[Tips[Random.Range(0, Tips.Length)]], handVelocity, WithAlpha(c, alpha));
            }
        }

        // ---------------------------------------------------------------- hand skin

        void BuildSkin()
        {
            Material material = skin == HandSkin.Spirit && skinMaterial != null
                ? skinMaterial
                : CreateSkinMaterial(skin, skin == HandSkin.Solid ? solidColor : spiritColor);
            skinBlock = new MaterialPropertyBlock();
            for (int h = 0; h < 2; h++)
            {
                Transform root = transform.Find(h == 0 ? "SparkHand_Left" : "SparkHand_Right");
                int count = Segments.Length + 1;
                rigs[h].SkinParts = new Transform[count];
                rigs[h].SkinRenderers = new Renderer[count];
                for (int i = 0; i < count; i++)
                {
                    bool palm = i == Segments.Length;
                    var go = GameObject.CreatePrimitive(palm ? PrimitiveType.Sphere : PrimitiveType.Capsule);
                    Destroy(go.GetComponent<Collider>());
                    go.name = palm ? "Palm" : $"Segment{i}";
                    go.transform.SetParent(root, false);
                    var r = go.GetComponent<Renderer>();
                    r.sharedMaterial = material;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.enabled = false;
                    rigs[h].SkinParts[i] = go.transform;
                    rigs[h].SkinRenderers[i] = r;
                }
            }
        }

        void UpdateSkin(HandFrame f, HandRig rig, bool visible)
        {
            if (rig.SkinParts == null) return;
            Color baseColor = skin == HandSkin.Solid ? solidColor : spiritColor;
            skinBlock.SetColor(BaseColorId, WithAlpha(baseColor, baseColor.a * rig.Alpha));

            for (int i = 0; i < rig.SkinParts.Length; i++)
            {
                Renderer r = rig.SkinRenderers[i];
                if (r.enabled != visible) r.enabled = visible;
                if (!visible) continue;
                r.SetPropertyBlock(skinBlock);

                Transform t = rig.SkinParts[i];
                if (i < Segments.Length)
                {
                    (int a, int b, float rm) = Segments[i];
                    Vector3 pa = f.Joints[a], pb = f.Joints[b];
                    Vector3 dir = pb - pa;
                    float len = dir.magnitude;
                    if (len < 1e-4f) continue;
                    float radius = fingerRadius * rm;
                    // Unity's capsule is 2 units tall along Y with radius 0.5.
                    t.SetPositionAndRotation((pa + pb) * 0.5f, Quaternion.FromToRotation(Vector3.up, dir / len));
                    t.localScale = new Vector3(radius * 2f, len * 0.5f + radius * 0.5f, radius * 2f);
                }
                else
                {
                    // Palm: a flat oval from the wrist to the knuckles.
                    Vector3 knuckles = (f[HandJoint.IndexProximal] + f[HandJoint.PinkyProximal]) * 0.5f;
                    Vector3 forward = knuckles - f.Wrist;
                    if (forward.sqrMagnitude < 1e-6f) continue;
                    float width = Vector3.Distance(f[HandJoint.IndexProximal], f[HandJoint.PinkyProximal]) + fingerRadius * 2f;
                    t.SetPositionAndRotation((f.Wrist + knuckles) * 0.5f, Quaternion.LookRotation(forward, f.PalmNormal));
                    t.localScale = new Vector3(width, fingerRadius * 2.6f, forward.magnitude * 1.15f);
                }
            }
        }

        /// <summary>See-through unlit material for Spirit, lit opaque material for Solid.</summary>
        public static Material CreateSkinMaterial(HandSkin skin, Color color)
        {
            if (skin == HandSkin.Solid)
            {
                var lit = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "HandSolid" };
                lit.SetColor(BaseColorId, color);
                lit.SetFloat("_Smoothness", 0.35f);
                return lit;
            }
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "HandSpirit" };
            m.SetFloat("_Surface", 1f); // transparent
            m.SetFloat("_Blend", 0f);   // alpha
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent - 1; // under the glow
            m.SetColor(BaseColorId, color);
            return m;
        }

        // ---------------------------------------------------------------- events

        void HandleGesture(GestureEvent e)
        {
            if (e.Type == GestureType.AegisEnd || e.Type == GestureType.GrabEnd || e.Type == GestureType.BladeEnd) return;
            HandRig rig = rigs[(int)e.Hand];
            rig.Flash = 1f;
            rig.FlashColor = GestureColor(e.Type);
        }

        void HandleStrike(StrikeEvent e)
        {
            HandRig rig = rigs[(int)e.Hand];
            rig.Flash = 1f;
            rig.FlashColor = Color.white;
            if (sparkles == null) return;
            for (int i = 0; i < strikeBurst; i++)
                EmitSparkle(e.Position, e.Direction * e.Speed * 0.6f, WithAlpha(Color.Lerp(sparkColor, Color.white, 0.5f), 1f));
        }

        static Color GestureColor(GestureType type)
        {
            switch (type)
            {
                case GestureType.Firebolt: return new Color(1f, 0.45f, 0.15f);
                case GestureType.AegisStart:
                case GestureType.VoltBurst: return new Color(0.31f, 0.89f, 1f);  // Volt cyan #4FE3FF
                case GestureType.Seal: return new Color(0.42f, 0.25f, 0.85f);    // Hollow violet #6B3FD9
                case GestureType.Lumen: return Color.white;
                case GestureType.BladeStart: return new Color(1f, 0.79f, 0.29f);  // Spark gold #FFC94A
                case GestureType.Skyhammer: return new Color(0.31f, 0.89f, 1f);   // Volt cyan #4FE3FF
                default: return new Color(1f, 0.85f, 0.45f);
            }
        }

        // ---------------------------------------------------------------- particles

        void EmitSparkle(Vector3 position, Vector3 handVelocity, Color color)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = position + Random.insideUnitSphere * 0.01f,
                velocity = Random.insideUnitSphere * 0.08f + handVelocity * 0.15f,
                startSize = sparkleSize * Random.Range(0.6f, 1.4f),
                startLifetime = sparkleLifetime * Random.Range(0.6f, 1.2f),
                startColor = color,
            };
            sparkles.Emit(p, 1);
        }

        static ParticleSystem.Particle Dot(Vector3 position, float size, Color color) => new ParticleSystem.Particle
        {
            position = position,
            startSize = size,
            startColor = color,
            remainingLifetime = 1f,
            startLifetime = 1f,
        };

        static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, Mathf.Clamp01(a));

        ParticleSystem MakeParticleSystem(string name, int maxParticles, bool isSparkles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            main.startSpeed = 0f;
            main.startLifetime = isSparkles ? sparkleLifetime : 1f;
            main.gravityModifier = isSparkles ? -0.03f : 0f; // sparkles drift slightly upwards

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;

            if (isSparkles)
            {
                ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

                var fade = new Gradient();
                fade.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
                ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
                color.enabled = true;
                color.color = fade;
            }

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = glowMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ps.Play();
            return ps;
        }

        // ---------------------------------------------------------------- materials (also used by the editor menu)

        /// <summary>URP Particles/Unlit set to additive blending, so overlapping glow adds up into light.</summary>
        public static Material CreateGlowMaterial(Texture2D texture)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var m = new Material(shader) { name = "SparkGlow" };
            m.SetFloat("_Surface", 1f); // transparent
            m.SetFloat("_Blend", 2f);   // additive
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
            m.mainTexture = texture;
            return m;
        }

        /// <summary>Round soft glow dot with a bright core.</summary>
        public static Texture2D CreateSoftDot(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "SparkDot", wrapMode = TextureWrapMode.Clamp };
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Mathf.Clamp01(new Vector2(x - half, y - half).magnitude / half);
                float a = Mathf.Pow(1f - r, 2f) + Mathf.Pow(1f - r, 8f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
            }
            tex.Apply();
            return tex;
        }

        /// <summary>Soft across the width, solid along the length, for the bones.</summary>
        public static Texture2D CreateSoftLine(int size)
        {
            var tex = new Texture2D(4, size, TextureFormat.RGBA32, false) { name = "SparkLine", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
            {
                float v = Mathf.Abs(y / (size - 1f) * 2f - 1f);
                float a = Mathf.Pow(1f - v, 1.5f);
                for (int x = 0; x < 4; x++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            return tex;
        }
    }
}
