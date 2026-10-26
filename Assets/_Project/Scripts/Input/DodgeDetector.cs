using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ArcaneStrike.HandInput
{
    public enum DodgeType { Left, Right, Duck }

    public struct DodgeEvent
    {
        public DodgeType Type;
        public float Time;
        public bool FromKeyboard;

        public override string ToString() => $"Dodge {Type}{(FromKeyboard ? " (key)" : "")}";
    }

    /// <summary>
    /// Upper-body defence: lean left/right or duck to dodge. Reads only the head position, so it works on
    /// Quest (the headset tracks the head perfectly) and in the editor (WebcamHeadDriver moves the head).
    /// Distances are small on purpose: every dodge works seated (contest "airplane seat" guideline).
    /// Enemies check <see cref="IsDodging"/> when their attack lands.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public class DodgeDetector : MonoBehaviour
    {
        [SerializeField] HandInputService input;

        [Tooltip("Sideways head move (m) away from your resting position that counts as a lean dodge.")]
        [SerializeField] float leanDistance = 0.15f;
        [Tooltip("Downward head move (m) that counts as a duck.")]
        [SerializeField] float duckDistance = 0.15f;
        [Tooltip("The move must be at least this fast (m/s), so slowly shifting in your seat doesn't count.")]
        [SerializeField] float minSpeed = 0.5f;
        [Tooltip("How long a dodge protects you after it starts.")]
        [SerializeField] float dodgeWindow = 0.45f;
        [SerializeField] float cooldown = 0.5f;
        [Tooltip("How quickly the resting position follows you (per second). Low = dodges must return to centre.")]
        [SerializeField] float restFollow = 0.8f;

        [Header("Debug")]
        [Tooltip("Left/Right arrow = lean, Down arrow = duck. Editor and development builds only.")]
        [SerializeField] bool keyboardFallback = true;
        [SerializeField] bool logDodges = true;

        public event Action<DodgeEvent> OnDodge;
        public DodgeEvent? LastDodge { get; private set; }

        /// <summary>True for <see cref="dodgeWindow"/> seconds after a dodge starts.</summary>
        public bool IsDodging => LastDodge.HasValue && Time.time - LastDodge.Value.Time < dodgeWindow;
        public bool IsDodgingFrom(DodgeType type) => IsDodging && LastDodge.Value.Type == type;

        Vector3 rest, lastPos;
        bool hasRest;
        float lastDodge = -10f;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
        }

        void Update()
        {
            Transform head = input != null ? input.Head : null;
            if (head == null) return;

            float now = Time.time, dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 pos = head.position;
            if (!hasRest)
            {
                rest = lastPos = pos;
                hasRest = true;
            }

            Vector3 velocity = (pos - lastPos) / dt;
            lastPos = pos;
            rest = Vector3.Lerp(rest, pos, 1f - Mathf.Exp(-restFollow * dt));

            Vector3 right = Vector3.ProjectOnPlane(head.right, Vector3.up).normalized;
            Vector3 offset = pos - rest;
            float sideways = Vector3.Dot(offset, right);
            float sideSpeed = Vector3.Dot(velocity, right);

            if (now - lastDodge > cooldown)
            {
                if (offset.y < -duckDistance && velocity.y < -minSpeed) Raise(DodgeType.Duck, now);
                else if (sideways < -leanDistance && sideSpeed < -minSpeed) Raise(DodgeType.Left, now);
                else if (sideways > leanDistance && sideSpeed > minSpeed) Raise(DodgeType.Right, now);
            }

            if (keyboardFallback && (Application.isEditor || Debug.isDebugBuild)) UpdateKeyboard(now);
        }

        void UpdateKeyboard(float now)
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (kb.leftArrowKey.wasPressedThisFrame) Raise(DodgeType.Left, now, true);
            if (kb.rightArrowKey.wasPressedThisFrame) Raise(DodgeType.Right, now, true);
            if (kb.downArrowKey.wasPressedThisFrame) Raise(DodgeType.Duck, now, true);
        }

        void Raise(DodgeType type, float now, bool fromKeyboard = false)
        {
            lastDodge = now;
            var e = new DodgeEvent { Type = type, Time = now, FromKeyboard = fromKeyboard };
            LastDodge = e;
            if (logDodges) Debug.Log($"[Dodge] {e}");
            OnDodge?.Invoke(e);
        }
    }
}
