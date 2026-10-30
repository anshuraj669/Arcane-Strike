using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArcaneStrike.EditorTools
{
    /// <summary>
    /// Menu: Arcane Strike → Build Weaver Character (Apply Attic Model runs it too). Turns the main character's model
    /// (Tools/blender/build_weaver.py → Weaver.fbx: the boy, the outfit fitted to him, and the sword) into
    /// Resources/Characters/Weaver.prefab with URP materials. It is kept out of the attic scene so the attic loads
    /// lighter: WarriorOutfit loads the prefab when the Ascension begins.
    /// </summary>
    static class WeaverPrefabBuilder
    {
        const string Folder = "Assets/_Project/Art/Characters/Weaver";
        const string ModelPath = Folder + "/Weaver.fbx";
        const string MaterialsJson = Folder + "/Weaver.materials.json";
        const string PlacementsJson = Folder + "/Weaver.placements.json";
        const string MaterialFolder = Folder + "/Materials";
        const string PrefabFolder = "Assets/_Project/Resources/Characters";
        public const string PrefabPath = PrefabFolder + "/Weaver.prefab";

        [MenuItem("Arcane Strike/Build Weaver Character")]
        public static bool Build()
        {
            // The suit-up trims copies of the meshes as it wraps them on, so they must be readable at runtime.
            if (AssetImporter.GetAtPath(ModelPath) is ModelImporter importer && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[Weaver] {ModelPath} not found. Run Tools/blender/build_weaver.py in Blender first.");
                return false;
            }
            var materials = AtticModelApplier.BuildMaterials(MaterialsJson, MaterialFolder);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "Weaver";
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            AtticModelApplier.PlaceAsBuilt(instance.transform, PlacementsJson);
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                Material[] shared = r.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                    if (shared[i] != null && materials.TryGetValue(shared[i].name, out Material m)) shared[i] = m;
                r.sharedMaterials = shared;
                r.shadowCastingMode = ShadowCastingMode.On;
            }
            foreach (var c in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

            EnsureFolder(PrefabFolder);
            PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            Object.DestroyImmediate(instance);
            Debug.Log($"[Weaver] Saved {PrefabPath} (loaded by WarriorOutfit when the Ascension begins).");
            return true;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
