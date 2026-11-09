using System.Globalization;
using DriverSim.Field;
using DriverSim.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DriverSim.Game
{
    /// <summary>
    /// Keyboard controls for running the trainer (the robot itself is driven only through the driver station):
    /// Enter starts a match, Backspace aborts it, 1-6 pick Blue 1-3 / Red 1-3, R puts the robot at its starting spot,
    /// V switches camera view, [ and ] change the field of view, F1 toggles the debug details, F10 quits.
    /// </summary>
    public sealed class GameControls : MonoBehaviour
    {
        private const float StartLineXM = 3.56f; // meters from the own wall to the robot center behind the start line
        private const float FieldOfViewStepDeg = 5f; // degrees per [ or ] press

        private static readonly Key[] StationKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6,
        }; // keys for station indices 0..5

        [Tooltip("Link to the bridge.")]
        [SerializeField] private DriverSimLink link;

        [Tooltip("Camera to switch views on.")]
        [SerializeField] private DriverStationCamera driverCamera;

        [Tooltip("Debug overlay to toggle.")]
        [SerializeField] private DebugHud hud;

        /// <summary>Sets the references the controls need; call before the scene starts.</summary>
        /// <param name="driverSimLink">Link to the bridge.</param>
        /// <param name="cameraRig">Driver camera.</param>
        /// <param name="debugHud">Debug overlay.</param>
        public void SetReferences(DriverSimLink driverSimLink, DriverStationCamera cameraRig, DebugHud debugHud)
        {
            link = driverSimLink;
            driverCamera = cameraRig;
            hud = debugHud;
        }

        // ---- Unity messages

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || link == null)
            {
                return;
            }

            // match control
            bool isEnterPressed = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
            if (isEnterPressed)
            {
                PlaceRobotAtStart();
                link.SendCommand("start");
            }
            if (keyboard.backspaceKey.wasPressedThisFrame)
            {
                link.SendCommand("abort");
            }

            // driver station and robot placement
            for (int station = 0; station < StationKeys.Length; station++)
            {
                if (keyboard[StationKeys[station]].wasPressedThisFrame)
                {
                    string stationName = DriverStations.GetWireName(station);
                    link.SendCommand("station " + stationName);
                }
            }
            if (keyboard.rKey.wasPressedThisFrame)
            {
                PlaceRobotAtStart();
            }

            // view and overlay
            if (keyboard.vKey.wasPressedThisFrame && driverCamera != null)
            {
                driverCamera.CycleView();
            }
            if (keyboard.leftBracketKey.wasPressedThisFrame && driverCamera != null)
            {
                driverCamera.AdjustFieldOfView(-FieldOfViewStepDeg);
            }
            if (keyboard.rightBracketKey.wasPressedThisFrame && driverCamera != null)
            {
                driverCamera.AdjustFieldOfView(FieldOfViewStepDeg);
            }
            if (keyboard.f1Key.wasPressedThisFrame && hud != null)
            {
                hud.ToggleDetails();
            }
            if (keyboard.f10Key.wasPressedThisFrame)
            {
                Application.Quit();
            }
        }

        // ---- private stateful helpers

        /// <summary>
        /// Puts the robot behind its alliance's starting line, in line with the selected driver station, facing
        /// downfield.
        /// </summary>
        private void PlaceRobotAtStart()
        {
            // selected station, or the default
            int station = DriverStations.DefaultStation;
            MatchState match = link.GetMatchState();
            if (match.Station >= 0)
            {
                station = match.Station;
            }

            // start spot on that alliance's side
            double xFieldM = StartLineXM;
            if (DriverStations.IsRed(station))
            {
                xFieldM = FieldFrame.FieldLengthM - StartLineXM;
            }
            double yFieldM = DriverStations.GetYFieldM(station);
            double headingFieldRad = DriverStations.GetDownfieldHeadingRad(station);
            string command = string.Format(CultureInfo.InvariantCulture, "resetPose {0:F3} {1:F3} {2:F4}", xFieldM,
                yFieldM, headingFieldRad);
            link.SendCommand(command);
        }
    }
}
