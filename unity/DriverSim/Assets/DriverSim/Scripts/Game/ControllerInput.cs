using System;
using System.Collections.Generic;
using System.IO;
using DriverSim.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DriverSim.Game
{
    /// <summary>
    /// Builds the virtual Xbox controller the robot code reads, every frame, from the player's controller or keyboard.
    /// The inputs come from the actions in DriverControls.inputactions, which has one action per virtual control and
    /// two control schemes: Controller (any gamepad: Xbox, PlayStation, Switch Pro) and Keyboard. The controller is
    /// active whenever a gamepad (or a joystick the player has bound) is connected, otherwise the keyboard.
    ///
    /// While Start is held, Back is withheld from the robot so the pause chord (Start, then Back) never resets the
    /// field-centric heading. Bindings the player changes are saved to <c>bindings.json</c> in the persistent data
    /// folder. Rumble requests from the robot code go to the active gamepad's motors.
    /// </summary>
    public sealed class ControllerInput : MonoBehaviour
    {
        private const string BindingsFileName = "bindings.json"; // saved binding overrides, in the persistent data path
        private const string InvertPrefKey = "DriverSim.invertLeftStickY"; // PlayerPrefs flag: invert drive stick Y

        [Tooltip("The virtual controller's actions and default bindings (DriverControls.inputactions).")]
        [SerializeField] private InputActionAsset actionsAsset;

        private readonly Dictionary<VirtualControl, InputAction> actionsByControl =
            new Dictionary<VirtualControl, InputAction>(); // runtime action for each virtual control
        private InputActionAsset actions; // runtime copy of actionsAsset, so edits never touch the asset
        private ControlRebinder rebinder; // changes and saves bindings
        private InputScheme activeScheme = InputScheme.Keyboard; // scheme currently read
        private InputDevice activeController; // gamepad or joystick the player is using, null without one
        private bool isKeyboardForced; // the player chose the keyboard after the controller disconnected
        private bool hasAnnouncedStartup; // the controller present at startup has been announced
        private int reportedJoystickId; // device id of the unrecognized joystick already reported, 0 for none
        private bool isLeftStickYInverted; // drive stick Y is flipped
        private JoystickState rawState = JoystickState.Neutral; // controller reading as the player holds it
        private JoystickState robotState = JoystickState.Neutral; // reading the robot code should see

        /// <summary>Raised with the controller's name when a gamepad is connected (also for one present at
        /// startup).</summary>
        public event Action<string> ControllerConnected;

        /// <summary>Raised when the controller the player was using is disconnected.</summary>
        public event Action ActiveControllerLost;

        /// <summary>Raised with the device name the first time an unrecognized joystick is the only
        /// controller.</summary>
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

        /// <summary>The naming family of the active controller; Xbox when the keyboard is active.</summary>
        public ControllerFamily GetActiveFamily()
        {
            return ControlLabels.GetFamily(activeController);
        }

        /// <summary>The name of the active device for the title screen, e.g. "Xbox Wireless Controller" or
        /// "Keyboard".</summary>
        public string GetActiveDeviceName()
        {
            if (activeScheme == InputScheme.Controller && activeController != null)
            {
                return activeController.displayName;
            }
            return "Keyboard";
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

        /// <summary>Uses the keyboard even though a controller is (or was) connected, until a gamepad connects
        /// again.</summary>
        public void UseKeyboard()
        {
            isKeyboardForced = true;
        }

        /// <summary>Whether any gamepad is connected.</summary>
        public bool HasGamepad()
        {
            return Gamepad.all.Count > 0;
        }

        /// <summary>Runs the active gamepad's rumble motors; does nothing without one.</summary>
        /// <param name="rumble">Robot code's rumble request.</param>
        public void SetRumble(RumbleState rumble)
        {
            Gamepad gamepad = activeController as Gamepad;
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

            // start on the keyboard scheme; Update switches to a controller when one is there
            ApplyScheme(InputScheme.Keyboard);
            actions.Enable();
            InputSystem.onDeviceChange += HandleDeviceChange;
        }

        private void Update()
        {
            AnnounceStartupController();
            UpdateActiveController();
            rawState = ReadState();
            robotState = ComputeRobotState(rawState);
        }

        private void OnDestroy()
        {
            InputSystem.onDeviceChange -= HandleDeviceChange;
            StopRumble();
        }

        // ---- private stateful helpers

        /// <summary>Tells listeners about a controller that was already connected when the game started.</summary>
        private void AnnounceStartupController()
        {
            if (hasAnnouncedStartup)
            {
                return;
            }
            hasAnnouncedStartup = true;
            Gamepad gamepad = Gamepad.current;
            if (gamepad != null && ControllerConnected != null)
            {
                ControllerConnected(gamepad.displayName);
            }
        }

        /// <summary>Picks the controller and scheme to read this frame, and reports an unrecognized joystick
        /// once.</summary>
        private void UpdateActiveController()
        {
            // the most recently used gamepad, or a joystick the player has bound
            InputDevice device = Gamepad.current;
            if (device == null && rebinder.HasJoystickBindings())
            {
                device = Joystick.current;
            }
            activeController = device;

            // an unrecognized joystick with no bindings yet: ask the player to bind it
            Joystick joystick = Joystick.current;
            bool isOnlyJoystick = false;
            if (joystick != null && Gamepad.current == null && !rebinder.HasJoystickBindings())
            {
                isOnlyJoystick = true;
            }
            if (isOnlyJoystick && joystick.deviceId != reportedJoystickId)
            {
                reportedJoystickId = joystick.deviceId;
                if (UnknownJoystickFound != null)
                {
                    UnknownJoystickFound(joystick.displayName);
                }
            }

            // controller scheme whenever there is one, unless the player chose the keyboard
            InputScheme wanted = InputScheme.Keyboard;
            if (device != null && !isKeyboardForced)
            {
                wanted = InputScheme.Controller;
            }
            if (wanted != activeScheme)
            {
                ApplyScheme(wanted);
            }
        }

        /// <summary>Makes the actions read only one scheme's bindings.</summary>
        /// <param name="scheme">Scheme to read from now on.</param>
        private void ApplyScheme(InputScheme scheme)
        {
            activeScheme = scheme;
            string group = VirtualControls.GetGroupName(scheme);
            actions.bindingMask = InputBinding.MaskByGroup(group);
        }

        /// <summary>Reacts to controllers coming and going.</summary>
        private void HandleDeviceChange(InputDevice device, InputDeviceChange change)
        {
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
            if (isArrival && device is Gamepad)
            {
                isKeyboardForced = false;
                if (ControllerConnected != null)
                {
                    ControllerConnected(device.displayName);
                }
            }
            if (isDeparture && device == activeController && activeScheme == InputScheme.Controller)
            {
                activeController = null;
                if (ActiveControllerLost != null)
                {
                    ActiveControllerLost();
                }
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
