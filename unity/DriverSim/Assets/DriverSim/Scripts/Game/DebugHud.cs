using DriverSim.Field;
using DriverSim.Net;
using DriverSim.Robot;
using DriverSim.Util;
using UnityEngine;

namespace DriverSim.Game
{
    /// <summary>
    /// Plain overlay: match clock and phase across the top, connection and robot details in a corner panel (F1), a hood
    /// warning under the clock while a trench bar blocks the robot, and a one-line key reminder at the bottom. The
    /// whole overlay hides while a menu screen is open. The full game HUD replaces the top bar in a later phase. Layout
    /// is in points of a 900-point-tall virtual screen, scaled to the window.
    /// </summary>
    public sealed class DebugHud : MonoBehaviour
    {
        private const float VirtualHeight = 900f; // virtual screen height the layout is drawn in, points
        private const string TrenchWarning = "HOOD UP - LOWER IT FOR THE TRENCH";
        private const string KeyHelp =
            "Esc pause   F1 details   F2 colliders   F3 view   F4 F5 FOV   F6 reset robot   F10 quit";

        [Tooltip("Link to the bridge.")]
        [SerializeField] private DriverSimLink link;

        [Tooltip("Camera rig, for the current view name.")]
        [SerializeField] private DriverStationCamera driverCamera;

        [Tooltip("Collision resolver, for the warning and the details panel.")]
        [SerializeField] private RobotCollision collision;

        [Tooltip("Drawn robot, for the bump readout.")]
        [SerializeField] private RobotModel robotModel;

        [Tooltip("Frame timing, for the performance line.")]
        [SerializeField] private FrameRateLog frameRate;

        private bool isVisible = true; // overlay shown at all; false while a menu screen is open
        private bool isDetailVisible = true; // corner panel shown
        private GUIStyle clockStyle; // big match clock; made on the first OnGUI (GUI.skin exists only there)
        private GUIStyle panelStyle; // corner panel text
        private GUIStyle footerStyle; // key reminder
        private GUIStyle warningStyle; // trench warning

        /// <summary>Sets the references the overlay needs; call before the scene starts.</summary>
        /// <param name="driverSimLink">Link to the bridge.</param>
        /// <param name="cameraRig">Driver camera.</param>
        /// <param name="robotCollision">Collision resolver.</param>
        /// <param name="model">Drawn robot.</param>
        /// <param name="frameRateLog">Frame timing.</param>
        public void SetReferences(DriverSimLink driverSimLink, DriverStationCamera cameraRig,
            RobotCollision robotCollision, RobotModel model, FrameRateLog frameRateLog)
        {
            robotModel = model;
            frameRate = frameRateLog;
            link = driverSimLink;
            driverCamera = cameraRig;
            collision = robotCollision;
        }

        /// <summary>Shows or hides the whole overlay.</summary>
        /// <param name="shouldShow">True to draw the overlay.</param>
        public void SetVisible(bool shouldShow)
        {
            isVisible = shouldShow;
        }

        /// <summary>Whether the detail panel is shown (when the overlay is).</summary>
        public bool IsDetailVisible()
        {
            return isDetailVisible;
        }

        /// <summary>Shows or hides the detail panel.</summary>
        /// <param name="shouldShow">True to show the panel.</param>
        public void SetDetailVisible(bool shouldShow)
        {
            isDetailVisible = shouldShow;
        }

        // ---- Unity messages

        private void OnGUI()
        {
            if (link == null || !isVisible)
            {
                return;
            }
            if (clockStyle == null)
            {
                MakeStyles();
            }

            // draw in a virtual 900-point-tall screen
            float scale = Screen.height / VirtualHeight;
            Vector3 scaleVector = new Vector3(scale, scale, 1);
            GUI.matrix = Matrix4x4.Scale(scaleVector);
            float width = Screen.width / scale;
            float height = Screen.height / scale;

            // clock, details, key help
            DrawClock(width);
            if (collision != null && collision.IsTrenchBlocked())
            {
                DrawTrenchWarning(width);
            }
            if (isDetailVisible)
            {
                DrawDetails();
            }
            GUI.Label(new Rect(10, height - 28, width - 20, 24), KeyHelp, footerStyle);
        }

        // ---- private stateful helpers

