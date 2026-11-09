using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Placeholder sounds for Chapter 2, made from code (swap in real recordings later): the heavy heartbeat,
    /// panting breath, the ringing in the ears after a shock, and a soft wooden thud.
    /// </summary>
    public static class Chapter2Sounds
    {
        const int Rate = 44100;

        /// <summary>One heavy heartbeat: lub-dub, two low thumps (0.7 s).</summary>
        public static AudioClip Heartbeat()
        {
            var data = new float[(int)(Rate * 0.7f)];
            foreach (float start in new[] { 0f, 0.26f })
            {
                float level = start == 0f ? 1f : 0.65f;
                for (int i = (int)(start * Rate); i < data.Length; i++)
                {
                    float t = (float)i / Rate - start;
                    if (t > 0.35f) break;
                    data[i] += Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(62f, 38f, t * 3f) * t) * Mathf.Exp(-t * 13f) * level * Mathf.Min(1f, t * 400f);
                }
            }
            return Make("Heartbeat (placeholder)", data);
        }

        /// <summary>One breath, in through the mouth then out (about 1 s): breathy noise, louder on the out-breath.</summary>
        public static AudioClip Breath(int seed)
        {
            var rng = new System.Random(seed);
            float inhale = 0.38f + (float)rng.NextDouble() * 0.08f, exhale = 0.5f + (float)rng.NextDouble() * 0.1f;
            var data = new float[(int)(Rate * (inhale + exhale + 0.05f))];
            float low = 0f, band = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / Rate;
                float white = (float)rng.NextDouble() * 2f - 1f;
                low += (white - low) * 0.25f;   // drop the hiss above about 2 kHz
                band += (low - band) * 0.02f;   // ... and the rumble below about 150 Hz
                float s = low - band;
                float env = t < inhale
                    ? Mathf.Sin(Mathf.PI * t / inhale) * 0.55f
                    : Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - inhale) / exhale)) * 1f;
                data[i] = s * env * 0.9f;
            }
            return Make($"Breath {seed} (placeholder)", data);
        }

        /// <summary>The high ring in the ears after a shock (4 s, fading away).</summary>
        public static AudioClip EarRing()
        {
            var data = new float[Rate * 4];
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Min(1f, t * 8f) * Mathf.Exp(-t * 0.9f);
                data[i] = (Mathf.Sin(2f * Mathf.PI * 6200f * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * 6247f * t)) * env * 0.12f;
            }
            return Make("Ear ring (placeholder)", data);
        }

        /// <summary>A body bumping into a wooden crate: a dull knock and a short creak (0.6 s).</summary>
        public static AudioClip CrateThud()
        {
            var rng = new System.Random(11);
            var data = new float[(int)(Rate * 0.6f)];
            float low = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / Rate;
                low += (((float)rng.NextDouble() * 2f - 1f) - low) * 0.08f;
                float knock = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(140f, 90f, t * 4f) * t) * Mathf.Exp(-t * 22f);
                float creak = t > 0.08f ? Mathf.Sin(2f * Mathf.PI * (380f + 60f * Mathf.Sin(t * 40f)) * t) * Mathf.Exp(-(t - 0.08f) * 9f) * 0.12f : 0f;
                data[i] = knock * 0.8f + low * Mathf.Exp(-t * 30f) * 0.6f + creak;
            }
            return Make("Crate thud (placeholder)", data);
        }

        /// <summary>Searing flesh: a sharp "tsssk" of high hiss with crackles, `seconds` long, fading.</summary>
        public static AudioClip Hiss(float seconds)
        {
            var rng = new System.Random(5);
            var data = new float[(int)(Rate * seconds)];
            float prev = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / Rate;
                float white = (float)rng.NextDouble() * 2f - 1f;
                float high = white - prev; // hiss, no rumble
                prev = white;
                float env = Mathf.Min(1f, t * 60f) * Mathf.Exp(-t * 2.2f / seconds) * (0.8f + 0.2f * Mathf.Sin(t * 37f));
                float crackle = rng.NextDouble() < 0.004 ? ((float)rng.NextDouble() * 2f - 1f) * 1.5f : 0f;
                data[i] = Mathf.Clamp((high * 0.35f + crackle) * env, -1f, 1f);
            }
            return Make("Hiss (placeholder)", data);
        }

        /// <summary>High arcane ringing after the boom: bell-like partials, slowly fading (4 s).</summary>
        public static AudioClip ArcaneRing()
        {
            var data = new float[Rate * 4];
            float[] partials = { 1320f, 1987f, 2641f, 3960f };
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / Rate, s = 0f;
                for (int k = 0; k < partials.Length; k++)
                    s += Mathf.Sin(2f * Mathf.PI * partials[k] * t + k) * Mathf.Exp(-t * (0.6f + k * 0.4f)) / (k + 1);
                data[i] = s * 0.25f * Mathf.Min(1f, t * 200f);
            }
            return Make("Arcane ring (placeholder)", data);
        }

        static AudioClip Make(string name, float[] data)
        {
            AudioClip clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
