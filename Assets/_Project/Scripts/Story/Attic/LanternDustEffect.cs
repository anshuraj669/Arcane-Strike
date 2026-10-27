using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Dust coming off the lantern while it is rubbed. Uses <see cref="dustParticles"/> when assigned
    /// (art can drop in a real effect), otherwise soft dust motes made at runtime.
    /// </summary>
    public class LanternDustEffect : MonoBehaviour
    {
        [Tooltip("Optional real dust effect. It is moved to the rubbing point and emits there.")]
        public ParticleSystem dustParticles;
        [SerializeField] Color dustColor = new Color(0.78f, 0.7f, 0.58f, 0.55f);
        [Tooltip("Dust motes per metre of rubbing.")]
        [SerializeField] float motesPerMetre = 60f;
        [SerializeField] float moteSize = 0.025f;

        ParticleSystem motes;
        float debt;

        void Start()
        {
            motes = dustParticles != null ? dustParticles : StoryFx.CreateParticles(transform, "LanternDust", false, 0.02f);
        }

        /// <summary>Called every frame the lantern is rubbed, with the palm speed (m/s).</summary>
        public void Rub(Vector3 point, float speed)
        {
            debt += speed * Time.deltaTime * motesPerMetre;
            while (debt >= 1f)
            {
                debt -= 1f;
                Vector3 v = Random.insideUnitSphere * 0.15f + Vector3.up * 0.05f;
                StoryFx.Emit(motes, point + Random.insideUnitSphere * 0.04f, v, moteSize * Random.Range(0.6f, 1.6f),
                             Random.Range(1.2f, 2.5f), dustColor);
            }
        }

        /// <summary>A bigger cloud, e.g. the last of the dust when the lantern comes clean.</summary>
        public void Puff(Vector3 point)
        {
            StoryFx.Burst(motes, point, 40, 0.25f, moteSize * 1.5f, 2.5f, dustColor);
        }
    }
}
