using System;
using ArcaneStrike.HandInput;
using ArcaneStrike.VFX;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Shared helpers for story scenes: runtime particle systems, placeholder sounds made from code, and
    /// simple hand poses. A scene plays fully before real VFX and audio exist; every script that uses these
    /// also takes a real asset in the Inspector, which wins when it is assigned.
    /// </summary>
    public static class StoryFx
    {
        static Material additive, alphaBlended;

        public static Transform Head(HandInputService input) =>
            input != null && input.Head != null ? input.Head : Camera.main != null ? Camera.main.transform : null;

        public static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        // ---------------------------------------------------------------- hands

        // Own pose checks: the story runs before the player has powers, while GestureRecognizer's Grab is still locked.
        // Three of four fingers is forgiving enough for webcam hands.
        public static bool IsFist(HandFrame f) => CountFingers(f, s => s < 0.65f) >= 3;
        public static bool IsOpen(HandFrame f) => CountFingers(f, s => s > 0.75f) >= 3;

        static int CountFingers(HandFrame f, Func<float, bool> test)
        {
            int n = 0;
            for (Finger finger = Finger.Index; finger <= Finger.Pinky; finger++)
                if (test(f.Straightness(finger))) n++;
            return n;
        }

        // ---------------------------------------------------------------- particles

        /// <summary>Additive glow: light, fire, magic.</summary>
        public static Material Additive
        {
            get
            {
                if (additive == null) additive = SparkHands.CreateGlowMaterial(SparkHands.CreateSoftDot(64));
                return additive;
            }
        }

        /// <summary>Alpha blended: dust, blood, smoke (things that are not light).</summary>
        public static Material AlphaBlended
        {
            get
            {
                if (alphaBlended == null)
                {
                    alphaBlended = SparkHands.CreateGlowMaterial(SparkHands.CreateSoftDot(64));
                    alphaBlended.name = "StoryAlpha";
                    alphaBlended.SetFloat("_Blend", 0f);
                    alphaBlended.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    alphaBlended.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                }
                return alphaBlended;
            }
        }

        /// <summary>A world-space particle system that only emits when told to (see <see cref="Emit"/>).</summary>
        public static ParticleSystem CreateParticles(Transform parent, string name, bool additiveBlend, float gravity, int maxParticles = 600)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            main.startSpeed = 0f;
            main.gravityModifier = gravity;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;

            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));

            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = fade;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = additiveBlend ? Additive : AlphaBlended;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ps.Play();
            return ps;
        }

        public static void Emit(ParticleSystem ps, Vector3 position, Vector3 velocity, float size, float life, Color color)
        {
            if (ps == null) return;
            ps.Emit(new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startSize = size,
                startLifetime = life,
                startColor = color,
            }, 1);
        }

        public static void Burst(ParticleSystem ps, Vector3 position, int count, float speed, float size, float life, Color color)
        {
            var rng = new Random(Environment.TickCount);
            for (int i = 0; i < count; i++)
            {
                Vector3 v = UnityEngine.Random.onUnitSphere * speed * (0.4f + 0.6f * (float)rng.NextDouble());
                Emit(ps, position, v, size * (0.5f + (float)rng.NextDouble()), life * (0.6f + 0.6f * (float)rng.NextDouble()), color);
            }
        }

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, Mathf.Clamp01(a));

        // ---------------------------------------------------------------- placeholder audio

        const int Rate = 44100;

        /// <summary>A soft magical hum (2 s seamless loop).</summary>
        public static AudioClip Hum()
        {
            var data = new float[Rate * 2];
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / Rate;
                float tremolo = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 0.5f * t);
                float s = Mathf.Sin(2f * Mathf.PI * 110f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 220f * t) +
                          0.25f * Mathf.Sin(2f * Mathf.PI * 330f * t) + 0.12f * Mathf.Sin(2f * Mathf.PI * 165f * t);
                data[i] = s * 0.3f * tremolo;
            }
            return Make("Hum (placeholder)", data);
        }

        /// <summary>Breathy, syllable-like noise that reads as distant whispering (3 s seamless loop).</summary>
        public static AudioClip Whisper(int seed)
        {
            var rng = new Random(seed);
            var data = new float[Rate * 3];
            float len = data.Length / (float)Rate;
            float syllableRate = 3f + (float)rng.NextDouble() * 3f;
            float phase = (float)rng.NextDouble() * 10f;
            float prev = 0f, smooth = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / Rate;
                float white = (float)rng.NextDouble() * 2f - 1f;
                float high = white - prev; // keep the hiss, drop the rumble
                prev = white;
                smooth += (high - smooth) * 0.35f;
                float syllable = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * syllableRate * t + phase + Mathf.Sin(t * 1.7f))), 2f);
                float phrase = Mathf.Sin(Mathf.PI * t / len);
                data[i] = smooth * syllable * phrase * 0.6f;
            }
            return Make($"Whisper {seed} (placeholder)", data);
        }

        /// <summary>A deep rumble for the attic shaking (3 s, fades in and out).</summary>
        public static AudioClip Rumble()
        {
            var rng = new Random(7);
            var data = new float[Rate * 3];
            float brown = 0f, low = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / data.Length;
                brown = (brown + ((float)rng.NextDouble() * 2f - 1f) * 0.02f) * 0.997f;
                low += (brown - low) * 0.03f;
                data[i] = Mathf.Clamp(low * 6f, -1f, 1f) * Mathf.Sin(Mathf.PI * t);
            }
            return Make("Rumble (placeholder)", data);
        }

        static AudioClip Make(string name, float[] data)
        {
            AudioClip clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public static AudioSource CreateSource(GameObject host, AudioClip clip, bool loop, float spatialBlend)
        {
            var source = host.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = spatialBlend;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 0.5f;
            source.maxDistance = 8f;
            source.volume = 0f;
            return source;
        }
    }
}
