using System.IO;
using DriverSim.Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DriverSim.Editor
{
    /// <summary>
    /// Generates DriverControls.inputactions from <see cref="RobotControlsLayout"/>: one action per virtual Xbox
    /// control, with the default controller binding (gamepad paths, which Unity maps for Xbox, PlayStation and Switch
    /// Pro controllers alike) and the default keyboard binding, in two control schemes (Controller and Keyboard). The
    /// keyboard drive keys are a normalized W A S D composite on the left stick.
    /// </summary>
    public static class DriverControlsBuilder
    {
        public const string AssetPath = "Assets/DriverSim/Input/DriverControls.inputactions"; // generated asset

        /// <summary>Writes the actions asset (when its content changed) and loads it.</summary>
        /// <returns>The imported actions asset.</returns>
        public static InputActionAsset BuildAsset()
        {
            // the actions and their default bindings
            InputActionAsset asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "DriverControls";
            InputActionMap map = asset.AddActionMap(VirtualControls.ActionMapName);
            foreach (VirtualControl control in VirtualControls.GetAll())
            {
                AddControl(map, control);
            }
            asset.AddControlScheme(VirtualControls.ControllerGroup).WithRequiredDevice("<Gamepad>");
            asset.AddControlScheme(VirtualControls.KeyboardGroup).WithRequiredDevice("<Keyboard>");

            // write it only when it differs, so builds do not churn the file
            string json = asset.ToJson();
            Object.DestroyImmediate(asset);
            string existing = "";
            if (File.Exists(AssetPath))
            {
                existing = File.ReadAllText(AssetPath);
            }
            if (existing != json)
            {
                File.WriteAllText(AssetPath, json);
                AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            }
            InputActionAsset loaded = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);
            if (loaded == null)
            {
                throw new System.InvalidOperationException("input actions not imported: " + AssetPath);
            }
            return loaded;
        }

        /// <summary>Adds one control's action and its default bindings to the map.</summary>
        private static void AddControl(InputActionMap map, VirtualControl control)
        {
            // action type from the kind of control
            VirtualControlKind kind = VirtualControls.GetKind(control);
            InputActionType type = InputActionType.Button;
            string layout = "Button";
            if (kind == VirtualControlKind.Stick)
            {
                type = InputActionType.Value;
                layout = "Vector2";
            }
            else if (kind == VirtualControlKind.Trigger)
            {
                type = InputActionType.Value;
                layout = "Axis";
            }
            string actionName = VirtualControls.GetActionName(control);
            InputAction action = map.AddAction(actionName, type, expectedControlLayout: layout);

            // controller default
            ControlEntry entry = RobotControlsLayout.GetEntry(control);
            action.AddBinding(entry.GamepadPath, groups: VirtualControls.ControllerGroup);

            // keyboard default: a composite for the left stick, a key for the rest
            if (control == VirtualControl.LeftStick)
            {
                action.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/w", VirtualControls.KeyboardGroup)
                    .With("Down", "<Keyboard>/s", VirtualControls.KeyboardGroup)
                    .With("Left", "<Keyboard>/a", VirtualControls.KeyboardGroup)
                    .With("Right", "<Keyboard>/d", VirtualControls.KeyboardGroup);
            }
            else if (entry.KeyboardPath.Length > 0)
            {
                action.AddBinding(entry.KeyboardPath, groups: VirtualControls.KeyboardGroup);
            }
        }
    }
}
