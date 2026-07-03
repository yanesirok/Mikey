using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Mikey.Pose.DevSandbox.EditorTools
{
    /// <summary>
    /// Builds the ExerciseSandbox scene to an Android APK for on-device pose testing.
    /// Run from the menu, or headless:
    ///   Unity.exe -quit -batchmode -nographics -projectPath &lt;proj&gt; -buildTarget Android
    ///     -executeMethod Mikey.Pose.DevSandbox.EditorTools.AndroidBuilder.BuildAndroid
    ///     -logFile &lt;log&gt;
    /// </summary>
    public static class AndroidBuilder
    {
        private const string Scene = "Assets/Scenes/ExerciseSandbox.unity";
        private const string OutputDir = "Builds";
        private const string OutputApk = "Builds/ExerciseSandbox.apk";

        [MenuItem("Mikey/Dev/Build Android (Exercise Sandbox)")]
        public static void BuildAndroid()
        {
            if (!File.Exists(Scene))
            {
                Fail($"Scene not found: {Scene}. Run 'Mikey/Dev/Create or Open Exercise Sandbox Scene' first.");
                return;
            }

            // Android player config for this device (ARM64, IL2CPP, MediaPipe needs SDK 24+).
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            // Target device (Redmi 9A) ships a 32-bit ROM: abilist is armeabi-v7a only.
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.mikey.equilibrium");

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            Directory.CreateDirectory(OutputDir);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = OutputApk,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[AndroidBuilder] BUILD OK -> {summary.outputPath} ({summary.totalSize} bytes)");
                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
            }
            else
            {
                Fail($"BUILD {summary.result}: {summary.totalErrors} error(s). See log above.");
            }
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[AndroidBuilder] {message}");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
        }
    }
}
