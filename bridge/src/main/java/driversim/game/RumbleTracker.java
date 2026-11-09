package driversim.game;

/**
 * Turns the robot code's rumble requests into updates for the game, reporting only changes that matter. The driver
 * station simulation stores rumble as 16-bit values.
 */
public final class RumbleTracker {
    public static final double MIN_CHANGE = 0.01;  // smallest change of either motor (0..1) worth sending
    private static final double RAW_MAX = 65535;   // driver station rumble value for full strength

    private double lastLeft;   // left value last reported, 0..1
    private double lastRight;  // right value last reported, 0..1

    /** Creates a tracker whose last reported rumble is none. */
    public RumbleTracker() {
        this.lastLeft = 0;
        this.lastRight = 0;
    }

    /**
     * Reads the robot's current rumble and says whether the game needs to hear about it.
     *
     * @param leftRaw  Left motor as the driver station simulation stores it, 0 to 65535.
     * @param rightRaw Right motor as the driver station simulation stores it, 0 to 65535.
     * @return The new rumble when either motor changed by more than {@link #MIN_CHANGE} or stopped; null otherwise.
     */
    public RumbleValues update(int leftRaw, int rightRaw) {
        double left = convertToUnit(leftRaw);
        double right = convertToUnit(rightRaw);
        boolean hasLeftChanged = hasChanged(lastLeft, left);
        boolean hasRightChanged = hasChanged(lastRight, right);
        if (!hasLeftChanged && !hasRightChanged) {
            return null;
        }
        lastLeft = left;
        lastRight = right;
        return new RumbleValues(left, right);
    }

    // ---- low-level helpers (static, no side effects)

    /**
     * Scales a raw rumble value to 0..1.
     *
     * @param raw Driver station rumble value, 0 to 65535.
     * @return Strength from 0 to 1.
     */
    public static double convertToUnit(int raw) {
        double cappedRaw = Math.min(RAW_MAX, raw);
        double clampedRaw = Math.max(0, cappedRaw);
        return clampedRaw / RAW_MAX;
    }

    /**
     * Whether a motor's value moved enough to report: by more than {@link #MIN_CHANGE}, or down to exactly zero.
     *
     * @param oldValue Value last reported, 0..1.
     * @param newValue Value now, 0..1.
     * @return True when it should be reported.
     */
    private static boolean hasChanged(double oldValue, double newValue) {
        if (Math.abs(newValue - oldValue) > MIN_CHANGE) {
            return true;
        }
        if (newValue == 0 && oldValue != 0) {
            return true;
        }
        return false;
    }
}
