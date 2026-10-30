using System;
using System.Collections.Generic;
using System.IO;
using ArcaneStrike.HandInput;
using ArcaneStrike.Props;
using ArcaneStrike.Story;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ArcaneStrike.EditorTools
{
    /// <summary>
    /// Menu: Arcane Strike → Apply Attic Model. Puts the Blender store room (Tools/blender/build_attic.py →
    /// Attic_Room.fbx) into the open scene's Attic set:
    /// - the room goes under the set, and each story prop (chest body and lid, both lanterns, crates, books)
    ///   moves onto the placeholder that carries its scripts, so the story still works;
    /// - URP materials are made from Attic_Room.materials.json and the textures in ../Textures;
    /// - the floor, walls and furniture get colliders, and every Grab_* object (and the story books) becomes a
    ///   physics prop the player can pick up and throw;
    /// - lights, AtticAtmosphere (dust, window beam, candle, wind, creaks) and HandPhysics are added;
    /// - the story scripts get their models: RuneGlow the carved runes, LanternGlow the flame, PortalController
    ///   the stone ring, the rag becomes the CleaningCloth, and the loose props float in the awakening.
    /// The placeholder shapes are hidden, not deleted (their colliders stay). Running it again replaces the
    /// model, so re-export from Blender and apply again after changes. Remove Attic Model brings the
    /// placeholders back.
    /// </summary>
    static class AtticModelApplier
    {
        const string ModelPath = "Assets/_Project/Art/Environment/Attic/Models/Attic_Room.fbx";
        const string MaterialsJson = "Assets/_Project/Art/Environment/Attic/Models/Attic_Room.materials.json";
        const string PlacementsJson = "Assets/_Project/Art/Environment/Attic/Models/Attic_Room.placements.json";
        const string TextureFolder = "Assets/_Project/Art/Environment/Attic/Textures";
        const string MaterialFolder = "Assets/_Project/Art/Environment/Attic/Materials";
        const string ScenePath = "Assets/_Project/Scenes/Attic.unity";
        const string InstanceName = "AtticModel";

        // FBX object → placeholder (path under the Attic root). Their pivots match, so each sits at local zero.
        static readonly (string part, string target)[] Parts =
        {
            ("Chest_Body", "Chest"),
            ("Chest_Lid", "Chest/LidHinge"),
            ("Lantern_Dirty", "Chest/Lantern/Dirty"),
            ("Lantern_Clean", "Chest/Lantern/Clean"),
            ("Crate_Left", "CrateLeft"),
            ("Crate_Right", "CrateRight"),
            ("Book_1", "Books/Book1"), ("Book_2", "Books/Book2"), ("Book_3", "Books/Book3"),
            ("Book_4", "Books/Book4"), ("Book_5", "Books/Book5"), ("Book_6", "Books/Book6"),
            ("Lantern_Runes", "Chest/Lantern/Runes"),
            ("Lantern_Shard", "Chest/Lantern/SharpEdge"),
            ("Lantern_Flame", "Chest/Lantern/Center"),
            ("Portal_Frame", "Portal"),
        };

        // Parts whose own pivot is right and the placeholder moves to it instead: the real chest's lid hinge and
        // the tip of the lantern's sharp shard.
        static readonly HashSet<string> MovePlaceholder = new HashSet<string> { "Chest_Lid", "Lantern_Shard" };

        // Model objects that things can rest on, walk on and bump into: floors, walls, stairs, the gate, the
        // house, and every Prop_* except the cobwebs.
        static bool IsSolid(string name) =>
            name == "Room_Floor" || name == "Room_Walls" || name.StartsWith("House_") || name.StartsWith("Gate_") ||
            (name.StartsWith("Prop_") && name != "Prop_Cobwebs");

        [Serializable]
        class MaterialDef
        {
            public string name;
            public float[] color;
            public float smoothness;
            public float metallic;
            public float alpha;
            public float[] emission;
            public string texture;
            public float tile;
            public bool emissive;
            // Props made by prepare_prop.py bring their own textures instead of a shared texture set.
            public string baseMap;
            public string normalMap;
            public string maskMap;
            public float smoothnessScale; // 0 = 1: the mask map's smoothness as it is
            public bool cutout;           // leaves and petals cut out of flat cards by the colour texture's alpha
        }

        [Serializable]
        class Placement
        {
            public string name;
            public float[] position;
            public float[] rotation; // x, y, z, w
            public float scale;
        }

        [Serializable]
        class PlacementList
        {
            public Placement[] objects;
        }

        [Serializable]
        class MaterialList
        {
            public MaterialDef[] materials;
        }

        [MenuItem("Arcane Strike/Apply Attic Model")]
        static void ApplyFromMenu()
        {
            if (Apply()) EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        [MenuItem("Arcane Strike/Remove Attic Model")]
        static void RemoveFromMenu()
        {
            Transform attic = FindAttic();
            if (attic == null) return;
            Remove(attic);
            foreach (var r in attic.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = true;
            Transform runes = attic.Find("Chest/Lantern/Runes");
            var runeGlow = attic.GetComponentInChildren<RuneGlow>(true);
            if (runes != null && runeGlow != null) SetObjects(runeGlow, "runeRenderers", new Object[] { runes.GetComponent<Renderer>() });
            EditorSceneManager.MarkSceneDirty(attic.gameObject.scene);
            Debug.Log("[AtticModel] Removed the model; the placeholder shapes are back.");
        }

        /// <summary>For batch mode: Unity -batchmode -executeMethod ArcaneStrike.EditorTools.AtticModelApplier.ApplyToAtticScene -quit</summary>
        public static void ApplyToAtticScene()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (Apply()) EditorSceneManager.SaveScene(scene);
        }

        static bool Apply()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[AtticModel] {ModelPath} not found. Run Tools/blender/build_attic.py in Blender first.");
                return false;
            }
            Transform attic = FindAttic();
            if (attic == null) return false;

            Dictionary<string, Material> materials = BuildMaterials(MaterialsJson, MaterialFolder);

            Remove(attic);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            Undo.RegisterCreatedObjectUndo(instance, "Apply attic model");
            instance.name = InstanceName;
            instance.transform.SetParent(attic, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            PlaceAsBuilt(instance.transform, PlacementsJson);

            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                Material[] shared = r.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                    if (shared[i] != null && materials.TryGetValue(shared[i].name, out Material m)) shared[i] = m;
                r.sharedMaterials = shared;
                r.shadowCastingMode = ShadowCastingMode.On; // the roof and walls keep the moon out, except through the windows
            }

            foreach (var r in attic.GetComponentsInChildren<MeshRenderer>(true))
                if (!r.transform.IsChildOf(instance.transform)) r.enabled = false;

            foreach (var (partName, targetPath) in Parts)
            {
                Transform part = FindDeep(instance.transform, partName);
                Transform target = attic.Find(targetPath);
                if (part == null || target == null)
                {
                    Debug.LogWarning($"[AtticModel] Skipped {partName}: {(part == null ? "not in the model" : $"no {targetPath} in the Attic set")}.");
                    continue;
                }
                Vector3 offset = attic.InverseTransformPoint(part.position) - attic.InverseTransformPoint(target.position);
                if (MovePlaceholder.Contains(partName))
                {
                    Undo.RecordObject(target, "Move placeholder to the model");
                    target.position = part.position;
                }
                else if (offset.magnitude > 0.02f)
                {
                    Debug.LogWarning($"[AtticModel] {partName}'s pivot is {offset.magnitude:F2} m from {targetPath}; it snaps onto the placeholder.");
                }

                // Keep the part's own size (the chest is built at 80%), cancelling the placeholder box's scale.
                Vector3 s = target.lossyScale, own = part.lossyScale;
                Quaternion rotation = Quaternion.Inverse(instance.transform.rotation) * part.rotation;
                part.SetParent(target, false);
                part.localPosition = Vector3.zero;
                part.localRotation = rotation;
                part.localScale = new Vector3(own.x / s.x, own.y / s.y, own.z / s.z);
            }

            FitLidHandle(attic);
            WireStory(attic);
            AddPhysics(instance.transform, attic);
            AddLights(instance.transform);
            SetUpNight(instance.transform);
            instance.AddComponent<AtticAtmosphere>();
            instance.AddComponent<HandPhysics>();
            instance.AddComponent<PlayerLocomotion>();
            instance.AddComponent<PointGrab>(); // point the left index finger at something to grab it
            instance.AddComponent<AtticIntro>();
            // The Weaver (the main character, his outfit and sword) is a separate model, used only in the ShadowRift
            // scene, where the Ascension happens now: rebuild its prefab from Weaver.fbx, but keep it out of the attic.
            WeaverPrefabBuilder.Build();
            // Powers: none in the attic; the Ascension moves to chapter 1 (the Jyoti Seal).
            if (Object.FindAnyObjectByType<Core.PowerUnlocks>() == null) instance.AddComponent<Core.PowerUnlocks>();
            // The house stands where it was built and the intro walks the player in, so the set isn't moved
            // in front of the player any more.
            var manager = attic.GetComponent<AtticGameManager>();
            if (manager != null) SetBool(manager, "placeInFrontOfPlayer", false);
            Debug.Log($"[AtticModel] Applied {ModelPath} to the Attic set. Save the scene (Ctrl+S).");
            return true;
        }

        // Puts every object exactly where Blender built it (Attic_Room.placements.json, in the attic's space), so
        // however the FBX axes get converted on import, nothing ends up tipped over or out of place.
        internal static void PlaceAsBuilt(Transform instance, string placementsJson)
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(placementsJson);
            if (json == null)
            {
                Debug.LogWarning($"[AtticModel] {placementsJson} not found: keeping the FBX's own placement. Re-run the Blender script.");
                return;
            }
            int moved = 0, missing = 0;
            float worst = 0f;
            foreach (Placement p in JsonUtility.FromJson<PlacementList>(json.text).objects)
            {
                Transform t = FindDeep(instance, p.name);
                if (t == null)
                {
                    missing++;
                    continue;
                }
                Vector3 position = instance.TransformPoint(new Vector3(p.position[0], p.position[1], p.position[2]));
                Quaternion rotation = instance.rotation * new Quaternion(p.rotation[0], p.rotation[1], p.rotation[2], p.rotation[3]);
                worst = Mathf.Max(worst, Vector3.Distance(t.position, position));
                t.SetPositionAndRotation(position, rotation);
                Vector3 parentScale = t.parent != null ? t.parent.lossyScale : Vector3.one;
                t.localScale = new Vector3(p.scale / parentScale.x, p.scale / parentScale.y, p.scale / parentScale.z);
                moved++;
            }
            Debug.Log($"[AtticModel] Placed {moved} objects as built in Blender (the import had them up to {worst:F2} m out){(missing > 0 ? $"; {missing} not in the model" : "")}.");
        }

        static void Remove(Transform attic)
        {
            Transform old = attic.Find(InstanceName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            foreach (var (partName, targetPath) in Parts)
            {
                Transform target = attic.Find(targetPath);
                Transform part = target != null ? target.Find(partName) : null;
                if (part != null) Undo.DestroyObjectImmediate(part.gameObject);
            }
            Transform books = attic.Find("Books");
            if (books == null) return;
            foreach (Transform book in books)
            {
                var g = book.GetComponent<Grabbable>();
                if (g != null) Undo.DestroyObjectImmediate(g);
                var body = book.GetComponent<Rigidbody>();
                if (body != null) Undo.DestroyObjectImmediate(body);
            }
        }

        // ---------------------------------------------------------------- story scripts

        static void WireStory(Transform attic)
        {
            Transform runes = attic.Find("Chest/Lantern/Runes/Lantern_Runes");
            var runeGlow = attic.GetComponentInChildren<RuneGlow>(true);
            if (runeGlow != null)
            {
                // The carved glyphs glow; without them the placeholder band comes back.
                Transform band = attic.Find("Chest/Lantern/Runes");
                Renderer target = runes != null ? runes.GetComponent<Renderer>() : band != null ? band.GetComponent<Renderer>() : null;
                if (runes == null && target != null) target.enabled = true;
                SetObjects(runeGlow, "runeRenderers", new Object[] { target });
            }

            Transform flame = attic.Find("Chest/Lantern/Center/Lantern_Flame");
            var glow = attic.GetComponentInChildren<LanternGlow>(true);
            if (glow != null) SetObject(glow, "flame", flame != null ? flame.GetComponent<Renderer>() : null);

            // The ending is now the rift: it tears open in front of the player and pulls them through (the Ascension is
            // on the other side, in the ShadowRift scene) instead of the void.
            var awakening = attic.GetComponentInChildren<LanternAwakening>(true);
            if (awakening != null)
            {
                var so = new SerializedObject(awakening);
                so.FindProperty("ending").enumValueIndex = (int)LanternAwakening.Ending.IntoTheRift;
                so.FindProperty("riftScene").stringValue = "AscensionCave"; // the cave, where the Ascension happens
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            Transform ring = attic.Find("Portal/Portal_Frame");
            var portal = attic.GetComponentInChildren<PortalController>(true);
            if (portal != null) SetObject(portal, "frame", ring);
        }

        static void SetObject(Object target, string property, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(property).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetObjects(Object target, string property, Object[] values)
        {
            var so = new SerializedObject(target);
            SerializedProperty list = so.FindProperty(property);
            list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetBool(Object target, string property, bool value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(property).boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ChestOpen lifts the lid by its Handle: put it on the real lid's front edge.
        static void FitLidHandle(Transform attic)
        {
            Transform hinge = attic.Find("Chest/LidHinge");
            Transform lid = hinge != null ? hinge.Find("Chest_Lid") : null;
            Transform handle = hinge != null ? hinge.Find("Handle") : null;
            var filter = lid != null ? lid.GetComponent<MeshFilter>() : null;
            if (handle == null || filter == null || filter.sharedMesh == null) return;
            Bounds b = filter.sharedMesh.bounds; // in the lid's space; the lid sits at the hinge, turned to face the player
            Vector3 front = lid.TransformPoint(new Vector3(b.center.x, b.max.y * 0.5f, b.max.z));
            Vector3 back = lid.TransformPoint(new Vector3(b.center.x, b.max.y * 0.5f, b.min.z));
            Vector3 chest = attic.Find("Chest").position;
            // The handle goes on whichever edge is nearer the player's side of the chest (away from the hinge).
            Vector3 grab = Vector3.Distance(front, hinge.position) > Vector3.Distance(back, hinge.position) ? front : back;
            Undo.RecordObject(handle, "Fit lid handle");
            handle.position = new Vector3(grab.x, grab.y, grab.z);
        }

        // Midnight: the scene's directional light becomes the moon, low and cold, shining in through the attic
        // window onto the chest (with shadows, so only the windows let it in), and a night sky with the moon on it.
        static void SetUpNight(Transform instance)
        {
            Light moon = null;
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { moon = l; break; }
            if (moon == null)
            {
                moon = new GameObject("Moon").AddComponent<Light>();
                moon.type = LightType.Directional;
                Undo.RegisterCreatedObjectUndo(moon.gameObject, "Add moon");
            }
            Undo.RecordObject(moon, "Moon");
            Undo.RecordObject(moon.transform, "Moon");
            moon.name = "Moon";
            moon.transform.rotation = instance.rotation * Quaternion.LookRotation(new Vector3(0f, -0.588f, -0.809f));
            moon.color = new Color(0.62f, 0.72f, 1f);
            moon.intensity = 1.6f;
            moon.shadows = LightShadows.Soft;
            moon.shadowStrength = 1f;

            const string skyPath = MaterialFolder + "/M_NightSky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural")) { name = "M_NightSky" };
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            sky.SetFloat("_SunDisk", 2f);            // the moon's disc, where the moonlight comes from
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_SunSizeConvergence", 8f);
            sky.SetFloat("_AtmosphereThickness", 0.35f);
            sky.SetColor("_SkyTint", new Color(0.12f, 0.14f, 0.25f));
            sky.SetColor("_GroundColor", new Color(0.02f, 0.02f, 0.03f));
            sky.SetFloat("_Exposure", 0.35f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.sun = moon;
        }

        static void SetFloat(Object target, string property, float value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(property).floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- physics

        static void AddPhysics(Transform instance, Transform attic)
        {
            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
                if (IsSolid(filter.name) && filter.GetComponent<Collider>() == null)
                    filter.gameObject.AddComponent<MeshCollider>();

            // The garden sways in the wind.
            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
                if (filter.name.StartsWith("Garden_") && filter.GetComponent<VFX.WindSway>() == null)
                    filter.gameObject.AddComponent<VFX.WindSway>();

            int grabbables = 0;
            var floating = new List<Object>();
            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.name.StartsWith("Grab_") || filter.sharedMesh == null) continue;
                Bounds b = filter.sharedMesh.bounds;
                var box = filter.gameObject.AddComponent<BoxCollider>();
                box.center = b.center;
                box.size = b.size;
                var body = filter.gameObject.AddComponent<Rigidbody>();
                body.mass = Mathf.Clamp(b.size.x * b.size.y * b.size.z * 600f, 0.1f, 3f); // about as dense as wood
                var g = filter.gameObject.AddComponent<Grabbable>();
                g.sound = SoundFor(filter.name);
                if (filter.name.StartsWith("Grab_Cloth")) filter.gameObject.AddComponent<CleaningCloth>();

                // The attic reacts in the awakening: loose things drift up too.
                var lift = filter.gameObject.AddComponent<FloatingObject>();
                SetFloat(lift, "liftHeight", UnityEngine.Random.Range(0.25f, 0.6f));
                SetFloat(lift, "riseTime", UnityEngine.Random.Range(2f, 3.5f));
                floating.Add(lift);
                grabbables++;
            }
            var awakening = attic.GetComponentInChildren<LanternAwakening>(true);
            if (awakening != null) SetObjects(awakening, "floatingProps", floating.ToArray());

            // The story books can be picked up too, until the awakening lifts them.
            Transform books = attic.Find("Books");
            if (books != null)
                foreach (Transform book in books)
                {
                    if (book.GetComponent<Collider>() == null) continue;
                    var body = book.GetComponent<Rigidbody>(); // (Unity objects: no ?? here)
                    if (body == null) body = book.gameObject.AddComponent<Rigidbody>();
                    body.mass = 0.5f;
                    var g = book.GetComponent<Grabbable>();
                    if (g == null) g = book.gameObject.AddComponent<Grabbable>();
                    g.sound = Grabbable.Sound.Paper;
                    grabbables++;
                }
            Debug.Log($"[AtticModel] {grabbables} props can be picked up.");
        }

        static Grabbable.Sound SoundFor(string name)
        {
            if (name.Contains("Jar") || name.Contains("Bottle")) return Grabbable.Sound.Glass;
            if (name.Contains("Mug") || name.Contains("Candle")) return Grabbable.Sound.Metal;
            if (name.Contains("Book") || name.Contains("Scroll") || name.Contains("Cloth")) return Grabbable.Sound.Paper;
            return Grabbable.Sound.Wood;
        }

        // ---------------------------------------------------------------- materials

        internal static Dictionary<string, Material> BuildMaterials(string materialsJson, string materialFolder)
        {
            var result = new Dictionary<string, Material>();
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(materialsJson);
            if (json == null)
            {
                Debug.LogWarning($"[AtticModel] {materialsJson} not found: keeping the FBX's own materials.");
                return result;
            }
            if (!AssetDatabase.IsValidFolder(materialFolder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(materialFolder).Replace('\\', '/'), Path.GetFileName(materialFolder));

            foreach (MaterialDef d in JsonUtility.FromJson<MaterialList>(json.text).materials)
            {
                if (!string.IsNullOrEmpty(d.texture)) PrepareTextures(d.texture);
                PrepareTexture(d.baseMap, TextureImporterType.Default, true);
                PrepareTexture(d.normalMap, TextureImporterType.NormalMap, false);
                PrepareTexture(d.maskMap, TextureImporterType.Default, false);
                string path = $"{materialFolder}/{d.name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool created = m == null;
                if (created) m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = d.name };
                Setup(m, d);
                if (created) AssetDatabase.CreateAsset(m, path);
                else EditorUtility.SetDirty(m);
                result[d.name] = m;
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        // URP Lit: texture × tint, normal map, metallic (R) + smoothness (A) map, occlusion, transparency, glow.
        static void Setup(Material m, MaterialDef d)
        {
            m.SetColor("_BaseColor", new Color(d.color[0], d.color[1], d.color[2], d.alpha));
            m.SetFloat("_Metallic", d.metallic);
            m.SetFloat("_Smoothness", d.smoothness);

            string set = string.IsNullOrEmpty(d.texture) ? null : $"{TextureFolder}/{d.texture}/{d.texture}";
            Texture baseMap = Load(d.baseMap) ?? (set != null ? AssetDatabase.LoadAssetAtPath<Texture>(set + "_Color.jpg") : null);
            Texture normal = Load(d.normalMap) ?? (set != null ? AssetDatabase.LoadAssetAtPath<Texture>(set + "_Normal.jpg") : null);
            Texture metalSmooth = Load(d.maskMap) ?? (set != null ? AssetDatabase.LoadAssetAtPath<Texture>(set + "_MetalSmooth.png") : null);
            Texture occlusion = set != null ? AssetDatabase.LoadAssetAtPath<Texture>(set + "_AO.jpg") : null;
            m.SetTexture("_BaseMap", baseMap);
            m.SetTexture("_BumpMap", normal);
            m.SetTexture("_MetallicGlossMap", metalSmooth);
            m.SetTexture("_OcclusionMap", occlusion);
            Keyword(m, "_NORMALMAP", normal != null);
            Keyword(m, "_METALLICSPECGLOSSMAP", metalSmooth != null);
            Keyword(m, "_OCCLUSIONMAP", occlusion != null);
            if (metalSmooth != null) m.SetFloat("_Smoothness", d.smoothnessScale > 0f ? d.smoothnessScale : 1f); // the map's own smoothness (scaled)

            bool transparent = d.alpha < 0.999f;
            m.SetFloat("_Surface", transparent ? 1f : 0f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            m.SetFloat("_DstBlend", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            m.SetFloat("_SrcBlendAlpha", 1f);
            m.SetFloat("_DstBlendAlpha", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            m.SetFloat("_ZWrite", transparent ? 0f : 1f);
            Keyword(m, "_SURFACE_TYPE_TRANSPARENT", transparent);
            m.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");
            m.renderQueue = transparent ? (int)RenderQueue.Transparent : -1;
            m.SetShaderPassEnabled("ShadowCaster", !transparent);

            // Cut-out leaves and petals: clipped to shape by the colour texture's alpha, seen from both sides.
            m.SetFloat("_AlphaClip", d.cutout ? 1f : 0f);
            m.SetFloat("_Cutoff", 0.5f);
            Keyword(m, "_ALPHATEST_ON", d.cutout);
            m.SetFloat("_Cull", d.cutout ? 0f : 2f);
            if (d.cutout)
            {
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }

            // Emissive materials keep the keyword even while black, so a script can light them (and builds keep the variant).
            bool glows = d.emission != null && (d.emission[0] + d.emission[1] + d.emission[2]) > 0f;
            m.SetColor("_EmissionColor", glows ? new Color(d.emission[0], d.emission[1], d.emission[2]) : Color.black);
            Keyword(m, "_EMISSION", glows || d.emissive);
            m.globalIlluminationFlags = glows ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        // Texture assets are never "fake null", so ?? is safe here (unlike components).
        static Texture Load(string path) => string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture>(path);

        static void PrepareTexture(string path, TextureImporterType type, bool srgb)
        {
            if (string.IsNullOrEmpty(path) || !(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
            if (importer.textureType == type && importer.sRGBTexture == srgb && importer.maxTextureSize <= 1024) return;
            importer.textureType = type;
            if (type != TextureImporterType.NormalMap) importer.sRGBTexture = srgb;
            importer.maxTextureSize = Mathf.Min(importer.maxTextureSize, 1024);
            importer.SaveAndReimport();
        }

        static void Keyword(Material m, string keyword, bool on)
        {
            if (on) m.EnableKeyword(keyword);
            else m.DisableKeyword(keyword);
        }

        // Normal maps as normal maps, data maps as linear, and at most 1K for Quest.
        static void PrepareTextures(string set)
        {
            foreach (string suffix in new[] { "_Color.jpg", "_Normal.jpg", "_MetalSmooth.png", "_AO.jpg" })
            {
                string path = $"{TextureFolder}/{set}/{set}{suffix}";
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                bool changed = false;
                if (suffix == "_Normal.jpg" && importer.textureType != TextureImporterType.NormalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    changed = true;
                }
                bool linear = suffix == "_MetalSmooth.png" || suffix == "_AO.jpg";
                if (linear && importer.sRGBTexture)
                {
                    importer.sRGBTexture = false;
                    changed = true;
                }
                if (importer.maxTextureSize > 1024)
                {
                    importer.maxTextureSize = 1024;
                    changed = true;
                }
                if (changed) importer.SaveAndReimport();
            }
        }

        // ---------------------------------------------------------------- lights

        // Midnight inside: a faint cold glow of moonlight bouncing round the attic, and the moonbeam from the
        // window onto the chest (the moon's own shadows shape it; this spot makes it read clearly on Quest).
        static void AddLights(Transform parent)
        {
            var lights = new GameObject("Lights").transform;
            lights.SetParent(parent, false);

            var bounce = new GameObject("MoonBounce").AddComponent<Light>();
            bounce.transform.SetParent(lights, false);
            bounce.transform.localPosition = new Vector3(0f, 2.4f, 1.5f);
            bounce.type = LightType.Point;
            bounce.color = new Color(0.55f, 0.65f, 0.95f);
            bounce.intensity = 0.9f;
            bounce.range = 8f;
            bounce.shadows = LightShadows.None;

            var window = new GameObject("WindowLight").AddComponent<Light>();
            window.transform.SetParent(lights, false);
            window.transform.localPosition = new Vector3(0f, 2.775f, 4.1f);
            window.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, -0.588f, -0.809f));
            window.type = LightType.Spot;
            window.color = new Color(0.62f, 0.72f, 1f);
            window.intensity = 3.5f;
            window.range = 6f;
            window.spotAngle = 26f;
            window.innerSpotAngle = 12f;
            window.shadows = LightShadows.None;
        }

        // ---------------------------------------------------------------- helpers

        static Transform FindAttic()
        {
            var go = GameObject.Find("Attic");
            if (go == null) Debug.LogError("[AtticModel] No Attic set in this scene. Open Assets/_Project/Scenes/Attic.unity.");
            return go != null ? go.transform : null;
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
