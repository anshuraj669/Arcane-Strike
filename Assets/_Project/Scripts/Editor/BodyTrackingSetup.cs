using ArcaneStrike.HandInput;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ArcaneStrike.EditorTools
{
    /// <summary>
    /// Menu: Arcane Strike → Add Body Tracking Test.
    /// Adds DodgeDetector (lean/duck dodges, works on Quest too) and WebcamHeadDriver (editor only: the game
    /// head follows your real head from the webcam) to the HandInput object. Save the scene afterwards (Ctrl+S).
    /// </summary>
    static class BodyTrackingSetup
    {
        [MenuItem("Arcane Strike/Add Body Tracking Test")]
        static void Add()
        {
            var input = Object.FindAnyObjectByType<HandInputService>();
            if (input == null)
            {
                EditorUtility.DisplayDialog("Body Tracking Test",
                    "No HandInputService found in this scene. Open the scene with the HandInput object first.", "OK");
                return;
            }

            if (Object.FindAnyObjectByType<DodgeDetector>() == null) Undo.AddComponent<DodgeDetector>(input.gameObject);
            if (Object.FindAnyObjectByType<WebcamHeadDriver>() == null)
            {
                var driver = Undo.AddComponent<WebcamHeadDriver>(input.gameObject);
                var so = new SerializedObject(driver);
                so.FindProperty("input").objectReferenceValue = input;
                so.FindProperty("webcam").objectReferenceValue = Object.FindAnyObjectByType<WebcamHandSource>();
                if (Camera.main != null) so.FindProperty("rig").objectReferenceValue = Camera.main.transform.root;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(input.gameObject.scene);
            Debug.Log("[BodyTrackingSetup] Added DodgeDetector and WebcamHeadDriver to HandInput (existing ones were kept). Press Ctrl+S to save the scene.");
        }
    }
}
