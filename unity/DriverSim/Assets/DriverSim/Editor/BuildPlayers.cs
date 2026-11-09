using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace DriverSim.Editor
{
    /// <summary>
    /// Batch-mode player builds. Each regenerates the scene first, then builds to
    /// Fieldrunner/unity/Builds/&lt;Platform&gt; (where <c>./gradlew simulateDriverSim</c> looks for the game), or to
    /// the path given with <c>-buildOutput &lt;path&gt;</c>. Exits the editor with code 1 when a build fails.
    ///
    /// Each build sets its platform's graphics APIs explicitly, in the order the player tries them: the first one the
    /// machine supports wins, so a player with a GPU driver uses the GPU and still starts without one.
    /// </summary>
    public static class BuildPlayers
    {
        /// <summary>Builds the Linux player.</summary>
        [MenuItem("Fieldrunner/Build Linux Player")]
        public static void BuildLinux()
        {
            GraphicsDeviceType[] graphicsApis = { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLCore };
            Build(BuildTarget.StandaloneLinux64, graphicsApis, "Linux/Fieldrunner.x86_64");
        }

        /// <summary>Builds the Windows player.</summary>
        [MenuItem("Fieldrunner/Build Windows Player")]
        public static void BuildWindows()
        {
            GraphicsDeviceType[] graphicsApis = {
                GraphicsDeviceType.Direct3D11,
                GraphicsDeviceType.Direct3D12,
                GraphicsDeviceType.Vulkan,
            };
            Build(BuildTarget.StandaloneWindows64, graphicsApis, "Windows/Fieldrunner.exe");
        }

        /// <summary>
        /// Builds the macOS player as a universal app (Intel and Apple Silicon); needs the editor's Mac Build Support
        /// (Mono) module.
        /// </summary>
        [MenuItem("Fieldrunner/Build Mac Player")]
        public static void BuildMac()
        {
            string macTargetName = BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneOSX);
            EditorUserBuildSettings.SetPlatformSettings(macTargetName, "Architecture", "x64ARM64");
            GraphicsDeviceType[] graphicsApis = { GraphicsDeviceType.Metal };
            Build(BuildTarget.StandaloneOSX, graphicsApis, "Mac/Fieldrunner.app");
        }

        /// <summary>Builds every desktop player we ship.</summary>
        public static void BuildAll()
        {
            BuildLinux();
            BuildWindows();
            BuildMac();
        }

        /// <summary>Regenerates the scene and builds one player; exits with code 1 on failure in batch mode.</summary>
        /// <param name="target">Platform to build.</param>
        /// <param name="graphicsApis">Graphics APIs the player tries at startup, most preferred first.</param>
        /// <param name="defaultRelativePath">Output path under unity/Builds when -buildOutput is not given.</param>
        private static void Build(BuildTarget target, GraphicsDeviceType[] graphicsApis, string defaultRelativePath)
        {
            SceneBuilder.BuildScene();

            // graphics APIs, most preferred first
            PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
            PlayerSettings.SetGraphicsAPIs(target, graphicsApis);

            // output path: -buildOutput, else unity/Builds/<platform>/...
            DirectoryInfo projectDirInfo = Directory.GetParent(Application.dataPath);
            string projectDir = projectDirInfo.FullName;
            string outputPath = Path.Combine(projectDir, "..", "Builds", defaultRelativePath);
            string overridePath = Util.CommandLine.GetValue("-buildOutput");
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
    }
}
