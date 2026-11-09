namespace DriverSim.Net
{
    /// <summary>
    /// Topic names and protocol version shared with the Java bridge (bridge/src/main/java/driversim/game/GameLink.java).
    /// </summary>
    public static class Protocol
    {
        public const int Version = 2; // GameLink.PROTOCOL_VERSION; must match the bridge
        public const string Prefix = "/DriverSim/"; // every Driver-Sim topic starts with this
        public const string RobotStateTopic = "/DriverSim/robot/state"; // double[], bridge to game
        public const string MatchStateTopic = "/DriverSim/match/state"; // double[], bridge to game
        public const string BridgeInfoTopic = "/DriverSim/bridge/info"; // string, bridge to game
        public const string CommandTopic = "/DriverSim/game/command"; // string, game to bridge
        public const string PoseCorrectionTopic = "/DriverSim/game/poseCorrection"; // double[3], game to bridge
    }

    /// <summary>
    /// Index of each value in /DriverSim/robot/state. Mirrors RobotStateLayout.java; change both together.
    /// </summary>
    public static class RobotStateLayout
    {
        public const int ProtocolVersion = 0; // GameLink.PROTOCOL_VERSION
        public const int TimestampS = 1; // robot clock, seconds
        public const int PoseXM = 2; // field frame (wpiBlue), meters
        public const int PoseYM = 3; // field frame, meters
        public const int HeadingRad = 4; // field frame, radians, CCW positive
        public const int VelocityXMps = 5; // field frame, meters/s
        public const int VelocityYMps = 6; // field frame, meters/s
        public const int OmegaRadps = 7; // radians/s, CCW positive
        public const int TurretRobotRelRad = 8; // radians, CCW positive, 0 = robot front
        public const int HoodDeg = 9; // launch angle, degrees
        public const int FlywheelRpm = 10; // RPM
        public const int IntakeArmDeg = 11; // degrees above horizontal
        public const int IntakeRollerV = 12; // volts, positive pulls in
        public const int SpindexFeedV = 13; // volts, negative feeds the launcher
        public const int SpindexWheelV = 14; // negative turns the hopper forward
        public const int ClimberExtension = 15; // travel, 0 retracted .. 1 extended
        public const int IsEnabled = 16; // 1 when enabled
        public const int IsAutonomous = 17; // 1 in autonomous
        public const int Length = 18; // number of values
    }

    /// <summary>
    /// Index of each value in /DriverSim/match/state. Mirrors MatchStateLayout.java; change both together.
    /// </summary>
    public static class MatchStateLayout
    {
        public const int Phase = 0; // MatchPhase
        public const int MatchTimeS = 1; // seconds left in the period, -1 with no match
        public const int IsBlueHubActive = 2; // 1 when the blue hub scores
        public const int IsRedHubActive = 3; // 1 when the red hub scores
        public const int Station = 4; // 0..2 Blue 1..3, 3..5 Red 1..3, -1 unknown
        public const int AutoWinner = 5; // 0 unknown, 1 blue, 2 red
        public const int Length = 6; // number of values
    }

    /// <summary>Match phases, in the same order as MatchController.Phase in the bridge.</summary>
    public enum MatchPhase
    {
        FreePlay = 0,
        Auto = 1,
        AutoToTeleop = 2,
        Teleop = 3,
        PostMatch = 4,
    }
}