        /// <summary>Draws the match phase and clock across the top center.</summary>
        /// <param name="width">Virtual screen width, points.</param>
        private void DrawClock(float width)
        {
            MatchState match = link.GetMatchState();
            string clockText = MakeClockText(match);
            if (!link.IsConnected())
            {
                clockText = "WAITING FOR ROBOT SIM";
            }
            Rect box = new Rect(width / 2 - 230, 8, 460, 52);
            GUI.Box(box, GUIContent.none);
            GUI.Label(box, clockText, clockStyle);
        }

        /// <summary>Draws the hood warning centered under the clock.</summary>
        /// <param name="width">Virtual screen width, points.</param>
        private void DrawTrenchWarning(float width)
        {
            Rect box = new Rect(width / 2 - 300, 66, 600, 40);
            GUI.Box(box, GUIContent.none);
            GUI.Label(box, TrenchWarning, warningStyle);
        }

        /// <summary>Draws the connection, station, robot and hub details in the top-left corner.</summary>
        private void DrawDetails()
        {
            RobotState robot = link.GetRobotState();
            MatchState match = link.GetMatchState();

            // connection
            string connection = "disconnected (ws://" + link.GetNtHost() + ":5810)";
            if (link.IsConnected())
            {
                connection = "connected";
            }
            string mismatch = "";
            if (link.IsProtocolMismatch())
            {
                mismatch = "PROTOCOL MISMATCH: " + link.GetBridgeInfo() + "\n";
            }

            // station and view
            string stationName = DriverStations.GetName(match.Station);
            DriverStationCamera.View view = driverCamera.GetView();

            // robot
            string robotLine = "no robot state";
            if (link.HasRobotState())
            {
                robotLine = MakeRobotLine(robot);
            }
            string estimateLine = MakeEstimateLine(robot);
            double turretDeg = robot.TurretRobotRelRad * Mathf.Rad2Deg;
            string mechanismLine = string.Format("turret {0:F0} deg  hood {1:F0} deg  flywheel {2:F0} rpm", turretDeg,
                robot.HoodDeg, robot.FlywheelRpm);
            string intakeLine = string.Format("intake {0:F0} deg  roller {1:F1} V  feed {2:F1} V  climber {3:F2}",
                robot.IntakeArmDeg, robot.IntakeRollerVolts, robot.SpindexFeedVolts, robot.ClimberExtension);

            // hubs
            string blueHub = MakeActiveText(match.IsBlueHubActive);
            string redHub = MakeActiveText(match.IsRedHubActive);

            // performance: frame rate and the share of each frame the cpu and gpu spend working
            string performanceLine = string.Format("frame rate {0:F0} fps  cpu {1:F0}%  gpu {2:F0}%",
                frameRate.GetFramesPerS(), frameRate.GetCpuPercent(), frameRate.GetGpuPercent());

            string collisionLine = MakeCollisionLine() + "\n" + MakeBumpLine();
            string text = "Fieldrunner\n"
                + performanceLine + "\n"
                + "robot sim: " + connection + "\n"
                + mismatch
                + "station: " + stationName + "   view: " + view + "\n"
                + robotLine + "\n"
                + estimateLine + "\n"
                + mechanismLine + "\n"
                + intakeLine + "\n"
                + collisionLine + "\n"
                + "hubs: blue " + blueHub + "   red " + redHub;
            GUI.Box(new Rect(8, 8, 470, 232), GUIContent.none);
            GUI.Label(new Rect(16, 12, 460, 224), text, panelStyle);
        }

        /// <summary>How far the robot code's own pose estimate is from where the robot really is, in one line.</summary>
        /// <param name="robot">Latest robot state, which carries both poses.</param>
        private string MakeEstimateLine(RobotState robot)
        {
            if (!link.HasRobotState())
            {
                return "estimate error: n/a";
            }
            double dxM = robot.EstimateXFieldM - robot.XFieldM;
            double dyM = robot.EstimateYFieldM - robot.YFieldM;
            double errorM = System.Math.Sqrt(dxM * dxM + dyM * dyM);
            double headingErrorRad = robot.EstimateHeadingFieldRad - robot.HeadingFieldRad;
            double headingErrorDeg = Mathf.DeltaAngle(0, (float)(headingErrorRad * Mathf.Rad2Deg));
            return string.Format("estimate error: {0:F2} m, {1:F1} deg", errorM, headingErrorDeg);
        }

