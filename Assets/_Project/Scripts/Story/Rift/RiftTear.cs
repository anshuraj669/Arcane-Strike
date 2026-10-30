using System.Collections;
using ArcaneStrike.Combat;
using ArcaneStrike.VFX;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// A rift: a tall, jagged tear in the air, black inside with violet light crackling round its edge, specks of
    /// light being sucked into it, a violet glow and a low drone. Built from code at the object's position (the
    /// bottom of the tear), facing along its forward. <see cref="Open"/> and <see cref="Close"/> tear it open and
    /// seal it; <see cref="Openness"/> is how far open it is (0 shut .. 1 open).
    /// </summary>
    public class RiftTear : MonoBehaviour
    {
        [SerializeField] float height = 2.6f;
        [SerializeField] float width = 1.2f;
        [SerializeField] Color edgeColor = new Color(0.75f, 0.3f, 1f, 1f);
        [SerializeField] Color coreColor = new Color(0.02f, 0f, 0.04f, 0.97f);
        [SerializeField, Range(0f, 1f)] float openness;
        [SerializeField] bool openOnStart;

        public float Openness => openness;
        public float Height => height;
        /// <summary>Middle of the opening.</summary>
        public Vector3 Centre => transform.position + Vector3.up * height * 0.5f;

        const int Points = 24; // down each side
        Mesh mesh;
        LineRenderer edge;
        Light glow;
        AudioSource drone;
        ParticleSystem motes;
        float[] jag;
        Vector3[] outline;

        /// <summary>Makes a closed rift with its bottom at `position`, facing `facing`.</summary>
        public static RiftTear Create(Vector3 position, Quaternion facing, float height = 2.6f, float width = 1.2f)
        {
            var go = new GameObject("RiftTear");
            go.transform.SetPositionAndRotation(position, facing);
            var tear = go.AddComponent<RiftTear>();
            tear.height = height;
            tear.width = width;
            return tear;
        }

        void Start()
        {
            Build();
            if (openOnStart) StartCoroutine(Open(1.5f));
        }

        void Build()
        {
            if (mesh != null) return;
            jag = new float[Points * 2];
            for (int i = 0; i < jag.Length; i++) jag[i] = Random.Range(-0.25f, 0.25f);
            outline = new Vector3[Points * 2];

            var core = new GameObject("Inside");
            core.transform.SetParent(transform, false);
            mesh = new Mesh { name = "Rift" };
            core.AddComponent<MeshFilter>().sharedMesh = mesh;
            var material = AegisShield.CreateFaceMaterial(coreColor);
            material.SetFloat("_Cull", 0f); // seen from both sides
            material.name = "RiftInside";
            var r = core.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            edge = new GameObject("Edge").AddComponent<LineRenderer>();
            edge.transform.SetParent(transform, false);
            edge.useWorldSpace = false;
            edge.loop = true;
            edge.positionCount = outline.Length;
            edge.sharedMaterial = SparkHands.CreateGlowMaterial(SparkHands.CreateSoftLine(64));
            edge.startColor = edge.endColor = edgeColor;
            edge.widthMultiplier = 0.09f;

            glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = new Vector3(0f, height * 0.5f, 0.3f);
            glow.type = LightType.Point;
            glow.color = edgeColor;
            glow.range = height * 2.5f;
            glow.shadows = LightShadows.None;

            drone = StoryFx.CreateSource(gameObject, CombatAudio.Drone, true, 1f);
            drone.minDistance = 2f;
            drone.maxDistance = 25f;
            drone.Play();
            motes = StoryFx.CreateParticles(transform, "RiftMotes", true, 0f, 400);
            Apply();
        }

        /// <summary>Tears it open over `seconds`, with a crack of sound.</summary>
        public IEnumerator Open(float seconds)
        {
            Build();
            CombatAudio.Play(CombatAudio.Tear, Centre, 1f, 0f);
            float from = openness;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = t / seconds;
                openness = Mathf.Lerp(from, 1f, 1f - (1f - k) * (1f - k) * (1f - k)); // bursts open, then settles
                if (Random.value < 0.6f) StoryFx.Burst(motes, Centre + Random.insideUnitSphere * height * 0.3f, 3, 2f, 0.03f, 0.5f, edgeColor);
                yield return null;
            }
            openness = 1f;
        }

        /// <summary>Seals it over `seconds`: the edges pull together from the ends to a line, then nothing.</summary>
        public IEnumerator Close(float seconds)
        {
            Build();
            float from = openness;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                openness = Mathf.Lerp(from, 0f, Mathf.SmoothStep(0f, 1f, t / seconds));
                yield return null;
            }
            openness = 0f;
            StoryFx.Burst(motes, Centre, 80, 3f, 0.04f, 0.8f, edgeColor);
            CombatAudio.Play(CombatAudio.Tear, Centre, 0.8f, 0f);
        }

        /// <summary>Snaps it shut at once.</summary>
        public void SetOpenness(float value)
        {
            Build();
            openness = Mathf.Clamp01(value);
        }

        void Update()
        {
            if (mesh == null) return;
            Apply();
            // Light being drawn in from all round.
            if (openness > 0.05f && Random.value < 0.9f * openness)
            {
                Vector3 c = Centre;
                Vector3 p = c + (Random.onUnitSphere * Random.Range(1f, 2.5f)) * height * 0.5f;
                StoryFx.Emit(motes, p, (c - p) * 0.9f, Random.Range(0.015f, 0.04f), 1f, StoryFx.WithAlpha(edgeColor, 0.8f));
            }
        }

        // The outline: a tall lens, wider in the middle, ragged along both edges (the ragged bits flicker).
        void Apply()
        {
            float w = width * openness;
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < Points; i++)
                {
                    float u = (float)(i + 1) / (Points + 1);
                    int k = side * Points + i;
                    float flicker = Mathf.PerlinNoise(Time.time * 3f, k * 0.37f) - 0.5f;
                    float half = w * 0.5f * Mathf.Pow(Mathf.Sin(u * Mathf.PI), 0.8f) * (1f + jag[k] + flicker * 0.3f);
                    float y = height * (side == 0 ? u : 1f - u);
                    outline[k] = new Vector3(side == 0 ? half : -half, y, 0f);
                }
            edge.SetPositions(outline);
            edge.enabled = openness > 0.001f;
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 5f);
            glow.intensity = 3f * openness * pulse;
            drone.volume = 0.5f * openness;

            // A fan from the middle to the outline (plus its top and bottom points).
            var verts = new Vector3[outline.Length + 3];
            verts[0] = new Vector3(0f, height * 0.5f, 0f);
            verts[1] = Vector3.zero;
            verts[2] = Vector3.up * height;
            for (int i = 0; i < outline.Length; i++) verts[i + 3] = outline[i];
            var ring = new int[outline.Length + 2];
            ring[0] = 1; // bottom, up the right side, top, down the left side
            for (int i = 0; i < Points; i++) ring[i + 1] = i + 3;
            ring[Points + 1] = 2;
            for (int i = 0; i < Points; i++) ring[Points + 2 + i] = Points + i + 3;
            var tris = new int[ring.Length * 3];
            for (int i = 0; i < ring.Length; i++)
            {
                tris[i * 3] = 0;
                tris[i * 3 + 1] = ring[i];
                tris[i * 3 + 2] = ring[(i + 1) % ring.Length];
            }
            mesh.Clear();
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }
    }
}
