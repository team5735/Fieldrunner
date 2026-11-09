package driversim.game;

/**
 * Index of each value in the {@code /DriverSim/robot/state} double array. The Unity side mirrors these indices in
 * {@code RobotStateLayout.cs}; change both together and bump {@link GameLink#PROTOCOL_VERSION}.
 */
public final class RobotStateLayout {
    public static final int PROTOCOL_VERSION = 0; // GameLink.PROTOCOL_VERSION
    public static final int TIMESTAMP_S = 1; // robot clock, seconds
    public static final int POSE_X_M = 2; // field frame (wpiBlue origin), meters
    public static final int POSE_Y_M = 3; // field frame, meters
    public static final int HEADING_RAD = 4; // field frame, radians, counter-clockwise positive
    public static final int VELOCITY_X_MPS = 5; // field frame, meters/s
    public static final int VELOCITY_Y_MPS = 6; // field frame, meters/s
    public static final int OMEGA_RADPS = 7; // radians/s, counter-clockwise positive
    public static final int TURRET_ROBOT_REL_RAD = 8; // turret heading relative to robot front, radians, CCW positive
    public static final int HOOD_DEG = 9; // hood launch angle, degrees
    public static final int FLYWHEEL_RPM = 10; // flywheel speed, RPM
    public static final int INTAKE_ARM_DEG = 11; // intake arm above horizontal, degrees
    public static final int INTAKE_ROLLER_V = 12; // intake roller volts, positive pulls in
    public static final int SPINDEX_FEED_V = 13; // spindex feed volts, negative feeds the launcher
    public static final int SPINDEX_WHEEL_V = 14; // spindex wheel volts, negative turns forward
    public static final int CLIMBER_EXTENSION = 15; // 0 retracted .. 1 extended
    public static final int IS_ENABLED = 16; // 1 when the robot is enabled
    public static final int IS_AUTONOMOUS = 17; // 1 in autonomous
    public static final int LENGTH = 18; // number of values

    private RobotStateLayout() {
        throw new UnsupportedOperationException("This is a utility class!");
    }
}
