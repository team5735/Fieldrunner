using System.Collections.Generic;
using DriverSim.Util;
using UnityEngine;

namespace DriverSim.Game
{
    /// <summary>
    /// The player's display and camera settings: camera view, field of view, details panel, collider overlay,
    /// fullscreen, resolution and graphics quality. Each change applies at once and is saved in PlayerPrefs; the saved
    /// values are applied on the first frame. Command-line arguments win over saved values (<c>--view</c>,
    /// <c>--show-colliders</c>), and a window with no display (batch mode) keeps its screen settings.
    /// </summary>
    public sealed class GameOptions : MonoBehaviour
    {
        private const string ViewKey = "DriverSim.view"; // PlayerPrefs: DriverStationCamera.View as an int
        private const string FieldOfViewKey = "DriverSim.fieldOfViewDeg"; // PlayerPrefs: float, degrees
        private const string DetailsKey = "DriverSim.showDetails"; // PlayerPrefs: 1 shown, 0 hidden
        private const string CollidersKey = "DriverSim.showColliders"; // PlayerPrefs: 1 shown, 0 hidden
        private const string FullscreenKey = "DriverSim.fullscreen"; // PlayerPrefs: 1 fullscreen, 0 windowed
        private const string ResolutionWidthKey = "DriverSim.resolutionWidth"; // PlayerPrefs: pixels
        private const string ResolutionHeightKey = "DriverSim.resolutionHeight"; // PlayerPrefs: pixels
        private const string QualityKey = "DriverSim.quality"; // PlayerPrefs: QualitySettings level index
        private const float FieldOfViewStepDeg = 5f; // degrees per field-of-view hotkey press

        [Tooltip("Camera whose view and field of view are set.")]
        [SerializeField] private DriverStationCamera driverCamera;

        [Tooltip("Overlay whose details panel is shown or hidden.")]
        [SerializeField] private DebugHud hud;

        [Tooltip("Collider overlay to show or hide.")]
        [SerializeField] private ColliderDebugView colliderView;

        private bool hasAppliedSaved; // the saved values have been applied

        /// <summary>Sets the objects the options change; call before the scene starts.</summary>
        /// <param name="cameraRig">Driver camera.</param>
        /// <param name="debugHud">Debug overlay.</param>
        /// <param name="debugColliderView">Collider overlay.</param>
        public void SetReferences(DriverStationCamera cameraRig, DebugHud debugHud, ColliderDebugView debugColliderView)
        {
            driverCamera = cameraRig;
            hud = debugHud;
            colliderView = debugColliderView;
        }

        /// <summary>The camera view in use.</summary>
        public DriverStationCamera.View GetView()
        {
            return driverCamera.GetView();
        }

        /// <summary>Switches the camera view and remembers it.</summary>
        /// <param name="view">View to show.</param>
        public void SetView(DriverStationCamera.View view)
        {
            driverCamera.SetView(view);
            PlayerPrefs.SetInt(ViewKey, (int)view);
        }

        /// <summary>Switches to the other camera view and remembers it.</summary>
        public void CycleView()
        {
            DriverStationCamera.View next = DriverStationCamera.View.Overhead;
            if (driverCamera.GetView() == DriverStationCamera.View.Overhead)
            {
                next = DriverStationCamera.View.DriverStation;
            }
            SetView(next);
        }

        /// <summary>The driver's vertical field of view, degrees.</summary>
        public float GetFieldOfViewDeg()
        {
            return driverCamera.GetFieldOfViewDeg();
        }

        /// <summary>Sets the driver's vertical field of view and remembers it.</summary>
        /// <param name="fieldOfViewDeg">Vertical field of view, degrees.</param>
        public void SetFieldOfViewDeg(float fieldOfViewDeg)
        {
            driverCamera.SetFieldOfViewDeg(fieldOfViewDeg);
            float applied = driverCamera.GetFieldOfViewDeg();
            PlayerPrefs.SetFloat(FieldOfViewKey, applied);
        }

        /// <summary>Widens or narrows the field of view by one hotkey step and remembers it.</summary>
        /// <param name="direction">-1 to narrow, 1 to widen.</param>
        public void StepFieldOfView(int direction)
        {
            float current = driverCamera.GetFieldOfViewDeg();
            SetFieldOfViewDeg(current + direction * FieldOfViewStepDeg);
        }

        /// <summary>Whether the details panel is shown.</summary>
        public bool IsDetailsShown()
        {
            return hud.IsDetailVisible();
        }

        /// <summary>Shows or hides the details panel and remembers it.</summary>
        /// <param name="isShown">True to show.</param>
        public void SetDetailsShown(bool isShown)
        {
            hud.SetDetailVisible(isShown);
            int shownFlag = ConvertToInt(isShown);
            PlayerPrefs.SetInt(DetailsKey, shownFlag);
        }

        /// <summary>Whether the collider overlay is shown.</summary>
        public bool AreCollidersShown()
        {
            return colliderView.IsShown();
        }

        /// <summary>Shows or hides the collider overlay and remembers it.</summary>
        /// <param name="isShown">True to show.</param>
        public void SetCollidersShown(bool isShown)
        {
            colliderView.SetShown(isShown);
            int shownFlag = ConvertToInt(isShown);
            PlayerPrefs.SetInt(CollidersKey, shownFlag);
        }

