using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ArcaneStrike.EditorTools
{
    /// <summary>
    /// Menu: Arcane Strike → Put Scenes In Story Order. Build Settings in the order the story plays: the Attic first
    /// (the scene a build starts in), then the Ascension cave, then the monster world, then Chapter 2's scenes. Scenes the story no longer
    /// visits (the Shadow Rift, the Lantern Void, the template SampleScene) stay listed but switched off. The scene builders call this
    /// too. (Order only decides which scene a build starts in: each scene loads the next one itself.)
    /// </summary>
    static class SceneOrder
    {
        static readonly string[] Story =
        {
            "Assets/_Project/Scenes/Attic.unity",
            "Assets/_Project/Scenes/AscensionCave.unity",
            "Assets/_Project/Scenes/MonsterWorld.unity",
            // Chapter 2 (scenes not built yet are skipped)
            "Assets/_Project/Scenes/Chapter2_AtticWake.unity",
            "Assets/_Project/Scenes/Chapter2_Bedroom.unity",
        };

        [MenuItem("Arcane Strike/Put Scenes In Story Order")]
        public static void Apply()
        {
            var ordered = Story.Where(p => AssetDatabase.LoadAssetAtPath<SceneAsset>(p) != null)
                               .Select(p => new EditorBuildSettingsScene(p, true)).ToList();
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                if (!Story.Contains(s.path)) ordered.Add(new EditorBuildSettingsScene(s.path, false));
            EditorBuildSettings.scenes = ordered.ToArray();
            Debug.Log("[Scenes] Build Settings in story order: " +
                      string.Join(" → ", ordered.Select(s => System.IO.Path.GetFileNameWithoutExtension(s.path) + (s.enabled ? "" : " (off)"))));
        }

        /// <summary>Puts the player's rig at the origin, facing +Z (the scenes are built round that spot).</summary>
        public static void PlaceRigAtOrigin()
        {
            foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go.transform.parent == null && go.name.Contains("Camera Rig"))
                {
                    go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    if (PrefabUtility.IsPartOfPrefabInstance(go)) PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
                    EditorUtility.SetDirty(go.transform);
                }
        }
    }
}
