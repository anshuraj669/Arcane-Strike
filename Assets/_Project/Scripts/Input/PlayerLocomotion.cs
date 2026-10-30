using ArcaneStrike.Story;
using UnityEngine;
using UnityEngine.XR;

namespace ArcaneStrike.HandInput
{
    /// <summary>
    /// Walking in the game without walking in the room. Real walking always works; with Smooth Locomotion on,
    /// the player can also move with:
    /// - controllers: left thumbstick walks (the way you look), right thumbstick turns in 30° steps;
    /// - hands: point with the right index finger (other fingers curled) and you walk the way it points
    ///   (the left index finger grabs things instead, see PointGrab);
    /// - keyboard (<see cref="GameInput"/>): W A S D walk, Z / X turn, hold the right mouse button and move the mouse
    ///   to look, Space jumps.
    /// L switches smooth locomotion on and off. Walls stop you, and you follow floors and stairs up and down. A jump
    /// rises and falls under gravity (stopping under a ceiling) and lands back on the floor.
    /// Moves the whole camera rig. Paused while something else drives the player (the intro). In a headset the edge of
    /// the view narrows (<see cref="ComfortVignette"/>) while walking, turning or being shoved, where the eyes see
    /// motion the body doesn't feel.
    /// </summary>
    public class PlayerLocomotion : MonoBehaviour
    {
        [Tooltip("Walk with the thumbstick, a pointing hand or the keyboard, not only by really walking.")]
        public bool smoothLocomotion = true;
        [SerializeField] HandInputService input;
        [Tooltip("m/s. A gentle pace is the most comfortable in a headset.")]
        [SerializeField] float speed = 1.3f;
        [SerializeField] float snapTurn = 30f;
        [Tooltip("Body radius for bumping into walls (m).")]
        [SerializeField] float radius = 0.2f;
        [Tooltip("Steps up to this height (m) are climbed; anything higher is a wall.")]
        [SerializeField] float stepHeight = 0.35f;
        [Tooltip("Eye height used when no headset is running (the editor without the Simulator).")]
        [SerializeField] float desktopEyeHeight = 1.6f;
        [Tooltip("Point with the right index finger to walk (hand tracking). The left one grabs (PointGrab).")]
        [SerializeField] bool pointToWalk = true;
        [SerializeField] float mouseLook = 0.15f;
        [Tooltip("How high a jump lifts you (m).")]
        [SerializeField] float jumpHeight = 0.45f;
        [SerializeField] float gravity = 12f;
        [Tooltip("Switch off Meta's own FirstPersonLocomotor in the camera rig. It pins the player to its invisible " +
                 "capsule every frame (dropping the eyes to the floor without a headset, and catching on doors and the " +
                 "chest) and would also walk on the thumbstick, fighting this script.")]
        [SerializeField] bool replaceMetaLocomotor = true;
        [Header("Comfort (headset only)")]
        [Tooltip("How far the edge of the view darkens while smooth walking or turning (0 = off).")]
        [Range(0f, 1f)] [SerializeField] float comfortVignette = 0.4f;
        [Tooltip("A blow knocks a player in a headset back this much of the way (the head moving on its own is the " +
                 "least comfortable motion there is). Moves the player chose, like a lunge, always go the full way.")]
        [Range(0f, 1f)] [SerializeField] float headsetKnockback = 0.6f;

        /// <summary>Something else is moving the player (the intro): don't walk.</summary>
        public bool Paused { get; set; }
        /// <summary>Held or stunned in a fight: no walking, turning still works.</summary>
        public bool Stunned { get; set; }
        /// <summary>Walking speed multiplier (slowed by venom, say).</summary>
        public float SpeedScale { get; set; } = 1f;
        /// <summary>In the air from a jump (clear of low sweeps).</summary>
        public bool Airborne => airborne && jumpOffset > 0.12f;

        /// <summary>Pushes the player across the floor (a blow knocks them back), stopping at walls.</summary>
        public void Shove(Vector3 delta)
        {
            if (head == null || Paused) return;
            delta.y = 0f;
            if (Headset)
            {
                delta *= headsetKnockback;
                ComfortVignette.Moving(comfortVignette * 1.4f, 0.15f);
            }
            Walk(delta);
        }

