using System.Collections.Generic;

namespace DriverSim.Game
{
    /// <summary>One row of the robot's controls: what a virtual control does and its default inputs.</summary>
    public readonly struct ControlEntry
    {
        /// <summary>Creates an entry from its values; see the properties for meanings.</summary>
        public ControlEntry(
            VirtualControl control,
            string actionText,
            string gamepadPath,
            string keyboardPath,
            string keyboardText)
        {
            Control = control;
            ActionText = actionText;
            GamepadPath = gamepadPath;
            KeyboardPath = keyboardPath;
            KeyboardText = keyboardText;
        }

        /// <summary>The virtual Xbox control the robot code reads.</summary>
        public VirtualControl Control { get; }

        /// <summary>What the robot does with it, e.g. "slow mode (hold)".</summary>
        public string ActionText { get; }

        /// <summary>Default controller binding, an Input System path such as "&lt;Gamepad&gt;/buttonSouth".</summary>
        public string GamepadPath { get; }

        /// <summary>Default keyboard binding path; empty for the left stick (its keys are a W A S D
        /// composite).</summary>
        public string KeyboardPath { get; }

        /// <summary>Default keyboard keys as shown to the player, e.g. "Left Alt"; empty when there is none.</summary>
        public string KeyboardText { get; }
    }

    /// <summary>
    /// The robot's driver controls as the Controls screen shows them: a table mirroring
    /// <c>Robot.setupDriverBindings()</c> and the default drive command in
    /// <c>FRC-2026/src/main/java/frc/robot/Robot.java</c> (the non-demo bindings). Update it whenever the robot code's
    /// driver bindings change. The same table gives the default bindings DriverControls.inputactions is generated from.
    /// </summary>
    public static class RobotControlsLayout
    {
        public const string UnusedText =
            "Unused: right stick, stick presses, D-pad right"; // controls the robot ignores
        public const string PauseHint = "Pause: hold Start, then Back"; // how to pause with a controller

        private static readonly ControlEntry[] Entries =
        {
            new ControlEntry(VirtualControl.LeftStick, "drive (field-centric, 3 m/s)",
                "<Gamepad>/leftStick", "", "W A S D"),
            new ControlEntry(VirtualControl.LeftTrigger, "rotate left",
                "<Gamepad>/leftTrigger", "<Keyboard>/q", "Q"),
            new ControlEntry(VirtualControl.RightTrigger, "rotate right",
                "<Gamepad>/rightTrigger", "<Keyboard>/e", "E"),
            new ControlEntry(VirtualControl.ButtonA, "face hub (hold)",
                "<Gamepad>/buttonSouth", "<Keyboard>/r", "R"),
            new ControlEntry(VirtualControl.ButtonB, "launch: score (hold)",
                "<Gamepad>/buttonEast", "<Keyboard>/space", "Space"),
            new ControlEntry(VirtualControl.ButtonX, "launch: ferry + intake (hold)",
                "<Gamepad>/buttonWest", "<Keyboard>/f", "F"),
            new ControlEntry(VirtualControl.ButtonY, "slow mode (hold)",
                "<Gamepad>/buttonNorth", "<Keyboard>/c", "C"),
            new ControlEntry(VirtualControl.Start, "turbo, 5 m/s (hold)",
                "<Gamepad>/start", "<Keyboard>/leftAlt", "Left Alt"),
            new ControlEntry(VirtualControl.Back, "reset field-centric heading",
                "<Gamepad>/select", "<Keyboard>/tab", "Tab"),
            new ControlEntry(VirtualControl.RightBumper, "intake in (hold)",
                "<Gamepad>/rightShoulder", "<Keyboard>/leftShift", "Left Shift"),
            new ControlEntry(VirtualControl.LeftBumper, "intake out (hold)",
                "<Gamepad>/leftShoulder", "<Keyboard>/leftCtrl", "Left Ctrl"),
            new ControlEntry(VirtualControl.DpadDown, "intake slapdown (hold)",
                "<Gamepad>/dpad/down", "<Keyboard>/downArrow", "Down arrow"),
            new ControlEntry(VirtualControl.DpadUp, "intake lift (hold)",
                "<Gamepad>/dpad/up", "<Keyboard>/upArrow", "Up arrow"),
            new ControlEntry(VirtualControl.DpadLeft, "spindex backwards (hold)",
                "<Gamepad>/dpad/left", "<Keyboard>/leftArrow", "Left arrow"),
        }; // used controls, in the order the Controls screen lists them

        private static readonly ControlEntry[] UnusedEntries =
        {
            new ControlEntry(VirtualControl.RightStick, "",
                "<Gamepad>/rightStick", "", ""),
            new ControlEntry(VirtualControl.LeftStickPress, "",
                "<Gamepad>/leftStickPress", "", ""),
            new ControlEntry(VirtualControl.RightStickPress, "",
                "<Gamepad>/rightStickPress", "", ""),
            new ControlEntry(VirtualControl.DpadRight, "",
                "<Gamepad>/dpad/right", "", ""),
        }; // controls the robot code ignores; they still have default controller bindings

        /// <summary>The controls the robot uses, in listing order.</summary>
        public static IReadOnlyList<ControlEntry> GetUsedEntries()
        {
            return Entries;
        }

        /// <summary>The controls the robot ignores.</summary>
        public static IReadOnlyList<ControlEntry> GetUnusedEntries()
        {
            return UnusedEntries;
        }

        /// <summary>The entry for a control, whether used or not.</summary>
        /// <param name="control">Virtual control.</param>
        public static ControlEntry GetEntry(VirtualControl control)
        {
            foreach (ControlEntry entry in Entries)
            {
                if (entry.Control == control)
                {
                    return entry;
                }
            }
            foreach (ControlEntry entry in UnusedEntries)
            {
                if (entry.Control == control)
                {
                    return entry;
                }
            }
            return Entries[0];
        }

        /// <summary>Whether the robot does anything with a control.</summary>
        /// <param name="control">Virtual control.</param>
        public static bool IsUsed(VirtualControl control)
        {
            foreach (ControlEntry entry in Entries)
            {
                if (entry.Control == control)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
