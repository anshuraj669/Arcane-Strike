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
        [SerializeField] bool showJoints = true;
        [SerializeField] float jointSize = 0.015f;
        [SerializeField] Color leftColor = new Color(0.3f, 0.7f, 1f);
        [SerializeField] Color rightColor = new Color(1f, 0.6f, 0.2f);

        readonly Transform[][] joints = new Transform[2][];
        GUIStyle style;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            if (recognizer == null) recognizer = FindAnyObjectByType<GestureRecognizer>();
            if (!Application.isEditor && !Debug.isDebugBuild) enabled = false;
        }

        void Start()
        {
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

        void LateUpdate()
        {
            if (input == null || joints[0] == null) return;
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
            if (input == null) return;
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
            GUI.Box(new Rect(10, 10, 520, 120), sb.ToString(), style);
            GUI.matrix = Matrix4x4.identity;
        }
    }
}
