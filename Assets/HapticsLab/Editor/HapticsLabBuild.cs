using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HapticsLab.Editor
{
    /// <summary>
    /// "Haptics Lab > Build Quest APK": builds build/HapticsLab-v&lt;version&gt;.apk for the Quest
    /// (needs the Android Build Support module). The version is Player Settings > Version.
    /// From the command line: Unity -batchmode -buildTarget Android -projectPath . -quit
    ///   -executeMethod HapticsLab.Editor.HapticsLabBuild.BuildApkFromCommandLine
    /// </summary>
    public static class HapticsLabBuild
    {
        [MenuItem("Haptics Lab/Build Quest APK", priority = 23)]
        public static void BuildApk() => Build();

        public static void BuildApkFromCommandLine() => EditorApplication.Exit(Build() ? 0 : 1);

        static bool Build()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var path = Path.Combine("build", $"HapticsLab-v{PlayerSettings.bundleVersion}.apk");
            Directory.CreateDirectory("build");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            });

            bool ok = report.summary.result == BuildResult.Succeeded;
            if (ok)
                Debug.Log($"<b>[HapticsLab]</b> APK built: {Path.GetFullPath(path)} ({report.summary.totalSize / (1024f * 1024f):0.0} MB)");
            else
                Debug.LogError($"[HapticsLab] APK build failed: {report.summary.result} ({report.summary.totalErrors} errors). See the Console.");
            return ok;
        }
    }
}
