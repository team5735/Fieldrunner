using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace DriverSim.Util
{
    /// <summary>
    /// Saves screenshots on a schedule and optionally quits, for checking builds without a person watching:
    /// <c>--screenshot &lt;path.jpg&gt; [--screenshot-delay &lt;s&gt;] [--screenshot-count &lt;n&gt;]
    /// [--screenshot-interval &lt;s&gt;] [--screenshot-camera-too] [--quit-after-screenshot]</c>. The extension picks
    /// the format: <c>.jpg</c> / <c>.jpeg</c> save JPEG (the project's screenshot format, much smaller), anything else
    /// PNG. With a count above 1, files are numbered (path_0.jpg, path_1.jpg, ...). <c>--screenshot-camera-too</c> also
    /// saves the camera rendered straight to a texture (path_N_camera.jpg, no HUD or menus), to tell rendering problems
    /// from window/display problems.
    /// </summary>
    public sealed class AutoScreenshot : MonoBehaviour
    {
        private const float DefaultDelayS = 5f; // seconds before the first screenshot
        private const float DefaultIntervalS = 1f; // seconds between screenshots
        private const float WriteWaitS = 1f; // seconds to let the log and the last file settle before quitting
        private const int JpegQuality = 90; // JPEG quality, 1..100; keeps menu text crisp

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
                    string numberSuffix = "_" + i;
                    file = MakeSuffixedPath(path, numberSuffix);
                }

                // the window as the player sees it, menus and overlay included
                yield return new WaitForEndOfFrame();
                Texture2D screen = ScreenCapture.CaptureScreenshotAsTexture();
                SaveImage(screen, file);
                Destroy(screen);
                Debug.Log("[Driver-Sim] screenshot " + file);
                if (isCameraRenderWanted)
                {
                    string cameraFile = MakeSuffixedPath(file, "_camera");
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
                yield return new WaitForSecondsRealtime(WriteWaitS);
                Application.Quit();
            }
        }

        // ---- low-level helpers

        /// <summary>
        /// Renders the main camera into an sRGB texture and saves it, bypassing the window's backbuffer (no HUD or
        /// menus). Leaves the camera and render target as it found them.
        /// </summary>
        /// <param name="file">Image path to write; its extension picks the format.</param>
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
            SaveImage(image, file);

            // restore and clean up
            cam.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            Destroy(image);
            target.Release();
            Debug.Log("[Driver-Sim] camera render " + file);
        }

        /// <summary>Writes an image to a file as JPEG for a .jpg / .jpeg path, PNG otherwise.</summary>
        /// <param name="image">Readable texture to save.</param>
        /// <param name="file">Path to write.</param>
        private static void SaveImage(Texture2D image, string file)
        {
            string extension = Path.GetExtension(file);
            string lowerExtension = extension.ToLowerInvariant();
            byte[] bytes;
            if (lowerExtension == ".jpg" || lowerExtension == ".jpeg")
            {
                bytes = image.EncodeToJPG(JpegQuality);
            }
            else
            {
                bytes = image.EncodeToPNG();
            }
            File.WriteAllBytes(file, bytes);
        }

        /// <summary>Inserts a suffix before a path's extension, e.g. "shot.jpg" + "_0" gives "shot_0.jpg".</summary>
        /// <param name="path">Original path.</param>
        /// <param name="suffix">Text to insert.</param>
        /// <returns>The path with the suffix before its extension.</returns>
        private static string MakeSuffixedPath(string path, string suffix)
        {
            string extension = Path.GetExtension(path);
            string pathWithoutExtension = path.Substring(0, path.Length - extension.Length);
            return pathWithoutExtension + suffix + extension;
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
