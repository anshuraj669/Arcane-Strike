using System.Linq;
using ArcaneStrike.Combat;
using ArcaneStrike.Core;
using ArcaneStrike.HandInput;
using ArcaneStrike.Story;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ArcaneStrike.EditorTools
{
    /// <summary>
    /// Menu: Arcane Strike → Create Shadow Rift Scene. The far side of the rift that tears open in the attic when the
    /// lantern awakens: the Ascension happens here as the player arrives, then chapter 1's fight. The set: SampleScene's player rig on a wide floor of wet black stone veined with glowing
    /// violet cracks, shards of rock floating in a violet haze, broken spires round the edge, the tear the player came
    /// through behind them and the (still closed) far tear ahead, where the mini boss will come out.
    /// The scene runs on RiftArena (the waves, Ember, the ending), WeaverSword (sword fighting), RiftPlayer (health),
    /// Ascension (the light, the soul and Ember, the outfit forming), WarriorOutfit, PlayerLocomotion, and PowerUnlocks at the "Attic" chapter (no powers: the
    /// sword only). Adds the scene to Build Settings after the Attic. Running it again rebuilds the set.
    /// </summary>
    static class ShadowRiftSceneBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/ShadowRift.unity";
        const string TemplateScene = "Assets/Scenes/SampleScene.unity";
        const string MaterialFolder = "Assets/_Project/Art/Environment/ShadowRift/Materials";
        const string Textures = "Assets/_Project/Art/Environment/Attic/Textures";
        const string RootName = "ShadowRift";

        [MenuItem("Arcane Strike/Create Shadow Rift Scene")]
        static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(WeaverPrefabBuilder.PrefabPath) == null) WeaverPrefabBuilder.Build();
            KalakhPrefabBuilder.Build(); // the shadow monster's model (placeholders without it)
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && !AssetDatabase.CopyAsset(TemplateScene, ScenePath))
            {
                EditorUtility.DisplayDialog("Shadow Rift", $"Couldn't copy {TemplateScene} to {ScenePath}.", "OK");
                return;
            }
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (string name in new[] { "ExampleCubePrefab", "TrainingDummy", RootName })
            {
                var go = GameObject.Find(name);
                if (go != null) Object.DestroyImmediate(go);
            }
            Build();
            SceneOrder.PlaceRigAtOrigin(); // between the entrance tear (behind) and the far tear (ahead)
            EditorSceneManager.SaveScene(scene);
            SceneOrder.Apply();
            Debug.Log($"[RiftBuilder] Saved {ScenePath}. Press Play here to start from the Ascension, or play the Attic and let the rift pull you in.");
        }

        static void Build()
        {
            var root = new GameObject(RootName);
            Random.InitState(1234);

            // The floor: wet black stone (the haze hides its edge).
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "RiftFloor";
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.005f, 0f);
            floor.transform.localScale = new Vector3(80f, 0.005f, 80f);
            floor.GetComponent<Renderer>().sharedMaterial = Lit("M_Rift_Ground", new Color(0.16f, 0.13f, 0.2f), 0f, 0.65f, "Rock058", 30f);
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            var ground = floor.AddComponent<BoxCollider>();
            ground.size = new Vector3(1f, 20f, 1f);   // 20 × 0.005 = 0.1 m thick
            ground.center = new Vector3(0f, -9f, 0f); // its top just at the floor

            // Glowing violet cracks running across it, each a short zigzag.
            Material crack = Glow("M_Rift_Crack", new Color(1.3f, 0.35f, 2.4f));
            var cracks = new GameObject("Cracks").transform;
            cracks.SetParent(root.transform, false);
            for (int i = 0; i < 34; i++)
            {
                float angle = Random.Range(0f, 360f), radius = Random.Range(1.6f, 18f);
                Vector3 at = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
                float heading = angle + Random.Range(-50f, 50f);
                for (int seg = 0; seg < 3; seg++)
                {
                    float length = Random.Range(0.6f, 1.8f);
                    heading += Random.Range(-35f, 35f);
                    Vector3 dir = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
                    var c = Box(cracks, "Crack", at + dir * length * 0.5f + Vector3.up * 0.003f, new Vector3(Random.Range(0.025f, 0.07f), 0.01f, length),
                                Quaternion.LookRotation(dir), crack);
                    Object.DestroyImmediate(c.GetComponent<Collider>());
                    at += dir * length;
                }
            }

            // Shards of rock floating in the haze.
            Material rock = Lit("M_Rift_Rock", new Color(0.3f, 0.26f, 0.36f), 0f, 0.35f, "Rock058", 2f);
            var rocks = new GameObject("FloatingRocks").transform;
            rocks.SetParent(root.transform, false);
            for (int i = 0; i < 40; i++)
            {
                float angle = Random.Range(0f, 360f), radius = Random.Range(9f, 30f);
                Vector3 at = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius + Vector3.up * Random.Range(1.5f, 14f);
                float s = Random.Range(0.4f, 2.4f);
                var r = Box(rocks, "Rock", at, new Vector3(s * Random.Range(0.6f, 1.4f), s * Random.Range(0.4f, 1f), s * Random.Range(0.6f, 1.3f)), Random.rotation, rock);
                Object.DestroyImmediate(r.GetComponent<Collider>());
                r.AddComponent<RiftDrift>();
            }

            // Broken spires round the arena, with violet veins.
            var spires = new GameObject("Spires").transform;
            spires.SetParent(root.transform, false);
            for (int i = 0; i < 11; i++)
            {
                float angle = i * 360f / 11f + Random.Range(-10f, 10f);
                if (Mathf.Abs(Mathf.DeltaAngle(angle, 0f)) < 18f) continue; // keep the way to the far tear clear
                float radius = Random.Range(11.5f, 14f), height = Random.Range(2.5f, 7f);
                Vector3 at = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
                Quaternion tilt = Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0f, 360f), Random.Range(-12f, 12f));
                var spire = Box(spires, "Spire", at + Vector3.up * height * 0.45f, new Vector3(0.8f, height, 0.7f), tilt, rock);
                var vein = Box(spire.transform, "Vein", new Vector3(0f, 0f, 0.51f), new Vector3(0.06f, 0.8f, 0.02f), Quaternion.identity, crack);
                Object.DestroyImmediate(vein.GetComponent<Collider>());
            }

            // The tear the player came through (behind them), and the far one ahead (closed until the end).
            RiftTear entrance = Tear(root.transform, "EntranceTear", new Vector3(0f, 0f, -2.2f), Quaternion.identity, 2.3f, 1.1f);
            RiftTear farEnd = Tear(root.transform, "FarTear", new Vector3(0f, 0f, 13f), Quaternion.Euler(0f, 180f, 0f), 4.5f, 2.2f);

            // Dim, cold violet light from above.
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional)
                {
                    l.color = new Color(0.45f, 0.35f, 0.7f);
                    l.intensity = 0.35f;
                    l.shadows = LightShadows.Soft;
                    l.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
                }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.06f, 0.02f, 0.09f);
            RenderSettings.fogDensity = 0.045f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.16f, 0.1f, 0.25f);
            RenderSettings.ambientEquatorColor = new Color(0.08f, 0.04f, 0.12f);
            RenderSettings.ambientGroundColor = new Color(0.02f, 0.01f, 0.03f);
            RenderSettings.skybox = null;

            // What runs it.
            var arena = root.AddComponent<RiftArena>();
            Set(arena, "entrance", entrance);
            Set(arena, "farEnd", farEnd);
            var kalakh = AssetDatabase.LoadAssetAtPath<GameObject>(KalakhPrefabBuilder.PrefabPath);
            arena.shadeModel = arena.lurkerModel = arena.bruteModel = kalakh; // each kind scales it to its own height
            arena.bossModel = AssetDatabase.LoadAssetAtPath<GameObject>(KalakhPrefabBuilder.BossPrefabPath);
            root.AddComponent<WarriorOutfit>(); // the Ascension dresses the Weaver here
            Set(root.AddComponent<Ascension>(), "entrance", entrance);
            root.AddComponent<WeaverSword>();
            root.AddComponent<RiftPlayer>();
            root.AddComponent<PlayerLocomotion>();
            var powers = Object.FindAnyObjectByType<PowerUnlocks>();
            if (powers == null) powers = root.AddComponent<PowerUnlocks>();
            var so = new SerializedObject(powers);
            so.FindProperty("startChapter").intValue = 0; // "Attic": no powers, the sword only
            so.ApplyModifiedPropertiesWithoutUndo();
            Selection.activeGameObject = root;
        }

        static RiftTear Tear(Transform parent, string name, Vector3 position, Quaternion facing, float height, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = facing;
            var tear = go.AddComponent<RiftTear>();
            var so = new SerializedObject(tear);
            so.FindProperty("height").floatValue = height;
            so.FindProperty("width").floatValue = width;
            so.ApplyModifiedPropertiesWithoutUndo();
            return tear;
        }

        static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Quaternion rotation, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        // ---------------------------------------------------------------- materials

        static Material Lit(string name, Color color, float metallic, float smoothness, string textureSet = null, float tiling = 1f)
        {
            Material m = Asset(name);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            if (textureSet != null)
            {
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture>($"{Textures}/{textureSet}/{textureSet}_Color.jpg"));
                var normal = AssetDatabase.LoadAssetAtPath<Texture>($"{Textures}/{textureSet}/{textureSet}_Normal.jpg");
                if (normal != null)
                {
                    m.SetTexture("_BumpMap", normal);
                    m.EnableKeyword("_NORMALMAP");
                }
                m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Glow(string name, Color emission)
        {
            Material m = Asset(name);
            m.SetColor("_BaseColor", new Color(0.05f, 0.01f, 0.08f));
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Asset(string name)
        {
            EnsureFolder(MaterialFolder);
            string path = $"{MaterialFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
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

        static void SetBool(Object target, string property, bool value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(property).boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
