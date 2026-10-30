using System.Collections.Generic;
using ArcaneStrike.HandInput;
using ArcaneStrike.Story;
using UnityEngine;

namespace ArcaneStrike.Combat
{
    /// <summary>
    /// The Weaver's two arms, once the outfit is worn: each reaches from its shoulder to its hand, bending at the elbow
    /// (two-bone IK), and ends in a gauntlet with five real fingers, bronze across the knuckles and at the cuff. A
    /// tracked hand's fingers follow the player's own. The right hand closes round the sword whenever it is drawn, so
    /// the hand goes wherever the blade goes (the draw over the shoulder, every swing, tracked or on the keys). A hand
    /// that isn't tracked rests low in front, fingers relaxed. Each arm is one skinned mesh made here (three materials,
    /// no shadows), its bones set directly every frame: light enough for a Quest. They stand in for the worn sleeves.
    /// </summary>
    [DefaultExecutionOrder(230)] // after the hands and the sword have moved
    public class WeaverArms : MonoBehaviour
    {
        [SerializeField] float upperLength = 0.3f;
        [SerializeField] float foreLength = 0.28f;
        [SerializeField] float thickness = 0.048f;
        [Tooltip("The right shoulder relative to the head (m): right, down, back. The left mirrors it.")]
        [SerializeField] Vector3 shoulder = new Vector3(0.18f, 0.22f, 0.05f);
        [Tooltip("From the middle of the sword's grip back to the wrist (m).")]
        [SerializeField] float gripToWrist = 0.085f;
        [Tooltip("How far the wrist sits above the grip, so the palm rests on it (m).")]
        [SerializeField] float palmOverGrip = 0.028f;
        [SerializeField] Color leather = new Color(0.075f, 0.05f, 0.04f);
        [SerializeField] Color bronze = new Color(0.62f, 0.44f, 0.2f);

        // Finger bones: from joint, to joint, radius (HandJoint order).
        static readonly (int a, int b, float r)[] Fingers =
        {
            (1, 2, 0.0125f), (2, 3, 0.0112f), (3, 4, 0.0098f),
            (5, 6, 0.0104f), (6, 7, 0.0094f), (7, 8, 0.0082f),
            (9, 10, 0.0108f), (10, 11, 0.0096f), (11, 12, 0.0084f),
            (13, 14, 0.0102f), (14, 15, 0.0092f), (15, 16, 0.008f),
            (17, 18, 0.0092f), (18, 19, 0.0082f), (19, 20, 0.0072f),
        };
        const int Upper = 0, Fore = 1, Palm = 2, FirstFinger = 3;

        class Arm
        {
            public HandSide side;
            public Renderer renderer;
            public Transform[] bones;
            public readonly Vector3[] joints = new Vector3[HandFrame.JointCount];
        }

        readonly Arm[] arms = { new Arm { side = HandSide.Left }, new Arm { side = HandSide.Right } };
        WarriorOutfit outfit;
        WeaverSword weapon;
        HandInputService input;
        Transform head;
        Material leatherMaterial, bronzeMaterial;
        float nextLook;
        bool built, shown;

        void Awake()
        {
            outfit = GetComponent<WarriorOutfit>();
            input = FindAnyObjectByType<HandInputService>();
        }

        void LateUpdate()
        {
            bool show = outfit != null && outfit.IsWorn && !outfit.BodyShown;
            if (show && head == null) head = StoryFx.Head(input);
            if (head == null) show = false;
            if (!show)
            {
                Show(false);
                return;
            }
            if (!built) Build();
            Show(true);
            if (weapon == null && Time.time >= nextLook)
            {
                nextLook = Time.time + 1f;
                weapon = FindAnyObjectByType<WeaverSword>();
            }
            Transform held = weapon != null && weapon.Drawn ? weapon.Held : null;
            foreach (Arm arm in arms) Pose(arm, held);
        }

