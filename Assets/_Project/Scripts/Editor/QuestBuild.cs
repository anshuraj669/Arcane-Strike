using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace ArcaneStrike.EditorTools
{
    /// <summary>
    /// Arcane Strike → Build for Quest: sets the Android/Quest options a steady 72 fps needs, then builds
    /// Builds/ArcaneStrike.apk from the scenes enabled in Build Settings. The options are IL2CPP on ARM64, Vulkan only,
    /// single-pass multiview, ASTC textures and the lean "Mobile" quality level. The headset's display rate and clocks
    /// are set at start-up by <see cref="ArcaneStrike.Core.QuestPerformance"/>.
    /// Arcane Strike → Install on Quest puts the last build on the headset connected by USB (adb).
    /// </summary>
    static class QuestBuild
    {
        const string Apk = "Builds/ArcaneStrike.apk";

        [MenuItem("Arcane Strike/Build for Quest (APK)", priority = 400)]
        static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Build for Quest", "Stop Play mode first.", "OK");
                return;
            }
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                EditorUtility.DisplayDialog("Build for Quest", "Couldn't switch to Android. Is Android Build Support installed in Unity Hub?", "OK");
                return;
            }
            if (MakeApk(Apk)) EditorUtility.RevealInFinder(Apk);
        }

        /// <summary>For a build from the command line (Unity -batchmode -buildTarget Android -executeMethod
        /// ArcaneStrike.EditorTools.QuestBuild.BuildFromCommandLine [-apk path]); exits 0 when the APK is built.</summary>
        public static void BuildFromCommandLine()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-apk");
            bool ok = MakeApk(i >= 0 && i + 1 < args.Length ? args[i + 1] : Apk);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool MakeApk(string apk)
        {
            Configure();
            if (!Sign()) return false;
            // Minutes since 2026: always higher than the last upload (the dashboard refuses a version code it has seen),
            // wherever the build was made.
            PlayerSettings.Android.bundleVersionCode = (int)((System.DateTime.UtcNow - new System.DateTime(2026, 1, 1)).TotalMinutes);

            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(apk)));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = apk,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            });
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[Quest] ✅ Built {Path.GetFullPath(apk)} ({report.summary.totalSize / (1024 * 1024)} MB, {scenes.Length} scenes, " +
                          $"version code {PlayerSettings.Android.bundleVersionCode}). Arcane Strike → Install on Quest puts it on the headset.");
                return true;
            }
            Debug.LogError($"[Quest] Build {report.summary.result}: {report.summary.totalErrors} errors (see the Console above).");
            return false;
        }

        /// <summary>The player and graphics options for the Quest (safe to run any time).</summary>
        [MenuItem("Arcane Strike/Apply Quest Settings", priority = 401)]
        static void Configure()
        {
            var android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, "com.teamhello.arcanestrike");
            PlayerSettings.companyName = "Team Hello";
            PlayerSettings.productName = "Arcane Strike";
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            if ((int)PlayerSettings.Android.minSdkVersion < 32) PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)32;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing; // (multiview on the Quest)
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Minimal);
            PlayerSettings.SetIl2CppCompilerConfiguration(android, Il2CppCompilerConfiguration.Release);
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.Android.forceInternetPermission = false;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            EditorUserBuildSettings.development = false;

            // The Quest uses the "Mobile" quality level (its URP asset: MSAA 4x, no HDR, one shadow cascade).
            string[] levels = QualitySettings.names;
            int mobile = System.Array.IndexOf(levels, "Mobile");
            if (mobile >= 0)
            {
                var so = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
                SerializedProperty perPlatform = so.FindProperty("m_PerPlatformDefaultQuality");
                for (int i = 0; i < perPlatform.arraySize; i++)
                {
                    SerializedProperty e = perPlatform.GetArrayElementAtIndex(i);
                    if (e.FindPropertyRelative("first").stringValue == "Android") e.FindPropertyRelative("second").intValue = mobile;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Quest] Settings applied: IL2CPP ARM64, Vulkan, multiview, ASTC, linear, quality \"Mobile\"; 72 Hz at start-up.");
        }

        // The Meta dashboard won't take an APK signed with the debug key: sign with the release key in Keys/ (kept out of
        // git; the password lives next to it in keystore.properties, so it never goes into ProjectSettings).
        static bool Sign()
        {
            const string props = "Keys/keystore.properties";
            if (!File.Exists(props))
            {
                EditorUtility.DisplayDialog("Build for Quest", $"No release key ({props} is missing), so the APK can't be uploaded to the Meta dashboard.", "OK");
                return false;
            }
            var kv = File.ReadAllLines(props).Where(l => l.Contains('=')).ToDictionary(l => l.Substring(0, l.IndexOf('=')).Trim(),
                                                                                         l => l.Substring(l.IndexOf('=') + 1).Trim());
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = Path.GetFullPath(kv["keystore"]);
            PlayerSettings.Android.keystorePass = kv["password"];
            PlayerSettings.Android.keyaliasName = kv["alias"];
            PlayerSettings.Android.keyaliasPass = kv["password"];
            return true;
        }

        [MenuItem("Arcane Strike/Install on Quest", priority = 402)]
        static void Install()
        {
            if (!File.Exists(Apk))
            {
                EditorUtility.DisplayDialog("Install on Quest", "Build it first (Arcane Strike → Build for Quest).", "OK");
                return;
            }
            string adb = Adb();
            string devices = Run(adb, "devices");
            string quest = devices.Split('\n').Select(l => l.Trim()).Where(l => l.EndsWith("device") && !l.StartsWith("emulator"))
                                  .Select(l => l.Split('\t')[0]).FirstOrDefault();
            if (quest == null)
            {
                EditorUtility.DisplayDialog("Install on Quest",
                    "No headset found. Plug the Quest in by USB, put it on and allow USB debugging, then try again.\n\n" + devices, "OK");
                return;
            }
            string result = Run(adb, $"-s {quest} install -r \"{Path.GetFullPath(Apk)}\"");
            Debug.Log($"[Quest] adb install: {result}");
            if (result.Contains("Success"))
            {
                Run(adb, $"-s {quest} shell monkey -p {PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)} -c android.intent.category.LAUNCHER 1");
                Debug.Log("[Quest] ✅ Installed and started on the headset (also under Library → Unknown Sources).");
            }
        }

        static string Adb()
        {
            string sdk = UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath;
            string bundled = string.IsNullOrEmpty(sdk) ? null : Path.Combine(sdk, "platform-tools", "adb");
            return bundled != null && File.Exists(bundled) ? bundled : "adb";
        }

        static string Run(string exe, string args)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(exe, args)
                    { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
                string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(120000);
                return output.Trim();
            }
            catch (System.Exception e)
            {
                return $"(couldn't run {exe}: {e.Message})";
            }
        }
    }
}
