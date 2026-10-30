using UnityEngine;

namespace ArcaneStrike.Combat
{
    /// <summary>
    /// Placeholder sounds for sword fighting in the Rift, made from code (each built once and cached), and a way to
    /// play one at a point. Real clips can replace them later; every user also works without them. One-shots share a
    /// small pool of voices (no new object per sound, no pile-up when a sweep hits a crowd): the same clip at most a few
    /// times at once, and the oldest voice gives way when all are busy.
    /// </summary>
    public static class CombatAudio
    {
        const int Rate = 44100;
        const int Voices = 16, SameClip = 3;
        static AudioSource[] voices;
        static float[] startedAt;
        static AudioClip swoosh, heavySwoosh, hit, clang, growl, death, roar, tear, drone, wave;

        /// <summary>A blade cutting the air: a sweep of filtered noise (0.3 s).</summary>
        public static AudioClip Swoosh => swoosh != null ? swoosh : swoosh = Sweep("Swoosh", 0.3f, 900f, 3500f, 0.5f, 1);

        /// <summary>A heavy swing: lower and longer (0.5 s).</summary>
        public static AudioClip HeavySwoosh => heavySwoosh != null ? heavySwoosh : heavySwoosh = Sweep("Heavy swoosh", 0.5f, 300f, 1400f, 0.7f, 2);

        /// <summary>The blade biting into shadow: a thwack and a sizzle (0.4 s).</summary>
        public static AudioClip Hit => hit != null ? hit : hit = Make("Hit", 0.4f, (t, rng) =>
            (Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(180f, 60f, t * 4f) * t) * Mathf.Exp(-t * 30f) * 0.9f
             + Noise(rng) * Mathf.Exp(-t * 9f) * 0.35f));

        /// <summary>Steel on claw: a bright ring (0.8 s).</summary>
        public static AudioClip Clang => clang != null ? clang : clang = Make("Clang", 0.8f, (t, rng) =>
        {
            float s = 0f;
            float[] partials = { 1f, 2.76f, 5.4f, 8.9f };
            for (int k = 0; k < partials.Length; k++) s += Mathf.Sin(2f * Mathf.PI * 1250f * partials[k] * t) * Mathf.Exp(-t * (5f + k * 4f)) / (k + 1);
            return s * 0.5f + Noise(rng) * Mathf.Exp(-t * 60f) * 0.6f;
        });

        /// <summary>A shadow's snarl (0.9 s).</summary>
        public static AudioClip Growl => growl != null ? growl : growl = Make("Growl", 0.9f, (t, rng) =>
        {
            float env = Mathf.Sin(Mathf.PI * t / 0.9f);
            float buzz = Mathf.Sin(2f * Mathf.PI * (70f + 20f * Mathf.Sin(t * 30f)) * t);
            return (buzz * 0.6f + Noise(rng) * 0.35f) * env * (0.7f + 0.3f * Mathf.Sin(t * 47f));
        });

