using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The monster world's fire and smoke: the volcano's crater glows and throws up a towering plume of smoke, now and
    /// then erupting (a flare of light, a fountain of glowing rock, a distant boom); steam and smoke curl up from vents
    /// in the plains; ash drifts down and embers float up round the player.
    /// </summary>
    public class VolcanoFx : MonoBehaviour
    {
        public Vector3 crater = new Vector3(45f, 62f, 170f);
        public Vector3[] vents = new Vector3[0];
        [SerializeField] Color fire = new Color(1f, 0.42f, 0.12f, 1f);
        [SerializeField] Vector2 eruptionEvery = new Vector2(12f, 25f);

        ParticleSystem smoke, sparks, ash;
        Light craterLight;
        Transform head;
        float nextEruption, eruption, ashDue;

        void Start()
        {
            smoke = StoryFx.CreateParticles(transform, "Smoke", false, -0.01f, 900);
            sparks = StoryFx.CreateParticles(transform, "Fire", true, 0.6f, 600);
            ash = StoryFx.CreateParticles(transform, "Embers", true, 0f, 300);
            ParticleSystem.SizeOverLifetimeModule billow = smoke.sizeOverLifetime; // smoke spreads as it rises
            billow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.4f, 1f, 1.8f));
            craterLight = new GameObject("CraterGlow").AddComponent<Light>();
            craterLight.transform.SetParent(transform, false);
            craterLight.transform.position = crater + Vector3.up * 6f;
            craterLight.type = LightType.Point;
            craterLight.color = fire;
            craterLight.range = 160f;
            craterLight.intensity = 3f;
            craterLight.shadows = LightShadows.None;
            nextEruption = Time.time + Random.Range(5f, 10f);
        }

        void Update()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            float dt = Time.deltaTime;

            // The plume: big dark billows rising from the crater, spreading and drifting with the wind.
            // (rates per second, not per frame: the same sky at 72 or 90 fps)
            if (Random.value < dt * 36f)
                StoryFx.Emit(smoke, crater + Random.insideUnitSphere * 6f, new Vector3(1.5f, Random.Range(5f, 9f), 0.5f) + Random.insideUnitSphere * 1.5f,
                             Random.Range(18f, 34f), Random.Range(10f, 16f), new Color(0.12f, 0.1f, 0.1f, 0.6f));
            // Glowing rock thrown up from the crater; more of it while it erupts.
            int bombs = Random.value < dt * (18f + eruption * 140f) ? (eruption > 0f ? 4 : 1) : 0;
            for (int i = 0; i < bombs; i++)
                StoryFx.Emit(sparks, crater + Random.insideUnitSphere * 3f, Random.insideUnitSphere * 8f + Vector3.up * Random.Range(12f, 28f),
                             Random.Range(0.8f, 2f), Random.Range(2.5f, 4f), fire);
            // Steam and smoke from the vents in the plains.
            foreach (Vector3 v in vents)
                if (Random.value < dt * 6f)
                    StoryFx.Emit(smoke, v + Random.insideUnitSphere * 0.5f, new Vector3(0.3f, Random.Range(1.2f, 2.2f), 0.1f),
                                 Random.Range(1.5f, 3.5f), Random.Range(4f, 7f), new Color(0.55f, 0.5f, 0.48f, 0.35f));

            // Eruptions: a flare of light and a boom rolling in from far off.
            if (Time.time > nextEruption)
            {
                eruption = 1f;
                nextEruption = Time.time + Random.Range(eruptionEvery.x, eruptionEvery.y);
                if (head != null) Combat.CombatAudio.Play(Combat.CombatAudio.Roar, head.position + (crater - head.position).normalized * 30f, 0.35f, 0.1f);
                Combat.CombatFx.Rumble(null, 0.25f, 0.6f); // the ground trembles under the player's feet
            }
            eruption = Mathf.Max(0f, eruption - dt * 0.4f);
            craterLight.intensity = 3f + eruption * 6f + Mathf.PerlinNoise(Time.time * 1.3f, 0f) * 1.5f;

            // Ash drifting down and embers floating up round the player.
            if (head != null)
            {
                for (ashDue = Mathf.Min(ashDue + dt * 140f, 8f); ashDue >= 1f; ashDue -= 1f)
                {
                    Vector3 p = head.position + new Vector3(Random.Range(-8f, 8f), Random.Range(-1f, 5f), Random.Range(-8f, 8f));
                    bool ember = Random.value < 0.35f;
                    StoryFx.Emit(ember ? ash : smoke, p, ember ? new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0.3f, 0.8f), Random.Range(-0.2f, 0.2f))
                                               : new Vector3(0.3f, -Random.Range(0.2f, 0.5f), 0.1f),
                                 ember ? Random.Range(0.015f, 0.035f) : Random.Range(0.02f, 0.05f), Random.Range(3f, 6f),
                                 ember ? fire : new Color(0.35f, 0.33f, 0.32f, 0.6f));
                }
            }
        }
    }
}
