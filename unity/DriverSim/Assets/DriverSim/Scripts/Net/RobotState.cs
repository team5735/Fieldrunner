using UnityEngine;

namespace DriverSim.Net
{
    /// <summary>
    /// Robot pose and mechanism state at one instant, in the robot code's units and WPILib field frame.
    /// </summary>
    public struct RobotState
    {
        public double TimestampS; // robot clock, seconds
        public double XM; // field frame (wpiBlue origin), meters
        public double YM; // field frame, meters
        public double HeadingRad; // field frame, CCW positive
        public double VelocityXMps; // field frame, meters/s
        public double VelocityYMps; // field frame, meters/s
        public double OmegaRadps; // CCW positive
        public double TurretRobotRelRad; // CCW positive, 0 = robot front
        public double HoodDeg; // launch angle
        public double FlywheelRpm;
        public double IntakeArmDeg; // above horizontal
        public double IntakeRollerV; // positive pulls in
        public double SpindexFeedV; // negative feeds the launcher
        public double SpindexWheelV;
        public double ClimberExtension; // 0..1
        public bool IsEnabled;
        public bool IsAutonomous;

        /// <summary>
        /// Decodes the bridge's robot-state array.
        /// </summary>
        /// <param name="values">Values laid out per <see cref="RobotStateLayout"/>.</param>
        /// <param name="state">Decoded state when the array is valid.</param>
        /// <returns>False when the array is too short.</returns>
        public static bool TryParse(double[] values, out RobotState state)
        {
            state = default;
            if (values == null || values.Length < RobotStateLayout.Length)
            {
                return false;
            }
            state.TimestampS = values[RobotStateLayout.TimestampS];
            state.XM = values[RobotStateLayout.PoseXM];
            state.YM = values[RobotStateLayout.PoseYM];
            state.HeadingRad = values[RobotStateLayout.HeadingRad];
            state.VelocityXMps = values[RobotStateLayout.VelocityXMps];
            state.VelocityYMps = values[RobotStateLayout.VelocityYMps];
            state.OmegaRadps = values[RobotStateLayout.OmegaRadps];
            state.TurretRobotRelRad = values[RobotStateLayout.TurretRobotRelRad];
            state.HoodDeg = values[RobotStateLayout.HoodDeg];
            state.FlywheelRpm = values[RobotStateLayout.FlywheelRpm];
            state.IntakeArmDeg = values[RobotStateLayout.IntakeArmDeg];
            state.IntakeRollerV = values[RobotStateLayout.IntakeRollerV];
            state.SpindexFeedV = values[RobotStateLayout.SpindexFeedV];
            state.SpindexWheelV = values[RobotStateLayout.SpindexWheelV];
            state.ClimberExtension = values[RobotStateLayout.ClimberExtension];
            state.IsEnabled = values[RobotStateLayout.IsEnabled] > 0.5;
            state.IsAutonomous = values[RobotStateLayout.IsAutonomous] > 0.5;
            return true;
        }

        /// <summary>
        /// Blends two states: continuous values interpolate (headings along the short way round), flags come from
        /// the later state.
        /// </summary>
        /// <param name="a">Earlier state.</param>
        /// <param name="b">Later state.</param>
        /// <param name="t">Blend fraction, 0 = a, 1 = b.</param>
        public static RobotState Lerp(RobotState a, RobotState b, double t)
        {
            RobotState s = b;
            s.TimestampS = a.TimestampS + (b.TimestampS - a.TimestampS) * t;
            s.XM = a.XM + (b.XM - a.XM) * t;
            s.YM = a.YM + (b.YM - a.YM) * t;
            s.HeadingRad = LerpAngle(a.HeadingRad, b.HeadingRad, t);
            s.VelocityXMps = a.VelocityXMps + (b.VelocityXMps - a.VelocityXMps) * t;
            s.VelocityYMps = a.VelocityYMps + (b.VelocityYMps - a.VelocityYMps) * t;
            s.OmegaRadps = a.OmegaRadps + (b.OmegaRadps - a.OmegaRadps) * t;
            s.TurretRobotRelRad = LerpAngle(a.TurretRobotRelRad, b.TurretRobotRelRad, t);
            s.HoodDeg = a.HoodDeg + (b.HoodDeg - a.HoodDeg) * t;
            s.FlywheelRpm = a.FlywheelRpm + (b.FlywheelRpm - a.FlywheelRpm) * t;
            s.IntakeArmDeg = a.IntakeArmDeg + (b.IntakeArmDeg - a.IntakeArmDeg) * t;
            s.ClimberExtension = a.ClimberExtension + (b.ClimberExtension - a.ClimberExtension) * t;
            return s;
        }

        private static double LerpAngle(double a, double b, double t)
        {
            double delta = Mathf.DeltaAngle((float)(a * Mathf.Rad2Deg), (float)(b * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
            return a + delta * t;
        }
    }

    /// <summary>
    /// What the bridge's field system says about the match.
    /// </summary>
    public struct MatchState
    {
        public MatchPhase Phase;
        public double MatchTimeS; // seconds left in the period; -1 with no match
        public bool IsBlueHubActive;
        public bool IsRedHubActive;
        public int Station; // 0..2 Blue 1..3, 3..5 Red 1..3, -1 unknown
        public int AutoWinner; // 0 unknown, 1 blue, 2 red

        /// <summary>True when the driver station is on the red alliance.</summary>
        public bool IsRed
        {
            get { return Station >= 3; }
        }

        /// <summary>
        /// Decodes the bridge's match-state array.
        /// </summary>
        /// <param name="values">Values laid out per <see cref="MatchStateLayout"/>.</param>
        /// <param name="state">Decoded state when the array is valid.</param>
        /// <returns>False when the array is too short.</returns>
        public static bool TryParse(double[] values, out MatchState state)
        {
            state = default;
            if (values == null || values.Length < MatchStateLayout.Length)
            {
                return false;
            }
            state.Phase = (MatchPhase)(int)values[MatchStateLayout.Phase];
            state.MatchTimeS = values[MatchStateLayout.MatchTimeS];
            state.IsBlueHubActive = values[MatchStateLayout.IsBlueHubActive] > 0.5;
            state.IsRedHubActive = values[MatchStateLayout.IsRedHubActive] > 0.5;
            state.Station = (int)values[MatchStateLayout.Station];
            state.AutoWinner = (int)values[MatchStateLayout.AutoWinner];
            return true;
        }
    }
}
