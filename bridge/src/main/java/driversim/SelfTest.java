package driversim;

import static edu.wpi.first.units.Units.Degrees;
import static edu.wpi.first.units.Units.RPM;

import java.util.ArrayList;
import java.util.List;
import java.util.function.BooleanSupplier;

import edu.wpi.first.hal.AllianceStationID;
import edu.wpi.first.math.geometry.Pose2d;
import edu.wpi.first.wpilibj.DriverStation;
import edu.wpi.first.wpilibj.Timer;
import edu.wpi.first.wpilibj.simulation.DriverStationSim;
import edu.wpi.first.wpilibj.smartdashboard.SmartDashboard;
import edu.wpi.first.wpilibj2.command.Command;
import edu.wpi.first.wpilibj2.command.CommandScheduler;
import edu.wpi.first.wpilibj2.command.Commands;

import driversim.game.MatchController;
import frc.robot.Robot;

/**
 * Scripted headless check that the bridge makes the real robot code work in sim: the robot enables, the turret zeroes
 * on its hall switch, the intake deploys, the flywheel reaches speed, the hood follows its servo, the climber reaches
 * its switch, the driver's stick drives the robot, the shot-gating checks all pass, and a match starts in autonomous.
 *
 * <p>
 * Inputs go through the simulated driver station exactly as the GUI's joysticks would. Run it without the sim GUI
 * (the GUI would overwrite the joystick values). Prints a PASS/FAIL line per step and exits the process with 0 when
 * every step passed, 1 otherwise.
 */
final class SelfTest {
    private static final int DRIVER_PORT = 0; // driver controller port
    private static final int AXIS_LEFT_Y = 1; // Xbox left stick Y; pushing forward is negative
    private static final int BUTTON_B = 2; // Xbox B: dynamic launch at the hub
    private static final double SHOOT_TEST_TIMEOUT_S = 0.95; // seconds; under the 1 s launch override timeout

    private final Robot robot; // robot under test
    private final DriverSimBridge bridge; // bridge under test
    private final List<String> results; // one line per finished step
    private int failureCount; // steps that failed
    private double driveStartXMeters; // robot x at the start of the drive step
    private boolean hasShotGatingPassed; // latched once all three launch checks read true together

    /**
     * Prepares a self-test against a running robot and bridge.
     *
     * @param robot  The robot built by {@link DriverSimMain}.
     * @param bridge The bridge attached to it.
     */
    SelfTest(Robot robot, DriverSimBridge bridge) {
        this.robot = robot;
        this.bridge = bridge;
        this.results = new ArrayList<>();
        this.failureCount = 0;
        this.driveStartXMeters = 0;
        this.hasShotGatingPassed = false;
    }

    /** Schedules the whole test on the command scheduler. */
    void schedule() {
        Command test = Commands.sequence(
            Commands.waitSeconds(1.0),
            Commands.runOnce(this::enableTeleopBlue),

            // turret: teleopInit schedules the real zero sequence; it needs the hall switch to finish
            step("turret zeroes on hall switch", Commands.none(), () -> robot.turret.isZeroed, 8),

            // intake arm deploys down to its release angle
            step(
                "intake slapdown reaches 15 deg",
                robot.intake.getSlapdownCommand(),
                () -> robot.intake.getSlapdownPosition().lte(Degrees.of(16)),
                4
            ),

            // flywheel reaches a 3000 RPM setpoint with the robot's bang-bang + feedforward
            step(
                "flywheel reaches 3000 RPM",
                robot.launcher.getLaunchFuel(RPM.of(3000)),
                () -> Math.abs(robot.launcher.getRPM() - 3000) < 60,
                4
            ),

            // hood servo feedback follows the commanded angle
            step(
                "hood feedback reaches 30 deg",
                robot.hood.run(() -> robot.hood.setHoodAngle(30)),
                () -> Math.abs(robot.hood.getNormalizedAngle() - 30) < 1.5,
                3
            ),

            // climber runs to its upper switch
            step(
                "climber reaches upper switch",
                robot.climber.getFullyExtendCommand(),
                robot.climber::isAtUpLimit,
                5
            ),
            step(
                "climber returns to lower switch",
                robot.climber.getFullyDetractCommand(),
                robot.climber::isAtDownLimit,
                5
            ),

            // driver's left stick forward drives the robot downfield (blue perspective = +x)
            Commands.runOnce(this::startDriveForward),
            step(
                "left stick drives robot 1 m downfield",
                Commands.none(),
                () -> robot.drivetrain.getState().Pose.getX() - driveStartXMeters > 1.0,
                3
            ),
            Commands.runOnce(this::releaseSticks),
            Commands.waitSeconds(0.5),

            // holding B at range: hood, turret and flywheel checks all pass before the 1 s override would kick in
            Commands.runOnce(this::startShootFromDefaultPose),
            Commands.waitSeconds(0.5),
            Commands.runOnce(() -> setButton(BUTTON_B, true)),
            step("B: launch gating passes without override", Commands.none(), this::checkShotGating, 3),
            step(
                "B: spindex feeds the launcher",
                Commands.none(),
                () -> bridge.getMechanismSnapshot().spindexFeedVolts() < -1,
                3
            ),
            Commands.runOnce(() -> setButton(BUTTON_B, false)),

            // a match starts in autonomous, enabled, with the match clock running
            Commands.runOnce(() -> bridge.startMatch(Timer.getFPGATimestamp())),
            step(
                "match start enables autonomous",
                Commands.none(),
                () -> DriverStation.isAutonomousEnabled() && DriverStation.getMatchTime() < MatchController.AUTO_S,
                2
            ),
            Commands.runOnce(() -> bridge.abortMatch(Timer.getFPGATimestamp())),
            step("match abort disables robot", Commands.none(), DriverStation::isDisabled, 2),

            Commands.runOnce(this::finish)
        ).ignoringDisable(true).finallyDo(this::handleTestEnded).withName("Driver-Sim self-test");
        CommandScheduler.getInstance().schedule(test);
    }

