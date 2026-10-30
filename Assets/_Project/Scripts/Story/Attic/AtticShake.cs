using System.Collections;
using ArcaneStrike.HandInput;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The attic shakes: props rattle, dust falls from the ceiling and a deep rumble plays.
    /// The camera is never moved: shaking the player's view in VR causes motion sickness,
    /// so the room's objects shake instead.
    /// </summary>
    public class AtticShake : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [Tooltip("Props that rattle (chest, crates...). Not the camera rig.")]
        public Transform[] shakeTargets;
        [SerializeField] float duration = 2.5f;
        [SerializeField] float amplitude = 0.012f;
        [Tooltip("Rattle rotation (degrees).")]
        [SerializeField] float rotationAmplitude = 1.5f;
        [SerializeField] float frequency = 16f;

        [Header("Falling dust")]
        [Tooltip("Height above the player's head that dust falls from (m).")]
        [SerializeField] float dustHeight = 1.0f;
        [SerializeField] float dustArea = 3f;
        [SerializeField] float dustPerSecond = 120f;
        [SerializeField] Color dustColor = new Color(0.75f, 0.68f, 0.56f, 0.5f);

        [Header("Sound")]
        [Tooltip("Optional. A placeholder rumble is used when empty.")]
        public AudioSource rumbleAudio;

        ParticleSystem dust;
        Coroutine shaking;
        float shakeStart;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
        }

        void Start()
        {
            dust = StoryFx.CreateParticles(transform, "CeilingDust", false, 0.25f, 800);
            if (rumbleAudio == null) rumbleAudio = StoryFx.CreateSource(gameObject, StoryFx.Rumble(), false, 0f);
        }

        /// <summary>Starts a shake, or (already shaking) keeps it going longer: a second shake mustn't take the first
        /// one's shaken positions as the place to settle back to.</summary>
        public void Shake()
        {
            if (shakeTargets == null) shakeTargets = new Transform[0];
            float t = Time.time - shakeStart;
            if (shaking == null) shakeStart = Time.time;
            else if (t > duration * 0.5f) shakeStart = Time.time - (duration - t); // settling: build up again from this strength
            if (rumbleAudio != null)
            {
                rumbleAudio.volume = 1f;
                rumbleAudio.Play();
            }
            if (shaking == null) shaking = StartCoroutine(ShakeRoutine());
        }

        IEnumerator ShakeRoutine()
        {
            Debug.Log("[Attic] The attic shakes");

            var basePos = new Vector3[shakeTargets.Length];
            var baseRot = new Quaternion[shakeTargets.Length];
            for (int i = 0; i < shakeTargets.Length; i++)
            {
                if (shakeTargets[i] == null) continue;
                basePos[i] = shakeTargets[i].localPosition;
                baseRot[i] = shakeTargets[i].localRotation;
            }

            float debt = 0f;
            for (float t = 0f; t < duration; t = Time.time - shakeStart)
            {
                float envelope = Mathf.Sin(Mathf.PI * t / duration); // builds up, then settles
                for (int i = 0; i < shakeTargets.Length; i++)
                {
                    Transform target = shakeTargets[i];
                    if (target == null) continue;
                    float seed = i * 13.7f;
                    float n(float o) => (Mathf.PerlinNoise(seed + o, Time.time * frequency) - 0.5f) * 2f;
                    target.localPosition = basePos[i] + new Vector3(n(0f), n(3f) * 0.5f, n(6f)) * amplitude * envelope;
                    target.localRotation = baseRot[i] * Quaternion.Euler(n(9f) * rotationAmplitude * envelope, 0f, n(12f) * rotationAmplitude * envelope);
                }

                Transform head = StoryFx.Head(input);
                if (head != null)
                {
                    debt += dustPerSecond * envelope * Time.deltaTime;
                    while (debt >= 1f)
                    {
                        debt -= 1f;
                        Vector3 p = head.position + Vector3.up * dustHeight +
                                    new Vector3(Random.Range(-0.5f, 0.5f), 0f, Random.Range(-0.5f, 0.5f)) * dustArea;
                        StoryFx.Emit(dust, p, Vector3.down * Random.Range(0.1f, 0.4f), Random.Range(0.01f, 0.035f),
                                     Random.Range(2f, 3.5f), dustColor);
                    }
                }
                yield return null;
            }

            for (int i = 0; i < shakeTargets.Length; i++)
            {
                if (shakeTargets[i] == null) continue;
                shakeTargets[i].localPosition = basePos[i];
                shakeTargets[i].localRotation = baseRot[i];
            }
            shaking = null;
        }
    }
}
