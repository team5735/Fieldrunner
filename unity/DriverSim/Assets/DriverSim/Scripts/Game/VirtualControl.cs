namespace DriverSim.Game
{
    /// <summary>
    /// One control of the virtual Xbox controller the robot code reads on driver station port 0. Each has one input
    /// action of the same name in DriverControls.inputactions.
    /// </summary>
    public enum VirtualControl
    {
        LeftStick,
        RightStick,
        LeftTrigger,
        RightTrigger,
        ButtonA,
        ButtonB,
        ButtonX,
        ButtonY,
        LeftBumper,
        RightBumper,
        Back,
        Start,
        LeftStickPress,
        RightStickPress,
        DpadUp,
        DpadRight,
        DpadDown,
        DpadLeft,
    }

    /// <summary>Which kind of values a virtual control produces.</summary>
    public enum VirtualControlKind
    {
        Stick,
        Trigger,
        Button,
    }

    /// <summary>The two ways of driving: a controller, or the keyboard.</summary>
    public enum InputScheme
    {
        Keyboard,
        Controller,
    }

    /// <summary>Facts about the virtual controls that do not change.</summary>
    public static class VirtualControls
    {
        public const string ActionMapName = "Driver"; // action map in DriverControls.inputactions
        public const string ControllerGroup = "Controller"; // binding group and control scheme of controller bindings
        public const string KeyboardGroup = "Keyboard"; // binding group and control scheme of keyboard bindings
        public const int StartBit = 7; // WPILib Xbox button index of Start (bit in the button bits)
        public const int BackBit = 6; // WPILib Xbox button index of Back

        private static readonly VirtualControl[] AllControls =
        {
            VirtualControl.LeftStick, VirtualControl.RightStick, VirtualControl.LeftTrigger,
            VirtualControl.RightTrigger, VirtualControl.ButtonA, VirtualControl.ButtonB, VirtualControl.ButtonX,
            VirtualControl.ButtonY, VirtualControl.LeftBumper, VirtualControl.RightBumper, VirtualControl.Back,
            VirtualControl.Start, VirtualControl.LeftStickPress, VirtualControl.RightStickPress,
            VirtualControl.DpadUp, VirtualControl.DpadRight, VirtualControl.DpadDown, VirtualControl.DpadLeft,
        }; // every virtual control, in enum order

        /// <summary>Every virtual control, in declaration order.</summary>
        public static VirtualControl[] GetAll()
        {
            return AllControls;
        }

        /// <summary>The input action name of a control.</summary>
        /// <param name="control">Virtual control.</param>
        public static string GetActionName(VirtualControl control)
        {
            return control.ToString();
        }

        /// <summary>What kind of values a control produces.</summary>
        /// <param name="control">Virtual control.</param>
        public static VirtualControlKind GetKind(VirtualControl control)
        {
            if (control == VirtualControl.LeftStick || control == VirtualControl.RightStick)
            {
                return VirtualControlKind.Stick;
            }
            if (control == VirtualControl.LeftTrigger || control == VirtualControl.RightTrigger)
            {
                return VirtualControlKind.Trigger;
            }
            return VirtualControlKind.Button;
        }

        /// <summary>
        /// The control's bit in the Xbox button bits (A 0, B 1, X 2, Y 3, LB 4, RB 5, Back 6, Start 7, left stick
        /// press 8, right stick press 9).
        /// </summary>
        /// <param name="control">Virtual control.</param>
        /// <returns>Bit index, or -1 for sticks, triggers and the D-pad.</returns>
        public static int GetButtonBit(VirtualControl control)
        {
            switch (control)
            {
                case VirtualControl.ButtonA: return 0;
                case VirtualControl.ButtonB: return 1;
                case VirtualControl.ButtonX: return 2;
                case VirtualControl.ButtonY: return 3;
                case VirtualControl.LeftBumper: return 4;
                case VirtualControl.RightBumper: return 5;
                case VirtualControl.Back: return BackBit;
                case VirtualControl.Start: return StartBit;
                case VirtualControl.LeftStickPress: return 8;
                case VirtualControl.RightStickPress: return 9;
            }
            return -1;
        }

        /// <summary>The binding group name of a scheme.</summary>
        /// <param name="scheme">Input scheme.</param>
        public static string GetGroupName(InputScheme scheme)
        {
            if (scheme == InputScheme.Controller)
            {
                return ControllerGroup;
            }
            return KeyboardGroup;
        }
    }
}
