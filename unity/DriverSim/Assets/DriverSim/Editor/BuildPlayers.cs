using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DriverSim.Editor
{
    /// <summary>
    /// Batch-mode player builds. Each regenerates the scene first, then builds to Driver-Sim/unity/Builds/&lt;Platform&gt;
    /// (where <c>./gradlew simulateDriverSim</c> looks for the game), or to the path given with
    /// <c>-buildOutput &lt;path&gt;</c>. Exits the editor with code 1 when a build fails.
    /// </summary>
    public static class BuildPlayers
    {
        /// <summary>Builds the Linux player.</summary>
        [MenuItem("Driver-Sim/Build Linux Player")]
        public static void BuildLinux()
        {
            Build(BuildTarget.StandaloneLinux64, "Linux/DriverSim.x86_64");
        }

        /// <summary>Builds the Windows player.</summary>
        [MenuItem("Driver-Sim/Build Windows Player")]
        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64, "Windows/DriverSim.exe");
        }

        /// <summary>Builds every desktop player we ship.</summary>
        public static void BuildAll()
        {
            BuildLinux();
            BuildWindows();
        }

        private static void Build(BuildTarget target, string defaultRelativePath)
        {
            SceneBuilder.BuildScene();

            // output path: -buildOutput, else unity/Builds/<platform>/...
            string projectDir = Directory.GetParent(Application.dataPath).FullName;
            string outputPath = Path.Combine(projectDir, "..", "Builds", defaultRelativePath);
            string overridePath = GetArgument("-buildOutput");
            if (!string.IsNullOrEmpty(overridePath))
            {
                outputPath = overridePath;
            }
            outputPath = Path.GetFullPath(outputPath);

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { SceneBuilder.ScenePath },
                locationPathName = outputPath,
                target = target,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log("[Driver-Sim] build " + target + ": " + summary.result + " -> " + outputPath + " ("
                + summary.totalSize / (1024 * 1024) + " MB, " + summary.totalErrors + " errors)");
            if (summary.result != BuildResult.Succeeded && Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }

        private static string GetArgument(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == flag)
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}