        /// <summary>Collision state and the correction last sent, in one line.</summary>
        private string MakeCollisionLine()
        {
            if (collision == null || !collision.HasConstrainedPose())
            {
                return "collision: no robot state";
            }
            RobotState robot = link.GetRobotState();
            string stateText = "clear";
            if (collision.IsTrenchBlocked())
            {
                stateText = "TRENCH BLOCKED";
            }
            else if (collision.IsColliding())
            {
                stateText = "colliding";
            }
            double dxM = collision.GetConstrainedXFieldM() - robot.XFieldM;
            double dyM = collision.GetConstrainedYFieldM() - robot.YFieldM;
            return string.Format("collision: {0}  correction ({1:F3}, {2:F3}) m  sent {3}", stateText, dxM, dyM,
                link.GetCorrectionCount());
        }

        /// <summary>How the drawn robot is raised and tilted by bumps, in one line.</summary>
        private string MakeBumpLine()
        {
            if (robotModel == null)
            {
                return "bump: n/a";
            }
            return string.Format("bump: rise {0:F3} m  nose up {1:F1} deg  right up {2:F1} deg",
                robotModel.GetBumpRiseM(), robotModel.GetBumpNoseUpDeg(), robotModel.GetBumpRightUpDeg());
        }

        /// <summary>Makes the overlay's text styles from the current GUI skin.</summary>
        private void MakeStyles()
        {
            clockStyle = new GUIStyle(GUI.skin.label);
            clockStyle.fontSize = 30;
            clockStyle.fontStyle = FontStyle.Bold;
            clockStyle.alignment = TextAnchor.MiddleCenter;
            clockStyle.normal.textColor = Color.white;
            panelStyle = new GUIStyle(GUI.skin.label);
            panelStyle.fontSize = 15;
            panelStyle.normal.textColor = Color.white;
            warningStyle = new GUIStyle(GUI.skin.label);
            warningStyle.fontSize = 24;
            warningStyle.fontStyle = FontStyle.Bold;
            warningStyle.alignment = TextAnchor.MiddleCenter;
            warningStyle.normal.textColor = new Color(1f, 0.85f, 0.1f);
            footerStyle = new GUIStyle(GUI.skin.label);
            footerStyle.fontSize = 14;
            footerStyle.normal.textColor = new Color(1, 1, 1, 0.8f);
        }

        // ---- low-level helpers (static, no side effects)

        /// <summary>Phase name plus m:ss clock, e.g. "TELEOP   2:15".</summary>
        private static string MakeClockText(MatchState match)
        {
            string phaseText = "FREE PLAY";
            switch (match.Phase)
            {
                case MatchPhase.Auto: phaseText = "AUTO"; break;
                case MatchPhase.AutoToTeleop: phaseText = "GET READY"; break;
                case MatchPhase.Teleop: phaseText = "TELEOP"; break;
                case MatchPhase.PostMatch: phaseText = "MATCH OVER"; break;
            }
            if (match.MatchTimeS < 0)
            {
                return phaseText;
            }
            int seconds = Mathf.CeilToInt((float)match.MatchTimeS);
            int minutes = seconds / 60;
            string secondsText = (seconds % 60).ToString("00");
            return phaseText + "   " + minutes + ":" + secondsText;
        }

        /// <summary>Enable state, mode and pose in one line.</summary>
        private static string MakeRobotLine(RobotState robot)
        {
            string enableText = "disabled";
            if (robot.IsEnabled)
            {
                enableText = "ENABLED";
            }
            string modeText = "";
            if (robot.IsAutonomous)
            {
                modeText = " auto";
            }
            double headingDeg = robot.HeadingFieldRad * Mathf.Rad2Deg;
            return string.Format("{0}{1}  pose ({2:F2}, {3:F2}) m  {4:F0} deg", enableText, modeText, robot.XFieldM,
                robot.YFieldM, headingDeg);
        }

        /// <summary>"ACTIVE" or "off".</summary>
        private static string MakeActiveText(bool isActive)
        {
            if (isActive)
            {
                return "ACTIVE";
            }
            return "off";
        }
    }
}
