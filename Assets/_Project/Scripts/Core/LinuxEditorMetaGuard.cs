#if UNITY_EDITOR_LINUX
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcaneStrike.Core
{
    /// <summary>
    /// EDITOR ON LINUX ONLY. Meta's native plugins (OVRPlugin, MetaXRAudioUnity) ship for Windows and
    /// Android, not Linux, so in Play mode some Meta components throw DllNotFoundException every frame.
    /// Thousands of errors a second freeze the editor and grow Editor.log by gigabytes.
    /// This turns those components off when Play mode starts. Device builds and the Windows editor are untouched.
    /// </summary>
    static class LinuxEditorMetaGuard
    {
        // Matched by name so this file doesn't need references to the Meta assemblies.
        static readonly HashSet<string> Blocked = new HashSet<string>
        {
            "OVRManager",
            "Meta.XR.MRUtilityKit.MRUK",
            "Meta.XR.EnvironmentDepth.EnvironmentDepthManager",
            "MetaXRAudioRoomAcousticProperties",
            "MetaXRAudioSource",
            "OVRPassthroughLayer",
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            DisableBlocked();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => DisableBlocked();

        static void DisableBlocked()
        {
            var names = new List<string>();
            foreach (var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (b == null || !b.enabled || !Blocked.Contains(b.GetType().FullName)) continue;
                b.enabled = false;
                names.Add($"{b.GetType().Name} on '{b.gameObject.name}'");
            }
            if (names.Count > 0)
                Debug.Log("[LinuxEditorMetaGuard] Meta native plugins aren't available in the Linux editor. Disabled for this Play session: " +
                          string.Join(", ", names) + ". Webcam hands, keyboard testing and gameplay scripts still work.");
        }
    }
}
#endif
