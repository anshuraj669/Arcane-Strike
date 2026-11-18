using ArcaneStrike.HandInput;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ArcaneStrike.VFX
{
    /// <summary>
    /// The player's own hands as proper skinned hands: Meta's OpenXR hand model (Resources/Hands, copied from the
    /// Interaction SDK) posed every frame from the tracked joints, so fingers bend smoothly at their knuckles instead of
    /// being drawn as separate pieces. Works for any hand source (Quest hands, the webcam bridge). The bones keep the
    /// model's own lengths and only take the tracked directions, so the hand never stretches; it is scaled once to the
    /// player's hand size. Each hand comes out of a sleeve (the boy's dark hoodie): the hand model stops at the wrist,
    /// which looked like a floating glove, so a short sleeve runs back from the wrist the way the forearm would, angled
    /// a little towards the body, and fades into the dark. (A whole arm bent at an elbow was tried and looked deformed:
    /// a webcam can't see where the elbow and shoulder really are, so the guessed arm twisted and stretched.)
    /// Hands the story plays out on the keys (<see cref="HandInput.ActedHands"/>: wiping and lifting a chest's lid,
    /// taking the lantern) reach far from the body, so there the whole arm is drawn, shoulder, elbow and wrist.
    /// Hidden while the armour's gauntlets are the hands (<see cref="SparkHands.ArmsDrawnElsewhere"/>).
    /// Adds itself to every scene with hands; <see cref="Active"/> tells SparkHands to stop drawing its rougher skin.
    /// </summary>
    [DefaultExecutionOrder(90)]
    public class RealHands : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] Color skin = new Color(0.85f, 0.6f, 0.46f, 1f);
        [Tooltip("A faint warm glow of the skin's own colour, so the hands still read as skin under cold blue moonlight " +
                 "(lit only by the night, they turned grey-blue). 0 = none.")]
        [Range(0f, 1f)]
        [SerializeField] float skinGlow = 0.22f;
        [Tooltip("The hoodie sleeve each hand comes out of.")]
        [SerializeField] Color sleeve = new Color(0.16f, 0.17f, 0.2f, 1f);
        [Range(0f, 1f)]
        [SerializeField] float smoothness = 0.3f;
        [Tooltip("How quickly the hand's size follows the tracked hand (per second): slow, so noise doesn't make it pulse.")]
        [SerializeField] float sizeSettle = 1.5f;

        /// <summary>The skinned hands loaded and are being drawn (SparkHands hides its own skin then).</summary>
        public static bool Active { get; private set; }

        // Our joints (HandJoint order) → the model's bones. The four finger metacarpals have no tracked joint of their
        // own: they are aimed at the knuckle (Proximal) instead.
        static readonly string[] Bones =
        {
            "Wrist",
            "ThumbMetacarpal", "ThumbProximal", "ThumbDistal", "ThumbTip",
            "IndexProximal", "IndexIntermediate", "IndexDistal", "IndexTip",
            "MiddleProximal", "MiddleIntermediate", "MiddleDistal", "MiddleTip",
            "RingProximal", "RingIntermediate", "RingDistal", "RingTip",
            "LittleProximal", "LittleIntermediate", "LittleDistal", "LittleTip",
        };
        static readonly string[] Metacarpals = { "IndexMetacarpal", "MiddleMetacarpal", "RingMetacarpal", "LittleMetacarpal" };
        static readonly int[] FingerBase = { 5, 9, 13, 17 };

        class Hand
        {
            public Transform root, wrist;
            public Renderer renderer;
            public Transform[] bone = new Transform[HandFrame.JointCount];
            public Transform[] metacarpal = new Transform[4];
            public Transform[] all;
            public Quaternion[] bind;
            public Quaternion frameInWrist; // the palm's frame at rest, in the wrist's space
            public float restLength, scale = 1f;
            public bool ready;
            public Transform forearm, upperArm;
            public Renderer forearmRenderer, upperArmRenderer;
            public Vector3 elbowBend;
        }

        readonly Hand[] hands = { new Hand(), new Hand() };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            AddToScene();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Active = false;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AddToScene();

        static void AddToScene()
        {
            var service = FindAnyObjectByType<HandInputService>();
            if (service != null && FindAnyObjectByType<RealHands>() == null) service.gameObject.AddComponent<RealHands>();
        }

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "PlayerHandSkin" };
            material.SetColor("_BaseColor", skin);
            material.SetFloat("_Smoothness", smoothness);
            if (skinGlow > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", skin * skinGlow);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            Mesh arm = ForearmMesh();
            var sleeveMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "PlayerSleeve" };
            sleeveMaterial.SetColor("_BaseColor", sleeve);
            sleeveMaterial.SetFloat("_Smoothness", 0.12f);
            for (int h = 0; h < 2; h++)
            {
                Load(hands[h], h == 0 ? "Hands/OpenXRLeftHand" : "Hands/OpenXRRightHand", material);
                var go = new GameObject(h == 0 ? "LeftForearm" : "RightForearm");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = arm;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = sleeveMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.enabled = false;
                hands[h].forearm = go.transform;
                hands[h].forearmRenderer = r;
                // The upper arm, for hands that reach out (the chest, the lantern): the sleeve then runs all the way
                // from the wrist to the shoulder, bending at an elbow, so the hand never floats free of the body.
                var up = new GameObject(h == 0 ? "LeftUpperArm" : "RightUpperArm");
                up.transform.SetParent(transform, false);
                up.AddComponent<MeshFilter>().sharedMesh = arm;
                var ur = up.AddComponent<MeshRenderer>();
                ur.sharedMaterial = sleeveMaterial;
                ur.shadowCastingMode = ShadowCastingMode.Off;
                ur.receiveShadows = false;
                ur.enabled = false;
                hands[h].upperArm = up.transform;
                hands[h].upperArmRenderer = ur;
            }
            Active = hands[0].ready && hands[1].ready;
            if (!Active) Debug.LogWarning("[RealHands] The hand models in Resources/Hands didn't load: SparkHands draws its simple hands instead.");
        }

        void OnDestroy() => Active = false;

        void Load(Hand hand, string path, Material material)
        {
            var model = Resources.Load<GameObject>(path);
            if (model == null) return;
            hand.root = Instantiate(model, transform).transform;
            hand.root.name = model.name;
            hand.root.localPosition = Vector3.zero;
            hand.root.localRotation = Quaternion.identity;
            hand.root.localScale = Vector3.one;
            var skinned = hand.root.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skinned == null) return;
            skinned.sharedMaterial = material;
            skinned.shadowCastingMode = ShadowCastingMode.Off;
            skinned.receiveShadows = false;
            skinned.updateWhenOffscreen = true;
            skinned.enabled = false;
            hand.renderer = skinned;

            Transform Find(string name) => FindDeep(hand.root, "XRHand_" + name);
            for (int j = 0; j < Bones.Length; j++)
                if ((hand.bone[j] = Find(Bones[j])) == null) return;
            for (int f = 0; f < 4; f++)
                if ((hand.metacarpal[f] = Find(Metacarpals[f])) == null) return;
            hand.wrist = hand.bone[0];
            hand.all = hand.wrist.GetComponentsInChildren<Transform>();
            hand.bind = new Quaternion[hand.all.Length];
            for (int i = 0; i < hand.all.Length; i++) hand.bind[i] = hand.all[i].localRotation;

            // The palm's frame at rest, measured the same way as the tracked one below.
            Vector3 w = hand.wrist.position;
            Vector3 forward = hand.bone[9].position - w;
            Vector3 across = hand.bone[5].position - hand.bone[17].position;
            hand.frameInWrist = Quaternion.Inverse(hand.wrist.rotation) * Quaternion.LookRotation(forward, Vector3.Cross(forward, across));
            hand.restLength = forward.magnitude;
            hand.ready = hand.restLength > 1e-4f;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }

        void LateUpdate()
        {
            if (input == null) return;
            for (int h = 0; h < 2; h++)
            {
                Hand hand = hands[h];
                if (!hand.ready) continue;
                HandFrame f = input.Get((HandSide)h);
                bool show = f != null && f.IsTracked && !SparkHands.ArmsDrawnElsewhere;
                if (hand.renderer.enabled != show) hand.renderer.enabled = show;
                if (hand.forearmRenderer.enabled != show) hand.forearmRenderer.enabled = show;
                if (!show && hand.upperArmRenderer.enabled) hand.upperArmRenderer.enabled = false;
                if (show)
                {
                    Pose(hand, f.Joints);
                    Arm(hand, (HandSide)h, f.Joints);
                }
            }
        }

        void Pose(Hand hand, Vector3[] j)
        {
            for (int i = 0; i < hand.all.Length; i++) hand.all[i].localRotation = hand.bind[i];

            // Size: the player's wrist-to-knuckle length against the model's, settling slowly.
            Vector3 forward = j[9] - j[0];
            float want = Mathf.Clamp(forward.magnitude / hand.restLength, 0.75f, 1.35f);
            hand.scale = Mathf.Lerp(hand.scale, want, 1f - Mathf.Exp(-sizeSettle * Time.deltaTime));
            hand.root.localScale = Vector3.one * hand.scale;

            // The wrist: placed and turned so the palm faces the way the tracked palm does.
            Vector3 across = j[5] - j[17];
            if (forward.sqrMagnitude < 1e-8f || across.sqrMagnitude < 1e-8f) return;
            hand.wrist.SetPositionAndRotation(j[0], Quaternion.LookRotation(forward, Vector3.Cross(forward, across)) * Quaternion.Inverse(hand.frameInWrist));

            // Each finger from the palm outwards: turn a bone so its child lies along the tracked direction.
            for (int f = 0; f < 4; f++)
            {
                int b = FingerBase[f];
                Aim(hand.metacarpal[f], hand.bone[b], j[b] - j[0]);
                for (int k = 0; k < 3; k++) Aim(hand.bone[b + k], hand.bone[b + k + 1], j[b + k + 1] - j[b + k]);
            }
            Aim(hand.bone[1], hand.bone[2], j[2] - j[1]);
            Aim(hand.bone[2], hand.bone[3], j[3] - j[2]);
            Aim(hand.bone[3], hand.bone[4], j[4] - j[3]);
        }

        [Header("Sleeves")]
        [Tooltip("How long a sleeve shows behind the wrist (m), for an average hand.")]
        [SerializeField] float sleeveLength = 0.22f;
        [Tooltip("How far a sleeve turns from straight back along the hand towards the body (0..1).")]
        [Range(0f, 1f)]
        [SerializeField] float towardsBody = 0.35f;

        // The sleeve: from just behind the wrist, back along the line of the forearm.
        void Arm(Hand hand, HandSide side, Vector3[] j)
        {
            Transform head = input.Head != null ? input.Head : Camera.main != null ? Camera.main.transform : null;
            if (head == null) return;
            Vector3 forward = j[9] - j[0];
            if (forward.sqrMagnitude < 1e-8f) return;
            forward.Normalize();
            float sign = side == HandSide.Left ? -1f : 1f;
            // Where that side's shoulder roughly is: only to lean the sleeve that way, never to reach it.
            Vector3 shoulder = head.position - Vector3.up * 0.25f + head.right * (0.18f * sign) - StoryFlat(head.forward) * 0.05f;
            Vector3 wrist = j[0] - forward * 0.015f * hand.scale;
            Vector3 toBody = (shoulder - wrist).normalized;
            Vector3 thumbUp = Vector3.Cross(forward, j[5] - j[17]);
            // Hands played out for the keyboard (ActedHands) reach well away from the body (down to a chest's lid, out
            // to the lantern): there the whole arm is drawn, shoulder to elbow to wrist, so it never comes apart.
            bool acted = input.ActiveSource != null && ReferenceEquals(input.ActiveSource, HandInputService.ActedSource);
            if (hand.upperArmRenderer.enabled != acted) hand.upperArmRenderer.enabled = acted;
            if (acted)
            {
                WholeArm(hand, sign, head, shoulder, wrist, thumbUp);
                return;
            }
            Vector3 want = Vector3.Slerp(-forward, toBody, towardsBody).normalized;
            float length = sleeveLength * hand.scale;
            hand.elbowBend = hand.elbowBend.sqrMagnitude < 0.5f ? want
                : Vector3.Slerp(hand.elbowBend, want, 1f - Mathf.Exp(-12f * Time.deltaTime)).normalized;
            Place(hand.forearm, wrist, wrist + hand.elbowBend * length, hand.scale * 1.25f, thumbUp);
        }

        [Tooltip("Shoulder to elbow and elbow to wrist (m) of the whole arm drawn for acted hands.")]
        [SerializeField] float upperArmLength = 0.3f, forearmLength = 0.28f;

        // Two-bone arm: the elbow drops down and out to the side. Further than an arm can reach, both parts stretch a
        // little rather than letting go, so the sleeve always joins the shoulder to the wrist.
        void WholeArm(Hand hand, float sign, Transform head, Vector3 shoulder, Vector3 wrist, Vector3 thumbUp)
        {
            Vector3 flat = StoryFlat(head.forward), side = Vector3.Cross(Vector3.up, flat) * sign;
            Vector3 to = wrist - shoulder;
            float reach = Mathf.Max(to.magnitude, 0.05f), a = upperArmLength, b = forearmLength;
            if (reach > (a + b) * 0.99f)
            {
                float s = reach / ((a + b) * 0.99f);
                a *= s;
                b *= s;
            }
            Vector3 dir = to / reach;
            float along = (a * a - b * b + reach * reach) / (2f * reach);
            float rise = Mathf.Sqrt(Mathf.Max(a * a - along * along, 0f));
            Vector3 bend = Vector3.ProjectOnPlane(-Vector3.up * 0.7f + side * 0.7f - flat * 0.2f, dir);
            bend = bend.sqrMagnitude > 1e-5f ? bend.normalized : side;
            hand.elbowBend = hand.elbowBend.sqrMagnitude < 0.5f ? bend
                : Vector3.Slerp(hand.elbowBend, bend, 1f - Mathf.Exp(-12f * Time.deltaTime)).normalized;
            Vector3 elbow = shoulder + dir * along + hand.elbowBend * rise;
            // The forearm from the wrist back to a little past the elbow, the upper arm from the elbow up into the
            // shoulder (each tube's thin end first), overlapping at the elbow so there's no gap at the bend.
            Place(hand.forearm, wrist, elbow + (elbow - wrist).normalized * 0.03f, hand.scale * 1.15f, thumbUp);
            Place(hand.upperArm, elbow - (shoulder - elbow).normalized * 0.02f, shoulder + (shoulder - elbow).normalized * 0.04f, 1.35f, side);
        }

        static Vector3 StoryFlat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        // A tube from `from` (its thin end) to `to`.
        static void Place(Transform tube, Vector3 from, Vector3 to, float thickness, Vector3 up)
        {
            Vector3 along = to - from;
            float length = along.magnitude;
            if (length < 1e-4f) return;
            Vector3 u = Vector3.ProjectOnPlane(up, along);
            tube.SetPositionAndRotation(from, Quaternion.LookRotation(along / length, u.sqrMagnitude > 1e-6f ? u : Vector3.up));
            tube.localScale = new Vector3(thickness, thickness, length);
        }

        // A tapered tube along +Z from 0 to 1 (its radius in metres: the wrist end 2.8 cm, the elbow end 4 cm),
        // a little flatter than round like a real forearm, closed at the elbow end.
        static Mesh ForearmMesh()
        {
            const int sides = 18, rings = 10;
            var v = new System.Collections.Generic.List<Vector3>();
            var n = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            for (int r = 0; r <= rings + 2; r++)
            {
                float t = Mathf.Min(1f, (float)r / rings);
                float radius = Mathf.Lerp(0.027f, 0.04f, Mathf.SmoothStep(0f, 1f, t)) * (1f + 0.08f * Mathf.Sin(t * Mathf.PI));
                float z = t;
                if (r > rings) // the rounded end
                {
                    float k = (r - rings) / 2f;
                    radius *= Mathf.Cos(k * Mathf.PI * 0.5f * 0.95f);
                    z = 1f + k * 0.06f;
                }
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2f / sides;
                    var dir = new Vector3(Mathf.Cos(a) * 1.15f, Mathf.Sin(a) * 0.85f, 0f);
                    v.Add(new Vector3(dir.x * radius, dir.y * radius, z));
                    n.Add(new Vector3(Mathf.Cos(a) / 1.15f, Mathf.Sin(a) / 0.85f, 0f).normalized);
                }
            }
            int count = rings + 3;
            for (int r = 0; r + 1 < count; r++)
                for (int i = 0; i < sides; i++)
                {
                    int a = r * sides + i, b = r * sides + (i + 1) % sides, c = a + sides, d = b + sides;
                    tris.AddRange(new[] { a, b, c, b, d, c }); // (clockwise seen from outside: faces out)
                }
            var mesh = new Mesh { name = "Forearm" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Turns `bone` (keeping its twist as far as possible) so `child` lies along `direction` from it.
        static void Aim(Transform bone, Transform child, Vector3 direction)
        {
            Vector3 now = child.position - bone.position;
            if (now.sqrMagnitude < 1e-10f || direction.sqrMagnitude < 1e-10f) return;
            bone.rotation = Quaternion.FromToRotation(now, direction) * bone.rotation;
        }
    }
}
