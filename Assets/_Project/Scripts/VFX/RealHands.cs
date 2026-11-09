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
    /// player's hand size. Hidden while the armour's gauntlets are the hands (<see cref="SparkHands.ArmsDrawnElsewhere"/>).
    /// Adds itself to every scene with hands; <see cref="Active"/> tells SparkHands to stop drawing its rougher skin.
    /// </summary>
    [DefaultExecutionOrder(90)]
    public class RealHands : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] Color skin = new Color(0.8f, 0.6f, 0.48f, 1f);
        [Range(0f, 1f)]
        [SerializeField] float smoothness = 0.4f;
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
            for (int h = 0; h < 2; h++) Load(hands[h], h == 0 ? "Hands/OpenXRLeftHand" : "Hands/OpenXRRightHand", material);
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
                if (show) Pose(hand, f.Joints);
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

        // Turns `bone` (keeping its twist as far as possible) so `child` lies along `direction` from it.
        static void Aim(Transform bone, Transform child, Vector3 direction)
        {
            Vector3 now = child.position - bone.position;
            if (now.sqrMagnitude < 1e-10f || direction.sqrMagnitude < 1e-10f) return;
            bone.rotation = Quaternion.FromToRotation(now, direction) * bone.rotation;
        }
    }
}
