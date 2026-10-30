using ArcaneStrike.VFX;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// A soft dark ring round the edge of the view while the story moves the player (pulled into the rift, lifted by
    /// the light, the soul drifting out of the body). The edges are where the eyes feel motion the body doesn't, so
    /// narrowing the view then keeps a headset comfortable. <see cref="Set"/> takes 0 (off) .. 1 (strongest); it eases
    /// towards it, so callers can set it in steps. Works on a screen too (barely noticeable there).
    /// <see cref="Moving"/> is the player's own movement (smooth walking, snap turns, a lunge), kept apart from the
    /// story's setting so the two never switch each other off. A new scene starts clear: whatever the last one left on
    /// (a coroutine cut short by the scene change) eases away.
    /// </summary>
    public static class ComfortVignette
    {
        static Renderer quad;
        static Material material;
        static float target, current, tintTarget, tintCurrent, motion, motionUntil;
        static Color tint;
        static Runner runner;
        static int setFrame = -1, loadedFrame = -1;

        public static void Set(float amount)
        {
            target = Mathf.Clamp01(amount);
            setFrame = Time.frameCount;
            Ensure();
        }

        /// <summary>The player is moving through the world without moving their body: call it every frame while they
        /// do (`hold`: keep it a moment after, for a one-off like a snap turn). Eases in quickly, out gently.</summary>
        public static void Moving(float amount, float hold = 0f)
        {
            amount = Mathf.Clamp01(amount);
            if (Time.unscaledTime > motionUntil) motion = 0f;
            motion = Mathf.Max(motion, amount);
            motionUntil = Mathf.Max(motionUntil, Time.unscaledTime + Mathf.Max(hold, 0.05f));
            if (amount > 0f) Ensure();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            quad = null;
            material = null;
            target = current = tintTarget = tintCurrent = motion = motionUntil = 0f;
            setFrame = loadedFrame = -1;
            SceneManager.sceneLoaded -= Loaded;
            SceneManager.sceneLoaded += Loaded;
        }

        static void Loaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single) loadedFrame = Time.frameCount;
        }

        /// <summary>A coloured glow round the edge instead of the dark (the lava's heat): 0 (off) .. 1.</summary>
        public static void Tint(Color color, float amount)
        {
            tint = color;
            tintTarget = Mathf.Clamp01(amount);
            setFrame = Time.frameCount;
            if (tintTarget > 0f) Ensure();
        }

        static void Ensure()
        {
            if (runner == null)
            {
                runner = new GameObject("ComfortVignetteRunner").AddComponent<Runner>();
                Object.DontDestroyOnLoad(runner.gameObject);
            }
            Camera cam = Camera.main;
            if (quad != null)
            {
                if (cam != null && quad.transform.parent != cam.transform) Object.Destroy(quad.gameObject); // the view moved to another camera
                else return;
            }
            if (cam == null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "ComfortVignette";
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, Mathf.Max(cam.nearClipPlane * 1.6f, 0.13f));
            go.transform.localScale = new Vector3(0.62f, 0.62f, 1f); // about the headset's field of view at that distance
            if (material == null)
            {
                material = AegisShield.CreateFaceMaterial(new Color(0f, 0f, 0f, 0f));
                material.name = "ComfortVignette";
                material.SetTexture("_BaseMap", Ring(128));
                material.renderQueue = 4990; // over the world, under ScreenFade
            }
            quad = go.GetComponent<Renderer>();
            quad.sharedMaterial = material;
            quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quad.enabled = false;
        }

        static void Tick()
        {
            if (loadedFrame > setFrame && loadedFrame >= 0)
            {
                // A new scene, and nothing in it has asked for the vignette: let go of what the last one left on.
                target = tintTarget = 0f;
                setFrame = loadedFrame;
            }
            if (Time.unscaledTime > motionUntil) motion = 0f;
            if (quad == null)
            {
                if (target <= 0f && current <= 0f && tintTarget <= 0f && tintCurrent <= 0f && motion <= 0f) return;
                Ensure(); // a new scene's camera
                if (quad == null) return;
            }
            float want = Mathf.Max(target, motion);
            current = Mathf.MoveTowards(current, want, Time.unscaledDeltaTime * (want > current && motion >= want ? 4f : 1.5f));
            tintCurrent = Mathf.MoveTowards(tintCurrent, tintTarget, Time.unscaledDeltaTime * 3f);
            float dark = current * 0.92f, glow = tintCurrent * 0.6f;
            Color c = tint * (glow / (glow + dark + 1e-4f));
            material.SetColor("_BaseColor", new Color(c.r, c.g, c.b, Mathf.Max(dark, glow)));
            quad.enabled = dark > 0.001f || glow > 0.001f;
        }

        // Clear in the middle, darkening smoothly towards the edge (alpha only; the colour is the material's).
        static Texture2D Ring(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "VignetteRing" };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.38f, 0.85f, r));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        class Runner : MonoBehaviour
        {
            void LateUpdate() => Tick();
        }
    }
}
