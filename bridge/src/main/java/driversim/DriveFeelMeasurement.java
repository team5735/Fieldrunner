package driversim;

import edu.wpi.first.math.geometry.Pose2d;
import edu.wpi.first.math.kinematics.ChassisSpeeds;
import edu.wpi.first.wpilibj.Timer;
import edu.wpi.first.wpilibj.simulation.DriverStationSim;
import edu.wpi.first.wpilibj2.command.Command;
import edu.wpi.first.wpilibj2.command.Commands;

import frc.robot.subsystems.DrivetrainSubsystem;

/**
 * Measures how the simulated drivetrain accelerates and stops under the driver's stick: from rest it holds the left
 * stick fully forward, then releases it, sampling the drivetrain's state every robot loop. Prints time to 90% of top
 * speed, peak acceleration and stopping distance, which is what a driver feels as the robot's mass. It reports numbers
 * only; it never fails.
 *
 * <p>
 * Runs with no game attached: nothing collides, so the robot can travel as far as it needs.
 */
final class DriveFeelMeasurement {
    private static final int DRIVER_PORT = 0;                // driver controller port
    private static final int AXIS_LEFT_Y = 1;                // Xbox left stick Y; pushing forward is negative
    private static final double HOLD_S = 2.0;                // seconds the stick is held forward
    private static final double COAST_S = 2.0;               // seconds sampled after the stick is released
    private static final double TOP_SPEED_FRACTION = 0.9;    // fraction of top speed that counts as "up to speed"
    private static final double STOPPED_MPS = 0.05;          // speed below which the robot counts as stopped, m/s
    private static final Pose2d START_FIELD = new Pose2d();  // field origin; nothing collides without a game

    private final DrivetrainSubsystem drivetrain;  // drivetrain under measurement
    private double phaseStartS;                    // robot clock when the current phase began, seconds
    private double lastSampleS;                    // robot clock of the previous sample, seconds
    private double lastSpeedMps;                   // speed at the previous sample, m/s
    private double topSpeedMps;                    // highest speed seen while the stick was held, m/s
    private double peakAccelMps2;                  // highest acceleration between two samples, m/s^2
    private double peakDecelMps2;                  // highest deceleration between two samples, m/s^2
    private double timeToNinetyS;                  // seconds from stick push to 90% of top speed; -1 until known
    private double releaseXFieldM;                 // field x when the stick was released, meters
    private double stopDistanceM;                  // meters travelled from release until stopped; -1 until known
    private double stopTimeS;                      // seconds from release until stopped; -1 until known
    private final double[] holdSpeedsMps;          // speed sampled each loop while held, m/s
    private final double[] holdTimesS;             // seconds since the push for each held sample
    private int holdSampleCount;                   // samples stored in holdSpeedsMps / holdTimesS

    /**
     * Prepares a measurement of one drivetrain.
     *
     * @param drivetrain The robot code's drivetrain, simulated by its own CTRE sim thread.
     */
    DriveFeelMeasurement(DrivetrainSubsystem drivetrain) {
        this.drivetrain = drivetrain;
        int maxSamples = (int) Math.ceil(HOLD_S / 0.005) + 1;
        this.holdSpeedsMps = new double[maxSamples];
        this.holdTimesS = new double[maxSamples];
        reset();
    }

    // ---- public API

    /**
     * Makes the command that runs the measurement and prints its report. The command holds no subsystem requirements.
     *
     * @return Command for the whole measurement.
     */
    Command makeCommand() {
        Command place = Commands.runOnce(this::placeAtRest);
        Command settle = Commands.waitSeconds(0.5);
        Command push = Commands.runOnce(this::push);
        Command hold = Commands.run(this::sampleHold).withTimeout(HOLD_S);
        Command release = Commands.runOnce(this::release);
        Command coast = Commands.run(this::sampleCoast).withTimeout(COAST_S);
        Command report = Commands.runOnce(this::printReport);
        return Commands.sequence(place, settle, push, hold, release, coast, report);
    }

    // ---- private stateful helpers

    /** Clears every result so the measurement can run again. */
    private void reset() {
        phaseStartS = 0;
        lastSampleS = 0;
        lastSpeedMps = 0;
        topSpeedMps = 0;
        peakAccelMps2 = 0;
        peakDecelMps2 = 0;
        timeToNinetyS = -1;
        releaseXFieldM = 0;
        stopDistanceM = -1;
        stopTimeS = -1;
        holdSampleCount = 0;
    }

