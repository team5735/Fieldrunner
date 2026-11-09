using UnityEngine;

namespace DriverSim.Net
{
    /// <summary>
    /// Robot pose and mechanism state at one instant, in the robot code's units and WPILib's field frame (wpiBlue
    /// origin). Immutable; build it with <see cref="TryParse"/> or <see cref="Interpolate"/>.
    /// </summary>
    public readonly struct RobotState
    {
        /// <summary>Creates a state from its values; see the properties for units and frames.</summary>
        public RobotState(
            double timestampS,
            double xFieldM,
            double yFieldM,
            double headingFieldRad,
            double velocityXFieldMps,
            double velocityYFieldMps,
            double omegaRadps,
            double turretRobotRelRad,
            double hoodDeg,
            double flywheelRpm,
            double intakeArmDeg,
            double intakeRollerVolts,
            double spindexFeedVolts,
            double spindexWheelVolts,
            double climberExtension,
            bool isEnabled,
            bool isAutonomous)
        {
            TimestampS = timestampS;
            XFieldM = xFieldM;
            YFieldM = yFieldM;
            HeadingFieldRad = headingFieldRad;
            VelocityXFieldMps = velocityXFieldMps;
            VelocityYFieldMps = velocityYFieldMps;
            OmegaRadps = omegaRadps;
            TurretRobotRelRad = turretRobotRelRad;
            HoodDeg = hoodDeg;
            FlywheelRpm = flywheelRpm;
            IntakeArmDeg = intakeArmDeg;
            IntakeRollerVolts = intakeRollerVolts;
            SpindexFeedVolts = spindexFeedVolts;
            SpindexWheelVolts = spindexWheelVolts;
            ClimberExtension = climberExtension;
            IsEnabled = isEnabled;
            IsAutonomous = isAutonomous;
        }

        /// <summary>Robot clock, seconds.</summary>
        public double TimestampS { get; }

        /// <summary>Robot x, field frame, meters.</summary>
        public double XFieldM { get; }

        /// <summary>Robot y, field frame, meters.</summary>
        public double YFieldM { get; }

        /// <summary>Robot heading, field frame, radians, counter-clockwise positive.</summary>
        public double HeadingFieldRad { get; }

        /// <summary>Robot velocity along field x, meters/s.</summary>
        public double VelocityXFieldMps { get; }

        /// <summary>Robot velocity along field y, meters/s.</summary>
        public double VelocityYFieldMps { get; }

        /// <summary>Robot turn rate, radians/s, counter-clockwise positive.</summary>
        public double OmegaRadps { get; }

        /// <summary>Turret heading relative to the robot's front, radians, counter-clockwise positive.</summary>
        public double TurretRobotRelRad { get; }

        /// <summary>Hood launch angle, degrees.</summary>
        public double HoodDeg { get; }

        /// <summary>Flywheel speed, RPM.</summary>
        public double FlywheelRpm { get; }

        /// <summary>Intake arm angle above horizontal, degrees (about 15 deployed, 105 stowed).</summary>
        public double IntakeArmDeg { get; }

        /// <summary>Intake roller voltage; positive pulls balls in.</summary>
        public double IntakeRollerVolts { get; }

        /// <summary>Spindex feed voltage; negative feeds balls to the launcher.</summary>
        public double SpindexFeedVolts { get; }

        /// <summary>Spindex wheel voltage; negative turns the hopper forward.</summary>
        public double SpindexWheelVolts { get; }

        /// <summary>Climber travel, 0 fully retracted to 1 fully extended.</summary>
        public double ClimberExtension { get; }

        /// <summary>Whether the robot is enabled.</summary>
        public bool IsEnabled { get; }

        /// <summary>Whether the robot is in autonomous.</summary>
        public bool IsAutonomous { get; }

        /// <summary>
        /// Decodes the bridge's robot-state array.
        /// </summary>
        /// <param name="values">Values laid out per <see cref="RobotStateLayout"/>; may be null.</param>
        /// <param name="state">Decoded state when the array is valid; default otherwise.</param>
        /// <returns>False when the array is missing or too short.</returns>
        public static bool TryParse(double[] values, out RobotState state)
        {
            state = default;
            if (values == null || values.Length < RobotStateLayout.Length)
            {
                return false;
            }
            bool isEnabled = values[RobotStateLayout.IsEnabled] > 0.5;
            bool isAutonomous = values[RobotStateLayout.IsAutonomous] > 0.5;
            state = new RobotState(
                values[RobotStateLayout.TimestampS],
                values[RobotStateLayout.PoseXM],
                values[RobotStateLayout.PoseYM],
                values[RobotStateLayout.HeadingRad],
                values[RobotStateLayout.VelocityXMps],
                values[RobotStateLayout.VelocityYMps],
                values[RobotStateLayout.OmegaRadps],
                values[RobotStateLayout.TurretRobotRelRad],
                values[RobotStateLayout.HoodDeg],
                values[RobotStateLayout.FlywheelRpm],
                values[RobotStateLayout.IntakeArmDeg],
                values[RobotStateLayout.IntakeRollerV],
                values[RobotStateLayout.SpindexFeedV],
                values[RobotStateLayout.SpindexWheelV],
                values[RobotStateLayout.ClimberExtension],
                isEnabled,
                isAutonomous);
            return true;
        }

        /// <summary>
        /// Blends two states: continuous values interpolate (angles the short way round); voltages and flags come
        /// from the later state.
        /// </summary>
        /// <param name="a">Earlier state.</param>
        /// <param name="b">Later state.</param>
        /// <param name="t">Blend fraction, 0 = a, 1 = b.</param>
        /// <returns>The blended state.</returns>
        public static RobotState Interpolate(RobotState a, RobotState b, double t)
        {
            double headingRad = InterpolateAngle(a.HeadingFieldRad, b.HeadingFieldRad, t);
            double turretRad = InterpolateAngle(a.TurretRobotRelRad, b.TurretRobotRelRad, t);
            return new RobotState(
                InterpolateValue(a.TimestampS, b.TimestampS, t),
                InterpolateValue(a.XFieldM, b.XFieldM, t),
                InterpolateValue(a.YFieldM, b.YFieldM, t),
                headingRad,
                InterpolateValue(a.VelocityXFieldMps, b.VelocityXFieldMps, t),
                InterpolateValue(a.VelocityYFieldMps, b.VelocityYFieldMps, t),
                InterpolateValue(a.OmegaRadps, b.OmegaRadps, t),
                turretRad,
                InterpolateValue(a.HoodDeg, b.HoodDeg, t),
                InterpolateValue(a.FlywheelRpm, b.FlywheelRpm, t),
                InterpolateValue(a.IntakeArmDeg, b.IntakeArmDeg, t),
                b.IntakeRollerVolts,
                b.SpindexFeedVolts,
                b.SpindexWheelVolts,
                InterpolateValue(a.ClimberExtension, b.ClimberExtension, t),
                b.IsEnabled,
                b.IsAutonomous);
        }

        // ---- low-level helpers (static, no side effects)

        /// <summary>Linear blend of two numbers.</summary>
        private static double InterpolateValue(double a, double b, double t)
        {
            return a + (b - a) * t;
        }

        /// <summary>Blend of two angles along the shorter way round, radians.</summary>
        private static double InterpolateAngle(double aRad, double bRad, double t)
        {
            float aDeg = (float)(aRad * Mathf.Rad2Deg);
            float bDeg = (float)(bRad * Mathf.Rad2Deg);
            float deltaDeg = Mathf.DeltaAngle(aDeg, bDeg);
            return aRad + deltaDeg * Mathf.Deg2Rad * t;
        }
    }

