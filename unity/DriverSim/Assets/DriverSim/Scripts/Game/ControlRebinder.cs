using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DriverSim.Game
{
    /// <summary>
    /// Changes and saves the bindings of the virtual controls: listens for the next input the player presses, writes it
    /// as a binding override, and keeps the overrides in a JSON file. Works on one action asset; the owner disables the
    /// asset's actions while a rebind runs (<see cref="StartRebind"/> does it).
    /// </summary>
    public sealed class ControlRebinder
    {
        private const float MatchWaitS = 0.05f; // seconds to wait for a better match, so a chord picks its main key
        private const string CancelPath = "<Keyboard>/escape"; // pressing this cancels a rebind
        private static readonly string[] LeftStickKeyNames =
        {
            "drive forward", "drive back", "drive left", "drive right",
        }; // left stick key prompts, in the 2D composite's up/down/left/right order

        private readonly InputActionAsset actions; // asset whose bindings are changed
        private readonly string savePath; // JSON file the overrides are saved to
        private InputActionRebindingExtensions.RebindingOperation operation; // the rebind in progress, or null
        private readonly List<int> pendingIndices = new List<int>(); // binding indices still to rebind in this request
        private InputAction pendingAction; // action being rebound
        private InputScheme pendingScheme; // scheme being rebound
        private string pendingActionText; // robot action text for the prompt
        private Action onFinished; // called when the whole request ends, whether finished or cancelled
        private int totalPartCount; // binding indices in the current request
        private int lastFinishFrame = -1; // Time.frameCount when the last rebind ended

        /// <summary>Creates a rebinder for an asset.</summary>
        /// <param name="inputActions">Asset whose bindings are changed.</param>
        /// <param name="overridesFilePath">JSON file for the saved overrides.</param>
        public ControlRebinder(InputActionAsset inputActions, string overridesFilePath)
        {
            actions = inputActions;
            savePath = overridesFilePath;
        }

        /// <summary>Whether a rebind is waiting for the player's input.</summary>
        public bool IsRebinding()
        {
            return operation != null;
        }

        /// <summary>Whether a rebind ended in the current frame (its cancel key may also mean "go back").</summary>
        public bool HasFinishedThisFrame()
        {
            return lastFinishFrame == Time.frameCount;
        }

        /// <summary>The prompt to show while rebinding, e.g. "Press a button for face hub... (Esc to
        /// cancel)".</summary>
        public string GetPromptText()
        {
            if (operation == null)
            {
                return "";
            }
            string what = pendingActionText;
            if (totalPartCount > 1)
            {
                int partIndex = totalPartCount - pendingIndices.Count;
                what = LeftStickKeyNames[partIndex];
            }
            string device = "button";
            if (pendingScheme == InputScheme.Keyboard)
            {
                device = "key";
            }
            return "Press a " + device + " for " + what + "... (Esc to cancel)";
        }

        /// <summary>
        /// Starts listening for the player's next input and rebinds a control to it. The left stick's keyboard keys are
        /// rebound as four keys in turn (forward, back, left, right).
        /// </summary>
        /// <param name="control">Virtual control to rebind.</param>
        /// <param name="scheme">Which scheme's binding to change.</param>
        /// <param name="actionText">What the control does, for the prompt.</param>
        /// <param name="whenFinished">Called when the rebind has finished or was cancelled.</param>
        public void StartRebind(VirtualControl control, InputScheme scheme, string actionText, Action whenFinished)
        {
            if (operation != null)
            {
                return;
            }
            string actionName = VirtualControls.GetActionName(control);
            InputAction action = actions.FindAction(VirtualControls.ActionMapName + "/" + actionName);
            List<int> indices = FindBindingIndices(action, scheme);
            if (indices.Count == 0)
            {
                return;
            }
            pendingAction = action;
            pendingScheme = scheme;
            pendingActionText = actionText;
            onFinished = whenFinished;
            pendingIndices.Clear();
            pendingIndices.AddRange(indices);
            totalPartCount = indices.Count;
            actions.Disable();
            RebindNextIndex();
        }

        /// <summary>Stops a rebind in progress without changing anything more.</summary>
        public void CancelRebind()
        {
            if (operation != null)
            {
                operation.Cancel();
            }
        }

        /// <summary>The player-facing text of a control's current binding in a scheme, e.g. "Left Alt" or "W A S
        /// D".</summary>
        /// <param name="control">Virtual control.</param>
        /// <param name="scheme">Scheme to describe.</param>
        /// <param name="family">Naming family for controller inputs.</param>
        public string GetBindingText(VirtualControl control, InputScheme scheme, ControllerFamily family)
        {
            string actionName = VirtualControls.GetActionName(control);
            InputAction action = actions.FindAction(VirtualControls.ActionMapName + "/" + actionName);
            List<int> indices = FindBindingIndices(action, scheme);
            string text = "";
            foreach (int index in indices)
            {
                string path = action.bindings[index].effectivePath;
                string name = ControlLabels.GetPathName(path, family);
                if (text.Length > 0)
                {
                    text = text + " ";
                }
                text = text + name;
            }
            return text;
        }

        /// <summary>Finds the virtual controls whose controller binding is a given physical input.</summary>
        /// <param name="path">Input path such as "&lt;Gamepad&gt;/buttonSouth".</param>
        /// <returns>The controls bound to it; empty when none is.</returns>
        public List<VirtualControl> FindControlsBoundTo(string path)
        {
            List<VirtualControl> found = new List<VirtualControl>();
            foreach (VirtualControl control in VirtualControls.GetAll())
            {
                string actionName = VirtualControls.GetActionName(control);
                InputAction action = actions.FindAction(VirtualControls.ActionMapName + "/" + actionName);
                List<int> indices = FindBindingIndices(action, InputScheme.Controller);
                foreach (int index in indices)
                {
                    string effectivePath = action.bindings[index].effectivePath;
                    if (string.Equals(effectivePath, path, StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(control);
                    }
                }
            }
            return found;
        }

        /// <summary>Removes the player's changes to one control's binding in a scheme and saves.</summary>
        /// <param name="control">Virtual control.</param>
        /// <param name="scheme">Scheme to reset.</param>
        public void ResetBinding(VirtualControl control, InputScheme scheme)
        {
            string actionName = VirtualControls.GetActionName(control);
            InputAction action = actions.FindAction(VirtualControls.ActionMapName + "/" + actionName);
            List<int> indices = FindBindingIndices(action, scheme);
            foreach (int index in indices)
            {
                action.RemoveBindingOverride(index);
            }
            SaveOverrides();
        }

        /// <summary>Removes every change the player made and deletes the saved file.</summary>
        public void ResetAll()
        {
            actions.RemoveAllBindingOverrides();
            if (File.Exists(savePath))
            {
                File.Delete(savePath);
            }
        }

        /// <summary>Whether any controller binding points at a joystick (an unrecognized HID device) rather than a
        /// gamepad.</summary>
        public bool HasJoystickBindings()
        {
            foreach (InputAction action in actions)
            {
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    string path = action.bindings[i].effectivePath;
                    if (!string.IsNullOrEmpty(path) && path.StartsWith("<Joystick>"))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Loads the saved overrides, if the file exists.</summary>
        public void LoadOverrides()
        {
            if (!File.Exists(savePath))
            {
                return;
            }
            string json = File.ReadAllText(savePath);
            actions.LoadBindingOverridesFromJson(json);
        }

        // ---- private stateful helpers

        /// <summary>Writes the current overrides to the save file.</summary>
        private void SaveOverrides()
        {
            string json = actions.SaveBindingOverridesAsJson();
            File.WriteAllText(savePath, json);
        }

        /// <summary>Starts listening for the next binding index of the current request, or ends the request.</summary>
        private void RebindNextIndex()
        {
            if (pendingIndices.Count == 0)
            {
                EndRequest();
                return;
            }
            int bindingIndex = pendingIndices[0];
            pendingIndices.RemoveAt(0);
            InputActionRebindingExtensions.RebindingOperation rebind =
                pendingAction.PerformInteractiveRebinding(bindingIndex);
            rebind = rebind.WithCancelingThrough(CancelPath);
            rebind = rebind.WithControlsExcluding("<Mouse>");
            if (pendingScheme == InputScheme.Keyboard)
            {
                rebind = rebind.WithControlsHavingToMatchPath("<Keyboard>");
            }
            else
            {
                rebind = rebind.WithControlsHavingToMatchPath("<Gamepad>");
                rebind = rebind.WithControlsHavingToMatchPath("<Joystick>");
            }
            rebind = rebind.OnMatchWaitForAnother(MatchWaitS);
            rebind = rebind.OnComplete(HandlePartComplete);
            rebind = rebind.OnCancel(HandleCancel);
            operation = rebind;
            operation.Start();
        }

        /// <summary>One binding was rebound: go on to the next one, if any.</summary>
        private void HandlePartComplete(InputActionRebindingExtensions.RebindingOperation finished)
        {
            finished.Dispose();
            operation = null;
            SaveOverrides();
            RebindNextIndex();
        }

        /// <summary>The player cancelled: end the whole request.</summary>
        private void HandleCancel(InputActionRebindingExtensions.RebindingOperation cancelled)
        {
            cancelled.Dispose();
            operation = null;
            pendingIndices.Clear();
            SaveOverrides();
            EndRequest();
        }

        /// <summary>Ends the request and tells the owner.</summary>
        private void EndRequest()
        {
            operation = null;
            lastFinishFrame = Time.frameCount;
            actions.Enable();
            Action callback = onFinished;
            onFinished = null;
            if (callback != null)
            {
                callback();
            }
        }

        // ---- low-level helpers (static, no side effects)

        /// <summary>
        /// The indices of an action's bindings in one scheme, in order. A composite (the keyboard drive keys) gives its
        /// part bindings; the composite itself is skipped.
        /// </summary>
        /// <param name="action">Action to search.</param>
        /// <param name="scheme">Scheme whose bindings are wanted.</param>
        private static List<int> FindBindingIndices(InputAction action, InputScheme scheme)
        {
            List<int> indices = new List<int>();
            string group = VirtualControls.GetGroupName(scheme);
            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (binding.isComposite)
                {
                    continue;
                }
                if (string.IsNullOrEmpty(binding.groups))
                {
                    continue;
                }
                string[] groups = binding.groups.Split(';');
                if (Array.IndexOf(groups, group) >= 0)
                {
                    indices.Add(i);
                }
            }
            return indices;
        }
    }
}
