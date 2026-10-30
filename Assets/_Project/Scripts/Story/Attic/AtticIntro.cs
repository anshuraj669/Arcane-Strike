using System;
using System.Collections;
using ArcaneStrike.HandInput;
using ArcaneStrike.Props;
using ArcaneStrike.VFX;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The walk in, at midnight. The player starts outside the old house and is carried along a path: up the
    /// porch, through the front door (it swings open), across the hall, up the stairs to the loft and through the
    /// castle gate (both doors swing open) into the store room, where the chest is shaking. Then the story
    /// (AtticGameManager) takes over and the player can walk on their own (PlayerLocomotion).
    /// Moves the whole camera rig gently (a slow walk, slow turns) and fades in from black. Space or a
    /// controller's B / Y button skips to the store room. Waypoints are in the attic's space (attic floor y 0).
    /// </summary>
    public class AtticIntro : MonoBehaviour
    {
        [Serializable]
        public class Waypoint
        {
            [Tooltip("Where the player's feet go (attic space).")]
            public Vector3 position;
            [Tooltip("Open this door (by name) on arriving, and wait while it swings.")]
            public string openDoor;
            [Tooltip("Seconds to stand still here.")]
            public float pause;
        }

        [SerializeField] bool playIntro = true;
        [SerializeField] HandInputService input;
        [SerializeField] ChestOpen chest;
        [Tooltip("Walking speed (m/s). Slow is comfortable in a headset.")]
        [SerializeField] float speed = 0.9f;
        [Tooltip("Turning speed (degrees/s). Slow is comfortable in a headset.")]
        [SerializeField] float turnSpeed = 45f;
        [Tooltip("Eye height used when no headset is running (the editor without the Simulator).")]
        [SerializeField] float desktopEyeHeight = 1.6f;
        [SerializeField] float fadeInTime = 2.5f;
        [Tooltip("Where the doors swing towards (inside the house).")]
        [SerializeField] Vector3 insideHouse = new Vector3(0f, -2f, -4f);
        [SerializeField] Vector3 insideAttic = new Vector3(0f, 1f, 1.5f);

        public Waypoint[] path =
        {
            new Waypoint { position = new Vector3(0f, -3.65f, -22f), pause = 2.5f },   // the yard, looking at the house
            new Waypoint { position = new Vector3(0f, -3.65f, -10.6f) },               // the foot of the porch steps
            new Waypoint { position = new Vector3(0f, -3.2f, -9.6f) },                 // up onto the porch
            new Waypoint { position = new Vector3(0f, -3.2f, -8.2f), openDoor = "House_FrontDoor", pause = 0.4f },
            new Waypoint { position = new Vector3(0f, -3.2f, -6.7f) },                 // into the hall
            new Waypoint { position = new Vector3(0.7f, -3.2f, -6.1f) },               // the foot of the stairs
            new Waypoint { position = new Vector3(0.7f, 0f, -2.8f) },                  // up the stairs to the loft
            new Waypoint { position = new Vector3(0.3f, 0f, -2.45f), openDoor = "Gate_Left,Gate_Right", pause = 0.6f },
            new Waypoint { position = new Vector3(0f, 0f, -1.0f) },                    // through the gate
            new Waypoint { position = new Vector3(0f, 0f, 0.15f), pause = 0.3f },      // in front of the chest
        };

        public bool IsPlaying { get; private set; }
        public event Action OnFinished;

        Transform head, rig;
        AtticAtmosphere atmosphere;
        PlayerLocomotion locomotion;
        Material fadeMaterial;
        Renderer fadeRenderer;
        float fade = 1f, stepDistance;
        bool skip;
        Coroutine fading;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            if (chest == null) chest = FindAnyObjectByType<ChestOpen>();
            atmosphere = GetComponent<AtticAtmosphere>();
            locomotion = FindAnyObjectByType<PlayerLocomotion>();
        }

        IEnumerator Start()
        {
            yield return null; // let the rig and the hands initialise
            head = StoryFx.Head(input);
            if (!playIntro || head == null || path.Length == 0)
            {
                Finish();
                yield break;
            }
            rig = head.root;
            if (!HeadsetActive) head.localRotation = Quaternion.identity; // editor: start looking level
            IsPlaying = true;
            if (locomotion != null) locomotion.Paused = true;
            BuildFade();
            Debug.Log("[Attic] Intro: midnight, outside the old house. Space (or B / Y) skips to the store room.");

            // Start in the yard, looking along the path.
            Vector3 first = World(path[0].position), next = World(path[Mathf.Min(1, path.Length - 1)].position);
            PlaceHead(first, 0f);
            TurnTowards(next - first, 360f);
            Debug.Log($"[Attic] Intro starts at {transform.InverseTransformPoint(head.position):F2} (attic space), " +
                      $"looking {transform.InverseTransformDirection(head.forward):F2}; the house is ahead (+Z).");
            fading = StartCoroutine(FadeTo(0f, fadeInTime));

            for (int i = 0; i < path.Length && !skip; i++)
            {
                Waypoint w = path[i];
                if (i > 0) yield return WalkTo(World(path[i - 1].position), World(w.position));
                if (!string.IsNullOrEmpty(w.openDoor)) yield return OpenDoors(w.openDoor);
                for (float t = 0f; t < w.pause && !skip; t += Time.deltaTime) yield return null;
            }

            if (HeadsetActive) ComfortVignette.Set(0f);
            if (skip)
            {
                // The slow fade-in may still be running: stop it, or it fights this fade and the jump shows.
                if (fading != null) StopCoroutine(fading);
                yield return FadeTo(1f, 0.3f);
                foreach (Waypoint w in path)
                    if (!string.IsNullOrEmpty(w.openDoor))
                        foreach (string name in w.openDoor.Split(','))
                            SwingOpen(name.Trim(), 1f);
                PlaceHead(World(path[path.Length - 1].position), 0f);
                TurnTowards(transform.TransformDirection(Vector3.forward), 360f);
                yield return FadeTo(0f, 0.5f);
            }
            else
            {
                // Face the chest for the moment it starts to shake.
                Vector3 target = chest != null ? chest.transform.position : World(path[path.Length - 1].position) + transform.forward;
                for (float t = 0f; t < 1.5f; t += Time.deltaTime)
                {
                    TurnTowards(target - head.position, turnSpeed);
                    yield return null;
                }
            }
            Finish();
        }

        void Update()
        {
            if (!IsPlaying || skip) return;
            bool key = GameInput.Pressed(GameAction.Skip) || GameInput.Pressed(GameAction.Interact);
            bool button = ButtonDown(XRNode.RightHand) || ButtonDown(XRNode.LeftHand);
            if (key || button)
            {
                skip = true;
                Debug.Log("[Attic] Intro skipped.");
            }
        }

        static bool ButtonDown(XRNode node) =>
            InputDevices.GetDeviceAtXRNode(node).TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool pressed) && pressed;

        void Finish()
        {
            IsPlaying = false;
            if (locomotion != null) locomotion.Paused = false;
            if (rig != null && HeadsetActive) ComfortVignette.Set(0f);
            if (fadeMaterial != null && fade > 0f)
            {
                if (fading != null) StopCoroutine(fading);
                fading = StartCoroutine(FadeTo(0f, 0.3f));
            }
            if (chest != null && !chest.IsOpen)
            {
                chest.Rattle(2.5f);
                if (atmosphere != null) atmosphere.PlayCreak(chest.transform.position + Vector3.up * 0.3f, 0.8f, 0.6f);
            }
            Debug.Log("[Attic] The chest is shaking. Walk up and open it (thumbstick, pointing hand or WASD to walk; E or N opens it). " +
                      (head != null ? $"Eyes at {transform.InverseTransformPoint(head.position):F2} (attic space)." : ""));
            OnFinished?.Invoke();
        }

        // ---------------------------------------------------------------- moving the player

        Vector3 World(Vector3 local) => transform.TransformPoint(local);

        // Is a headset tracking the player's real height? Measured, not asked: XRSettings.isDeviceActive can
        // flip on part-way through in the editor without a headset, which dropped the camera to the floor.
        bool HeadsetActive => head != null && rig != null && head.position.y - rig.position.y > 0.3f;

        /// <summary>Moves the rig so the player's eyes are above `feet` (keeping their real height in a headset).</summary>
        void PlaceHead(Vector3 feet, float bob)
        {
            Vector3 p = rig.position;
            p.x += feet.x - head.position.x;
            p.z += feet.z - head.position.z;
            p.y = feet.y + (HeadsetActive ? 0f : desktopEyeHeight) + bob;
            rig.position = p;
        }

        void TurnTowards(Vector3 direction, float maxDegreesPerSecond)
        {
            // Headset comfort: never turn the view smoothly (the inner ear feels no turn, which is what makes people
            // sick); the player turns their own head. Only the instant turns, made while the view is black, apply.
            if (HeadsetActive && maxDegreesPerSecond < 360f) return;
            direction.y = 0f;
            Vector3 forward = head.forward;
            forward.y = 0f;
            if (direction.sqrMagnitude < 1e-4f || forward.sqrMagnitude < 1e-4f) return;
            float angle = Vector3.SignedAngle(forward, direction, Vector3.up);
            float step = maxDegreesPerSecond >= 360f ? angle : Mathf.Clamp(angle, -maxDegreesPerSecond * Time.deltaTime, maxDegreesPerSecond * Time.deltaTime);
            rig.RotateAround(head.position, Vector3.up, step);
        }

        IEnumerator WalkTo(Vector3 from, Vector3 to)
        {
            float length = Vector3.Distance(from, to);
            bool sideStep = new Vector2(to.x - from.x, to.z - from.z).magnitude < 0.8f; // keep looking ahead on a short step
            if (HeadsetActive) ComfortVignette.Set(0.35f); // a soft ring round the view while the walk carries them
            for (float d = 0f; d < length && !skip; d += speed * Time.deltaTime)
            {
                Vector3 feet = Vector3.Lerp(from, to, d / Mathf.Max(length, 1e-3f));
                PlaceHead(feet, 0f);
                if (!sideStep) TurnTowards(to - from, turnSpeed);
                Footsteps(feet, speed * Time.deltaTime);
                yield return null;
            }
            if (!skip) PlaceHead(to, 0f);
            if (HeadsetActive) ComfortVignette.Set(0f);
        }

        void Footsteps(Vector3 feet, float moved)
        {
            stepDistance += moved;
            if (stepDistance < 0.7f) return;
            stepDistance = 0f;
            bool outside = transform.InverseTransformPoint(feet).z < -7.4f;
            ImpactFx.Play(outside ? Grabbable.Sound.Paper : Grabbable.Sound.Wood, feet, outside ? 0.08f : 0.15f, 3f);
        }

        // ---------------------------------------------------------------- doors

        IEnumerator OpenDoors(string names)
        {
            Vector3 middle = Vector3.zero;
            int count = 0;
            foreach (string name in names.Split(','))
            {
                Transform door = FindDeep(transform, name.Trim());
                if (door == null) continue;
                Renderer r = door.GetComponentInChildren<Renderer>();
                middle += r != null ? r.bounds.center : door.position;
                count++;
                if (atmosphere != null) atmosphere.PlayCreak(door.position + Vector3.up, 0.9f, 0.55f);
            }
            for (float t = 0f; t < 1.6f && !skip; t += Time.deltaTime)
            {
                if (count > 0) TurnTowards(middle / count - head.position, turnSpeed); // face the door as it swings
                foreach (string name in names.Split(','))
                    SwingOpen(name.Trim(), Mathf.SmoothStep(0f, 1f, t / 1.6f));
                yield return null;
            }
        }

        /// <summary>Swings a door (pivot on its hinge) open by `amount` of 95°, towards the inside of the house or attic.</summary>
        void SwingOpen(string name, float amount)
        {
            Transform door = FindDeep(transform, name);
            if (door == null) return;
            var state = door.GetComponent<DoorState>();
            if (state == null) state = door.gameObject.AddComponent<DoorState>(); // (no ?? with Unity objects)
            if (!state.ready)
            {
                state.ready = true;
                state.closed = door.localRotation;
                Renderer r = door.GetComponentInChildren<Renderer>();
                Vector3 leaf = (r != null ? r.bounds.center : door.position + door.right) - door.position;
                Vector3 inside = World(name.StartsWith("Gate") ? insideAttic : insideHouse) - door.position;
                // Open the way that brings the door's free edge towards the inside.
                Vector3 plus = Quaternion.AngleAxis(90f, Vector3.up) * leaf;
                Vector3 minus = Quaternion.AngleAxis(-90f, Vector3.up) * leaf;
                state.sign = Vector3.Dot(plus, inside) > Vector3.Dot(minus, inside) ? 1f : -1f;
            }
            door.localRotation = Quaternion.AngleAxis(state.sign * 95f * amount, door.parent != null ? door.parent.InverseTransformDirection(Vector3.up) : Vector3.up) * state.closed;
        }

        class DoorState : MonoBehaviour
        {
            public bool ready;
            public Quaternion closed;
            public float sign;
        }

        // ---------------------------------------------------------------- fade

        void BuildFade()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.name = "IntroFade";
            quad.transform.SetParent(head, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 0.15f);
            quad.transform.localScale = new Vector3(1f, 1f, 1f);
            fadeMaterial = AegisShield.CreateFaceMaterial(Color.black);
            fadeMaterial.renderQueue = 5000; // over everything
            fadeRenderer = quad.GetComponent<Renderer>();
            fadeRenderer.sharedMaterial = fadeMaterial;
            fadeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetFade(1f);
        }

        IEnumerator FadeTo(float target, float seconds)
        {
            float from = fade;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                SetFade(Mathf.Lerp(from, target, t / seconds));
                yield return null;
            }
            SetFade(target);
        }

        void SetFade(float alpha)
        {
            fade = alpha;
            if (fadeMaterial == null) return;
            fadeMaterial.SetColor("_BaseColor", new Color(0f, 0f, 0f, alpha));
            if (fadeRenderer != null) fadeRenderer.enabled = alpha > 0.001f;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform hit = FindDeep(child, name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
