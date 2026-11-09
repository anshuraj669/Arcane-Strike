using System.Collections.Generic;
using ArcaneStrike.Props;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Years of dust on one of the attic's boxes. The lid won't lift until the player wipes the box clean with the old
    /// cloth (<see cref="CleaningCloth"/>), held in their hand and rubbed over the box: the cloth goes wherever the
    /// player's hand goes, so the wiping is their own movement. The dust is a grey film over the box (a slightly
    /// larger copy of its own surfaces) that thins as they rub, with dust puffing from under the cloth.
    /// </summary>
    public class BoxDust : MonoBehaviour
    {
        [Tooltip("Metres of brisk rubbing to wipe the box clean (about 3 seconds of wiping).")]
        [SerializeField] float rubToClean = 1.6f;
        [Tooltip("The cloth this close (m) to the box's surface is rubbing it.")]
        [SerializeField] float reach = 0.1f;
        [Tooltip("Slower cloth movement (m/s) doesn't count as rubbing.")]
        [SerializeField] float minRubSpeed = 0.1f;
        [SerializeField] Color dustColor = new Color(0.66f, 0.62f, 0.55f, 0.72f);

        /// <summary>0 (thick with dust) .. 1 (wiped clean).</summary>
        public float Progress { get; private set; }
        public bool IsClean => Progress >= 1f;

        static Material film;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        ChestOpen box;
        readonly List<Renderer> films = new List<Renderer>();
        readonly List<Renderer> surfaces = new List<Renderer>();
        MaterialPropertyBlock block;
        ParticleSystem puffs;
        Vector3 lastCloth;
        bool hadCloth;
        int lastQuarter;

        /// <summary>Covers `chest` in dust and keeps its lid shut until it is wiped clean.</summary>
        public void Setup(ChestOpen chest)
        {
            box = chest;
            box.Locked = true;
            block = new MaterialPropertyBlock();
            if (film == null) film = MakeFilm();
            foreach (MeshFilter mf in chest.GetComponentsInChildren<MeshFilter>())
            {
                var source = mf.GetComponent<Renderer>();
                if (source == null || !source.enabled || mf.sharedMesh == null) continue;
                surfaces.Add(source);
                var go = new GameObject("Dust");
                go.transform.SetParent(mf.transform, false);
                go.transform.localScale = Vector3.one * 1.015f; // (just outside the surface it covers)
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = Fill(film, mf.sharedMesh.subMeshCount);
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                films.Add(r);
            }
            puffs = StoryFx.CreateParticles(transform, "DustPuffs", false, -0.05f, 120);
            Show();
        }

        static Material[] Fill(Material m, int count)
        {
            var all = new Material[Mathf.Max(count, 1)];
            for (int i = 0; i < all.Length; i++) all[i] = m;
            return all;
        }

        /// <summary>Wiped clean at once (keyboard / debug).</summary>
        public void CleanNow()
        {
            Progress = 1f;
            Finish();
        }

        void Update()
        {
            if (box == null || IsClean || box.IsOpen) return;
            CleaningCloth cloth = CleaningCloth.Held;
            if (cloth == null)
            {
                hadCloth = false;
                return;
            }
            Vector3 p = cloth.RubPoint;
            if (!Near(p, out Vector3 onSurface))
            {
                hadCloth = false;
                return;
            }
            if (hadCloth)
            {
                float moved = Vector3.Distance(p, lastCloth);
                float speed = moved / Mathf.Max(Time.deltaTime, 1e-4f);
                if (speed > minRubSpeed)
                {
                    Progress = Mathf.Min(1f, Progress + moved / rubToClean);
                    if (Random.value < Mathf.Clamp01(speed * 0.6f))
                        StoryFx.Emit(puffs, onSurface, Random.insideUnitSphere * 0.12f + Vector3.up * 0.1f, Random.Range(0.03f, 0.07f),
                                     Random.Range(0.8f, 1.5f), new Color(0.75f, 0.7f, 0.62f, 0.5f));
                    int quarter = Mathf.FloorToInt(Progress * 4f);
                    if (quarter > lastQuarter && quarter < 4)
                    {
                        lastQuarter = quarter;
                        Debug.Log($"[Attic] {box.name}: {quarter * 25}% wiped clean");
                    }
                    Show();
                    if (IsClean) Finish();
                }
            }
            lastCloth = p;
            hadCloth = true;
        }

        // The cloth is at the box: within `reach` of the box's outside (its renderers' bounds).
        bool Near(Vector3 p, out Vector3 onSurface)
        {
            onSurface = p;
            float best = float.MaxValue;
            foreach (Renderer r in surfaces)
            {
                if (r == null) continue;
                Vector3 c = r.bounds.ClosestPoint(p);
                float d = (c - p).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    onSurface = c;
                }
            }
            return best <= reach * reach;
        }

        void Show()
        {
            Color c = dustColor;
            c.a *= 1f - Progress;
            block.SetColor(BaseColor, c);
            foreach (Renderer r in films)
            {
                if (r == null) continue;
                r.enabled = c.a > 0.01f;
                r.SetPropertyBlock(block);
            }
        }

        void Finish()
        {
            Show();
            if (box != null) box.Locked = false;
            for (int i = 0; i < 20 && puffs != null; i++)
                StoryFx.Emit(puffs, transform.position + Vector3.up * 0.3f + Random.insideUnitSphere * 0.25f,
                             Random.insideUnitSphere * 0.2f + Vector3.up * 0.15f, Random.Range(0.04f, 0.09f), Random.Range(1f, 2f),
                             new Color(0.8f, 0.75f, 0.66f, 0.45f));
            Debug.Log($"[Attic] {(box != null ? box.name : name)} is wiped clean: now lift its lid");
        }

        // A see-through grey film, mottled, as dust lies on old wood.
        static Material MakeFilm()
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "BoxDust" };
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "DustMottle", wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.06f, y * 0.06f) * 0.6f + Mathf.PerlinNoise(x * 0.25f + 40f, y * 0.25f) * 0.4f;
                    byte a = (byte)(255 * Mathf.Clamp01(0.45f + n * 0.7f));
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            m.SetTexture("_BaseMap", tex);
            m.SetColor(BaseColor, Color.white);
            return m;
        }
    }
}