        /// <summary>Moves the player across the floor in a move they made themselves (a lunge), stopping at walls.</summary>
        public void Carry(Vector3 delta)
        {
            if (head == null || Paused) return;
            delta.y = 0f;
            if (Headset) ComfortVignette.Moving(comfortVignette, 0.1f);
            Walk(delta);
        }

        /// <summary>Everything this script has moved the rig by, ever (walking, shoves, jumps): what of the head's
        /// movement wasn't the player's own body (<see cref="DodgeDetector"/> leaves it out).</summary>
        public static Vector3 Moved { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Moved = Vector3.zero;

        Transform head, rig;
        bool turnHeld;
        float pointSince = -1f, pitch;
        float jumpOffset, jumpSpeed; // height above the floor while in the air, and how fast it is changing
        bool airborne;
        readonly RaycastHit[] hits = new RaycastHit[16];

        // Is a headset tracking the player's real height? Measured, not asked: XRSettings.isDeviceActive can
        // flip on part-way through in the editor without a headset.
        bool Headset => head != null && rig != null && head.position.y - rig.position.y > 0.3f;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            if (replaceMetaLocomotor) DisableMetaLocomotor();
        }

        void Start()
        {
            if (replaceMetaLocomotor) DisableMetaLocomotor(); // in case it woke up after us
        }

        static void DisableMetaLocomotor()
        {
            // By name, so this script needs no reference to the Interaction SDK.
            foreach (var b in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (b == null || !b.enabled || b.GetType().Name != "FirstPersonLocomotor") continue;
                b.enabled = false;
                Debug.Log($"[Move] Switched off Meta's FirstPersonLocomotor on '{b.gameObject.name}': PlayerLocomotion moves the player here.");
            }
        }

        void Update()
        {
            if (head == null)
            {
                head = input != null && input.Head != null ? input.Head : Camera.main != null ? Camera.main.transform : null;
                if (head == null) return;
                rig = head.root;
            }
            if (GameInput.Pressed(GameAction.ToggleSmoothMove))
            {
                smoothLocomotion = !smoothLocomotion;
                Debug.Log($"[Move] Smooth locomotion {(smoothLocomotion ? "on" : "off")} ({GameInput.KeyName(GameAction.ToggleSmoothMove)}).");
            }
            if (Paused)
            {
                airborne = false; // something else is moving the player: drop any jump where it is
                jumpOffset = jumpSpeed = 0f;
                return;
            }
            UpdateJump();
            if (!smoothLocomotion) return;

            Vector2 move = Vector2.zero;
            float turn = 0f;

            // Controllers.
            if (InputDevices.GetDeviceAtXRNode(XRNode.LeftHand).TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 stick) && stick.magnitude > 0.15f)
                move = stick;
            if (InputDevices.GetDeviceAtXRNode(XRNode.RightHand).TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 turnStick))
            {
                if (Mathf.Abs(turnStick.x) > 0.7f && !turnHeld) turn = Mathf.Sign(turnStick.x) * snapTurn;
                turnHeld = Mathf.Abs(turnStick.x) > 0.5f;
            }

            // Keyboard and mouse.
            Vector2 keys = GameInput.MoveAxis();
            if (keys != Vector2.zero) move = keys;
            if (GameInput.Pressed(GameAction.TurnLeft)) turn = -snapTurn;
            if (GameInput.Pressed(GameAction.TurnRight)) turn = snapTurn;
            if (!Headset && GameInput.Held(GameAction.Look))
            {
                Vector2 delta = GameInput.MouseDelta() * mouseLook;
                rig.RotateAround(head.position, Vector3.up, delta.x);
                pitch = Mathf.Clamp(pitch - delta.y, -70f, 70f);
                head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            // Pointing hand: walk where the right index finger points.
            Vector3 direction = Vector3.zero;
            if (move != Vector2.zero)
            {
                Vector3 forward = Flat(head.forward), right = Flat(head.right);
                direction = forward * move.y + right * move.x;
            }
            else if (pointToWalk && input != null && IsPointing(input.Get(HandSide.Right)))
            {
                if (pointSince < 0f) pointSince = Time.time;
                HandFrame f = input.Get(HandSide.Right);
                if (Time.time - pointSince > 0.25f) direction = Flat(f[HandJoint.IndexTip] - f[HandJoint.IndexProximal]);
            }
            else
            {
                pointSince = -1f;
            }

            if (turn != 0f)
            {
                rig.RotateAround(head.position, Vector3.up, turn);
                if (Headset) ComfortVignette.Moving(comfortVignette, 0.12f); // (a blink of narrowing over the snap)
            }
            if (direction.sqrMagnitude > 1e-4f && !Stunned)
            {
                Walk(Vector3.ClampMagnitude(direction, 1f) * speed * SpeedScale * Time.deltaTime);
                if (Headset) ComfortVignette.Moving(comfortVignette);
            }
        }

