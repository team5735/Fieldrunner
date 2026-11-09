package driversim.mech;

/**
 * One-degree-of-freedom mechanism driven by a voltage, modelled with the same feedforward terms the robot code tunes
 * (kS, kV, kA and optionally an arm-style kG). Positions are in mechanism rotations and velocities in mechanism
 * rotations per second; the gains must use those units too.
 *
 * <p>
 * This is a game-feel model, not a physics model: the mechanism accelerates as {@code a = (V - kS*sign(v) - kG*cos(theta)
 * - kV*v) / kA}, sticks when the drive voltage can't beat static friction, and stops dead at its hard stops. It carries
 * its own position and velocity history.
 */
public final class FeedforwardMechanismSim {
    private static final double SUBSTEP_S = 0.001;   // seconds, integration step; keeps small-kA mechanisms stable
    private static final double STOPPED_RPS = 1e-6;  // rotations/s, below this the mechanism counts as stationary

    private final double kS;                    // volts, static friction
    private final double kV;                    // volts per (rotation/s)
    private final double kA;                    // volts per (rotation/s^2)
    private final double kG;                    // volts, gravity torque when the arm is horizontal; 0 for no gravity
    private final double minPositionRotations;  // hard stop, mechanism rotations
    private final double maxPositionRotations;  // hard stop, mechanism rotations

    private double positionRotations;  // current mechanism position
    private double velocityRps;        // current mechanism velocity, rotations/s

    /**
     * Creates a mechanism resting at a starting position.
     *
     * @param kS                   Static friction, volts.
     * @param kV                   Velocity gain, volts per (rotation/s); must be positive.
     * @param kA                   Acceleration gain, volts per (rotation/s^2); must be positive.
     * @param kG                   Arm gravity gain, volts at horizontal (position 0 = horizontal); 0 disables gravity.
     * @param minPositionRotations Lower hard stop, mechanism rotations.
     * @param maxPositionRotations Upper hard stop, mechanism rotations; must exceed the lower one.
     * @param startRotations       Starting position, mechanism rotations; clamped to the hard stops.
     * @throws IllegalArgumentException When kV or kA is not positive, or the hard stops are out of order.
     */
    public FeedforwardMechanismSim(
        double kS, double kV, double kA, double kG, double minPositionRotations, double maxPositionRotations,
        double startRotations
    ) {
        if (kV <= 0 || kA <= 0) {
            throw new IllegalArgumentException("kV and kA must be positive");
        }
        if (maxPositionRotations <= minPositionRotations) {
            throw new IllegalArgumentException("max position must exceed min position");
        }
        this.kS = kS;
        this.kV = kV;
        this.kA = kA;
        this.kG = kG;
        this.minPositionRotations = minPositionRotations;
        this.maxPositionRotations = maxPositionRotations;
        this.positionRotations = clamp(startRotations, minPositionRotations, maxPositionRotations);
        this.velocityRps = 0;
    }

    /**
     * Advances the mechanism by one time step under a constant applied voltage.
     *
     * @param volts Voltage applied to the mechanism, in the mechanism's positive direction.
     * @param dtS   Time step, seconds; non-positive steps do nothing.
     */
    public void update(double volts, double dtS) {
        double remainingS = dtS;
        while (remainingS > 0) {
            double stepS = Math.min(SUBSTEP_S, remainingS);
            remainingS -= stepS;

            // integrate one substep
            MotionState next = computeStep(positionRotations, velocityRps, volts, stepS);
            positionRotations = next.positionRotations();
            velocityRps = next.velocityRps();
        }
    }

    /**
     * Applies a sudden change in velocity, such as a flywheel losing speed when a ball goes through it.
     *
     * @param deltaRps Change in velocity, mechanism rotations/s.
     */
    public void addVelocity(double deltaRps) { velocityRps += deltaRps; }

    /**
     * Reports where the mechanism is.
     *
     * @return Current position, mechanism rotations.
     */
    public double getPositionRotations() { return positionRotations; }

    /**
     * Reports how fast the mechanism is moving.
     *
     * @return Current velocity, mechanism rotations/s.
     */
    public double getVelocityRps() { return velocityRps; }

    // ---- private stateful helpers

    /**
     * Computes the next position and velocity after one small step, honouring static friction and hard stops.
     *
     * @param position Position at the start of the step, mechanism rotations.
     * @param velocity Velocity at the start of the step, rotations/s.
     * @param volts    Applied voltage.
     * @param stepS    Step length, seconds.
     * @return Position and velocity at the end of the step.
     */
    private MotionState computeStep(double position, double velocity, double volts, double stepS) {
        // voltage left after gravity
        double gravityVolts = kG * Math.cos(2 * Math.PI * position);
        double drivingVolts = volts - gravityVolts;

        // stuck: static friction holds a stationary mechanism
        boolean isStationary = Math.abs(velocity) < STOPPED_RPS;
        if (isStationary && Math.abs(drivingVolts) <= kS) {
            return new MotionState(position, 0);
        }

        // friction opposes motion, or the push when starting from rest
        double frictionDirection = Math.signum(velocity);
        if (isStationary) {
            frictionDirection = Math.signum(drivingVolts);
        }
        double accelRps2 = (drivingVolts - kS * frictionDirection - kV * velocity) / kA;
        double nextVelocity = velocity + accelRps2 * stepS;

        // friction can stop the mechanism but never reverse it
        boolean didReverse = !isStationary && Math.signum(nextVelocity) != Math.signum(velocity);
        if (didReverse && Math.abs(drivingVolts) <= kS) {
            nextVelocity = 0;
        }

        // hard stops
        double nextPosition = position + nextVelocity * stepS;
        if (nextPosition <= minPositionRotations && nextVelocity < 0) {
            return new MotionState(minPositionRotations, 0);
        }
        if (nextPosition >= maxPositionRotations && nextVelocity > 0) {
            return new MotionState(maxPositionRotations, 0);
        }
        return new MotionState(nextPosition, nextVelocity);
    }

    // ---- low-level helpers (static, no side effects)

    /**
     * Limits a value to a range.
     *
     * @param value Value to limit.
     * @param low   Lowest allowed value.
     * @param high  Highest allowed value; must not be below low.
     * @return The value, moved into [low, high].
     */
    private static double clamp(double value, double low, double high) { return Math.max(low, Math.min(high, value)); }

    /**
     * Position and velocity of the mechanism at an instant.
     *
     * @param positionRotations Position, mechanism rotations.
     * @param velocityRps       Velocity, mechanism rotations/s.
     */
    private record MotionState(double positionRotations, double velocityRps) {}
}
