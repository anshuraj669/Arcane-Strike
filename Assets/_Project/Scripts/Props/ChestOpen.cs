using ArcaneStrike.HandInput;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace ArcaneStrike.Props
{
    /// <summary>
    /// The attic trunk that holds grandfather's lantern (chapter 1 opening). The player opens it the
    /// way they would in real life: reach a hand to the front edge of the lid and lift. No powers are
    /// involved, because the player has none yet. Hook the lantern reveal / next story beat to On Opened.
    /// The lid's pivot must sit on the hinge line, or it will spin around its centre instead.
    /// </summary>
    public class ChestOpen : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] Transform lid;

        [Tooltip("Empty at the front edge of the lid, where the player grabs it. Move it with the lid (make it a child).")]
        [SerializeField] Transform handle;

        [Tooltip("Degrees around the lid's local X axis, relative to its closed rotation. Flip the sign if it opens into the chest.")]
        [SerializeField] float openAngle = -110f;

        [Tooltip("Degrees per second.")]
        [SerializeField] float openSpeed = 220f;

        [Header("Motion")]
        [Tooltip("Code-driven lid motion. Untick when an Animator clip opens the lid instead (see StoryAnimations).")]
        [SerializeField] bool animateLid = true;

        [Tooltip("Something inside knocks: the closed lid jumps a little about every this many seconds (0 = never).")]
        [SerializeField] float idleRattleEvery = 5f;

        [Tooltip("Degrees the lid jumps when it rattles.")]
        [SerializeField] float rattleAngle = 4f;

        [Tooltip("The lid jiggles this long (s) before it swings open.")]
        [SerializeField] float rattleTime = 0.3f;

        [Tooltip("How far the lid swings past open before it settles back (0 = no bounce).")]
        [SerializeField] float overshoot = 1.4f;

        [Tooltip("A palm closer than this to the handle (m) counts as holding the lid. Webcam depth is rough, so keep it generous.")]
        [SerializeField] float reach = 0.25f;

        [Tooltip("How far (m) the hand must rise while holding the lid to open it.")]
        [SerializeField] float liftDistance = 0.08f;

        [Tooltip("Keyboard (GameInput): press E to open.")]
        [SerializeField] bool keyboardFallback = true;

        [SerializeField] UnityEvent onOpened;

        Quaternion closedRot;
        Quaternion openRot;
        bool opening;
        float openedAt, nextRattle, rattleStart = float.NegativeInfinity, shakeUntil;
        Quaternion restRotation;
        readonly float[] holdStartY = { float.NaN, float.NaN };
        HandSide? liftedBy;
        Story.AtticIntro intro;

        public bool IsOpen { get; private set; }
        /// <summary>The lid (it swings open about its hinge).</summary>
        public Transform Lid => lid;
        /// <summary>Shut fast: the lid won't lift (a box still thick with dust).</summary>
        public bool Locked { get; set; }
        /// <summary>E opens this box by itself (off when something else decides which box a key opens).</summary>
        public bool KeyboardFallback { get => keyboardFallback; set => keyboardFallback = value; }
        /// <summary>Raised as the lid starts to open.</summary>
        public event System.Action<ChestOpen> Opened;

        /// <summary>No knocking from inside (until <see cref="Rattle"/> is called).</summary>
        public void Quiet()
        {
            idleRattleEvery = 0f;
            shakeUntil = 0f;
        }

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            if (lid == null)
            {
                Debug.LogWarning($"[ChestOpen] {name} has no lid assigned.", this);
                enabled = false;
                return;
            }
            if (handle == null) handle = lid;
            closedRot = lid.localRotation;
            openRot = closedRot * Quaternion.Euler(openAngle, 0f, 0f);
            nextRattle = Time.time + idleRattleEvery;
            restRotation = transform.localRotation;
            intro = FindAnyObjectByType<Story.AtticIntro>();
        }

        /// <summary>
        /// Something inside wants out: the chest shakes hard for `seconds` (lid jumping, the whole chest rocking),
        /// then keeps knocking about every `knockEvery` seconds until it is opened.
        /// </summary>
        public void Rattle(float seconds, float knockEvery = 2.5f)
        {
            shakeUntil = Time.time + seconds;
            idleRattleEvery = knockEvery;
            nextRattle = shakeUntil + knockEvery;
        }

        public void OpenChest()
        {
            if (IsOpen || lid == null || Locked) return;
            IsOpen = true;
            opening = animateLid;
            openedAt = Time.time;
            // Opened mid-rock (the chest shakes after the intro): settle it, or it stays tilted for good.
            transform.localRotation = restRotation;
            // The lid's weight in the hand that lifted it (controllers only; bare hands feel nothing).
            Combat.CombatFx.Rumble(liftedBy, 0.35f, 0.12f);
            Debug.Log($"[ChestOpen] {name} opened");
            onOpened?.Invoke();
            Opened?.Invoke(this);
        }

        void Update()
        {
            if (opening) AnimateLid();
            if (IsOpen) return;
            if (animateLid) IdleRattle();

            if (input != null)
                for (int i = 0; i < 2; i++) UpdateHand(input.Get((HandSide)i), i);

            // Not while the intro walk plays: there E skips the walk, and must not open the chest in the same press.
            if (keyboardFallback && (intro == null || !intro.IsPlaying) && GameInput.Pressed(GameAction.Interact)) OpenChest();
        }

        void UpdateHand(HandFrame f, int i)
        {
            if (Locked || !f.IsTracked || Vector3.Distance(f.PalmCenter, handle.position) > reach)
            {
                holdStartY[i] = float.NaN;
                return;
            }

            float y = f.PalmCenter.y;
            if (float.IsNaN(holdStartY[i])) holdStartY[i] = y;
            else if (y < holdStartY[i]) holdStartY[i] = y; // measure the lift from the lowest point
            else if (y - holdStartY[i] >= liftDistance)
            {
                liftedBy = (HandSide)i;
                OpenChest();
            }
        }

        // Knock, knock: every few seconds the closed lid jumps twice, as if something inside wants out.
        void IdleRattle()
        {
            float now = Time.time;
            if (now < shakeUntil)
            {
                float left = shakeUntil - now;
                lid.localRotation = Lifted(rattleAngle * 2f * Mathf.Abs(Mathf.Sin(now * 31f)) * Mathf.Min(1f, left));
                transform.localRotation = restRotation * Quaternion.Euler(0f, Mathf.Sin(now * 23f) * 1.5f, Mathf.Sin(now * 37f) * 1.2f);
                return;
            }
            transform.localRotation = restRotation;
            if (idleRattleEvery <= 0f) return;
            if (now >= nextRattle)
            {
                rattleStart = now;
                nextRattle = now + idleRattleEvery * Random.Range(0.7f, 1.3f);
            }
            float t = now - rattleStart;
            lid.localRotation = t < 0.25f ? Lifted(rattleAngle * 0.6f * Mathf.Abs(Mathf.Sin(t * 25f))) : closedRot;
        }

        // A quick jiggle, then the lid swings open, overshoots and settles.
        void AnimateLid()
        {
            float t = Time.time - openedAt;
            if (t < rattleTime)
            {
                lid.localRotation = Lifted(rattleAngle * Mathf.Abs(Mathf.Sin(t * 45f)));
                return;
            }
            float u = Mathf.Clamp01((t - rattleTime) * openSpeed / Mathf.Max(Mathf.Abs(openAngle), 1f));
            lid.localRotation = Quaternion.SlerpUnclamped(closedRot, openRot, EaseOutBack(u));
            if (u >= 1f)
            {
                lid.localRotation = openRot;
                opening = false;
            }
        }

        // The closed lid raised by this many degrees, in the opening direction.
        Quaternion Lifted(float degrees) => closedRot * Quaternion.Euler(Mathf.Sign(openAngle) * degrees, 0f, 0f);

        float EaseOutBack(float u)
        {
            float k = overshoot, v = u - 1f;
            return 1f + (k + 1f) * v * v * v + k * v * v;
        }
    }
}
