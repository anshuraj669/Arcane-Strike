using ArcaneStrike.Core;
using ArcaneStrike.HandInput;
using ArcaneStrike.Story;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ArcaneStrike.EditorTools
{
    /// <summary>
    /// Menu: Arcane Strike → Create Ascension Temple Scene. Where the attic's rift drops the player: an open ring of
    /// pointed arches on jade water under a golden sky, a mosaic floor, a bronze cauldron at its centre,
    /// a shaft of light on it (Tools/blender/build_temple.py → Temple.fbx). The scene is still called AscensionCave.
    /// Runs on Ascension: the player arrives out of the rift, walks into the light and touches the cauldron, a shockwave
    /// rings out, the Ascension and the suit-up happen, the sword forming with the outfit, Ember offers
    /// sword practice, and the sealed gate ahead breaks: the bridge behind it leads to the shrine, the way into
    /// MonsterWorld. Adds the scene to Build Settings after the Attic. Running it again rebuilds it.
    /// </summary>
    static class CaveSceneBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/AscensionCave.unity";
        const string TemplateScene = "Assets/Scenes/SampleScene.unity";
        const string Folder = "Assets/_Project/Art/Environment/Temple";
        const string ModelPath = Folder + "/Temple.fbx";
        const string RootName = "AscensionCave";
        static readonly Vector3 Altar = new Vector3(0f, 0f, 4f);

        [MenuItem("Arcane Strike/Create Ascension Temple Scene")]
        static void Create()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Ascension Temple", "Unity is still compiling. Wait for the spinner at the bottom right to stop, then click this again (or the scene is built with the old code).", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                EditorUtility.DisplayDialog("Ascension Temple", $"{ModelPath} not found. Run Tools/blender/build_temple.py in Blender first.", "OK");
                return;
            }
            WeaverPrefabBuilder.Build(); // (always: the sword or outfit may have been rebuilt in Blender)
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && !AssetDatabase.CopyAsset(TemplateScene, ScenePath))
            {
                EditorUtility.DisplayDialog("Ascension Temple", $"Couldn't copy {TemplateScene} to {ScenePath}.", "OK");
                return;
            }
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (string name in new[] { "ExampleCubePrefab", "TrainingDummy", RootName })
            {
                var go = GameObject.Find(name);
                if (go != null) Object.DestroyImmediate(go);
            }
            Build(model);
            SceneOrder.PlaceRigAtOrigin(); // the player arrives at the origin, facing the altar
            EditorSceneManager.SaveScene(scene);
            SceneOrder.Apply();
            Debug.Log($"[CaveBuilder] Saved {ScenePath}. Press Play here to start from the temple, or play the Attic and let the rift pull you in.");
        }

        static void Build(GameObject model)
        {
            var root = new GameObject(RootName);

            // The temple, with its materials and colliders to walk on and against.
            var materials = AtticModelApplier.BuildMaterials(Folder + "/Temple.materials.json", Folder + "/Materials");
            var temple = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(temple, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            temple.name = "Temple";
            temple.transform.SetParent(root.transform, false);
            AtticModelApplier.PlaceAsBuilt(temple.transform, Folder + "/Temple.placements.json");
            Renderer cauldron = null;
            Transform lantern = null;
            Renderer exitSeal = null;
            Texture2D skyTexture = TempleSky();
            foreach (var r in temple.GetComponentsInChildren<Renderer>(true))
            {
                Material[] shared = r.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                    if (shared[i] != null && materials.TryGetValue(shared[i].name, out Material m)) shared[i] = m;
                r.sharedMaterials = shared;
                string n = r.gameObject.name;
                bool far = n == "Temple_Water" || n == "Temple_Banks" || n == "Temple_Trees" || n == "Temple_Orb";
                r.shadowCastingMode = far ? ShadowCastingMode.Off : ShadowCastingMode.On;
                if (n == "Temple_Floor" || n == "Temple_Piers" || n == "Temple_Altar" || n == "Temple_Statues" || n == "Temple_ExitSeal" ||
                    n == "Temple_Cauldron" || n == "Temple_Bridge" || n == "Temple_ShrineFloor" || n == "Temple_Shrine" || n == "Temple_BridgePosts")
                    r.gameObject.AddComponent<MeshCollider>();
                if (n == "Temple_Water")
                {
                    r.sharedMaterial = WaterMaterial(r.sharedMaterial, skyTexture);
                    r.gameObject.AddComponent<MoonLake>();
                }
                if (n == "Temple_ExitSeal") exitSeal = r;
                if (n == "Temple_Cauldron") cauldron = r;
                if (n == "Temple_Lantern") lantern = r.transform;
            }

            // A warm, bright, open afternoon: the sun low and gold, soft haze over the water.
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional)
                {
                    l.color = new Color(1f, 0.88f, 0.68f);
                    l.intensity = 1.5f;
                    l.shadows = LightShadows.Soft;
                    l.transform.rotation = Quaternion.Euler(42f, 150f, 0f);
                }
            string skyPath = Folder + "/Materials/M_Temple_Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Panoramic")) { name = "M_Temple_Sky" };
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            sky.SetTexture("_MainTex", skyTexture);
            sky.SetFloat("_Mapping", 1f); // latitude-longitude
            sky.SetFloat("_Exposure", 1f);
            sky.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 1f));
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.82f, 0.72f, 0.5f);
            RenderSettings.ambientEquatorColor = new Color(0.6f, 0.56f, 0.4f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.36f, 0.3f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.92f, 0.76f, 0.46f);
            RenderSettings.fogDensity = 0.004f;
            foreach (Camera cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, 400f);
            }

            // The shaft of light from the open sky down onto the altar: gold-white here, not cold blue.
            var shaftGo = new GameObject("LightShaft");
            shaftGo.transform.SetParent(root.transform, false);
            shaftGo.transform.localPosition = Altar + Vector3.up * 10.5f;
            var shaft = shaftGo.AddComponent<LightShaft>();
            SetFloat(shaft, "length", 10.5f);
            SetFloat(shaft, "topRadius", 0.9f);
            SetFloat(shaft, "bottomRadius", 1.5f);
            var shaftSo = new SerializedObject(shaft);
            shaftSo.FindProperty("color").colorValue = new Color(1f, 0.9f, 0.62f, 1f);
            shaftSo.ApplyModifiedPropertiesWithoutUndo();

            // The fire in the cauldron, from just inside its mouth.
            var fire = new GameObject("CauldronFlames");
            fire.transform.SetParent(root.transform, false);
            fire.transform.localPosition = Altar + Vector3.up * 1.08f;
            fire.AddComponent<CauldronFlames>();

            // The blue light in the shrine at the end of the bridge.
            var orbLight = new GameObject("ShrineOrbLight").AddComponent<Light>();
            orbLight.transform.SetParent(root.transform, false);
            orbLight.transform.localPosition = new Vector3(0f, 1.6f, 29.4f);
            orbLight.type = LightType.Point;
            orbLight.color = new Color(0.45f, 0.8f, 1f);
            orbLight.intensity = 2.5f;
            orbLight.range = 7f;
            orbLight.shadows = LightShadows.None;

            // The tear the player comes out of (behind them, open), and where the Kalakh's rift will open afterwards.
            var tearGo = new GameObject("EntranceTear");
            tearGo.transform.SetParent(root.transform, false);
            tearGo.transform.localPosition = new Vector3(0f, 0f, -1.2f);
            var tear = tearGo.AddComponent<RiftTear>();
            SetFloat(tear, "height", 2.3f);
            SetFloat(tear, "width", 1.1f);
            SetFloat(tear, "openness", 1f);
            var exit = new GameObject("RiftExit").transform;
            exit.SetParent(root.transform, false);
            exit.localPosition = new Vector3(-4.2f, 0f, 3.2f);
            exit.localRotation = Quaternion.LookRotation(Vector3.right);

            // The way out straight ahead: the sealed gate, the bridge behind it, and the threshold into the monster world in the shrine.
            CavePassage passage = null;
            var passageFile = AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "/Temple.passage.json");
            if (passageFile != null)
            {
                var spots = JsonUtility.FromJson<PassageSpots>(passageFile.text);
                var gate = new GameObject("WorldThreshold");
                gate.transform.SetParent(root.transform, false);
                gate.transform.localPosition = ToVector(spots.threshold);
                gate.transform.localRotation = Quaternion.LookRotation(ToVector(spots.facing));
                var threshold = gate.AddComponent<RiftTear>();
                var ts = new SerializedObject(threshold);
                ts.FindProperty("height").floatValue = 2.5f;
                ts.FindProperty("width").floatValue = 1.4f;
                ts.FindProperty("edgeColor").colorValue = new Color(1f, 0.5f, 0.15f);
                ts.FindProperty("coreColor").colorValue = new Color(1f, 0.55f, 0.25f, 0.85f);
                ts.ApplyModifiedPropertiesWithoutUndo();
                passage = root.AddComponent<CavePassage>();
                passage.rocks = exitSeal;
                passage.threshold = threshold;
                passage.nextScene = "MonsterWorld";
            }

            // What runs it.
            var ascension = root.AddComponent<Ascension>();
            ascension.passage = passage;
            // Optional sword practice with Ember before the way out (a practice shadow in the Kalakh's shape).
            KalakhPrefabBuilder.Build();
            var practice = root.AddComponent<SwordPractice>();
            practice.shadowModel = AssetDatabase.LoadAssetAtPath<GameObject>(KalakhPrefabBuilder.PrefabPath);
            ascension.entrance = tear;
            ascension.relic = cauldron;
            ascension.shaft = shaft;
            ascension.exitScene = "MonsterWorld"; // (only used without the tunnel: a rift opens in the wall instead)
            ascension.exitPoint = exit;
            if (lantern != null)
            {
                lantern.gameObject.SetActive(false); // until it comes out of the rift
                ascension.lanternModel = lantern;
            }
            var start = new GameObject("PlayerStart").transform;
            start.SetParent(root.transform, false);
            ascension.playerStart = start;
            SetBool(ascension, "runOnStart", true);
            SetBool(ascension, "closeEntrance", true);
            root.AddComponent<WarriorOutfit>();
            root.AddComponent<PlayerLocomotion>();
                        var powers = Object.FindAnyObjectByType<PowerUnlocks>();
            if (powers == null) powers = root.AddComponent<PowerUnlocks>();
            var so = new SerializedObject(powers);
            so.FindProperty("startChapter").intValue = 0; // no powers yet
            so.ApplyModifiedPropertiesWithoutUndo();
            if (cauldron == null) Debug.LogWarning("[CaveBuilder] No Temple_Cauldron in Temple.fbx: the Ascension will start as the player arrives.");
            Selection.activeGameObject = root;
        }

        [System.Serializable]
        class PassageSpots
        {
            public float[] mouth, threshold, facing;
        }

        static Vector3 ToVector(float[] v) => v != null && v.Length >= 3 ? new Vector3(v[0], v[1], v[2]) : Vector3.zero;

        // The sky panorama (Tools/textures/build_temple_textures.py): no mipmaps, they'd show a seam where it wraps.
        static Texture2D TempleSky()
        {
            string path = Folder + "/T_Temple_Sky.png";
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Jade water, mirroring that sky, with pale streaks sliding across it.
        static Material WaterMaterial(Material fallback, Texture2D sky)
        {
            Shader shader = Shader.Find(MoonLake.ShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[CaveBuilder] No {MoonLake.ShaderName} shader: the water stays on {fallback?.name}.");
                return fallback;
            }
            string path = Folder + "/Materials/M_Temple_JadeWater.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = "M_Temple_JadeWater" };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetTexture("_SkyTex", sky);
            m.SetColor("_Deep", new Color(0.03f, 0.3f, 0.24f));
            m.SetColor("_Ridges", new Color(0.3f, 0.42f, 0.22f));
            m.SetVector("_PeakShape", new Vector4(0f, 1f, 0f, 0f)); // (no peak in this water)
            m.SetFloat("_Mirror", 0.3f);
            m.SetFloat("_Ripple", 0.05f);
            m.SetFloat("_Tiling", 0.12f);
            m.SetFloat("_Foam", 0.55f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void SetFloat(Object target, string property, float value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(property).floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetBool(Object target, string property, bool value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(property).boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
