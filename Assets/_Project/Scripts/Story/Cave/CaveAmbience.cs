using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The cave breathing while nothing else happens: water drips from the roof here and there (a falling bead of
    /// light-catching water, a tiny splash and a plink that echoes from where it lands), and the mushroom clusters'
    /// glow swells and ebbs slowly, each at its own pace. Cheap enough for a Quest: one particle system, three pooled
    /// sounds, no new lights (the mushrooms' own lights are only re-weighted). <see cref="Flare"/> makes the mushrooms
    /// flare (the sword's shockwave).
    /// </summary>
    public class CaveAmbience : MonoBehaviour
    {
        [Tooltip("Around this point (the altar) drips are looked for (m).")]
        [SerializeField] float radius = 6f;
        [Tooltip("Seconds between drips (random in this range).")]
        [SerializeField] Vector2 dripEvery = new Vector2(0.8f, 2.6f);
        [SerializeField, Range(0f, 0.4f)] float glowBreath = 0.14f;
        [SerializeField] Color dropColor = new Color(0.7f, 0.82f, 1f, 0.75f);

        const int Spots = 14;
        readonly Vector3[] roofs = new Vector3[Spots];
        readonly float[] falls = new float[Spots]; // drop height (m) under each roof spot
        int spots;

        // Drops on their way down: where and when each lands.
        readonly Vector3[] landAt = new Vector3[8];
        readonly float[] landTime = new float[8];

        Light[] mushrooms;
        float[] baseIntensity, phase;
        float flare, flareFade = 1f;
        ParticleSystem drops;
        AudioSource[] plinks;
        int nextPlink;
        float nextDrip;

        void Start()
        {
            drops = StoryFx.CreateParticles(transform, "CaveDrips", false, 1f, 120);
            FindDripSpots();

            var glows = new System.Collections.Generic.List<Light>();
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.name.StartsWith("MushroomGlow")) glows.Add(l);
            mushrooms = glows.ToArray();
            baseIntensity = new float[mushrooms.Length];
            phase = new float[mushrooms.Length];
            for (int i = 0; i < mushrooms.Length; i++)
            {
                baseIntensity[i] = mushrooms[i].intensity;
                phase[i] = Random.value * 10f;
            }

            AudioClip clip = Plink();
            plinks = new AudioSource[3];
            for (int i = 0; i < plinks.Length; i++)
            {
                var go = new GameObject("DripSound");
                go.transform.SetParent(transform, false);
                plinks[i] = StoryFx.CreateSource(go, clip, false, 1f);
                plinks[i].maxDistance = 14f;
            }
            nextDrip = Time.time + Random.Range(dripEvery.x, dripEvery.y);
        }

        /// <summary>The mushrooms flare `times` as bright, easing back over `seconds`.</summary>
        public void Flare(float times, float seconds)
        {
            flare = Mathf.Max(flare, times - 1f);
            flareFade = flare / Mathf.Max(seconds, 0.05f);
        }

        // Points on the roof with open floor below them (found once: a ray up, a ray down), round the shaft of light.
        void FindDripSpots()
        {
            var shaft = FindAnyObjectByType<LightShaft>();
            Vector3 centre = shaft != null ? new Vector3(shaft.transform.position.x, transform.position.y, shaft.transform.position.z) : transform.position;
            for (int tries = 0; tries < Spots * 3 && spots < Spots; tries++)
            {
                Vector2 r = Random.insideUnitCircle * radius;
                Vector3 p = centre + new Vector3(r.x, 1.5f, r.y);
                if (!Physics.Raycast(p, Vector3.up, out RaycastHit roof, 12f, ~0, QueryTriggerInteraction.Ignore)) continue; // (the hole in the roof)
                if (!Physics.Raycast(p, Vector3.down, out RaycastHit floor, 4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (roof.point.y - floor.point.y < 1.5f) continue;
                roofs[spots] = roof.point - Vector3.up * 0.03f;
                falls[spots] = roof.point.y - floor.point.y - 0.03f;
                spots++;
            }
        }

        void Update()
        {
            // The mushrooms breathe: two slow waves each, never in step with each other.
            flare = Mathf.MoveTowards(flare, 0f, flareFade * Time.deltaTime);
            for (int i = 0; i < mushrooms.Length; i++)
            {
                if (mushrooms[i] == null) continue;
                float t = Time.time + phase[i];
                float breath = 1f + glowBreath * (0.65f * Mathf.Sin(t * 0.9f) + 0.35f * Mathf.Sin(t * 2.3f + 1.7f));
                mushrooms[i].intensity = baseIntensity[i] * breath * (1f + flare);
            }

            if (spots > 0 && Time.time >= nextDrip)
            {
                nextDrip = Time.time + Random.Range(dripEvery.x, dripEvery.y);
                Drip(Random.Range(0, spots));
            }
            for (int i = 0; i < landTime.Length; i++)
                if (landTime[i] > 0f && Time.time >= landTime[i])
                {
                    landTime[i] = 0f;
                    Splash(landAt[i]);
                }
        }

        // A bead of water swells on the roof, lets go and falls (the particle's gravity does the falling).
        void Drip(int spot)
        {
            float h = falls[spot];
            float seconds = Mathf.Sqrt(2f * h / Mathf.Abs(Physics.gravity.y));
            StoryFx.Emit(drops, roofs[spot], Vector3.zero, Random.Range(0.012f, 0.02f), seconds, dropColor);
            for (int i = 0; i < landTime.Length; i++)
                if (landTime[i] <= 0f)
                {
                    landTime[i] = Time.time + seconds;
                    landAt[i] = roofs[spot] - Vector3.up * h;
                    return;
                }
        }

        void Splash(Vector3 at)
        {
            for (int i = 0; i < 5; i++)
            {
                Vector2 r = Random.insideUnitCircle * 0.6f;
                StoryFx.Emit(drops, at + Vector3.up * 0.01f, new Vector3(r.x, Random.Range(0.6f, 1.1f), r.y), Random.Range(0.005f, 0.01f), 0.22f, dropColor);
            }
            AudioSource s = plinks[nextPlink];
            nextPlink = (nextPlink + 1) % plinks.Length;
            s.transform.position = at;
            s.pitch = Random.Range(0.85f, 1.25f);
            s.volume = Random.Range(0.12f, 0.25f);
            s.Play();
        }

        /// <summary>A drop landing in a puddle: a short rising "plink" with a small echo (0.6 s).</summary>
        static AudioClip Plink()
        {
            const int rate = 44100;
            var data = new float[(int)(rate * 0.6f)];
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / rate;
                data[i] = (Tone(t) * 0.8f + Tone(t - 0.11f) * 0.25f + Tone(t - 0.23f) * 0.1f) * Mathf.Min(1f, t * 2000f);
            }
            // (the pitch sweeps up fast as the bubble closes, and dies away in a tenth of a second)
            static float Tone(float s) => s < 0f ? 0f : Mathf.Sin(2f * Mathf.PI * (900f * s + 28000f * s * s)) * Mathf.Exp(-s * 38f);
            AudioClip clip = AudioClip.Create("Drip (placeholder)", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
