using System;
using System.IO;
using ArcaneStrike.HandInput;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ArcaneStrike.Demo
{
    /// <summary>
    /// Records demo takes and replays them smoothly, for the demo video (editor only, while playing):
    /// - F6: restart this scene and record a take (play it normally with the webcam, keys and mouse); F6 again saves it
    ///   to Recordings/Takes. A red "● REC" shows in the corner while recording (not in the replays).
    /// - F7: replay the latest take (or the one picked in Arcane Strike → Demo Video), smoothed, from the start of its
    ///   scene. The game runs again with the recorded hands, head, keys and mouse, so everything happens as it did,
    ///   only steadier. With "Capture video" on (the default), Unity Recorder saves it to Recordings/Video at a steady
    ///   60 fps. F7 (or Esc) during a replay stops it.
    /// - F8: the camera: first person → over the shoulder → this scene's trailer shots (<see cref="CinematicCamera"/>).
    ///   Works while playing, recording or replaying, so one take can be filmed several ways.
    /// The same random seed is used for a take and its replays, so random things (which box the lantern is in) match.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public class DemoDirector : MonoBehaviour
    {
        public enum State { Idle, StartingRecord, Recording, StartingReplay, Replaying }

        public static DemoDirector Instance { get; private set; }

        /// <summary>The editor's video capture (Unity Recorder) listens to these: a replay started / ended.</summary>
        public static event Action<string> ReplayStarted;
        public static event Action ReplayEnded;
        /// <summary>The editor's video capture also films a take live while it is recorded (F6): started / ended.</summary>
        public static event Action<string> RecordingStarted;
        public static event Action RecordingEnded;

        public const string PrefTake = "ArcaneDemo.Take", PrefSmoothing = "ArcaneDemo.Smoothing", PrefCapture = "ArcaneDemo.Capture",
                            PrefAutoReplay = "ArcaneDemo.AutoReplay";

        public State Current { get; private set; }

        /// <summary>A take is being filmed (replayed, or recorded with capture on): debug overlays keep off the screen.</summary>
        public static bool Filming => Instance != null && (Instance.Current == State.Replaying || Instance.Current == State.StartingReplay
            || (Instance.Current == State.Recording && PlayerPrefs.GetInt(PrefCapture, 1) == 1));
        public float Time01 => replay == null || replay.Duration <= 0f ? 0f : Mathf.Clamp01(clock / replay.Duration);

        DemoTake take, replay;
        float clock, start;
        int seed;
        string takeName;
        readonly ReplaySource source = new ReplaySource();
        Transform head, rig;
        WebcamHeadDriver headDriver;
        Matrix4x4 rigNow = Matrix4x4.identity;
        // Replay bookkeeping: where in the take the last frame got to, and the head moves applied so far.
        float lastClock;
        Vector3 appliedLean;
        float appliedTurn, appliedNod;
        Vector2 mouseThisFrame;
        bool sceneReady;
        // Scene changes in a take (Attic → temple → ... → bedroom): each one re-seeds the random numbers, and the
        // replay lines each new scene up with the moment it loaded in the take (loading takes a different time each run).
        int sceneCount, nextMark;
        float frozenFor;
        // Recording: the head driver's moves as one running total across scene changes (each scene has its own driver).
        WebcamHeadDriver countedDriver;
        Vector3 driverLean, sumLean;
        float driverTurn, driverNod, sumTurn, sumNod;
        ulong pressedThisFrame, releasedThisFrame, heldThisFrame;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (!Application.isEditor || Instance != null) return;
            var go = new GameObject("DemoDirector");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<DemoDirector>();
            go.AddComponent<CinematicCamera>();
        }

        void OnEnable() => SceneManager.sceneLoaded += SceneLoaded;

        // (Arcane Strike → Demo Video → Replay… from outside Play mode: replay as soon as the game is running.)
        void Start()
        {
            if (PlayerPrefs.GetInt(PrefAutoReplay, 0) == 0) return;
            PlayerPrefs.SetInt(PrefAutoReplay, 0);
            StartReplay(PlayerPrefs.GetString(PrefTake, ""));
        }

        // Play stopped (or the game quit) mid-take: keep what was recorded instead of losing it.
        void OnApplicationQuit()
        {
            if (Current == State.Recording) StopRecording();
        }

        void OnDisable()
        {
            if (Current == State.Recording) StopRecording();
            SceneManager.sceneLoaded -= SceneLoaded;
            EndReplayHooks();
        }

        void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            head = null;
            headDriver = null;
            if (Current == State.StartingRecord || Current == State.StartingReplay)
            {
                UnityEngine.Random.InitState(seed); // before the new scene's Start()s roll anything
                sceneReady = true;
                sceneCount = nextMark = 0;
                frozenFor = 0f;
            }
            else if (Current == State.Recording)
            {
                UnityEngine.Random.InitState(seed + ++sceneCount);
                take.sceneMarks.Add(new DemoTake.SceneMark { time = Time.time - start, scene = scene.name });
                Debug.Log($"[Demo] ● The take goes on in {scene.name}.");
            }
            else if (Current == State.Replaying)
            {
                UnityEngine.Random.InitState(seed + ++sceneCount);
                for (int i = nextMark; i < replay.sceneMarks.Count; i++)
                {
                    if (replay.sceneMarks[i].scene != scene.name) continue;
                    // Carry on from the moment this scene loaded in the take.
                    float at = replay.sceneMarks[i].time;
                    start = Time.time - at;
                    clock = at;
                    lastClock = Mathf.Max(lastClock, at - 1e-4f);
                    nextMark = i + 1;
                    frozenFor = 0f;
                    break;
                }
            }
        }

        // ---------------------------------------------------------------- the keys

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f6Key.wasPressedThisFrame)
                {
                    if (Current == State.Recording) StopRecording();
                    else if (Current == State.Idle) StartRecording();
                }
                if (kb.f7Key.wasPressedThisFrame || (kb.escapeKey.wasPressedThisFrame && Current == State.Replaying))
                {
                    if (Current == State.Replaying || Current == State.StartingReplay) StopReplay("stopped");
                    else if (Current == State.Idle) StartReplay(PlayerPrefs.GetString(PrefTake, ""));
                }
            }

            if (!FindRig()) return;
            if ((Current == State.StartingRecord || Current == State.StartingReplay) && !sceneReady) return;
            switch (Current)
            {
                case State.StartingRecord:
                    // The first frame of the restarted scene: the take starts now.
                    start = Time.time;
                    countedDriver = null;
                    sumLean = Vector3.zero;
                    sumTurn = sumNod = 0f;
                    Current = State.Recording;
                    RecordingStarted?.Invoke($"{take.scene}_live");
                    Debug.Log($"[Demo] ● Recording a take of {take.scene}. Play it through; F6 again saves it.");
                    goto case State.Recording;
                case State.Recording:
                    clock = Time.time - start;
                    rigNow = rig.localToWorldMatrix; // (the hands are read at this rig pose, before anything moves it)
                    break;
                case State.StartingReplay:
                    start = Time.time;
                    lastClock = -1e-4f;
                    appliedLean = replay.frames[0].lean;
                    appliedTurn = replay.frames[0].turn;
                    appliedNod = replay.frames[0].nod;
                    Current = State.Replaying;
                    ReplayStarted?.Invoke(takeName);
                    Debug.Log($"[Demo] ▶ Replaying {takeName} ({replay.Duration:F0} s). F7 or Esc stops.");
                    goto case State.Replaying;
                case State.Replaying:
                    ReplayFrame();
                    break;
            }
        }

        bool FindRig()
        {
            if (head == null)
            {
                var input = FindAnyObjectByType<HandInputService>();
                head = input != null && input.Head != null ? input.Head : Camera.main != null ? Camera.main.transform : null;
                if (head == null) return false;
                rig = head.root;
            }
            if (headDriver == null) headDriver = FindAnyObjectByType<WebcamHeadDriver>(); // (added just after the scene loads)
            return true;
        }

        // ---------------------------------------------------------------- recording

        void StartRecording()
        {
            seed = Environment.TickCount;
            take = new DemoTake { scene = SceneManager.GetActiveScene().name, seed = seed, recordedAt = DateTime.Now.ToString("u") };
            Current = State.StartingRecord;
            sceneReady = false;
            SceneManager.LoadScene(take.scene);
        }

        void StopRecording()
        {
            Current = State.Idle;
            RecordingEnded?.Invoke();
            if (take.frames.Count < 10)
            {
                Debug.LogWarning("[Demo] The take was too short to keep.");
                return;
            }
            string path = take.Save();
            PlayerPrefs.SetString(PrefTake, path);
            Debug.Log($"[Demo] ■ Saved the take ({take.Duration:F0} s): {path}. F7 replays it smoothly.");
        }

        // Every frame of a take, after everything has run (hands, head, keys).
        void LateUpdate()
        {
            if (Current != State.Recording || head == null) return;
            var input = FindAnyObjectByType<HandInputService>();
            var f = new DemoTake.Frame { time = clock };
            Matrix4x4 toRig = rigNow.inverse;
            if (input != null)
            {
                if (input.ActiveSource is WebcamHandSource) take.webcam = true;
                for (int h = 0; h < 2; h++)
                {
                    HandFrame hand = input.Get((HandSide)h);
                    f.handTracked[h] = hand.IsTracked;
                    f.handConfidence[h] = hand.Confidence;
                    for (int j = 0; j < HandFrame.JointCount; j++) f.hands[h][j] = toRig.MultiplyPoint3x4(hand.Joints[j]);
                }
                UpperBodyFrame body = input.Body;
                f.bodyTracked = body.IsTracked;
                f.bodyConfidence = body.Confidence;
                for (int j = 0; j < UpperBodyFrame.JointCount; j++)
                {
                    f.body[j] = toRig.MultiplyPoint3x4(body.Joints[j]);
                    f.bodyVisibility[j] = body.Visibility[j];
                }
            }
            if (headDriver != null)
            {
                if (headDriver != countedDriver)
                {
                    countedDriver = headDriver; // a new scene's driver: count on from where the last one left off
                    driverLean = headDriver.TotalLean;
                    driverTurn = headDriver.TotalTurn;
                    driverNod = headDriver.TotalNod;
                }
                sumLean += headDriver.TotalLean - driverLean;
                sumTurn += headDriver.TotalTurn - driverTurn;
                sumNod += headDriver.TotalNod - driverNod;
                driverLean = headDriver.TotalLean;
                driverTurn = headDriver.TotalTurn;
                driverNod = headDriver.TotalNod;
            }
            f.lean = sumLean;
            f.turn = sumTurn;
            f.nod = sumNod;
            f.mouse = GameInput.RawMouseDelta();
            int count = Mathf.Min(DemoTake.Actions, Enum.GetValues(typeof(GameAction)).Length);
            for (int a = 0; a < count; a++)
            {
                var action = (GameAction)a;
                ulong bit = 1UL << a;
                if (GameInput.RawHeld(action)) f.held |= bit;
                if (GameInput.RawPressed(action)) f.pressed |= bit;
                if (GameInput.RawReleased(action)) f.released |= bit;
            }
            // (the recorder's own keys aren't game actions, so F6 / F7 / F8 never end up in a take)
            take.frames.Add(f);
        }

        // ---------------------------------------------------------------- replaying

        /// <summary>Replays a take file (empty: the latest one).</summary>
        public void StartReplay(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) path = DemoTake.Latest();
            if (path == null)
            {
                Debug.LogWarning($"[Demo] No takes yet: press F6 to record one (they go to {DemoTake.Folder}).");
                return;
            }
            DemoTake raw;
            try { raw = DemoTake.Load(path); }
            catch (Exception e)
            {
                Debug.LogError($"[Demo] Couldn't read {path}: {e.Message}");
                return;
            }
            if (raw.frames.Count < 2)
            {
                Debug.LogWarning($"[Demo] {path} is empty.");
                return;
            }
            if (!Application.CanStreamedLevelBeLoaded(raw.scene))
            {
                Debug.LogError($"[Demo] The take starts in '{raw.scene}', which isn't in the Build Profile's scene list.");
                return;
            }
            int level = PlayerPrefs.GetInt(PrefSmoothing, 2);
            replay = raw.Smoothed(DemoTake.Smoothing.Preset(level));
            takeName = Path.GetFileNameWithoutExtension(path);
            seed = replay.seed;
            Debug.Log($"[Demo] Smoothing: {DemoTake.Smoothing.Names[Mathf.Clamp(level, 0, 3)]}.");

            source.director = this;
            HandInputService.ReplaySource = source;
            HandInputService.ReplayIsWebcam = replay.webcam;
            GameInput.ReplayPressed = a => Bit(pressedThisFrame, a);
            GameInput.ReplayHeld = a => Bit(heldThisFrame, a);
            GameInput.ReplayReleased = a => Bit(releasedThisFrame, a);
            GameInput.ReplayMouse = () => mouseThisFrame;
            Current = State.StartingReplay;
            sceneReady = false;
            SceneManager.LoadScene(replay.scene);
        }

        static bool Bit(ulong mask, GameAction a) => (int)a < DemoTake.Actions && (mask & (1UL << (int)a)) != 0;

        void ReplayFrame()
        {
            clock = Time.time - start;
            // The take changed scene here, but the replay's scene hasn't loaded yet: hold the take until it has.
            if (nextMark < replay.sceneMarks.Count && clock > replay.sceneMarks[nextMark].time)
            {
                frozenFor += Time.deltaTime;
                if (frozenFor < 20f)
                {
                    clock = replay.sceneMarks[nextMark].time;
                    start = Time.time - clock;
                }
                else
                {
                    Debug.LogWarning($"[Demo] The replay never reached {replay.sceneMarks[nextMark].scene} (it played out differently); going on anyway.");
                    nextMark++;
                    frozenFor = 0f;
                }
            }
            if (clock > replay.Duration + 0.5f)
            {
                StopReplay("finished");
                return;
            }
            rigNow = rig.localToWorldMatrix; // (the hands go where they were, relative to the player, at this rig pose)

            // The keys and the mouse of every recorded frame since the last game frame (the replay may run at a
            // different frame rate from the recording: a press is never lost, and the mouse adds up to the same turn).
            pressedThisFrame = releasedThisFrame = 0;
            mouseThisFrame = Vector2.zero;
            int from = replay.IndexAt(lastClock) + 1, to = replay.IndexAt(clock);
            for (int i = Mathf.Max(0, from); i <= to; i++)
            {
                DemoTake.Frame f = replay.frames[i];
                pressedThisFrame |= f.pressed;
                releasedThisFrame |= f.released;
                mouseThisFrame += f.mouse;
            }
            heldThisFrame = to >= 0 ? replay.frames[to].held : 0;
            lastClock = clock;

            // The webcam head moves, smoothed: applied as the driver did, by the change since last frame.
            if (headDriver != null && headDriver.enabled) headDriver.enabled = false;
            Sample(clock, out DemoTake.Frame a, out DemoTake.Frame b, out float u);
            Vector3 lean = Vector3.Lerp(a.lean, b.lean, u);
            float turn = Mathf.Lerp(a.turn, b.turn, u), nod = Mathf.Lerp(a.nod, b.nod, u);
            rig.position += lean - appliedLean;
            rig.RotateAround(head.position, Vector3.up, turn - appliedTurn);
            if (Mathf.Abs(nod - appliedNod) > 1e-5f) head.localRotation = Quaternion.Euler(nod - appliedNod, 0f, 0f) * head.localRotation;
            appliedLean = lean;
            appliedTurn = turn;
            appliedNod = nod;
        }

        void Sample(float t, out DemoTake.Frame a, out DemoTake.Frame b, out float u)
        {
            int i = Mathf.Clamp(replay.IndexAt(t), 0, replay.frames.Count - 1);
            int j = Mathf.Min(i + 1, replay.frames.Count - 1);
            a = replay.frames[i];
            b = replay.frames[j];
            u = j == i ? 0f : Mathf.Clamp01(Mathf.InverseLerp(a.time, b.time, t));
        }

        public void StopReplay(string why)
        {
            if (Current != State.Replaying && Current != State.StartingReplay) return;
            Current = State.Idle;
            EndReplayHooks();
            if (headDriver != null) headDriver.enabled = true;
            Debug.Log($"[Demo] ■ Replay {why}.");
            ReplayEnded?.Invoke();
        }

        static void EndReplayHooks()
        {
            HandInputService.ReplaySource = null;
            HandInputService.ReplayIsWebcam = false;
            GameInput.ReplayPressed = GameInput.ReplayHeld = GameInput.ReplayReleased = null;
            GameInput.ReplayMouse = null;
        }

        // The hands and body of the take at the replay's time, back in the world from the player's rig.
        class ReplaySource : IHandSource, IUpperBodySource
        {
            public DemoDirector director;

            public bool IsActive => director != null && director.Current == State.Replaying;

            public bool TryGetHand(HandSide side, HandFrame frame)
            {
                if (!IsActive) return false;
                director.Sample(director.clock, out DemoTake.Frame a, out DemoTake.Frame b, out float u);
                int h = (int)side;
                bool ta = a.handTracked[h], tb = b.handTracked[h];
                if (!ta && !tb) return false;
                if (!ta) { a = b; u = 0f; }
                else if (!tb) { b = a; u = 0f; }
                Matrix4x4 m = director.rigNow;
                for (int j = 0; j < HandFrame.JointCount; j++) frame.Joints[j] = m.MultiplyPoint3x4(Vector3.Lerp(a.hands[h][j], b.hands[h][j], u));
                frame.Confidence = Mathf.Lerp(a.handConfidence[h], b.handConfidence[h], u);
                frame.Time = Time.time;
                return true;
            }

            public bool TryGetBody(UpperBodyFrame frame)
            {
                if (!IsActive) return false;
                director.Sample(director.clock, out DemoTake.Frame a, out DemoTake.Frame b, out float u);
                if (!a.bodyTracked && !b.bodyTracked) return false;
                if (!a.bodyTracked) { a = b; u = 0f; }
                else if (!b.bodyTracked) { b = a; u = 0f; }
                Matrix4x4 m = director.rigNow;
                for (int j = 0; j < UpperBodyFrame.JointCount; j++)
                {
                    frame.Joints[j] = m.MultiplyPoint3x4(Vector3.Lerp(a.body[j], b.body[j], u));
                    frame.Visibility[j] = Mathf.Lerp(a.bodyVisibility[j], b.bodyVisibility[j], u);
                }
                frame.Confidence = Mathf.Lerp(a.bodyConfidence, b.bodyConfidence, u);
                frame.Time = Time.time;
                return true;
            }
        }

        // ---------------------------------------------------------------- what's on screen while recording

        void OnGUI()
        {
            if (Current != State.Recording) return; // (never in a replay: that's what goes in the video)
            if (PlayerPrefs.GetInt(PrefCapture, 1) == 1) return; // (being filmed live: keep it out of the video)
            var style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            style.normal.textColor = (Time.unscaledTime % 1f) < 0.6f ? new Color(1f, 0.15f, 0.1f) : new Color(1f, 0.15f, 0.1f, 0.35f);
            TimeSpan t = TimeSpan.FromSeconds(clock);
            GUI.Label(new Rect(14, 10, 320, 30), $"● REC  {t:mm\\:ss}   (F6 saves)", style);
        }
    }
}
