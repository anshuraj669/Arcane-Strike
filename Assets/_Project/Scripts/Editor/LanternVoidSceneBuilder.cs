using System.Linq;
using ArcaneStrike.Core;
using ArcaneStrike.HandInput;
using ArcaneStrike.Props;
using ArcaneStrike.Story;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ArcaneStrike.EditorTools
{
    /// <summary>
    /// Menu: Arcane Strike → Create Lantern Void Scene. Chapter 2, inside the lantern: SampleScene's player rig,
    /// an endless white floor, the bronze cauldron (Tools/blender/build_cauldron.py) with Ember burning in it,
    /// the director that runs the chapter, walking and point-to-grab, and the story's power chapters starting at
    /// "Attic" (the Jyoti Seal unlocks during the chapter). Also puts Attic then LanternVoid first in Build
    /// Settings, so the attic's golden light can carry the player in. Running it again rebuilds the set.
    /// </summary>
    static class LanternVoidSceneBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/LanternVoid.unity";
        const string AtticScene = "Assets/_Project/Scenes/Attic.unity";
        const string TemplateScene = "Assets/Scenes/SampleScene.unity";
        const string CauldronModel = "Assets/_Project/Art/Environment/LanternVoid/Models/Cauldron.fbx";
        const string MaterialFolder = "Assets/_Project/Art/Environment/LanternVoid/Materials";
        const string Textures = "Assets/_Project/Art/Environment/Attic/Textures";
        const string RootName = "LanternVoid";

        [MenuItem("Arcane Strike/Create Lantern Void Scene")]
        static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && !AssetDatabase.CopyAsset(TemplateScene, ScenePath))
            {
                EditorUtility.DisplayDialog("Lantern Void", $"Couldn't copy {TemplateScene} to {ScenePath}.", "OK");
                return;
            }
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (string name in new[] { "ExampleCubePrefab", "TrainingDummy", RootName })
            {
                var go = GameObject.Find(name);
                if (go != null) Object.DestroyImmediate(go);
            }
            Build();
            EditorSceneManager.SaveScene(scene);
            AddToBuildSettings();
            Debug.Log($"[VoidBuilder] Saved {ScenePath}. Press Play here to test it alone, or play the Attic: its ending carries you in.");
        }

        static void Build()
        {
            var root = new GameObject(RootName);

            // The endless white floor (the haze hides its edge).
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "VoidFloor";
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.005f, 0f);
            floor.transform.localScale = new Vector3(90f, 0.005f, 90f);
            floor.GetComponent<Renderer>().sharedMaterial = Lit("M_Void_Floor", new Color(0.95f, 0.94f, 0.91f), 0f, 0.15f);
            // A cylinder comes with a capsule collider, which at this shape would be a huge sphere: use a flat box.
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            var ground = floor.AddComponent<BoxCollider>();
            ground.size = new Vector3(1f, 20f, 1f);      // 20 × 0.005 = 0.1 m thick
            ground.center = new Vector3(0f, -9f, 0f);    // its top just at the floor

            // The cauldron, 3 m ahead of the player, with Ember at its mouth.
            Transform cauldron;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CauldronModel);
            if (model != null)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                cauldron = go.transform;
                Material bronze = Bronze(), coals = Coals();
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    r.sharedMaterial = r.name.Contains("Coals") ? coals : bronze;
                foreach (var f in go.GetComponentsInChildren<MeshFilter>())
                    if (!f.name.Contains("Coals")) f.gameObject.AddComponent<MeshCollider>();
            }
            else
            {
                Debug.LogWarning($"[VoidBuilder] {CauldronModel} not found (run Tools/blender/build_cauldron.py): using a stand-in.");
                cauldron = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
                cauldron.localScale = new Vector3(1.05f, 0.75f, 1.05f);
                cauldron.GetComponent<Renderer>().sharedMaterial = Bronze();
            }
            cauldron.name = "Cauldron";
            cauldron.SetParent(root.transform, false);
            cauldron.localPosition = new Vector3(0f, 0f, 3f);

            var emberGo = new GameObject("Ember");
            emberGo.transform.SetParent(cauldron, false);
            emberGo.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            var ember = emberGo.AddComponent<EmberFlame>();
            var speech = emberGo.AddComponent<StorySpeech>();

            var director = root.AddComponent<LanternVoidDirector>();
            Set(director, "ember", ember);
            Set(director, "emberSpeech", speech);
            Set(director, "cauldron", cauldron);
            root.AddComponent<PlayerLocomotion>();
            root.AddComponent<PointGrab>();

            // A soft, high light so the cauldron has shape in the white.
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional)
                {
                    l.color = new Color(1f, 0.98f, 0.94f);
                    l.intensity = 0.6f;
                    l.shadows = LightShadows.Soft;
                    l.transform.rotation = Quaternion.Euler(60f, 30f, 0f);
                }

            // Powers: the chapter starts with none; the Jyoti Seal unlocks during it.
            if (Object.FindAnyObjectByType<PowerUnlocks>() == null) new GameObject("PowerUnlocks").AddComponent<PowerUnlocks>();
            Selection.activeGameObject = root;
        }

        static void AddToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != AtticScene && s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            scenes.Insert(0, new EditorBuildSettingsScene(AtticScene, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[VoidBuilder] Build Settings: " + string.Join(", ", scenes.Select(s => System.IO.Path.GetFileNameWithoutExtension(s.path))));
        }

        // ---------------------------------------------------------------- materials

        static Material Bronze()
        {
            Material m = Lit("M_Void_Bronze", new Color(0.8f, 0.52f, 0.3f), 1f, 0.5f);
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture>($"{Textures}/Metal063/Metal063_Color.jpg"));
            var normal = AssetDatabase.LoadAssetAtPath<Texture>($"{Textures}/Metal063/Metal063_Normal.jpg");
            if (normal != null)
            {
                m.SetTexture("_BumpMap", normal);
                m.EnableKeyword("_NORMALMAP");
            }
            m.SetTextureScale("_BaseMap", new Vector2(2f, 2f));
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Coals()
        {
            Material m = Lit("M_Void_Coals", new Color(0.12f, 0.04f, 0.02f), 0f, 0.2f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(1.6f, 0.55f, 0.1f));
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Lit(string name, Color color, float metallic, float smoothness)
        {
            EnsureFolder(MaterialFolder);
            string path = $"{MaterialFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        static void Set(Object target, string property, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(property).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