    /// <summary>
    /// What the bridge's field system says about the match. Immutable; build it with <see cref="TryParse"/>.
    /// </summary>
    public readonly struct MatchState
    {
        /// <summary>Creates a match state from its values; see the properties for meanings.</summary>
        public MatchState(
            MatchPhase phase,
            double matchTimeS,
            bool isBlueHubActive,
            bool isRedHubActive,
            int station,
            int autoWinner,
            bool isPaused)
        {
            Phase = phase;
            MatchTimeS = matchTimeS;
            IsBlueHubActive = isBlueHubActive;
            IsRedHubActive = isRedHubActive;
            Station = station;
            AutoWinner = autoWinner;
            IsPaused = isPaused;
        }

        /// <summary>Match phase.</summary>
        public MatchPhase Phase { get; }

        /// <summary>Seconds left in the current period; -1 with no match.</summary>
        public double MatchTimeS { get; }

        /// <summary>Whether the blue hub currently scores.</summary>
        public bool IsBlueHubActive { get; }

        /// <summary>Whether the red hub currently scores.</summary>
        public bool IsRedHubActive { get; }

        /// <summary>Driver station: 0..2 = Blue 1..3, 3..5 = Red 1..3, -1 unknown.</summary>
        public int Station { get; }

        /// <summary>Autonomous winner: 0 unknown, 1 blue, 2 red.</summary>
        public int AutoWinner { get; }

        /// <summary>Whether the session is paused: the match clock is held and the robot is disabled.</summary>
        public bool IsPaused { get; }

        /// <summary>Whether the driver station is on the red alliance.</summary>
        public bool IsRed()
        {
            return Station >= 3;
        }

        /// <summary>
        /// Decodes the bridge's match-state array.
        /// </summary>
        /// <param name="values">Values laid out per <see cref="MatchStateLayout"/>; may be null.</param>
        /// <param name="state">Decoded state when the array is valid; default otherwise.</param>
        /// <returns>False when the array is missing or too short.</returns>
        public static bool TryParse(double[] values, out MatchState state)
        {
            state = default;
            if (values == null || values.Length < MatchStateLayout.Length)
            {
                return false;
            }
            MatchPhase phase = (MatchPhase)(int)values[MatchStateLayout.Phase];
            bool isBlueHubActive = values[MatchStateLayout.IsBlueHubActive] > 0.5;
            bool isRedHubActive = values[MatchStateLayout.IsRedHubActive] > 0.5;
            int station = (int)values[MatchStateLayout.Station];
            int autoWinner = (int)values[MatchStateLayout.AutoWinner];
            bool isPaused = values[MatchStateLayout.IsPaused] > 0.5;
            state = new MatchState(
                phase, values[MatchStateLayout.MatchTimeS], isBlueHubActive, isRedHubActive, station, autoWinner,
                isPaused);
            return true;
        }
    }
}
