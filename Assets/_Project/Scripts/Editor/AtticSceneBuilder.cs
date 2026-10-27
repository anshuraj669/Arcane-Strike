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
    /// Menu: Arcane Strike → Create Attic Scene (a copy of SampleScene's player rig plus the attic set,
    /// saved as Assets/_Project/Scenes/Attic.unity) or Add Attic Sequence To Open Scene.
    /// Builds the attic opening from placeholder shapes with every script wired up. Art can swap the
    /// placeholder meshes and materials later: keep the object names and components.
    /// The set is laid out with the player at its origin facing +Z; AtticGameManager moves it in front
    /// of the player at runtime.
    /// </summary>
    static class AtticSceneBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/Attic.unity";
        const string TemplateScene = "Assets/Scenes/SampleScene.unity";
        const string MaterialFolder = "Assets/_Project/Art/Environment/Attic/Placeholders";

        [MenuItem("Arcane Strike/Create Attic Scene")]
        static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && !AssetDatabase.CopyAsset(TemplateScene, ScenePath))
            {
                EditorUtility.DisplayDialog("Attic", $"Couldn't copy {TemplateScene} to {ScenePath}.", "OK");
                return;
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (GameObject.Find("Attic") != null)
            {
                EditorUtility.DisplayDialog("Attic", "This scene already has an Attic set. Delete it first to rebuild it.", "OK");
                return;
            }

            // SampleScene's test objects don't belong in the story.
            foreach (string name in new[] { "ExampleCubePrefab", "TrainingDummy" })
            {
                var go = GameObject.Find(name);
                if (go != null) Object.DestroyImmediate(go);
            }

            Build();
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[AtticBuilder] Saved {ScenePath}. Press Play; the Console guides each step ([Attic] ▶ ...). N does the current step for you.");
        }

        [MenuItem("Arcane Strike/Add Attic Sequence To Open Scene")]
        static void AddToOpenScene()
        {
            if (Object.FindAnyObjectByType<HandInputService>() == null)
            {
                EditorUtility.DisplayDialog("Attic", "No HandInputService in this scene. Open a scene with the player rig (like SampleScene) first.", "OK");
                return;
            }
            if (GameObject.Find("Attic") != null)
            {
                EditorUtility.DisplayDialog("Attic", "This scene already has an Attic set.", "OK");
                return;
            }
            Build();
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[AtticBuilder] Added the Attic set. Press Ctrl+S to save the scene.");
        }

        // ---------------------------------------------------------------- the set

        static void Build()
        {
            var m = new Materials();
            var root = new GameObject("Attic");
            Undo.RegisterCreatedObjectUndo(root, "Create attic");

            // Chest: hollow, so the lantern shows when the lid opens. The lid pivots on the back edge.
            Transform chest = Empty("Chest", root.transform, new Vector3(0f, 0f, 0.9f));
            const float w = 0.7f, h = 0.4f, d = 0.45f, t = 0.03f;
            Box("Bottom", chest, new Vector3(0f, t * 0.5f, 0f), new Vector3(w, t, d), m.Wood);
            Box("Front", chest, new Vector3(0f, h * 0.5f, -d * 0.5f + t * 0.5f), new Vector3(w, h, t), m.Wood);
            Box("Back", chest, new Vector3(0f, h * 0.5f, d * 0.5f - t * 0.5f), new Vector3(w, h, t), m.Wood);
            Box("Left", chest, new Vector3(-w * 0.5f + t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d), m.Wood);
            Box("Right", chest, new Vector3(w * 0.5f - t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d), m.Wood);
            Transform hinge = Empty("LidHinge", chest, new Vector3(0f, h, d * 0.5f));
            Box("Lid", hinge, new Vector3(0f, 0.03f, -d * 0.5f), new Vector3(w + 0.02f, 0.06f, d + 0.02f), m.WoodDark);
            Box("Clasp", hinge, new Vector3(0f, 0f, -d - 0.01f), new Vector3(0.08f, 0.08f, 0.02f), m.Brass);
            Transform lidHandle = Empty("Handle", hinge, new Vector3(0f, 0.06f, -d));
            var chestOpen = chest.gameObject.AddComponent<ChestOpen>();
            Set(chestOpen, "lid", hinge);
            Set(chestOpen, "handle", lidHandle);
            Set(chestOpen, "openAngle", 110f);
            chest.gameObject.AddComponent<DustTrigger>();

            // Lantern, lying in the chest.
            Transform lantern = Empty("Lantern", chest, new Vector3(0f, t, 0f));
            GameObject dirty = LanternShape("Dirty", lantern, m.DirtyMetal, m.DirtyGlass);
            GameObject clean = LanternShape("Clean", lantern, m.Brass, m.Glass);
            clean.SetActive(false);
            Renderer runeBand = Shape(PrimitiveType.Cylinder, "Runes", lantern, new Vector3(0f, 0.11f, 0f), new Vector3(0.122f, 0.012f, 0.122f), m.Rune, false);
            Transform ring = Empty("Handle", lantern, new Vector3(0f, 0.27f, 0f));
            Transform edge = Empty("SharpEdge", lantern, new Vector3(0.05f, 0.225f, -0.05f));
            Transform center = Empty("Center", lantern, new Vector3(0f, 0.12f, 0f));

            var pickup = lantern.gameObject.AddComponent<LanternPickup>();
            Set(pickup, "handle", ring);
            var dust = lantern.gameObject.AddComponent<LanternDustEffect>();
            var glow = lantern.gameObject.AddComponent<LanternGlow>();
            var runes = lantern.gameObject.AddComponent<RuneGlow>();
            runes.runeRenderers = new[] { runeBand };
            var sound = lantern.gameObject.AddComponent<LanternSound>();
            var cleaning = lantern.gameObject.AddComponent<LanternCleaning>();
            cleaning.dirtyLantern = dirty;
            cleaning.cleanLantern = clean;
            Set(cleaning, "pickup", pickup);
            Set(cleaning, "dust", dust);
            Set(cleaning, "sound", sound);
            Set(cleaning, "glow", glow);
            Set(cleaning, "runes", runes);
            var cut = lantern.gameObject.AddComponent<FingerCutEvent>();
            Set(cut, "sharpEdge", edge);
            Set(cut, "lanternCenter", center);
            var lanternFloat = lantern.gameObject.AddComponent<FloatingObject>();
            Set(lanternFloat, "liftHeight", 0.35f);
            Set(lanternFloat, "riseTime", 2.5f);

            // Crates, and old books lying around that will float.
            Transform crateLeft = Box("CrateLeft", root.transform, new Vector3(-1.05f, 0.25f, 1.45f), new Vector3(0.5f, 0.5f, 0.5f), m.WoodDark).transform;
            Transform crateRight = Box("CrateRight", root.transform, new Vector3(0.95f, 0.2f, 1.3f), new Vector3(0.5f, 0.4f, 0.5f), m.Wood).transform;
            Transform books = Empty("Books", root.transform, Vector3.zero);
            var bookSpots = new[]
            {
                new Vector3(-0.7f, 0.02f, 1.05f), new Vector3(-0.68f, 0.055f, 1.07f), new Vector3(-0.72f, 0.09f, 1.04f),
                new Vector3(0.9f, 0.42f, 1.25f), new Vector3(1.05f, 0.42f, 1.35f), new Vector3(-1.0f, 0.52f, 1.4f),
            };
            Material[] bookMats = { m.BookRed, m.BookGreen, m.BookBlue };
            var floating = new FloatingObject[bookSpots.Length];
            for (int i = 0; i < bookSpots.Length; i++)
            {
                GameObject book = Box($"Book{i + 1}", books, bookSpots[i], new Vector3(0.17f, 0.035f, 0.23f), bookMats[i % bookMats.Length]);
                book.transform.localRotation = Quaternion.Euler(0f, (i * 37) % 60 - 30f, 0f);
                floating[i] = book.AddComponent<FloatingObject>();
                Set(floating[i], "liftHeight", 0.7f + (i % 3) * 0.25f);
            }

            // Portal: hidden until the end; it moves onto the real wall when the room is known.
            Transform portalGo = Empty("Portal", root.transform, new Vector3(0f, 1.3f, 2.6f));
            var portal = portalGo.gameObject.AddComponent<PortalController>();
            portalGo.gameObject.SetActive(false);

            // Directors on the root.
            var shake = root.AddComponent<AtticShake>();
            shake.shakeTargets = new[] { chest, crateLeft, crateRight };
            var whispers = root.AddComponent<WhisperVoices>();
            var awakening = root.AddComponent<LanternAwakening>();
            awakening.floatingObjects = floating;
            Set(awakening, "lantern", lantern);
            Set(awakening, "pickup", pickup);
            Set(awakening, "glow", glow);
            Set(awakening, "runes", runes);
            Set(awakening, "sound", sound);
            Set(awakening, "whispers", whispers);
            Set(awakening, "shake", shake);
            Set(awakening, "portal", portal);
            var manager = root.AddComponent<AtticGameManager>();
            manager.chest = chestOpen;
            manager.awakening = awakening;
            Set(manager, "pickup", pickup);
            Set(manager, "cleaning", cleaning);
            Set(manager, "fingerCut", cut);
            Set(manager, "portal", portal);

            // The attic starts with no powers (chapter 0); the portal moves on to the next chapter.
            if (Object.FindAnyObjectByType<PowerUnlocks>() == null)
            {
                var powers = new GameObject("PowerUnlocks");
                Undo.RegisterCreatedObjectUndo(powers, "Add PowerUnlocks");
                powers.AddComponent<PowerUnlocks>();
            }

            Selection.activeGameObject = root;
        }

        /// <summary>Base, glass body, cap and top ring: a simple lantern silhouette (0.27 m tall).</summary>
        static GameObject LanternShape(string name, Transform parent, Material metal, Material glass)
        {
            Transform shape = Empty(name, parent, Vector3.zero);
            Shape(PrimitiveType.Cylinder, "Base", shape, new Vector3(0f, 0.01f, 0f), new Vector3(0.14f, 0.01f, 0.14f), metal, false);
            Shape(PrimitiveType.Cylinder, "Glass", shape, new Vector3(0f, 0.11f, 0f), new Vector3(0.11f, 0.09f, 0.11f), glass, false);
            Shape(PrimitiveType.Cylinder, "Cap", shape, new Vector3(0f, 0.215f, 0f), new Vector3(0.14f, 0.015f, 0.14f), metal, false);
            Renderer ring = Shape(PrimitiveType.Cylinder, "Ring", shape, new Vector3(0f, 0.255f, 0f), new Vector3(0.06f, 0.006f, 0.06f), metal, false);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return shape.gameObject;
        }

        // ---------------------------------------------------------------- helpers

        static Transform Empty(string name, Transform parent, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        static GameObject Box(string name, Transform parent, Vector3 localPosition, Vector3 size, Material material) =>
            Shape(PrimitiveType.Cube, name, parent, localPosition, size, material, true).gameObject;

        static Renderer Shape(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        static void Set(Object target, string property, object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(property);
            if (p == null)
            {
                Debug.LogError($"[AtticBuilder] {target.GetType().Name}.{property} not found");
                return;
            }
            switch (value)
            {
                case Object o: p.objectReferenceValue = o; break;
                case float f: p.floatValue = f; break;
                case bool b: p.boolValue = b; break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Placeholder URP Lit materials, saved as assets so the scene keeps them.</summary>
        class Materials
        {
            public readonly Material Wood, WoodDark, Brass, DirtyMetal, Glass, DirtyGlass, Rune, BookRed, BookGreen, BookBlue;

            public Materials()
            {
                EnsureFolder(MaterialFolder);
                Wood = Get("Wood", new Color(0.42f, 0.27f, 0.15f), 0f, 0.25f);
                WoodDark = Get("WoodDark", new Color(0.27f, 0.17f, 0.1f), 0f, 0.2f);
                Brass = Get("Brass", new Color(0.85f, 0.65f, 0.3f), 0.9f, 0.7f);
                DirtyMetal = Get("DirtyMetal", new Color(0.3f, 0.27f, 0.22f), 0.4f, 0.1f);
                Glass = Get("Glass", new Color(1f, 0.92f, 0.7f), 0f, 0.9f);
                DirtyGlass = Get("DirtyGlass", new Color(0.45f, 0.42f, 0.35f), 0f, 0.1f);
                Rune = Get("Rune", new Color(0.12f, 0.1f, 0.08f), 0.3f, 0.4f);
                Rune.EnableKeyword("_EMISSION");
                Rune.SetColor("_EmissionColor", Color.black);
                BookRed = Get("BookRed", new Color(0.45f, 0.1f, 0.08f), 0f, 0.3f);
                BookGreen = Get("BookGreen", new Color(0.12f, 0.3f, 0.15f), 0f, 0.3f);
                BookBlue = Get("BookBlue", new Color(0.12f, 0.16f, 0.4f), 0f, 0.3f);
                AssetDatabase.SaveAssets();
            }

            static Material Get(string name, Color color, float metallic, float smoothness)
            {
                string path = $"{MaterialFolder}/M_Attic_{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null) return mat; // keep art's edits if the placeholder was already changed
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = $"M_Attic_{name}" };
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Metallic", metallic);
                mat.SetFloat("_Smoothness", smoothness);
                AssetDatabase.CreateAsset(mat, path);
                return mat;
            }

            static void EnsureFolder(string path)
            {
                if (AssetDatabase.IsValidFolder(path)) return;
                int slash = path.LastIndexOf('/');
                string parent = path.Substring(0, slash);
                EnsureFolder(parent);
                AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
            }
        }
    }
}