        void Show(bool on)
        {
            if (on == shown) return;
            shown = on;
            foreach (Arm arm in arms)
                if (arm.renderer != null) arm.renderer.enabled = on;
            if (outfit != null) outfit.TakeArms(on);
        }

        void Pose(Arm arm, Transform held)
        {
            bool right = arm.side == HandSide.Right;
            Vector3 flat = StoryFx.Flat(head.forward), across = Vector3.Cross(Vector3.up, flat), side = right ? across : -across;
            HandFrame f = input != null ? input.Get(arm.side) : null;
            Vector3 wrist, forward, back;
            if (right && held != null)
            {
                // Closed round the grip: the thumb along the blade, the knuckles towards the edge.
                Vector3 blade = held.up;
                back = held.forward;
                forward = Vector3.Cross(blade, -back).normalized;
                wrist = held.position - forward * gripToWrist + back * palmOverGrip;
                Template(arm.joints, wrist, forward, back, blade, 1f);
            }
            else if (f != null && f.IsTracked)
            {
                wrist = f.Wrist;
                forward = f.HandForward;
                back = -f.PalmNormal;
                System.Array.Copy(f.Joints, arm.joints, HandFrame.JointCount);
            }
            else
            {
                forward = (flat - Vector3.up * 0.35f).normalized;
                back = Vector3.Cross(forward, across).normalized;
                if (Vector3.Dot(back, Vector3.up) < 0f) back = -back;
                wrist = head.position + flat * 0.3f - Vector3.up * 0.42f + side * 0.17f;
                Template(arm.joints, wrist, forward, back, -side, 0.3f);
            }

            // Shoulder to wrist, the elbow dropping down and out.
            Vector3 root = head.position + side * shoulder.x - Vector3.up * shoulder.y - flat * shoulder.z;
            Vector3 to = wrist - root;
            float reach = Mathf.Max(to.magnitude, 0.05f), a = upperLength, b = foreLength;
            if (reach > (a + b) * 0.995f) // further than an arm: it stretches a little rather than letting go
            {
                float s = reach / ((a + b) * 0.995f);
                a *= s;
                b *= s;
            }
            Vector3 dir = to / reach;
            float along = (a * a - b * b + reach * reach) / (2f * reach);
            float rise = Mathf.Sqrt(Mathf.Max(a * a - along * along, 0f));
            Vector3 bend = Vector3.ProjectOnPlane(-Vector3.up * 0.7f + side * 0.7f - flat * 0.2f, dir);
            bend = bend.sqrMagnitude > 1e-5f ? bend.normalized : side;
            Vector3 elbow = root + dir * along + bend * rise;
            Bone(arm.bones[Upper], root, elbow, thickness * 1.15f);
            Bone(arm.bones[Fore], elbow, wrist, thickness);

            arm.bones[Palm].SetPositionAndRotation(wrist, Quaternion.LookRotation(back, forward));
            for (int i = 0; i < Fingers.Length; i++)
                Bone(arm.bones[FirstFinger + i], arm.joints[Fingers[i].a], arm.joints[Fingers[i].b], Fingers[i].r);
        }

        // A bone's mesh is a unit tube along +Y: scale stretches it from `from` to `to`.
        static void Bone(Transform t, Vector3 from, Vector3 to, float radius)
        {
            Vector3 d = to - from;
            float length = d.magnitude;
            if (length < 1e-4f) return;
            t.SetPositionAndRotation(from, Quaternion.FromToRotation(Vector3.up, d / length));
            t.localScale = new Vector3(radius, length, radius);
        }

