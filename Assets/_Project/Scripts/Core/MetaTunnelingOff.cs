using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcaneStrike.Core
{
    /// <summary>
    /// Switches off the Interaction SDK's own comfort tunneling (Oculus.Interaction.TunnelingEffect) in every scene, on
    /// every platform. It closes the view down to a small hole in a grey screen whenever the rig moves, and the story
    /// moves the rig a lot (the intro walk, the rift, the Ascension), so the player sees almost nothing. The game has
    /// its own softer ring for that (Story.ComfortVignette). Matched by name, so this needs no Meta assembly reference.
    /// </summary>
    static class MetaTunnelingOff
    {
        const string Effect = "Oculus.Interaction.TunnelingEffect";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            HideAll();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => HideAll();

        static void HideAll()
        {
            foreach (var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (b == null || b.GetType().FullName != Effect || !b.gameObject.activeSelf) continue;
                b.gameObject.SetActive(false); // (its whole object: the grey quad goes with it)
                Debug.Log($"[Comfort] Meta's tunneling on '{b.gameObject.name}' is off: the game's own vignette does that job.");
            }
        }
    }
}
