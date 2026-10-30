using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ArcaneStrike.Combat;
using ArcaneStrike.Core;
using ArcaneStrike.HandInput;
using ArcaneStrike.Story;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArcaneStrike.EditorTools
{
    /// <summary>
    /// Menu: Arcane Strike → Create Monster World Scene. The Kalakh's world, beyond the Ascension cave's tunnel
    /// (Tools/blender/build_monster_world.py → MonsterWorld.fbx, Tools/sky/build_night_sky.py → the sky): the player steps
    /// out of a cave mouth onto a raised basalt ledge above bare rocky plains, one jagged crimson peak ahead under a
    /// pink moon, dark ranges all round and a sky of stars and nebulae. Chapter 1's fight happens on the ledge (RiftArena:
    /// the Kalakh in waves, then the mini boss), the player arriving suited and armed. Puts the scenes in story order.
    /// </summary>
    static class MonsterWorldSceneBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/MonsterWorld.unity";
        const string TemplateScene = "Assets/Scenes/SampleScene.unity";
        const string Folder = "Assets/_Project/Art/Environment/MonsterWorld";
        const string ModelPath = Folder + "/MonsterWorld.fbx";
        const string ExtrasPath = Folder + "/WorldExtras.fbx";
        const string RootName = "MonsterWorld";

        [MenuItem("Arcane Strike/Create Monster World Scene")]
        static void Create()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Monster World", "Unity is still compiling. Wait for the spinner at the bottom right to stop, then click this again (or the scene is built with the old code).", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                EditorUtility.DisplayDialog("Monster World", $"{ModelPath} not found. Run Tools/blender/build_monster_world.py in Blender first.", "OK");
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(WeaverPrefabBuilder.PrefabPath) == null) WeaverPrefabBuilder.Build();
            KalakhPrefabBuilder.Build();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && !AssetDatabase.CopyAsset(TemplateScene, ScenePath))
            {
                EditorUtility.DisplayDialog("Monster World", $"Couldn't copy {TemplateScene} to {ScenePath}.", "OK");
                return;
            }
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (string name in new[] { "ExampleCubePrefab", "TrainingDummy", RootName })
            {
                var go = GameObject.Find(name);
                if (go != null) Object.DestroyImmediate(go);
            }
            Build(model);
            SceneOrder.PlaceRigAtOrigin(); // just out of the cave mouth, facing the volcano
            EditorSceneManager.SaveScene(scene);
            SceneOrder.Apply();
            Debug.Log($"[WorldBuilder] Saved {ScenePath}. Press Play here to fight straight away, or come through the cave's tunnel.");
        }

        static void Build(GameObject model)
        {
            var root = new GameObject(RootName);
            string points = AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "/MonsterWorld.points.json")?.text ?? "{}";

            // The world, with its materials; colliders on what can be walked on or against.
            var materials = AtticModelApplier.BuildMaterials(Folder + "/MonsterWorld.materials.json", Folder + "/Materials");
            var world = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(world, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            world.name = "World";
            world.transform.SetParent(root.transform, false);
            foreach (var r in world.GetComponentsInChildren<Renderer>(true))
            {
                Material[] shared = r.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                    if (shared[i] != null && materials.TryGetValue(shared[i].name, out Material m)) shared[i] = m;
                r.sharedMaterials = shared;
                string n = r.gameObject.name;
                if (n == "World_Ground" || n == "World_Rocks" || n == "World_CaveMouth" || n == "World_CaveTunnel")
                    r.gameObject.AddComponent<MeshCollider>();
                if (n == "World_Mountains" || n == "World_Peak") r.shadowCastingMode = ShadowCastingMode.Off; // far off
            }

            // Moonlight: pink, from the moon straight ahead and high up, so the peak stands dark against the sky and
            // the rocks catch a rim of it. The dark air is crimson, and indigo overhead, enough to fight by.
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional)
                {
                    l.color = new Color(1f, 0.74f, 0.92f);
                    l.intensity = 1.1f;
                    l.shadows = LightShadows.Soft;
                    l.transform.rotation = Quaternion.Euler(38f, 180f, 0f);
                }
            // The sky, painted by Tools/sky/build_night_sky.py: stars, nebulae and the moon.
            string skyPath = Folder + "/Materials/M_World_Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            Shader panoramic = Shader.Find("Skybox/Panoramic");
            if (sky != null && sky.shader != panoramic)
            {
                AssetDatabase.DeleteAsset(skyPath);
                sky = null;
            }
            if (sky == null)
            {
                sky = new Material(panoramic) { name = "M_World_Sky" };
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            sky.SetTexture("_MainTex", NightSky());
            sky.SetFloat("_Mapping", 1f); // latitude-longitude
            sky.SetFloat("_Exposure", 1f);
            sky.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 1f));
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.3f, 0.2f, 0.42f);
            RenderSettings.ambientEquatorColor = new Color(0.32f, 0.1f, 0.18f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.03f, 0.06f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.17f, 0.05f, 0.09f);
            RenderSettings.fogDensity = 0.0035f;
            foreach (Camera cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, 900f);
            }
            Vector3[] crater = Triplets(points, "crater"); // the peak's top

            // The world's extras (Tools/blender/build_world_extras.py): the remains of warriors who fought here before,
            // the boulders the Kalakh burst out of (and the rubble they leave), and the birds and beasts in the sky.
            var spawnRocks = new List<SpawnRock>();
            Flyers flyers = null;
            var extrasModel = AssetDatabase.LoadAssetAtPath<GameObject>(ExtrasPath);
            if (extrasModel != null)
            {
                var extraMaterials = AtticModelApplier.BuildMaterials(Folder + "/WorldExtras.materials.json", Folder + "/Materials");
                var extras = (GameObject)PrefabUtility.InstantiatePrefab(extrasModel);
                PrefabUtility.UnpackPrefabInstance(extras, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                extras.name = "WorldExtras";
                extras.transform.SetParent(root.transform, false);
                AtticModelApplier.PlaceAsBuilt(extras.transform, Folder + "/WorldExtras.placements.json");
                foreach (var r in extras.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] shared = r.sharedMaterials;
                    for (int i = 0; i < shared.Length; i++)
                        if (shared[i] != null && extraMaterials.TryGetValue(shared[i].name, out Material m)) shared[i] = m;
                    r.sharedMaterials = shared;
                }
                Transform Part(string name) => extras.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
                var chunks = new List<GameObject>();
                for (int i = 0; Part($"RockChunk_{i}") != null; i++)
                {
                    GameObject chunk = Part($"RockChunk_{i}").gameObject;
                    chunk.SetActive(false); // (copied when a boulder bursts)
                    chunks.Add(chunk);
                }
                for (int i = 0; Part($"SpawnRock_{i}") != null; i++)
                {
                    Transform rock = Part($"SpawnRock_{i}");
                    rock.gameObject.AddComponent<MeshCollider>();
                    var spawn = rock.gameObject.AddComponent<SpawnRock>();
                    Transform cracks = Part($"SpawnRockCracks_{i}");
                    if (cracks != null)
                    {
                        cracks.SetParent(rock, true); // (shakes with it)
                        spawn.cracks = cracks.GetComponent<Renderer>();
                    }
                    spawn.chunkTemplates = chunks.ToArray();
                    spawnRocks.Add(spawn);
                }
                flyers = root.AddComponent<Flyers>();
                flyers.birdBody = Part("Bird_Body");
                flyers.birdWingL = Part("Bird_WingL");
                flyers.birdWingR = Part("Bird_WingR");
                flyers.wyvernBody = Part("Wyvern_Body");
                flyers.wyvernWingL = Part("Wyvern_WingL");
                flyers.wyvernWingR = Part("Wyvern_WingR");
                if (crater.Length > 0) flyers.volcano = crater[0];
                int remains = extras.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("Remains_"));
                int debris = extras.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("Debris_"));
                Debug.Log($"[WorldBuilder] Extras: {remains} remains of fallen warriors, {debris} pieces of their gear (skulls, armour, shields, blades), {spawnRocks.Count} spawn boulders, {chunks.Count} rubble pieces, flyers {(flyers.birdBody != null ? "birds" : "no birds")} / {(flyers.wyvernBody != null ? "beasts" : "no beasts")}.");
            }
            else Debug.LogWarning($"[WorldBuilder] {ExtrasPath} not found: run Tools/blender/build_world_extras.py in Blender (no remains, flyers or boulders; the Kalakh break out of rough rocks).");

            // The way the player came (the cave mouth's threshold, behind them) and the far tear the boss comes out of.
            Vector3[] threshold = Triplets(points, "threshold");
            RiftTear entrance = Tear(root.transform, "EntranceTear", threshold.Length > 0 ? threshold[0] : new Vector3(0f, 0f, -2.2f),
                                     Quaternion.identity, 2.4f, 1.3f, new Color(1f, 0.5f, 0.15f), new Color(0.2f, 0.05f, 0.02f, 0.95f));
            RiftTear farEnd = Tear(root.transform, "FarTear", new Vector3(0f, 0f, 13f), Quaternion.Euler(0f, 180f, 0f), 4.5f, 2.2f,
                                   new Color(1f, 0.2f, 0.15f), new Color(0.06f, 0f, 0f, 0.97f));

            // The fight.
            var arena = root.AddComponent<RiftArena>();
            Set(arena, "entrance", entrance);
            Set(arena, "farEnd", farEnd);
            var kalakh = AssetDatabase.LoadAssetAtPath<GameObject>(KalakhPrefabBuilder.PrefabPath);
            arena.shadeModel = arena.lurkerModel = arena.bruteModel = kalakh;
            arena.bossModel = AssetDatabase.LoadAssetAtPath<GameObject>(KalakhPrefabBuilder.BossPrefabPath);
            // Fifteen soldiers in waves of 1, 2, 4 and 8, then the Warlord (Tools/blender/build_kalakh_breeds.py).
            var hunt = root.AddComponent<KalakhHunt>();
            hunt.model = kalakh;
            hunt.bossModel = arena.bossModel;
            hunt.rocks = spawnRocks.ToArray();
            hunt.soldier.model = AssetDatabase.LoadAssetAtPath<GameObject>(KalakhPrefabBuilder.BreedPrefabPath(hunt.soldier.breed));
            hunt.boss.model = AssetDatabase.LoadAssetAtPath<GameObject>(KalakhPrefabBuilder.BreedPrefabPath(hunt.boss.breed));
            Debug.Log($"[WorldBuilder] Soldier: {(hunt.soldier.model != null ? hunt.soldier.model.name : "the Kalakh")}. Boss: {(hunt.boss.model != null ? hunt.boss.model.name : "the boss Kalakh")}.");
            arena.hunt = hunt;
            var so = new SerializedObject(arena);
            so.FindProperty("ascensionOnArrival").boolValue = false; // the Ascension happened in the cave
            so.FindProperty("riftAtmosphere").boolValue = false;     // this world keeps its own sky
            SerializedProperty lines = so.FindProperty("arrivalLines");
            string[] arrival =
            {
                "We're through. This is their world, Weaver: the Kalakh's world.",
                "Bare rock, a dead moon, and not one honest light in it. Nothing here has seen true light in a thousand years.",
            };
            lines.arraySize = arrival.Length;
            for (int i = 0; i < arrival.Length; i++) lines.GetArrayElementAtIndex(i).stringValue = arrival[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            var outfit = root.AddComponent<WarriorOutfit>();
            SetBool(outfit, "dressedAtStart", true); // suited and armed from the cave
            root.AddComponent<WeaverSword>();
            root.AddComponent<RiftPlayer>();
            root.AddComponent<PlayerLocomotion>();
            var powers = Object.FindAnyObjectByType<PowerUnlocks>();
            if (powers == null) powers = root.AddComponent<PowerUnlocks>();
            var ps = new SerializedObject(powers);
            ps.FindProperty("startChapter").intValue = 0; // the sword only
            ps.ApplyModifiedPropertiesWithoutUndo();
            Selection.activeGameObject = root;
        }

        static RiftTear Tear(Transform parent, string name, Vector3 position, Quaternion facing, float height, float width, Color edge, Color core)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = facing;
            var tear = go.AddComponent<RiftTear>();
            var so = new SerializedObject(tear);
            so.FindProperty("height").floatValue = height;
            so.FindProperty("width").floatValue = width;
            so.FindProperty("edgeColor").colorValue = edge;
            so.FindProperty("coreColor").colorValue = core;
            so.ApplyModifiedPropertiesWithoutUndo();
            return tear;
        }

        // Tools/sky/build_night_sky.py's panorama, set up for the sky: no mipmaps (they'd show a seam where it wraps).
        static Texture2D NightSky()
        {
            string path = Folder + "/Materials/T_World_NightSky.png";
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // MonsterWorld.points.json: "key": [x, y, z] or "key": [[x, y, z], ...] → the points.
        static Vector3[] Triplets(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(\\[(?:[^\\[\\]]|\\[[^\\[\\]]*\\])*\\])");
            if (!m.Success) return new Vector3[0];
            float[] n = Regex.Matches(m.Groups[1].Value, @"-?\d+(\.\d+)?([eE]-?\d+)?").Cast<Match>()
                             .Select(x => float.Parse(x.Value, CultureInfo.InvariantCulture)).ToArray();
            var result = new Vector3[n.Length / 3];
            for (int i = 0; i < result.Length; i++) result[i] = new Vector3(n[i * 3], n[i * 3 + 1], n[i * 3 + 2]);
            return result;
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
