using System.Text;
using UnityEngine;

namespace ArcaneStrike.HandInput
{
    /// <summary>
    /// The controls on screen: "F1: controls" in the corner, and with F1 the whole list from <see cref="GameInput"/>
    /// (always in step with the bindings). Added to every scene by itself; drawn on the desktop view.
    /// </summary>
    public class ControlsHelp : MonoBehaviour
    {
        static bool shown;
        string text;
        GUIStyle style, small;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
#if !UNITY_ANDROID || UNITY_EDITOR // (not on the headset: no keyboard there, and OnGUI costs every frame)
            var go = new GameObject("ControlsHelp");
            DontDestroyOnLoad(go);
            go.AddComponent<ControlsHelp>();
#endif
        }

        void Update()
        {
            if (GameInput.Pressed(GameAction.ShowControls)) shown = !shown;
        }

        void OnGUI()
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, richText = true };
                style.normal.textColor = Color.white;
                small = new GUIStyle(GUI.skin.label) { fontSize = 12 };
                small.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
            }
            if (!shown)
            {
                GUI.Label(new Rect(10, Screen.height - 24, 300, 20), $"{GameInput.KeyName(GameAction.ShowControls)}: controls", small);
                return;
            }
            if (text == null)
            {
                var sb = new StringBuilder();
                string group = null;
                foreach (var (g, key, label) in GameInput.List())
                {
                    if (g != group)
                    {
                        if (group != null) sb.Append('\n');
                        sb.Append("<b>").Append(g).Append("</b>\n");
                        group = g;
                    }
                    sb.Append("  ").Append(key.PadRight(12)).Append(label).Append('\n');
                }
                text = sb.ToString();
            }
            float h = Mathf.Min(Screen.height - 20, 900);
            GUI.Box(new Rect(10, 10, 380, h), text, style);
        }
    }
}
