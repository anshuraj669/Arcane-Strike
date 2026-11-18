using System.IO;
using ArcaneStrike.Demo;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace ArcaneStrike.EditorTools
{
    /// <summary>
    /// The demo video, editor side. Menu: Arcane Strike → Demo Video (replay a take, how much to smooth it, capture on
    /// or off, open the folders). While a take replays (<see cref="DemoDirector"/>, F7) Unity Recorder films the Game
    /// view to Recordings/Video at a steady 60 fps (1920 × 1080, with sound): the game is stepped one 1/60 s frame at a
    /// time, so the video is smooth even if the laptop can't keep up live. H.264 MP4 where the editor can encode it
    /// (Windows, macOS), VP8 WebM otherwise (Linux), which is then turned into an MP4 beside it with ffmpeg automatically.
    /// </summary>
    [InitializeOnLoad]
    static class DemoVideoCapture
    {
        const string Menu = "Arcane Strike/Demo Video/";
        static RecorderController controller;
        static string lastVideo;
        static bool lastIsWebm;

        static DemoVideoCapture()
        {
            DemoDirector.ReplayStarted += Start;
            DemoDirector.ReplayEnded += Stop;
            DemoDirector.RecordingStarted += name => Start(name, true);
            DemoDirector.RecordingEnded += Stop;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode) Stop();
            };
        }

        static string VideoFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "Video"));

        static void Start(string takeName) => Start(takeName, false);

        /// <param name="live">Filming while you play (F6): real time, so the game isn't slowed down. A replay (F7) is
        /// filmed one fixed 1/60 s step at a time instead, which is smoother.</param>
        static void Start(string takeName, bool live)
        {
            Stop();
            if (PlayerPrefs.GetInt(DemoDirector.PrefCapture, 1) == 0) return;
            Directory.CreateDirectory(VideoFolder);

            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = "Demo video";
            movie.Enabled = true;
            var encoder = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
            };
            // (CoreEncoderSettings.SupportsCurrentPlatform() always says true in Recorder 5.1, but the Linux editor
            // can't write H.264: it fails with "Codec 'MP4' is not supported on this platform". So pick WebM there.)
#if UNITY_EDITOR_LINUX
            encoder.Codec = CoreEncoderSettings.OutputCodec.WEBM;
#else
            if (!encoder.SupportsCurrentPlatform()) encoder.Codec = CoreEncoderSettings.OutputCodec.WEBM;
