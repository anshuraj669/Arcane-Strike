using UnityEngine;

namespace ArcaneStrike.HandInput
{
    /// <summary>
    /// DEV ONLY. Draws the 21 joints of each hand as small spheres and shows the hand source,
    /// finger straightness and the last gesture in the top-left corner of the Game view.
    /// </summary>
    public class HandDebugView : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] GestureRecognizer recognizer;
        [SerializeField] WebcamHandSource webcam;
        [SerializeField] bool showJoints = true;
        [SerializeField] float jointSize = 0.015f;
        [SerializeField] Color leftColor = new Color(0.3f, 0.7f, 1f);
        [SerializeField] Color rightColor = new Color(1f, 0.6f, 0.2f);
        [Tooltip("Draws the webcam upper-body skeleton (shoulders, arms, hips) as thin green lines. Off by default: your body is " +
                 "round the camera, so the lines run right across the view.")]
        [SerializeField] bool showBodyLines;
        [SerializeField] Color bodyColor = new Color(0.45f, 1f, 0.45f, 0.8f);

        // Pairs of UpperBodyJoint indices drawn as lines.
        static readonly int[,] BodyBones = { { 0, 3 }, { 0, 4 }, { 3, 4 }, { 3, 5 }, { 5, 7 }, { 4, 6 }, { 6, 8 }, { 3, 9 }, { 4, 10 }, { 9, 10 } };

        readonly Transform[][] joints = new Transform[2][];
        LineRenderer[] bodyLines;
        GUIStyle style;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            if (recognizer == null) recognizer = FindAnyObjectByType<GestureRecognizer>();
            if (webcam == null) webcam = FindAnyObjectByType<WebcamHandSource>();
            if (!Application.isEditor && !Debug.isDebugBuild) enabled = false;
        }

        void Start()
        {
            if (showBodyLines) CreateBodyLines();
            if (!showJoints) return;
            for (int h = 0; h < 2; h++)
            {
                var root = new GameObject(h == 0 ? "DebugHand_Left" : "DebugHand_Right").transform;
                root.SetParent(transform, false);
                joints[h] = new Transform[HandFrame.JointCount];
                Color c = h == 0 ? leftColor : rightColor;
                for (int j = 0; j < HandFrame.JointCount; j++)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Destroy(go.GetComponent<Collider>());
                    go.name = ((HandJoint)j).ToString();
                    go.transform.SetParent(root, false);
                    bool tip = j % 4 == 0 && j > 0;
                    go.transform.localScale = Vector3.one * (tip ? jointSize * 1.4f : jointSize);
                    go.GetComponent<Renderer>().material.color = tip ? Color.white : c;
                    joints[h][j] = go.transform;
                }
            }
        }

        void CreateBodyLines()
        {
            var root = new GameObject("DebugBody").transform;
            root.SetParent(transform, false);
            var material = new Material(Shader.Find("Sprites/Default"));
            bodyLines = new LineRenderer[BodyBones.GetLength(0)];
            for (int i = 0; i < bodyLines.Length; i++)
            {
                var line = new GameObject($"Bone{i}").AddComponent<LineRenderer>();
                line.transform.SetParent(root, false);
                line.sharedMaterial = material;
                line.positionCount = 2;
                line.widthMultiplier = 0.012f;
                line.startColor = line.endColor = bodyColor;
                line.enabled = false;
                bodyLines[i] = line;
            }
        }

        void UpdateBodyLines()
        {
            if (bodyLines == null) return;
            UpperBodyFrame b = input.Body;
            for (int i = 0; i < bodyLines.Length; i++)
            {
                var a = (UpperBodyJoint)BodyBones[i, 0];
                var c = (UpperBodyJoint)BodyBones[i, 1];
                bool show = b.IsVisible(a) && b.IsVisible(c);
                bodyLines[i].enabled = show;
                if (!show) continue;
                bodyLines[i].SetPosition(0, b[a]);
                bodyLines[i].SetPosition(1, b[c]);
            }
        }

        void LateUpdate()
        {
            if (input == null) return;
            UpdateBodyLines();
            if (joints[0] == null) return;
            for (int h = 0; h < 2; h++)
            {
                HandFrame f = input.Get((HandSide)h);
                Transform root = joints[h][0].parent;
                if (root.gameObject.activeSelf != f.IsTracked) root.gameObject.SetActive(f.IsTracked);
                if (!f.IsTracked) continue;
                for (int j = 0; j < HandFrame.JointCount; j++) joints[h][j].position = f.Joints[j];
            }
        }

        void OnGUI()
        {
            if (input == null || ArcaneStrike.Demo.DemoDirector.Filming) return; // (never in the demo video)
            style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, richText = true };

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>Hands:</b> {input.ActiveSourceName}");
            for (int h = 0; h < 2; h++)
            {
                HandFrame f = input.Get((HandSide)h);
                if (!f.IsTracked) { sb.AppendLine($"{(HandSide)h}: —"); continue; }
                string palm = input.Head == null ? "" :
                    $"  palm fwd{Vector3.Dot(f.PalmNormal, input.Head.forward):+0.0;-0.0} up{Vector3.Dot(f.PalmNormal, Vector3.up):+0.0;-0.0}";
                sb.AppendLine($"{(HandSide)h}: T{f.Straightness(Finger.Thumb):F2} I{f.Straightness(Finger.Index):F2} " +
                              $"M{f.Straightness(Finger.Middle):F2} R{f.Straightness(Finger.Ring):F2} P{f.Straightness(Finger.Pinky):F2}{palm}");
            }
            if (webcam != null && webcam.IsActive)
                sb.AppendLine(webcam.CalibrationCount == 0
                    ? "<color=orange>Not calibrated: stand still 1 s, or click here and press C</color>"
                    : webcam.CalibratedFromBody ? "Calibrated from body" : "Calibrated from hands (step back for body)");
            sb.AppendLine(input.Body.IsTracked ? $"Body: tracked ({input.Body.Confidence:F2})" : "Body: —");
            if (recognizer != null && recognizer.LastGesture.HasValue)
            {
                GestureEvent g = recognizer.LastGesture.Value;
                float age = Time.time - g.Time;
                sb.AppendLine(age < 1.5f ? $"<b><color=yellow>{g}</color></b>" : $"last: {g}");
            }
            sb.Append("<size=11>C = calibrate webcam · keys 1–6, G = test spells</size>");

            // Scale with the Game view so the text stays readable on high-resolution screens.
            float scale = Mathf.Max(1f, Screen.height / 540f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            GUI.Box(new Rect(10, 10, 520, 160), sb.ToString(), style);
            GUI.matrix = Matrix4x4.identity;
        }
    }
}
