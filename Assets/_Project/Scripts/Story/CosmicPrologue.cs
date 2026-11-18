using System.Collections;
using System.Collections.Generic;
using ArcaneStrike.HandInput;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The opening of the game, before the attic: the player floats in deep space, among the stars. Ahead of them a
    /// great deity of shadow sits cross-legged on a lotus seat (four arms, a trident, matted hair with a crescent moon),
    /// an eclipsed sun blazing behind its head like a
    /// halo, wisps of shadow smoking off it, two embers for eyes. It speaks to the player: what it is, what the
    /// Agni-vansh and the Kalakh are, and what the player has to do (find the lantern, wake the fire, fight with
    /// their hands). Then the sun flares, and the player falls back to Earth, outside grandfather's house.
    /// Everything is built here from code (no models). It is built far above the scene, the player's rig is
    /// carried up to it and brought back afterwards, and the scene's fog and far plane are put back as they were.
    /// N / E: the next line.
    /// </summary>
    public class CosmicPrologue : MonoBehaviour
    {
        // What the figure says.
        static readonly string[] Lines =
        {
            "Be still, child. You are not dreaming. You stand where the stars are kept.",
            "I am the Witness. I sat here when the first fire was lit, and I watch it still.",
            "Your blood is the Agni-vansh, the line of sacred fire. Long ago it held back the Kalakh: shadows that crawl through the rifts between worlds.",
            "Your people forgot. The fire did not. It waits in your grandfather's house, inside an old brass lantern.",
            "Find it. Wake it. Then your hands will carry the flame: reach, grab, raise your palms, and fight.",
            "Go now. The veil is already breaking.",
        };

        static readonly Vector3 Far = new Vector3(0f, 5000f, 0f); // (where the cosmos is built: far above everything)
        const float FigureScale = 14f, FigureDistance = 13f; // (its height, m, and how far off it sits)

        public static bool Playing { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Playing = false;

        Transform head, rig, figure, sun, sunRays, seal;
        Vector3 forward;
        readonly List<Transform> parts = new List<Transform>();
        readonly List<Vector3> surface = new List<Vector3>();
        Material flameMaterial;
        Transform handFlame, backGlow;
        readonly List<Material> materials = new List<Material>();
        readonly List<Texture2D> textures = new List<Texture2D>();
        ParticleSystem smoke, dust;
        Material eyeMaterial, sunCore, sunCorona, raysMaterial, sealMaterial, ringMaterial, skyMaterial, sigilMaterial, boltMaterial;
        TextMesh subtitle, subtitleShade;
        AudioSource drone, voice;
        readonly float[] levels = new float[256];
        bool typing;
        Vector3 subtitleAt;
        float speaking, eyeGlow = 0.3f, flare;
        bool next, subtitlePlaced;

        /// <summary>Plays the whole prologue (run it as a coroutine). The screen is black when it ends.</summary>
        public static IEnumerator Play(Transform head)
        {
            if (head == null) yield break;
            var go = new GameObject("CosmicPrologue");
            var p = go.AddComponent<CosmicPrologue>();
            Playing = true;
            yield return p.Run(head);
            Playing = false;
            Destroy(go);
        }

        IEnumerator Run(Transform h)
        {
            head = h;
            rig = head.root;
            Camera cam = Camera.main;
            float farClip = cam != null ? cam.farClipPlane : 1000f;
            bool fog = RenderSettings.fog;

            ScreenFade.Set(Color.black);
            // Up into the cosmos: the rig carried far above the scene, which is out of sight below (past the far plane).
            Vector3 lift = Far;
            rig.position += lift;
            if (cam != null) cam.farClipPlane = 900f;
            RenderSettings.fog = false;
            forward = StoryFx.Flat(head.forward);
            transform.position = head.position;
            bool built = true;
            try { Build(); }
            catch (System.Exception e)
            {
                // Never leave the player stuck in the dark up here: skip the prologue.
                Debug.LogException(e);
                built = false;
            }
            if (!built)
            {
                figure = null;
                rig.position -= lift;
                if (cam != null) cam.farClipPlane = farClip;
                RenderSettings.fog = fog;
                yield break;
            }
            Debug.Log("[Prologue] The cosmos: the Witness speaks. N / E: the next line.");

            yield return ScreenFade.To(new Color(0f, 0f, 0f, 0f), 3.5f);
            yield return Wait(1.5f);

            for (int i = 0; i < Lines.Length; i++)
            {
                yield return Speak(i);
                yield return Wait(0.6f);
            }

            // The sun flares: everything goes white, and the player falls back to Earth.
            drone.PlayOneShot(Chapter2Sounds.ArcaneRing(), 0.5f);
            for (float t = 0f; t < 1.6f; t += Time.deltaTime)
            {
                flare = t / 1.6f;
                yield return null;
            }
            yield return ScreenFade.To(new Color(1f, 0.95f, 0.85f, 1f), 0.8f);
            yield return Wait(0.4f, false);
            ScreenFade.Set(Color.black);

            rig.position -= lift;
            if (cam != null) cam.farClipPlane = farClip;
            RenderSettings.fog = fog;
        }

        IEnumerator Wait(float seconds, bool skippable = true)
        {
            next = false;
            for (float t = 0f; t < seconds && !(skippable && next); t += Time.deltaTime) yield return null;
            next = false;
        }

        // The Witness speaks the line aloud (Resources/VO/Witness/wNN), its voice coming from the figure; its eyes and
        // the gold crackle about it follow the words. No text: only if a recording is missing is the line shown.
        IEnumerator Speak(int index)
        {
            AudioClip clip = Resources.Load<AudioClip>($"VO/Witness/w{index + 1:00}");
            if (clip == null)
            {
                yield return Say(Lines[index]);
                yield break;
            }
            Debug.Log($"[Witness] {Lines[index]}");
            next = false;
            voice.clip = clip;
            voice.Play();
            while (voice.isPlaying && !next) yield return null;
            voice.Stop();
            next = false;
        }

        // (Fallback) typed out under the figure while it speaks.
        IEnumerator Say(string text)
        {
            Debug.Log($"[Witness] {text}");
            next = false;
            const float typeSpeed = 24f;
            typing = true;
            float shown = 0f;
            while (shown < text.Length && !next)
            {
                shown += typeSpeed * Time.deltaTime;
                SetSubtitle(Wrap(text.Substring(0, Mathf.Min(text.Length, Mathf.FloorToInt(shown))), 44));
                speaking = 1f;
                yield return null;
            }
            next = false;
            SetSubtitle(Wrap(text, 44));
            speaking = 0.35f;
            float hold = 1.4f + text.Length * 0.025f;
            for (float t = 0f; t < hold && !next; t += Time.deltaTime) yield return null;
            next = false;
            speaking = 0f;
            for (float t = 0f; t < 0.35f; t += Time.deltaTime)
            {
                SetSubtitleAlpha(1f - t / 0.35f);
                yield return null;
            }
            SetSubtitle("");
            typing = false;
        }

        void Update()
        {
            if (GameInput.Pressed(GameAction.Skip) || GameInput.Pressed(GameAction.Interact)) next = true;
        }

        void LateUpdate()
        {
            if (figure == null || drone == null) return;
            float time = Time.time, dt = Time.deltaTime;

            // The figure breathes; the sun turns and pulses.
            float breath = Mathf.Sin(time * 0.6f);
            figure.localScale = new Vector3(1f, 1f + breath * 0.004f, 1f);
            sunRays.localRotation = Quaternion.Euler(0f, 0f, time * 2.5f);
            Billboard(sun);

            float pulse = 1f + 0.06f * Mathf.Sin(time * 1.1f) + flare * 4f;
            // The eclipse: a black disc in a blazing white-blue ring; at the end it breaks open into light.
            sunCore.SetColor("_BaseColor", new Color(0f, 0f, 0.005f, 1f - flare));
            ringMaterial.SetColor("_BaseColor", new Color(2.2f, 2.6f, 3.4f, 1f) * pulse);
            sunCorona.SetColor("_BaseColor", new Color(0.35f, 0.5f, 1f, 0.45f) * pulse);
            raysMaterial.SetColor("_BaseColor", new Color(0.6f, 0.72f, 1f, 0.22f + 0.08f * Mathf.Sin(time * 0.7f)) * pulse);
            Storm(time, dt);

            // While it speaks, everything follows how loud the voice is right now.
            if (voice.isPlaying)
            {
                voice.GetOutputData(levels, 0);
                float sum = 0f;
                foreach (float v in levels) sum += v * v;
                float level = Mathf.Clamp01(Mathf.Sqrt(sum / levels.Length) * 7f);
                speaking = Mathf.Lerp(speaking, level, dt * (level > speaking ? 18f : 6f));
            }
            else if (!typing) speaking = Mathf.Lerp(speaking, 0f, dt * 4f);
            // Under it all, a low hum of the deep (a little louder when it speaks).
            drone.volume = Mathf.Lerp(drone.volume, 0.07f + 0.08f * speaking, dt * 4f);

            UpdateCosmicFigure(time, dt);
            EmitDust(dt);
            PlaceSubtitle(dt);
        }

        void Billboard(Transform t)
        {
            Vector3 away = t.position - head.position;
            if (away.sqrMagnitude > 1e-4f) t.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        // ---------------------------------------------------------------- building it

        void Build()
        {
            Quaternion facing = Quaternion.LookRotation(forward, Vector3.up);
            Vector3 eyes = head.position;

            // The sky: stars and nebulae all round.
            var sky = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sky.name = "Stars";
            Destroy(sky.GetComponent<Collider>());
            sky.transform.SetParent(transform, false);
            sky.transform.position = eyes;
            sky.transform.localScale = Vector3.one * 800f;
            skyMaterial = Unlit(Color.white);
            skyMaterial.SetFloat("_Cull", (float)CullMode.Front); // (seen from inside)
            Texture2D stars = StarTexture(2048, 1024);
            skyMaterial.SetTexture("_BaseMap", stars);
            skyMaterial.mainTexture = stars;
            Setup(sky, skyMaterial);

            // The seated figure, facing the player, a little below them, so they look up at it.
            figure = new GameObject("Witness").transform;
            figure.SetParent(transform, false);
            figure.SetPositionAndRotation(eyes + forward * FigureDistance + Vector3.down * 5.5f, facing);
            BuildCosmicFigure();
            BuildPlanet(eyes);

            // The sun, behind and above its head: a halo it sits in front of.
            sun = new GameObject("Sun").transform;
            sun.SetParent(transform, false);
            // (far behind, high enough that the head sits in front of the lower half of the disc)
            const float sunDistance = FigureDistance + 60f;
            // (straight behind its head as the player sees it: the blazing ring is a halo round the head)
            Vector3 toHead = (headPoint - eyes).normalized;
            sun.position = eyes + toHead * sunDistance;
            float sunSize = 17f;
            sunCorona = Glow(Dot(), new Color(0.35f, 0.5f, 1f, 0.45f));
            Quad("Corona", sunCorona, sun).localScale = Vector3.one * sunSize * 3.2f;
            raysMaterial = Glow(RaysTexture(512), new Color(0.6f, 0.72f, 1f, 0.25f));
            sunRays = Quad("Rays", raysMaterial, sun);
            sunRays.localScale = Vector3.one * sunSize * 4.5f;
            ringMaterial = Glow(RingTexture(512), new Color(2.2f, 2.6f, 3.4f, 1f));
            Quad("Ring", ringMaterial, sun).localScale = Vector3.one * sunSize * 1.12f;
            // The black disc in front (drawn over the ring's inside: alpha blended, not glow).
            sunCore = Glow(Disc(), Color.black);
            sunCore.SetFloat("_Blend", 0f);
            sunCore.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            sunCore.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            Transform disc = Quad("Eclipse", sunCore, sun);
            disc.localScale = Vector3.one * sunSize;
            disc.localPosition = new Vector3(0f, 0f, -0.5f);
            // A shaft of light falling through it from the sky.
            Material shaft = Glow(VFX.SparkHands.CreateSoftLine(64), new Color(0.6f, 0.75f, 1.6f, 0.6f));
            Transform beam = Quad("Shaft", shaft, sun);
            beam.localPosition = new Vector3(0f, sunSize * 2.2f, 0.2f);
            beam.localScale = new Vector3(1.4f, sunSize * 3.6f, 1f);

            BuildRealm(eyes, facing);

            // Shadow smoking off the figure, and dust drifting past the player (near and far: depth).
            smoke = StoryFx.CreateParticles(transform, "Shadow", false, -0.01f, 1500);
            dust = StoryFx.CreateParticles(transform, "Stardust", true, 0f, 800);

            // The subtitle.
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var textMaterial = new Material(font.material) { name = "WitnessText", renderQueue = 5000 };
            textMaterial.SetFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
            materials.Add(textMaterial);
            subtitleShade = Text("Shade", font, textMaterial, new Color(0f, 0f, 0f, 0.85f), 0.004f);
            subtitle = Text("Line", font, textMaterial, new Color(1f, 0.88f, 0.62f, 1f), 0f);

            // Its voice, from the figure (heard from where it sits, at full strength), and the hum of the deep.
            var mouth = new GameObject("Voice");
            mouth.transform.SetParent(figure, false);
            mouth.transform.localPosition = new Vector3(0f, 1.15f, 0.1f);
            voice = mouth.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0.8f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.minDistance = 60f;
            voice.maxDistance = 200f;
            voice.dopplerLevel = 0f;
            drone = gameObject.AddComponent<AudioSource>();
            drone.clip = Drone();
            drone.loop = true;
            drone.spatialBlend = 0f;
            drone.volume = 0f;
            drone.Play();
        }

        // A hooded figure in a long cloak, sitting in meditation (figure space: 1 ≈ a person's size; y up, +z towards
        // the player): one flowing shape from the spread knees to the point of the hood, with folds; sleeves meeting
        // in the lap, where its hands cup a small flame.
        void BuildFigure(Material m)
        {
            // (height, half-width, half-depth, forward shift) up the cloak.
            float[,] profile =
            {
                { 0.00f, 0.92f, 0.66f, 0.13f }, { 0.07f, 1.04f, 0.76f, 0.15f }, { 0.16f, 0.96f, 0.70f, 0.13f },
                { 0.25f, 0.62f, 0.46f, 0.05f }, { 0.36f, 0.43f, 0.32f, 0.01f }, { 0.50f, 0.41f, 0.29f, 0.00f },
                { 0.64f, 0.47f, 0.30f, 0.00f }, { 0.78f, 0.55f, 0.30f, -0.01f }, { 0.86f, 0.46f, 0.28f, 0.00f },
                { 0.93f, 0.27f, 0.24f, 0.01f }, { 1.00f, 0.25f, 0.26f, 0.02f }, { 1.10f, 0.26f, 0.28f, 0.02f },
                { 1.21f, 0.24f, 0.26f, 0.00f }, { 1.31f, 0.18f, 0.20f, -0.03f }, { 1.40f, 0.08f, 0.09f, -0.07f },
            };
            Mesh cloak = Lathe(profile, 56, new Vector3(0f, 1.47f, -0.1f), new Vector3(0f, 0f, 0.13f));
            var go = new GameObject("Cloak");
            go.transform.SetParent(figure, false);
            go.AddComponent<MeshFilter>().sharedMesh = cloak;
            Setup(go, m);
            foreach (Vector3 v in cloak.vertices) surface.Add(v);

            // A rim of pale light round its outline (a slightly bigger shell, only its far side drawn: it shows just
            // beyond the edges), so the shape reads against the dark.
            var rim = new GameObject("Rim");
            rim.transform.SetParent(figure, false);
            rim.transform.localScale = Vector3.one * 1.035f;
            rim.transform.localPosition = new Vector3(0f, -0.7f * 0.035f, 0f);
            rim.AddComponent<MeshFilter>().sharedMesh = cloak;
            Material rimMaterial = Glow(Texture2D.whiteTexture, new Color(0.35f, 0.5f, 1.3f, 0.55f));
            rimMaterial.SetFloat("_Cull", (float)CullMode.Front);
            Setup(rim, rimMaterial);

            // Sleeves from the shoulders, down and forward to the lap; the hands together there.
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 shoulder = new Vector3(0.44f * side, 0.8f, 0.02f), elbow = new Vector3(0.5f * side, 0.47f, 0.2f), wrist = new Vector3(0.13f * side, 0.31f, 0.45f);
                Limb(m, shoulder, elbow, 0.1f);
                Limb(m, elbow, wrist, 0.12f);
                Blob(m, new Vector3(0.06f * side, 0.3f, 0.49f), new Vector3(0.09f, 0.06f, 0.12f));
            }
        }

        // A surface turned round the vertical (rings from the profile, folds rippling down it), closed at both ends.
        Mesh Lathe(float[,] profile, int sides, Vector3 tip, Vector3 bottom)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            int rings = profile.GetLength(0);
            for (int k = 0; k < rings; k++)
            {
                float h = profile[k, 0];
                float fold = 0.055f * (1f - Mathf.SmoothStep(0f, 0.55f, h)) + 0.012f;
                for (int i = 0; i < sides; i++)
                {
                    float a = (float)i / sides * Mathf.PI * 2f;
                    float f = 1f + fold * Mathf.Sin(a * 9f + h * 4f) + fold * 0.5f * Mathf.Sin(a * 17f - h * 7f);
                    verts.Add(new Vector3(Mathf.Sin(a) * profile[k, 1] * f, h, profile[k, 3] + Mathf.Cos(a) * profile[k, 2] * f));
                }
            }
            int top = verts.Count;
            verts.Add(tip);
            int bot = verts.Count;
            verts.Add(bottom);
            for (int k = 0; k < rings - 1; k++)
                for (int i = 0; i < sides; i++)
                {
                    int a = k * sides + i, b = k * sides + (i + 1) % sides, c = a + sides, d = b + sides;
                    tris.AddRange(new[] { a, c, b, b, c, d });
                }
            int last = (rings - 1) * sides;
            for (int i = 0; i < sides; i++)
            {
                tris.AddRange(new[] { last + i, top, last + (i + 1) % sides });
                tris.AddRange(new[] { bot, i, (i + 1) % sides });
            }
            var mesh = new Mesh { name = "Cloak" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        Vector3 SurfacePoint() => figure.TransformPoint(surface[rng.Next(surface.Count)]);

        void Blob(Material m, Vector3 at, Vector3 size)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Setup(s, m);
            s.transform.SetParent(figure, false);
            s.transform.localPosition = at;
            s.transform.localScale = size;
            parts.Add(s.transform);
        }

        void Limb(Material m, Vector3 a, Vector3 b, float radius)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Setup(c, m);
            c.transform.SetParent(figure, false);
            Vector3 d = b - a;
            c.transform.localPosition = (a + b) * 0.5f;
            c.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
            c.transform.localScale = new Vector3(radius * 2f, (d.magnitude + radius * 2f) * 0.5f, radius * 2f);
            parts.Add(c.transform);
        }

        void Setup(GameObject go, Material m)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var r = go.GetComponent<Renderer>();
            if (r == null) r = go.AddComponent<MeshRenderer>(); // (the meshes made here)
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        Transform Quad(string name, Material m, Transform parent)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Setup(q, m);
            q.transform.SetParent(parent, false);
            return q.transform;
        }

        TextMesh Text(string name, Font font, Material m, Color color, float depth)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var t = go.AddComponent<TextMesh>();
            t.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            t.anchor = TextAnchor.UpperCenter;
            t.alignment = TextAlignment.Center;
            t.fontSize = 128;
            t.characterSize = 0.0075f;
            t.lineSpacing = 1.1f;
            t.color = color;
            t.text = "";
            go.transform.localPosition = Vector3.forward * depth; // (re-placed every frame)
            return t;
        }

        Material Unlit(Color color)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "PrologueUnlit" };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Cull", (float)CullMode.Off); // (the rocks are made here: both sides, whichever way they wind)
            materials.Add(m);
            return m;
        }

        Material Glow(Texture2D texture, Color color)
        {
            Material m = VFX.SparkHands.CreateGlowMaterial(texture);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Cull", (float)CullMode.Off);
            materials.Add(m);
            return m;
        }

        // ---------------------------------------------------------------- the subtitle

        void SetSubtitle(string text)
        {
            subtitle.text = text;
            subtitleShade.text = text;
            SetSubtitleAlpha(1f);
        }

        void SetSubtitleAlpha(float a)
        {
            Color c = subtitle.color;
            c.a = a;
            subtitle.color = c;
            c = subtitleShade.color;
            c.a = a * 0.85f;
            subtitleShade.color = c;
        }

        // Low in the view, a couple of metres off, drifting after the head (not glued to it).
        void PlaceSubtitle(float dt)
        {
            Vector3 flat = StoryFx.Flat(head.forward);
            Vector3 want = head.position + flat * 2.2f + Vector3.down * 0.55f;
            subtitleAt = subtitlePlaced ? Vector3.Lerp(subtitleAt, want, 1f - Mathf.Exp(-2.5f * dt)) : want;
            subtitlePlaced = true;
            Quaternion r = Quaternion.LookRotation(StoryFx.Flat(subtitleAt - head.position), Vector3.up);
            subtitle.transform.SetPositionAndRotation(subtitleAt, r);
            subtitleShade.transform.SetPositionAndRotation(subtitleAt + r * new Vector3(0.006f, -0.006f, 0.004f), r);
        }

        static string Wrap(string s, int width)
        {
            var sb = new System.Text.StringBuilder();
            int col = 0;
            foreach (string word in s.Split(' '))
            {
                if (col > 0 && col + word.Length > width)
                {
                    sb.Append('\n');
                    col = 0;
                }
                else if (col > 0)
                {
                    sb.Append(' ');
                    col++;
                }
                sb.Append(word);
                col += word.Length;
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- particles

        static readonly System.Random rng = new System.Random(); // (not the game's random numbers: replays re-seed those)
        static float V => (float)rng.NextDouble();
        static float R(float a, float b) => a + (b - a) * V;

        // Wisps of shadow rising off the figure, dark against the sun.
        void EmitSmoke(float dt)
        {
            int n = Mathf.FloorToInt(55f * dt + V);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = SurfacePoint();
                Vector3 v = new Vector3(R(-0.15f, 0.15f), R(0.25f, 0.7f), R(-0.15f, 0.15f));
                StoryFx.Emit(smoke, p, v, R(0.5f, 1.3f), R(2.5f, 4.5f), new Color(0.02f, 0.015f, 0.04f, 0.75f));
            }
        }

        // Specks of light drifting past the player at every depth.
        void EmitDust(float dt)
        {
            int n = Mathf.FloorToInt(30f * dt + V);
            for (int i = 0; i < n; i++)
            {
                float depth = R(1f, 14f);
                Vector3 p = head.position + Quaternion.LookRotation(forward) * new Vector3(R(-1f, 1f) * depth, R(-0.8f, 0.8f) * depth * 0.6f, depth);
                Vector3 v = new Vector3(R(-0.05f, 0.05f), R(-0.02f, 0.06f), R(-0.08f, -0.02f));
                Color c = V < 0.5f ? new Color(1f, 0.8f, 0.5f, 1f) : new Color(0.7f, 0.8f, 1f, 1f);
                StoryFx.Emit(dust, p, v, R(0.008f, 0.02f) * Mathf.Sqrt(depth), R(3f, 6f), c);
            }
        }

        // ---------------------------------------------------------------- the Witness: a deity of shadow

        // A great seated deity in silhouette (drawn here, in code): matted hair piled high with a crescent moon in it,
        // four arms (a trident with a small drum on it; a hand raised in blessing; a hand holding something up; a hand
        // on a water-pot at the knee), a cobra rising at the shoulder, cross-legged on a lotus seat. Its body is
        // shadow: dark, smoke curling off its edges, a violet glow round its outline; its half-closed eyes, third eye
        // and the crescent glow faintly, brighter while it speaks. It faces the player.
        const int MaskSize = 1024;
        const float FigureHeight = 15f; // (m: the drawing's full height)
        Material bodyMaterial, rimMaterial, detailMaterial;
        readonly List<Vector3> edge = new List<Vector3>();
        Vector3 headPoint;

        void BuildCosmicFigure()
        {
            float[] body = new float[MaskSize * MaskSize], detail = new float[MaskSize * MaskSize];
            DrawDeity(body, detail);

            // Soft edges, and a wide blurred copy for the glow round it.
            float[] soft = Blur(body, 2);
            float[] rim = Blur(Blur(body, 14), 14);

            Transform bodyQuad = Quad("Body", bodyMaterial = Glow(MaskTexture(soft, "WitnessBody"), new Color(0.012f, 0.008f, 0.025f, 1f)), figure);
            bodyMaterial.SetFloat("_Blend", 0f);
            bodyMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            bodyMaterial.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            Transform rimQuad = Quad("Glow", rimMaterial = Glow(MaskTexture(rim, "WitnessGlow"), new Color(0.55f, 0.3f, 1.3f, 0.8f)), figure);
            Transform detailQuad = Quad("Details", detailMaterial = Glow(MaskTexture(Blur(detail, 1), "WitnessDetails"), new Color(1.6f, 1.6f, 2f, 1f)), figure);
            foreach (Transform q in new[] { bodyQuad, rimQuad, detailQuad }) q.localScale = new Vector3(FigureHeight, FigureHeight, 1f);
            bodyQuad.localPosition = new Vector3(0f, FigureHeight * 0.5f, 0f);
            rimQuad.localPosition = new Vector3(0f, FigureHeight * 0.5f, 0.4f);     // (behind)
            detailQuad.localPosition = new Vector3(0f, FigureHeight * 0.5f, -0.05f); // (in front)
            rimQuad.localScale *= 1.0f;

            // Where its edges are (smoke rises from them) and where the inside is (for the speaking glow).
            for (int y = 2; y < MaskSize - 2; y += 3)
                for (int x = 2; x < MaskSize - 2; x += 3)
                {
                    int i = y * MaskSize + x;
                    if (body[i] > 0.5f && (body[i - 3] < 0.5f || body[i + 3] < 0.5f || body[i - 3 * MaskSize] < 0.5f || body[i + 3 * MaskSize] < 0.5f))
                        edge.Add(Local(x / (float)MaskSize, y / (float)MaskSize, -0.02f));
                    else if (body[i] > 0.5f && rng.NextDouble() < 0.05) surface.Add(Local(x / (float)MaskSize, y / (float)MaskSize, -0.02f));
                }
            headPoint = figure.TransformPoint(Local(0.5f, 0.6f, 0f));
        }

        Vector3 Local(float u, float v, float z) => new Vector3((u - 0.5f) * FigureHeight, v * FigureHeight, z);

        void UpdateCosmicFigure(float time, float dt)
        {
            float glow = 0.75f + 0.15f * Mathf.Sin(time * 0.9f) + speaking * 0.9f + flare * 3f;
            rimMaterial.SetColor("_BaseColor", new Color(0.55f, 0.3f, 1.3f, 0.8f) * glow);
            float eyes = 0.45f + speaking * 1.6f + flare * 3f;
            detailMaterial.SetColor("_BaseColor", new Color(1.5f, 1.5f, 2.1f, 1f) * eyes);

            // Shadow smoking off its edges (more while it speaks), and specks of violet light drifting off it.
            int n = Mathf.FloorToInt((70f + 90f * speaking) * dt + V);
            for (int i = 0; i < n && edge.Count > 0; i++)
            {
                Vector3 p = figure.TransformPoint(edge[rng.Next(edge.Count)]);
                Vector3 v = new Vector3(R(-0.25f, 0.25f), R(0.3f, 0.9f), R(-0.25f, 0.25f));
                StoryFx.Emit(smoke, p, v, R(0.6f, 1.6f), R(2f, 4f), new Color(0.015f, 0.01f, 0.03f, 0.7f));
            }
            if (V < dt * (6f + 30f * speaking) && edge.Count > 0)
                StoryFx.Emit(dust, figure.TransformPoint(edge[rng.Next(edge.Count)]), Random3() * 0.3f + Vector3.up * 0.3f, R(0.06f, 0.14f), R(1f, 2.5f), new Color(0.8f, 0.55f, 1f, 1f));
        }

        // ---- the drawing (u across, v up, both 0 → 1)

        void DrawDeity(float[] m, float[] d)
        {
            // The lotus seat: a drum, a ring of petals round its rim.
            Poly(m, (0.25f, 0.02f), (0.75f, 0.02f), (0.78f, 0.09f), (0.22f, 0.09f));
            Ellipse(m, 0.5f, 0.02f, 0.25f, 0.015f);
            for (int i = 0; i <= 12; i++) Ellipse(m, 0.235f + i * 0.53f / 12f, 0.1f, 0.03f, 0.028f);

            // Crossed legs: the lap, thighs out to wide knees, shins crossed in front, a foot turned up.
            Ellipse(m, 0.5f, 0.2f, 0.2f, 0.065f);
            Capsule(m, 0.45f, 0.24f, 0.245f, 0.185f, 0.052f);
            Capsule(m, 0.55f, 0.24f, 0.755f, 0.185f, 0.052f);
            Capsule(m, 0.26f, 0.155f, 0.6f, 0.165f, 0.036f);
            Capsule(m, 0.74f, 0.155f, 0.4f, 0.145f, 0.034f);
            Ellipse(m, 0.42f, 0.185f, 0.035f, 0.018f);

            // The body: a slim waist, the chest, rounded shoulders.
            Poly(m, (0.435f, 0.25f), (0.565f, 0.25f), (0.58f, 0.36f), (0.605f, 0.465f), (0.595f, 0.5f), (0.405f, 0.5f), (0.395f, 0.465f), (0.42f, 0.36f));
            Ellipse(m, 0.395f, 0.482f, 0.036f, 0.03f);
            Ellipse(m, 0.605f, 0.482f, 0.036f, 0.03f);
            // Neck and head, the long hair falling behind the shoulders.
            Capsule(m, 0.5f, 0.5f, 0.5f, 0.54f, 0.022f);
            Ellipse(m, 0.5f, 0.585f, 0.044f, 0.054f);
            Capsule(m, 0.462f, 0.585f, 0.43f, 0.5f, 0.016f);
            Capsule(m, 0.538f, 0.585f, 0.57f, 0.5f, 0.016f);
            // The matted hair piled high, a lock sweeping off to one side.
            Ellipse(m, 0.5f, 0.645f, 0.04f, 0.026f);
            Ellipse(m, 0.5f, 0.672f, 0.033f, 0.024f);
            Ellipse(m, 0.5f, 0.698f, 0.026f, 0.022f);
            Ellipse(m, 0.5f, 0.72f, 0.016f, 0.017f);
            Capsule(m, 0.508f, 0.722f, 0.542f, 0.745f, 0.009f);
            Capsule(m, 0.542f, 0.745f, 0.563f, 0.716f, 0.0085f);
            Capsule(m, 0.563f, 0.716f, 0.572f, 0.665f, 0.008f);

            // The cobra: round the neck, rising beside the head, its hood spread.
            Capsule(m, 0.475f, 0.51f, 0.428f, 0.53f, 0.011f);
            Capsule(m, 0.428f, 0.53f, 0.42f, 0.585f, 0.0095f);
            Ellipse(m, 0.42f, 0.602f, 0.018f, 0.022f);

            // Arm 1 (back, left): up to a trident, a small drum tied to its staff.
            Capsule(m, 0.39f, 0.49f, 0.33f, 0.445f, 0.026f);
            Capsule(m, 0.33f, 0.445f, 0.35f, 0.545f, 0.021f);
            Ellipse(m, 0.351f, 0.55f, 0.021f, 0.024f);
            Capsule(m, 0.352f, 0.3f, 0.352f, 0.86f, 0.0055f);
            Poly(m, (0.33f, 0.71f), (0.374f, 0.71f), (0.36f, 0.735f), (0.374f, 0.76f), (0.33f, 0.76f), (0.344f, 0.735f)); // the drum
            Capsule(m, 0.325f, 0.86f, 0.379f, 0.86f, 0.006f);  // the trident's crossbar
            Capsule(m, 0.325f, 0.86f, 0.322f, 0.92f, 0.0058f); // its side prongs
            Capsule(m, 0.379f, 0.86f, 0.382f, 0.92f, 0.0058f);
            Poly(m, (0.316f, 0.915f), (0.322f, 0.95f), (0.328f, 0.915f));
            Poly(m, (0.376f, 0.915f), (0.382f, 0.95f), (0.388f, 0.915f));
            Capsule(m, 0.352f, 0.86f, 0.352f, 0.94f, 0.006f);  // and the middle one, the tallest
            Poly(m, (0.344f, 0.935f), (0.352f, 0.985f), (0.36f, 0.935f));

            // Arm 2 (front, left): the hand raised, palm out, in blessing.
            Capsule(m, 0.4f, 0.48f, 0.385f, 0.37f, 0.025f);
            Capsule(m, 0.385f, 0.37f, 0.415f, 0.455f, 0.021f);
            Ellipse(m, 0.418f, 0.475f, 0.024f, 0.03f);
            for (int f = 0; f < 4; f++) Capsule(m, 0.404f + f * 0.0095f, 0.49f, 0.404f + f * 0.0095f, 0.525f - Mathf.Abs(f - 1.5f) * 0.007f, 0.0048f);
            Capsule(m, 0.397f, 0.468f, 0.389f, 0.49f, 0.005f); // the thumb

            // Arm 3 (back, right): raised, holding a small drum up.
            Capsule(m, 0.61f, 0.49f, 0.67f, 0.445f, 0.026f);
            Capsule(m, 0.67f, 0.445f, 0.655f, 0.545f, 0.021f);
            Ellipse(m, 0.655f, 0.552f, 0.021f, 0.022f);
            Poly(m, (0.635f, 0.565f), (0.68f, 0.565f), (0.666f, 0.588f), (0.68f, 0.61f), (0.635f, 0.61f), (0.649f, 0.588f));

            // Arm 4 (front, right): down to a water-pot at the knee.
            Capsule(m, 0.6f, 0.48f, 0.655f, 0.36f, 0.026f);
            Capsule(m, 0.655f, 0.36f, 0.64f, 0.275f, 0.022f);
            Ellipse(m, 0.645f, 0.235f, 0.042f, 0.037f);
            Capsule(m, 0.645f, 0.26f, 0.645f, 0.282f, 0.017f);

            // What glows: the half-closed eyes, the third eye, the crescent moon in the hair.
            Ellipse(d, 0.483f, 0.583f, 0.01f, 0.0026f);
            Ellipse(d, 0.517f, 0.583f, 0.01f, 0.0026f);
            Ellipse(d, 0.5f, 0.607f, 0.0032f, 0.008f);
            Ellipse(d, 0.463f, 0.668f, 0.019f, 0.019f);
            Ellipse(d, 0.471f, 0.673f, 0.016f, 0.017f, 0f); // (cut out: a crescent)
        }

        static void Ellipse(float[] m, float cx, float cy, float rx, float ry, float value = 1f)
        {
            int x0 = Mathf.Max(0, (int)((cx - rx) * MaskSize) - 1), x1 = Mathf.Min(MaskSize - 1, (int)((cx + rx) * MaskSize) + 1);
            int y0 = Mathf.Max(0, (int)((cy - ry) * MaskSize) - 1), y1 = Mathf.Min(MaskSize - 1, (int)((cy + ry) * MaskSize) + 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = ((x + 0.5f) / MaskSize - cx) / rx, dy = ((y + 0.5f) / MaskSize - cy) / ry;
                    if (dx * dx + dy * dy <= 1f) m[y * MaskSize + x] = value;
                }
        }

        static void Capsule(float[] m, float ax, float ay, float bx, float by, float r)
        {
            int x0 = Mathf.Max(0, (int)((Mathf.Min(ax, bx) - r) * MaskSize) - 1), x1 = Mathf.Min(MaskSize - 1, (int)((Mathf.Max(ax, bx) + r) * MaskSize) + 1);
            int y0 = Mathf.Max(0, (int)((Mathf.Min(ay, by) - r) * MaskSize) - 1), y1 = Mathf.Min(MaskSize - 1, (int)((Mathf.Max(ay, by) + r) * MaskSize) + 1);
            var a = new Vector2(ax, ay);
            Vector2 ab = new Vector2(bx, by) - a;
            float len2 = Mathf.Max(ab.sqrMagnitude, 1e-9f);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2((x + 0.5f) / MaskSize, (y + 0.5f) / MaskSize);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                    if ((p - (a + ab * t)).sqrMagnitude <= r * r) m[y * MaskSize + x] = 1f;
                }
        }

        static void Poly(float[] m, params (float x, float y)[] pts)
        {
            float minY = 1f, maxY = 0f;
            foreach (var p in pts) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
            var xs = new List<float>();
            for (int y = Mathf.Max(0, (int)(minY * MaskSize)); y <= Mathf.Min(MaskSize - 1, (int)(maxY * MaskSize)); y++)
            {
                float fy = (y + 0.5f) / MaskSize;
                xs.Clear();
                for (int i = 0; i < pts.Length; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Length];
                    if ((a.y <= fy && b.y > fy) || (b.y <= fy && a.y > fy)) xs.Add(a.x + (fy - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                    for (int x = Mathf.Max(0, (int)(xs[k] * MaskSize)); x <= Mathf.Min(MaskSize - 1, (int)(xs[k + 1] * MaskSize)); x++)
                        m[y * MaskSize + x] = 1f;
            }
        }

        // A box blur, across then down (run twice for a soft glow).
        static float[] Blur(float[] src, int r)
        {
            int n = MaskSize;
            var tmp = new float[n * n];
            var dst = new float[n * n];
            float inv = 1f / (2 * r + 1);
            for (int y = 0; y < n; y++)
            {
                float sum = 0f;
                for (int x = -r; x <= r; x++) sum += src[y * n + Mathf.Clamp(x, 0, n - 1)];
                for (int x = 0; x < n; x++)
                {
                    tmp[y * n + x] = sum * inv;
                    sum += src[y * n + Mathf.Min(x + r + 1, n - 1)] - src[y * n + Mathf.Max(x - r, 0)];
                }
            }
            for (int x = 0; x < n; x++)
            {
                float sum = 0f;
                for (int y = -r; y <= r; y++) sum += tmp[Mathf.Clamp(y, 0, n - 1) * n + x];
                for (int y = 0; y < n; y++)
                {
                    dst[y * n + x] = sum * inv;
                    sum += tmp[Mathf.Min(y + r + 1, n - 1) * n + x] - tmp[Mathf.Max(y - r, 0) * n + x];
                }
            }
            return dst;
        }

        Texture2D MaskTexture(float[] m, string name)
        {
            var tex = new Texture2D(MaskSize, MaskSize, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[m.Length];
            for (int i = 0; i < m.Length; i++) px[i] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(m[i]) * 255));
            tex.SetPixels32(px);
            tex.Apply();
            textures.Add(tex);
            return tex;
        }

        // A great planet hanging behind its shoulder, banded rust and violet, with a thin bright rim.
        void BuildPlanet(Vector3 eyes)
        {
            Vector3 dir = Quaternion.AngleAxis(-24f, Vector3.up) * forward;
            Vector3 at = eyes + dir * 150f + Vector3.up * 28f;
            var planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            planet.name = "Planet";
            planet.transform.SetParent(transform, false);
            planet.transform.position = at;
            planet.transform.localScale = Vector3.one * 42f;
            planet.transform.rotation = Quaternion.Euler(18f, 30f, 12f);
            Material m = Unlit(Color.white);
            const int w = 512, h = 256;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Planet", wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = y / (float)h, u = x / (float)w;
                    float band = Mathf.PerlinNoise(v * 14f, Mathf.Sin(u * Mathf.PI * 2f) * 0.6f + 3f);
                    float swirl = Mathf.PerlinNoise(u * 12f, v * 30f + band * 2f);
                    Color c = Color.Lerp(new Color(0.32f, 0.14f, 0.12f), new Color(0.55f, 0.32f, 0.2f), band);
                    c = Color.Lerp(c, new Color(0.28f, 0.16f, 0.36f), swirl * 0.5f);
                    // Night side: darker towards one edge (the light comes from the eclipse).
                    float shade = Mathf.Clamp01(0.35f + 0.75f * Mathf.Cos((u - 0.15f) * Mathf.PI * 2f));
                    px[y * w + x] = c * Mathf.Lerp(0.25f, 1f, shade);
                }
            tex.SetPixels32(px);
            tex.Apply();
            textures.Add(tex);
            m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            Setup(planet, m);
            // Its thin atmosphere, a soft bright rim.
            Material rim = Glow(RingTexture(256), new Color(0.9f, 0.6f, 1.2f, 0.5f));
            Transform halo = Quad("PlanetRim", rim, transform);
            halo.position = at;
            halo.rotation = Quaternion.LookRotation(at - eyes);
            halo.localScale = Vector3.one * 42f * 1.12f;
        }

        // ---------------------------------------------------------------- the realm round it

        static readonly Color Rock = new Color(0.025f, 0.03f, 0.055f, 1f), Haze = new Color(0.11f, 0.13f, 0.27f, 1f);
        readonly List<LineRenderer> bolts = new List<LineRenderer>();
        readonly List<float> boltUntil = new List<float>();
        float nextBolt, nextCrackle, skyFlash;
        Vector3 realmCentre;

        // Islands of rock floating in the storm, crowned with dark spires, one joined to the next by an arched bridge;
        // shafts of pale light falling on them. Further ones fade into the blue haze.
        void BuildRealm(Vector3 eyes, Quaternion facing)
        {
            realmCentre = eyes + forward * FigureDistance;
            // (angle round the player from straight ahead, distance, height, size, spires)
            float[,] islands =
            {
                { -38f, 55f, -6f, 9f, 3f }, { -62f, 95f, 14f, 13f, 5f }, { 41f, 60f, 4f, 8f, 2f }, { 70f, 120f, 22f, 16f, 6f },
                { -20f, 150f, 30f, 12f, 4f }, { 18f, 135f, -18f, 10f, 3f }, { -105f, 80f, -14f, 7f, 1f }, { 112f, 85f, 8f, 9f, 2f },
                { 160f, 110f, 12f, 11f, 3f }, { -150f, 100f, 25f, 10f, 2f }, { 0f, 240f, 55f, 22f, 7f },
            };
            var tops = new List<Vector3>();
            for (int i = 0; i < islands.GetLength(0); i++)
            {
                Vector3 dir = Quaternion.AngleAxis(islands[i, 0], Vector3.up) * forward;
                Vector3 at = eyes + dir * islands[i, 1] + Vector3.up * islands[i, 2];
                float distance = islands[i, 1];
                tops.Add(Island(at, islands[i, 3], (int)islands[i, 4], distance, i));
            }
            Bridge(tops[0], tops[1], 55f);
            Bridge(tops[2], tops[3], 60f);

            // Shafts of light.
            Material shaft = Glow(VFX.SparkHands.CreateSoftLine(64), new Color(0.45f, 0.6f, 1.4f, 0.35f));
            int[] lit = { 1, 3, 4, 10 };
            foreach (int i in lit)
            {
                Transform b = Quad("LightShaft", shaft, transform);
                b.position = tops[i] + Vector3.up * 70f;
                Vector3 away = StoryFx.Flat(b.position - eyes);
                b.rotation = Quaternion.LookRotation(away, Vector3.up);
                b.localScale = new Vector3(2.2f, 150f, 1f);
            }

            boltMaterial = Glow(VFX.SparkHands.CreateSoftLine(32), Color.white);
            nextBolt = Time.time + 2.5f;
        }

        Color Shade(float distance) => Color.Lerp(Rock, Haze, Mathf.Clamp01((distance - 30f) / 220f));

        // A jagged rock hanging in the air (flat on top, a long ragged point below), with spires on it. Returns the
        // middle of its top.
        Vector3 Island(Vector3 at, float size, int spires, float distance, int seed)
        {
            var r = new System.Random(seed * 7919 + 13);
            Material m = Unlit(Shade(distance));
            var go = new GameObject("Island" + seed);
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            go.AddComponent<MeshFilter>().sharedMesh = Spike(size, size * 1.9f, 14, 6, 0.35f, false, r);
            Setup(go, m);
            Vector3 top = at + Vector3.up * 0.02f;
            // A broken crown of rock on top, then the spires.
            for (int k = 0; k < 4; k++)
            {
                float a = (float)r.NextDouble() * Mathf.PI * 2f, d = (float)r.NextDouble() * size * 0.6f;
                SpikeAt(m, top + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d, size * 0.25f, size * (0.25f + 0.4f * (float)r.NextDouble()), true, r);
            }
            for (int k = 0; k < spires; k++)
            {
                float a = (float)r.NextDouble() * Mathf.PI * 2f, d = (float)r.NextDouble() * size * 0.55f;
                Vector3 foot = top + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d;
                float h = size * (0.6f + 1.6f * (float)r.NextDouble()) * (k == 0 ? 1.4f : 1f), w = size * (0.06f + 0.05f * (float)r.NextDouble());
                Tower(m, foot, w, h, r);
            }
            return top;
        }

        // A gothic tower: a tall shaft, a band, a needle of a roof, and a lit window or two.
        void Tower(Material m, Vector3 foot, float width, float height, System.Random r)
        {
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Setup(shaft, m);
            shaft.transform.SetParent(transform, false);
            shaft.transform.position = foot + Vector3.up * height * 0.5f;
            shaft.transform.rotation = Quaternion.Euler(0f, (float)r.NextDouble() * 90f, 0f);
            shaft.transform.localScale = new Vector3(width, height, width);
            SpikeAt(m, foot + Vector3.up * height, width * 0.75f, height * 0.45f, true, r);
            // Little pinnacles at the corners of a crown.
            for (int c = 0; c < 4; c++)
            {
                Vector3 corner = shaft.transform.rotation * new Vector3((c & 1) == 0 ? -0.5f : 0.5f, 0f, (c & 2) == 0 ? -0.5f : 0.5f) * width;
                SpikeAt(m, foot + Vector3.up * height + corner, width * 0.18f, height * 0.18f, true, r);
            }
            if (r.NextDouble() < 0.7)
            {
                Material lamp = Glow(Dot(), r.NextDouble() < 0.5 ? new Color(0.6f, 0.75f, 1.6f, 1f) : new Color(1.6f, 0.9f, 0.4f, 1f));
                Transform window = Quad("Window", lamp, transform);
                window.position = foot + Vector3.up * height * (0.55f + 0.3f * (float)r.NextDouble());
                window.rotation = Quaternion.LookRotation(window.position - head.position);
                window.localScale = Vector3.one * width * 0.9f;
            }
        }

        void SpikeAt(Material m, Vector3 at, float radius, float height, bool up, System.Random r)
        {
            var go = new GameObject("Spike");
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            go.AddComponent<MeshFilter>().sharedMesh = Spike(radius, height, 8, 2, 0.15f, up, r);
            Setup(go, m);
        }

        // A bridge on arches from one island to the next, sagging a little.
        void Bridge(Vector3 a, Vector3 b, float distance)
        {
            Material m = Unlit(Shade(distance));
            Vector3 span = b - a;
            int n = Mathf.Max(4, Mathf.RoundToInt(span.magnitude / 4f));
            Quaternion along = Quaternion.LookRotation(span.normalized, Vector3.up);
            for (int i = 0; i < n; i++)
            {
                float u = (i + 0.5f) / n;
                Vector3 p = Vector3.Lerp(a, b, u) + Vector3.down * Mathf.Sin(u * Mathf.PI) * span.magnitude * 0.04f + Vector3.up * 1.5f;
                var deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Setup(deck, m);
                deck.transform.SetParent(transform, false);
                deck.transform.SetPositionAndRotation(p, along);
                deck.transform.localScale = new Vector3(1.6f, 0.45f, span.magnitude / n + 0.05f);
                // A pier and an arch under every other span.
                if (i % 2 == 0)
                {
                    var pier = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Setup(pier, m);
                    pier.transform.SetParent(transform, false);
                    pier.transform.SetPositionAndRotation(p + Vector3.down * 3.2f, along);
                    pier.transform.localScale = new Vector3(1.2f, 6f, 0.9f);
                    SpikeAt(m, p + Vector3.down * 6.2f, 0.7f, 3.5f, false, new System.Random(i));
                }
            }
        }

        // A ragged cone: `up` = a point at the top (a spire), otherwise flat on top and pointing down (a floating rock).
        Mesh Spike(float radius, float height, int sides, int rings, float jag, bool up, System.Random r)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int k = 0; k <= rings; k++)
            {
                float t = (float)k / rings; // 0 at the wide end
                float rad = radius * (1f - t) * (1f - t * 0.15f);
                float y = (up ? 1f : -1f) * height * t;
                for (int i = 0; i < sides; i++)
                {
                    float a = (i + (float)r.NextDouble() * 0.3f) / sides * Mathf.PI * 2f;
                    float rr = rad * (1f + jag * ((float)r.NextDouble() - 0.5f) * 2f);
                    verts.Add(new Vector3(Mathf.Cos(a) * rr, y + (k > 0 && k < rings ? ((float)r.NextDouble() - 0.5f) * height * 0.08f : 0f), Mathf.Sin(a) * rr));
                }
            }
            int tip = verts.Count;
            verts.Add(new Vector3(0f, (up ? 1f : -1f) * height * 1.05f, 0f));
            int cap = verts.Count;
            verts.Add(Vector3.zero);
            for (int k = 0; k < rings; k++)
                for (int i = 0; i < sides; i++)
                {
                    int a = k * sides + i, b = k * sides + (i + 1) % sides, c = a + sides, d = b + sides;
                    if (k == rings - 1) { c = tip; d = tip; }
                    tris.AddRange(new[] { a, c, b, b, c, d });
                }
            for (int i = 0; i < sides; i++) tris.AddRange(new[] { cap, i, (i + 1) % sides });
            var mesh = new Mesh { name = "Spike" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Lightning: now and then a bolt forks down through the far storm and the sky flashes; while the Witness
        // speaks, threads of gold crackle about it.
        void Storm(float time, float dt)
        {
            for (int i = bolts.Count - 1; i >= 0; i--)
            {
                if (time < boltUntil[i])
                {
                    float left = (boltUntil[i] - time) / 0.25f;
                    Color c = bolts[i].startColor;
                    c.a = Mathf.Clamp01(left) * (0.6f + 0.4f * V);
                    bolts[i].startColor = bolts[i].endColor = c;
                    continue;
                }
                Destroy(bolts[i].gameObject);
                bolts.RemoveAt(i);
                boltUntil.RemoveAt(i);
            }
            if (time >= nextBolt)
            {
                nextBolt = time + R(2.5f, 6f);
                float a = R(-80f, 80f) * Mathf.Deg2Rad;
                Vector3 dir = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * forward;
                Vector3 from = head.position + dir * R(90f, 180f) + Vector3.up * R(60f, 90f);
                Bolt(from, Vector3.down, R(60f, 110f), 4.5f, new Color(1.6f, 1.9f, 3f, 1f), 0.25f, 3);
                skyFlash = 1f;
            }
            if (false && time >= nextCrackle)
            {
                nextCrackle = time + R(0.25f, 0.8f);
                Vector3 p = SurfacePoint();
                Bolt(p, Random3(), R(1.5f, 3.5f), 0.6f, new Color(2.4f, 1.5f, 0.45f, 1f), 0.2f, 1);
            }
            skyFlash = Mathf.Max(0f, skyFlash - dt * 4f);
            float f = 1f + skyFlash * (0.6f + 0.4f * V);
            skyMaterial.SetColor("_BaseColor", new Color(f, f, f * 1.05f, 1f));
        }

        static Vector3 Random3() => new Vector3(R(-1f, 1f), R(-0.3f, 1f), R(-1f, 1f)).normalized;

        void Bolt(Vector3 from, Vector3 dir, float length, float width, Color color, float life, int forks)
        {
            const int segments = 14;
            var go = new GameObject("Bolt");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = boltMaterial;
            line.positionCount = segments + 1;
            line.widthCurve = AnimationCurve.Linear(0f, width * 0.06f, 1f, width * 0.015f);
            line.startColor = line.endColor = color;
            line.shadowCastingMode = ShadowCastingMode.Off;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-3f) side = Vector3.right;
            side.Normalize();
            Vector3 other = Vector3.Cross(dir, side).normalized;
            Vector3 p = from;
            float step = length / segments;
            for (int i = 0; i <= segments; i++)
            {
                line.SetPosition(i, p);
                if (forks > 0 && i > 3 && i < segments - 2 && V < 0.18f)
                    Bolt(p, (dir + side * R(-0.9f, 0.9f)).normalized, length * R(0.2f, 0.4f), width * 0.6f, color, life, forks - 1);
                p += dir * step + (side * R(-1f, 1f) + other * R(-1f, 1f)) * step * 0.45f;
            }
            bolts.Add(line);
            boltUntil.Add(Time.time + life);
        }

        // A thin blazing ring (the eclipse's edge), soft outside.
        Texture2D RingTexture(int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "EclipseRing", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float r = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
                    float d = r - 0.89f;
                    float a = d < 0f ? Mathf.Exp(-d * d / 0.0004f) : Mathf.Exp(-d / 0.035f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            textures.Add(tex);
            return tex;
        }

        // ---------------------------------------------------------------- textures and sound, made here

        Texture2D Dot()
        {
            Texture2D t = VFX.SparkHands.CreateSoftDot(128);
            textures.Add(t);
            return t;
        }

        // A bright disc with a soft edge.
        Texture2D Disc()
        {
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "SunDisc", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float r = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
                    float a = Mathf.Clamp01((1f - r) / 0.12f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            textures.Add(tex);
            return tex;
        }

        // Long soft rays out from the centre.
        Texture2D RaysTexture(int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "SunRays", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            var rays = new float[64];
            for (int i = 0; i < rays.Length; i++) rays[i] = 0.25f + 0.75f * V * V;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    Vector2 p = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f) / (n / 2f);
                    float r = p.magnitude;
                    float a = (Mathf.Atan2(p.y, p.x) / (Mathf.PI * 2f) + 0.5f) * rays.Length;
                    int i0 = Mathf.FloorToInt(a) % rays.Length, i1 = (i0 + 1) % rays.Length;
                    float length = Mathf.Lerp(rays[i0], rays[i1], Mathf.SmoothStep(0f, 1f, a - Mathf.Floor(a)));
                    float streak = Mathf.Pow(Mathf.Abs(Mathf.Sin(a * Mathf.PI)), 6f);
                    float v = Mathf.Clamp01(1f - r / Mathf.Max(0.05f, length)) * streak * Mathf.Clamp01(r * 4f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(v) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            textures.Add(tex);
            return tex;
        }

        // Deep space: a scatter of stars (a few bright ones), and faint violet and blue clouds of nebula.
        Texture2D StarTexture(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "PrologueStars", wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[w * h];
            float ox = R(0f, 100f), oy = R(0f, 100f);
            for (int y = 0; y < h; y++)
            {
                float lat = (y / (float)h - 0.5f) * Mathf.PI;
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (float)h;
                    // (noise sampled round the sphere so the seam doesn't show)
                    float cx = Mathf.Cos(u * Mathf.PI * 2f) * Mathf.Cos(lat), cz = Mathf.Sin(u * Mathf.PI * 2f) * Mathf.Cos(lat), cy = Mathf.Sin(lat);
                    float n1 = Mathf.PerlinNoise(ox + cx * 1.6f + cy * 0.7f, oy + cz * 1.6f + cy);
                    float n2 = Mathf.PerlinNoise(ox * 2f + cx * 4f + cy * 3f, oy + cz * 4f - cy * 2f);
                    float band = Mathf.Exp(-Mathf.Pow((cy - 0.25f * cx) / 0.35f, 2f)); // a milky band across the sky
                    float cloud = Mathf.Clamp01(n1 * n2 * 1.8f - 0.25f) * (0.35f + band);
                    // Storm clouds of deep blue and violet, lit pale at their edges (like moonlit thunderheads).
                    float n3 = Mathf.PerlinNoise(ox * 3f + cx * 9f + cy * 5f, oy * 2f + cz * 9f - cy * 4f);
                    float storm = Mathf.Clamp01(n1 * 1.6f - 0.35f + (n3 - 0.5f) * 0.4f) * (0.45f + band);
                    float edge = Mathf.Clamp01(1f - Mathf.Abs(storm - 0.45f) * 4f) * n3;
                    Color c = new Color(0.008f, 0.01f, 0.028f) +
                              new Color(0.08f, 0.1f, 0.26f) * storm +
                              new Color(0.16f, 0.07f, 0.28f) * cloud * cloud * 0.6f +
                              new Color(0.35f, 0.42f, 0.7f) * edge * storm * 0.55f;
                    px[y * w + x] = c;
                }
            }
            // The stars.
            int count = w * h / 220;
            for (int i = 0; i < count; i++)
            {
                int x = rng.Next(w), y = rng.Next(h);
                float b = Mathf.Pow(V, 6f);
                Color tint = V < 0.3f ? new Color(1f, 0.85f, 0.7f) : V < 0.5f ? new Color(0.75f, 0.85f, 1f) : Color.white;
                Plot(px, w, h, x, y, tint * (0.25f + b * 1.5f));
                if (b > 0.35f)
                {
                    // A bright one: a soft cross.
                    for (int d = 1; d <= 2; d++)
                    {
                        Color f = tint * (b * 0.6f / d);
                        Plot(px, w, h, x + d, y, f);
                        Plot(px, w, h, x - d, y, f);
                        Plot(px, w, h, x, y + d, f);
                        Plot(px, w, h, x, y - d, f);
                    }
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            textures.Add(tex);
            return tex;
        }

        static void Plot(Color32[] px, int w, int h, int x, int y, Color c)
        {
            if (y < 0 || y >= h) return;
            x = (x % w + w) % w;
            Color old = px[y * w + x];
            px[y * w + x] = new Color(Mathf.Min(1f, old.r + c.r), Mathf.Min(1f, old.g + c.g), Mathf.Min(1f, old.b + c.b), 1f);
        }

        // The voice of the Witness: a low, slow, many-voiced hum (looped; its volume follows the words).
        static AudioClip Drone()
        {
            const int rate = 44100;
            const float seconds = 6f;
            var data = new float[(int)(rate * seconds)];
            float[] tones = { 73.4f, 110f, 146.8f, 220f, 293.7f };
            float[] gains = { 0.5f, 0.35f, 0.25f, 0.12f, 0.06f };
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / rate, s = 0f;
                for (int k = 0; k < tones.Length; k++)
                {
                    // Whole cycles per loop, so it loops without a click.
                    float f = Mathf.Round(tones[k] * seconds) / seconds;
                    float vib = 0.6f * Mathf.Sin(2f * Mathf.PI * (1f / seconds) * (k + 1) * t);
                    s += Mathf.Sin(2f * Mathf.PI * f * t + vib) * gains[k];
                }
                data[i] = s * 0.45f;
            }
            AudioClip clip = AudioClip.Create("Witness voice", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void OnDestroy()
        {
            foreach (Material m in materials)
                if (m != null) Destroy(m);
            foreach (Texture2D t in textures)
                if (t != null) Destroy(t);
        }
    }
}
