using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.Switch;
using UnityEngine.InputSystem.XInput;

namespace DriverSim.Game
{
    /// <summary>Which naming the player knows their controller's buttons by.</summary>
    public enum ControllerFamily
    {
        Xbox,
        PlayStation,
        Generic,
    }

    /// <summary>Names for controller inputs that follow the connected controller (A/B/X/Y, Cross/Circle/..., or
    /// generic).</summary>
    public static class ControlLabels
    {
        private const string GamepadPrefix = "<Gamepad>/"; // start of every gamepad control path

        /// <summary>The naming family of a device.</summary>
        /// <param name="device">Active controller; null (keyboard play) counts as Xbox, the robot's own layout.</param>
        public static ControllerFamily GetFamily(InputDevice device)
        {
            if (device == null)
            {
                return ControllerFamily.Xbox;
            }
            if (device is XInputController)
            {
                return ControllerFamily.Xbox;
            }
            if (device is DualShockGamepad)
            {
                return ControllerFamily.PlayStation;
            }

            // a gamepad without a dedicated type (on Linux an Xbox pad is a plain gamepad): go by its name
            if (device is Gamepad)
            {
                string familyName = GetGamepadFamilyName(device.displayName);
                if (familyName == "Xbox")
                {
                    return ControllerFamily.Xbox;
                }
                if (familyName == "PlayStation")
                {
                    return ControllerFamily.PlayStation;
                }
            }
            return ControllerFamily.Generic;
        }

        /// <summary>The name shown for a device: its display name, or "Keyboard".</summary>
        /// <param name="device">Any input device; null counts as the keyboard.</param>
        public static string GetDeviceName(InputDevice device)
        {
            if (device == null || device is Keyboard)
            {
                return "Keyboard";
            }
            return device.displayName;
        }

        /// <summary>The kind of device as shown on a Controllers card: Xbox, PlayStation, Switch, Gamepad, Joystick
        /// or Keyboard.</summary>
        /// <param name="device">Any input device; null counts as the keyboard.</param>
        public static string GetFamilyName(InputDevice device)
        {
            if (device == null || device is Keyboard)
            {
                return "Keyboard";
            }
            if (device is XInputController)
            {
                return "Xbox";
            }
            if (device is DualShockGamepad)
            {
                return "PlayStation";
            }
            if (device is SwitchProControllerHID)
            {
                return "Switch";
            }
            if (device is Gamepad)
            {
                return GetGamepadFamilyName(device.displayName);
            }
            return "Joystick";
        }

        /// <summary>
        /// The player-facing name of an input path: gamepad paths use the family's names, everything else the Input
        /// System's own readable form without the device ("Left Alt", "Space").
        /// </summary>
        /// <param name="path">Input System path such as "&lt;Gamepad&gt;/buttonSouth" or
        /// "&lt;Keyboard&gt;/space".</param>
        /// <param name="family">Naming family for gamepad paths.</param>
        public static string GetPathName(string path, ControllerFamily family)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "(none)";
            }
            if (path.StartsWith(GamepadPrefix))
            {
                string controlName = path.Substring(GamepadPrefix.Length);
                string name = GetGamepadControlName(controlName, family);
                if (name != null)
                {
                    return name;
                }
            }
            return InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
        }

        // ---- low-level helpers (static, no side effects)

        /// <summary>The family of a gamepad the Input System has no dedicated type for (on Linux an Xbox pad is a plain
        /// gamepad), guessed from its display name.</summary>
        private static string GetGamepadFamilyName(string displayName)
        {
            string lowerName = displayName.ToLowerInvariant();
            if (lowerName.Contains("xbox"))
            {
                return "Xbox";
            }
            if (lowerName.Contains("dualsense") || lowerName.Contains("dualshock") || lowerName.Contains("playstation"))
            {
                return "PlayStation";
            }
            if (lowerName.Contains("nintendo") || lowerName.Contains("pro controller"))
            {
                return "Switch";
            }
            return "Gamepad";
        }

        /// <summary>Name of a gamepad control for a family, or null when the control is not one of the known
        /// ones.</summary>
        private static string GetGamepadControlName(string controlName, ControllerFamily family)
        {
            switch (controlName)
            {
                case "buttonSouth": return PickName(family, "A", "Cross", "South button");
                case "buttonEast": return PickName(family, "B", "Circle", "East button");
                case "buttonWest": return PickName(family, "X", "Square", "West button");
                case "buttonNorth": return PickName(family, "Y", "Triangle", "North button");
                case "leftShoulder": return PickName(family, "LB", "L1", "Left shoulder");
                case "rightShoulder": return PickName(family, "RB", "R1", "Right shoulder");
                case "leftTrigger": return PickName(family, "LT", "L2", "Left trigger");
                case "rightTrigger": return PickName(family, "RT", "R2", "Right trigger");
                case "select": return PickName(family, "Back", "Share", "Select");
                case "start": return PickName(family, "Start", "Options", "Start");
                case "leftStick": return "Left stick";
                case "rightStick": return "Right stick";
                case "leftStickPress": return "Left stick press";
                case "rightStickPress": return "Right stick press";
                case "dpad/up": return "D-pad up";
                case "dpad/down": return "D-pad down";
                case "dpad/left": return "D-pad left";
                case "dpad/right": return "D-pad right";
            }
            return null;
        }

        /// <summary>The name for one family.</summary>
        private static string PickName(
            ControllerFamily family, string xboxName, string playStationName, string genericName)
        {
            if (family == ControllerFamily.Xbox)
            {
                return xboxName;
            }
            if (family == ControllerFamily.PlayStation)
            {
                return playStationName;
            }
            return genericName;
        }
    }
}