#endif
            lastIsWebm = encoder.Codec == CoreEncoderSettings.OutputCodec.WEBM;
            movie.EncoderSettings = encoder;
            movie.CaptureAlpha = false;
            movie.CaptureAudio = true;
            movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1920, OutputHeight = 1080 };
            lastVideo = Path.Combine(VideoFolder, $"{takeName}_{System.DateTime.Now:HH-mm-ss}");
            movie.OutputFile = lastVideo;

            settings.AddRecorderSettings(movie);
            settings.SetRecordModeToManual();
            settings.FrameRate = 60f;
            settings.FrameRatePlayback = live ? FrameRatePlayback.Variable : FrameRatePlayback.Constant;
            settings.CapFrameRate = !live;
            settings.ExitPlayMode = false;
            RecorderOptions.VerboseMode = false;

            controller = new RecorderController(settings);
            controller.PrepareRecording();
            if (controller.StartRecording())
                Debug.Log($"[Demo] 🎥 Capturing {(live ? "what you play, live," : "the replay")} at 60 fps ({(encoder.Codec == CoreEncoderSettings.OutputCodec.MP4 ? "MP4" : "WebM")}, 1920×1080) to {VideoFolder}");
            else
            {
                Debug.LogError("[Demo] Unity Recorder didn't start (see the Console). The replay goes on without capture.");
                controller = null;
            }
        }

        static void Stop()
        {
            if (controller == null) return;
            if (controller.IsRecording())
            {
                controller.StopRecording();
                Debug.Log($"[Demo] 🎥 Saved the video: {lastVideo}.* (open the folder: Arcane Strike → Demo Video → Open Videos Folder)");
                if (lastIsWebm) ConvertToMp4Later(lastVideo + ".webm");
            }
            controller = null;
        }

        // ---------------------------------------------------------------- WebM → MP4 (Linux)

        /// <summary>
        /// Where the editor can only write WebM (Linux), turn the finished video into an H.264 MP4 with ffmpeg, beside
        /// it. Waits until the WebM has stopped growing (the recorder may still be finishing it after StopRecording),
        /// then runs ffmpeg in the background so the editor doesn't freeze. The WebM is kept.
        /// </summary>
        static void ConvertToMp4Later(string webm)
        {
            long lastSize = -1;
            double stableSince = 0, giveUpAt = EditorApplication.timeSinceStartup + 120;
            EditorApplication.CallbackFunction poll = null;
            poll = () =>
            {
                double now = EditorApplication.timeSinceStartup;
                if (now > giveUpAt)
                {
                    EditorApplication.update -= poll;
                    Debug.LogWarning($"[Demo] The WebM never appeared, so no MP4 was made: {webm}");
                    return;
                }
                if (!File.Exists(webm)) return;
                long size = new FileInfo(webm).Length;
                if (size != lastSize || size == 0) { lastSize = size; stableSince = now; return; }
                if (now - stableSince < 1.5) return;
                EditorApplication.update -= poll;
                RunFfmpeg(webm, Path.ChangeExtension(webm, ".mp4"));
            };
            EditorApplication.update += poll;
        }

        static void RunFfmpeg(string webm, string mp4)
        {
            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -i \"{webm}\" -c:v libx264 -crf 18 -preset medium -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart \"{mp4}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            System.Diagnostics.Process process;
            try { process = System.Diagnostics.Process.Start(info); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Demo] Couldn't run ffmpeg to make an MP4 ({e.Message}). The WebM is saved: {webm}");
                return;
            }
            Debug.Log($"[Demo] 🎞 Converting to MP4 in the background: {mp4}");
            var errors = new System.Text.StringBuilder();
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (errors) errors.AppendLine(e.Data); };
            process.EnableRaisingEvents = true;
            process.Exited += (_, __) =>
            {
                int code = process.ExitCode;
                EditorApplication.delayCall += () =>
                {
                    if (code == 0) Debug.Log($"[Demo] ✅ MP4 ready: {mp4}");
                    else lock (errors) Debug.LogError($"[Demo] ffmpeg failed (exit {code}); the WebM is still there: {webm}\n{errors}");
                };
            };
            process.BeginErrorReadLine();
        }

        // ---------------------------------------------------------------- the menu

        [MenuItem(Menu + "Replay a Take…", false, 0)]
        static void ReplayTake()
        {
            Directory.CreateDirectory(DemoTake.Folder);
            string path = EditorUtility.OpenFilePanel("Replay a demo take", DemoTake.Folder, "take");
            if (string.IsNullOrEmpty(path)) return;
            PlayerPrefs.SetString(DemoDirector.PrefTake, path);
            if (EditorApplication.isPlaying && DemoDirector.Instance != null) DemoDirector.Instance.StartReplay(path);
            else
            {
                PlayerPrefs.SetInt(DemoDirector.PrefAutoReplay, 1);
                EditorApplication.isPlaying = true;
            }
        }

        [MenuItem(Menu + "Replay the Latest Take", false, 1)]
        static void ReplayLatest()
        {
            string path = DemoTake.Latest();
            if (path == null)
            {
                EditorUtility.DisplayDialog("Demo Video", "No takes yet. Press Play, then F6 to record one (F6 again saves it).", "OK");
                return;
            }
            PlayerPrefs.SetString(DemoDirector.PrefTake, path);
            if (EditorApplication.isPlaying && DemoDirector.Instance != null) DemoDirector.Instance.StartReplay(path);
            else
            {
                PlayerPrefs.SetInt(DemoDirector.PrefAutoReplay, 1);
                EditorApplication.isPlaying = true;
            }
        }

        [MenuItem(Menu + "Smoothing/Off (raw)", false, 20)] static void S0() => Smoothing(0);
        [MenuItem(Menu + "Smoothing/Light", false, 21)] static void S1() => Smoothing(1);
        [MenuItem(Menu + "Smoothing/Medium", false, 22)] static void S2() => Smoothing(2);
        [MenuItem(Menu + "Smoothing/Strong", false, 23)] static void S3() => Smoothing(3);
        [MenuItem(Menu + "Smoothing/Off (raw)", true)] static bool V0() => Check("Off (raw)", 0);
        [MenuItem(Menu + "Smoothing/Light", true)] static bool V1() => Check("Light", 1);
        [MenuItem(Menu + "Smoothing/Medium", true)] static bool V2() => Check("Medium", 2);
        [MenuItem(Menu + "Smoothing/Strong", true)] static bool V3() => Check("Strong", 3);

        static void Smoothing(int level)
        {
            PlayerPrefs.SetInt(DemoDirector.PrefSmoothing, level);
            Debug.Log($"[Demo] Replays are smoothed: {DemoTake.Smoothing.Names[level]} (from the next replay).");
        }

        static bool Check(string name, int level)
        {
            UnityEditor.Menu.SetChecked(Menu + "Smoothing/" + name, PlayerPrefs.GetInt(DemoDirector.PrefSmoothing, 2) == level);
            return true;
        }

        [MenuItem(Menu + "Capture Video While Replaying", false, 40)]
        static void ToggleCapture()
        {
            bool on = PlayerPrefs.GetInt(DemoDirector.PrefCapture, 1) == 0;
            PlayerPrefs.SetInt(DemoDirector.PrefCapture, on ? 1 : 0);
            Debug.Log($"[Demo] Video capture on replay: {(on ? "on" : "off (replays only play)")}.");
        }

        [MenuItem(Menu + "Capture Video While Replaying", true)]
        static bool ToggleCaptureCheck()
        {
            UnityEditor.Menu.SetChecked(Menu + "Capture Video While Replaying", PlayerPrefs.GetInt(DemoDirector.PrefCapture, 1) == 1);
            return true;
        }

        [MenuItem(Menu + "Win the Monster World Fight (F10)", false, 50)]
        static void WinFight()
        {
            var arena = Object.FindAnyObjectByType<ArcaneStrike.Story.RiftArena>();
            if (arena != null) arena.WinNow();
            else Debug.LogWarning("[Demo] Play the MonsterWorld scene first: there is no fight running.");
        }

        [MenuItem(Menu + "Win the Monster World Fight (F10)", true)]
        static bool WinFightCheck() => EditorApplication.isPlaying;

        [MenuItem(Menu + "Open Takes Folder", false, 60)]
        static void OpenTakes()
        {
            Directory.CreateDirectory(DemoTake.Folder);
            EditorUtility.RevealInFinder(DemoTake.Folder);
        }

        [MenuItem(Menu + "Open Videos Folder", false, 61)]
        static void OpenVideos()
        {
            Directory.CreateDirectory(VideoFolder);
            EditorUtility.RevealInFinder(VideoFolder);
        }

        [MenuItem(Menu + "How It Works", false, 80)]
        static void Help() => EditorUtility.DisplayDialog("Demo Video",
            "While playing:\n" +
            "F6  restart this scene and record a take (play it normally); F6 again saves it, and the video of it\n" +
            "     (filmed live) to Recordings/Video as WebM + MP4.\n" +
            "F7  replay the latest take, smoothed, and film it to Recordings/Video at 60 fps. F7 or Esc stops.\n" +
            "F8  camera: first person → over the shoulder → trailer shots of this scene.\n\n" +
            "Record a few short takes per scene, replay the good ones (try each camera), and cut them together.\n" +
            "Smoothing (this menu) changes how steady the replay is; the raw take is kept, so you can re-film it any time.",
            "OK");
    }
}
