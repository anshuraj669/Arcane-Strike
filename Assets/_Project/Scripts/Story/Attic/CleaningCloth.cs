using System.Collections;
using System.Collections.Generic;
using ArcaneStrike.HandInput;
using ArcaneStrike.Props;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The old rag in the chest (the original CleaningCloth / VRCleaningCloth). Pick it up (it is a
    /// <see cref="Grabbable"/>) and rub it over the lantern: the faster it moves, the faster the lantern
    /// cleans, and better than a bare hand. Only counts once the lantern is in hand (LanternCleaning is active).
    /// </summary>
    [RequireComponent(typeof(Grabbable))]
    public class CleaningCloth : MonoBehaviour
    {
        [SerializeField] LanternCleaning lantern;
        [Tooltip("The cloth's middle within this distance (m) of the lantern's centre is rubbing it.")]
        [SerializeField] float reach = 0.15f;
        [Tooltip("Slower movement (m/s) doesn't count as rubbing.")]
        [SerializeField] float minRubSpeed = 0.1f;
        [Tooltip("How much better than a bare hand.")]
        [SerializeField] float cleaningMultiplier = 2f;

        static readonly List<CleaningCloth> All = new List<CleaningCloth>();

        Grabbable grab;
        Collider body;
        Vector3 last;
        bool hadLast;

        public bool IsAutoCleaning { get; private set; }

        /// <summary>
        /// The cleaning played out without hands (N, or webcam testing): the left hand takes the cloth from the
        /// chest and rubs the lantern for `seconds`, so the dust, hum, golden glow and runes build up as they would.
        /// </summary>
        /// <returns>False when it can't be played out (no lantern, or the cloth is already in a hand).</returns>
        public bool AutoClean(float seconds = 4.5f)
        {
            if (IsAutoCleaning || lantern == null || grab.IsHeld || !isActiveAndEnabled) return false;
            StartCoroutine(AutoCleanRoutine(seconds));
            return true;
        }

        IEnumerator AutoCleanRoutine(float seconds)
        {
            IsAutoCleaning = true;
            var rb = GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            Transform head = Camera.main != null ? Camera.main.transform : null;

            // The left hand picks the cloth out of the chest.
            Vector3 from = transform.position;
            for (float t = 0f; t < 0.6f && head != null; t += Time.deltaTime)
            {
                Vector3 hand = head.position + StoryFx.Flat(head.forward) * 0.35f - StoryFx.Flat(head.right) * 0.2f + Vector3.down * 0.45f;
                transform.position = Vector3.Lerp(from, hand, Mathf.SmoothStep(0f, 1f, t / 0.6f));
                yield return null;
            }

            // Brisk rubbing across the front of the lantern.
            Vector3 previous = transform.position;
            for (float t = 0f; t < seconds && !lantern.IsCleaned; t += Time.deltaTime)
            {
                Vector3 c = lantern.Center;
                Vector3 toPlayer = head != null ? StoryFx.Flat(head.position - c) : Vector3.back;
                Vector3 across = Vector3.Cross(Vector3.up, toPlayer);
                float a = t * 11f;
                Vector3 p = c + toPlayer * 0.07f + across * (Mathf.Sin(a) * 0.07f) + Vector3.up * (Mathf.Sin(a * 0.5f) * 0.05f);
                transform.SetPositionAndRotation(p, Quaternion.LookRotation(c - p));
                float speed = Vector3.Distance(p, previous) / Mathf.Max(Time.deltaTime, 1e-4f);
                previous = p;
                lantern.Rub(Vector3.Lerp(p, c, 0.5f), lantern.Required / seconds * Time.deltaTime, speed);
                yield return null;
            }
            if (!lantern.IsCleaned) lantern.CompleteNow();

            // Done: the cloth drops.
            if (rb != null) rb.isKinematic = false;
            IsAutoCleaning = false;
        }

        /// <summary>A cleaning cloth in either hand, or null.</summary>
        public static CleaningCloth Held
        {
            get
            {
                foreach (CleaningCloth c in All)
                    if (c.grab.IsHeld) return c;
                return null;
            }
        }

        /// <summary>The middle of the cloth: where it rubs.</summary>
        public Vector3 RubPoint => body != null ? body.bounds.center : transform.position;

        /// <summary>True when this hand is holding a cleaning cloth.</summary>
        public static bool IsHeldBy(HandSide side)
        {
            foreach (CleaningCloth c in All)
                if (c.grab.IsHeld && c.grab.HeldBy == side) return true;
            return false;
        }

        void Awake()
        {
            grab = GetComponent<Grabbable>();
            body = GetComponentInChildren<Collider>();
            if (lantern == null) lantern = FindAnyObjectByType<LanternCleaning>();
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Update()
        {
            Vector3 p = body != null ? body.bounds.center : transform.position;
            if (lantern == null || !grab.IsHeld || !lantern.IsActive || Vector3.Distance(p, lantern.Center) > reach)
            {
                hadLast = false;
                return;
            }
            if (hadLast)
            {
                float moved = Vector3.Distance(p, last);
                float speed = moved / Mathf.Max(Time.deltaTime, 1e-4f);
                if (speed > minRubSpeed) lantern.Rub(Vector3.Lerp(p, lantern.Center, 0.5f), moved * lantern.CleaningPerMetre * cleaningMultiplier, speed);
            }
            last = p;
            hadLast = true;
        }
    }
}
