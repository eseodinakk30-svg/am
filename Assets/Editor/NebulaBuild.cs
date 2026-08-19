// -----------------------------------------------------------------------------
//  NEBULA NINE - one click / CI builds.
//  Menu: Nebula Nine > Build Android (APK) | Build Android (AAB) | Build Windows
//  CLI:  Unity -batchmode -quit -projectPath . -executeMethod Nebula.EditorTools.NebulaBuild.AndroidApk
// -----------------------------------------------------------------------------

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Nebula.EditorTools
{
    public static class NebulaBuild
    {
        private const string OutputDir = "Builds";

        [MenuItem("Nebula Nine/Build Android (APK)", false, 20)]
        public static void AndroidApk() => Build(BuildTarget.Android, "NebulaNine.apk", false);

        [MenuItem("Nebula Nine/Build Android (AAB)", false, 21)]
        public static void AndroidAab() => Build(BuildTarget.Android, "NebulaNine.aab", true);

        [MenuItem("Nebula Nine/Build Windows", false, 22)]
        public static void Windows() => Build(BuildTarget.StandaloneWindows64, "NebulaNine.exe", false);

        /// <summary>
        /// CI smoke test: Unity compiles every script before it can invoke an
        /// -executeMethod target, so simply reaching this method proves the whole
        /// project builds. Much faster than a full Android build (no IL2CPP, no NDK),
        /// which makes it the right first thing to run on a fresh licence.
        /// </summary>
        public static void CompileOnly()
        {
            NebulaProjectSetup.RunSetup(false);
            Debug.Log("[Nebula Nine] Compilation OK. " +
                      Map.StationLayout.Areas.Count + " areas, " +
                      Map.StationLayout.Vents.Count + " vents, " +
                      Tasks.TaskCatalog.All.Count + " task definitions.");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void Build(BuildTarget target, string fileName, bool appBundle)
        {
            NebulaProjectSetup.RunSetup(false);
            Directory.CreateDirectory(OutputDir);
            EditorUserBuildSettings.buildAppBundle = appBundle;

            var scenes = new[] { NebulaProjectSetup.BootScenePath };
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(OutputDir, fileName),
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[Nebula Nine] Build OK: {summary.outputPath} " +
                          $"({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:F0} s)");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[Nebula Nine] Build {summary.result}: {summary.totalErrors} errors.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }
    }
}