        // Space (or a gesture driving GameAction.Jump): up under gravity, and back down onto the floor.
        void UpdateJump()
        {
            if (!airborne && GameInput.Pressed(GameAction.Jump))
            {
                airborne = true;
                jumpSpeed = Mathf.Sqrt(2f * gravity * jumpHeight);
            }
            if (!airborne) return;
            float dt = Time.deltaTime;
            jumpSpeed -= gravity * dt;
            float step = jumpSpeed * dt;
            // A ceiling stops the rise.
            if (step > 0f && Physics.SphereCast(head.position, 0.12f, Vector3.up, out _, step + 0.15f, ~0, QueryTriggerInteraction.Ignore))
            {
                jumpSpeed = 0f;
                step = 0f;
            }
            if (jumpOffset + step <= 0f) // landed
            {
                step = -jumpOffset;
                airborne = false;
                jumpSpeed = 0f;
            }
            jumpOffset += step;
            rig.position += Vector3.up * step;
            Moved += Vector3.up * step;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.zero;
        }

        static bool IsPointing(HandFrame f) =>
            f.IsTracked && f.Straightness(Finger.Index) > 0.9f && f.Straightness(Finger.Middle) < 0.55f &&
            f.Straightness(Finger.Ring) < 0.55f && f.Straightness(Finger.Pinky) < 0.6f;

        // ---------------------------------------------------------------- moving with walls and floors

        void Walk(Vector3 delta)
        {
            float floor = FloorBelow(head.position, out bool found);
            if (!found) floor = rig.position.y - (Headset ? 0f : desktopEyeHeight);
            Vector3 body = new Vector3(head.position.x, floor + stepHeight + radius, head.position.z);

            // Slide along walls: up to two passes (into a corner).
            for (int pass = 0; pass < 2 && delta.sqrMagnitude > 1e-8f; pass++)
            {
                if (!Blocked(body, delta, out RaycastHit hit)) break;
                Vector3 normal = Flat(hit.normal);
                delta -= normal * Vector3.Dot(delta, normal);
                delta = Vector3.ClampMagnitude(delta, Mathf.Max(0f, hit.distance - 0.01f) + delta.magnitude * 0.5f);
            }
            if (Blocked(body, delta, out _)) return;

            Vector3 p = rig.position + delta;
            float newFloor = FloorBelow(head.position + delta, out bool onFloor);
            if (onFloor && Mathf.Abs(newFloor - floor) <= stepHeight + 0.05f) p.y += newFloor - floor; // stairs and slopes
            else if (onFloor && newFloor < floor - stepHeight) return; // don't walk off an edge
            Moved += p - rig.position;
            rig.position = p;
        }

        bool Blocked(Vector3 body, Vector3 delta, out RaycastHit nearest)
        {
            nearest = default;
            float distance = delta.magnitude;
            if (distance < 1e-5f) return false;
            int n = Physics.SphereCastNonAlloc(body, radius, delta / distance, hits, distance + 0.02f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (!IsWorld(hits[i].collider) || hits[i].distance <= 0f) continue;
                if (hits[i].distance < best)
                {
                    best = hits[i].distance;
                    nearest = hits[i];
                }
            }
            return best < float.MaxValue;
        }

        float FloorBelow(Vector3 from, out bool found)
        {
            int n = Physics.RaycastNonAlloc(from + Vector3.up * 0.1f, Vector3.down, hits, 4f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MinValue;
            for (int i = 0; i < n; i++)
                if (IsWorld(hits[i].collider) && hits[i].normal.y > 0.6f && hits[i].point.y > best && hits[i].point.y < from.y - 0.3f)
                    best = hits[i].point.y;
            found = best > float.MinValue;
            return best;
        }

        // Walls and floors only: loose props, hands and the player's own bits don't stop you.
        bool IsWorld(Collider c) => c != null && c.attachedRigidbody == null && !c.transform.IsChildOf(rig);
    }
}
