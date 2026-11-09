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
    /// V switches camera view, [ and ] change the field of view, F1 toggles the debug overlay, F10 quits.
    /// </summary>
    public sealed class GameControls : MonoBehaviour
    {
        private const float StartLineXM = 3.56f; // robot center behind the starting line, field x from own wall, meters

        [Tooltip("Link to the bridge.")]
        public DriverSimLink link;

        [Tooltip("Camera to switch views on.")]
        public DriverStationCamera driverCamera;

        [Tooltip("Debug overlay to toggle.")]
        public DebugHud hud;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || link == null)
            {
                return;
            }

            // match control
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                PlaceRobotAtStart();
                link.SendCommand("start");
            }
            if (keyboard.backspaceKey.wasPressedThisFrame)
            {
                link.SendCommand("abort");
            }

            // driver station
            Key[] stationKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6 };
            for (int station = 0; station < stationKeys.Length; station++)
            {
                if (keyboard[stationKeys[station]].wasPressedThisFrame)
                {
                    link.SendCommand("station " + DriverStations.GetWireName(station));
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
                driverCamera.AdjustFieldOfView(-5f);
            }
            if (keyboard.rightBracketKey.wasPressedThisFrame && driverCamera != null)
            {
                driverCamera.AdjustFieldOfView(5f);
            }
            if (keyboard.f1Key.wasPressedThisFrame && hud != null)
            {
                hud.Toggle();
            }
            if (keyboard.f10Key.wasPressedThisFrame)
            {
                Application.Quit();
            }
        }

        /// <summary>
        /// Puts the robot behind its alliance's starting line, in line with the selected driver station, facing
        /// downfield.
        /// </summary>
        private void PlaceRobotAtStart()
        {
            int station = DriverStations.DefaultStation;
            if (link.Match.Station >= 0)
            {
                station = link.Match.Station;
            }
            double xM = StartLineXM;
            if (DriverStations.IsRed(station))
            {
                xM = FieldFrame.FieldLengthM - StartLineXM;
            }
            double yM = DriverStations.GetYM(station);
            double headingRad = DriverStations.GetFacingRad(station);
            link.SendCommand(string.Format(CultureInfo.InvariantCulture, "resetPose {0:F3} {1:F3} {2:F4}", xM, yM,
                headingRad));
        }
    }
}
