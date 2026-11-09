using System.Collections;
using ArcaneStrike.HandInput;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The player character's own words, which have no speaker in the room to float beside: a subtitle low in the
    /// view that follows the head lazily (it drifts after a turn rather than being glued to the eyes, which is
    /// easier in a headset). Also shows small hints ("Go downstairs") and full-view captions on a black screen
    /// ("23:42 PM — Midnight Hours"). Everything draws over the screen fade, so it reads on black.
    /// A line plays a recording from Resources/VO/Chapter2/&lt;key&gt; when one is there; otherwise it is text only.
    /// </summary>
    public class InnerVoice : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] Color lineColor = new Color(0.92f, 0.94f, 1f, 1f);
        [SerializeField] Color hintColor = new Color(1f, 0.86f, 0.55f, 1f);
        [SerializeField] Color captionColor = new Color(0.95f, 0.95f, 0.95f, 1f);
        [Tooltip("Where the subtitle floats: metres ahead of the eyes and below them.")]
        [SerializeField] float distance = 1.3f, drop = 0.28f;
        [Tooltip("How quickly the subtitle drifts back in front after the head turns (per second).")]
        [SerializeField] float follow = 2.5f;
        [Tooltip("Characters per second.")]
        [SerializeField] float typeSpeed = 26f;

        public bool IsSpeaking { get; private set; }

        Transform head, panel;
        TextMesh line, hint, caption;
        AudioSource voice;
        Material overlay;
        bool placed, skip;
        int lineId;

        public static InnerVoice Create(HandInputService input)
        {
            var go = new GameObject("InnerVoice");
            var v = go.AddComponent<InnerVoice>();
            v.input = input;
            return v;
        }

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            overlay = new Material(font.material) { name = "InnerVoiceText", renderQueue = 5001 }; // over the screen fade
            overlay.SetFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
            panel = new GameObject("Panel").transform;
            panel.SetParent(transform, false);
            line = Text("Line", font, new Vector3(0f, 0f, 0f), 0.0105f, lineColor);
            hint = Text("Hint", font, new Vector3(0f, -0.12f, 0f), 0.008f, hintColor);
            caption = Text("Caption", font, new Vector3(0f, 0.3f, 0.5f), 0.016f, captionColor);
            caption.anchor = TextAnchor.MiddleCenter;
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0f; // inside the player's own head
        }

        TextMesh Text(string name, Font font, Vector3 at, float size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(panel, false);
            go.transform.localPosition = at;
            var t = go.AddComponent<TextMesh>();
            t.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = overlay;
            t.anchor = TextAnchor.UpperCenter;
            t.alignment = TextAlignment.Center;
            t.fontSize = 64;
            t.characterSize = size;
            t.color = color;
            t.text = "";
            return t;
        }

        void LateUpdate()
        {
            if (head == null) head = StoryFx.Head(input);
            if (head == null) return;
            Vector3 forward = StoryFx.Flat(head.forward);
            Vector3 want = head.position + forward * distance - Vector3.up * drop;
            float k = placed ? 1f - Mathf.Exp(-follow * Time.deltaTime) : 1f;
            panel.position = Vector3.Lerp(panel.position, want, k);
            Vector3 away = panel.position - head.position;
            away.y = 0f;
            if (away.sqrMagnitude > 1e-4f) panel.rotation = Quaternion.Slerp(panel.rotation, Quaternion.LookRotation(away), k);
            placed = true;
        }

        /// <summary>Finish the current line now (N).</summary>
        public void SkipLine() => skip = IsSpeaking;

        /// <summary>The player character says (or thinks) a line: typed out low in the view, held, then cleared.
        /// `key` names an optional recording in Resources/VO/Chapter2.</summary>
        public IEnumerator Say(string text, string key = null, float hold = 1.8f, bool italic = false)
        {
            int id = ++lineId;
            IsSpeaking = true;
            skip = false;
            Debug.Log($"[You] {text}");
            AudioClip clip = string.IsNullOrEmpty(key) ? null : Resources.Load<AudioClip>("VO/Chapter2/" + key);
            if (clip != null) voice.PlayOneShot(clip);
            line.fontStyle = italic ? FontStyle.Italic : FontStyle.Normal;
            float shown = 0f;
            while (shown < text.Length && !skip)
            {
                shown += typeSpeed * Time.deltaTime;
                line.text = Wrap(text.Substring(0, Mathf.Min(text.Length, Mathf.FloorToInt(shown))), 40);
                yield return null;
                if (id != lineId) yield break;
            }
            line.text = Wrap(text, 40);
            float wait = Mathf.Max(hold, clip != null ? clip.length - text.Length / typeSpeed + 0.3f : 0f);
            for (float t = 0f; t < wait && !skip; t += Time.deltaTime)
            {
                yield return null;
                if (id != lineId) yield break;
            }
            if (skip) voice.Stop();
            skip = false;
            line.text = "";
            IsSpeaking = false;
        }

        /// <summary>A small prompt under the subtitle (what to do next); empty clears it.</summary>
        public void Hint(string text) => hint.text = Wrap(text ?? "", 46);

        /// <summary>A caption in the middle of the view (on a black screen): fades in, holds, fades out.</summary>
        public IEnumerator Caption(string text, float fadeIn, float hold, float fadeOut)
        {
            Debug.Log($"[Caption] {text.Replace('\n', ' ')}");
            caption.text = text;
            yield return FadeText(caption, 0f, 1f, fadeIn);
            for (float t = 0f; t < hold && !skip; t += Time.unscaledDeltaTime) yield return null;
            skip = false;
            yield return FadeText(caption, 1f, 0f, fadeOut);
            caption.text = "";
        }

        IEnumerator FadeText(TextMesh t, float from, float to, float seconds)
        {
            Color c = t.color;
            for (float s = 0f; s < seconds; s += Time.unscaledDeltaTime)
            {
                c.a = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, s / seconds));
                t.color = c;
                yield return null;
            }
            c.a = to;
            t.color = c;
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
    }
}