        // A hand's joints from its wrist: `thumb` is the way the thumb lies, `curl` 0 (flat) .. 1 (a closed fist).
        static void Template(Vector3[] j, Vector3 wrist, Vector3 forward, Vector3 back, Vector3 thumb, float curl)
        {
            j[0] = wrist;
            Vector3 root = wrist + thumb * 0.024f + forward * 0.022f - back * 0.012f;
            Vector3 way = (thumb * 0.62f + forward * (0.6f + curl * 0.25f) - back * (0.25f + curl * 0.3f)).normalized;
            j[1] = root;
            j[2] = root + way * 0.036f;
            way = (way + forward * curl * 0.7f - thumb * curl * 0.55f).normalized;
            j[3] = j[2] + way * 0.03f;
            way = (way + forward * curl * 0.4f - thumb * curl * 0.5f).normalized;
            j[4] = j[3] + way * 0.026f;
            Finger(j, 5, wrist, forward, back, thumb, 0.029f, 0.092f, 1f, curl);
            Finger(j, 9, wrist, forward, back, thumb, 0.009f, 0.098f, 1.1f, curl);
            Finger(j, 13, wrist, forward, back, thumb, -0.011f, 0.094f, 1.02f, curl);
            Finger(j, 17, wrist, forward, back, thumb, -0.03f, 0.084f, 0.8f, curl);
        }

        static void Finger(Vector3[] j, int first, Vector3 wrist, Vector3 forward, Vector3 back, Vector3 thumb, float across, float along, float size, float curl)
        {
            Vector3 p = wrist + thumb * across + forward * along;
            j[first] = p;
            float[] lengths = { 0.044f, 0.027f, 0.022f };
            float[] bends = { 1.15f, 2.3f, 3.15f };
            for (int k = 0; k < 3; k++)
            {
                float angle = bends[k] * curl;
                p += (forward * Mathf.Cos(angle) - back * Mathf.Sin(angle)) * lengths[k] * size;
                j[first + k + 1] = p;
            }
        }

        // ---------------------------------------------------------------- the mesh

        void Build()
        {
            built = true;
            Material cloth = outfit != null ? outfit.SleeveMaterial : null;
            leatherMaterial = Surface("WeaverGlove", leather, 0f, 0.35f);
            bronzeMaterial = Surface("WeaverGloveBronze", bronze, 0.9f, 0.55f);
            if (cloth == null) cloth = leatherMaterial;
            Mesh mesh = ArmMesh();
            foreach (Arm arm in arms)
            {
                var root = new GameObject(arm.side + "Arm").transform;
                root.SetParent(transform, false);
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                arm.bones = new Transform[FirstFinger + Fingers.Length];
                for (int i = 0; i < arm.bones.Length; i++)
                {
                    arm.bones[i] = new GameObject("Bone" + i).transform;
                    arm.bones[i].SetParent(root, false);
                }
                var smr = root.gameObject.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.bones = arm.bones;
                smr.rootBone = arm.bones[Palm];
                smr.sharedMaterials = new[] { cloth, leatherMaterial, bronzeMaterial };
                smr.updateWhenOffscreen = true;
                smr.quality = SkinQuality.Bone1;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                smr.receiveShadows = false;
                smr.enabled = false;
                arm.renderer = smr;
            }
            shown = false;
        }

        static Material Surface(string name, Color color, float metallic, float smoothness)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<BoneWeight> weights = new List<BoneWeight>();
        readonly List<Matrix4x4> binds = new List<Matrix4x4>();
        readonly List<int>[] tris = { new List<int>(), new List<int>(), new List<int>() };