        /// <summary>A shadow coming apart: a falling hiss (1.2 s).</summary>
        public static AudioClip Death => death != null ? death : death = Make("Death", 1.2f, (t, rng) =>
            Noise(rng) * Mathf.Exp(-t * 3f) * 0.6f + Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(220f, 50f, t) * t) * Mathf.Exp(-t * 4f) * 0.4f);

        /// <summary>The mini boss: a long, deep roar (3 s).</summary>
        public static AudioClip Roar => roar != null ? roar : roar = Make("Roar", 3f, (t, rng) =>
        {
            float env = Mathf.Min(1f, t * 3f) * Mathf.Exp(-Mathf.Max(0f, t - 1.8f) * 2.5f);
            float pitch = 45f + 25f * Mathf.Sin(Mathf.Min(t, 1.5f) * 2f);
            float body = Mathf.Sin(2f * Mathf.PI * pitch * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * pitch * 2.01f * t);
            return (body * 0.5f + Noise(rng) * 0.45f * (0.6f + 0.4f * Mathf.Sin(t * 23f))) * env;
        }, 5);

        /// <summary>The world tearing open: crackling rip (1.5 s).</summary>
        public static AudioClip Tear => tear != null ? tear : tear = Make("Tear", 1.5f, (t, rng) =>
            Noise(rng) * (rng.NextDouble() < 0.04 ? 1f : 0.25f) * Mathf.Exp(-t * 1.5f) + Mathf.Sin(2f * Mathf.PI * 38f * t) * Mathf.Exp(-t * 2f) * 0.5f, 7);

        /// <summary>The Rift's air: a low, slowly beating drone (4 s loop).</summary>
        public static AudioClip Drone => drone != null ? drone : drone = Make("Drone", 4f, (t, rng) =>
            (Mathf.Sin(2f * Mathf.PI * 55f * t) + Mathf.Sin(2f * Mathf.PI * 55.5f * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * 82.5f * t)) * 0.12f
            + Noise(rng) * 0.03f, 9);

        /// <summary>The Jyoti Wave leaving the blade: a bright rising shimmer (1 s).</summary>
        public static AudioClip Wave => wave != null ? wave : wave = Make("Jyoti wave", 1f, (t, rng) =>
        {
            float s = 0f;
            for (int k = 1; k <= 4; k++) s += Mathf.Sin(2f * Mathf.PI * (400f + 600f * t) * k * t) / k;
            return s * Mathf.Exp(-t * 2.5f) * 0.35f + Noise(rng) * Mathf.Exp(-t * 6f) * 0.2f;
        });

        /// <summary>Plays a clip at a point in the world (3D), at a slightly varied pitch.</summary>
        public static void Play(AudioClip clip, Vector3 position, float volume = 1f, float pitchJitter = 0.1f)
        {
            if (clip == null || volume <= 0.01f) return;
            AudioSource source = Voice(clip);
            if (source == null) return;
            source.transform.position = position;
            source.clip = clip;
            source.volume = volume;
            source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            source.Play();
        }

        // A free voice, or the oldest; null if this clip is already sounding enough times right now.
        static AudioSource Voice(AudioClip clip)
        {
            if (voices == null || voices[0] == null) MakeVoices();
            int same = 0, free = -1, oldest = 0;
            for (int i = 0; i < Voices; i++)
            {
                AudioSource v = voices[i];
                bool busy = v.isPlaying;
                if (busy && v.clip == clip && Time.unscaledTime - startedAt[i] < 0.25f) same++;
                if (!busy && free < 0) free = i;
                if (startedAt[i] < startedAt[oldest]) oldest = i;
            }
            if (same >= SameClip) return null; // (a crowd hit at once sounds as one big hit, not twelve)
            int pick = free >= 0 ? free : oldest;
            startedAt[pick] = Time.unscaledTime;
            return voices[pick];
        }

        static void MakeVoices()
        {
            var host = new GameObject("CombatAudio");
            Object.DontDestroyOnLoad(host);
            voices = new AudioSource[Voices];
            startedAt = new float[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var go = new GameObject("Voice " + i);
                go.transform.SetParent(host.transform, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0.8f;
                source.minDistance = 1.5f;
                source.maxDistance = 30f;
                source.dopplerLevel = 0f;
                voices[i] = source;
                startedAt[i] = -10f;
            }
        }

        static float Noise(System.Random rng) => (float)rng.NextDouble() * 2f - 1f;

        static AudioClip Sweep(string name, float seconds, float from, float to, float level, int seed)
        {
            // Band-passed noise whose centre slides from `from` to `to` and back down: whoosh.
            var rng = new System.Random(seed);
            var data = new float[(int)(Rate * seconds)];
            float y1 = 0f, y2 = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float k = (float)i / data.Length;
                float f = Mathf.Lerp(from, to, Mathf.Sin(k * Mathf.PI));
                float w = 2f * Mathf.PI * f / Rate, r = 0.985f;
                float y = Noise(rng) + 2f * r * Mathf.Cos(w) * y1 - r * r * y2;
                y2 = y1;
                y1 = y;
                data[i] = Mathf.Clamp(y * 0.03f * level * Mathf.Sin(k * Mathf.PI), -1f, 1f);
            }
            return Clip(name, data);
        }

        static AudioClip Make(string name, float seconds, System.Func<float, System.Random, float> sample, int seed = 3)
        {
            var rng = new System.Random(seed);
            var data = new float[(int)(Rate * seconds)];
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / Rate;
                data[i] = Mathf.Clamp(sample(t, rng) * Mathf.Min(1f, t * 300f), -1f, 1f);
            }
            return Clip(name, data);
        }

        static AudioClip Clip(string name, float[] data)
        {
            AudioClip clip = AudioClip.Create(name + " (placeholder)", data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
