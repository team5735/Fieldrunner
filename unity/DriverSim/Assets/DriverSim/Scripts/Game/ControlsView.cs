using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DriverSim.Game
{
    /// <summary>
    /// The Controls screen: a schematic gamepad with each input labelled by the robot action bound to it, and a table
    /// of the robot's actions with the controller and keyboard input for each. Selecting an input in the table rebinds
    /// it. Labels follow the active controller (Xbox A/B/X/Y, PlayStation Cross/Circle/Square/Triangle, or generic).
    /// </summary>
    public sealed class ControlsView
    {
        private const float DiagramWidthPx = 1000f; // schematic size in reference pixels
        private const float LeftAnchorXPx = 292f; // where leader lines leave the left callouts
        private const float RightAnchorXPx = 708f; // where leader lines leave the right callouts
        private const float CalloutHeightPx = 50f; // two lines of 19 px text

        /// <summary>A physical gamepad input drawn on the schematic.</summary>
        private readonly struct PadSlot
        {
            public PadSlot(string path, float xPx, float yPx, bool isOnLeft, float labelTopPx)
            {
                Path = path;
                XPx = xPx;
                YPx = yPx;
                IsOnLeft = isOnLeft;
                LabelTopPx = labelTopPx;
            }

            public string Path { get; } // Input System path of the physical input
            public float XPx { get; } // center of the drawn part, diagram pixels
            public float YPx { get; } // center of the drawn part, diagram pixels
            public bool IsOnLeft { get; } // whether its callout is in the left column
            public float LabelTopPx { get; } // top of its callout, diagram pixels
        }

        private static readonly PadSlot[] Slots =
        {
            new PadSlot("<Gamepad>/leftTrigger", 370, 108, true, 20),
            new PadSlot("<Gamepad>/leftShoulder", 370, 142, true, 88),
            new PadSlot("<Gamepad>/leftStick", 390, 235, true, 160),
            new PadSlot("<Gamepad>/select", 477, 237, true, 228),
            new PadSlot("<Gamepad>/dpad/up", 430, 322, true, 296),
            new PadSlot("<Gamepad>/dpad/down", 430, 378, true, 356),
            new PadSlot("<Gamepad>/dpad/left", 404, 350, true, 416),
            new PadSlot("<Gamepad>/dpad/right", 456, 350, true, 476),
            new PadSlot("<Gamepad>/leftStickPress", 390, 235, true, 536),
            new PadSlot("<Gamepad>/rightTrigger", 630, 108, false, 20),
            new PadSlot("<Gamepad>/rightShoulder", 630, 142, false, 88),
            new PadSlot("<Gamepad>/buttonNorth", 610, 200, false, 150),
            new PadSlot("<Gamepad>/buttonWest", 576, 235, false, 210),
            new PadSlot("<Gamepad>/buttonEast", 644, 235, false, 270),
            new PadSlot("<Gamepad>/buttonSouth", 610, 270, false, 330),
            new PadSlot("<Gamepad>/start", 523, 237, false, 390),
            new PadSlot("<Gamepad>/rightStick", 570, 350, false, 450),
            new PadSlot("<Gamepad>/rightStickPress", 570, 350, false, 510),
        }; // every physical input that can carry a robot action; only the bound ones get a callout

        private readonly VisualElement diagram; // container for the schematic
        private readonly VisualElement table; // container for the rows
        private readonly Label prompt; // rebinding prompt line
        private readonly Label banner; // "New controller" line
        private readonly Label deviceHeading; // heading of the controller column
        private readonly Toggle invertToggle; // drive stick invert
        private readonly Label unusedLabel; // line naming the unused controls
        private readonly Label pauseHintLabel; // line explaining how to pause
        private readonly ControllerInput input; // bindings and active device
        private readonly List<Label> callouts = new List<Label>(); // callouts currently on the diagram
        private readonly List<VisualElement> leaderLines = new List<VisualElement>(); // leader lines currently drawn
        private readonly List<VisualElement> tableRows = new List<VisualElement>(); // rows currently in the table
        private string pendingFocusKey = ""; // key of the binding button to focus after the next rebuild

        /// <summary>Builds the screen's static parts and wires its controls.</summary>
        /// <param name="screen">The Controls screen element.</param>
        /// <param name="controllerInput">Bindings and the active device.</param>
        public ControlsView(VisualElement screen, ControllerInput controllerInput)
        {
            input = controllerInput;
            diagram = screen.Q<VisualElement>("controls-diagram");
            table = screen.Q<VisualElement>("controls-table");
            prompt = screen.Q<Label>("controls-prompt");
            banner = screen.Q<Label>("controls-banner");
            deviceHeading = screen.Q<Label>("controls-device-heading");
            invertToggle = screen.Q<Toggle>("controls-invert");
            unusedLabel = screen.Q<Label>("controls-unused");
            pauseHintLabel = screen.Q<Label>("controls-pause-hint");
            invertToggle.RegisterValueChangedCallback(HandleInvertChanged);
            DrawPad();
            unusedLabel.text = RobotControlsLayout.UnusedText;
            pauseHintLabel.text = RobotControlsLayout.PauseHint;
        }

        /// <summary>Shows a line above the schematic, e.g. "New controller: bind its inputs"; empty hides it.</summary>
        /// <param name="text">Line to show.</param>
        public void SetBanner(string text)
        {
            banner.text = text;
        }

        /// <summary>The first input in the table, for focus when the screen opens.</summary>
        public VisualElement GetFirstFocusable()
        {
            if (table.childCount == 0)
            {
                return null;
            }
            VisualElement firstRow = table[0];
            return firstRow.Q<Button>(className: "bind-button");
        }

        /// <summary>Rebuilds the labels and the table from the current bindings and active device.</summary>
        public void Refresh()
        {
            ControllerFamily family = input.GetActiveFamily();
            deviceHeading.text = MakeDeviceHeading(family);
            bool isLeftStickYInverted = input.IsLeftStickYInverted();
            invertToggle.SetValueWithoutNotify(isLeftStickYInverted);
            RebuildCallouts(family);
            RebuildTable(family);
        }

        /// <summary>Per-frame upkeep: the rebinding prompt, and the table's availability while a rebind
        /// listens.</summary>
        public void UpdateView()
        {
            ControlRebinder rebinder = input.GetRebinder();
            bool isRebinding = rebinder.IsRebinding();
            prompt.text = rebinder.GetPromptText();
            table.SetEnabled(!isRebinding);
        }

        // ---- private stateful helpers

        /// <summary>Draws the gamepad body and its parts once; the callouts are added by <see
        /// cref="Refresh"/>.</summary>
        private void DrawPad()
        {
            // the body first: leader lines (added per callout in RebuildCallouts) go above it and below the parts
            AddPart("pad-body", 300, 150, 400, 320, false);
            AddPart("pad-part", 330, 92, 80, 32, false);
            AddPart("pad-part", 590, 92, 80, 32, false);
            AddPart("pad-part", 322, 130, 96, 24, false);
            AddPart("pad-part", 582, 130, 96, 24, false);
            AddPart("pad-part", 354, 199, 72, 72, true);
            AddPart("pad-part", 536, 316, 68, 68, true);
            AddPart("pad-part", 418, 316, 24, 68, false);
            AddPart("pad-part", 396, 338, 68, 24, false);
            AddPart("pad-part", 460, 228, 34, 18, false);
            AddPart("pad-part", 506, 228, 34, 18, false);
            AddFaceButton("Y", 610, 200, new Color(0.95f, 0.8f, 0.1f));
            AddFaceButton("X", 576, 235, new Color(0.2f, 0.45f, 0.95f));
            AddFaceButton("B", 644, 235, new Color(0.9f, 0.2f, 0.2f));
            AddFaceButton("A", 610, 270, new Color(0.2f, 0.75f, 0.3f));
        }

        /// <summary>Adds one drawn rectangle or circle to the schematic.</summary>
        private void AddPart(string kindClass, float leftPx, float topPx, float widthPx, float heightPx, bool isRound)
        {
            VisualElement part = new VisualElement();
            part.AddToClassList(kindClass);
            part.pickingMode = PickingMode.Ignore;
            part.style.left = leftPx;
            part.style.top = topPx;
            part.style.width = widthPx;
            part.style.height = heightPx;
            if (isRound)
            {
                part.AddToClassList("pad-round");
                part.style.borderTopLeftRadius = widthPx;
                part.style.borderTopRightRadius = widthPx;
                part.style.borderBottomLeftRadius = widthPx;
                part.style.borderBottomRightRadius = widthPx;
            }
            diagram.Add(part);
        }

        /// <summary>Adds a lettered face button.</summary>
        private void AddFaceButton(string letter, float centerXPx, float centerYPx, Color color)
        {
            const float diameterPx = 34f;
            Label button = new Label(letter);
            button.AddToClassList("pad-part");
            button.AddToClassList("pad-round");
            button.AddToClassList("pad-key");
            button.pickingMode = PickingMode.Ignore;
            button.style.left = centerXPx - diameterPx / 2;
            button.style.top = centerYPx - diameterPx / 2;
            button.style.width = diameterPx;
            button.style.height = diameterPx;
            button.style.borderTopLeftRadius = diameterPx;
            button.style.borderTopRightRadius = diameterPx;
            button.style.borderBottomLeftRadius = diameterPx;
            button.style.borderBottomRightRadius = diameterPx;
            button.style.backgroundColor = color;
            diagram.Add(button);
        }

        /// <summary>Removes the old callouts and lines, and adds one for each physical input that has a robot
        /// action.</summary>
        private void RebuildCallouts(ControllerFamily family)
        {
            // clear the previous labels and lines
            foreach (Label callout in callouts)
            {
                callout.RemoveFromHierarchy();
            }
            callouts.Clear();
            foreach (VisualElement line in leaderLines)
            {
                line.RemoveFromHierarchy();
            }
            leaderLines.Clear();

            // one callout per bound, used input
            ControlRebinder rebinder = input.GetRebinder();
            foreach (PadSlot slot in Slots)
            {
                List<VirtualControl> boundControls = rebinder.FindControlsBoundTo(slot.Path);
                string actionText = MakeActionText(boundControls);
                if (actionText.Length == 0)
                {
                    continue;
                }
                string name = ControlLabels.GetPathName(slot.Path, family);
                AddCallout(slot, name, actionText);
            }
        }

        /// <summary>Adds a leader line and a two-line label for one physical input.</summary>
        private void AddCallout(PadSlot slot, string name, string actionText)
        {
            // label in its column
            Label callout = new Label(name + "\n" + actionText);
            callout.AddToClassList("callout");
            callout.pickingMode = PickingMode.Ignore;
            float labelLeftPx = DiagramWidthPx - 280f;
            callout.style.unityTextAlign = TextAnchor.UpperLeft;
            if (slot.IsOnLeft)
            {
                labelLeftPx = 0;
                callout.style.unityTextAlign = TextAnchor.UpperRight;
            }
            callout.style.left = labelLeftPx;
            callout.style.top = slot.LabelTopPx;
            callout.style.width = 285f;
            callout.style.fontSize = 19;
            diagram.Add(callout);
            callouts.Add(callout);

            // leader line from the label to the part, drawn under the parts
            float anchorXPx = RightAnchorXPx;
            if (slot.IsOnLeft)
            {
                anchorXPx = LeftAnchorXPx;
            }
            float anchorYPx = slot.LabelTopPx + CalloutHeightPx / 2;
            float deltaXPx = slot.XPx - anchorXPx;
            float deltaYPx = slot.YPx - anchorYPx;
            float lengthPx = Mathf.Sqrt(deltaXPx * deltaXPx + deltaYPx * deltaYPx);
            float angleDeg = Mathf.Atan2(deltaYPx, deltaXPx) * Mathf.Rad2Deg;
            VisualElement line = new VisualElement();
            line.AddToClassList("pad-line");
            line.pickingMode = PickingMode.Ignore;
            line.style.left = anchorXPx;
            line.style.top = anchorYPx - 1;
            line.style.width = lengthPx;
            Angle lineAngle = new Angle(angleDeg, AngleUnit.Degree);
            line.style.rotate = new Rotate(lineAngle);
            diagram.Insert(1, line);  // above the body, below the buttons and sticks
            leaderLines.Add(line);
        }

        /// <summary>Rebuilds the table: one row per robot action with its controller and keyboard inputs.</summary>
        private void RebuildTable(ControllerFamily family)
        {
            foreach (VisualElement row in tableRows)
            {
                row.RemoveFromHierarchy();
            }
            tableRows.Clear();
            IReadOnlyList<ControlEntry> entries = RobotControlsLayout.GetUsedEntries();
            foreach (ControlEntry entry in entries)
            {
                VisualElement row = MakeRow(entry, family);
                table.Add(row);
                tableRows.Add(row);
            }
        }

        /// <summary>Makes one table row.</summary>
        private VisualElement MakeRow(ControlEntry entry, ControllerFamily family)
        {
            ControlRebinder rebinder = input.GetRebinder();
            VisualElement row = new VisualElement();
            row.AddToClassList("table-row");

            // robot action
            Label actionLabel = new Label(entry.ActionText);
            actionLabel.AddToClassList("cell");
            actionLabel.AddToClassList("cell-action");
            row.Add(actionLabel);

            // controller and keyboard inputs, each with its own reset
            string controllerText = rebinder.GetBindingText(entry.Control, InputScheme.Controller, family);
            string keyboardText = rebinder.GetBindingText(entry.Control, InputScheme.Keyboard, family);
            AddBindingCells(row, entry, InputScheme.Controller, controllerText);
            AddBindingCells(row, entry, InputScheme.Keyboard, keyboardText);
            return row;
        }

        /// <summary>Adds a binding button and its reset button to a row.</summary>
        private void AddBindingCells(VisualElement row, ControlEntry entry, InputScheme scheme, string text)
        {
            Button bindButton = new Button();
            bindButton.text = text;
            bindButton.AddToClassList("bind-button");
            string key = MakeKey(entry.Control, scheme);
            bindButton.userData = key;
            bindButton.clicked += () => StartRebind(entry, scheme);
            row.Add(bindButton);
            if (key == pendingFocusKey)
            {
                pendingFocusKey = "";
                bindButton.schedule.Execute(() => bindButton.Focus());
            }

            Button resetButton = new Button();
            resetButton.text = "x";
            resetButton.AddToClassList("bind-button");
            resetButton.AddToClassList("reset-button");
            resetButton.clicked += () => ResetBinding(entry, scheme);
            row.Add(resetButton);
        }

        /// <summary>Starts listening for the player's next input for one binding.</summary>
        private void StartRebind(ControlEntry entry, InputScheme scheme)
        {
            ControlRebinder rebinder = input.GetRebinder();
            pendingFocusKey = MakeKey(entry.Control, scheme);
            rebinder.StartRebind(entry.Control, scheme, entry.ActionText, HandleRebindFinished);
        }

        /// <summary>Puts one binding back to its default.</summary>
        private void ResetBinding(ControlEntry entry, InputScheme scheme)
        {
            ControlRebinder rebinder = input.GetRebinder();
            rebinder.ResetBinding(entry.Control, scheme);
            Refresh();
        }

        /// <summary>Refreshes the screen after a rebind ends.</summary>
        private void HandleRebindFinished()
        {
            Refresh();
        }

        /// <summary>Flips or restores the drive stick's Y axis.</summary>
        private void HandleInvertChanged(ChangeEvent<bool> change)
        {
            input.SetLeftStickYInverted(change.newValue);
        }

        // ---- low-level helpers (static, no side effects)

        /// <summary>The robot actions of some virtual controls as one text; empty when the robot ignores them
        /// all.</summary>
        private static string MakeActionText(List<VirtualControl> controls)
        {
            string text = "";
            foreach (VirtualControl control in controls)
            {
                if (!RobotControlsLayout.IsUsed(control))
                {
                    continue;
                }
                ControlEntry entry = RobotControlsLayout.GetEntry(control);
                if (text.Length > 0)
                {
                    text = text + "; ";
                }
                text = text + entry.ActionText;
            }
            return text;
        }

        /// <summary>Identifies a binding button.</summary>
        private static string MakeKey(VirtualControl control, InputScheme scheme)
        {
            return control + "/" + scheme;
        }

        /// <summary>The controller column heading for a family.</summary>
        private static string MakeDeviceHeading(ControllerFamily family)
        {
            if (family == ControllerFamily.PlayStation)
            {
                return "PlayStation";
            }
            if (family == ControllerFamily.Generic)
            {
                return "Gamepad";
            }
            return "Xbox";
        }
    }
}
