using ArcaneStrike.HandInput;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArcaneStrike.VFX
{
    /// <summary>
    /// The Aegis shield: a glowing round shield that pops up on the fist that cast Aegis, follows it
    /// while the fist is held, and shrinks away on AegisEnd. Built at runtime (a disc and a glowing rim),
    /// so it needs no assets. <see cref="SparkHands"/> adds it automatically.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public class AegisShield : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] GestureRecognizer recognizer;

        [Header("Look")]
        [SerializeField] float radius = 0.28f;
        [Tooltip("How far in front of the fist the shield sits (m).")]
        [SerializeField] float forwardOffset = 0.06f;
        [SerializeField] Color faceColor = new Color(0.31f, 0.89f, 1f, 0.25f); // Volt cyan #4FE3FF
        [SerializeField] Color rimColor = new Color(0.31f, 0.89f, 1f, 1f);
        [SerializeField] float rimWidth = 0.025f;
        [Tooltip("Seconds to grow in and to shrink away.")]
        [SerializeField] float popTime = 0.15f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        class Shield
        {
            public Transform Root;
            public bool Up;
            public float Grow;
            public Vector3 FallbackPosition;
        }

        readonly Shield[] shields = { new Shield(), new Shield() };

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            if (recognizer == null) recognizer = FindAnyObjectByType<GestureRecognizer>();
        }

        void OnEnable()
        {
            if (recognizer != null) recognizer.OnGesture += HandleGesture;
        }

        void OnDisable()
        {
            if (recognizer != null) recognizer.OnGesture -= HandleGesture;
        }

        void Start()
        {
            Material face = CreateFaceMaterial(faceColor);
            Material rim = SparkHands.CreateGlowMaterial(SparkHands.CreateSoftLine(64));
            for (int h = 0; h < 2; h++)
            {
                shields[h].Root = BuildShield(h == 0 ? "AegisShield_Left" : "AegisShield_Right", face, rim);
                shields[h].Root.gameObject.SetActive(false);
            }
        }

        void HandleGesture(GestureEvent e)
        {
            Shield s = shields[(int)e.Hand];
            if (e.Type == GestureType.AegisStart)
            {
                s.Up = true;
                s.FallbackPosition = e.Position;
            }
            else if (e.Type == GestureType.AegisEnd) s.Up = false;
        }

        void LateUpdate()
        {
            Transform head = input != null && input.Head != null ? input.Head : Camera.main != null ? Camera.main.transform : null;
            if (head == null) return;

            for (int h = 0; h < 2; h++)
            {
                Shield s = shields[h];
                if (s.Root == null) continue;
                s.Grow = Mathf.MoveTowards(s.Grow, s.Up ? 1f : 0f, Time.deltaTime / Mathf.Max(popTime, 0.01f));
                bool visible = s.Grow > 0f;
                if (s.Root.gameObject.activeSelf != visible) s.Root.gameObject.SetActive(visible);
                if (!visible) continue;

                // Follow the fist; keyboard casts (or a lost hand) keep the last known spot.
                HandFrame f = input != null ? input.Get((HandSide)h) : null;
                if (f != null && f.IsTracked) s.FallbackPosition = f.PalmCenter;

                s.Root.SetPositionAndRotation(s.FallbackPosition + head.forward * forwardOffset,
                                              Quaternion.LookRotation(head.forward, head.up));
                float t = 1f - (1f - s.Grow) * (1f - s.Grow); // ease out
                s.Root.localScale = Vector3.one * t;
            }
        }

        Transform BuildShield(string name, Material face, Material rim)
        {
            var root = new GameObject(name).transform;
            root.SetParent(transform, false);

            // Disc: a flattened cylinder turned so its flat side faces forward (+Z).
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(disc.GetComponent<Collider>());
            disc.name = "Face";
            disc.transform.SetParent(root, false);
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            disc.transform.localScale = new Vector3(radius * 2f, 0.004f, radius * 2f);
            var discRenderer = disc.GetComponent<Renderer>();
            discRenderer.sharedMaterial = face;
            discRenderer.shadowCastingMode = ShadowCastingMode.Off;
            discRenderer.receiveShadows = false;

            // Glowing rim, plus a smaller inner ring like a shield boss.
            AddRing(root, rim, radius, rimWidth);
            AddRing(root, rim, radius * 0.35f, rimWidth * 0.7f);
            return root;
        }

        void AddRing(Transform parent, Material material, float r, float width)
        {
            const int points = 48;
            var line = new GameObject("Ring").AddComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = points;
            line.widthMultiplier = width;
            line.startColor = line.endColor = rimColor;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            for (int i = 0; i < points; i++)
            {
                float a = i * Mathf.PI * 2f / points;
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            }
        }

        /// <summary>See-through URP Unlit for summoned objects (shield face, blade). Also used by <see cref="SparkBlade"/>.</summary>
        public static Material CreateFaceMaterial(Color color)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "AegisFace" };
            m.SetFloat("_Surface", 1f); // transparent
            m.SetFloat("_Blend", 0f);   // alpha
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent - 1; // under the rim glow
            m.SetColor(BaseColorId, color);
            return m;
        }
    }
}
