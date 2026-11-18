using System.Text;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Voice clips for lines that have no hand-made recording: Resources/VO/Lines/&lt;speaker&gt;/&lt;key&gt;, where the key
    /// is made from the line's own words (<see cref="Key"/>), so any line said anywhere finds its clip with no table to
    /// keep up to date. The clips are made by Tools/tts/make_voices.py, which works the keys out the same way.
    /// A real recording passed in by a scene always wins over these.
    /// </summary>
    public static class VoiceBank
    {
        /// <summary>The clip for this speaker saying this line, or null.</summary>
        public static AudioClip Find(string speaker, string line)
        {
            if (string.IsNullOrEmpty(speaker) || string.IsNullOrWhiteSpace(line)) return null;
            return Resources.Load<AudioClip>($"VO/Lines/{speaker}/{Key(line)}");
        }

        /// <summary>"l" + the FNV-1a hash (32 bits, hex) of the line's UTF-8 bytes, trimmed.</summary>
        public static string Key(string line)
        {
            uint hash = 2166136261;
            foreach (byte b in Encoding.UTF8.GetBytes(line.Trim()))
            {
                hash ^= b;
                hash *= 16777619;
            }
            return "l" + hash.ToString("x8");
        }
    }
}
