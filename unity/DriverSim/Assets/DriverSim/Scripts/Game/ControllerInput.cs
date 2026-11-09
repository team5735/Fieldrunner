using System;
using System.Collections.Generic;
using System.IO;
using DriverSim.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DriverSim.Game
{
    /// <summary>
    /// Builds the virtual Xbox controller the robot code reads, every frame, from the driver device: the keyboard or
    /// one controller. The inputs come from the actions in DriverControls.inputactions, which has one action per
    /// virtual control and two control schemes: Controller (any gamepad: Xbox, PlayStation, Switch Pro) and Keyboard.
    /// The actions read only the driver device. At startup the driver is the first gamepad (or a joystick the player
    /// has bound), otherwise the keyboard; a gamepad connecting while the keyboard drives takes over, one connecting
    /// while another controller drives joins the other candidates. The Controllers screen changes the driver with
    /// <see cref="SetDriverDevice"/> and <see cref="StepOutDriver"/>; while the slot is empty the keyboard drives.
    ///
    /// While Start is held, Back is withheld from the robot so the pause chord (Start, then Back) never resets the
    /// field-centric heading. Bindings the player changes are saved to <c>bindings.json</c> in the persistent data
    /// folder. Rumble requests from the robot code go to the driver's motors when it is a gamepad.
    /// </summary>
    public sealed class ControllerInput : MonoBehaviour
    {
        private const string BindingsFileName = "bindings.json"; // saved binding overrides, in the persistent data path
        private const string InvertPrefKey = "DriverSim.invertLeftStickY"; // PlayerPrefs flag: invert drive stick Y

        [Tooltip("The virtual controller's actions and default bindings (DriverControls.inputactions).")]
        [SerializeField] private InputActionAsset actionsAsset;

        private readonly Dictionary<VirtualControl, InputAction> actionsByControl =
            new Dictionary<VirtualControl, InputAction>(); // runtime action for each virtual control
        private readonly List<InputDevice> candidates = new List<InputDevice>(); // gamepads, joysticks, then keyboard
        private InputActionAsset actions; // runtime copy of actionsAsset, so edits never touch the asset
        private ControlRebinder rebinder; // changes and saves bindings
        private InputScheme activeScheme = InputScheme.Keyboard; // scheme currently read
        private InputDevice driverDevice; // the keyboard or controller that drives; null when there is no keyboard
        private InputDevice appliedDevice; // device the actions are restricted to now; null for none
        private InputDevice pendingBindDevice; // joystick that becomes the driver once it has bindings
        private bool isDriverSlotEmpty; // the driver stepped out on the Controllers screen; the keyboard drives
        private int layoutVersion; // counts changes to the candidates, the driver and the empty slot
        private bool hasAnnouncedStartup; // the driver present at startup has been announced
        private int reportedJoystickId; // device id of the unrecognized joystick already reported, 0 for none
        private bool isLeftStickYInverted; // drive stick Y is flipped
        private JoystickState rawState = JoystickState.Neutral; // controller reading as the player holds it
        private JoystickState robotState = JoystickState.Neutral; // reading the robot code should see

        /// <summary>Raised with the gamepad's name and whether it became the driver when a gamepad is connected (also
        /// for the driver present at startup).</summary>
        public event Action<string, bool> ControllerConnected;

        /// <summary>Raised when the driver device is disconnected; the keyboard drives from then on.</summary>
        public event Action ActiveControllerLost;

        /// <summary>Raised with the device name when an unrecognized joystick needs bindings before it can
        /// drive.</summary>
        public event Action<string> UnknownJoystickFound;

        /// <summary>Sets the actions to read; call before the scene starts.</summary>
        /// <param name="inputActionsAsset">DriverControls.inputactions.</param>
        public void SetActionsAsset(InputActionAsset inputActionsAsset)
        {
            actionsAsset = inputActionsAsset;
        }

        /// <summary>The scheme currently read.</summary>
        public InputScheme GetActiveScheme()
        {
            return activeScheme;
        }

        /// <summary>The naming family of the driver device; Xbox when the keyboard drives.</summary>
        public ControllerFamily GetDriverFamily()
        {
            if (IsKeyboard(driverDevice))
            {
                return ControllerFamily.Xbox;
            }
            return ControlLabels.GetFamily(driverDevice);
        }

        /// <summary>The name of the driver device for the title screen, e.g. "Xbox Wireless Controller" or
        /// "Keyboard".</summary>
        public string GetDriverDeviceName()
        {
            return ControlLabels.GetDeviceName(driverDevice);
        }

        /// <summary>The device the robot reads: the keyboard or one controller. While the slot is empty it is the
        /// keyboard.</summary>
        public InputDevice GetDriverDevice()
        {
            return driverDevice;
        }

        /// <summary>Whether a controller (not the keyboard) drives.</summary>
        public bool IsDriverController()
        {
            return !IsKeyboard(driverDevice);
        }

        /// <summary>Whether the driver stepped out on the Controllers screen, so the slot shows the keyboard
        /// placeholder.</summary>
        public bool IsDriverSlotEmpty()
        {
            return isDriverSlotEmpty;
        }

        /// <summary>Every device that could drive: each gamepad and joystick, then the keyboard.</summary>
        public IReadOnlyList<InputDevice> GetCandidateDevices()
        {
            return candidates;
        }

        /// <summary>A number that changes whenever the candidates, the driver or the empty slot change.</summary>
        public int GetLayoutVersion()
        {
            return layoutVersion;
        }

        /// <summary>The controller reading as the player holds it, including Back while Start is held.</summary>
        public JoystickState GetRawState()
        {
            return rawState;
        }

        /// <summary>The reading the robot code should see: the raw one with Back withheld while Start is
        /// held.</summary>
        public JoystickState GetRobotState()
        {
            return robotState;
        }

        /// <summary>The binding editor.</summary>
        public ControlRebinder GetRebinder()
        {
            return rebinder;
        }

        /// <summary>Whether the drive stick's Y axis is flipped.</summary>
        public bool IsLeftStickYInverted()
        {
            return isLeftStickYInverted;
        }

        /// <summary>Flips or restores the drive stick's Y axis and remembers the choice.</summary>
        /// <param name="isInverted">True to flip the axis.</param>
        public void SetLeftStickYInverted(bool isInverted)
        {
            isLeftStickYInverted = isInverted;
            int stored = 0;
            if (isInverted)
            {
                stored = 1;
            }
            PlayerPrefs.SetInt(InvertPrefKey, stored);
        }

        /// <summary>
        /// Makes a device the driver; the previous driver is then just a candidate. A joystick without saved bindings
        /// is not made the driver yet: <see cref="UnknownJoystickFound"/> is raised so the player can bind it, and it
        /// drives once it has bindings.
        /// </summary>
        /// <param name="device">A gamepad, a joystick or the keyboard from <see cref="GetCandidateDevices"/>.</param>
        public void SetDriverDevice(InputDevice device)
        {
            if (device == null)
            {
                return;
            }

            // a joystick the game has no bindings for cannot drive yet
            if (device is Joystick && !rebinder.HasJoystickBindings())
            {
                pendingBindDevice = device;
                if (UnknownJoystickFound != null)
                {
                    UnknownJoystickFound(device.displayName);
                }
                return;
            }

            // take the slot
            driverDevice = device;
            isDriverSlotEmpty = false;
            pendingBindDevice = null;
            ApplyDriver();
            layoutVersion++;
            LogDevices();
        }

        /// <summary>Empties the driver slot: the driver becomes a candidate and the keyboard drives unless someone
        /// takes the slot. Does nothing when the slot is already empty.</summary>
        public void StepOutDriver()
        {
            if (isDriverSlotEmpty)
            {
                return;
            }
            driverDevice = Keyboard.current;
            isDriverSlotEmpty = true;
            pendingBindDevice = null;
            ApplyDriver();
            layoutVersion++;
            LogDevices();
        }

        /// <summary>Ends the empty slot when the Controllers screen closes: the keyboard, which already drives, takes
        /// the slot. Does nothing when the slot is not empty.</summary>
        public void CloseDriverSlot()
        {
            if (!isDriverSlotEmpty)
            {
                return;
            }
            isDriverSlotEmpty = false;
            layoutVersion++;
            LogDevices();
        }

        /// <summary>Runs the driver's rumble motors when it is a gamepad; does nothing otherwise.</summary>
        /// <param name="rumble">Robot code's rumble request.</param>
        public void SetRumble(RumbleState rumble)
        {
            Gamepad gamepad = driverDevice as Gamepad;
            if (gamepad == null)
            {
                return;
            }
            gamepad.SetMotorSpeeds(rumble.Left, rumble.Right);
        }

        /// <summary>Stops rumble on every connected gamepad.</summary>
        public void StopRumble()
        {
            foreach (Gamepad gamepad in Gamepad.all)
            {
                gamepad.SetMotorSpeeds(0, 0);
            }
        }

        // ---- Unity messages

        private void Awake()
        {
            // a private copy: rebinding edits this one, never the asset
            actions = Instantiate(actionsAsset);
            foreach (VirtualControl control in VirtualControls.GetAll())
            {
                string actionName = VirtualControls.GetActionName(control);
                InputAction action = actions.FindAction(VirtualControls.ActionMapName + "/" + actionName);
                actionsByControl[control] = action;
            }

            // saved bindings and settings
            string bindingsPath = Path.Combine(Application.persistentDataPath, BindingsFileName);
            rebinder = new ControlRebinder(actions, bindingsPath);
            rebinder.LoadOverrides();
            isLeftStickYInverted = PlayerPrefs.GetInt(InvertPrefKey, 0) != 0;

            // the default driver; later connections are handled by HandleDeviceChange
            RefreshCandidates();
            driverDevice = FindDefaultDriver();
            ApplyDriver();
            actions.Enable();
            InputSystem.onDeviceChange += HandleDeviceChange;
        }

        private void Update()
        {
            AnnounceStartupDriver();
            UpdatePendingJoystick();
            UpdateDeviceRestriction();
            rawState = ReadState();
            robotState = ComputeRobotState(rawState);
        }

        private void OnDestroy()
        {
            InputSystem.onDeviceChange -= HandleDeviceChange;
            StopRumble();
        }

        // ---- private stateful helpers

        /// <summary>Tells listeners about the driver chosen at startup and logs the device list once.</summary>
        private void AnnounceStartupDriver()
        {
            if (hasAnnouncedStartup)
            {
                return;
            }
            hasAnnouncedStartup = true;
            LogDevices();
            if (IsDriverController() && ControllerConnected != null)
            {
                ControllerConnected(driverDevice.displayName, true);
            }
        }

        /// <summary>
        /// Makes a joystick the driver once it has bindings, and reports an unrecognized joystick that is the only
        /// controller once so the player can bind it.
        /// </summary>
        private void UpdatePendingJoystick()
        {
            // a joystick chosen on the Controllers screen drives as soon as the player has bound it
            if (pendingBindDevice != null)
            {
                if (!pendingBindDevice.added)
                {
                    pendingBindDevice = null;
                }
                else if (rebinder.HasJoystickBindings())
                {
                    InputDevice bound = pendingBindDevice;
                    SetDriverDevice(bound);
                }
            }

            // an unrecognized joystick with no bindings yet, and no gamepad: ask the player to bind it
            Joystick joystick = Joystick.current;
            bool isOnlyJoystick = false;
            if (joystick != null && Gamepad.current == null && !rebinder.HasJoystickBindings())
            {
                isOnlyJoystick = true;
            }
            if (isOnlyJoystick && joystick.deviceId != reportedJoystickId)
            {
                reportedJoystickId = joystick.deviceId;
                pendingBindDevice = joystick;
                if (UnknownJoystickFound != null)
                {
                    UnknownJoystickFound(joystick.displayName);
                }
            }
        }

        /// <summary>Restricts the actions to the driver device, except while a rebind listens: that must hear any
        /// device of the scheme being rebound.</summary>
        private void UpdateDeviceRestriction()
        {
            InputDevice wanted = driverDevice;
            if (rebinder.IsRebinding())
            {
                wanted = null;
            }
            if (wanted == appliedDevice)
            {
                return;
            }
            appliedDevice = wanted;
            if (wanted == null)
            {
                actions.devices = null;
            }
            else
            {
                actions.devices = new InputDevice[] { wanted };
            }
        }

        /// <summary>Makes the actions read the driver's scheme: the keyboard's, or the controller's.</summary>
        private void ApplyDriver()
        {
            InputScheme scheme = InputScheme.Controller;
            if (IsKeyboard(driverDevice))
            {
                scheme = InputScheme.Keyboard;
            }
            activeScheme = scheme;
            string group = VirtualControls.GetGroupName(scheme);
            actions.bindingMask = InputBinding.MaskByGroup(group);
        }

        /// <summary>Rebuilds the candidate list from the devices that are connected now.</summary>
        private void RefreshCandidates()
        {
            candidates.Clear();
            foreach (InputDevice device in InputSystem.devices)
            {
                if (device is Gamepad || device is Joystick)
                {
                    candidates.Add(device);
                }
            }
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                candidates.Add(keyboard);
            }
            layoutVersion++;
        }

        /// <summary>The device that drives at startup: the first gamepad, else the first joystick when the player has
        /// bound one, else the keyboard.</summary>
        private InputDevice FindDefaultDriver()
        {
            foreach (InputDevice device in candidates)
            {
                if (device is Gamepad)
                {
                    return device;
                }
            }
            if (rebinder.HasJoystickBindings())
            {
                foreach (InputDevice device in candidates)
                {
                    if (device is Joystick)
                    {
                        return device;
                    }
                }
            }
            return Keyboard.current;
        }

        /// <summary>Writes the device list to the log: each name and family, marking the driver.</summary>
        private void LogDevices()
        {
            List<string> parts = new List<string>();
            foreach (InputDevice device in candidates)
            {
                string name = ControlLabels.GetDeviceName(device);
                string family = ControlLabels.GetFamilyName(device);
                string detail = family;
                if (device == driverDevice && !isDriverSlotEmpty)
                {
                    detail = family + ", driver";
                }
                parts.Add(name + " (" + detail + ")");
            }
            string list = string.Join("; ", parts);
            string slotText = "";
            if (isDriverSlotEmpty)
            {
                slotText = "; driver slot empty (the keyboard drives)";
            }
            Debug.Log("[Driver-Sim] input devices: " + list + slotText);
        }

        /// <summary>Reacts to controllers and keyboards coming and going.</summary>
        private void HandleDeviceChange(InputDevice device, InputDeviceChange change)
        {
            bool isRelevant = false;
            if (device is Gamepad || device is Joystick || device is Keyboard)
            {
                isRelevant = true;
            }
            if (!isRelevant)
            {
                return;
            }
            bool isArrival = false;
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected)
            {
                isArrival = true;
            }
            bool isDeparture = false;
            if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected)
            {
                isDeparture = true;
            }
            if (!isArrival && !isDeparture)
            {
                return;
            }
            RefreshCandidates();

            // a keyboard that comes or goes while the keyboard drives
            if (device is Keyboard && !IsDriverController() && !isDriverSlotEmpty)
            {
                driverDevice = Keyboard.current;
                ApplyDriver();
            }

            // a gamepad arrives: it takes the slot from the keyboard, otherwise it waits on the Controllers screen
            if (isArrival && device is Gamepad)
            {
                bool takesSlot = false;
                if (!IsDriverController() && !isDriverSlotEmpty)
                {
                    takesSlot = true;
                }
                if (takesSlot)
                {
                    driverDevice = device;
                    ApplyDriver();
                }
                if (ControllerConnected != null)
                {
                    ControllerConnected(device.displayName, takesSlot);
                }
            }

            // the driver leaves: the slot empties and the keyboard drives
            bool isDriverLost = false;
            if (isDeparture && device == driverDevice && !IsKeyboard(device))
            {
                isDriverLost = true;
            }
            if (isDriverLost)
            {
                driverDevice = Keyboard.current;
                isDriverSlotEmpty = true;
                ApplyDriver();
            }
            LogDevices();
            if (isDriverLost && ActiveControllerLost != null)
            {
                ActiveControllerLost();
            }
        }

        /// <summary>Reads the virtual controller from the actions as the player holds it right now.</summary>
        private JoystickState ReadState()
        {
            // sticks: the robot expects forward to be negative Y, Unity's gamepad has it positive
            Vector2 left = actionsByControl[VirtualControl.LeftStick].ReadValue<Vector2>();
            Vector2 right = actionsByControl[VirtualControl.RightStick].ReadValue<Vector2>();
            double leftY = -left.y;
            if (isLeftStickYInverted)
            {
                leftY = left.y;
            }
            double rightY = -right.y;

            // triggers
            float leftTrigger = actionsByControl[VirtualControl.LeftTrigger].ReadValue<float>();
            float rightTrigger = actionsByControl[VirtualControl.RightTrigger].ReadValue<float>();
            leftTrigger = Mathf.Clamp01(leftTrigger);
            rightTrigger = Mathf.Clamp01(rightTrigger);

            // buttons
            int buttonBits = 0;
            foreach (VirtualControl control in VirtualControls.GetAll())
            {
                int bit = VirtualControls.GetButtonBit(control);
                if (bit >= 0 && actionsByControl[control].IsPressed())
                {
                    buttonBits = buttonBits | (1 << bit);
                }
            }

            // D-pad
            bool isUp = actionsByControl[VirtualControl.DpadUp].IsPressed();
            bool isRight = actionsByControl[VirtualControl.DpadRight].IsPressed();
            bool isDown = actionsByControl[VirtualControl.DpadDown].IsPressed();
            bool isLeft = actionsByControl[VirtualControl.DpadLeft].IsPressed();
            int povDeg = ComputePovDeg(isUp, isRight, isDown, isLeft);
            return new JoystickState(left.x, leftY, leftTrigger, rightTrigger, right.x, rightY, buttonBits, povDeg);
        }

        // ---- low-level helpers (static, no side effects)

        /// <summary>Whether a device counts as the keyboard; a missing device (no keyboard present) does.</summary>
        /// <param name="device">Device to check; null for none.</param>
        private static bool IsKeyboard(InputDevice device)
        {
            if (device == null)
            {
                return true;
            }
            return device is Keyboard;
        }

        /// <summary>The reading the robot code should see: Back withheld while Start is held.</summary>
        /// <param name="state">Raw reading.</param>
        private static JoystickState ComputeRobotState(JoystickState state)
        {
            if (state.IsButtonDown(VirtualControls.StartBit))
            {
                return state.MakeWithButtonCleared(VirtualControls.BackBit);
            }
            return state;
        }

        /// <summary>The D-pad direction in degrees clockwise from up, from its four directions.</summary>
        /// <param name="isUp">Up pressed.</param>
        /// <param name="isRight">Right pressed.</param>
        /// <param name="isDown">Down pressed.</param>
        /// <param name="isLeft">Left pressed.</param>
        /// <returns>0, 45, ... 315, or -1 when nothing (or only opposite directions) is pressed.</returns>
        private static int ComputePovDeg(bool isUp, bool isRight, bool isDown, bool isLeft)
        {
            // opposite directions cancel
            bool isVertical = isUp != isDown;
            bool isHorizontal = isLeft != isRight;
            bool hasUp = false;
            bool hasDown = false;
            if (isVertical)
            {
                hasUp = isUp;
                hasDown = isDown;
            }
            bool hasLeft = false;
            bool hasRight = false;
            if (isHorizontal)
            {
                hasLeft = isLeft;
                hasRight = isRight;
            }

            // diagonals, then the four directions
            if (hasUp && hasRight)
            {
                return 45;
            }
            if (hasDown && hasRight)
            {
                return 135;
            }
            if (hasDown && hasLeft)
            {
                return 225;
            }
            if (hasUp && hasLeft)
            {
                return 315;
            }
            if (hasUp)
            {
                return 0;
            }
            if (hasRight)
            {
                return 90;
            }
            if (hasDown)
            {
                return 180;
            }
            if (hasLeft)
            {
                return 270;
            }
            return -1;
        }
    }
}
