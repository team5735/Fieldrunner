namespace DriverSim.Net
{
    /// <summary>
    /// Topic names and protocol version shared with the Java bridge (bridge/src/main/java/driversim/game/GameLink.java).
    /// </summary>
    public static class Protocol
    {
        public const int Version = 1; // GameLink.PROTOCOL_VERSION; must match the bridge
        public const string Prefix = "/DriverSim/";
        public const string RobotStateTopic = "/DriverSim/robot/state";
        public const string MatchStateTopic = "/DriverSim/match/state";
        public const string BridgeInfoTopic = "/DriverSim/bridge/info";
        public const string CommandTopic = "/DriverSim/game/command";
        public const string PoseNudgeTopic = "/DriverSim/game/poseNudge";
    }

    /// <summary>
    /// Index of each value in /DriverSim/robot/state. Mirrors RobotStateLayout.java; change both together.
    /// </summary>
    public static class RobotStateLayout
    {
        public const int ProtocolVersion = 0;
        public const int TimestampS = 1; // robot clock, seconds
        public const int PoseXM = 2; // field frame (wpiBlue), meters
        public const int PoseYM = 3;
        public const int HeadingRad = 4; // CCW positive
        public const int VelocityXMps = 5; // field frame
        public const int VelocityYMps = 6;
        public const int OmegaRadps = 7;
        public const int TurretRobotRelRad = 8; // CCW positive, 0 = robot front
        public const int HoodDeg = 9;
        public const int FlywheelRpm = 10;
        public const int IntakeArmDeg = 11; // above horizontal
        public const int IntakeRollerV = 12; // positive pulls in
        public const int SpindexFeedV = 13; // negative feeds the launcher
        public const int SpindexWheelV = 14;
        public const int ClimberExtension = 15; // 0..1
        public const int IsEnabled = 16;
        public const int IsAutonomous = 17;
        public const int Length = 18;
    }

    /// <summary>
    /// Index of each value in /DriverSim/match/state. Mirrors MatchStateLayout.java; change both together.
    /// </summary>
    public static class MatchStateLayout
    {
        public const int Phase = 0; // MatchPhase
        public const int MatchTimeS = 1; // -1 with no match
        public const int IsBlueHubActive = 2;
        public const int IsRedHubActive = 3;
        public const int Station = 4; // 0..2 Blue 1..3, 3..5 Red 1..3, -1 unknown
        public const int AutoWinner = 5; // 0 unknown, 1 blue, 2 red
        public const int Length = 6;
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
