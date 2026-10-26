using System.IO;
using ArcaneStrike.HandInput;
using ArcaneStrike.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ArcaneStrike.EditorTools
{
    /// <summary>
    /// Menu: Arcane Strike → Add Spark Hands.
    /// Saves the glow textures and materials under Assets/_Project/VFX/Hands (so device builds include
    /// the shader), adds a SparkHands object to the open scene and hides the grey debug joint spheres.
    /// Save the scene afterwards (Ctrl+S).
    /// </summary>
    static class SparkHandsSetup
    {
        const string Folder = "Assets/_Project/VFX/Hands";

        [MenuItem("Arcane Strike/Add Spark Hands")]
        static void Add()
        {
            var input = Object.FindAnyObjectByType<HandInputService>();
            if (input == null)
            {
                EditorUtility.DisplayDialog("Spark Hands",
                    "No HandInputService found in this scene. Open the scene with the HandInput object first.", "OK");
                return;
            }

            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project/VFX", "Hands");
            Texture2D dot = SaveTexture(SparkHands.CreateSoftDot(64), "T_SparkDot.png");
            Texture2D line = SaveTexture(SparkHands.CreateSoftLine(64), "T_SparkLine.png");
            Material glow = SaveMaterial(dot, "M_SparkGlow.mat");
            Material bone = SaveMaterial(line, "M_SparkLine.mat");
            Material spirit = SaveSkinMaterial("M_HandSpirit.mat");

            var hands = Object.FindAnyObjectByType<SparkHands>();
            if (hands == null)
            {
                var go = new GameObject("SparkHands");
                Undo.RegisterCreatedObjectUndo(go, "Add Spark Hands");
                hands = go.AddComponent<SparkHands>();
            }
            var so = new SerializedObject(hands);
            so.FindProperty("input").objectReferenceValue = input;
            so.FindProperty("motion").objectReferenceValue = Object.FindAnyObjectByType<HandMotion>();
            so.FindProperty("recognizer").objectReferenceValue = Object.FindAnyObjectByType<GestureRecognizer>();
            so.FindProperty("glowMaterial").objectReferenceValue = glow;
            so.FindProperty("lineMaterial").objectReferenceValue = bone;
            so.FindProperty("skinMaterial").objectReferenceValue = spirit;
            so.ApplyModifiedProperties();

            // The Spark hands replace the grey debug spheres (the debug text overlay stays).
            var debugView = Object.FindAnyObjectByType<HandDebugView>();
            if (debugView != null)
            {
                var dso = new SerializedObject(debugView);
                dso.FindProperty("showJoints").boolValue = false;
                dso.ApplyModifiedProperties();
            }

            EditorSceneManager.MarkSceneDirty(input.gameObject.scene);
            Selection.activeObject = hands.gameObject;
            Debug.Log($"[SparkHandsSetup] Spark Hands added with materials in {Folder}. Press Ctrl+S to save the scene.");
        }

        static Texture2D SaveTexture(Texture2D tex, string fileName)
        {
            string path = $"{Folder}/{fileName}";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material SaveSkinMaterial(string fileName)
        {
            string path = $"{Folder}/{fileName}";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Material m = SparkHands.CreateSkinMaterial(HandSkin.Spirit, new Color(1f, 0.86f, 0.55f, 0.28f));
            AssetDatabase.CreateAsset(m, path);
            AssetDatabase.SaveAssets();
            return m;
        }

        static Material SaveMaterial(Texture2D tex, string fileName)
        {
            string path = $"{Folder}/{fileName}";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Material m = SparkHands.CreateGlowMaterial(tex);
            AssetDatabase.CreateAsset(m, path);
            AssetDatabase.SaveAssets();
            return m;
        }
    }
}
