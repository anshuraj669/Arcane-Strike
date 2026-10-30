using System.Collections;
using System.Collections.Generic;
using ArcaneStrike.Combat;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// A boulder on the ledge with a Kalakh inside. <see cref="Burst"/>: it shudders harder and harder, the cracks in it
    /// glow hotter, dust trickles off it and the ground rumbles... then it blows apart: chunks of rock tumble and roll
    /// across the ground, smoke and sparks billow out, and the monster comes through it.
    /// </summary>
    public class SpawnRock : MonoBehaviour
    {
        [Tooltip("The glowing cracks over the boulder (brightened as it is about to burst).")]
        public Renderer cracks;
        [Tooltip("Rock chunks to scatter when it bursts (copied; empty: plain lumps).")]
        public GameObject[] chunkTemplates = new GameObject[0];
        [SerializeField] Color crackGlow = new Color(1f, 0.38f, 0.08f, 1f);

        public bool Broken { get; private set; }
        /// <summary>Where the monster stands as it breaks out (the boulder's base, on the ground).</summary>
        public Vector3 Base => transform.position;

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static AudioClip crash, crack;

        /// <summary>Moves the boulder (and its cracks) to stand somewhere else, turned by `yaw` (a fresh fight: the
        /// hunt scatters them).</summary>
        public void MoveTo(Vector3 at, float yaw)
        {
            if (Broken) return;
            transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f) * transform.rotation);
        }
        readonly List<GameObject> debris = new List<GameObject>();

        /// <summary>Shakes, glows, and bursts (the coroutine returns at the moment it blows apart).</summary>
        public IEnumerator Burst(float shake = 2.2f)
        {
            if (Broken) yield break;
            Broken = true;
            Vector3 home = transform.position;
            Quaternion rest = transform.rotation;
            Bounds bounds = Bounds();
            Material glow = cracks != null ? cracks.material : null;
            if (glow != null) glow.EnableKeyword("_EMISSION");
            var ears = new GameObject("RockRumble");
            ears.transform.position = bounds.center;
            AudioSource rumble = StoryFx.CreateSource(ears, StoryFx.Rumble(), true, 1f);
            rumble.maxDistance = 60f;
            rumble.Play();
            CombatAudio.Play(CombatAudio.Growl, bounds.center, 0.9f, 0.2f); // something inside
            float nextCrack = 0.3f;

            for (float t = 0f; t < shake; t += Time.deltaTime)
            {
                float k = t / shake, s = k * k;
                transform.SetPositionAndRotation(home + Random.insideUnitSphere * 0.06f * s,
                                                 rest * Quaternion.Euler(Random.Range(-2f, 2f) * s, Random.Range(-2f, 2f) * s, Random.Range(-2f, 2f) * s));
                rumble.volume = 0.3f + 0.7f * k;
                if (glow != null) glow.SetColor(EmissionColor, crackGlow * Mathf.Lerp(0.2f, 4f, s) * (0.8f + 0.2f * Mathf.Sin(t * 25f)));
                if (Random.value < 0.25f + k) CombatFx.SmokePuff(bounds.center + new Vector3(Random.Range(-1f, 1f) * bounds.extents.x, bounds.extents.y * Random.Range(0f, 1f), Random.Range(-1f, 1f) * bounds.extents.z), 0.2f, 1, -0.3f);
                if (Random.value < k * 0.5f) StoryFx.Emit(CombatFx.Sparks, bounds.center + Random.insideUnitSphere * bounds.extents.x, Random.insideUnitSphere, 0.03f, 0.4f, crackGlow);
                if (Random.value < 0.1f) CombatFx.Rumble(null, 0.2f + 0.5f * k, 0.1f);
                if (t >= nextCrack) // the stone splitting, faster and louder as it goes
                {
                    Loud(crack != null ? crack : crack = Crack(), bounds.center, 0.5f + 0.5f * k, 0.8f + Random.value * 0.4f);
                    nextCrack = t + Mathf.Lerp(0.6f, 0.15f, k) * Random.Range(0.7f, 1.3f);
                }
                yield return null;
            }
            transform.SetPositionAndRotation(home, rest);
            Destroy(ears);
            Explode(bounds);
        }

        Bounds Bounds()
        {
            var rs = GetComponentsInChildren<Renderer>();
            Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(transform.position + Vector3.up, Vector3.one * 2f);
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            if (cracks != null) b.Encapsulate(cracks.bounds);
            return b;
        }

        void Explode(Bounds b)
        {
            Renderer own = GetComponentInChildren<Renderer>();
            Material stone = own != null ? own.sharedMaterial : null;
            foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;
            if (cracks != null) cracks.enabled = false;
            foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;

            for (int i = 0; i < 16; i++)
            {
                GameObject chunk;
                if (chunkTemplates != null && chunkTemplates.Length > 0 && chunkTemplates[i % chunkTemplates.Length] != null)
                {
                    chunk = Instantiate(chunkTemplates[i % chunkTemplates.Length]);
                    chunk.SetActive(true);
                    chunk.transform.localScale = Vector3.one * Random.Range(0.7f, 1.6f);
                }
                else
                {
                    chunk = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Destroy(chunk.GetComponent<Collider>());
                    chunk.transform.localScale = new Vector3(Random.Range(0.3f, 0.7f), Random.Range(0.25f, 0.55f), Random.Range(0.3f, 0.7f));
                    if (stone != null) chunk.GetComponent<Renderer>().sharedMaterial = stone;
                }
                chunk.name = "RockChunk";
                chunk.transform.SetPositionAndRotation(b.center + Vector3.Scale(Random.insideUnitSphere, b.extents * 0.8f), Random.rotation);
                var col = chunk.AddComponent<SphereCollider>();
                col.radius = 0.35f;
                var body = chunk.AddComponent<Rigidbody>();
                body.mass = 30f;
                Vector3 out_ = chunk.transform.position - new Vector3(b.center.x, b.min.y, b.center.z);
                body.linearVelocity = out_.normalized * Random.Range(3f, 7f) + Vector3.up * Random.Range(1f, 4f);
                body.angularVelocity = Random.insideUnitSphere * 6f;
                debris.Add(chunk);
            }
            CombatFx.SmokePuff(b.center, b.extents.x, 50, 1.2f);
            for (int i = 0; i < 60; i++)
                StoryFx.Emit(CombatFx.Sparks, b.center + Random.insideUnitSphere * b.extents.x * 0.5f, Random.insideUnitSphere * 6f + Vector3.up * 2f,
                             Random.Range(0.03f, 0.07f), Random.Range(0.5f, 1.1f), crackGlow);
            Loud(crash != null ? crash : crash = Crash(), b.center, 1f, Random.Range(0.9f, 1.05f)); // heard across the ledge
            CombatAudio.Play(CombatAudio.HeavySwoosh, b.center, 1f, 0.1f);
            CombatAudio.Play(CombatAudio.Hit, b.center, 1f, 0.3f);
            CombatAudio.Play(CombatAudio.Roar, b.center, 0.8f, 0.1f);
            CombatFx.Rumble(null, 1f, 0.4f);
            var flash = new GameObject("BurstLight").AddComponent<Light>();
            flash.transform.position = b.center;
            flash.type = LightType.Point;
            flash.color = crackGlow;
            flash.range = 12f;
            flash.intensity = 8f;
            flash.shadows = LightShadows.None;
            StartCoroutine(Settle(flash));
        }

        // The flash fades; after a while the rubble stops tumbling and sinks out of the way.
        IEnumerator Settle(Light flash)
        {
            for (float t = 0f; t < 0.8f; t += Time.deltaTime)
            {
                flash.intensity = Mathf.Lerp(8f, 0f, t / 0.8f);
                yield return null;
            }
            Destroy(flash.gameObject);
            yield return new WaitForSeconds(7f);
            foreach (GameObject d in debris)
                if (d != null)
                {
                    Destroy(d.GetComponent<Rigidbody>());
                    Destroy(d.GetComponent<Collider>());
                }
            for (float t = 0f; t < 4f; t += Time.deltaTime)
            {
                foreach (GameObject d in debris)
                    if (d != null) d.transform.position += Vector3.down * Time.deltaTime * 0.15f;
                yield return null;
            }
            foreach (GameObject d in debris)
                if (d != null) Destroy(d);
        }

        // A big sound that carries (most of it heard wherever the player is, a little of it placed at the rock so
        // they can tell where it came from); it outlives the boulder.
        static void Loud(AudioClip clip, Vector3 at, float volume, float pitch)
        {
            var go = new GameObject("RockCrash");
            go.transform.position = at;
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume;
            source.pitch = pitch;
            source.spatialBlend = 0.6f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 8f;
            source.maxDistance = 80f;
            source.dopplerLevel = 0f;
            source.Play();
            Destroy(go, clip.length / Mathf.Max(pitch, 0.1f) + 0.2f);
        }

        // The boulder blowing apart: a deep thump with a ragged rock-shatter on top and rubble rattling down after.
        static AudioClip Crash()
        {
            const int rate = 44100;
            int length = (int)(rate * 2.2f);
            var data = new float[length];
            var rng = new System.Random(41);
            float low = 0f, mid = 0f;
            for (int i = 0; i < length; i++)
            {
                float t = (float)i / rate;
                float noise = (float)rng.NextDouble() * 2f - 1f;
                low += (noise - low) * 0.02f;
                mid += (noise - mid) * 0.25f;
                float thump = Mathf.Sin(2f * Mathf.PI * (48f - 18f * t) * t) * Mathf.Exp(-t * 3.5f);
                float shatter = mid * Mathf.Exp(-t * 6f);
                float rubble = (rng.NextDouble() < 0.004 * Mathf.Exp(-t * 1.2f) ? noise : 0f) + low * 2.5f * Mathf.Exp(-t * 1.8f);
                data[i] = Mathf.Clamp(thump * 0.9f + shatter * 0.7f + rubble * 0.5f, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("Rock crash (placeholder)", length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // A sharp crack of stone splitting.
        static AudioClip Crack()
        {
            const int rate = 44100;
            int length = (int)(rate * 0.35f);
            var data = new float[length];
            var rng = new System.Random(17);
            for (int i = 0; i < length; i++)
            {
                float t = (float)i / rate;
                float noise = (float)rng.NextDouble() * 2f - 1f;
                data[i] = Mathf.Clamp(noise * Mathf.Exp(-t * 28f) * 0.9f + Mathf.Sin(2f * Mathf.PI * 90f * t) * Mathf.Exp(-t * 12f) * 0.5f, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("Rock crack (placeholder)", length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>No boulder model: a rough one made of lumps, so the monster still has something to break out of.</summary>
        public static SpawnRock Makeshift(Vector3 at)
        {
            var root = new GameObject("SpawnRock (makeshift)");
            root.transform.position = at;
            var stone = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "MakeshiftRock" };
            stone.SetColor("_BaseColor", new Color(0.2f, 0.17f, 0.17f));
            for (int i = 0; i < 4; i++)
            {
                var lump = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(lump.GetComponent<Collider>());
                lump.transform.SetParent(root.transform, false);
                lump.transform.localPosition = new Vector3(Random.Range(-0.4f, 0.4f), 0.7f + i * 0.45f, Random.Range(-0.4f, 0.4f));
                lump.transform.localScale = new Vector3(Random.Range(1.4f, 2f), Random.Range(1f, 1.4f), Random.Range(1.4f, 2f)) * (1f - i * 0.15f);
                lump.transform.localRotation = Random.rotation;
                lump.GetComponent<Renderer>().sharedMaterial = stone;
            }
            return root.AddComponent<SpawnRock>();
        }
    }
}
