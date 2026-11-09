using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace DriverSim.Util
{
    /// <summary>
    /// Saves screenshots on a schedule and optionally quits, for checking builds without a person watching:
    /// <c>--screenshot &lt;path.png&gt; [--screenshot-delay &lt;s&gt;] [--screenshot-count &lt;n&gt;]
    /// [--screenshot-interval &lt;s&gt;] [--screenshot-camera-too] [--quit-after-screenshot]</c>. With a count above 1,
    /// files are numbered (path_0.png, path_1.png, ...). <c>--screenshot-camera-too</c> also saves the camera rendered
    /// straight to a texture (path_N_camera.png, no HUD), to tell rendering problems from window/display problems.
    /// </summary>
    public sealed class AutoScreenshot : MonoBehaviour
    {
        private const float DefaultDelayS = 5f; // seconds before the first screenshot
        private const float DefaultIntervalS = 1f; // seconds between screenshots
        private const float WriteWaitS = 1f; // seconds to let the last screenshot finish writing before quitting

        // ---- Unity messages

        private IEnumerator Start()
        {
            string path = CommandLine.GetValue("--screenshot");
            if (string.IsNullOrEmpty(path))
            {
                yield break;
            }

            // schedule from the command line
            float delayS = GetFloatArgument("--screenshot-delay", DefaultDelayS);
            float requestedCount = GetFloatArgument("--screenshot-count", 1f);
            int count = Mathf.Max(1, (int)requestedCount);
            float intervalS = GetFloatArgument("--screenshot-interval", DefaultIntervalS);
            bool isCameraRenderWanted = CommandLine.HasFlag("--screenshot-camera-too");

            // take the screenshots
            yield return new WaitForSecondsRealtime(delayS);
            for (int i = 0; i < count; i++)
            {
                string file = path;
                if (count > 1)
                {
                    file = path.Replace(".png", "_" + i + ".png");
                }
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(file);
                Debug.Log("[Driver-Sim] screenshot " + file);
                if (isCameraRenderWanted)
                {
                    string cameraFile = file.Replace(".png", "_camera.png");
                    SaveCameraRender(cameraFile);
                }
                if (i < count - 1)
                {
                    yield return new WaitForSecondsRealtime(intervalS);
                }
            }

            // optionally quit
            if (CommandLine.HasFlag("--quit-after-screenshot"))
            {
                yield return new WaitForSecondsRealtime(WriteWaitS); // CaptureScreenshot writes asynchronously
                Application.Quit();
            }
        }

        // ---- low-level helpers

        /// <summary>
        /// Renders the main camera into an sRGB texture and saves it as a PNG, bypassing the window's backbuffer (no
        /// HUD). Leaves the camera and render target as it found them.
        /// </summary>
        /// <param name="file">PNG path to write.</param>
        private static void SaveCameraRender(string file)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            // render into an offscreen texture
            int width = Screen.width;
            int height = Screen.height;
            RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            RenderTexture previousTarget = cam.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            cam.targetTexture = target;
            cam.Render();

            // read it back and save it
            RenderTexture.active = target;
            Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            byte[] png = image.EncodeToPNG();
            File.WriteAllBytes(file, png);

            // restore and clean up
            cam.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            Destroy(image);
            target.Release();
            Debug.Log("[Driver-Sim] camera render " + file);
        }

        /// <summary>Number following a command-line flag.</summary>
        /// <param name="flag">Flag, e.g. "--screenshot-delay".</param>
        /// <param name="fallback">Value when the flag is absent or not a number.</param>
        private static float GetFloatArgument(string flag, float fallback)
        {
            string text = CommandLine.GetValue(flag);
            if (text == null)
            {
                return fallback;
            }
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                return value;
            }
            return fallback;
        }
    }
}
