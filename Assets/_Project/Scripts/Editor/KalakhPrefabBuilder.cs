using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArcaneStrike.EditorTools
{
    /// <summary>
    /// Menu: Arcane Strike → Build Kalakh Monster (Create Shadow Rift Scene and Create Monster World Scene run it too).
    /// Turns the Kalakh models into prefabs with URP materials:
    /// - Kalakh.prefab (Tools/blender/rig_kalakh.py → Kalakh.fbx: 1 m tall, feet at the pivot, skinned), its hide
    ///   darkened to black, its eyes (Eye.L / Eye.R under the head bone) burning;
    /// - KalakhBoss.prefab, the same creature with a red glow in it;
    /// - one prefab per breed in Breeds/ (Tools/blender/build_kalakh_breeds.py: Cinderfang, Ravager, DreadWarden,
    ///   Warlord...), each with its own materials (Breeds/&lt;Breed&gt;.materials.json).
    /// </summary>
    static class KalakhPrefabBuilder
    {
        const string Folder = "Assets/_Project/Art/Characters/Kalakh";
        const string ModelPath = Folder + "/Kalakh.fbx";
        const string MaterialsJson = Folder + "/Kalakh.materials.json";
        const string MaterialFolder = Folder + "/Materials";
        const string BreedFolder = Folder + "/Breeds";
        public const string PrefabPath = Folder + "/Kalakh.prefab";
        public const string BossPrefabPath = Folder + "/KalakhBoss.prefab";

        /// <summary>Where a breed's prefab is saved (it may not exist: the breed's model wasn't built).</summary>
        public static string BreedPrefabPath(string breed) => $"{BreedFolder}/{breed}.prefab";

        [MenuItem("Arcane Strike/Build Kalakh Monster")]
        public static bool Build()
        {
            var model = LoadModel(ModelPath);
            if (model == null)
            {
                Debug.LogWarning($"[Kalakh] {ModelPath} not found: run Tools/blender/prepare_kalakh.py first (the shadows stay placeholders).");
                return false;
            }
            var materials = AtticModelApplier.BuildMaterials(MaterialsJson, MaterialFolder);
            if (!materials.TryGetValue("M_Kalakh", out Material shadow)) return false;

            // A black hide: the texture's detail still shows, but only just.
            shadow.SetColor("_BaseColor", new Color(0.12f, 0.11f, 0.12f));
            shadow.SetFloat("_Metallic", 0.15f);
            shadow.SetFloat("_Smoothness", 0.45f);
            shadow.SetColor("_EmissionColor", Color.black);
            shadow.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(shadow);
            EyeMaterial(materials, "Kalakh_Eyes", MaterialFolder);

            Save(model, materials, shadow, PrefabPath);

            // The boss: the same body, embers glowing through the black.
            string bossPath = MaterialFolder + "/M_KalakhBoss.mat";
            var boss = AssetDatabase.LoadAssetAtPath<Material>(bossPath);
            if (boss == null)
            {
                boss = new Material(shadow) { name = "M_KalakhBoss" };
                AssetDatabase.CreateAsset(boss, bossPath);
            }
            else boss.CopyPropertiesFromMaterial(shadow);
            boss.SetColor("_BaseColor", new Color(0.16f, 0.1f, 0.1f));
            boss.EnableKeyword("_EMISSION");
            boss.SetColor("_EmissionColor", new Color(0.3f, 0.03f, 0.02f));
            boss.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(boss);
            var bossMaterials = new Dictionary<string, Material>(materials) { ["M_Kalakh"] = boss };
            Save(model, bossMaterials, boss, BossPrefabPath);

            // Every other kind of Kalakh.
            int breeds = 0;
            if (AssetDatabase.IsValidFolder(BreedFolder))
                foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { BreedFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith(".fbx")) continue;
                    string breed = Path.GetFileNameWithoutExtension(path);
                    string json = $"{BreedFolder}/{breed}.materials.json";
                    var breedModel = LoadModel(path);
                    if (breedModel == null || !File.Exists(json)) continue;
                    var breedMaterials = AtticModelApplier.BuildMaterials(json, BreedFolder + "/Materials");
                    EyeMaterial(breedMaterials, breed + "_Eyes", BreedFolder + "/Materials");
                    breedMaterials.TryGetValue(breed + "_Body", out Material body);
                    Save(breedModel, breedMaterials, body, BreedPrefabPath(breed));
                    breeds++;
                }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Kalakh] Saved {PrefabPath}, {BossPrefabPath} and {breeds} breed prefab(s) in {BreedFolder}.");
            return true;
        }

        // The models are skinned and their bones are moved by code (KalakhBody): no animation import, no Animator, the
        // axes baked so the bones' rest pose faces the right way.
        static GameObject LoadModel(string path)
        {
            if (AssetImporter.GetAtPath(path) is ModelImporter importer &&
                (importer.animationType != ModelImporterAnimationType.None || !importer.bakeAxisConversion || importer.importAnimation))
            {
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
                importer.bakeAxisConversion = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // The eyes burn: an emissive fire material (made if the json had none).
        static void EyeMaterial(Dictionary<string, Material> materials, string name, string folder)
        {
            if (!materials.TryGetValue(name, out Material eyes))
            {
                if (!AssetDatabase.IsValidFolder(folder)) return;
                string path = $"{folder}/{name}.mat";
                eyes = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (eyes == null)
                {
                    eyes = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                    AssetDatabase.CreateAsset(eyes, path);
                }
                materials[name] = eyes;
            }
            eyes.SetColor("_BaseColor", new Color(1f, 0.55f, 0.15f));
            eyes.EnableKeyword("_EMISSION");
            eyes.SetColor("_EmissionColor", new Color(6f, 2.2f, 0.3f));
            eyes.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(eyes);
        }

        // Each material slot gets the built material of its name (the eyes: any *_Eyes material); unknown ones `fallback`.
        static void Save(GameObject model, Dictionary<string, Material> materials, Material fallback, string path)
        {
            Material eyes = null;
            foreach (var kv in materials)
                if (kv.Key.EndsWith("_Eyes")) eyes = kv.Value;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = Path.GetFileNameWithoutExtension(path);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                bool eye = r.name.StartsWith("Eye.");
                var shared = r.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                {
                    string name = shared[i] != null ? shared[i].name : "";
                    if (eye && eyes != null) shared[i] = eyes;
                    else if (materials.TryGetValue(name, out Material m)) shared[i] = m;
                    else if (fallback != null) shared[i] = fallback;
                }
                r.sharedMaterials = shared;
                r.shadowCastingMode = eye ? ShadowCastingMode.Off : ShadowCastingMode.On;
            }
            foreach (var c in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var a in instance.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
            PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);
        }
    }
}