        /// <summary>Whether the game runs fullscreen.</summary>
        public bool IsFullscreen()
        {
            return Screen.fullScreenMode != FullScreenMode.Windowed;
        }

        /// <summary>Switches between fullscreen and a window and remembers it.</summary>
        /// <param name="isFullscreen">True for fullscreen.</param>
        public void SetFullscreen(bool isFullscreen)
        {
            FullScreenMode mode = FullScreenMode.Windowed;
            if (isFullscreen)
            {
                mode = FullScreenMode.FullScreenWindow;
            }
            Screen.fullScreenMode = mode;
            int fullscreenFlag = ConvertToInt(isFullscreen);
            PlayerPrefs.SetInt(FullscreenKey, fullscreenFlag);
        }

        /// <summary>The resolutions the display offers, as "1920 x 1080" texts, without repeats from different refresh
        /// rates.</summary>
        public List<string> GetResolutionChoices()
        {
            List<string> choices = new List<string>();
            foreach (Resolution resolution in Screen.resolutions)
            {
                string text = MakeResolutionText(resolution.width, resolution.height);
                if (!choices.Contains(text))
                {
                    choices.Add(text);
                }
            }
            return choices;
        }

        /// <summary>The current window or screen size as a "1920 x 1080" text.</summary>
        public string GetResolutionText()
        {
            return MakeResolutionText(Screen.width, Screen.height);
        }

        /// <summary>Changes the resolution and remembers it.</summary>
        /// <param name="widthPx">Width, pixels.</param>
        /// <param name="heightPx">Height, pixels.</param>
        public void SetResolution(int widthPx, int heightPx)
        {
            Screen.SetResolution(widthPx, heightPx, Screen.fullScreenMode);
            PlayerPrefs.SetInt(ResolutionWidthKey, widthPx);
            PlayerPrefs.SetInt(ResolutionHeightKey, heightPx);
        }

        /// <summary>The names of the graphics quality levels.</summary>
        public string[] GetQualityNames()
        {
            return QualitySettings.names;
        }

        /// <summary>The index of the current graphics quality level.</summary>
        public int GetQualityLevel()
        {
            return QualitySettings.GetQualityLevel();
        }

        /// <summary>Sets the graphics quality level and remembers it.</summary>
        /// <param name="level">Index into <see cref="GetQualityNames"/>.</param>
        public void SetQualityLevel(int level)
        {
            QualitySettings.SetQualityLevel(level, true);
            PlayerPrefs.SetInt(QualityKey, level);
        }

        // ---- Unity messages

        private void Update()
        {
            // after every object's Start, so the camera and overlays exist
            if (!hasAppliedSaved)
            {
                hasAppliedSaved = true;
                ApplySaved();
            }
        }

        private void OnApplicationQuit()
        {
            PlayerPrefs.Save();
        }

        // ---- private stateful helpers

        /// <summary>Applies the saved settings, except where the command line already decided.</summary>
        private void ApplySaved()
        {
            // camera and overlays
            bool isViewFromCommandLine = CommandLine.GetValue("--view") != null;
            if (PlayerPrefs.HasKey(ViewKey) && !isViewFromCommandLine)
            {
                int savedView = PlayerPrefs.GetInt(ViewKey);
                driverCamera.SetView((DriverStationCamera.View)savedView);
            }
            if (PlayerPrefs.HasKey(FieldOfViewKey))
            {
                float savedFieldOfViewDeg = PlayerPrefs.GetFloat(FieldOfViewKey);
                driverCamera.SetFieldOfViewDeg(savedFieldOfViewDeg);
            }
            if (PlayerPrefs.HasKey(DetailsKey))
            {
                int savedDetails = PlayerPrefs.GetInt(DetailsKey);
                hud.SetDetailVisible(savedDetails != 0);
            }
            bool isCollidersFromCommandLine = CommandLine.HasFlag("--show-colliders");
            if (PlayerPrefs.HasKey(CollidersKey) && !isCollidersFromCommandLine)
            {
                int savedColliders = PlayerPrefs.GetInt(CollidersKey);
                colliderView.SetShown(savedColliders != 0);
            }

            // screen settings need a display
            if (Application.isBatchMode)
            {
                return;
            }
            if (PlayerPrefs.HasKey(QualityKey))
            {
                int savedQuality = PlayerPrefs.GetInt(QualityKey);
                QualitySettings.SetQualityLevel(savedQuality, true);
            }
            if (PlayerPrefs.HasKey(FullscreenKey))
            {
                int savedFullscreen = PlayerPrefs.GetInt(FullscreenKey);
                if (savedFullscreen != 0)
                {
                    Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
                }
            }
            if (PlayerPrefs.HasKey(ResolutionWidthKey) && PlayerPrefs.HasKey(ResolutionHeightKey))
            {
                int savedWidth = PlayerPrefs.GetInt(ResolutionWidthKey);
                int savedHeight = PlayerPrefs.GetInt(ResolutionHeightKey);
                Screen.SetResolution(savedWidth, savedHeight, Screen.fullScreenMode);
            }
        }

        // ---- low-level helpers (static, no side effects)

        /// <summary>1 for true, 0 for false.</summary>
        private static int ConvertToInt(bool value)
        {
            if (value)
            {
                return 1;
            }
            return 0;
        }

        /// <summary>A resolution as "1920 x 1080".</summary>
        private static string MakeResolutionText(int widthPx, int heightPx)
        {
            return widthPx + " x " + heightPx;
        }
    }
}
