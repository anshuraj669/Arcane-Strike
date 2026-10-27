using System;
using ArcaneStrike.HandInput;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The player rubs years of dust off the lantern with their free hand. Cleaning drives the first half
    /// of the awakening: dust puffs while rubbing, then (as it gets cleaner) a soft hum, a golden glow and
    /// glowing runes, and when fully clean the hum grows louder.
    /// A cleaning cloth or any other script can also call <see cref="AddCleaning"/>.
    /// </summary>
    public class LanternCleaning : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] LanternPickup pickup;

        [Header("Cleaning")]
        [SerializeField] float requiredCleaning = 100f;
        [Tooltip("A palm this close to the lantern (m) is rubbing it.")]
        [SerializeField] float cleanReach = 0.2f;
        [Tooltip("Slower palm movement (m/s) doesn't count as rubbing.")]
        [SerializeField] float minRubSpeed = 0.12f;
        [Tooltip("Cleaning per metre of rubbing. 40 means about 2.5 m of rubbing: roughly 6 s of brisk polishing.")]
        [SerializeField] float cleaningPerMetre = 40f;
        [Tooltip("Lantern centre, relative to its origin.")]
        [SerializeField] Vector3 centerOffset = new Vector3(0f, 0.12f, 0f);

        [Header("Looks: dirty swaps to clean when finished")]
        public GameObject dirtyLantern;
        public GameObject cleanLantern;

        [Header("Story beats while cleaning")]
        [SerializeField] LanternDustEffect dust;
        [SerializeField] LanternSound sound;
        [SerializeField] LanternGlow glow;
        [SerializeField] RuneGlow runes;
        [Range(0f, 1f)] [SerializeField] float humAt = 0.15f;
        [Range(0f, 1f)] [SerializeField] float glowAt = 0.4f;
        [Range(0f, 1f)] [SerializeField] float runesAt = 0.7f;

        [SerializeField] float currentCleaning;

        public float Progress => Mathf.Clamp01(currentCleaning / Mathf.Max(requiredCleaning, 0.01f));
        public bool IsCleaned { get; private set; }
        public event Action OnStarted, OnCleaned;

        bool active, started, humOn, glowOn, runesOn;
        int lastLoggedQuarter;
        readonly Vector3[] lastPalm = new Vector3[2];
        readonly bool[] hadPalm = new bool[2];

        Vector3 Center => transform.TransformPoint(centerOffset);

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            if (pickup == null) pickup = GetComponent<LanternPickup>();
            if (dust == null) dust = GetComponent<LanternDustEffect>();
            if (sound == null) sound = GetComponent<LanternSound>();
            if (glow == null) glow = GetComponent<LanternGlow>();
            if (runes == null) runes = GetComponent<RuneGlow>();
        }

        /// <summary>The lantern is in hand: rubbing it now counts.</summary>
        public void Begin() => active = true;

        public void CompleteNow() => AddCleaning(requiredCleaning);

        void Update()
        {
            if (!active || IsCleaned || input == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);

            for (int i = 0; i < 2; i++)
            {
                var side = (HandSide)i;
                HandFrame f = input.Get(side);
                // The hand holding the lantern can't polish it.
                bool free = pickup == null || !pickup.IsPickedUp || side != pickup.HeldBy;
                bool near = free && f.IsTracked && Vector3.Distance(f.PalmCenter, Center) < cleanReach;
                if (!near)
                {
                    hadPalm[i] = false;
                    continue;
                }

                if (hadPalm[i])
                {
                    float moved = Vector3.Distance(f.PalmCenter, lastPalm[i]);
                    if (moved / dt > minRubSpeed)
                    {
                        AddCleaning(moved * cleaningPerMetre);
                        if (dust != null) dust.Rub(Vector3.Lerp(f.PalmCenter, Center, 0.5f), moved / dt);
                    }
                }
                lastPalm[i] = f.PalmCenter;
                hadPalm[i] = true;
            }
        }

        public void AddCleaning(float amount)
        {
            if (IsCleaned || amount <= 0f) return;
            if (!started)
            {
                started = true;
                Debug.Log("[Attic] Cleaning started");
                OnStarted?.Invoke();
            }

            currentCleaning = Mathf.Min(currentCleaning + amount, requiredCleaning);
            float p = Progress;

            int quarter = Mathf.FloorToInt(p * 4f);
            if (quarter > lastLoggedQuarter && quarter < 4)
            {
                lastLoggedQuarter = quarter;
                Debug.Log($"[Attic] Cleaning {quarter * 25}%");
            }

            if (!humOn && p >= humAt)
            {
                humOn = true;
                Debug.Log("[Attic] Soft hum starts");
                if (sound != null) sound.StartHum();
            }
            if (!glowOn && p >= glowAt)
            {
                glowOn = true;
                Debug.Log("[Attic] Golden glow appears");
                if (glow != null) glow.StartGlow();
            }
            if (!runesOn && p >= runesAt)
            {
                runesOn = true;
                Debug.Log("[Attic] Runes activate");
                if (runes != null) runes.ActivateRunes();
            }
            if (sound != null && humOn) sound.SetIntensity(Mathf.InverseLerp(humAt, 1f, p) * 0.6f);
            if (glow != null && glowOn) glow.SetLevel(Mathf.Lerp(0.3f, 0.8f, Mathf.InverseLerp(glowAt, 1f, p)));

            if (currentCleaning >= requiredCleaning) CompleteCleaning();
        }

        void CompleteCleaning()
        {
            IsCleaned = true;
            if (dirtyLantern != null) dirtyLantern.SetActive(false);
            if (cleanLantern != null) cleanLantern.SetActive(true);
            if (dust != null) dust.Puff(Center);
            if (sound != null) sound.SetIntensity(1f);
            if (glow != null) glow.SetLevel(1f);
            Debug.Log("[Attic] Lantern fully cleaned: the hum grows louder");
            OnCleaned?.Invoke();
        }
    }
}
