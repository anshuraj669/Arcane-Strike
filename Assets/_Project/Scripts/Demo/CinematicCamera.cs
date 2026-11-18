using System.Collections.Generic;
using ArcaneStrike.HandInput;
using ArcaneStrike.Story;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ArcaneStrike.Demo
{
    /// <summary>
    /// A second camera for the demo video and trailer (editor only, F8 cycles through):
    /// - First person: off, the game's own view.
    /// - Over the shoulder: just behind and above the player's head, following smoothly (like a steady-cam operator),
    ///   so the hands, the room and what's happening in front of the player all show at once.
    /// - Trailer shots: slow camera moves made for this scene (a push in through the attic gate, round the chest, past
    ///   the moonlit window, round the player as the bedroom shatters...), one after another, on a loop.
    /// The game doesn't notice: the player's head (and everything that looks at it) stays where it is; the main camera
    /// just stops drawing while this one draws instead. The screen fades still show; the player's own subtitles and
    /// the head-locked overlays are hidden in the wide shots (they'd hang in the air beside the player).
    /// </summary>
    public class CinematicCamera : MonoBehaviour
    {
        public enum Mode { FirstPerson, OverShoulder, TrailerShots }

        const int HiddenLayer = 31; // (the head-locked things, moved here while this camera is on)

        struct Shot
        {
            public bool orbit, aroundPlayer;
            public Vector3 from, fromLook, to, toLook; // move: anchor (or player) space
            public Vector3 centre;                      // orbit: centre, radius, height above it, angles (deg, 0 = +Z)
            public float radius, height, fromAngle, toAngle;
            public float seconds, fov;
        }

        public Mode Current { get; private set; }

        Camera cam;
        Renderer fade;
        Material fadeMaterial;
        Camera main;
        int mainMask;
        Transform head, anchor;
        string sceneName;
        readonly List<Shot> shots = new List<Shot>();
        int shot;
        float shotStart;
        Vector3 velocity, centreVelocity, smoothCentre;
        // The player's place and facing when the shot began (shots round the player are framed on these, not on
        // every twitch of the head).
        Vector3 shotFeet, shotForward = Vector3.forward;
        float shotFramedAt = -1f;
        readonly Dictionary<GameObject, int> hidden = new Dictionary<GameObject, int>();

        void OnEnable() => SceneManager.sceneLoaded += Loaded;
        void OnDisable() => SceneManager.sceneLoaded -= Loaded;

        void Loaded(Scene scene, LoadSceneMode mode)
        {
            RestoreMain();
            head = null;
            anchor = null;
            sceneName = null;
            hidden.Clear();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f8Key.wasPressedThisFrame)
            {
                Current = (Mode)(((int)Current + 1) % 3);
                shot = 0;
                shotStart = Time.time;
                velocity = Vector3.zero;
                if (Current == Mode.FirstPerson) TurnOff();
                Debug.Log($"[Demo] Camera: {Current switch { Mode.OverShoulder => "over the shoulder", Mode.TrailerShots => "trailer shots", _ => "first person" }} (F8 changes it).");
            }
        }

        void LateUpdate()
        {
            if (Current == Mode.FirstPerson) return;
            if (!Find()) return;
            TurnOn();
            HideHeadLocked();

            if (Current == Mode.OverShoulder) OverShoulder();
            else Trailer();

            Color c = ScreenFade.Current;
            fade.enabled = c.a > 0.001f;
            fadeMaterial.SetColor("_BaseColor", c);
        }

        bool Find()
        {
            if (head != null && main != null) return true;
            var input = FindAnyObjectByType<HandInputService>();
            head = input != null && input.Head != null ? input.Head : Camera.main != null ? Camera.main.transform : null;
            if (head == null) return false;
            main = head.GetComponent<Camera>();
            if (main == null) main = Camera.main;
            if (sceneName != SceneManager.GetActiveScene().name)
            {
                sceneName = SceneManager.GetActiveScene().name;
                BuildShots();
            }
            return main != null;
        }

        // ---------------------------------------------------------------- the camera

        void TurnOn()
        {
            if (cam == null)
            {
                var go = new GameObject("CinematicCamera");
                go.transform.SetParent(transform, false);
                cam = go.AddComponent<Camera>();
                cam.nearClipPlane = 0.03f;
                cam.farClipPlane = 500f;
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(quad.GetComponent<Collider>());
                quad.name = "CinematicFade";
                quad.transform.SetParent(cam.transform, false);
                quad.transform.localPosition = new Vector3(0f, 0f, 0.1f);
                quad.transform.localScale = new Vector3(1f, 1f, 1f);
                fadeMaterial = VFX.AegisShield.CreateFaceMaterial(Color.clear);
                fadeMaterial.renderQueue = 4995;
                fade = quad.GetComponent<Renderer>();
                fade.sharedMaterial = fadeMaterial;
                fade.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                cam.transform.SetPositionAndRotation(head.position, head.rotation);
            }
            cam.enabled = true;
            if (main.cullingMask != 0)
            {
                mainMask = main.cullingMask;
                main.cullingMask = 0; // (still the main camera, so the game finds it; it just draws nothing)
                cam.CopyFrom(main);
                cam.cullingMask = mainMask & ~(1 << HiddenLayer);
                cam.depth = main.depth + 10;
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                cam.targetTexture = null;
                cam.nearClipPlane = 0.03f;
                var mainData = main.GetUniversalAdditionalCameraData();
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = mainData.renderPostProcessing;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
            }
        }

        void TurnOff()
        {
            if (cam != null) cam.enabled = false;
            RestoreMain();
            foreach (var pair in hidden)
                if (pair.Key != null) pair.Key.layer = pair.Value;
            hidden.Clear();
        }

        void RestoreMain()
        {
            if (main != null && main.cullingMask == 0 && mainMask != 0) main.cullingMask = mainMask;
            main = null;
        }

        // The things locked to the player's eyes (the fade and vignette quads, the player's own subtitles) would hang
        // in mid-air in a wide shot: keep them out of this camera.
        void HideHeadLocked()
        {
            foreach (Renderer r in head.GetComponentsInChildren<Renderer>(true)) Hide(r.gameObject);
            foreach (InnerVoice v in FindObjectsByType<InnerVoice>(FindObjectsSortMode.None))
                foreach (Renderer r in v.GetComponentsInChildren<Renderer>(true)) Hide(r.gameObject);
        }

        void Hide(GameObject go)
        {
            if (go.layer == HiddenLayer) return;
            hidden[go] = go.layer;
            go.layer = HiddenLayer;
        }

        // ---------------------------------------------------------------- over the shoulder

        void OverShoulder()
        {
            Vector3 forward = StoryFx.Flat(head.forward), right = Vector3.Cross(Vector3.up, forward);
            Vector3 want = head.position - forward * 1.05f + Vector3.up * 0.22f + right * 0.38f;
            // Not through a wall behind the player.
            Vector3 back = want - head.position;
            if (Physics.SphereCast(head.position, 0.12f, back.normalized, out RaycastHit hit, back.magnitude, ~0, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(head.root))
                want = head.position + back.normalized * Mathf.Max(0.25f, hit.distance - 0.1f);
            cam.transform.position = Vector3.SmoothDamp(cam.transform.position, want, ref velocity, 0.35f);
            Vector3 look = head.position + head.forward * 2.5f - cam.transform.position;
            Quaternion rotation = Quaternion.LookRotation(look, Vector3.up);
            cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, rotation, 1f - Mathf.Exp(-4f * Time.deltaTime));
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 62f, 0.1f);
        }

        // ---------------------------------------------------------------- trailer shots

        void Trailer()
        {
            if (shots.Count == 0) return;
            Shot s = shots[shot];
            if (shotFramedAt != shotStart)
            {
                shotFramedAt = shotStart;
                shotForward = StoryFx.Flat(head.forward);
                shotFeet = new Vector3(head.position.x, head.position.y - 1.6f, head.position.z);
                smoothCentre = new Vector3(head.position.x, head.position.y - 0.25f, head.position.z);
                centreVelocity = Vector3.zero;
            }
            float u = Mathf.Clamp01((Time.time - shotStart) / s.seconds);
            float e = u * u * (3f - 2f * u); // ease in and out
            Vector3 position, look;
            Transform frame = s.aroundPlayer ? null : anchor;
            if (s.orbit)
            {
                Vector3 centre;
                if (s.aroundPlayer)
                {
                    Vector3 want = new Vector3(head.position.x, head.position.y - 0.25f, head.position.z);
                    smoothCentre = Vector3.SmoothDamp(smoothCentre, want, ref centreVelocity, 0.6f);
                    centre = smoothCentre;
                }
                else centre = Point(frame, s.centre);
                float a = Mathf.Lerp(s.fromAngle, s.toAngle, e) * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * s.radius;
                offset = frame != null ? frame.TransformDirection(offset) : Quaternion.LookRotation(shotForward) * offset;
                position = centre + offset + Vector3.up * s.height;
                look = centre;
            }
            else
            {
                position = Vector3.Lerp(Point(frame, s.from), Point(frame, s.to), e);
                look = Vector3.Lerp(Point(frame, s.fromLook), Point(frame, s.toLook), e);
            }
            cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(look - position, Vector3.up));
            cam.fieldOfView = s.fov;
            if (u >= 1f)
            {
                shot = (shot + 1) % shots.Count;
                shotStart = Time.time;
            }
        }

        // A point in the scene's own space (the attic / bedroom model), or round the player's feet without one.
        Vector3 Point(Transform frame, Vector3 p)
        {
            if (frame != null) return frame.TransformPoint(p);
            Vector3 right = Vector3.Cross(Vector3.up, shotForward);
            return shotFeet + right * p.x + Vector3.up * p.y + shotForward * p.z;
        }

        static Shot Move(Vector3 from, Vector3 fromLook, Vector3 to, Vector3 toLook, float seconds, float fov = 50f) =>
            new Shot { from = from, fromLook = fromLook, to = to, toLook = toLook, seconds = seconds, fov = fov };

        static Shot Orbit(Vector3 centre, float radius, float height, float fromAngle, float toAngle, float seconds, float fov = 50f) =>
            new Shot { orbit = true, centre = centre, radius = radius, height = height, fromAngle = fromAngle, toAngle = toAngle, seconds = seconds, fov = fov };

        static Shot AroundPlayer(float radius, float height, float fromAngle, float toAngle, float seconds, float fov = 55f) =>
            new Shot { orbit = true, aroundPlayer = true, radius = radius, height = height, fromAngle = fromAngle, toAngle = toAngle, seconds = seconds, fov = fov };

        void BuildShots()
        {
            shots.Clear();
            shot = 0;
            shotStart = Time.time;
            anchor = null;
            var bedroom = FindAnyObjectByType<BedroomDirector>();
            var attic = FindAnyObjectByType<AtticAtmosphere>();
            if (bedroom != null)
            {
                // The bedroom (room space: x -2.2 .. 2.2, z -1.4 .. 3.6, 2.6 m high; the bed on the left, the window in
                // the right wall, Ember's corner ahead on the right by the wardrobe).
                anchor = bedroom.transform;
                shots.Add(Move(new Vector3(1.9f, 2.2f, -1.1f), new Vector3(-1.0f, 0.6f, 1.4f), new Vector3(0.5f, 2.15f, -1.1f), new Vector3(-1.1f, 0.8f, 1.45f), 7f));
                shots.Add(Move(new Vector3(-1.8f, 1.25f, 2.7f), new Vector3(2.2f, 1.45f, 1.1f), new Vector3(-1.2f, 1.1f, 2.1f), new Vector3(2.2f, 1.3f, 1.1f), 6f, 45f));
                shots.Add(Move(new Vector3(0.1f, 1.3f, 1.3f), new Vector3(1.7f, 1.0f, 3.1f), new Vector3(0.7f, 1.15f, 2.0f), new Vector3(1.7f, 1.05f, 3.1f), 6f, 45f));
                shots.Add(AroundPlayer(2.0f, 0.35f, 150f, 290f, 12f));
            }
            else if (attic != null)
            {
                // The attic (attic space: x -2.5 .. 2.5, z -1.8 .. 4.2, 3.4 m to the ridge, the chest at z 0.9, the castle
                // gate in the front wall at z -1.8; the house front at z -7.4 and the yard 3.65 m below the floor).
                anchor = attic.transform;
                if (SceneManager.GetActiveScene().name == "Attic")
                    shots.Add(Move(new Vector3(-8f, -1.2f, -21f), new Vector3(0f, -0.5f, -5f), new Vector3(-4.5f, -0.8f, -14f), new Vector3(0f, 0f, -4f), 9f));
                shots.Add(Move(new Vector3(0f, 1.65f, -1.5f), new Vector3(0f, 0.5f, 0.9f), new Vector3(0f, 1.45f, -0.4f), new Vector3(0f, 0.45f, 0.9f), 7f));
                shots.Add(Orbit(new Vector3(0f, 0.4f, 0.9f), 1.5f, 0.5f, 125f, 235f, 9f));
                shots.Add(Move(new Vector3(-2.0f, 2.9f, 3.8f), new Vector3(0f, 0.5f, 0.9f), new Vector3(2.0f, 2.9f, 3.8f), new Vector3(0f, 0.5f, 0.9f), 8f));
                shots.Add(AroundPlayer(2.2f, 0.35f, 160f, 280f, 10f));
            }
            else
            {
                // Anywhere else (the cave, the Kalakh's world): round the player, then a slow rise away from them.
                shots.Add(AroundPlayer(2.4f, 0.35f, 140f, 260f, 12f));
                shots.Add(Move(new Vector3(0.6f, 1.9f, -1.5f), new Vector3(0f, 1.3f, 1.5f), new Vector3(1.5f, 3.5f, -5f), new Vector3(0f, 1.2f, 1f), 9f, 55f));
            }
            Debug.Log($"[Demo] {shots.Count} trailer shots for {sceneName}.");
        }
    }
}
