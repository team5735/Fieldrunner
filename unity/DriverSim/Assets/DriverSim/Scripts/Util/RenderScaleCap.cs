using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DriverSim.Util
{
    /// <summary>
    /// Keeps the rendered pixel count near 1080p's however big the window is: when the window has more pixels than
    /// <see cref="MaxRenderPixels"/>, URP renders at a lower resolution and upscales (AMD FSR 1) to fill it. Logs the
    /// render size whenever the window size changes.
    /// </summary>
    public sealed class RenderScaleCap : MonoBehaviour
    {
        private const int MaxRenderPixels = 1920 * 1080; // pixels rendered per frame at most
        private const float MinRenderScale = 0.5f; // never render below half the window resolution per axis

        private UniversalRenderPipelineAsset pipeline; // the active URP asset; null when URP isn't active
        private float originalRenderScale; // the asset's render scale before this component changed it
        private UpscalingFilterSelection originalFilter; // the asset's upscaling filter before this component
        private int lastWidth; // window width the scale was computed for, pixels
        private int lastHeight; // window height the scale was computed for, pixels

        /// <summary>Render scale currently applied per axis.</summary>
        /// <returns>Fraction of the window resolution rendered, 0.5..1; 1 when URP isn't active.</returns>
        public float GetRenderScale()
        {
            if (pipeline == null)
            {
                return 1f;
            }
            return pipeline.renderScale;
        }

        // ---- Unity messages

        private void OnEnable()
        {
            pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null)
            {
                Debug.LogWarning("[Driver-Sim] render scale cap: URP is not the active pipeline");
                return;
            }
            originalRenderScale = pipeline.renderScale;
            originalFilter = pipeline.upscalingFilter;
            pipeline.upscalingFilter = UpscalingFilterSelection.FSR;
            lastWidth = 0;
            lastHeight = 0;
        }

        private void Update()
        {
            // only when the window size changed
            if (pipeline == null)
            {
                return;
            }
            int width = Screen.width;
            int height = Screen.height;
            if (width == lastWidth && height == lastHeight)
            {
                return;
            }
            lastWidth = width;
            lastHeight = height;

            // scale per axis so the rendered pixel count stays at or below the cap
            float scale = ComputeRenderScale(width, height);
            pipeline.renderScale = scale;
            int renderWidth = Mathf.RoundToInt(width * scale);
            int renderHeight = Mathf.RoundToInt(height * scale);
            Debug.Log("[Driver-Sim] render " + renderWidth + "x" + renderHeight + " for a " + width + "x" + height
                      + " window (scale " + scale.ToString("F2") + ")");
        }

        private void OnDisable()
        {
            // the asset is shared with the editor; put it back
            if (pipeline == null)
            {
                return;
            }
            pipeline.renderScale = originalRenderScale;
            pipeline.upscalingFilter = originalFilter;
        }

        // ---- private static pure helpers

        /// <summary>Render scale per axis that keeps a window's rendered pixel count within the cap.</summary>
        /// <param name="width">Window width, pixels.</param>
        /// <param name="height">Window height, pixels.</param>
        /// <returns>Scale per axis, <see cref="MinRenderScale"/>..1.</returns>
        private static float ComputeRenderScale(int width, int height)
        {
            float pixels = (float)width * height;
            if (pixels <= MaxRenderPixels)
            {
                return 1f;
            }
            float scale = Mathf.Sqrt(MaxRenderPixels / pixels);
            return Mathf.Max(scale, MinRenderScale);
        }
    }
}
