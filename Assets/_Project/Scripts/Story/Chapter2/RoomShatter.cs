using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// The bedroom breaking like glass at the end of Chapter 2's first act. <see cref="Crack"/> runs glowing gold cracks
    /// across the walls from the point nearest the player; <see cref="Shatter"/> swaps each surface (walls, ceiling,
    /// floor) for shards cut from the same surface (same material, with gold-hot edges) that fly away from the player and
    /// tumble out into the dark, the furniture drifting off after them, while the abyss (Resources/FX/Abyss.shader) and
    /// ash fill the space behind. A disc of floor stays under the player's feet. Lives on the room model (its space:
    /// floor y 0); <see cref="AddSurface"/> describes each flat surface in that space.
    /// </summary>
    public class RoomShatter : MonoBehaviour
    {
        class Surface
        {
            public Renderer renderer;
            public Vector3 centre, right, up; // up × right faces into the room
            public float width, height, tile;
        }

        class Shard
        {
            public int first, count;
            public Vector3 centre, velocity, axis;
            public float spin, delay;
            public bool stays;
        }

        class Piece
        {
            public Mesh mesh;
            public Vector3[] basePositions, baseNormals, positions, normals;
            public List<Shard> shards = new List<Shard>();
        }

        [SerializeField] float shardSize = 0.5f;
        [SerializeField] Color crackColor = new Color(4f, 2.3f, 0.6f, 1f);
        [Tooltip("Floor this close to the player (m) stays under their feet.")]
        [SerializeField] float keepUnderfoot = 0.9f;

        readonly List<Surface> surfaces = new List<Surface>();
        readonly List<Piece> pieces = new List<Piece>();
        readonly List<Transform> drifting = new List<Transform>();
        readonly List<LineRenderer> cracks = new List<LineRenderer>();
        Material edge, abyss;
        ParticleSystem ash, motes;
        float shatteredAt = -1f;
        Vector3 origin;

        public bool Shattered => shatteredAt >= 0f;

        /// <summary>A flat surface of the room (its renderer, its centre and axes in room space; up × right points inward).</summary>
        public void AddSurface(Renderer renderer, Vector3 centre, Vector3 right, Vector3 up, float width, float height, float tile)
        {
            if (renderer == null) return;
            surfaces.Add(new Surface { renderer = renderer, centre = centre, right = right.normalized, up = up.normalized, width = width, height = height, tile = tile });
        }

        /// <summary>Something that floats away when the room breaks (furniture, the blinds).</summary>
        public void AddDrifting(Transform t)
        {
            if (t != null) drifting.Add(t);
        }

        void Awake()
        {
            edge = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "ShardEdge" };
            edge.SetColor("_BaseColor", crackColor);
        }

        // ---------------------------------------------------------------- cracks

        /// <summary>Gold cracks race across every wall and the ceiling from the points nearest `from` (world), over `seconds`.</summary>
        public IEnumerator Crack(Vector3 from, float seconds)
        {
            var growing = new List<(LineRenderer line, List<Vector3> path)>();
            foreach (Surface s in surfaces)
            {
                Vector3 c = transform.TransformPoint(s.centre), right = transform.TransformDirection(s.right), up = transform.TransformDirection(s.up);
                Vector3 inward = Vector3.Cross(up, right);
                // The nearest point of this surface to the shock, kept inside it.
                Vector3 d = from - c;
                float x = Mathf.Clamp(Vector3.Dot(d, right), -s.width * 0.4f, s.width * 0.4f);
                float y = Mathf.Clamp(Vector3.Dot(d, up), -s.height * 0.4f, s.height * 0.4f);
                int branches = Random.Range(4, 7);
                for (int b = 0; b < branches; b++)
                {
                    var path = new List<Vector3>();
                    float angle = Random.value * Mathf.PI * 2f;
                    Vector2 p = new Vector2(x, y);
                    float reach = Random.Range(0.8f, 1.6f) * Mathf.Max(s.width, s.height) * 0.5f;
                    for (float walked = 0f; walked < reach; walked += 0.12f)
                    {
                        path.Add(c + right * p.x + up * p.y + inward * 0.012f);
                        angle += Random.Range(-0.6f, 0.6f);
                        p += new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 0.12f;
                        if (Mathf.Abs(p.x) > s.width * 0.5f || Mathf.Abs(p.y) > s.height * 0.5f) break;
                    }
                    if (path.Count < 2) continue;
                    var go = new GameObject("Crack");
                    go.transform.SetParent(transform, true);
                    var line = go.AddComponent<LineRenderer>();
                    line.sharedMaterial = edge;
                    line.widthMultiplier = Random.Range(0.006f, 0.012f);
                    line.shadowCastingMode = ShadowCastingMode.Off;
                    line.receiveShadows = false;
                    line.numCapVertices = 2;
                    line.positionCount = 0;
                    cracks.Add(line);
                    growing.Add((line, path));
                }
            }
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / seconds);
                foreach (var (line, path) in growing)
                {
                    int count = Mathf.Clamp(Mathf.CeilToInt(path.Count * u), 2, path.Count);
                    if (line.positionCount == count) continue;
                    line.positionCount = count;
                    for (int i = 0; i < count; i++) line.SetPosition(i, path[i]);
                }
                yield return null;
            }
        }

        // ---------------------------------------------------------------- shatter

        /// <summary>The room breaks apart round `player` (world, their feet), starting from `from` (the shockwave's centre).</summary>
        public void Shatter(Vector3 from, Vector3 player, Vector3 breachDirection)
        {
            origin = from;
            shatteredAt = Time.time;
            foreach (LineRenderer c in cracks)
                if (c != null) Destroy(c.gameObject);
            cracks.Clear();
            foreach (Surface s in surfaces) pieces.Add(Cut(s, from, player));
            foreach (Transform t in drifting)
                if (t != null) StartCoroutine(Drift(t, from));
            BuildAbyss(player, breachDirection);
        }

        Piece Cut(Surface s, Vector3 from, Vector3 player)
        {
            var mats = s.renderer.sharedMaterials;
            Material face = mats.Length > 0 ? mats[0] : null;
            s.renderer.enabled = false;
            bool floor = s.renderer.name.Contains("Floor");
            var col = s.renderer.GetComponent<Collider>();
            if (col != null && !floor) col.enabled = false; // (the floor stays solid underfoot)

            Vector3 c = transform.TransformPoint(s.centre), right = transform.TransformDirection(s.right), up = transform.TransformDirection(s.up);
            Vector3 inward = Vector3.Cross(up, right).normalized;
            int nx = Mathf.Max(2, Mathf.RoundToInt(s.width / shardSize)), ny = Mathf.Max(2, Mathf.RoundToInt(s.height / shardSize));
            // A grid, its inner points jittered, so the pieces are uneven triangles that still fit together.
            var grid = new Vector2[nx + 1, ny + 1];
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= ny; j++)
                {
                    float gx = (float)i / nx, gy = (float)j / ny;
                    bool inner = i > 0 && i < nx && j > 0 && j < ny;
                    if (inner)
                    {
                        gx += Random.Range(-0.35f, 0.35f) / nx;
                        gy += Random.Range(-0.35f, 0.35f) / ny;
                    }
                    grid[i, j] = new Vector2((gx - 0.5f) * s.width, (gy - 0.5f) * s.height);
                }

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var faces = new List<int>();
            var sides = new List<int>();
            var piece = new Piece();
            const float thick = 0.03f;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    Vector2 a = grid[i, j], b = grid[i + 1, j], cc = grid[i + 1, j + 1], d = grid[i, j + 1];
                    bool flip = Random.value < 0.5f;
                    foreach (var tri in flip ? new[] { (a, b, cc), (a, cc, d) } : new[] { (a, b, d), (b, cc, d) })
                    {
                        Vector3 W(Vector2 p, float depth) => c + right * p.x + up * p.y - inward * depth;
                        Vector3 p0 = W(tri.Item1, 0f), p1 = W(tri.Item2, 0f), p2 = W(tri.Item3, 0f);
                        Vector3 mid = (p0 + p1 + p2) / 3f;
                        var shard = new Shard { first = verts.Count, centre = mid };
                        // Front (facing the room) and back.
                        Vector2 UV(Vector2 p) => new Vector2((p.x + s.width * 0.5f) / s.tile, (p.y + s.height * 0.5f) / s.tile);
                        Add(verts, norms, uvs, faces, new[] { p0, p1, p2 }, inward, new[] { UV(tri.Item1), UV(tri.Item2), UV(tri.Item3) });
                        Vector3 q0 = W(tri.Item1, thick), q1 = W(tri.Item2, thick), q2 = W(tri.Item3, thick);
                        Add(verts, norms, uvs, faces, new[] { q0, q2, q1 }, -inward, new[] { UV(tri.Item1), UV(tri.Item3), UV(tri.Item2) });
                        // The broken edges, glowing.
                        Vector3[] f = { p0, p1, p2 }, k = { q0, q1, q2 };
                        for (int e = 0; e < 3; e++)
                        {
                            int n = (e + 1) % 3;
                            Vector3 outward = Vector3.Cross(f[n] - f[e], inward).normalized;
                            if (Vector3.Dot(outward, f[e] - mid) < 0f) outward = -outward;
                            Add(verts, norms, uvs, sides, new[] { f[e], k[e], k[n] }, outward, null);
                            Add(verts, norms, uvs, sides, new[] { f[e], k[n], f[n] }, outward, null);
                        }
                        shard.count = verts.Count - shard.first;

                        Vector3 flat = mid - player;
                        flat.y = 0f;
                        shard.stays = floor && flat.magnitude < keepUnderfoot;
                        Vector3 away = (mid - from).normalized;
                        shard.velocity = away * Random.Range(1.2f, 3.2f) + Random.insideUnitSphere * 0.6f + (floor ? Vector3.down * 1.2f : Vector3.zero);
                        shard.axis = Random.onUnitSphere;
                        shard.spin = Random.Range(40f, 220f);
                        shard.delay = Vector3.Distance(mid, from) * 0.09f + Random.Range(0f, 0.12f) + (floor ? 0.5f : 0f);
                        piece.shards.Add(shard);
                    }
                }

            var go = new GameObject(s.renderer.name + "_Shards"); // at the world origin: the vertices are in world space
            var mesh = new Mesh { name = go.name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(faces, 0);
            mesh.SetTriangles(sides, 1);
            mesh.MarkDynamic();
            mesh.bounds = new Bounds(c, Vector3.one * 200f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = new[] { face, edge };
            r.shadowCastingMode = ShadowCastingMode.Off;
            piece.mesh = mesh;
            piece.basePositions = verts.ToArray();
            piece.baseNormals = norms.ToArray();
            piece.positions = verts.ToArray();
            piece.normals = norms.ToArray();
            return piece;
        }

        static void Add(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tris, Vector3[] p, Vector3 normal, Vector2[] uvs)
        {
            int start = v.Count;
            for (int i = 0; i < p.Length; i++)
            {
                v.Add(p[i]);
                n.Add(normal);
                uv.Add(uvs != null ? uvs[i] : Vector2.zero);
            }
            // Wind the triangle to face `normal`.
            bool facing = Vector3.Dot(Vector3.Cross(p[1] - p[0], p[2] - p[0]), normal) > 0f;
            tris.Add(start);
            tris.Add(facing ? start + 1 : start + 2);
            tris.Add(facing ? start + 2 : start + 1);
        }

        void Update()
        {
            if (!Shattered) return;
            float age = Time.time - shatteredAt;
            foreach (Piece piece in pieces)
            {
                foreach (Shard s in piece.shards)
                {
                    if (s.stays) continue;
                    float t = Mathf.Max(0f, age - s.delay);
                    if (t <= 0f) continue;
                    // A jolt free, then drifting out (slowing as it goes, as if through water) and tumbling.
                    float travel = (1f - Mathf.Exp(-t * 0.35f)) / 0.35f;
                    Vector3 move = s.velocity * travel;
                    Quaternion turn = Quaternion.AngleAxis(s.spin * t, s.axis);
                    for (int i = s.first; i < s.first + s.count; i++)
                    {
                        piece.positions[i] = s.centre + move + turn * (piece.basePositions[i] - s.centre);
                        piece.normals[i] = turn * piece.baseNormals[i];
                    }
                }
                piece.mesh.vertices = piece.positions;
                piece.mesh.normals = piece.normals;
            }
            if (abyss != null) abyss.SetFloat("_Opacity", Mathf.SmoothStep(0f, 1f, age / 1.8f));
            if (ash != null && Random.value < 0.6f) EmitAsh();
        }

        IEnumerator Drift(Transform t, Vector3 from)
        {
            Vector3 start = t.position, away = (start - from);
            away.y = Mathf.Abs(away.y) * 0.3f;
            away = away.normalized * Random.Range(0.25f, 0.6f) + Vector3.up * Random.Range(0.05f, 0.2f);
            Vector3 axis = Random.onUnitSphere;
            float spin = Random.Range(8f, 30f), delay = Vector3.Distance(start, from) * 0.1f + 0.2f;
            Quaternion rotation = t.rotation;
            yield return new WaitForSeconds(delay);
            for (float a = 0f; a < 14f; a += Time.deltaTime)
            {
                t.position = start + away * a * (1f + a * 0.15f);
                t.rotation = Quaternion.AngleAxis(spin * a, axis) * rotation;
                yield return null;
            }
            t.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- the abyss

        void BuildAbyss(Vector3 player, Vector3 breach)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Abyss";
            Destroy(sphere.GetComponent<Collider>());
            sphere.transform.position = player + Vector3.up * 1.4f;
            sphere.transform.localScale = Vector3.one * 60f;
            abyss = new Material(Shader.Find("ArcaneStrike/Abyss")) { name = "Abyss" };
            abyss.SetTexture("_NoiseTex", Resources.Load<Texture2D>("FX/T_FireNoise"));
            abyss.SetVector("_Centre", sphere.transform.position);
            abyss.SetVector("_VortexDir", breach.normalized);
            abyss.SetFloat("_Opacity", 0f);
            var r = sphere.GetComponent<Renderer>();
            r.sharedMaterial = abyss;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            ash = StoryFx.CreateParticles(null, "Ash", false, -0.01f, 900);
            motes = StoryFx.CreateParticles(null, "VioletEmbers", true, -0.02f, 400);
            ash.transform.position = motes.transform.position = player;
            for (int i = 0; i < 160; i++) EmitAsh();
        }

        void EmitAsh()
        {
            Vector3 c = origin;
            Vector3 p = c + new Vector3(Random.Range(-7f, 7f), Random.Range(-3f, 5f), Random.Range(-7f, 7f));
            Vector3 v = new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(0.02f, 0.25f), Random.Range(-0.15f, 0.15f));
            if (Random.value < 0.7f)
                StoryFx.Emit(ash, p, v, Random.Range(0.01f, 0.04f), Random.Range(5f, 10f), new Color(0.12f, 0.1f, 0.12f, Random.Range(0.4f, 0.8f)));
            else
                StoryFx.Emit(motes, p, v * 1.5f, Random.Range(0.01f, 0.025f), Random.Range(3f, 6f), new Color(0.7f, 0.3f, 1f, 0.9f));
        }
    }
}
