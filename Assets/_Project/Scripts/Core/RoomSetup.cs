using System.Collections;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ArcaneStrike.Core
{
    /// <summary>
    /// Makes the room MRUK loads part of the game: gives its walls, floor, ceiling and furniture colliders,
    /// so fireballs explode on walls and the Skyhammer shockwave lands on the floor, and logs which room loaded.
    /// Works with a real room on Quest, the Meta XR Simulator's rooms (built-in or a custom Synthetic
    /// Environment Server) and MRUK's prefab rooms as the fallback.
    /// Added automatically to every scene that has MRUK, so scenes need no changes.
    /// </summary>
    public class RoomSetup : MonoBehaviour
    {
        [Tooltip("Give the room's surfaces colliders (fireballs, shockwaves and later enemies use them).")]
        [SerializeField] bool roomColliders = true;
        [Tooltip("Draw the room mesh in the editor, to check the Simulator room lines up. R toggles it.")]
        [SerializeField] bool showRoomMeshInEditor = true;
        [Tooltip("Draw the room mesh on device. Usually off: the player sees the real room through passthrough.")]
        [SerializeField] bool showRoomMeshOnDevice;
        [Tooltip("Editor and development builds: R shows or hides the room mesh.")]
        [SerializeField] bool keyboardToggle = true;

        bool showMesh;
        static bool hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAdd()
        {
            AddToScene();
            if (hooked) return;
            hooked = true;
            SceneManager.sceneLoaded += (_, _) => AddToScene(); // chapters loaded later get it too
        }

        // Runs after the scene's Awake/OnEnable but before Start, so the settings below are in place
        // before MRUK loads the room and EffectMesh builds it.
        static void AddToScene()
        {
            var mruk = FindAnyObjectByType<MRUK>();
            if (mruk != null && mruk.GetComponent<RoomSetup>() == null) mruk.gameObject.AddComponent<RoomSetup>();
        }

        void Awake()
        {
            showMesh = Application.isEditor ? showRoomMeshInEditor : showRoomMeshOnDevice;
            foreach (EffectMesh effectMesh in FindObjectsByType<EffectMesh>(FindObjectsSortMode.None))
            {
                if (roomColliders) effectMesh.Colliders = true;
                effectMesh.HideMesh = !showMesh;
            }
        }

        void Start()
        {
            if (MRUK.Instance == null)
            {
                Debug.LogWarning("[Room] No MRUK in this scene, so no room will load.");
                return;
            }
            MRUK.Instance.RegisterSceneLoadedCallback(OnRoomLoaded);
        }

        void OnDestroy()
        {
            if (MRUK.Instance != null) MRUK.Instance.SceneLoadedEvent.RemoveListener(OnRoomLoaded);
        }

        void OnRoomLoaded() => StartCoroutine(AfterRoomLoaded());

        IEnumerator AfterRoomLoaded()
        {
            yield return null; // let EffectMesh build its meshes first (it listens to the same event)

            foreach (EffectMesh effectMesh in FindObjectsByType<EffectMesh>(FindObjectsSortMode.None))
            {
                if (roomColliders) effectMesh.ToggleEffectMeshColliders(true); // adds any that are missing
                effectMesh.HideMesh = !showMesh;
            }
            LogRoom();
        }

        void LogRoom()
        {
            var rooms = MRUK.Instance.Rooms;
            if (rooms.Count == 0)
            {
                Debug.LogWarning("[Room] No room loaded. In the Meta XR Simulator choose an environment " +
                                 "(or start your Synthetic Environment Server) before pressing Play.");
                return;
            }

            MRUKRoom room = MRUK.Instance.GetCurrentRoom() ?? rooms[0];
            string floor = room.FloorAnchors.Count > 0 ? $"{room.FloorAnchors[0].transform.position.y:F2} m" : "none";
            Debug.Log($"[Room] Loaded {rooms.Count} room(s) from {MRUK.Instance.SceneSettings.DataSource}. " +
                      $"Current '{room.name}': {room.WallAnchors.Count} walls, {room.Anchors.Count} anchors, floor at y = {floor}. " +
                      $"Colliders {(roomColliders ? "on" : "off")}, mesh {(showMesh ? "shown" : "hidden")} (R toggles).");
        }

        void Update()
        {
            if (!keyboardToggle || !(Application.isEditor || Debug.isDebugBuild)) return;
            Keyboard kb = Keyboard.current;
            if (kb == null || !kb.rKey.wasPressedThisFrame) return;

            showMesh = !showMesh;
            foreach (EffectMesh effectMesh in FindObjectsByType<EffectMesh>(FindObjectsSortMode.None))
                effectMesh.HideMesh = !showMesh;
            Debug.Log($"[Room] Room mesh {(showMesh ? "shown" : "hidden")}.");
        }
    }
}