    /** Puts the robot at rest at the start pose, sticks centered. */
    private void placeAtRest() {
        reset();
        drivetrain.resetPose(START_FIELD);
        setLeftStickY(0);
    }

    /** Pushes the left stick fully forward and starts the clock. */
    private void push() {
        phaseStartS = Timer.getFPGATimestamp();
        lastSampleS = phaseStartS;
        lastSpeedMps = getSpeedMps();
        setLeftStickY(-1.0);
    }

    /** Samples speed and acceleration while the stick is held. */
    private void sampleHold() {
        double nowS = Timer.getFPGATimestamp();
        double speedMps = getSpeedMps();
        updatePeaks(nowS, speedMps);
        if (speedMps > topSpeedMps) {
            topSpeedMps = speedMps;
        }
        if (holdSampleCount < holdSpeedsMps.length) {
            holdSpeedsMps[holdSampleCount] = speedMps;
            holdTimesS[holdSampleCount] = nowS - phaseStartS;
            holdSampleCount++;
        }
    }

    /** Releases the stick and remembers where the robot was. */
    private void release() {
        setLeftStickY(0);
        phaseStartS = Timer.getFPGATimestamp();
        Pose2d poseField = drivetrain.getState().Pose;
        releaseXFieldM = poseField.getX();
    }

    /** Samples deceleration after the release and notes when the robot stops. */
    private void sampleCoast() {
        double nowS = Timer.getFPGATimestamp();
        double speedMps = getSpeedMps();
        updatePeaks(nowS, speedMps);
        if (stopTimeS < 0 && speedMps < STOPPED_MPS) {
            stopTimeS = nowS - phaseStartS;
            Pose2d poseField = drivetrain.getState().Pose;
            stopDistanceM = poseField.getX() - releaseXFieldM;
        }
    }

    /**
     * Updates the peak acceleration and deceleration from the change since the previous sample.
     *
     * @param nowS     Robot clock now, seconds.
     * @param speedMps Speed now, m/s.
     */
    private void updatePeaks(double nowS, double speedMps) {
        double dtS = nowS - lastSampleS;
        if (dtS > 1e-4) {
            double accelMps2 = (speedMps - lastSpeedMps) / dtS;
            if (accelMps2 > peakAccelMps2) {
                peakAccelMps2 = accelMps2;
            }
            if (-accelMps2 > peakDecelMps2) {
                peakDecelMps2 = -accelMps2;
            }
        }
        lastSampleS = nowS;
        lastSpeedMps = speedMps;
    }

    /** Prints the results, one line each. */
    private void printReport() {
        // time to 90% of the top speed seen while held
        double thresholdMps = TOP_SPEED_FRACTION * topSpeedMps;
        for (int i = 0; i < holdSampleCount; i++) {
            if (holdSpeedsMps[i] >= thresholdMps) {
                timeToNinetyS = holdTimesS[i];
                break;
            }
        }

        String prefix = "[Driver-Sim drive-feel] ";
        System.out.println(prefix + String.format("top speed            %.2f m/s", topSpeedMps));
        System.out.println(prefix + String.format("time to 90%% speed    %.2f s", timeToNinetyS));
        System.out.println(prefix + String.format("peak acceleration    %.1f m/s^2", peakAccelMps2));
        System.out.println(prefix + String.format("peak deceleration    %.1f m/s^2", peakDecelMps2));
        System.out.println(prefix + String.format("stop time / distance %.2f s / %.2f m", stopTimeS, stopDistanceM));
    }

    /**
     * Gets the drivetrain's ground speed from its robot-relative chassis speeds.
     *
     * @return Speed, m/s.
     */
    private double getSpeedMps() {
        ChassisSpeeds speedsRobot = drivetrain.getState().Speeds;
        return Math.hypot(speedsRobot.vxMetersPerSecond, speedsRobot.vyMetersPerSecond);
    }

    // ---- private static pure helpers

    /**
     * Sets the driver's left stick Y axis.
     *
     * @param stickY Axis value; -1 is fully forward.
     */
    private static void setLeftStickY(double stickY) {
        DriverStationSim.setJoystickAxis(DRIVER_PORT, AXIS_LEFT_Y, stickY);
        DriverStationSim.notifyNewData();
    }
}
