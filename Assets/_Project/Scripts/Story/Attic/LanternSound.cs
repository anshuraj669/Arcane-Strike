using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The lantern's hum: soft when it first stirs, louder as it is cleaned, swelling at the awakening.
    /// Uses <see cref="humAudio"/> (and its clip) when assigned, otherwise a placeholder hum made from code.
    /// </summary>
    public class LanternSound : MonoBehaviour
    {
        public AudioSource humAudio;
        [SerializeField] float softVolume = 0.2f;
        [SerializeField] float loudVolume = 0.8f;
        [Tooltip("Pitch at full intensity; the hum rises slightly as it grows.")]
        [SerializeField] float loudPitch = 1.12f;
        [SerializeField] float fadeSpeed = 0.6f;

        bool humming;
        float intensity, swell;

        void Start()
        {
            if (humAudio == null) humAudio = StoryFx.CreateSource(gameObject, null, true, 1f);
            if (humAudio.clip == null) humAudio.clip = StoryFx.Hum();
            humAudio.loop = true;
            humAudio.volume = 0f;
        }

        public void StartHum()
        {
            humming = true;
            if (humAudio != null && !humAudio.isPlaying) humAudio.Play();
        }

        public void StopHum() => humming = false;

        /// <summary>0 = soft hum, 1 = loud.</summary>
        public void SetIntensity(float t) => intensity = Mathf.Clamp01(t);

        /// <summary>A short surge on top of the current level (the awakening).</summary>
        public void Swell() => swell = 1f;

        void Update()
        {
            if (humAudio == null) return;
            swell = Mathf.MoveTowards(swell, 0f, Time.deltaTime * 0.5f);
            float target = humming ? Mathf.Lerp(softVolume, loudVolume, intensity) + swell * 0.3f : 0f;
            humAudio.volume = Mathf.MoveTowards(humAudio.volume, Mathf.Clamp01(target), fadeSpeed * Time.deltaTime);
            humAudio.pitch = Mathf.Lerp(1f, loudPitch, Mathf.Max(intensity, swell));
            if (!humming && humAudio.volume <= 0f && humAudio.isPlaying) humAudio.Stop();
        }
    }
}