        // Rings round the bone's local Y: (height, x radius, z radius, z offset), joined, both ends closed.
        void Tube(int bone, int surface, (float y, float rx, float rz, float dz)[] rings, int sides = 8)
        {
            int start = verts.Count;
            var weight = new BoneWeight { boneIndex0 = bone, weight0 = 1f };
            foreach (var ring in rings)
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    verts.Add(new Vector3(Mathf.Cos(a) * ring.rx, ring.y, ring.dz + Mathf.Sin(a) * ring.rz));
                    weights.Add(weight);
                }
            List<int> t = tris[surface];
            for (int r = 0; r + 1 < rings.Length; r++)
                for (int s = 0; s < sides; s++)
                {
                    int a = start + r * sides + s, b = start + r * sides + (s + 1) % sides, c = a + sides, d = b + sides;
                    t.Add(a); t.Add(c); t.Add(b);
                    t.Add(b); t.Add(c); t.Add(d);
                }
            for (int end = 0; end < 2; end++) // a little cap at each end
            {
                var ring = rings[end == 0 ? 0 : rings.Length - 1];
                int centre = verts.Count, first = start + (end == 0 ? 0 : (rings.Length - 1) * sides);
                verts.Add(new Vector3(0f, ring.y, ring.dz));
                weights.Add(weight);
                for (int s = 0; s < sides; s++)
                {
                    int a = first + s, b = first + (s + 1) % sides;
                    t.Add(centre);
                    t.Add(end == 0 ? a : b);
                    t.Add(end == 0 ? b : a);
                }
            }
        }

        Mesh ArmMesh()
        {
            // The sleeves (unit radius: Bone() scales them), tapering a little to the shoulder and the wrist.
            Tube(Upper, 0, new[] { (-0.06f, 0.55f, 0.55f, 0f), (0f, 0.95f, 0.95f, 0f), (0.5f, 1.05f, 1.05f, 0f), (1f, 0.85f, 0.85f, 0f), (1.06f, 0.5f, 0.5f, 0f) });
            Tube(Fore, 0, new[] { (-0.06f, 0.6f, 0.6f, 0f), (0f, 1.05f, 1.05f, 0f), (0.35f, 1.1f, 1.1f, 0f), (0.72f, 0.82f, 0.82f, 0f) });
            // The bronze bracer flaring to the cuff, over the end of the sleeve.
            Tube(Fore, 2, new[] { (0.7f, 0.92f, 0.92f, 0f), (0.86f, 0.88f, 0.88f, 0f), (0.98f, 1.12f, 1.12f, 0f), (1.02f, 1.02f, 1.02f, 0f) }, 10);
            // The palm (real metres: wrist at 0, fingers up +Y, the back of the hand +Z).
            Tube(Palm, 1, new[] { (-0.004f, 0.026f, 0.021f, 0f), (0.03f, 0.038f, 0.023f, 0f), (0.07f, 0.045f, 0.021f, 0f), (0.098f, 0.043f, 0.015f, 0f), (0.106f, 0.03f, 0.009f, 0f) }, 10);
            // Bronze plates on the back of the hand and across the knuckles.
            Tube(Palm, 2, new[] { (0.02f, 0.022f, 0.004f, 0.021f), (0.05f, 0.032f, 0.005f, 0.022f), (0.07f, 0.03f, 0.004f, 0.02f) }, 8);
            Tube(Palm, 2, new[] { (0.082f, 0.043f, 0.0045f, 0.016f), (0.094f, 0.045f, 0.0055f, 0.016f), (0.104f, 0.04f, 0.004f, 0.012f) }, 8);
            for (int i = 0; i < Fingers.Length; i++)
                Tube(FirstFinger + i, 1, new[] { (-0.1f, 0.65f, 0.65f, 0f), (0f, 1f, 1f, 0f), (0.8f, 1f, 1f, 0f), (1f, 0.85f, 0.85f, 0f), (1.12f, 0.45f, 0.45f, 0f) }, 6);

            var mesh = new Mesh { name = "WeaverArm" };
            mesh.SetVertices(verts);
            mesh.subMeshCount = 3;
            for (int i = 0; i < 3; i++) mesh.SetTriangles(tris[i], i);
            mesh.boneWeights = weights.ToArray();
            var bindposes = new Matrix4x4[FirstFinger + Fingers.Length];
            for (int i = 0; i < bindposes.Length; i++) bindposes[i] = Matrix4x4.identity; // (each part is modelled in its bone's own frame)
            mesh.bindposes = bindposes;
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return mesh;
        }
    }
}
