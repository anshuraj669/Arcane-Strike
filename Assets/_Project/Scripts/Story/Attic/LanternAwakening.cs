using System;
using System.Collections;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The blood has touched the lantern. Plays the awakening in order:
    /// blue energy burst → whisper voices → books float → the attic shakes → the portal opens.
    /// The lantern also slips out of the player's hand and floats.
    /// </summary>
    public class LanternAwakening : MonoBehaviour
    {
        [Header("Lantern")]
        [SerializeField] Transform lantern;
        [SerializeField] LanternPickup pickup;
        [SerializeField] LanternGlow glow;
        [SerializeField] RuneGlow runes;
        [SerializeField] LanternSound sound;
        [SerializeField] Color energyColor = new Color(0.3f, 0.6f, 1f, 1f);
        [SerializeField] int burstSparks = 160;

        [Header("The attic reacts")]
        [SerializeField] WhisperVoices whispers;
        [Tooltip("Optional root that is switched on when the books float (like the original floatingSystem).")]
        public GameObject floatingSystem;
        public FloatingObject[] floatingObjects;
        [SerializeField] AtticShake shake;
        [SerializeField] PortalController portal;

        [Header("Timing (seconds after the blood touches the lantern)")]
        [SerializeField] float whispersAt = 1.5f;
        [SerializeField] float booksAt = 3f;
        [Tooltip("Gap between one book lifting and the next.")]
        [SerializeField] float bookStagger = 0.25f;
        [SerializeField] float shakeAt = 6f;
        [SerializeField] float portalAt = 8.5f;

        public event Action OnPortalOpened;
        public bool IsAwakened { get; private set; }

        ParticleSystem burst;
        bool skip;

        Vector3 LanternCenter => (lantern != null ? lantern.position : transform.position) + Vector3.up * 0.12f;

        void Start()
        {
            burst = StoryFx.CreateParticles(transform, "EnergyBurst", true, -0.05f, 800);
        }

        public void TriggerAwakening()
        {
            if (IsAwakened) return;
            IsAwakened = true;
            StartCoroutine(Awaken());
        }

        /// <summary>Debug: run the remaining beats straight away.</summary>
        public void Skip() => skip = true;

        IEnumerator Awaken()
        {
            float start = Time.time;

            // Blue energy burst
            Debug.Log("[Attic] The lantern awakens: blue energy burst");
            Vector3 c = LanternCenter;
            StoryFx.Burst(burst, c, burstSparks, 2.2f, 0.06f, 1.2f, energyColor);
            StoryFx.Burst(burst, c, burstSparks / 4, 0.6f, 0.25f, 0.5f, StoryFx.WithAlpha(Color.white, 0.8f));
            if (glow != null)
            {
                glow.Flash(energyColor, 3f);
                glow.SetColor(energyColor);
                glow.SetLevel(1f);
            }
            if (runes != null) runes.Activate(energyColor, 6f);
            if (sound != null)
            {
                sound.SetIntensity(1f);
                sound.Swell();
            }
            if (pickup != null) pickup.Release();
            var lanternFloat = lantern != null ? lantern.GetComponent<FloatingObject>() : null;
            if (lanternFloat != null) lanternFloat.Activate();

            yield return Until(start + whispersAt);
            if (whispers != null) whispers.Play();

            yield return Until(start + booksAt);
            Debug.Log("[Attic] Books float");
            if (floatingSystem != null) floatingSystem.SetActive(true);
            if (floatingObjects != null)
                foreach (FloatingObject book in floatingObjects)
                {
                    if (book == null) continue;
                    book.Activate();
                    if (!skip) yield return new WaitForSeconds(bookStagger);
                }

            yield return Until(start + shakeAt);
            if (shake != null) shake.Shake();

            yield return Until(start + portalAt);
            if (portal != null) portal.Open();
            OnPortalOpened?.Invoke();
        }

        IEnumerator Until(float time)
        {
            while (Time.time < time && !skip) yield return null;
        }
    }
}
