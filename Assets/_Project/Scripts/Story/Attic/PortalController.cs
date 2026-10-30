using System;
using ArcaneStrike.Core;
using ArcaneStrike.HandInput;
using ArcaneStrike.VFX;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The portal that ends the attic. It opens on the real wall in front of the player when the room is
    /// known (MRUK: Quest or the Meta XR Simulator), otherwise where it was placed in the scene. Walking
    /// up to it (head within Enter Distance) moves to the next chapter, then loads Next Scene if that scene
    /// is in Build Settings. It opens from its frame: the stone ring rises out of nothing first, then the portal
    /// tears open inside it. <see cref="OpenInto"/> reuses it as the rift after the Ascension.
    /// </summary>
    public class PortalController : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [Tooltip("Scene to load on entering. Must be in File > Build Settings; otherwise the story stays here.")]
        public string nextScene = "BeastRealm";
        [Tooltip("Advance PowerUnlocks to the next chapter on entering (the lantern's powers awaken).")]
        [SerializeField] bool advanceChapter = true;

        [Header("Placement")]
        [SerializeField] bool placeOnRoomWall = true;
        [Tooltip("Height of the portal centre above the floor (m).")]
        [SerializeField] float centerHeight = 1.3f;

        [Header("Look")]
        [Tooltip("Optional portal art (prefab). The runtime swirl is used when empty.")]
        public GameObject visualPrefab;
        [Tooltip("Optional stone ring around the portal (a child that starts hidden with the portal): it grows in as it opens.")]
        public Transform frame;
        [SerializeField] float radius = 0.8f;
        [SerializeField] float openTime = 1.5f;
        [SerializeField] Color edgeColor = new Color(0.31f, 0.89f, 1f, 1f);   // Volt cyan #4FE3FF
        [SerializeField] Color innerColor = new Color(0.42f, 0.25f, 0.85f, 0.55f); // Hollow violet #6B3FD9
        [Tooltip("Degrees per second the portal (and a prefab visual) turns around its facing axis.")]
        [SerializeField] float spinSpeed = 40f;
        [Tooltip("How much the portal breathes in and out once open.")]
        [SerializeField] float pulse = 0.04f;

        [Header("Entering")]
        [SerializeField] float enterDistance = 0.6f;
        [Tooltip("Keyboard (GameInput): E steps through the portal.")]
        [SerializeField] bool keyboardFallback = true;
        [SerializeField] UnityEvent onEntered;

        public event Action OnOpened, OnEntered;
        public bool IsOpen { get; private set; }

        float openedAt;
        bool entered, tore, keepHeight;
        Transform visual;
        ParticleSystem swirl;
        Material innerMaterial;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Opens it as a way into another scene (the Rift after the Ascension), in its own colours, without
        /// moving the chapter on.</summary>
        public void OpenInto(string scene, Color edge, Color inner)
        {
            nextScene = scene;
            advanceChapter = false;
            placeOnRoomWall = false;
            keepHeight = true; // stands on the floor where it was put, frame and all
            edgeColor = edge;
            innerColor = inner;
            openTime = Mathf.Max(openTime, 2.4f);
            Open();
        }

        public void Open()
        {
            if (IsOpen) return;
            gameObject.SetActive(true);
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            Transform head = StoryFx.Head(input);
            bool onWall = placeOnRoomWall && head != null && TryPlaceOnWall(head);
            if (!onWall && head != null)
            {
                // No room wall: open at eye height (also keeps it in view in desk mode), facing the player.
                transform.position = new Vector3(transform.position.x, keepHeight ? transform.position.y : head.position.y, transform.position.z);
                transform.rotation = Quaternion.LookRotation(StoryFx.Flat(head.position - transform.position));
            }

            BuildVisual();
            IsOpen = true;
            openedAt = Time.time;
            if (frame != null)
            {
                frame.gameObject.SetActive(true);
                frame.localScale = Vector3.one * 0.001f;
                // The ring rises with a puff of dust and the boom of stone.
                StoryFx.Burst(swirl, frame.position, 60, 1f, 0.06f, 1f, StoryFx.WithAlpha(new Color(0.7f, 0.65f, 0.6f), 0.6f));
                Combat.CombatAudio.Play(Combat.CombatAudio.Tear, transform.position, 0.6f, 0f);
            }
            else
            {
                tore = true; // no frame: it tears open straight away, with a flash of sparks
                StoryFx.Burst(swirl, transform.position, 120, 1.6f, 0.05f, 0.9f, edgeColor);
                StoryFx.Burst(swirl, transform.position, 40, 0.5f, 0.2f, 0.6f, StoryFx.WithAlpha(Color.white, 0.8f));
            }
            Debug.Log($"[Attic] Portal opens {(onWall ? "on the room wall" : "at its scene position")}");
            OnOpened?.Invoke();
        }

        bool TryPlaceOnWall(Transform head)
        {
            MRUK mruk = MRUK.Instance;
            if (mruk == null || !mruk.IsInitialized) return false;
            MRUKRoom room = mruk.GetCurrentRoom();
            if (room == null || room.WallAnchors.Count == 0) return false;

            // The wall straight ahead of the player.
            Vector3 forward = StoryFx.Flat(head.forward);
            MRUKAnchor best = null;
            float bestScore = float.MinValue;
            foreach (MRUKAnchor wall in room.WallAnchors)
            {
                float score = Vector3.Dot(forward, StoryFx.Flat(wall.transform.position - head.position));
                if (score > bestScore)
                {
                    bestScore = score;
                    best = wall;
                }
            }
            if (best == null) return false;

            Vector3 normal = StoryFx.Flat(best.transform.forward);
            if (Vector3.Dot(normal, head.position - best.transform.position) < 0f) normal = -normal; // into the room
            var plane = new Plane(normal, best.transform.position);
            var ray = new Ray(head.position, forward);
            Vector3 point = plane.Raycast(ray, out float distance) && distance > 0f ? ray.GetPoint(distance) : best.transform.position;

            float floorY = room.FloorAnchors.Count > 0 ? room.FloorAnchors[0].transform.position.y : head.position.y - 1.6f;
            point.y = floorY + centerHeight;
            transform.SetPositionAndRotation(point + normal * 0.05f, Quaternion.LookRotation(normal));
            return true;
        }

        void BuildVisual()
        {
            if (visual != null) return;
            if (visualPrefab != null)
            {
                visual = Instantiate(visualPrefab, transform).transform;
                return;
            }

            visual = new GameObject("PortalVisual").transform;
            visual.SetParent(transform, false);
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(disc.GetComponent<Collider>());
            disc.name = "Inner";
            disc.transform.SetParent(visual, false);
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            disc.transform.localScale = new Vector3(radius * 2f, 0.002f, radius * 2f);
            innerMaterial = AegisShield.CreateFaceMaterial(innerColor);
            disc.GetComponent<Renderer>().sharedMaterial = innerMaterial;
            swirl = StoryFx.CreateParticles(transform, "Swirl", true, 0f, 1200);
        }

        void Update()
        {
            if (!IsOpen) return;
            float grow = Mathf.Clamp01((Time.time - openedAt) / Mathf.Max(openTime, 0.01f));
            float eased = 1f - (1f - grow) * (1f - grow);
            float age = Time.time - openedAt;
            // The frame first (the first 40%), then the portal tears open inside it.
            float ring = Mathf.Clamp01(grow / 0.4f), inside = Mathf.Clamp01((grow - 0.4f) / 0.6f);
            if (frame != null) frame.localScale = Vector3.one * Mathf.Max(1f - (1f - ring) * (1f - ring), 0.001f);
            if (inside > 0f && !tore)
            {
                tore = true; // it tears open with a flash of sparks
                StoryFx.Burst(swirl, transform.position, 120, 1.6f, 0.05f, 0.9f, edgeColor);
                StoryFx.Burst(swirl, transform.position, 40, 0.5f, 0.2f, 0.6f, StoryFx.WithAlpha(Color.white, 0.8f));
            }
            eased = frame != null ? 1f - (1f - inside) * (1f - inside) : eased;
            if (visual != null)
            {
                visual.localScale = Vector3.one * Mathf.Max(eased, 0.001f) * (1f + pulse * Mathf.Sin(age * 3f));
                visual.localRotation = Quaternion.Euler(0f, 0f, -age * spinSpeed);
            }
            if (innerMaterial != null)
                innerMaterial.SetColor(BaseColorId, StoryFx.WithAlpha(innerColor, innerColor.a * (0.8f + 0.2f * Mathf.Sin(age * 5f))));
            EmitSwirl(eased);

            if (entered) return;
            Transform head = StoryFx.Head(input);
            bool walkedIn = grow >= 1f && head != null && Vector3.Distance(head.position, transform.position) < enterDistance;
            bool key = keyboardFallback && GameInput.Pressed(GameAction.Interact);
            if (walkedIn || key) Enter();
        }

        void EmitSwirl(float scale)
        {
            if (swirl == null) return;
            float r = radius * scale;
            for (int i = 0; i < 8; i++)
            {
                float a = UnityEngine.Random.value * Mathf.PI * 2f;
                Vector3 local = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Vector3 tangent = new Vector3(-local.y, local.x, 0f);
                Vector3 p = transform.TransformPoint(local * r * UnityEngine.Random.Range(0.92f, 1.05f));
                Vector3 v = transform.TransformDirection(tangent * 0.9f - local * 0.25f);
                StoryFx.Emit(swirl, p, v, UnityEngine.Random.Range(0.03f, 0.08f), UnityEngine.Random.Range(0.4f, 0.8f),
                             i % 3 == 0 ? innerColor : edgeColor);
            }
        }

        public void Enter()
        {
            if (entered) return;
            entered = true;
            Debug.Log("[Attic] The player steps through the portal");
            if (advanceChapter)
            {
                var powers = FindAnyObjectByType<PowerUnlocks>();
                if (powers != null) powers.NextChapter();
            }
            onEntered?.Invoke();
            OnEntered?.Invoke();

            if (string.IsNullOrEmpty(nextScene)) return;
            if (Application.CanStreamedLevelBeLoaded(nextScene))
            {
                StartCoroutine(Through());
                return;
            }
            // Nowhere to go yet: say so where the player can see it, and let them try again once it exists.
            string fix = nextScene == "ShadowRift" ? "Arcane Strike → Create Shadow Rift Scene" : "File → Build Settings";
            Debug.LogWarning($"[Attic] Scene '{nextScene}' isn't in Build Settings yet ({fix}), so the story stays in this scene.");
            Transform head = StoryFx.Head(input);
            if (head != null)
                Combat.CombatFx.Popup($"No '{nextScene}' scene yet\nStop Play, then {fix}", head.position + StoryFx.Flat(head.forward) * 1.2f,
                                      new Color(1f, 0.6f, 0.6f), 0.7f);
            StartCoroutine(AllowRetry());
        }

        void OnDestroy()
        {
            if (innerMaterial != null) Destroy(innerMaterial); // made at runtime: not freed with the scene
        }

        System.Collections.IEnumerator AllowRetry()
        {
            yield return new WaitForSeconds(3f);
            entered = false;
        }

        // Pulled through: the view floods with the portal's colour, goes dark, and the next scene loads.
        System.Collections.IEnumerator Through()
        {
            var locomotion = FindAnyObjectByType<PlayerLocomotion>();
            if (locomotion != null) locomotion.Paused = true;
            yield return ScreenFade.To(StoryFx.WithAlpha(edgeColor, 0.6f), 0.7f);
            ScreenFade.LoadScene(nextScene, new Color(0.05f, 0f, 0.08f, 1f), 0.7f);
        }
    }
}
