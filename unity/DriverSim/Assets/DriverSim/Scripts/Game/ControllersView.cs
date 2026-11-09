using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace DriverSim.Game
{
    /// <summary>
    /// The Controllers screen: a large Driver slot on the left and one card in the middle for every other device (each
    /// gamepad, each joystick, the keyboard). Pressing left on a device in the middle makes it the driver, pressing
    /// right on the driver steps it out and leaves the slot empty, shown as a dimmed keyboard card because the
    /// keyboard drives if nobody takes the slot. A card lights up briefly when its device has any input. Cards can
    /// also be clicked. Devices are read directly, not through the action asset, which only sees the driver.
    /// </summary>
    public sealed class ControllersView
    {
        private const float LitS = 0.35f; // seconds a card stays lit after its device had input
        private const float DirectionThreshold = 0.5f; // stick travel that counts as a new push left or right
        private const float DirectionReleaseThreshold = 0.3f; // stick travel below which a held push counts as released
        private const float ActivityThreshold = 0.3f; // control travel that counts as input for the lit card
        private const string LinuxHint = "If one is plugged in, check you can read /dev/input."; // shown on Linux only

        private readonly VisualElement slot; // container of the Driver slot
        private readonly VisualElement middle; // container of the cards that are not the driver
        private readonly Label hint; // line shown when no controller is detected
        private readonly ControllerInput input; // devices and the driver slot
        private readonly Dictionary<int, VisualElement> cardsByDeviceId =
            new Dictionary<int, VisualElement>(); // card currently shown for each device id
        private readonly Dictionary<int, float> litUntilById =
            new Dictionary<int, float>(); // Time.unscaledTime until which each device's card is lit
        private readonly Dictionary<int, int> directionById =
            new Dictionary<int, int>(); // last horizontal direction per device id: -1 left, 0 none, 1 right
        private int shownVersion = -1; // layout version of the cards now on screen

        /// <summary>Wires the screen's elements.</summary>
        /// <param name="screen">The Controllers screen element.</param>
        /// <param name="controllerInput">Devices and the driver slot.</param>
        public ControllersView(VisualElement screen, ControllerInput controllerInput)
        {
            input = controllerInput;
            slot = screen.Q<VisualElement>("controllers-slot");
            middle = screen.Q<VisualElement>("controllers-middle");
            hint = screen.Q<Label>("controllers-hint");
        }

        /// <summary>Redraws the cards and takes the devices' current direction as the starting point, so a key that is
        /// already held when the screen opens does not count as a press.</summary>
        public void Refresh()
        {
            shownVersion = input.GetLayoutVersion();
            RebuildCards();
            IReadOnlyList<InputDevice> devices = input.GetCandidateDevices();
            directionById.Clear();
            foreach (InputDevice device in devices)
            {
                directionById[device.deviceId] = ReadDirection(device, 0);
            }
        }

        /// <summary>Per-frame upkeep: redraws when devices or the slot changed, lights cards with input, and acts on
        /// left and right presses.</summary>
        public void UpdateView()
        {
            if (shownVersion != input.GetLayoutVersion())
            {
                Refresh();
            }
            IReadOnlyList<InputDevice> devices = input.GetCandidateDevices();
            float nowS = Time.unscaledTime;
            for (int index = 0; index < devices.Count; index++)
            {
                InputDevice device = devices[index];
                UpdateLight(device, nowS);
                UpdateDirection(device);
            }
        }

        // ---- private stateful helpers

        /// <summary>Lights the device's card while it has input.</summary>
        private void UpdateLight(InputDevice device, float nowS)
        {
            int id = device.deviceId;
            if (HasInput(device))
            {
                litUntilById[id] = nowS + LitS;
            }
            if (!cardsByDeviceId.TryGetValue(id, out VisualElement card))
            {
                return;
            }
            bool isLit = false;
            if (litUntilById.TryGetValue(id, out float litUntilS))
            {
                isLit = nowS < litUntilS;
            }
            card.EnableInClassList("lit", isLit);
        }

        /// <summary>Acts on a new left or right press of one device: left takes the slot, right steps the driver
        /// out.</summary>
        private void UpdateDirection(InputDevice device)
        {
            int id = device.deviceId;
            int previous = 0;
            directionById.TryGetValue(id, out previous);
            int direction = ReadDirection(device, previous);
            directionById[id] = direction;
            if (direction == previous)
            {
                return;
            }
            bool isDriver = IsInSlot(device);
            if (direction < 0 && !isDriver)
            {
                input.SetDriverDevice(device);
            }
            if (direction > 0 && isDriver)
            {
                input.StepOutDriver();
            }
        }

        /// <summary>Whether a device is the one shown in the Driver slot.</summary>
        private bool IsInSlot(InputDevice device)
        {
            if (input.IsDriverSlotEmpty())
            {
                return false;
            }
            return device == input.GetDriverDevice();
        }

        /// <summary>Redraws the Driver slot, the middle cards and the hint.</summary>
        private void RebuildCards()
        {
            slot.Clear();
            middle.Clear();
            cardsByDeviceId.Clear();

            // the slot: its heading, then the driver or the ghost keyboard
            Label heading = new Label("DRIVER");
            heading.AddToClassList("slot-heading");
            slot.Add(heading);
            if (input.IsDriverSlotEmpty())
            {
                VisualElement ghostCard = MakeGhostCard();
                slot.Add(ghostCard);
            }

            // every candidate in its place
            int controllerCount = 0;
            IReadOnlyList<InputDevice> devices = input.GetCandidateDevices();
            foreach (InputDevice device in devices)
            {
                if (!(device is Keyboard))
                {
                    controllerCount++;
                }
                bool isDriver = IsInSlot(device);
                VisualElement card = MakeCard(device, isDriver);
                cardsByDeviceId[device.deviceId] = card;
                if (isDriver)
                {
                    slot.Add(card);
                }
                else
                {
                    middle.Add(card);
                }
            }
            hint.text = MakeHintText(controllerCount);
        }

        /// <summary>Makes the card of one device: its name and kind, clickable.</summary>
        private VisualElement MakeCard(InputDevice device, bool isDriver)
        {
            VisualElement card = new VisualElement();
            card.AddToClassList("device-card");
            if (isDriver)
            {
                card.AddToClassList("big");
            }
            string deviceName = ControlLabels.GetDeviceName(device);
            string familyName = ControlLabels.GetFamilyName(device);
            Label nameLabel = new Label(deviceName);
            nameLabel.AddToClassList("card-name");
            nameLabel.pickingMode = PickingMode.Ignore;
            card.Add(nameLabel);
            Label familyLabel = new Label(familyName);
            familyLabel.AddToClassList("card-family");
            familyLabel.pickingMode = PickingMode.Ignore;
            card.Add(familyLabel);
            card.RegisterCallback<ClickEvent>(evt => HandleCardClicked(device));
            return card;
        }

        /// <summary>Makes the dimmed keyboard card that fills an empty slot; clicking it makes the keyboard the
        /// driver.</summary>
        private VisualElement MakeGhostCard()
        {
            VisualElement card = new VisualElement();
            card.AddToClassList("device-card");
            card.AddToClassList("big");
            card.AddToClassList("ghost");
            Label nameLabel = new Label("Keyboard");
            nameLabel.AddToClassList("card-name");
            nameLabel.pickingMode = PickingMode.Ignore;
            card.Add(nameLabel);
            Label noteLabel = new Label("drives if no one takes the slot");
            noteLabel.AddToClassList("card-family");
            noteLabel.pickingMode = PickingMode.Ignore;
            card.Add(noteLabel);
            card.RegisterCallback<ClickEvent>(evt => HandleCardClicked(Keyboard.current));
            return card;
        }

        /// <summary>A click on a card: the driver steps out, any other device takes the slot.</summary>
        private void HandleCardClicked(InputDevice device)
        {
            if (device == null)
            {
                return;
            }
            if (IsInSlot(device))
            {
                input.StepOutDriver();
            }
            else
            {
                input.SetDriverDevice(device);
            }
        }

        // ---- low-level helpers (static, no side effects)

        /// <summary>The line under the cards: empty unless no controller is detected.</summary>
        /// <param name="controllerCount">Gamepads and joysticks detected.</param>
        private static string MakeHintText(int controllerCount)
        {
            if (controllerCount > 0)
            {
                return "";
            }
            string text = "No controllers detected - the keyboard drives.";
            RuntimePlatform platform = Application.platform;
            if (platform == RuntimePlatform.LinuxPlayer || platform == RuntimePlatform.LinuxEditor)
            {
                text = text + " " + LinuxHint;
            }
            return text;
        }

        /// <summary>Which way a device is pushed horizontally: gamepad D-pad or left stick, joystick stick, keyboard
        /// arrow keys or A / D.</summary>
        /// <param name="device">Gamepad, joystick or keyboard.</param>
        /// <param name="previousDirection">Direction read last frame; a held stick push stays held until the stick
        /// comes back inside the release threshold, so a stick resting near the press threshold cannot repeat.</param>
        /// <returns>-1 left, 1 right, 0 neither (or both).</returns>
        private static int ReadDirection(InputDevice device, int previousDirection)
        {
            bool isLeft = false;
            bool isRight = false;
            Gamepad gamepad = device as Gamepad;
            Joystick joystick = device as Joystick;
            Keyboard keyboard = device as Keyboard;
            float stickX = 0f;
            if (gamepad != null)
            {
                isLeft = gamepad.dpad.left.isPressed;
                isRight = gamepad.dpad.right.isPressed;
                stickX = gamepad.leftStick.x.ReadValue();
            }
            if (joystick != null)
            {
                stickX = joystick.stick.x.ReadValue();
            }
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed)
                {
                    isLeft = true;
                }
                if (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed)
                {
                    isRight = true;
                }
            }
            // stick: a new push past the press threshold, or a held push not yet back inside the release threshold
            float leftThreshold = DirectionThreshold;
            if (previousDirection < 0)
            {
                leftThreshold = DirectionReleaseThreshold;
            }
            float rightThreshold = DirectionThreshold;
            if (previousDirection > 0)
            {
                rightThreshold = DirectionReleaseThreshold;
            }
            if (stickX < -leftThreshold)
            {
                isLeft = true;
            }
            if (stickX > rightThreshold)
            {
                isRight = true;
            }
            if (isLeft && !isRight)
            {
                return -1;
            }
            if (isRight && !isLeft)
            {
                return 1;
            }
            return 0;
        }

        /// <summary>Whether a device has any input right now, for lighting its card.</summary>
        /// <param name="device">Gamepad, joystick or keyboard.</param>
        private static bool HasInput(InputDevice device)
        {
            Keyboard keyboard = device as Keyboard;
            if (keyboard != null)
            {
                return keyboard.anyKey.isPressed;
            }
            return device.IsActuated(ActivityThreshold);
        }
    }
}
