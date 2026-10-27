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
    /// is in Build Settings.
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
        [SerializeField] float radius = 0.8f;
        [SerializeField] float openTime = 1.5f;
        [SerializeField] Color edgeColor = new Color(0.31f, 0.89f, 1f, 1f);   // Volt cyan #4FE3FF
        [SerializeField] Color innerColor = new Color(0.42f, 0.25f, 0.85f, 0.55f); // Hollow violet #6B3FD9

        [Header("Entering")]
        [SerializeField] float enterDistance = 0.6f;
        [Tooltip("Editor / debug builds: Enter steps through the portal.")]
        [SerializeField] bool keyboardFallback = true;
        [SerializeField] UnityEvent onEntered;

        public event Action OnOpened, OnEntered;
        public bool IsOpen { get; private set; }

        float openedAt;
        bool entered;
        Transform visual;
        ParticleSystem swirl;

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
                transform.position = new Vector3(transform.position.x, head.position.y, transform.position.z);
                transform.rotation = Quaternion.LookRotation(StoryFx.Flat(head.position - transform.position));
            }

            BuildVisual();
            IsOpen = true;
            openedAt = Time.time;
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
            disc.GetComponent<Renderer>().sharedMaterial = AegisShield.CreateFaceMaterial(innerColor);
            swirl = StoryFx.CreateParticles(transform, "Swirl", true, 0f, 1200);
        }

        void Update()
        {
            if (!IsOpen) return;
            float grow = Mathf.Clamp01((Time.time - openedAt) / Mathf.Max(openTime, 0.01f));
            float eased = 1f - (1f - grow) * (1f - grow);
            if (visual != null) visual.localScale = Vector3.one * eased;
            EmitSwirl(eased);

            if (entered) return;
            Transform head = StoryFx.Head(input);
            bool walkedIn = grow >= 1f && head != null && Vector3.Distance(head.position, transform.position) < enterDistance;
            bool key = keyboardFallback && (Application.isEditor || Debug.isDebugBuild) &&
                       Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame;
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
            if (Application.CanStreamedLevelBeLoaded(nextScene)) SceneManager.LoadScene(nextScene);
            else Debug.Log($"[Attic] Scene '{nextScene}' isn't in Build Settings yet, so the story stays in this scene.");
        }
    }
}
