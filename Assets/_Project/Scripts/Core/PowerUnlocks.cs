using System;
using System.Collections.Generic;
using ArcaneStrike.HandInput;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace ArcaneStrike.Core
{
    /// <summary>Everything the player can learn. New powers go at the end so saved Inspector lists keep their meaning.</summary>
    public enum Power
    {
        JyotiSeal,  // the glowing palm mandala (Spark Hands). Without it the player has ordinary hands.
        Grab,       // fist: hold the compressed heat / Tether
        Firebolt,   // the thrown orb
        Seal,       // hands together, pulled apart: the kitchen shockwave, summoning Ember
        Lumen,      // raised palm: the hand torch in the power cut
        Aegis,
        VoltBurst,
        Sunwheel,
        SparkBlade,
        Skyhammer,
        Strikes,    // bare-hand punches and swipes that damage enemies
        WeaverSword, // the Jyoti Blade: the sword from the Ascension, kept as chapter 1's prize after the Rift
    }

    /// <summary>
    /// Which powers the player has. The story unlocks them chapter by chapter, so early scenes (the attic)
    /// have none. GestureRecognizer, HandMotion, HandHitbox and SparkHands ask <see cref="Allows"/> before
    /// acting. With no PowerUnlocks in the scene everything is allowed, so test scenes keep working.
    /// Story scripts call SetChapter(n) or Unlock(power) when a beat grants something.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class PowerUnlocks : MonoBehaviour
    {
        [Serializable]
        public class Chapter
        {
            public string name;
            [Tooltip("Powers this chapter adds. Earlier chapters' powers stay unlocked.")]
            public Power[] unlocks = Array.Empty<Power>();
        }

        [Tooltip("In story order. A starting guess from the storyline; change freely.")]
        [SerializeField] Chapter[] chapters =
        {
            new Chapter { name = "Attic" },
            new Chapter { name = "Lantern Void", unlocks = new[] { Power.JyotiSeal, Power.Grab, Power.Firebolt } },
            new Chapter { name = "Kitchen", unlocks = new[] { Power.Seal } },
            new Chapter { name = "Power Cut", unlocks = new[] { Power.Lumen } },
            new Chapter { name = "Living Room" },
        };

        [Tooltip("Chapter the scene starts in (index into Chapters).")]
        [SerializeField] int startChapter;

        [Tooltip("Editor only: allow every power, e.g. for combat testing in SampleScene.")]
        [SerializeField] bool unlockAllInEditor;

        [Tooltip("Keyboard (GameInput): ] = next chapter, [ = previous chapter.")]
        [SerializeField] bool keyboardFallback = true;

        [Tooltip("Fired once per newly unlocked power with its name: hook tutorial prompts and Ember's lines here.")]
        [SerializeField] UnityEvent<string> onPowerUnlocked;

        static PowerUnlocks instance;
        readonly HashSet<Power> unlocked = new HashSet<Power>();

        public event Action<Power> OnUnlocked;
        public int CurrentChapter { get; private set; } = -1;
        public string CurrentChapterName => CurrentChapter >= 0 && CurrentChapter < chapters.Length ? chapters[CurrentChapter].name : "";

        /// <summary>True if the power is unlocked, or if the scene has no PowerUnlocks at all.</summary>
        public static bool Allows(Power power)
        {
            if (instance == null) return true;
            if (instance.unlockAllInEditor && Application.isEditor) return true;
            return instance.unlocked.Contains(power);
        }

        /// <summary>The power a gesture needs. End events always pass, so a held spell can always be released.</summary>
        public static bool Allows(GestureType gesture)
        {
            switch (gesture)
            {
                case GestureType.Firebolt: return Allows(Power.Firebolt);
                case GestureType.AegisStart: return Allows(Power.Aegis);
                case GestureType.Seal: return Allows(Power.Seal);
                case GestureType.VoltBurst: return Allows(Power.VoltBurst);
                case GestureType.Lumen: return Allows(Power.Lumen);
                case GestureType.Sunwheel: return Allows(Power.Sunwheel);
                case GestureType.GrabStart: return Allows(Power.Grab);
                case GestureType.BladeStart: return Allows(Power.SparkBlade);
                case GestureType.Skyhammer: return Allows(Power.Skyhammer);
                default: return true;
            }
        }

        void OnEnable()
        {
            if (instance != null && instance != this)
                Debug.LogWarning($"[Powers] More than one PowerUnlocks in the scene; using {name}.", this);
            instance = this;
        }

        void OnDisable()
        {
            if (instance == this) instance = null;
        }

        void Start() => SetChapter(startChapter);

        /// <summary>Moves to a chapter: unlocks its powers and every earlier chapter's. Callable from UnityEvents.</summary>
        public void SetChapter(int index)
        {
            if (chapters.Length == 0) return;
            index = Mathf.Clamp(index, 0, chapters.Length - 1);
            CurrentChapter = index;

            // Going back (debug keys) relocks later powers, so a chapter always plays with exactly its own set.
            var keep = new HashSet<Power>();
            for (int c = 0; c <= index; c++) keep.UnionWith(chapters[c].unlocks);
            unlocked.IntersectWith(keep);
            foreach (Power p in keep) Unlock(p);

            Debug.Log($"[Powers] Chapter {index} \"{CurrentChapterName}\": {(unlocked.Count == 0 ? "no powers" : string.Join(", ", unlocked))}");
        }

        public void NextChapter() => SetChapter(CurrentChapter + 1);

        public void Unlock(Power power)
        {
            if (!unlocked.Add(power)) return;
            Debug.Log($"[Powers] Unlocked {power}");
            OnUnlocked?.Invoke(power);
            onPowerUnlocked?.Invoke(power.ToString());
        }

        public bool IsUnlocked(Power power) => unlocked.Contains(power);

        void Update()
        {
            if (!keyboardFallback) return;
            if (GameInput.Pressed(GameAction.NextChapter)) NextChapter();
            if (GameInput.Pressed(GameAction.PreviousChapter)) SetChapter(CurrentChapter - 1);
        }
    }
}
