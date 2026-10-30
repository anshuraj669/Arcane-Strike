using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The lantern goes through the rift with the player. <see cref="Keep"/> (in the attic, as the lantern vanishes
    /// into the rift) makes a look-only copy of it (meshes and lights, no scripts or physics) that survives the
    /// scene change; <see cref="Take"/> (in the Rift) brings it out again. Played straight in the Rift, there is
    /// nothing to take: <see cref="Placeholder"/> makes a small brass lantern instead.
    /// </summary>
    public static class CarriedLantern
    {
        static GameObject kept;

        public static void Keep(Transform lantern)
        {
            if (lantern == null) return;
            if (kept != null) Object.Destroy(kept);
            bool wasActive = lantern.gameObject.activeSelf;
            lantern.gameObject.SetActive(false); // so the copy's scripts never wake
            kept = Object.Instantiate(lantern.gameObject);
            lantern.gameObject.SetActive(wasActive);
            kept.name = "CarriedLantern";
            kept.transform.SetPositionAndRotation(lantern.position, lantern.rotation);
            kept.transform.localScale = lantern.lossyScale;
            foreach (Component c in kept.GetComponentsInChildren<Component>(true))
                if (c is MonoBehaviour || c is Collider || c is Joint) Object.DestroyImmediate(c);
            foreach (Rigidbody body in kept.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);
            foreach (AudioSource a in kept.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(a);
            foreach (ParticleSystem p in kept.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (p.gameObject == kept) Object.DestroyImmediate(p); // never the copy itself
                else Object.DestroyImmediate(p.gameObject);
            }
            Object.DontDestroyOnLoad(kept);
        }

        /// <summary>The kept lantern, back in the current scene (null if none was kept).</summary>
        public static Transform Take()
        {
            if (kept == null) return null;
            GameObject go = kept;
            kept = null;
            SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
            go.SetActive(true);
            return go.transform;
        }

        /// <summary>A small brass lantern: a body, a cap, a handle ring and a glowing wick.</summary>
        public static Transform Placeholder()
        {
            var root = new GameObject("Lantern (placeholder)");
            var brass = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Brass" };
            brass.SetColor("_BaseColor", new Color(0.62f, 0.45f, 0.2f));
            brass.SetFloat("_Metallic", 0.85f);
            brass.SetFloat("_Smoothness", 0.55f);
            Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.09f, 0f), new Vector3(0.11f, 0.08f, 0.11f), brass);
            Part(PrimitiveType.Sphere, root.transform, new Vector3(0f, 0.19f, 0f), new Vector3(0.1f, 0.06f, 0.1f), brass);
            Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.005f, 0f), new Vector3(0.14f, 0.01f, 0.14f), brass);
            var ring = Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.25f, 0f), new Vector3(0.05f, 0.004f, 0.05f), brass);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Part(PrimitiveType.Sphere, root.transform, new Vector3(0f, 0.12f, 0f), Vector3.one * 0.035f, StoryFx.Additive); // the wick
            return root.transform;
        }

        static GameObject Part(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }
    }
}
