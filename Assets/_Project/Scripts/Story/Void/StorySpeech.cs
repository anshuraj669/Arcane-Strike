using System.Collections;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// A speaking character's lines: floating subtitles beside the speaker (always turned to the player, typed
    /// out as they are said) and a crackling, fire-like voice made from code, one burst per syllable. Real voice
    /// clips can be passed to <see cref="Say(string, AudioClip, float)"/> instead. Lines are logged too.
    /// </summary>
    public class StorySpeech : MonoBehaviour
    {
        [SerializeField] string speakerName = "Ember";
        [Tooltip("Where the subtitles float, relative to this object.")]
        [SerializeField] Vector3 textOffset = new Vector3(0f, 1.9f, 0f);
        [Tooltip("Subtitle colour in the dark; on a bright background (the white void) it switches to the dark colour.")]
        [SerializeField] Color textColor = new Color(1f, 0.9f, 0.65f, 1f);
        [SerializeField] Color textColorOnBright = new Color(0.22f, 0.09f, 0.02f, 1f);
        [Tooltip("Characters per second.")]
        [SerializeField] float typeSpeed = 22f;
        [SerializeField, Range(0f, 1f)] float voiceVolume = 0.6f;
        [Tooltip("Base pitch of the made-up voice (Ember's is a small, quick crackle).")]
        [SerializeField] float voicePitch = 1.15f;

        public bool IsSpeaking { get; private set; }

        /// <summary>Where the subtitles float, relative to the speaker.</summary>
        public Vector3 TextOffset
        {
            get => textOffset;
            set
            {
                textOffset = value;
                if (text != null) text.transform.localPosition = value;
            }
        }

        TextMesh text;
        AudioSource voice;
        AudioClip[] syllables;
        EmberFlame ember;
        bool skipLine;

        void Awake()
        {
            ember = GetComponent<EmberFlame>();
            var go = new GameObject($"{speakerName} Subtitles");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = textOffset;
            text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 64;
            text.characterSize = 0.012f;
            text.color = textColor;
            text.text = "";
            voice = gameObject.AddComponent<AudioSource>();
            voice.spatialBlend = 1f;
            voice.minDistance = 1.5f;
            voice.maxDistance = 15f;
            voice.playOnAwake = false;
            syllables = new AudioClip[6];
            for (int i = 0; i < syllables.Length; i++) syllables[i] = Syllable(i);
        }

        void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null || text == null) return;
            Color back = cam.clearFlags == CameraClearFlags.SolidColor ? cam.backgroundColor : RenderSettings.fogColor;
            text.color = back.grayscale > 0.5f ? textColorOnBright : textColor; // always readable
            Vector3 away = text.transform.position - cam.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 1e-4f) text.transform.rotation = Quaternion.LookRotation(away);
        }

        /// <summary>Says a line (typed out, with the voice), keeps it up for `hold` seconds after, then clears it.</summary>
        public IEnumerator Say(string line, float hold = 1.6f) => Say(line, null, hold);

        /// <summary>Another speaker: its name (for the log), subtitle colour, voice pitch and where the subtitles float.
        /// Call it right after adding the component.</summary>
        public void Configure(string speaker, Color color, float pitch, Vector3 offset)
        {
            speakerName = speaker;
            textColor = color;
            textColorOnBright = new Color(color.r * 0.4f, color.g * 0.4f, color.b * 0.4f, 1f);
            voicePitch = pitch;
            TextOffset = offset;
            if (text != null) text.gameObject.name = $"{speaker} Subtitles";
        }

        /// <summary>Shows text without saying it (a prompt), until <see cref="ClearText"/> or the next line.</summary>
        public void ShowText(string line)
        {
            if (!IsSpeaking) text.text = Wrap(line, 34);
        }

        public void ClearText()
        {
            if (!IsSpeaking) text.text = "";
        }

        /// <summary>Debug: finish the current line now (shown whole, no hold).</summary>
        public void SkipLine() => skipLine = IsSpeaking;

        // Each line's number: a newer line takes the subtitle over and the older one just stops (no two typing at once).
        int lineId;

        public IEnumerator Say(string line, AudioClip clip, float hold)
        {
            int id = ++lineId;
            IsSpeaking = true;
            skipLine = false;
            Debug.Log($"[{speakerName}] {line}");
            if (clip != null) voice.PlayOneShot(clip, voiceVolume);
            float shown = 0f, nextSyllable = 0f;
            int typed = -1;
            while (shown < line.Length && !skipLine)
            {
                shown += typeSpeed * Time.deltaTime;
                int count = Mathf.Min(line.Length, Mathf.FloorToInt(shown));
                if (count != typed) text.text = Wrap(line.Substring(0, count), 34); // only when a letter is added
                typed = count;
                char c = count > 0 ? line[count - 1] : ' ';
                bool pause = c == '.' || c == ',' || c == '?' || c == '!';
                if (ember != null) ember.Speaking = pause ? 0.2f : 1f;
                if (clip == null && !pause && char.IsLetter(c) && Time.time >= nextSyllable)
                {
                    voice.pitch = voicePitch * Random.Range(0.85f, 1.2f);
                    voice.PlayOneShot(syllables[Random.Range(0, syllables.Length)], voiceVolume);
                    nextSyllable = Time.time + Random.Range(0.08f, 0.13f);
                }
                yield return pause ? new WaitForSeconds(0.12f) : null;
                if (id != lineId) yield break;
            }
            text.text = Wrap(line, 34);
            if (ember != null) ember.Speaking = 0f;
            for (float t = 0f; t < hold && !skipLine; t += Time.deltaTime)
            {
                yield return null;
                if (id != lineId) yield break;
            }
            skipLine = false;
            text.text = "";
            IsSpeaking = false;
        }

        /// <summary>Says a line in a recorded voice, with no subtitle, then waits `hold` seconds. `line` is only logged.</summary>
        public IEnumerator Speak(AudioClip clip, string line, float hold)
        {
            int id = ++lineId;
            IsSpeaking = true;
            skipLine = false;
            Debug.Log($"[{speakerName}] {line}");
            text.text = "";
            voice.pitch = 1f;
            voice.PlayOneShot(clip);
            if (ember != null) ember.Speaking = 1f;
            for (float t = 0f; t < clip.length + hold && !skipLine; t += Time.deltaTime)
            {
                if (t >= clip.length && ember != null) ember.Speaking = 0f;
                yield return null;
                if (id != lineId) yield break;
            }
            voice.Stop();
            if (ember != null) ember.Speaking = 0f;
            skipLine = false;
            IsSpeaking = false;
        }

        static string Wrap(string s, int width)
        {
            var sb = new System.Text.StringBuilder();
            int col = 0;
            foreach (string word in s.Split(' '))
            {
                if (col > 0 && col + word.Length > width)
                {
                    sb.Append('\n');
                    col = 0;
                }
                else if (col > 0)
                {
                    sb.Append(' ');
                    col++;
                }
                sb.Append(word);
                col += word.Length;
            }
            return sb.ToString();
        }

        // A crackling "syllable": a short voiced burst (a buzzy tone with a vowel-like resonance) plus fire crackle.
        static AudioClip Syllable(int seed)
        {
            const int rate = 44100;
            var rng = new System.Random(seed * 31 + 7);
            int length = (int)(rate * 0.09f);
            var data = new float[length];
            float f0 = 180f + seed * 18f, formant = 700f + (float)rng.NextDouble() * 900f;
            float y1 = 0f, y2 = 0f, w = 2f * Mathf.PI * formant / rate, r = 0.97f;
            for (int i = 0; i < length; i++)
            {
                float t = (float)i / rate;
                float env = Mathf.Sin(Mathf.PI * i / length);
                float buzz = (t * f0 % 1f) * 2f - 1f;
                float x = buzz * 0.3f + ((float)rng.NextDouble() * 2f - 1f) * (rng.NextDouble() < 0.02 ? 1f : 0.08f);
                float y = x + 2f * r * Mathf.Cos(w) * y1 - r * r * y2;
                y2 = y1;
                y1 = y;
                data[i] = Mathf.Clamp(y * 0.05f * env, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create($"Syllable {seed} (placeholder)", length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
