using System.Collections;
using ArcaneStrike.HandInput;
using UnityEngine;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// A dialogue choice: two glowing panels float side by side in front of the player, [A] on the left and [B] on the
    /// right. Touch one with a fingertip (either hand), or press 1 / 2 on the keyboard. The chosen panel flares and both
    /// fade away. Run <see cref="Ask"/> as a coroutine and read <see cref="Chosen"/> (0 = A, 1 = B) after.
    /// </summary>
    public class DialogueChoice : MonoBehaviour
    {
        const float Width = 0.42f, Height = 0.14f, Touch = 0.07f;
        static readonly Color Idle = new Color(0.08f, 0.04f, 0.02f, 0.72f), Hover = new Color(0.35f, 0.15f, 0.03f, 0.85f), Picked = new Color(1f, 0.6f, 0.2f, 0.95f);

        public int Chosen { get; private set; } = -1;

        HandInputService input;
        Transform head;
        readonly Transform[] panels = new Transform[2];
        readonly Material[] backs = new Material[2];
        readonly TextMesh[] texts = new TextMesh[2];

        public static DialogueChoice Create(HandInputService input)
        {
            var c = new GameObject("DialogueChoice").AddComponent<DialogueChoice>();
            c.input = input;
            return c;
        }

        public IEnumerator Ask(string a, string b)
        {
            head = StoryFx.Head(input);
            if (head == null) yield break;
            Debug.Log($"[Choice] [A] {a}   [B] {b}   (touch one, or press 1 / 2)");
            Vector3 forward = StoryFx.Flat(head.forward), right = Vector3.Cross(Vector3.up, forward);
            Vector3 centre = head.position + forward * 0.5f - Vector3.up * 0.28f;
            transform.SetPositionAndRotation(centre, Quaternion.LookRotation(forward));
            panels[0] = Panel(0, "[A]  " + a, -right * (Width * 0.5f + 0.03f));
            panels[1] = Panel(1, "[B]  " + b, right * (Width * 0.5f + 0.03f));

            for (float t = 0f; t < 0.4f; t += Time.deltaTime)
            {
                SetAlpha(t / 0.4f);
                yield return null;
            }
            SetAlpha(1f);
            while (Chosen < 0)
            {
                int hover = -1;
                for (int p = 0; p < 2; p++)
                    if (Touched(panels[p])) hover = p;
                for (int p = 0; p < 2; p++) backs[p].SetColor("_BaseColor", p == hover ? Hover : Idle);
                if (hover >= 0) Chosen = hover;
                if (GameInput.Pressed(GameAction.ChoiceA)) Chosen = 0;
                if (GameInput.Pressed(GameAction.ChoiceB)) Chosen = 1;
                yield return null;
            }
            Debug.Log($"[Choice] Chose {(Chosen == 0 ? "A" : "B")}");
            backs[Chosen].SetColor("_BaseColor", Picked);
            texts[Chosen].color = new Color(0.15f, 0.05f, 0f, 1f);
            yield return new WaitForSeconds(0.45f);
            for (float t = 0f; t < 0.4f; t += Time.deltaTime)
            {
                SetAlpha(1f - t / 0.4f);
                yield return null;
            }
            Destroy(gameObject);
        }

        Transform Panel(int index, string label, Vector3 offset)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.name = index == 0 ? "ChoiceA" : "ChoiceB";
            quad.transform.SetParent(transform, false);
            quad.transform.position = transform.position + offset;
            quad.transform.localScale = new Vector3(Width, Height, 1f);
            backs[index] = VFX.AegisShield.CreateFaceMaterial(Idle);
            backs[index].renderQueue = 3100;
            quad.GetComponent<Renderer>().sharedMaterial = backs[index];

            var go = new GameObject("Text");
            go.transform.SetParent(quad.transform.parent, false);
            go.transform.position = quad.transform.position - transform.forward * 0.004f;
            go.transform.rotation = transform.rotation;
            var t = go.AddComponent<TextMesh>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var m = new Material(t.font.material) { renderQueue = 3101 };
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            t.text = Wrap(label, 30);
            t.anchor = TextAnchor.MiddleCenter;
            t.alignment = TextAlignment.Center;
            t.fontSize = 64;
            t.characterSize = 0.0055f;
            t.color = new Color(1f, 0.88f, 0.62f, 1f);
            texts[index] = t;
            return quad.transform;
        }

        bool Touched(Transform panel)
        {
            if (input == null) return false;
            foreach (HandSide side in new[] { HandSide.Left, HandSide.Right })
            {
                HandFrame f = input.Get(side);
                if (f == null || !f.IsTracked) continue;
                Vector3 tip = f.Joints[8]; // index fingertip
                Vector3 local = panel.InverseTransformPoint(tip);
                if (Mathf.Abs(local.x) < 0.5f && Mathf.Abs(local.y) < 0.5f && Mathf.Abs(local.z * panel.lossyScale.z) < Touch) return true;
            }
            return false;
        }

        void SetAlpha(float a)
        {
            for (int p = 0; p < 2; p++)
            {
                if (backs[p] == null) continue;
                Color c = backs[p].GetColor("_BaseColor");
                c.a = (p == Chosen ? Picked.a : Idle.a) * a;
                backs[p].SetColor("_BaseColor", c);
                Color tc = texts[p].color;
                tc.a = a;
                texts[p].color = tc;
            }
        }

        static string Wrap(string s, int width)
        {
            var sb = new System.Text.StringBuilder();
            int col = 0;
            foreach (string word in s.Split(' '))
            {
                if (col > 0 && col + word.Length > width)
                {
                    sb.Append('\n');
                    col = 0;
                }
                else if (col > 0)
                {
                    sb.Append(' ');
                    col++;
                }
                sb.Append(word);
                col += word.Length;
            }
            return sb.ToString();
        }
    }
}
