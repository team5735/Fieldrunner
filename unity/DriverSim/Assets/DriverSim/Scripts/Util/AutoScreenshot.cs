using System.Collections;
using System.Globalization;
using UnityEngine;

namespace DriverSim.Util
{
    /// <summary>
    /// Saves screenshots on a schedule and optionally quits, for checking builds without a person watching:
    /// <c>--screenshot &lt;path.png&gt; [--screenshot-delay &lt;s&gt;] [--screenshot-count &lt;n&gt;]
    /// [--screenshot-interval &lt;s&gt;] [--quit-after-screenshot]</c>. With a count above 1, files are numbered
    /// (path_0.png, path_1.png, ...).
    /// </summary>
    public sealed class AutoScreenshot : MonoBehaviour
    {
        private IEnumerator Start()
        {
            string path = CommandLine.GetValue("--screenshot");
            if (string.IsNullOrEmpty(path))
            {
                yield break;
            }
            float delayS = ReadFloat("--screenshot-delay", 5f);
            int count = Mathf.Max(1, (int)ReadFloat("--screenshot-count", 1f));
            float intervalS = ReadFloat("--screenshot-interval", 1f);

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
                if (CommandLine.HasFlag("--screenshot-camera-too"))
                {
                    SaveCameraRender(file.Replace(".png", "_camera.png"));
                }
                if (i < count - 1)
                {
                    yield return new WaitForSecondsRealtime(intervalS);
                }
            }

            if (CommandLine.HasFlag("--quit-after-screenshot"))
            {
                yield return new WaitForSecondsRealtime(1f); // CaptureScreenshot writes asynchronously
                Application.Quit();
            }
        }

        /// <summary>
        /// Renders the main camera into an sRGB texture and saves it, bypassing the window's backbuffer (no HUD). Used to
        /// tell rendering problems apart from backbuffer/display color problems.
        /// </summary>
        private static void SaveCameraRender(string file)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }
            RenderTexture target = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            RenderTexture previousTarget = cam.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            cam.targetTexture = target;
            cam.Render();
            RenderTexture.active = target;
            Texture2D image = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false, false);
            image.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            image.Apply();
            cam.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            System.IO.File.WriteAllBytes(file, image.EncodeToPNG());
            Destroy(image);
            target.Release();
            Debug.Log("[Driver-Sim] camera render " + file);
        }

        private static float ReadFloat(string flag, float fallback)
        {
            string text = CommandLine.GetValue(flag);
            if (text != null && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                return value;
            }
            return fallback;
        }
    }
}
