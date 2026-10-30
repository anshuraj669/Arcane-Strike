using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The blue fire in the temple's cauldron: tall living flames (<see cref="FireFx"/>, the ArcaneStrike/Fire shader) on two
    /// upright sheets turned to the viewer, a little apart so a headset sees depth in it, and a ring of flames leaning in
    /// from the mouth's edge so it still looks like fire seen from above, over a dark bed in the bowl, with bright sparks spraying off it and
    /// a cold flickering light. <see cref="Flare"/> makes it roar up (the Ascension's touch).
    /// </summary>
    public class CauldronFlames : MonoBehaviour
    {
        [Tooltip("How wide the fire is at the mouth (m): the cauldron's lip is 0.94 m across inside, and the fire stays within it.")]
        [SerializeField] float width = 0.9f;
        [Tooltip("How high the flames rise (m).")]
        [SerializeField] float height = 2.6f;
        [SerializeField] float emberRate = 16f;

        /// <summary>How high it burns (1 = in full; low while the soul watches the suit-up, so nothing hides the body).</summary>
        public float Damp { get; set; } = 1f;

        Transform[] sheets;
        Transform ring;
        ParticleSystem embers;
        Light fire;
        AudioSource roar;
        float damp = 1f, emberDue, flare;

        /// <summary>The fire surges for a moment.</summary>
        public void Flare(float amount) => flare = Mathf.Max(flare, amount);

        void Start()
        {
            sheets = new[] { FireFx.Sheet(transform, 0.13f), FireFx.Sheet(transform, 0.57f) };
            ring = FireFx.Wall(transform, "CauldronRing", 0.36f, -0.03f, 1f, 1.6f, 0.83f, 0.08f).transform;
            DarkenBed();
            embers = StoryFx.CreateParticles(transform, "FlameEmbers", true, 0.25f, 120);
            fire = new GameObject("CauldronLight").AddComponent<Light>();
            fire.transform.SetParent(transform, false);
            fire.transform.localPosition = Vector3.up * 0.7f;
            fire.type = LightType.Point;
            fire.color = new Color(0.25f, 0.6f, 1f);
            fire.range = 9f;
            fire.shadows = LightShadows.None;
            roar = StoryFx.CreateSource(gameObject, StoryFx.Rumble(), true, 1f);
            roar.maxDistance = 16f;
            roar.pitch = 1.5f;
            roar.volume = 0.28f;
            roar.Play();
        }

        // The bowl's surface was modelled as a pale glowing disc: the flames need a dark bed to rise from, lit faintly blue.
        void DarkenBed()
        {
            GameObject bed = GameObject.Find("Temple_CauldronFire");
            Renderer r = bed != null ? bed.GetComponent<Renderer>() : null;
            if (r == null) { Debug.LogWarning("[CauldronFlames] No Temple_CauldronFire to darken."); return; }
            Material m = r.material;
            Color dark = new Color(0.008f, 0.012f, 0.03f, 1f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", dark);
            if (m.HasProperty("_Color")) m.SetColor("_Color", dark);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", new Color(0.01f, 0.05f, 0.2f, 1f));
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime, t = Time.time;
            flare = Mathf.MoveTowards(flare, 0f, dt * 0.7f);
            damp = Mathf.MoveTowards(damp, Damp, dt * 0.8f);
            float surge = (1f + flare) * damp;

            Camera eyes = Camera.main;
            Vector3 mouth = transform.position;
            Vector3 toEyes = eyes != null ? StoryFx.Flat(eyes.transform.position - mouth) : Vector3.forward;
            if (toEyes.sqrMagnitude < 1e-4f) toEyes = Vector3.forward;
            Quaternion facing = Quaternion.LookRotation(-toEyes, Vector3.up);
            Vector3 across = Vector3.Cross(Vector3.up, toEyes);
            for (int i = 0; i < sheets.Length; i++)
            {
                float size = i == 0 ? 1f : 0.86f;
                sheets[i].SetPositionAndRotation(mouth - Vector3.up * 0.03f + across * (i == 0 ? -0.02f : 0.03f) - toEyes * (i == 0 ? 0.06f : -0.06f), facing);
                sheets[i].localScale = new Vector3(width * size * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(surge)), height * size * surge, 1f);
            }

            ring.localScale = new Vector3(1f, Mathf.Max(surge, 0.05f), 1f);

            emberDue += emberRate * surge * dt;
            while (emberDue >= 1f)
            {
                emberDue -= 1f;
                Vector2 r = Random.insideUnitCircle * 0.3f;
                StoryFx.Emit(embers, mouth + new Vector3(r.x, 0.3f, r.y), Vector3.up * Random.Range(1.2f, 3f) + Random.insideUnitSphere * 0.25f,
                             Random.Range(0.012f, 0.04f), Random.Range(0.8f, 1.8f), new Color(0.55f, 0.88f, 1f, 1f));
            }
            fire.intensity = (3f + 1.2f * Mathf.PerlinNoise(t * 7f, 0f)) * surge;
        }
    }
}
