using System;
using ArcaneStrike.HandInput;
using ArcaneStrike.Props;
using ArcaneStrike.Story;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ArcaneStrike.EditorTools
{
    /// <summary>
    /// Menu: Arcane Strike → Chapter 2 → the scene builders, one per scene, in story order.
    /// Scene 1, Attic Wake: a copy of the Attic scene (the same house, moonlight, wind and creaks) with Chapter 1's
    /// story taken out: no intro walk, no lantern (the rift took it), the chest standing open and empty, and a wooden
    /// crate behind the spot where the player comes to. <see cref="AtticWakeDirector"/> runs the scene. Also makes the
    /// end of Chapter 1 (MonsterWorld) lead here and puts the scene in the Build Profile after it. Running it again
    /// rebuilds the scene from the current Attic.
    /// Scene 2, Bedroom: the Attic scene's player rig and settings with the attic taken away and the bedroom model
    /// (Tools/blender/build_bedroom.py) put in its place, moonlight through the window, the alarm clock reading 23:42,
    /// and <see cref="BedroomDirector"/> (with <see cref="RoomShatter"/>) running the scene.
    /// </summary>
    static class Chapter2SceneBuilder
    {
        const string AtticScene = "Assets/_Project/Scenes/Attic.unity";
        const string MonsterWorldScene = "Assets/_Project/Scenes/MonsterWorld.unity";
        public const string AtticWakeScene = "Assets/_Project/Scenes/Chapter2_AtticWake.unity";
        public const string BedroomScene = "Assets/_Project/Scenes/Chapter2_Bedroom.unity";
        const string BedroomFolder = "Assets/_Project/Art/Environment/Bedroom";
        const string BedroomModel = BedroomFolder + "/Models/Bedroom.fbx";

        // Chapter 1's attic story: none of it runs when the player wakes up there.
        static readonly Type[] Chapter1Story =
        {
            typeof(AtticGameManager), typeof(AtticIntro), typeof(LanternAwakening), typeof(PortalController),
            typeof(FingerCutEvent), typeof(WhisperVoices), typeof(AtticShake), typeof(StoryAnimations), typeof(DustTrigger),
            typeof(LanternPickup), typeof(LanternCleaning), typeof(LanternDustEffect), typeof(LanternGlow), typeof(RuneGlow),
            typeof(LanternSound),
        };

        [MenuItem("Arcane Strike/Chapter 2/1 · Create Attic Wake Scene")]
        static void CreateAtticWake()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(AtticWakeScene) != null) AssetDatabase.DeleteAsset(AtticWakeScene);
            if (!AssetDatabase.CopyAsset(AtticScene, AtticWakeScene))
            {
                EditorUtility.DisplayDialog("Chapter 2", $"Couldn't copy {AtticScene} to {AtticWakeScene}.", "OK");
                return;
            }
            var scene = EditorSceneManager.OpenScene(AtticWakeScene, OpenSceneMode.Single);

            var atmosphere = Object.FindAnyObjectByType<AtticAtmosphere>();
            if (atmosphere == null)
            {
                EditorUtility.DisplayDialog("Chapter 2", "The Attic scene has no attic model (AtticAtmosphere). Run Apply Attic Model on it first.", "OK");
                return;
            }
            Transform model = atmosphere.transform;

            StripChapter1();

            // The lantern went into the rift, and the rift is gone.
            var chest = Object.FindAnyObjectByType<ChestOpen>();
            if (chest != null)
            {
                Transform lantern = FindDeep(chest.transform, "Lantern");
                if (lantern != null) lantern.gameObject.SetActive(false);
                OpenLid(chest);
            }
            var portal = GameObject.Find("Portal");
            if (portal != null) portal.SetActive(false);

            var director = model.gameObject.AddComponent<AtticWakeDirector>();
            PlaceCrate(model, director);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            SceneOrder.Apply();
            LinkFromChapter1();
            EditorSceneManager.OpenScene(AtticWakeScene, OpenSceneMode.Single);
            Debug.Log($"[Chapter2] Saved {AtticWakeScene}. Press Play to test scene 1 (N skips a moment, E heads downstairs).");
        }

        [MenuItem("Arcane Strike/Chapter 2/2 · Create Bedroom Scene")]
        static void CreateBedroom()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(BedroomModel);
            if (model == null)
            {
                EditorUtility.DisplayDialog("Chapter 2", $"{BedroomModel} not found. Run Tools/blender/build_bedroom.py in Blender first.", "OK");
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BedroomScene) != null) AssetDatabase.DeleteAsset(BedroomScene);
            if (!AssetDatabase.CopyAsset(AtticScene, BedroomScene))
            {
                EditorUtility.DisplayDialog("Chapter 2", $"Couldn't copy {AtticScene} to {BedroomScene}.", "OK");
                return;
            }
            var scene = EditorSceneManager.OpenScene(BedroomScene, OpenSceneMode.Single);

            // Keep the player rig, the hands and the settings; the attic and its house go.
            var attic = GameObject.Find("Attic");
            if (attic != null) Object.DestroyImmediate(attic);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "BedroomModel";
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            AtticModelApplier.PlaceAsBuilt(instance.transform, $"{BedroomFolder}/Models/Bedroom.placements.json");
            var materials = AtticModelApplier.BuildMaterials($"{BedroomFolder}/Models/Bedroom.materials.json", $"{BedroomFolder}/Materials");
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                Material[] shared = r.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                    if (shared[i] != null && materials.TryGetValue(shared[i].name, out Material m)) shared[i] = m;
                r.sharedMaterials = shared;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; // the walls keep the moon out but for the window
            }
            foreach (var f in instance.GetComponentsInChildren<MeshFilter>(true))
                if (f.GetComponent<Collider>() == null && !f.name.StartsWith("Decor_Blinds")) f.gameObject.AddComponent<MeshCollider>();

            instance.AddComponent<PlayerLocomotion>();
            instance.AddComponent<HandPhysics>();
            instance.AddComponent<RoomShatter>();
            instance.AddComponent<BedroomDirector>();
            AddClock(instance.transform);
            Moonlight();
            SceneOrder.PlaceRigAtOrigin();
            if (Object.FindAnyObjectByType<Core.PowerUnlocks>() == null) new GameObject("PowerUnlocks").AddComponent<Core.PowerUnlocks>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            SceneOrder.Apply();
            Debug.Log($"[Chapter2] Saved {BedroomScene}. Press Play to test scene 2 (N skips a line, 1 / 2 choose, E touches the Seal).");
        }

        // The alarm clock on the nightstand glows 23:42 in red.
        static void AddClock(Transform model)
        {
            Transform clock = FindDeep(model, "Decor_AlarmClock");
            if (clock == null) return;
            var go = new GameObject("ClockDigits");
            go.transform.SetParent(clock, false);
            go.transform.position = model.TransformPoint(new Vector3(-1.889f, 0.752f, 1.48f));
            go.transform.rotation = model.rotation * Quaternion.LookRotation(Vector3.left);
            go.transform.localScale = new Vector3(1f / clock.lossyScale.x, 1f / clock.lossyScale.y, 1f / clock.lossyScale.z);
            var text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            text.text = "23:42";
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 64;
            text.characterSize = 0.0042f;
            text.color = new Color(1f, 0.12f, 0.08f, 1f);
            var glow = new GameObject("ClockGlow").AddComponent<Light>();
            glow.transform.SetParent(clock, false);
            glow.transform.position = model.TransformPoint(new Vector3(-1.84f, 0.76f, 1.48f));
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.15f, 0.08f);
            glow.range = 0.6f;
            glow.intensity = 0.35f;
            glow.shadows = LightShadows.None;
        }

        // Pale moonlight from the window in the right-hand wall, down across the floor towards the rug.
        static void Moonlight()
        {
            Light moon = null;
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { moon = l; break; }
            if (moon == null)
            {
                moon = new GameObject("Moon").AddComponent<Light>();
                moon.type = LightType.Directional;
            }
            moon.transform.rotation = Quaternion.LookRotation(new Vector3(-0.75f, -0.5f, 0.35f));
            moon.color = new Color(0.62f, 0.72f, 1f);
            moon.intensity = 1.5f;
            moon.shadows = LightShadows.Soft;
            moon.shadowStrength = 1f;
            RenderSettings.sun = moon;
        }

        static void StripChapter1()
        {
            // A few passes, in case one component needs another gone first.
            for (int pass = 0; pass < 3; pass++)
                foreach (Type type in Chapter1Story)
                    foreach (Object c in Object.FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None))
                        Object.DestroyImmediate(c);
        }

        // The chest stands open as the player left it, and nothing inside it stirs any more.
        static void OpenLid(ChestOpen chest)
        {
            var so = new SerializedObject(chest);
            var lid = so.FindProperty("lid").objectReferenceValue as Transform;
            float angle = so.FindProperty("openAngle").floatValue;
            if (lid != null) lid.localRotation *= Quaternion.Euler(angle, 0f, 0f);
            chest.enabled = false;
        }

        // The wooden crate the player stumbles back into: just behind where they stop.
        static void PlaceCrate(Transform model, AtticWakeDirector director)
        {
            Transform crate = FindDeep(model, "Prop_wooden_crate_01");
            if (crate == null)
            {
                Debug.LogWarning("[Chapter2] No Prop_wooden_crate_01 in the attic: the player stumbles back into thin air.");
                return;
            }
            var so = new SerializedObject(director);
            Vector3 wake = so.FindProperty("wakeAt").vector3Value;
            float facing = so.FindProperty("wakeFacing").floatValue, stumble = so.FindProperty("stumble").floatValue;
            Vector3 forward = Quaternion.Euler(0f, facing, 0f) * Vector3.forward;
            Vector3 stop = wake - forward * stumble;

            crate.rotation = model.rotation * Quaternion.Euler(0f, facing + 90f, 0f);
            Bounds b = Bounds(crate);
            float half = Mathf.Max(b.extents.x, b.extents.z);
            Vector3 want = model.TransformPoint(stop - forward * (half + 0.2f));
            Vector3 shift = want - b.center;
            shift.y = 0f;
            crate.position += shift;
        }

        static Bounds Bounds(Transform t)
        {
            var renderers = t.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(t.position, Vector3.one * 0.5f);
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        // The end of Chapter 1 wakes the player in the attic.
        static void LinkFromChapter1()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MonsterWorldScene) == null) return;
            var world = EditorSceneManager.OpenScene(MonsterWorldScene, OpenSceneMode.Single);
            var arena = Object.FindAnyObjectByType<RiftArena>();
            if (arena == null) return;
            var so = new SerializedObject(arena);
            so.FindProperty("nextScene").stringValue = System.IO.Path.GetFileNameWithoutExtension(AtticWakeScene);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(world);
            EditorSceneManager.SaveScene(world);
            Debug.Log("[Chapter2] MonsterWorld's ending now leads to Chapter2_AtticWake.");
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform hit = FindDeep(child, name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
