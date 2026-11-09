using System.Collections.Generic;
using UnityEngine.UIElements;

namespace DriverSim.Game
{
    /// <summary>
    /// The Options screen: camera view, field of view, details panel, colliders, fullscreen, resolution and graphics
    /// quality. Each control applies and saves its setting through <see cref="GameOptions"/> as soon as it changes.
    /// </summary>
    public sealed class OptionsView
    {
        private const string DriverStationViewText = "Driver station"; // choice for the driver-station view
        private const string OverheadViewText = "Overhead"; // choice for the overhead view

        private readonly GameOptions options; // settings applied and saved
        private readonly DropdownField viewDropdown; // camera view
        private readonly SliderInt fieldOfViewSlider; // vertical field of view, degrees
        private readonly Toggle detailsToggle; // details panel
        private readonly Toggle collidersToggle; // collider overlay
        private readonly Toggle fullscreenToggle; // fullscreen
        private readonly DropdownField resolutionDropdown; // resolution
        private readonly DropdownField qualityDropdown; // graphics quality

        /// <summary>Wires the screen's controls.</summary>
        /// <param name="screen">The Options screen element.</param>
        /// <param name="gameOptions">Settings the controls change.</param>
        public OptionsView(VisualElement screen, GameOptions gameOptions)
        {
            options = gameOptions;
            viewDropdown = screen.Q<DropdownField>("opt-view");
            fieldOfViewSlider = screen.Q<SliderInt>("opt-fov");
            detailsToggle = screen.Q<Toggle>("opt-details");
            collidersToggle = screen.Q<Toggle>("opt-colliders");
            fullscreenToggle = screen.Q<Toggle>("opt-fullscreen");
            resolutionDropdown = screen.Q<DropdownField>("opt-resolution");
            qualityDropdown = screen.Q<DropdownField>("opt-quality");

            // choices
            viewDropdown.choices = new List<string> { DriverStationViewText, OverheadViewText };
            qualityDropdown.choices = new List<string>(options.GetQualityNames());

            // changes apply at once
            viewDropdown.RegisterValueChangedCallback(HandleViewChanged);
            fieldOfViewSlider.RegisterValueChangedCallback(HandleFieldOfViewChanged);
            detailsToggle.RegisterValueChangedCallback(HandleDetailsChanged);
            collidersToggle.RegisterValueChangedCallback(HandleCollidersChanged);
            fullscreenToggle.RegisterValueChangedCallback(HandleFullscreenChanged);
            resolutionDropdown.RegisterValueChangedCallback(HandleResolutionChanged);
            qualityDropdown.RegisterValueChangedCallback(HandleQualityChanged);
        }

        /// <summary>The first control, for focus when the screen opens.</summary>
        public VisualElement GetFirstFocusable()
        {
            return viewDropdown;
        }

        /// <summary>Shows the current settings in the controls, without applying anything.</summary>
        public void Refresh()
        {
            string viewText = DriverStationViewText;
            if (options.GetView() == DriverStationCamera.View.Overhead)
            {
                viewText = OverheadViewText;
            }
            viewDropdown.SetValueWithoutNotify(viewText);
            float fieldOfViewDeg = options.GetFieldOfViewDeg();
            bool isDetailsShown = options.IsDetailsShown();
            bool areCollidersShown = options.AreCollidersShown();
            bool isFullscreen = options.IsFullscreen();
            fieldOfViewSlider.SetValueWithoutNotify((int)fieldOfViewDeg);
            detailsToggle.SetValueWithoutNotify(isDetailsShown);
            collidersToggle.SetValueWithoutNotify(areCollidersShown);
            fullscreenToggle.SetValueWithoutNotify(isFullscreen);

            // resolution: the display's choices, with the current size added when it is not one of them
            List<string> resolutions = options.GetResolutionChoices();
            string currentResolution = options.GetResolutionText();
            if (!resolutions.Contains(currentResolution))
            {
                resolutions.Add(currentResolution);
            }
            resolutionDropdown.choices = resolutions;
            resolutionDropdown.SetValueWithoutNotify(currentResolution);

            // quality
            string[] qualityNames = options.GetQualityNames();
            int level = options.GetQualityLevel();
            qualityDropdown.SetValueWithoutNotify(qualityNames[level]);
        }

        // ---- private stateful helpers

        /// <summary>Applies the chosen camera view.</summary>
        private void HandleViewChanged(ChangeEvent<string> change)
        {
            DriverStationCamera.View view = DriverStationCamera.View.DriverStation;
            if (change.newValue == OverheadViewText)
            {
                view = DriverStationCamera.View.Overhead;
            }
            options.SetView(view);
        }

        /// <summary>Applies the chosen field of view.</summary>
        private void HandleFieldOfViewChanged(ChangeEvent<int> change)
        {
            options.SetFieldOfViewDeg(change.newValue);
        }

        /// <summary>Shows or hides the details panel.</summary>
        private void HandleDetailsChanged(ChangeEvent<bool> change)
        {
            options.SetDetailsShown(change.newValue);
        }

        /// <summary>Shows or hides the collider overlay.</summary>
        private void HandleCollidersChanged(ChangeEvent<bool> change)
        {
            options.SetCollidersShown(change.newValue);
        }

        /// <summary>Switches between fullscreen and a window.</summary>
        private void HandleFullscreenChanged(ChangeEvent<bool> change)
        {
            options.SetFullscreen(change.newValue);
        }

        /// <summary>Applies the chosen resolution ("1920 x 1080").</summary>
        private void HandleResolutionChanged(ChangeEvent<string> change)
        {
            string[] parts = change.newValue.Split('x');
            if (parts.Length != 2)
            {
                return;
            }
            string widthText = parts[0].Trim();
            string heightText = parts[1].Trim();
            bool isWidthValid = int.TryParse(widthText, out int widthPx);
            bool isHeightValid = int.TryParse(heightText, out int heightPx);
            if (isWidthValid && isHeightValid)
            {
                options.SetResolution(widthPx, heightPx);
            }
        }

        /// <summary>Applies the chosen graphics quality level.</summary>
        private void HandleQualityChanged(ChangeEvent<string> change)
        {
            string[] names = options.GetQualityNames();
            int level = System.Array.IndexOf(names, change.newValue);
            if (level >= 0)
            {
                options.SetQualityLevel(level);
            }
        }
    }
}
