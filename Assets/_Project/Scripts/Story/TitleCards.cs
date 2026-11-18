using System.Collections;
using System.Collections.Generic;
using ArcaneStrike.HandInput;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Title cards in the dark: the game's opening (what Arcane Strike is), the chapter titles, and the "to be
    /// continued" at the end of what's built. Made to feel like something in the room with you, not words on a screen:
    /// - the card hangs in space a couple of metres away and drifts after the head lazily (not glued to the eyes),
    ///   swaying gently, so the stereo depth and parallax show;
    /// - the title letters are solid: gold faces over a bronze body several millimetres deep;
    /// - embers drift up all round the player at every depth, and a burst of sparks lights each title as it arrives;
    /// - a warm glow behind the words, and the Jyoti Seal's flame crest turning slowly behind the titles;
    /// - each card comes forward out of the dark as it fades in, with a soft arcane ring.
    /// N / E (or B / Y on a controller) moves on to the next card. In front of whatever camera is drawing (the headset,
    /// or the demo video's <see cref="Demo.CinematicCamera"/>), drawn over the screen fade.
    /// </summary>
    public class TitleCards : MonoBehaviour
    {
        public struct Card
        {
            public string title, body;
            public float hold;
            public Card(string title, string body, float hold = 3.2f)
            {
                this.title = title;
                this.body = body;
                this.hold = hold;
            }
        }

        static readonly Color Gold = new Color(1f, 0.8f, 0.42f, 1f), Pale = new Color(0.95f, 0.93f, 0.9f, 1f);
        static readonly Color Bronze = new Color(0.55f, 0.28f, 0.08f, 1f), Shade = new Color(0.25f, 0.2f, 0.18f, 1f);
        const float Distance = 2.3f, ComeFrom = 0.6f; // where the card settles, and how much further away it starts
        const int TitleDepth = 9, BodyDepth = 3;      // layers of the solid letters
        const float LayerStep = 0.0035f;              // (m between layers)
        // Queues: everything over the screen fade (4995), the words last. URP draws transparent queues up to 5000.
        const int GlowQueue = 4996, EmberQueue = 4997, TextQueue = 5000;

        public static bool Showing { get; private set; }

        // Their own random numbers (embers come and go with the frame rate): the game's, which a demo replay
        // re-seeds to repeat the take, are left alone.
        static readonly System.Random rng = new System.Random();
        static float V => (float)rng.NextDouble();
        static float R(float min, float max) => min + (max - min) * V;

        readonly List<TextMesh> titleLayers = new List<TextMesh>(), bodyLayers = new List<TextMesh>();
        readonly List<Material> materials = new List<Material>();
        Transform card, crest;
        Renderer halo, crestRenderer;
        ParticleSystem embers;
        AudioSource sound;
        AudioClip ring;
        Vector3 anchor;
        float alpha, push, crestAlpha, emberRate = 1f;
        bool next, placed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Showing = false;

        // ---------------------------------------------------------------- the cards in the game

        /// <summary>The game's title, before the prologue in the stars (<see cref="CosmicPrologue"/>).</summary>
        public static readonly Card[] GameTitle =
        {
            new Card("ARCANE STRIKE", "a hands-first VR adventure", 3.4f),
        };

        /// <summary>After the prologue, before the walk up to the old house.</summary>
        public static readonly Card[] ChapterOne =
        {
            new Card("CHAPTER 1", "THE LANTERN", 3.2f),
        };

        public static readonly Card[] ChapterTwo =
        {
            new Card("CHAPTER 2", "FIRST IGNITION", 3.2f),
        };

        public static readonly Card[] ToBeContinued =
        {
            new Card("TO BE CONTINUED…", "Chapter 2 continues: through the rift, into the Breach.", 3.8f),
            new Card("ARCANE STRIKE", "Thank you for playing.", 4.2f),
        };

        // ---------------------------------------------------------------- showing them

        /// <summary>Shows the cards one after another in the dark (run it as a coroutine). The screen stays black after;
        /// the embers fade out on their own.</summary>
        public static IEnumerator Show(Card[] cards)
        {
            var go = new GameObject("TitleCards");
            var tc = go.AddComponent<TitleCards>();
            Showing = true;
            ScreenFade.Set(Color.black); // (the demo video's cameras mirror this one)
            for (float t = 0f; t < 0.6f; t += Time.deltaTime) yield return null; // the embers rise first
            foreach (Card c in cards)
            {
                Debug.Log($"[Title] {c.title} {c.body.Replace('\n', ' ')}");
                yield return tc.Play(c);
            }
            Showing = false;
            tc.Release();
        }

        void Awake()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Shader overlayShader = font.material.shader; // GUI/Text: vertex colour × texture alpha, no lighting

            card = new GameObject("Card").transform;
            card.SetParent(transform, false);

            Material textMaterial = Overlay(new Material(font.material) { name = "TitleCardText" }, TextQueue);
            for (int i = 0; i < TitleDepth; i++) titleLayers.Add(Text("Title" + i, font, textMaterial, 0.017f, FontStyle.Bold, i));
            for (int i = 0; i < BodyDepth; i++) bodyLayers.Add(Text("Body" + i, font, textMaterial, 0.0085f, FontStyle.Normal, i));

            // The warm glow behind the words, and the flame crest behind the titles.
            Texture2D dot = VFX.SparkHands.CreateSoftDot(64);
            halo = Quad("Glow", Overlay(new Material(overlayShader) { name = "TitleGlow", mainTexture = dot }, GlowQueue), new Vector3(0f, 0f, 0.12f));
            crestRenderer = Quad("Crest", Overlay(new Material(overlayShader) { name = "TitleCrest", mainTexture = JyotiSealBrand.CrestAlpha(256) }, GlowQueue), new Vector3(0f, 0f, 0.08f));
            crest = crestRenderer.transform;
            crest.localScale = Vector3.one * 1.05f;

            // Embers all round, at every depth.
            embers = StoryFx.CreateParticles(transform, "TitleEmbers", true, -0.02f, 900);
            embers.GetComponent<ParticleSystemRenderer>().sharedMaterial =
                Overlay(new Material(overlayShader) { name = "TitleEmbers", mainTexture = dot }, EmberQueue);

            sound = gameObject.AddComponent<AudioSource>();
            sound.playOnAwake = false;
            sound.spatialBlend = 0f;
            ring = Chapter2Sounds.ArcaneRing();
            SetAlpha(0f);
        }

        Material Overlay(Material m, int queue)
        {
            m.renderQueue = queue;
            m.SetFloat("unity_GUIZTestMode", (float)CompareFunction.Always); // (the room can't hide it)
            materials.Add(m);
            return m;
        }

        TextMesh Text(string name, Font font, Material material, float size, FontStyle style, int layer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(card, false);
            go.transform.localPosition = new Vector3(0f, 0f, layer * LayerStep);
            var t = go.AddComponent<TextMesh>();
            t.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            t.anchor = TextAnchor.MiddleCenter;
            t.alignment = TextAlignment.Center;
            t.fontSize = 128; // (crisper letters up close in a headset)
            t.characterSize = size;
            t.fontStyle = style;
            t.lineSpacing = 1.15f;
            t.text = "";
            return t;
        }

        Renderer Quad(string name, Material material, Vector3 at)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(card, false);
            q.transform.localPosition = at;
            var r = q.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        IEnumerator Play(Card c)
        {
            next = false;
            bool hasTitle = !string.IsNullOrEmpty(c.title), hasBody = !string.IsNullOrEmpty(c.body);
            foreach (TextMesh t in titleLayers) t.text = c.title ?? "";
            foreach (TextMesh t in bodyLayers) t.text = c.body ?? "";
            // A card with only a title, or only a body, sits in the middle.
            float titleY = hasTitle && hasBody ? 0.1f : 0f, bodyY = hasTitle && hasBody ? -0.075f : 0f;
            foreach (TextMesh t in titleLayers) t.transform.localPosition = new Vector3(0f, titleY, t.transform.localPosition.z);
            foreach (TextMesh t in bodyLayers) t.transform.localPosition = new Vector3(0f, bodyY, t.transform.localPosition.z);
            // The body text is bigger when it's the whole card.
            foreach (TextMesh t in bodyLayers) t.characterSize = hasTitle ? 0.0085f : 0.0105f;
            halo.transform.localPosition = new Vector3(0f, hasTitle ? titleY : 0f, 0.12f);
            halo.transform.localScale = hasTitle ? new Vector3(2.4f, 0.75f, 1f) : new Vector3(2.6f, 1.1f, 1f);
            crest.localPosition = new Vector3(0f, titleY, 0.08f);
            crestAlpha = hasTitle ? 1f : 0f;
            placed = false; // each card arrives straight ahead

            if (hasTitle)
            {
                sound.PlayOneShot(ring, 0.35f);
                StartCoroutine(TitleSparks(titleY));
            }
            // Out of the dark: it comes forward as it fades in.
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / 1.2f);
                SetAlpha(u);
                push = (1f - u) * ComeFrom;
                if (next) break;
                yield return null;
            }
            SetAlpha(1f);
            push = 0f;
            for (float t = 0f; t < c.hold && !next; t += Time.deltaTime)
            {
                push = -0.04f * (t / c.hold); // (still drifting very slightly closer)
                yield return null;
            }
            float from = alpha, back = push;
            float seconds = next ? 0.35f : 0.9f;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / seconds);
                SetAlpha(from * (1f - u));
                push = back + u * 0.25f; // and recedes as it goes
                yield return null;
            }
            SetAlpha(0f);
            for (float t = 0f; t < 0.25f; t += Time.deltaTime) yield return null;
        }

        // Sparks fly off the title as it lights.
        IEnumerator TitleSparks(float y)
        {
            for (float t = 0f; t < 0.8f; t += Time.deltaTime)
            {
                for (int i = 0; i < 4; i++)
                {
                    Vector3 p = card.TransformPoint(new Vector3(R(-0.55f, 0.55f), y + R(-0.05f, 0.05f), -0.01f));
                    Vector3 v = card.TransformDirection(new Vector3(R(-0.15f, 0.15f), R(0.05f, 0.35f), R(-0.25f, 0.05f)));
                    StoryFx.Emit(embers, p, v, R(0.006f, 0.016f), R(0.6f, 1.4f),
                                 V < 0.5f ? new Color(1f, 0.85f, 0.45f, 1f) : new Color(1f, 0.45f, 0.1f, 1f));
                }
                yield return null;
            }
        }

        void SetAlpha(float a)
        {
            alpha = a;
            for (int i = 0; i < titleLayers.Count; i++)
            {
                // Gold face in front; the body behind it darkens into bronze.
                float k = (float)i / (TitleDepth - 1);
                Color c = i == 0 ? Gold : Color.Lerp(Color.Lerp(Gold, Bronze, 0.55f), Bronze * 0.45f, k);
                c.a = a;
                titleLayers[i].color = c;
            }
            for (int i = 0; i < bodyLayers.Count; i++)
            {
                Color c = i == 0 ? Pale : Shade;
                c.a = a;
                bodyLayers[i].color = c;
            }
        }

        void Update()
        {
            if (GameInput.Pressed(GameAction.Skip) || GameInput.Pressed(GameAction.Interact)) next = true;
            if (Showing) ScreenFade.Set(Color.black); // (keeps black under the cards even if a scene's own fade starts)
        }

        // In the room in front of the camera that is drawing (the headset / main camera, or the demo video's own
        // camera), following it lazily, swaying a little.
        void LateUpdate()
        {
            Camera cam = Viewer();
            if (cam == null) return;
            Transform v = cam.transform;
            Vector3 flat = StoryFx.Flat(v.forward);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            Vector3 want = v.position + flat * Distance;
            float k = placed ? 1f - Mathf.Exp(-1.6f * Time.deltaTime) : 1f;
            anchor = Vector3.Lerp(anchor, want, k);
            placed = true;

            Vector3 away = anchor - v.position;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = flat;
            Quaternion facing = Quaternion.LookRotation(away.normalized);
            float time = Time.time;
            Quaternion sway = Quaternion.Euler(Mathf.Sin(time * 0.37f) * 1.5f, Mathf.Sin(time * 0.29f) * 3.5f, Mathf.Sin(time * 0.23f) * 0.6f);
            card.SetPositionAndRotation(anchor + away.normalized * push + Vector3.up * Mathf.Sin(time * 0.5f) * 0.01f, facing * sway);

            // The glow breathes; the crest turns slowly and fades with the card.
            float breathe = 0.85f + 0.15f * Mathf.Sin(time * 1.3f);
            halo.sharedMaterial.color = new Color(1f, 0.5f, 0.15f, 0.22f * alpha * breathe);
            crest.localRotation = Quaternion.Euler(0f, 0f, time * 6f);
            crestRenderer.sharedMaterial.color = new Color(1f, 0.62f, 0.2f, 0.16f * alpha * crestAlpha * breathe);

            // Embers drift up all round the player: near ones pass by, far ones twinkle.
            if (emberRate > 0f)
            {
                int count = V < 22f * Time.deltaTime * emberRate ? 1 + (V < 0.4f ? 1 : 0) : 0;
                for (int i = 0; i < count; i++)
                {
                    float depth = R(0.9f, 5f);
                    Vector3 p = v.position + facing * new Vector3(R(-1.2f, 1.2f) * depth, R(-1.6f, 0.4f), depth);
                    Vector3 vel = new Vector3(R(-0.04f, 0.04f), R(0.06f, 0.2f), R(-0.04f, 0.04f));
                    float size = R(0.005f, 0.012f) * Mathf.Sqrt(depth);
                    Color c = V < 0.6f ? new Color(1f, 0.55f, 0.15f, 1f) : new Color(1f, 0.82f, 0.45f, 1f);
                    StoryFx.Emit(embers, p, vel, size, R(3f, 6f), c);
                }
            }
        }

        static Camera Viewer()
        {
            Camera best = null;
            foreach (Camera c in Camera.allCameras)
            {
                if (!c.enabled || c.cullingMask == 0 || c.targetTexture != null) continue;
                if (best == null || c.depth > best.depth) best = c;
            }
            return best != null ? best : Camera.main;
        }

        // The cards are done: the embers already in the air drift on and fade, then it all goes.
        void Release()
        {
            emberRate = 0f;
            card.gameObject.SetActive(false);
            // The ones left drift into the scene as it fades in: behind its walls now, not over them.
            embers.GetComponent<ParticleSystemRenderer>().sharedMaterial.SetFloat("unity_GUIZTestMode", (float)CompareFunction.LessEqual);
            Destroy(gameObject, 6.5f);
        }

        void OnDestroy()
        {
            foreach (Material m in materials)
                if (m != null) Destroy(m);
        }
    }
}
