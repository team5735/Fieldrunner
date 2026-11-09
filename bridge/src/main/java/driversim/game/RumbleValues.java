package driversim.game;

/**
 * Controller rumble strength.
 *
 * @param left  Left (low-frequency) motor, 0 to 1.
 * @param right Right (high-frequency) motor, 0 to 1.
 */
public record RumbleValues(double left, double right) {}
