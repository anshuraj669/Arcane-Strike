using System.Collections;
using ArcaneStrike.Core;
using ArcaneStrike.HandInput;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ArcaneStrike.Story
{
    /// <summary>
    /// Chapter 2, inside the lantern. The player arrives (fading in from the white the attic's golden light left)
    /// in an endless white void. In the middle a bronze cauldron holds a small flame. Walking up to it wakes
    /// Ember, who introduces himself and notices the cut on the player's hand ("you have it").
    /// Next beats (the Jyoti Seal, the first orb, the Archives) continue from <see cref="OnIntroduced"/>.
    /// N skips the current wait.
    /// </summary>
    public class LanternVoidDirector : MonoBehaviour
    {
        [SerializeField] HandInputService input;
        [SerializeField] EmberFlame ember;
        [SerializeField] StorySpeech emberSpeech;
        [Tooltip("The cauldron (Ember sits at its mouth).")]
        [SerializeField] Transform cauldron;
        [Tooltip("Where the player arrives (feet), in this object's space. The cauldron is ahead (+Z).")]
        [SerializeField] Vector3 arrival = Vector3.zero;
        [Tooltip("Walking this close to the cauldron (m) wakes Ember.")]
        [SerializeField] float wakeDistance = 2.2f;
        [SerializeField] float desktopEyeHeight = 1.6f;

        [Header("The white")]
        [SerializeField] Color voidColor = new Color(0.96f, 0.95f, 0.92f);
        [SerializeField] float hazeStart = 6f, hazeEnd = 40f;

        public System.Action OnIntroduced;

        Transform head, rig;
        AudioSource hum;

        IEnumerator Start()
        {
            if (input == null) input = FindAnyObjectByType<HandInputService>();
            EnterTheWhite();
            yield return null; // the rig and hands initialise
            head = StoryFx.Head(input);
            if (head == null) yield break;
            rig = head.root;
            PlacePlayer();

            ScreenFade.Set(Color.white); // continue from the attic's whiteout
            hum = StoryFx.CreateSource(gameObject, StoryFx.Hum(), true, 0f);
            hum.spatialBlend = 0f;
            hum.pitch = 0.6f; // a low, endless drone
            hum.volume = 0.18f;
            hum.Play();
            yield return ScreenFade.To(new Color(1f, 1f, 1f, 0f), 3f);
            Debug.Log("[Void] Inside the lantern. Walk to the flame in the cauldron (N skips).");

            if (ember != null)
            {
                ember.SetMood(EmberFlame.Mood.Asleep);
                ember.LookAt(head);
            }
            yield return WaitUntilNear();
            yield return null; // the N that skipped the walk mustn't also skip Ember's first line
            yield return Introduction();
            OnIntroduced?.Invoke();
        }

        // Endless white: white air, white haze swallowing the distance, soft white light everywhere,
        // and no passthrough or real-room geometry (this is fully virtual).
        void EnterTheWhite()
        {
            RoomSetup.VirtualWorld = true;
            foreach (var b in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (b != null && b.GetType().Name == "OVRPassthroughLayer") b.enabled = false;
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = voidColor;
            }
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = voidColor * 0.85f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = voidColor;
            RenderSettings.fogStartDistance = hazeStart;
            RenderSettings.fogEndDistance = hazeEnd;
        }

        void PlacePlayer()
        {
            bool tracked = head.position.y - rig.position.y > 0.3f; // a headset gives the real eye height
            Vector3 feet = transform.TransformPoint(arrival);
            Vector3 p = rig.position;
            p.x += feet.x - head.position.x;
            p.z += feet.z - head.position.z;
            p.y = feet.y + (tracked ? 0f : desktopEyeHeight);
            rig.position = p;
            Vector3 to = (cauldron != null ? cauldron.position : feet + transform.forward) - head.position;
            to.y = 0f;
            Vector3 fwd = head.forward;
            fwd.y = 0f;
            if (to.sqrMagnitude > 1e-4f && fwd.sqrMagnitude > 1e-4f)
                rig.RotateAround(head.position, Vector3.up, Vector3.SignedAngle(fwd, to, Vector3.up));
        }

        IEnumerator WaitUntilNear()
        {
            while (cauldron != null)
            {
                Vector3 d = head.position - cauldron.position;
                d.y = 0f;
                if (d.magnitude < wakeDistance || SkipPressed()) yield break;
                yield return null;
            }
        }

        IEnumerator Introduction()
        {
            if (ember != null)
            {
                ember.SetMood(EmberFlame.Mood.Calm);
                ember.Burst(40);
            }
            hum.volume = 0.12f;
            yield return Line("It's been many years since someone has been here.", 1.4f);
            yield return Line("You have no idea... Right. Introductions.", 1.2f);
            yield return Line("Hii... my name is Ember. And you?", 1.8f);
            if (ember != null) ember.PointAt(head, 4f);
            yield return Line("...Wait a min. Oh, oh, my, my... you have it.", 1.5f);
            if (ember != null) ember.SetMood(EmberFlame.Mood.Excited);
            Debug.Log("[Void] Ember has seen the cut. Next: the Jyoti Seal (raise both palms to your face).");
        }

        IEnumerator Line(string text, float hold)
        {
            if (emberSpeech == null)
            {
                Debug.Log($"[Ember] {text}");
                for (float t = 0f; t < 2f + hold && !SkipPressed(); t += Time.deltaTime) yield return null;
                yield break;
            }
            // N finishes the line being said (as the summary promises), instead of having to sit through it.
            IEnumerator say = emberSpeech.Say(text, hold);
            while (say.MoveNext())
            {
                if (SkipPressed()) emberSpeech.SkipLine();
                yield return say.Current;
            }
        }

        static bool SkipPressed() => GameInput.Pressed(GameAction.Skip);
    }
}
