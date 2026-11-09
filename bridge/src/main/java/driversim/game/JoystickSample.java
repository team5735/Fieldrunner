package driversim.game;

/**
 * One virtual Xbox controller reading from the game, in WPILib's {@code XboxController} conventions: sticks are -1 to
 * 1 with stick pushed forward negative, triggers are 0 to 1.
 *
 * @param leftX        Left stick X, -1 (left) to 1 (right).
 * @param leftY        Left stick Y, -1 (forward) to 1 (back).
 * @param leftTrigger  Left trigger, 0 to 1.
 * @param rightTrigger Right trigger, 0 to 1.
 * @param rightX       Right stick X, -1 (left) to 1 (right).
 * @param rightY       Right stick Y, -1 (forward) to 1 (back).
 * @param buttonBits   Pressed buttons: bit 0..9 = A, B, X, Y, LB, RB, Back, Start, left stick press, right stick press.
 * @param povDeg       D-pad direction in whole degrees clockwise from up (0, 45, ... 315); -1 when released.
 */
public record JoystickSample(
    double leftX, double leftY, double leftTrigger, double rightTrigger, double rightX, double rightY, int buttonBits,
    int povDeg
) {
    public static final int WIRE_LENGTH = 8;         // values in the topic's double array
    public static final int AXIS_COUNT = 6;          // axes an Xbox controller has
    public static final int BUTTON_COUNT = 10;       // buttons an Xbox controller has
    public static final int AXIS_LEFT_X = 0;         // WPILib axis index
    public static final int AXIS_LEFT_Y = 1;         // WPILib axis index
    public static final int AXIS_LEFT_TRIGGER = 2;   // WPILib axis index
    public static final int AXIS_RIGHT_TRIGGER = 3;  // WPILib axis index
    public static final int AXIS_RIGHT_X = 4;        // WPILib axis index
    public static final int AXIS_RIGHT_Y = 5;        // WPILib axis index
    private static final int WIRE_BUTTON_BITS = 6;   // index of the button bits in the topic's array
    private static final int WIRE_POV_DEG = 7;       // index of the D-pad angle in the topic's array

    /** Everything centered and released. */
    public static final JoystickSample NEUTRAL = new JoystickSample(0, 0, 0, 0, 0, 0, 0, -1);

    /**
     * Reads a sample from the topic's values {@code {leftX, leftY, leftTrigger, rightTrigger, rightX, rightY,
     * buttonBits, povDeg}}, clamping each to its valid range.
     *
     * @param values Values as published by the game; may be null.
     * @return The sample, or null when the length is wrong or any value is not a finite number.
     */
    public static JoystickSample parse(double[] values) {
        if (values == null || values.length != WIRE_LENGTH) {
            return null;
        }
        for (double value : values) {
            if (!Double.isFinite(value)) {
                return null;
            }
        }

        // axes
        double leftX = clamp(values[AXIS_LEFT_X], -1, 1);
        double leftY = clamp(values[AXIS_LEFT_Y], -1, 1);
        double leftTrigger = clamp(values[AXIS_LEFT_TRIGGER], 0, 1);
        double rightTrigger = clamp(values[AXIS_RIGHT_TRIGGER], 0, 1);
        double rightX = clamp(values[AXIS_RIGHT_X], -1, 1);
        double rightY = clamp(values[AXIS_RIGHT_Y], -1, 1);

        // buttons keep only the ten Xbox bits
        double maxBits = (1 << BUTTON_COUNT) - 1;
        double clampedBits = clamp(values[WIRE_BUTTON_BITS], 0, maxBits);
        int buttonBits = (int) Math.round(clampedBits);

        // D-pad: whole degrees in [0, 360), or -1
        int povDeg = (int) Math.round(values[WIRE_POV_DEG]);
        if (povDeg < 0) {
            povDeg = -1;
        } else {
            povDeg = povDeg % 360;
        }
        return new JoystickSample(leftX, leftY, leftTrigger, rightTrigger, rightX, rightY, buttonBits, povDeg);
    }

    /**
     * Reads one axis by its WPILib index.
     *
     * @param axisIndex 0 left X, 1 left Y, 2 left trigger, 3 right trigger, 4 right X, 5 right Y.
     * @return The axis value; 0 for an index outside 0..5.
     */
    public double getAxis(int axisIndex) {
        return switch (axisIndex) {
            case AXIS_LEFT_X -> leftX;
            case AXIS_LEFT_Y -> leftY;
            case AXIS_LEFT_TRIGGER -> leftTrigger;
            case AXIS_RIGHT_TRIGGER -> rightTrigger;
            case AXIS_RIGHT_X -> rightX;
            case AXIS_RIGHT_Y -> rightY;
            default -> 0;
        };
    }

    // ---- low-level helpers (static, no side effects)

    /**
     * Limits a value to a range.
     *
     * @param value Value to limit.
     * @param minValue Lower bound.
     * @param maxValue Upper bound.
     * @return The value, or the nearer bound when it lies outside.
     */
    private static double clamp(double value, double minValue, double maxValue) {
        double cappedValue = Math.min(maxValue, value);
        return Math.max(minValue, cappedValue);
    }
}
