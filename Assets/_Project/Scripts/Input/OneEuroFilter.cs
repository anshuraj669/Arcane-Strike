using UnityEngine;

namespace ArcaneStrike.HandInput
{
    /// <summary>
    /// The One Euro filter (Casiez et al. 2012): very steady while the value is still (a low cut-off of `minCutoff` Hz),
    /// opening up as it moves (the cut-off rises by `beta` per unit/s of speed), so slow drift and jitter are smoothed
    /// away but quick moves come through without lag. Feed it each new sample with its time.
    /// </summary>
    public class OneEuro3
    {
        public float minCutoff = 1f, beta = 1f, derivativeCutoff = 1f;
        Vector3 value, speed;
        float last;
        bool started;

        public OneEuro3(float minCutoff = 1f, float beta = 1f)
        {
            this.minCutoff = minCutoff;
            this.beta = beta;
        }

        public Vector3 Value => value;
        public void Reset() => started = false;

        public Vector3 Filter(Vector3 x, float t)
        {
            if (!started)
            {
                started = true;
                value = x;
                speed = Vector3.zero;
                last = t;
                return x;
            }
            float dt = Mathf.Clamp(t - last, 1e-3f, 0.25f);
            last = t;
            speed = Vector3.Lerp(speed, (x - value) / dt, Alpha(derivativeCutoff, dt));
            value = Vector3.Lerp(value, x, Alpha(minCutoff + beta * speed.magnitude, dt));
            return value;
        }

        public static float Alpha(float cutoff, float dt)
        {
            float tau = 1f / (2f * Mathf.PI * Mathf.Max(cutoff, 1e-3f));
            return 1f / (1f + tau / dt);
        }
    }

    /// <summary>The One Euro filter for a single number (an angle, a distance).</summary>
    public class OneEuro1
    {
        public float minCutoff = 1f, beta = 1f, derivativeCutoff = 1f;
        float value, speed, last;
        bool started;

        public OneEuro1(float minCutoff = 1f, float beta = 1f)
        {
            this.minCutoff = minCutoff;
            this.beta = beta;
        }

        public float Value => value;
        public void Reset() => started = false;

        public float Filter(float x, float t)
        {
            if (!started)
            {
                started = true;
                value = x;
                speed = 0f;
                last = t;
                return x;
            }
            float dt = Mathf.Clamp(t - last, 1e-3f, 0.25f);
            last = t;
            speed = Mathf.Lerp(speed, (x - value) / dt, OneEuro3.Alpha(derivativeCutoff, dt));
            value = Mathf.Lerp(value, x, OneEuro3.Alpha(minCutoff + beta * Mathf.Abs(speed), dt));
            return value;
        }
    }
}
