using System.Collections;
using ArcaneStrike.VFX;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// A full-view colour wash in front of the player's eyes: fade to gold, white out, fade back in from white.
    /// One quad just in front of the main camera, drawn over everything. Works in a headset and on screen.
    /// Fades ease in and out (no hard starts or stops). <see cref="LoadScene"/> changes scene behind the wash: it
    /// loads in the background (no frozen frame), keeps the colour over the new scene's first frames, and fades it
    /// away unless the new scene runs its own fade-in. Fades run in real time (a slow-motion moment doesn't stretch
    /// them, nor freeze them at timeScale 0).
    /// </summary>
    public static class ScreenFade
    {
        static Renderer quad;
        static Material material;
        static Color current = new Color(0f, 0f, 0f, 0f);
        static int sets, fadeFrame = -10;
        static bool loading;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static Color Current => current;
        /// <summary>A fade (<see cref="To"/>, a scene change) is running: brief flashes (a hurt flash) should stay off it.</summary>
        public static bool Busy => loading || Time.frameCount - fadeFrame <= 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            quad = null;
            material = null;
            current = new Color(0f, 0f, 0f, 0f);
            sets = 0;
            fadeFrame = -10;
            loading = false;
        }

        public static void Set(Color color)
        {
            sets++;
            current = color;
            Ensure();
            if (material == null || quad == null) return; // (no camera yet: shown as soon as there is one)
            material.SetColor(BaseColorId, color);
            quad.enabled = color.a > 0.001f;
        }

        /// <summary>Fades to `color` (alpha included) over `seconds`, easing in and out. Run it as a coroutine.</summary>
        public static IEnumerator To(Color color, float seconds)
        {
            Color from = current;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                fadeFrame = Time.frameCount;
                Set(Color.Lerp(from, color, Mathf.SmoothStep(0f, 1f, t / Mathf.Max(seconds, 1e-3f))));
                yield return null;
            }
            Set(color);
        }

        /// <summary>A flash: up to `color` quickly, then eases back to what was showing before.</summary>
        public static IEnumerator Pulse(Color color, float rise, float fall)
        {
            Color rest = current;
            yield return To(color, rise);
            yield return To(rest, fall);
        }

        /// <summary>Fades to `color`, loads `scene` in the background, and carries the colour into it; then fades it
        /// away over `fadeIn` seconds (if the new scene hasn't started its own fade by then).</summary>
        public static void LoadScene(string scene, Color color, float fadeOut = 0.8f, float fadeIn = 1.5f)
        {
            if (loading)
            {
                Debug.LogWarning($"[Fade] Already changing scene: '{scene}' ignored.");
                return; // (two triggers in one moment would load twice)
            }
            Runner.Get().StartCoroutine(Load(scene, color, fadeOut, fadeIn));
        }

        static IEnumerator Load(string scene, Color color, float fadeOut, float fadeIn)
        {
            loading = true;
            yield return To(color, fadeOut);
            AsyncOperation op = SceneManager.LoadSceneAsync(scene);
            if (op == null)
            {
                loading = false;
                Debug.LogError($"[Fade] Scene '{scene}' could not be loaded (is it in the Build Profile's scene list?): fading back.");
                yield return To(new Color(color.r, color.g, color.b, 0f), fadeIn);
                yield break;
            }
            op.allowSceneActivation = false;
            while (op.progress < 0.9f)
            {
                Set(color); // the camera may change; keep the wash up
                yield return null;
            }
            yield return null; // one more covered frame before the switch
            SceneManager.sceneLoaded += Cover;
            op.allowSceneActivation = true;
            while (!op.isDone) yield return null;
            loading = false;
            if (Time.timeScale < 0.01f) Time.timeScale = 1f; // (a new scene never starts frozen)

            // The new scene: hold the colour a moment while it settles (its first frames hitch), then fade out,
            // unless it is already fading itself.
            int mine = sets;
            for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
            {
                if (sets != mine) yield break; // the scene took over
                Set(color);
                mine = sets;
                yield return null;
            }
            yield return To(new Color(color.r, color.g, color.b, 0f), fadeIn);

            void Cover(Scene s, LoadSceneMode mode)
            {
                SceneManager.sceneLoaded -= Cover;
                Set(color); // before the new scene's first frame is drawn
            }
        }

        static void Ensure()
        {
            Camera cam = Camera.main;
            if (quad != null)
            {
                if (cam == null || quad.transform.parent == cam.transform) return;
                Object.Destroy(quad.gameObject); // the view moved to another camera (the rig woke up late): follow it
                quad = null;
            }
            if (cam == null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "ScreenFade";
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, Mathf.Max(cam.nearClipPlane * 1.5f, 0.12f));
            go.transform.localScale = new Vector3(2f, 2f, 1f);
            if (material == null)
            {
                material = AegisShield.CreateFaceMaterial(current);
                material.renderQueue = 5000; // over everything
            }
            material.SetColor(BaseColorId, current);
            quad = go.GetComponent<Renderer>();
            quad.sharedMaterial = material;
            quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quad.enabled = current.a > 0.001f;
        }

        // Runs the scene change: it has to outlive the scene it starts in.
        class Runner : MonoBehaviour
        {
            static Runner instance;

            public static Runner Get()
            {
                if (instance != null) return instance;
                var go = new GameObject("ScreenFadeRunner");
                Object.DontDestroyOnLoad(go);
                return instance = go.AddComponent<Runner>();
            }
        }
    }
}
