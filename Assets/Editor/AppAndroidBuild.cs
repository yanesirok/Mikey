using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds the actual game (SampleScene — title → map → level 0 → techniques) to an Android
/// APK. Distinct from Mikey.Pose.DevSandbox.EditorTools.AndroidBuilder, which builds only the
/// pose dev harness scene.
///
/// Player settings (IL2CPP, ARMv7, min SDK, com.mikey.equilibrium) already live in
/// ProjectSettings and are deliberately not re-set here — only the active build target is
/// switched, since a headless run may start on whatever target was last used.
///
/// Headless:
///   Unity.exe -quit -batchmode -nographics -projectPath &lt;proj&gt; -buildTarget Android
///     -executeMethod AppAndroidBuild.Build -logFile &lt;log&gt;
/// </summary>
public static class AppAndroidBuild
{
    private const string Scene = "Assets/Scenes/SampleScene.unity";
    private const string OutputApk = "Builds/Mikey.apk";

    [MenuItem("Mikey/Build Android APK (App)")]
    public static void Build()
    {
        if (!File.Exists(Scene))
        {
            Fail($"Scene not found: {Scene}");
            return;
        }

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        Directory.CreateDirectory(Path.GetDirectoryName(OutputApk));

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
            Debug.Log($"[AppAndroidBuild] BUILD OK -> {summary.outputPath} " +
                      $"({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:F0}s)");
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
        Debug.LogError($"[AppAndroidBuild] {message}");
        if (Application.isBatchMode)
            EditorApplication.Exit(1);
    }
}
