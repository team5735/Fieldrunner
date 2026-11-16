using System;
using UnityEngine;

namespace DriverSim.Util
{
    /// <summary>
    /// The graphics quality levels (<c>Low</c> and <c>High</c>) and how the starting level is chosen: the
    /// <c>--quality low|high</c> command-line flag, else the level saved by the Options screen, else Low on a weak GPU
    /// and High otherwise.
    /// </summary>
    public static class GraphicsQuality
    {
        /// <summary>PlayerPrefs key of the saved quality level index.</summary>
        public const string SavedLevelKey = "DriverSim.quality";

        /// <summary>Index of the Low quality level.</summary>
        public const int LowLevel = 0;

        /// <summary>Index of the High quality level.</summary>
        public const int HighLevel = 1;

        private const int MinGraphicsMemoryMb = 2048; // GPUs with less memory count as weak

        // GPU name parts (lower case) that mark software renderers and mobile or integrated GPUs as weak
        private static readonly string[] WeakGpuNames =
        {
            "llvmpipe", "lavapipe", "softpipe", "swiftshader", "virgl", "venus", "mali", "adreno", "powervr"
        };

        /// <summary>
        /// Picks the starting quality level, applies it and returns where the choice came from. In batch mode only the
        /// command line counts and the project's default level stays otherwise.
        /// </summary>
        /// <returns><c>command line</c>, <c>saved</c>, <c>auto</c> or <c>default</c>.</returns>
        public static string ApplyStartupLevel()
        {
            string[] names = QualitySettings.names;
            int level = FindLevel(names, CommandLine.GetValue("--quality"));
            if (level >= 0)
            {
                QualitySettings.SetQualityLevel(level, true);
                return "command line";
            }
            if (Application.isBatchMode)
            {
                return "default";
            }
            if (PlayerPrefs.HasKey(SavedLevelKey))
            {
                int savedLevel = PlayerPrefs.GetInt(SavedLevelKey);
                savedLevel = Mathf.Clamp(savedLevel, 0, names.Length - 1);
                QualitySettings.SetQualityLevel(savedLevel, true);
                return "saved";
            }
            string deviceName = SystemInfo.graphicsDeviceName;
            int memoryMb = SystemInfo.graphicsMemorySize;
            int autoLevel = HighLevel;
            if (IsWeakGpu(deviceName, memoryMb))
            {
                autoLevel = LowLevel;
            }
            QualitySettings.SetQualityLevel(autoLevel, true);
            return "auto";
        }

        /// <summary>Whether a GPU looks too weak for the High level.</summary>
        /// <param name="deviceName">The graphics device name.</param>
        /// <param name="memoryMb">Graphics memory, megabytes.</param>
        public static bool IsWeakGpu(string deviceName, int memoryMb)
        {
            string lowerName = deviceName.ToLowerInvariant();
            foreach (string weakName in WeakGpuNames)
            {
                if (lowerName.Contains(weakName))
                {
                    return true;
                }
            }
            bool isIntel = lowerName.Contains("intel");
            bool isArc = lowerName.Contains("arc");
            if (isIntel && !isArc)
            {
                return true;
            }
            return memoryMb < MinGraphicsMemoryMb;
        }

        // ---- private static pure helpers

        /// <summary>Index of the level with the given name, ignoring case.</summary>
        /// <param name="names">Quality level names.</param>
        /// <param name="wanted">Name to find; may be null.</param>
        /// <returns>The index, or -1 when there is no such level.</returns>
        private static int FindLevel(string[] names, string wanted)
        {
            if (string.IsNullOrEmpty(wanted))
            {
                return -1;
            }
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
