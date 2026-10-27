using ArcaneStrike.HandInput;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Old dust stirs when the player walks up to something (the chest). Uses the player's head position,
    /// so it needs no "Player" tag or collider on the rig. Plays <see cref="dustParticles"/> when assigned,
    /// otherwise a puff of motes made at runtime.
    /// </summary>
    public class DustTrigger : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        public ParticleSystem dustParticles;
        [Tooltip("Head closer than this (m, measured along the floor) stirs the dust.")]
        [SerializeField] float radius = 0.6f;
        [SerializeField] Color dustColor = new Color(0.78f, 0.7f, 0.58f, 0.45f);

        ParticleSystem motes;
        bool inside;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
        }

        void Start()
        {
            if (dustParticles == null) motes = StoryFx.CreateParticles(transform, "StirredDust", false, 0.03f);
        }

        void Update()
        {
            Transform head = StoryFx.Head(input);
            if (head == null) return;
            Vector3 d = head.position - transform.position;
            d.y = 0f;
            bool now = d.magnitude < radius;
            if (now && !inside) Stir();
            inside = now;
        }

        void Stir()
        {
            if (dustParticles != null)
            {
                dustParticles.Play();
                return;
            }
            for (int i = 0; i < 50; i++)
            {
                Vector3 p = transform.position + new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(0f, 0.6f), Random.Range(-0.5f, 0.5f));
                StoryFx.Emit(motes, p, Random.insideUnitSphere * 0.06f + Vector3.up * 0.03f, Random.Range(0.012f, 0.03f),
                             Random.Range(2f, 4f), dustColor);
            }
        }
    }
}
