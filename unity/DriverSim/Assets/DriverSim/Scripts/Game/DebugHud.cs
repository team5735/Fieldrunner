using DriverSim.Field;
using DriverSim.Net;
using UnityEngine;

namespace DriverSim.Game
{
    /// <summary>
    /// Plain overlay: match clock and phase across the top, connection and robot details in a corner panel (F1), and
    /// a one-line key reminder at the bottom. The full game HUD replaces the top bar in a later phase.
    /// </summary>
    public sealed class DebugHud : MonoBehaviour
    {
        [Tooltip("Link to the bridge.")]
        public DriverSimLink link;

        [Tooltip("Camera rig, for the current view name.")]
        public DriverStationCamera driverCamera;

        private bool isDetailVisible = true; // corner panel shown
        private GUIStyle clockStyle; // big match clock
        private GUIStyle panelStyle; // corner panel text
        private GUIStyle footerStyle; // key reminder

        /// <summary>Shows or hides the detail panel.</summary>
        public void Toggle()
        {
            isDetailVisible = !isDetailVisible;
        }

        private void OnGUI()
        {
            if (link == null)
            {
                return;
            }
            CreateStyles();
            float scale = Screen.height / 900f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float width = Screen.width / scale;
            float height = Screen.height / scale;

            DrawClock(width);
            if (isDetailVisible)
            {
                DrawDetails();
            }
            GUI.Label(new Rect(10, height - 28, width - 20, 24),
                "Enter start match   Backspace abort   1-6 station   R reset robot   V view   [ ] FOV   F1 details   F10 quit",
                footerStyle);
        }

        private void DrawClock(float width)
        {
            MatchState match = link.Match;
            string phaseText = "FREE PLAY";
            switch (match.Phase)
            {
                case MatchPhase.Auto: phaseText = "AUTO"; break;
                case MatchPhase.AutoToTeleop: phaseText = "GET READY"; break;
                case MatchPhase.Teleop: phaseText = "TELEOP"; break;
                case MatchPhase.PostMatch: phaseText = "MATCH OVER"; break;
            }
            string clockText = phaseText;
            if (match.MatchTimeS >= 0)
            {
                int seconds = Mathf.CeilToInt((float)match.MatchTimeS);
                clockText = phaseText + "   " + (seconds / 60) + ":" + (seconds % 60).ToString("00");
            }
            if (!link.IsConnected)
            {
                clockText = "WAITING FOR ROBOT SIM";
            }
            GUI.Box(new Rect(width / 2 - 230, 8, 460, 52), GUIContent.none);
            GUI.Label(new Rect(width / 2 - 230, 8, 460, 52), clockText, clockStyle);
        }

        private void DrawDetails()
        {
            RobotState robot = link.Robot;
            MatchState match = link.Match;
            string connection = "disconnected (ws://" + link.ntHost + ":5810)";
            if (link.IsConnected)
            {
                connection = "connected";
            }
            string robotLine = "no robot state";
            if (link.HasRobotState)
            {
                robotLine = string.Format("{0}{1}  pose ({2:F2}, {3:F2}) m  {4:F0} deg",
                    robot.IsEnabled ? "ENABLED" : "disabled", robot.IsAutonomous ? " auto" : "", robot.XM, robot.YM,
                    robot.HeadingRad * Mathf.Rad2Deg);
            }
            string text = "Driver-Sim\n"
                + "robot sim: " + connection + "\n"
                + (link.IsProtocolMismatch ? "PROTOCOL MISMATCH: " + link.BridgeInfo + "\n" : "")
                + "station: " + DriverStations.GetName(match.Station) + "   view: " + driverCamera.CurrentView + "\n"
                + robotLine + "\n"
                + string.Format("turret {0:F0} deg  hood {1:F0} deg  flywheel {2:F0} rpm\n",
                    robot.TurretRobotRelRad * Mathf.Rad2Deg, robot.HoodDeg, robot.FlywheelRpm)
                + string.Format("intake {0:F0} deg  roller {1:F1} V  feed {2:F1} V  climber {3:F2}\n",
                    robot.IntakeArmDeg, robot.IntakeRollerV, robot.SpindexFeedV, robot.ClimberExtension)
                + "hubs: blue " + (match.IsBlueHubActive ? "ACTIVE" : "off") + "   red "
                + (match.IsRedHubActive ? "ACTIVE" : "off");
            GUI.Box(new Rect(8, 8, 470, 158), GUIContent.none);
            GUI.Label(new Rect(16, 12, 460, 150), text, panelStyle);
        }

        private void CreateStyles()
        {
            if (clockStyle != null)
            {
                return;
            }
            clockStyle = new GUIStyle(GUI.skin.label);
            clockStyle.fontSize = 30;
            clockStyle.fontStyle = FontStyle.Bold;
            clockStyle.alignment = TextAnchor.MiddleCenter;
            clockStyle.normal.textColor = Color.white;
            panelStyle = new GUIStyle(GUI.skin.label);
            panelStyle.fontSize = 15;
            panelStyle.normal.textColor = Color.white;
            footerStyle = new GUIStyle(GUI.skin.label);
            footerStyle.fontSize = 14;
            footerStyle.normal.textColor = new Color(1, 1, 1, 0.8f);
        }
    }
}
