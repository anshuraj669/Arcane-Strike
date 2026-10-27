using ArcaneStrike.HandInput;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Whispering voices that circle slowly around the player's head after the lantern awakens.
    /// Uses <see cref="whisperClips"/> when assigned (real voice lines), otherwise placeholder whispers made from code.
    /// </summary>
    public class WhisperVoices : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        public AudioClip[] whisperClips;
        [SerializeField] int voices = 3;
        [Tooltip("Distance of the voices from the head (m).")]
        [SerializeField] float radius = 1.2f;
        [Tooltip("How fast the voices circle the player (degrees per second).")]
        [SerializeField] float orbitSpeed = 12f;
        [SerializeField] float volume = 0.6f;
        [SerializeField] float fadeSpeed = 0.4f;

        AudioSource[] sources;
        float[] angles;
        bool playing;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
        }

        public void Play()
        {
            if (sources == null) Build();
            playing = true;
            foreach (AudioSource s in sources)
                if (!s.isPlaying) s.Play();
            Debug.Log("[Attic] Whisper voices");
        }

        public void Stop() => playing = false;

        void Build()
        {
            sources = new AudioSource[voices];
            angles = new float[voices];
            for (int i = 0; i < voices; i++)
            {
                var go = new GameObject($"Whisper{i}");
                go.transform.SetParent(transform, false);
                AudioClip clip = whisperClips != null && whisperClips.Length > 0
                    ? whisperClips[i % whisperClips.Length]
                    : StoryFx.Whisper(i + 1);
                sources[i] = StoryFx.CreateSource(go, clip, true, 1f);
                sources[i].pitch = Random.Range(0.85f, 1.15f);
                angles[i] = i * 360f / voices;
            }
        }

        void Update()
        {
            if (sources == null) return;
            Transform head = StoryFx.Head(input);
            for (int i = 0; i < sources.Length; i++)
            {
                AudioSource s = sources[i];
                s.volume = Mathf.MoveTowards(s.volume, playing ? volume : 0f, fadeSpeed * Time.deltaTime);
                if (!playing && s.volume <= 0f && s.isPlaying) s.Stop();
                if (head == null) continue;

                angles[i] += orbitSpeed * Time.deltaTime * (i % 2 == 0 ? 1f : -1f);
                Vector3 dir = Quaternion.Euler(0f, angles[i], 0f) * Vector3.forward;
                s.transform.position = head.position + dir * radius + Vector3.up * Mathf.Sin(Time.time * 0.7f + i) * 0.3f;
            }
        }
    }
}
