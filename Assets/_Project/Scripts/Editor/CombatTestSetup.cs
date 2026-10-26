using ArcaneStrike.Combat;
using ArcaneStrike.HandInput;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ArcaneStrike.EditorTools
{
    /// <summary>
    /// Menu: Arcane Strike → Add Combat Test Setup.
    /// Adds HandMotion, one HandHitbox per hand and a TrainingDummy to the open scene,
    /// skipping anything that already exists. Save the scene afterwards (Ctrl+S).
    /// </summary>
    static class CombatTestSetup
    {
        [MenuItem("Arcane Strike/Add Combat Test Setup")]
        static void Add()
        {
            var input = Object.FindAnyObjectByType<HandInputService>();
            if (input == null)
            {
                EditorUtility.DisplayDialog("Combat Test Setup",
                    "No HandInputService found in this scene. Open the scene with the HandInput object first.", "OK");
                return;
            }

            var motion = Object.FindAnyObjectByType<HandMotion>();
            if (motion == null) motion = Undo.AddComponent<HandMotion>(input.gameObject);

            foreach (HandSide side in new[] { HandSide.Right, HandSide.Left })
            {
                string name = $"Hitbox_{side}";
                if (GameObject.Find(name) != null) continue;
                var go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "Add hitbox");
                var hitbox = go.AddComponent<HandHitbox>();
                var so = new SerializedObject(hitbox);
                so.FindProperty("side").enumValueIndex = (int)side;
                so.FindProperty("input").objectReferenceValue = input;
                so.FindProperty("motion").objectReferenceValue = motion;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            if (Object.FindAnyObjectByType<TrainingDummy>() == null)
            {
                Camera cam = Camera.main;
                Vector3 head = cam != null ? cam.transform.position : new Vector3(0f, 1.2f, 0f);
                Vector3 forward = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
                if (forward == Vector3.zero) forward = Vector3.forward;

                var dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Undo.RegisterCreatedObjectUndo(dummy, "Add training dummy");
                dummy.name = "TrainingDummy";
                // Within punching reach of the webcam hands (they sit ~0.35 m in front of the head).
                dummy.transform.position = head + forward * 0.55f - Vector3.up * 0.25f;
                dummy.transform.localScale = new Vector3(0.25f, 0.3f, 0.25f);
                dummy.AddComponent<TrainingDummy>();
            }

            EditorSceneManager.MarkSceneDirty(input.gameObject.scene);
            Debug.Log("[CombatTestSetup] Added HandMotion, Hitbox_Right, Hitbox_Left and TrainingDummy (existing ones were kept). Press Ctrl+S to save the scene.");
        }
    }
}