    // ---- private stateful helpers

    /**
     * Builds one test step: runs an action until a check passes or time runs out, then records the result. The action
     * runs as a proxy so the test itself holds no subsystem requirements; otherwise robot commands bound to buttons
     * the test presses would cancel the whole test.
     *
     * @param name     Step description for the report.
     * @param action   Command to run during the step; interrupted when the step ends.
     * @param check    Pass condition.
     * @param timeoutS Seconds before the step fails.
     * @return Command for the step.
     */
    private Command step(String name, Command action, BooleanSupplier check, double timeoutS) {
        double[] startS = new double[1];
        return Commands.sequence(
            Commands.runOnce(() -> startS[0] = Timer.getFPGATimestamp()),
            Commands.deadline(Commands.waitUntil(check).withTimeout(timeoutS), action.asProxy()),
            Commands.runOnce(() -> record(name, check.getAsBoolean(), Timer.getFPGATimestamp() - startS[0]))
        );
    }

    private void record(String name, boolean didPass, double elapsedS) {
        String verdict = "PASS";
        if (!didPass) {
            verdict = "FAIL";
            failureCount++;
        }
        String line = String.format("[Driver-Sim self-test] %s  %-45s %.2f s", verdict, name, elapsedS);
        results.add(line);
        System.out.println(line);
    }

    private void enableTeleopBlue() {
        DriverStationSim.setAllianceStationId(AllianceStationID.Blue1);
        DriverStationSim.setJoystickIsXbox(DRIVER_PORT, true);
        DriverStationSim.setJoystickAxisCount(DRIVER_PORT, 6);
        DriverStationSim.setJoystickButtonCount(DRIVER_PORT, 10);
        DriverStationSim.setJoystickPOVCount(DRIVER_PORT, 1);
        DriverStationSim.setJoystickPOV(DRIVER_PORT, 0, -1);
        DriverStationSim.setDsAttached(true);
        DriverStationSim.setAutonomous(false);
        DriverStationSim.setEnabled(true);
        DriverStationSim.notifyNewData();
    }

    private void startDriveForward() {
        driveStartXMeters = robot.drivetrain.getState().Pose.getX();
        DriverStationSim.setJoystickAxis(DRIVER_PORT, AXIS_LEFT_Y, -1.0);
        DriverStationSim.notifyNewData();
    }

    private void releaseSticks() {
        DriverStationSim.setJoystickAxis(DRIVER_PORT, AXIS_LEFT_Y, 0);
        DriverStationSim.notifyNewData();
    }

    private void startShootFromDefaultPose() {
        Pose2d startPose = DriverSimBridge.DEFAULT_START_POSE;
        robot.drivetrain.resetPose(startPose);
        hasShotGatingPassed = false;
    }

    private void setButton(int button, boolean isPressed) {
        DriverStationSim.setJoystickButton(DRIVER_PORT, button, isPressed);
        DriverStationSim.notifyNewData();
    }

    /**
     * Latches when the launch calculator's hood, turret and flywheel checks all read true together, within the window
     * before its 1 s override timeout would let the spindex feed regardless.
     *
     * @return True once gating has passed on its own merits.
     */
    private boolean checkShotGating() {
        boolean isHoodReady = SmartDashboard.getBoolean("launchCalc/hoodCheck", false);
        boolean isTurretReady = SmartDashboard.getBoolean("launchCalc/turretCheck", false);
        boolean isFlywheelReady = SmartDashboard.getBoolean("launchCalc/launcherCheck", false);
        double launchTimerS = SmartDashboard.getNumber("launchCalc/timeOut", 0);
        boolean isBeforeOverride = launchTimerS < SHOOT_TEST_TIMEOUT_S;
        if (isHoodReady && isTurretReady && isFlywheelReady && isBeforeOverride) {
            hasShotGatingPassed = true;
        }
        return hasShotGatingPassed;
    }

    /**
     * Fails and exits when something cancelled the test before it finished, so a broken run can't hang.
     *
     * @param wasInterrupted True when the test command was cancelled rather than completing.
     */
    private void handleTestEnded(boolean wasInterrupted) {
        if (wasInterrupted) {
            record("self-test ran to completion (it was cancelled)", false, 0);
            finish();
        }
    }

    private void finish() {
        System.out.println("[Driver-Sim self-test] " + (results.size() - failureCount) + "/" + results.size()
                           + " steps passed");
        int exitCode = 0;
        if (failureCount > 0) {
            exitCode = 1;
        }
        System.exit(exitCode);
    }
}
