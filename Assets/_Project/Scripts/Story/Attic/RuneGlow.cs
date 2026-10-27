using System.Collections;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Runes carved into the lantern light up: golden while it is cleaned, blue when the blood awakens it.
    /// Works on any URP Lit material by fading its emission.
    /// </summary>
    public class RuneGlow : MonoBehaviour
    {
        public Renderer[] runeRenderers;
        public float glowStrength = 5f;
        [SerializeField] Color goldColor = new Color(1f, 0.79f, 0.29f);
        [SerializeField] float fadeTime = 1.5f;

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        Color current = Color.black;
        Coroutine fading;

        /// <summary>The golden glow while cleaning.</summary>
        public void ActivateRunes() => Activate(goldColor, glowStrength);

        public void Activate(Color color, float strength)
        {
            if (fading != null) StopCoroutine(fading);
            fading = StartCoroutine(Fade(color * strength));
        }

        IEnumerator Fade(Color target)
        {
            Color from = current;
            for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(fadeTime, 0.01f))
            {
                Apply(Color.Lerp(from, target, t));
                yield return null;
            }
            Apply(target);
        }

        void Apply(Color c)
        {
            current = c;
            if (runeRenderers == null) return;
            foreach (Renderer r in runeRenderers)
            {
                if (r == null) continue;
                Material mat = r.material; // own copy, so other objects with this material stay dark
                mat.EnableKeyword("_EMISSION");
                mat.SetColor(EmissionColor, c);
            }
        }
    }
}
