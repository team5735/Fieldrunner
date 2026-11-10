using System.Globalization;
using UnityEngine;

namespace DriverSim.Util
{
    /// <summary>
    /// Measures the frame rate and how much of each frame the main thread and the graphics card spend working, as an
    /// average over the last half second (for the HUD), and writes it to the player log every few seconds:
    /// <c>[Driver-Sim] frame rate: 60.0 fps, slowest frame 18.2 ms, cpu 1.8 ms (11%), gpu 4.0 ms (24%)</c>. The cpu
    /// and gpu figures are 0 when the platform doesn't report them (OpenGL reports no gpu time).
    /// </summary>
    public sealed class FrameRateLog : MonoBehaviour
    {
        private const float WindowS = 0.5f; // seconds averaged for each reading
        private const int WindowsPerLogLine = 10; // readings between log lines

        private readonly FrameTiming[] timings = new FrameTiming[1]; // latest frame timing from the engine
        private float windowS; // seconds counted in the current window
        private int frameCount; // frames counted in the current window
        private double cpuMsSum; // main thread work summed over the window, milliseconds
        private double gpuMsSum; // graphics card work summed over the window, milliseconds
        private int timingCount; // frames in the window with a timing sample
        private float slowestFrameS; // longest frame since the last log line, seconds
        private int windowsSinceLog; // readings since the last log line
        private float framesPerS; // frame rate of the last finished window
        private double cpuMs; // average main thread work per frame in the last finished window, milliseconds
        private double gpuMs; // average graphics card work per frame in the last finished window, milliseconds

        /// <summary>Frame rate averaged over the last half second.</summary>
        /// <returns>Frames per second; 0 before the first reading.</returns>
        public float GetFramesPerS()
        {
            return framesPerS;
        }

        /// <summary>Share of each frame the main thread spends working, averaged over the last half second.</summary>
        /// <returns>Percent of the frame time; 0 when not reported.</returns>
        public double GetCpuPercent()
        {
            return ComputePercentOfFrame(cpuMs, framesPerS);
        }

        /// <summary>Share of each frame the graphics card spends working, averaged over the last half second.</summary>
        /// <returns>Percent of the frame time; 0 when not reported.</returns>
        public double GetGpuPercent()
        {
            return ComputePercentOfFrame(gpuMs, framesPerS);
        }

        // ---- Unity messages

        private void Update()
        {
            // count this frame
            float frameS = Time.unscaledDeltaTime;
            windowS += frameS;
            frameCount++;
            if (frameS > slowestFrameS)
            {
                slowestFrameS = frameS;
            }
            FrameTimingManager.CaptureFrameTimings();
            uint sampleCount = FrameTimingManager.GetLatestTimings(1, timings);
            if (sampleCount > 0)
            {
                cpuMsSum += timings[0].cpuMainThreadFrameTime;
                gpuMsSum += timings[0].gpuFrameTime;
                timingCount++;
            }
            if (windowS < WindowS)
            {
                return;
            }

            // finish the reading
            framesPerS = frameCount / windowS;
            cpuMs = 0;
            gpuMs = 0;
            if (timingCount > 0)
            {
                cpuMs = cpuMsSum / timingCount;
                gpuMs = gpuMsSum / timingCount;
            }
            windowS = 0;
            frameCount = 0;
            cpuMsSum = 0;
            gpuMsSum = 0;
            timingCount = 0;

            // every few readings, a log line
            windowsSinceLog++;
            if (windowsSinceLog < WindowsPerLogLine)
            {
                return;
            }
            WriteLogLine();
            windowsSinceLog = 0;
            slowestFrameS = 0;
        }

        // ---- private stateful helpers

        /// <summary>Writes the last reading and the slowest frame since the previous line to the player log.</summary>
        private void WriteLogLine()
        {
            double cpuPercent = GetCpuPercent();
            double gpuPercent = GetGpuPercent();
            float slowestMs = slowestFrameS * 1000f;
            string line = string.Format(CultureInfo.InvariantCulture,
                "[Driver-Sim] frame rate: {0:F1} fps, slowest frame {1:F1} ms, cpu {2:F1} ms ({3:F0}%), "
                    + "gpu {4:F1} ms ({5:F0}%)",
                framesPerS, slowestMs, cpuMs, cpuPercent, gpuMs, gpuPercent);
            Debug.Log(line);
        }

        // ---- private static pure helpers

        /// <summary>Converts work per frame to a share of the frame time.</summary>
        /// <param name="workMs">Work per frame, milliseconds.</param>
        /// <param name="ratePerS">Frame rate, frames per second.</param>
        /// <returns>Percent of the frame time; 0 when the frame rate is unknown.</returns>
        private static double ComputePercentOfFrame(double workMs, float ratePerS)
        {
            if (ratePerS <= 0)
            {
                return 0;
            }
            double frameMs = 1000.0 / ratePerS;
            return 100.0 * workMs / frameMs;
        }
    }
}
