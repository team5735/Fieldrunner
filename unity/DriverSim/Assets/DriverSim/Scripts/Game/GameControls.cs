using DriverSim.Net;
using DriverSim.Robot;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DriverSim.Game
{
    /// <summary>
    /// Keyboard hotkeys for the game itself, kept off the keys the robot uses: F1 details panel, F2 colliders, F3
    /// camera view, F4 / F5 field of view narrower / wider, F6 reset robot, F7 respawn fuel (free practice), F8 drop a
    /// ball a meter in front of the robot (development builds), F10 quit. Also watches the controller pause chord:
    /// hold Start, then Back, for half a second. (Esc is handled by <see cref="GameFlow"/>.)
    /// </summary>
    public sealed class GameControls : MonoBehaviour
    {
        private const float ChordHoldS = 0.5f; // seconds Start and Back are held together to pause
        private const float DropAheadM = 1f; // meters in front of the robot that F8 drops a ball

        [Tooltip("Game state, for pausing and resetting the robot.")]
        [SerializeField] private GameFlow flow;

        [Tooltip("The virtual controller, for the pause chord.")]
        [SerializeField] private ControllerInput input;

        [Tooltip("Display and camera settings the hotkeys change.")]
        [SerializeField] private GameOptions options;

        [Tooltip("The fuel on the field, for F8.")]
        [SerializeField] private FuelField fuelField;

        [Tooltip("The drawn robot, for F8.")]
        [SerializeField] private RobotModel robotModel;

        private float chordHeldS; // seconds Start and Back have been held together, Start first
        private bool isChordArmed; // Start has been held since before Back was pressed

        /// <summary>Sets the objects the hotkeys act on; call before the scene starts.</summary>
        /// <param name="gameFlow">Game state.</param>
        /// <param name="controllerInput">The virtual controller.</param>
        /// <param name="gameOptions">Display and camera settings.</param>
        public void SetReferences(GameFlow gameFlow, ControllerInput controllerInput, GameOptions gameOptions)
        {
            flow = gameFlow;
            input = controllerInput;
            options = gameOptions;
        }

        /// <summary>Sets the objects the fuel hotkeys act on; call before the scene starts.</summary>
        /// <param name="field">The fuel on the field.</param>
        /// <param name="model">The drawn robot.</param>
        public void SetFuelReferences(FuelField field, RobotModel model)
        {
            fuelField = field;
            robotModel = model;
        }

        // ---- Unity messages

        private void Update()
        {
            UpdateHotkeys();
            UpdatePauseChord();
        }

        // ---- private stateful helpers

        /// <summary>Acts on this frame's function-key presses.</summary>
        private void UpdateHotkeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }
            if (keyboard.f1Key.wasPressedThisFrame)
            {
                bool isShown = options.IsDetailsShown();
                options.SetDetailsShown(!isShown);
            }
            if (keyboard.f2Key.wasPressedThisFrame)
            {
                bool areShown = options.AreCollidersShown();
                options.SetCollidersShown(!areShown);
            }
            if (keyboard.f3Key.wasPressedThisFrame)
            {
                options.CycleView();
            }
            if (keyboard.f4Key.wasPressedThisFrame)
            {
                options.StepFieldOfView(-1);
            }
            if (keyboard.f5Key.wasPressedThisFrame)
            {
                options.StepFieldOfView(1);
            }
            if (keyboard.f6Key.wasPressedThisFrame)
            {
                flow.ResetRobot();
            }
            if (keyboard.f7Key.wasPressedThisFrame)
            {
                flow.RespawnFuel();
            }
            if (keyboard.f8Key.wasPressedThisFrame && Debug.isDebugBuild)
            {
                DropFuelAheadOfRobot();
            }
            if (keyboard.f10Key.wasPressedThisFrame)
            {
                Application.Quit();
            }
        }

        /// <summary>Puts one fuel ball on the carpet a meter in front of the drawn robot.</summary>
        private void DropFuelAheadOfRobot()
        {
            Transform body = robotModel.GetBody();
            Vector3 flatForward = new Vector3(body.forward.x, 0, body.forward.z).normalized;
            Vector3 position = body.position + flatForward * DropAheadM;
            position.y = ShotModel.BallRadiusM;
            fuelField.SpawnResting(position);
        }

        /// <summary>Pauses when Start, then Back, have been held together for <see cref="ChordHoldS"/>.</summary>
        private void UpdatePauseChord()
        {
            JoystickState raw = input.GetRawState();
            bool isStartDown = raw.IsButtonDown(VirtualControls.StartBit);
            bool isBackDown = raw.IsButtonDown(VirtualControls.BackBit);
            if (!flow.IsPlaying() || !isStartDown)
            {
                chordHeldS = 0;
                isChordArmed = false;
                return;
            }

            // Start alone arms the chord; Back pressed afterwards, with Start still held, times it
            if (!isBackDown)
            {
                chordHeldS = 0;
                isChordArmed = true;
                return;
            }
            if (isChordArmed)
            {
                chordHeldS += Time.unscaledDeltaTime;
            }
            if (chordHeldS >= ChordHoldS)
            {
                chordHeldS = 0;
                isChordArmed = false;
                flow.OpenPause();
            }
        }
    }
}
