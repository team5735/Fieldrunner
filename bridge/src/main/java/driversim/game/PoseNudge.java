package driversim.game;

/**
 * A small push to the robot's pose, such as the game pushing the robot back out of a wall it drove into. Added to the
 * robot's current pose.
 *
 * @param dxFieldMeters     Change in field x, meters.
 * @param dyFieldMeters     Change in field y, meters.
 * @param dHeadingRadians   Change in heading, radians, counter-clockwise positive.
 */
public record PoseNudge(double dxFieldMeters, double dyFieldMeters, double dHeadingRadians) {
    /** A nudge that moves nothing. */
    public static final PoseNudge ZERO = new PoseNudge(0, 0, 0);

    /**
     * Whether the nudge moves the robot at all.
     *
     * @return True when every component is zero.
     */
    public boolean isZero() { return dxFieldMeters == 0 && dyFieldMeters == 0 && dHeadingRadians == 0; }

    /**
     * Combines two nudges.
     *
     * @param other Nudge to add to this one.
     * @return The sum of both nudges.
     */
    public PoseNudge plus(PoseNudge other) {
        double dxSum = dxFieldMeters + other.dxFieldMeters;
        double dySum = dyFieldMeters + other.dyFieldMeters;
        double dHeadingSum = dHeadingRadians + other.dHeadingRadians;
        return new PoseNudge(dxSum, dySum, dHeadingSum);
    }
}
