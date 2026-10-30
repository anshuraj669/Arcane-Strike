using System.Collections;
using ArcaneStrike.HandInput;
using ArcaneStrike.Story;
using UnityEngine;

namespace ArcaneStrike.Combat
{
    /// <summary>
    /// Shared effects for the Rift's fights: one spark system (glowing, additive) and one smoke system (dark, alpha
    /// blended) for every sword and shadow, floating words for what just happened ("THRUST", "PARRY!", "COMBO x3"),
    /// and controller rumble on Quest (hand tracking has none, so it is skipped there).
    /// </summary>
    public static class CombatFx
    {
        static ParticleSystem sparks, smoke;
        static Runner runner;

        public static ParticleSystem Sparks
        {
            get
            {
                if (sparks == null) sparks = StoryFx.CreateParticles(Host, "CombatSparks", true, 0.4f, 1500);
                return sparks;
            }
        }

        public static ParticleSystem Smoke
        {
            get
            {
                if (smoke == null) smoke = StoryFx.CreateParticles(Host, "CombatSmoke", false, -0.03f, 2000);
                return smoke;
            }
        }

        static Transform Host
        {
            get
            {
                if (runner == null) runner = new GameObject("CombatFx").AddComponent<Runner>();
                return runner.transform;
            }
        }

        /// <summary>Golden sparks flying out of a point (a blade hit).</summary>
        public static void HitSparks(Vector3 point, Vector3 direction, int count, Color color)
        {
            if (direction.sqrMagnitude < 1e-6f) direction = Vector3.up; // (a still blade has no direction: sparks spray up)
            for (int i = 0; i < count; i++)
                StoryFx.Emit(Sparks, point, (direction * 1.5f + Random.insideUnitSphere * 2f), Random.Range(0.01f, 0.03f), Random.Range(0.25f, 0.6f), color);
        }

        /// <summary>A puff of shadow smoke.</summary>
        public static void SmokePuff(Vector3 point, float radius, int count, float rise = 0.4f)
        {
            for (int i = 0; i < count; i++)
                StoryFx.Emit(Smoke, point + Random.insideUnitSphere * radius, Random.insideUnitSphere * 0.4f + Vector3.up * rise,
                             Random.Range(0.12f, 0.35f) * (0.5f + radius), Random.Range(0.6f, 1.4f), new Color(0.04f, 0.02f, 0.06f, 0.75f));
        }

        /// <summary>A word that pops up at a point, rises and fades, always turned to the player.</summary>
        public static void Popup(string text, Vector3 position, Color color, float size = 1f)
        {
            var go = new GameObject("Popup " + text);
            go.transform.position = position;
            var mesh = go.AddComponent<TextMesh>();
            mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = mesh.font.material;
            mesh.text = text;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.fontSize = 64;
            mesh.fontStyle = FontStyle.Bold;
            mesh.characterSize = 0.008f * size;
            mesh.color = color;
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<Floating>();
        }

        /// <summary>Rumbles the controller in that hand (both if null) for a moment. Quest controllers only.</summary>
        public static void Rumble(HandSide? side, float amplitude, float seconds)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Host.GetComponent<Runner>().StartCoroutine(RumbleFor(side, amplitude, seconds));
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static int rumbles;

        static IEnumerator RumbleFor(HandSide? side, float amplitude, float seconds)
        {
            int mine = ++rumbles;
            Set(side, 0.5f, Mathf.Clamp01(amplitude));
            yield return new WaitForSecondsRealtime(seconds); // (a hit-stop or slow motion doesn't stretch it)
            if (mine == rumbles) Set(side, 0f, 0f); // (a newer rumble still going isn't cut short)
        }

        static void Set(HandSide? side, float frequency, float amplitude)
        {
            try
            {
                if (side != HandSide.Right) OVRInput.SetControllerVibration(frequency, amplitude, OVRInput.Controller.LTouch);
                if (side != HandSide.Left) OVRInput.SetControllerVibration(frequency, amplitude, OVRInput.Controller.RTouch);
            }
            catch (System.Exception) { } // hands only: nothing to shake
        }
#endif

        class Runner : MonoBehaviour
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            void OnDestroy() => Set(null, 0f, 0f); // a scene change mid-rumble: never leave a controller buzzing
#endif
        }

        class Floating : MonoBehaviour
        {
            TextMesh mesh;
            Color colour;
            float age;

            void Start()
            {
                mesh = GetComponent<TextMesh>();
                colour = mesh.color;
            }

            void Update()
            {
                age += Time.deltaTime;
                transform.position += Vector3.up * Time.deltaTime * 0.35f;
                Camera cam = Camera.main;
                if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
                float scale = age < 0.12f ? Mathf.Lerp(0.4f, 1.15f, age / 0.12f) : Mathf.Lerp(1.15f, 1f, Mathf.Min(1f, (age - 0.12f) * 5f));
                transform.localScale = Vector3.one * scale;
                mesh.color = new Color(colour.r, colour.g, colour.b, colour.a * Mathf.Clamp01(1.6f - age));
                if (age > 1.6f) Destroy(gameObject);
            }
        }
    }
}
