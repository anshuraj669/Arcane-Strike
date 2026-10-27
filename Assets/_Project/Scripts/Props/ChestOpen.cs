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

        [Tooltip("A palm closer than this to the handle (m) counts as holding the lid. Webcam depth is rough, so keep it generous.")]
        [SerializeField] float reach = 0.25f;

        [Tooltip("How far (m) the hand must rise while holding the lid to open it.")]
        [SerializeField] float liftDistance = 0.08f;

        [Tooltip("Editor / debug builds: press E to open, like the other keyboard fallbacks.")]
        [SerializeField] bool keyboardFallback = true;

        [SerializeField] UnityEvent onOpened;

        Quaternion closedRot;
        Quaternion openRot;
        bool opening;
        readonly float[] holdStartY = { float.NaN, float.NaN };

        public bool IsOpen { get; private set; }

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
        }

        public void OpenChest()
        {
            if (IsOpen || lid == null) return;
            IsOpen = true;
            opening = true;
            Debug.Log($"[ChestOpen] {name} opened");
            onOpened?.Invoke();
        }

        void Update()
        {
            if (opening) AnimateLid();
            if (IsOpen) return;

            if (input != null)
                for (int i = 0; i < 2; i++) UpdateHand(input.Get((HandSide)i), i);

            if (keyboardFallback && (Application.isEditor || Debug.isDebugBuild) &&
                Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                OpenChest();
        }

        void UpdateHand(HandFrame f, int i)
        {
            if (!f.IsTracked || Vector3.Distance(f.PalmCenter, handle.position) > reach)
            {
                holdStartY[i] = float.NaN;
                return;
            }

            float y = f.PalmCenter.y;
            if (float.IsNaN(holdStartY[i])) holdStartY[i] = y;
            else if (y < holdStartY[i]) holdStartY[i] = y; // measure the lift from the lowest point
            else if (y - holdStartY[i] >= liftDistance) OpenChest();
        }

        void AnimateLid()
        {
            lid.localRotation = Quaternion.RotateTowards(lid.localRotation, openRot, openSpeed * Time.deltaTime);
            if (Quaternion.Angle(lid.localRotation, openRot) < 0.1f)
            {
                lid.localRotation = openRot;
                opening = false;
            }
        }
    }
}
