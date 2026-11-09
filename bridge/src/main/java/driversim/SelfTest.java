package driversim;

import static edu.wpi.first.units.Units.Degrees;
import static edu.wpi.first.units.Units.RPM;

import edu.wpi.first.hal.AllianceStationID;
import edu.wpi.first.math.geometry.Pose2d;
import edu.wpi.first.math.geometry.Rotation2d;
import edu.wpi.first.networktables.DoubleArrayPublisher;
import edu.wpi.first.networktables.DoubleArrayTopic;
import edu.wpi.first.networktables.NetworkTableInstance;
import edu.wpi.first.units.measure.Angle;
import edu.wpi.first.units.measure.AngularVelocity;
import edu.wpi.first.wpilibj.DriverStation;
import edu.wpi.first.wpilibj.Timer;
import edu.wpi.first.wpilibj.simulation.DriverStationSim;
import edu.wpi.first.wpilibj.smartdashboard.SmartDashboard;
import edu.wpi.first.wpilibj2.command.Command;
import edu.wpi.first.wpilibj2.command.CommandScheduler;
import edu.wpi.first.wpilibj2.command.Commands;

import java.util.ArrayList;
import java.util.List;
import java.util.function.BooleanSupplier;

import frc.robot.Robot;

import driversim.game.MatchController;
import driversim.mech.MechanismSnapshot;

/**
 * Scripted headless check that the bridge makes the real robot code work in sim: the robot enables, the turret zeroes
 * on its hall switch, the intake deploys, the flywheel reaches speed, the hood follows its servo, the climber reaches
 * its switches, the driver's stick drives the robot (directly, and through the game's joystick topic), the
 * shot-gating checks all pass, and a match starts in autonomous. In with-game mode it then waits for the Unity game
 * to attach and checks that the game's collisions
 * reach the robot code: walls, hub and trench blocks stop the robot, a raised hood is blocked by the trench bar, and
 * bumps are drivable.
 *
 * <p>
 * Inputs go through the simulated driver station exactly as the GUI's joysticks would. Run it without the sim GUI
 * (the GUI would overwrite the joystick values). Prints a PASS/FAIL line per step and exits the process with 0 when
 * every step passed, 1 otherwise.
 */
final class SelfTest {
    private static final String JOYSTICK_TOPIC = "/DriverSim/game/joystick0";  // the game's virtual controller topic
    private static final double GAME_DRIVE_DISTANCE_M = 0.5;      // meters the robot must cover under the game's stick
    private static final int DRIVER_PORT = 0;                     // driver controller port
    private static final int AXIS_LEFT_Y = 1;                     // Xbox left stick Y; pushing forward is negative
    private static final int BUTTON_B = 2;                        // Xbox B: dynamic launch at the hub
    private static final double LAUNCH_OVERRIDE_WINDOW_S = 0.95;  // seconds; under the launch command's 1 s override
    private static final Angle INTAKE_DEPLOYED_MAX = Degrees.of(16);  // intake counts as deployed below this
    private static final double FLYWHEEL_TARGET_RPM = 3000;           // flywheel test setpoint, RPM
    private static final double FLYWHEEL_TOLERANCE_RPM = 60;          // flywheel test tolerance, RPM
    private static final double HOOD_TARGET_DEGREES = 30;             // hood test setpoint, degrees
    private static final double HOOD_TOLERANCE_DEGREES = 1.5;         // hood test tolerance, degrees
    private static final double DRIVE_DISTANCE_M = 1.0;               // meters the robot must cover in the drive step
    private static final double FEEDING_VOLTS = -1;          // spindex feed voltage below this means it is feeding
    private static final double GAME_WAIT_S = 30;            // seconds to wait for the game's hello
    private static final double ROBOT_LENGTH_M = 0.927;      // robot footprint along its x, bumpers included, meters
    private static final double WALL_TOLERANCE_M = 0.01;     // meters of slack in the collision checks
    private static final double HUB_FACE_X_M = 4.02;         // field x of the blue hub's near face, meters
    private static final double TRENCH_BAR_X_M = 4.54;       // field x of the blue trench bar's near face, meters
    private static final double TRENCH_PASSED_X_M = 5.5;     // field x past the blue trench and bump, meters
    private static final double HOOD_DOWN_MAX_DEGREES = 10;  // hood counts as lowered below this angle, degrees
    private static final double SETTLE_S = 0.5;              // seconds to let the robot stop after a drive
    private static final Pose2d WALL_START_FIELD =
        new Pose2d(1.2, 2.0, Rotation2d.kZero);  // clear lane to the x = 0 wall
    private static final Pose2d HUB_START_FIELD = new Pose2d(3.2, 4.02, Rotation2d.kZero);  // in line with the blue hub
    private static final Pose2d TRENCH_START_FIELD =
        new Pose2d(3.3, 0.634, Rotation2d.kZero);  // blue right trench lane
    private static final Pose2d BUMP_START_FIELD =
        new Pose2d(3.3, 2.4, Rotation2d.kZero);  // in line with the right bump

    private final Robot robot;             // robot under test
    private final DriverSimBridge bridge;  // bridge under test
    private final boolean isWithGame;      // whether a Unity game is attached and its collisions are checked
    private final List<String> results;    // one line per finished step
    private int failureCount;              // steps that failed
    private double driveStartXMeters;      // robot field x at the start of the drive step
    private DoubleArrayPublisher gameJoystickPublisher;  // publishes the game's joystick topic; made with the step
    private boolean hasShotGatingPassed;                 // latched once all three launch checks read true together

    /**
     * Prepares a self-test against a running robot and bridge.
     *
     * @param robot  The robot built by {@link DriverSimMain}.
     * @param bridge The bridge attached to it.
     * @param isWithGame True when a game is expected to attach, adding the collision steps.
     */
    SelfTest(Robot robot, DriverSimBridge bridge, boolean isWithGame) {
        this.robot = robot;
        this.bridge = bridge;
        this.isWithGame = isWithGame;
        this.results = new ArrayList<>();
        this.failureCount = 0;
        this.driveStartXMeters = 0;
        this.gameJoystickPublisher = null;
        this.hasShotGatingPassed = false;
    }

    /** Schedules the whole test on the command scheduler. */
    void schedule() {
        List<Command> steps = new ArrayList<>();

        // enable teleop on the blue alliance
        steps.add(Commands.waitSeconds(1.0));
        steps.add(Commands.runOnce(this::enableTeleopBlue));

        // turret: teleopInit schedules the real zero sequence; it needs the hall switch to finish
        Command noAction = Commands.none();
        steps.add(makeStep("turret zeroes on hall switch", noAction, this::isTurretZeroed, 8));

        // intake arm deploys down to its release angle
        Command slapdown = robot.intake.getSlapdownCommand();
        steps.add(makeStep("intake slapdown reaches 15 deg", slapdown, this::isIntakeDeployed, 4));

        // flywheel reaches its setpoint with the robot's bang-bang + feedforward
        AngularVelocity flywheelTarget = RPM.of(FLYWHEEL_TARGET_RPM);
        Command spinUp = robot.launcher.getLaunchFuel(flywheelTarget);
        steps.add(makeStep("flywheel reaches 3000 RPM", spinUp, this::isFlywheelAtTarget, 4));

        // hood servo feedback follows the commanded angle
        Command raiseHood = robot.hood.run(this::commandHoodToTarget);
        steps.add(makeStep("hood feedback reaches 30 deg", raiseHood, this::isHoodAtTarget, 3));

        // climber runs to each end switch
        Command extend = robot.climber.getFullyExtendCommand();
        steps.add(makeStep("climber reaches upper switch", extend, robot.climber::isAtUpLimit, 5));
        Command retract = robot.climber.getFullyDetractCommand();
        steps.add(makeStep("climber returns to lower switch", retract, robot.climber::isAtDownLimit, 5));

        // driver's left stick forward drives the robot downfield (blue perspective = +x)
        steps.add(Commands.runOnce(this::startDriveForward));
        steps.add(makeStep("left stick drives robot 1 m downfield", Commands.none(), this::hasDrivenDistance, 3));
        steps.add(Commands.runOnce(this::releaseSticks));
        steps.add(Commands.waitSeconds(0.5));

        // the game's virtual controller (NT topic) drives the robot the same way
        steps.add(Commands.runOnce(this::startGameJoystickTest));
        steps.add(makeStep(
            "game joystick drives robot", Commands.run(this::publishGameStickForward), this::hasGameDrivenRobot, 3
        ));
        steps.add(Commands.runOnce(this::stopGameJoystickTest));
        steps.add(Commands.waitSeconds(SETTLE_S));

        // without a game nothing collides: measure how the drivetrain accelerates and stops (report only)
        if (!isWithGame) {
            DriveFeelMeasurement driveFeel = new DriveFeelMeasurement(robot.drivetrain);
            Command measureDriveFeel = driveFeel.makeCommand();
            steps.add(measureDriveFeel);
        }

        // holding B at range: hood, turret and flywheel checks all pass before the 1 s override would kick in
        steps.add(Commands.runOnce(this::startShootFromDefaultPose));
        steps.add(Commands.waitSeconds(0.5));
        steps.add(Commands.runOnce(this::pressB));
        steps.add(makeStep("B: launch gating passes without override", Commands.none(), this::checkShotGating, 3));
        steps.add(makeStep("B: spindex feeds the launcher", Commands.none(), this::isSpindexFeeding, 3));
        steps.add(Commands.runOnce(this::releaseB));

        // a match starts in autonomous, enabled, with the match clock running; aborting disables
        steps.add(Commands.runOnce(this::startMatchNow));
        steps.add(makeStep("match start enables autonomous", Commands.none(), this::isMatchAutoRunning, 2));
        steps.add(Commands.runOnce(this::abortMatchNow));
        steps.add(makeStep("match abort disables robot", Commands.none(), DriverStation::isDisabled, 2));

        // with a game attached: its collisions must reach the robot code's pose
        if (isWithGame) {
            addCollisionSteps(steps);
        }

        // report and exit
        steps.add(Commands.runOnce(this::finish));
        Command[] stepArray = steps.toArray(new Command[0]);
        Command sequence = Commands.sequence(stepArray);
        Command test = sequence.ignoringDisable(true).finallyDo(this::handleTestEnded).withName("Driver-Sim self-test");
        CommandScheduler.getInstance().schedule(test);
    }

    // ---- private stateful helpers

    /**
     * Adds the steps that need the game: it must say hello, then walls, the hub and the trench must stop the robot
     * and the bump must not.
     *
     * @param steps Step list to append to.
     */
    private void addCollisionSteps(List<Command> steps) {
        // the match abort left the robot disabled; the game must have connected
        steps.add(Commands.runOnce(this::enableTeleopBlue));
        steps.add(makeStep("game says hello", Commands.none(), bridge::isGameAttached, GAME_WAIT_S));

        // driving back into the alliance wall
        steps.add(makeDriveStep("wall stops robot", WALL_START_FIELD, 1.0, 2.0, false, this::isFlushWithWall));

        // driving into the hub face
        steps.add(makeDriveStep("hub blocks robot", HUB_START_FIELD, -1.0, 2.0, false, this::isStoppedByHub));

        // through the trench lane with the hood down
        steps.add(
            makeDriveStep("trench passable with hood down", TRENCH_START_FIELD, -1.0, 2.5, false, this::hasPassedTrench)
        );

        // the trench bar blocks a raised hood
        steps.add(
            makeDriveStep("raised hood blocks trench", TRENCH_START_FIELD, -1.0, 2.0, true, this::isStoppedByTrenchBar)
        );

        // bumps are drivable
        steps.add(makeDriveStep("bump passable", BUMP_START_FIELD, -1.0, 2.5, false, this::hasPassedTrench));
    }

    /**
     * Builds one collision step: waits for the hood to reach the wanted state, places the robot, drives with the left
     * stick, releases, lets it settle, then checks where it ended up.
     *
     * @param name          Step description for the report.
     * @param startPoseField Where to place the robot, field frame.
     * @param stickY        Left stick Y axis value; -1 drives downfield on the blue alliance, +1 back toward the wall.
     * @param driveS        Seconds to hold the stick.
     * @param isHoodRaised  True to hold the hood at the test angle during the step; false to wait for it to be lowered.
     * @param check         Pass condition, read after the robot settles.
     * @return Command for the step.
     */
    private Command makeDriveStep(
        String name, Pose2d startPoseField, double stickY, double driveS, boolean isHoodRaised, BooleanSupplier check
    ) {
        // wait for the hood, then place the robot and drive
        BooleanSupplier isHoodReady = this::isHoodLowered;
        if (isHoodRaised) {
            isHoodReady = this::isHoodAtTarget;
        }
        Command waitForHood = Commands.waitUntil(isHoodReady).withTimeout(3);
        Command place = Commands.runOnce(() -> robot.drivetrain.resetPose(startPoseField));
        Command pushStick = Commands.runOnce(() -> pushLeftStick(stickY));
        Command drive = Commands.waitSeconds(driveS);
        Command release = Commands.runOnce(this::releaseSticks);
        Command settle = Commands.waitSeconds(SETTLE_S);
        Command script = Commands.sequence(waitForHood, place, pushStick, drive, release, settle);

        // the hood is held by a proxied command so the step itself holds no subsystem requirements
        Command action = script;
        if (isHoodRaised) {
            Command holdHood = robot.hood.run(this::commandHoodToTarget).asProxy();
            action = Commands.deadline(script, holdHood);
        }
        double[] startS = new double[1];  // written by the step's first command, read by its last
        Command markStart = Commands.runOnce(() -> startS[0] = Timer.getFPGATimestamp());
        Command reportPose = Commands.runOnce(this::printPose);
        Command recordStep = Commands.runOnce(() -> recordResult(name, check, startS[0]));
        return Commands.sequence(markStart, action, reportPose, recordStep);
    }

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
    private Command makeStep(String name, Command action, BooleanSupplier check, double timeoutS) {
        double[] startS = new double[1];  // written by the step's first command, read by its last
        Command markStart = Commands.runOnce(() -> startS[0] = Timer.getFPGATimestamp());
        Command waitForCheck = Commands.waitUntil(check).withTimeout(timeoutS);
        Command proxiedAction = action.asProxy();
        Command runUntilDone = Commands.deadline(waitForCheck, proxiedAction);
        Command recordStep = Commands.runOnce(() -> recordResult(name, check, startS[0]));
        return Commands.sequence(markStart, runUntilDone, recordStep);
    }

    /**
     * Records and prints one step's result.
     *
     * @param name   Step description.
     * @param check  Pass condition, read now.
     * @param startS Robot clock when the step began, seconds.
     */
    private void recordResult(String name, BooleanSupplier check, double startS) {
        boolean didPass = check.getAsBoolean();
        double elapsedS = Timer.getFPGATimestamp() - startS;
        String verdict = "PASS";
        if (!didPass) {
            verdict = "FAIL";
            failureCount++;
        }
        String line = String.format("[Driver-Sim self-test] %s  %-45s %.2f s", verdict, name, elapsedS);
        results.add(line);
        System.out.println(line);
    }

    /** Puts the robot on Blue 1 with an Xbox controller on the driver port and enables teleop. */
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

    /** Remembers where the robot is and pushes the left stick fully forward. */
    private void startDriveForward() {
        Pose2d poseField = robot.drivetrain.getState().Pose;
        driveStartXMeters = poseField.getX();
        DriverStationSim.setJoystickAxis(DRIVER_PORT, AXIS_LEFT_Y, -1.0);
        DriverStationSim.notifyNewData();
    }

    /**
     * Starts the game-joystick step: the bridge applies the game's topic to port 0, and the robot restarts from the
     * default pose with teleop enabled (the earlier steps may have left it disabled or far downfield).
     */
    private void startGameJoystickTest() {
        NetworkTableInstance nt = NetworkTableInstance.getDefault();
        DoubleArrayTopic joystickTopic = nt.getDoubleArrayTopic(JOYSTICK_TOPIC);
        gameJoystickPublisher = joystickTopic.publish();
        robot.drivetrain.resetPose(DriverSimBridge.DEFAULT_START_POSE_FIELD);
        Pose2d poseField = robot.drivetrain.getState().Pose;
        driveStartXMeters = poseField.getX();
        enableTeleopBlue();
        bridge.setSelfTestJoystickOn(true);
    }

    /** Publishes the game's left stick pushed fully forward; run every loop, as the game sends every frame. */
    private void publishGameStickForward() {
        double[] sample = {0, -1, 0, 0, 0, 0, 0, -1};
        gameJoystickPublisher.set(sample);
    }

    /** Ends the game-joystick step: publishes a neutral sample, lets it land, then stops applying the topic. */
    private void stopGameJoystickTest() {
        double[] neutral = {0, 0, 0, 0, 0, 0, 0, -1};
        gameJoystickPublisher.set(neutral);
        bridge.setSelfTestJoystickOn(false);
        gameJoystickPublisher.close();
        releaseSticks();
    }

    /** Prints where the robot ended up, so a failed collision step shows how far off it was. */
    private void printPose() {
        Pose2d poseField = robot.drivetrain.getState().Pose;
        System.out.println(String.format(
            "[Driver-Sim self-test]   robot at (%.3f, %.3f) m, %.1f deg", poseField.getX(), poseField.getY(),
            poseField.getRotation().getDegrees()
        ));
    }

    /**
     * Pushes the left stick along its Y axis.
     *
     * @param stickY Axis value, -1 (forward) to +1 (back).
     */
    private void pushLeftStick(double stickY) {
        DriverStationSim.setJoystickAxis(DRIVER_PORT, AXIS_LEFT_Y, stickY);
        DriverStationSim.notifyNewData();
    }

    /** Centers the left stick. */
    private void releaseSticks() {
        DriverStationSim.setJoystickAxis(DRIVER_PORT, AXIS_LEFT_Y, 0);
        DriverStationSim.notifyNewData();
    }

    /** Places the robot at the default start pose, in shooting range of the blue hub. */
    private void startShootFromDefaultPose() {
        robot.drivetrain.resetPose(DriverSimBridge.DEFAULT_START_POSE_FIELD);
        hasShotGatingPassed = false;
    }

    /** Presses the driver's B button. */
    private void pressB() {
        DriverStationSim.setJoystickButton(DRIVER_PORT, BUTTON_B, true);
        DriverStationSim.notifyNewData();
    }

    /** Releases the driver's B button. */
    private void releaseB() {
        DriverStationSim.setJoystickButton(DRIVER_PORT, BUTTON_B, false);
        DriverStationSim.notifyNewData();
    }

    /** Commands the hood to the test angle; run every loop so the hood's default command stays out. */
    private void commandHoodToTarget() { robot.hood.setHoodAngle(HOOD_TARGET_DEGREES); }

    /** Starts a match through the bridge, as the game's Start Match menu does. */
    private void startMatchNow() {
        double nowS = Timer.getFPGATimestamp();
        bridge.startMatch(nowS);
    }

    /** Aborts the match through the bridge, as the game's Quit to Title does. */
    private void abortMatchNow() {
        double nowS = Timer.getFPGATimestamp();
        bridge.abortMatch(nowS);
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
        boolean isBeforeOverride = launchTimerS < LAUNCH_OVERRIDE_WINDOW_S;
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
            recordResult("self-test ran to completion (it was cancelled)", () -> false, 0);
            finish();
        }
    }

    /** Prints the summary and exits: 0 when every step passed, 1 otherwise. */
    private void finish() {
        int passCount = results.size() - failureCount;
        System.out.println("[Driver-Sim self-test] " + passCount + "/" + results.size() + " steps passed");
        int exitCode = 0;
        if (failureCount > 0) {
            exitCode = 1;
        }
        System.exit(exitCode);
    }

    // ---- checks (read-only)

    /**
     * Self-test pass condition.
     *
     * @return True once the turret has found its hall switch.
     */
    private boolean isTurretZeroed() { return robot.turret.isZeroed; }

    /**
     * Self-test pass condition.
     *
     * @return True when the intake arm is down at its deployed angle.
     */
    private boolean isIntakeDeployed() {
        Angle armAngle = robot.intake.getSlapdownPosition();
        return armAngle.lte(INTAKE_DEPLOYED_MAX);
    }

    /**
     * Self-test pass condition.
     *
     * @return True when the flywheel is within tolerance of the test setpoint.
     */
    private boolean isFlywheelAtTarget() {
        double errorRpm = robot.launcher.getRPM() - FLYWHEEL_TARGET_RPM;
        return Math.abs(errorRpm) < FLYWHEEL_TOLERANCE_RPM;
    }

    /**
     * Self-test pass condition.
     *
     * @return True when the hood's feedback wire reads the test angle.
     */
    private boolean isHoodAtTarget() {
        double errorDegrees = robot.hood.getNormalizedAngle() - HOOD_TARGET_DEGREES;
        return Math.abs(errorDegrees) < HOOD_TOLERANCE_DEGREES;
    }

    /**
     * Self-test pass condition.
     *
     * @return True once the robot has driven the test distance downfield.
     */
    private boolean hasDrivenDistance() {
        Pose2d poseField = robot.drivetrain.getState().Pose;
        return poseField.getX() - driveStartXMeters > DRIVE_DISTANCE_M;
    }

    /**
     * Self-test pass condition.
     *
     * @return True once the robot has driven the test distance downfield under the game's stick.
     */
    private boolean hasGameDrivenRobot() {
        Pose2d poseField = robot.drivetrain.getState().Pose;
        return poseField.getX() - driveStartXMeters > GAME_DRIVE_DISTANCE_M;
    }

    /**
     * Self-test pass condition.
     *
     * @return True while the spindex is feeding balls to the launcher.
     */
    private boolean isSpindexFeeding() {
        MechanismSnapshot snapshot = bridge.getMechanismSnapshot();
        return snapshot.spindexFeedVolts() < FEEDING_VOLTS;
    }

    /**
     * Self-test pass condition.
     *
     * @return True while a match is in enabled autonomous with its clock running.
     */
    private boolean isMatchAutoRunning() {
        boolean isAutoEnabled = DriverStation.isAutonomousEnabled();
        boolean isClockRunning = DriverStation.getMatchTime() < MatchController.AUTO_S;
        return isAutoEnabled && isClockRunning;
    }

    /**
     * Self-test pass condition.
     *
     * @return True when the hood has come down far enough to pass under the trench bar.
     */
    private boolean isHoodLowered() {
        MechanismSnapshot snapshot = bridge.getMechanismSnapshot();
        return snapshot.hoodAngleDegrees() < HOOD_DOWN_MAX_DEGREES;
    }

    /**
     * Self-test pass condition.
     *
     * @return True when the robot's bumper rests against the x = 0 wall rather than inside it.
     */
    private boolean isFlushWithWall() {
        Pose2d poseField = robot.drivetrain.getState().Pose;
        return poseField.getX() >= ROBOT_LENGTH_M / 2 - WALL_TOLERANCE_M;
    }

    /**
     * Self-test pass condition.
     *
     * @return True when the robot is stopped in front of the blue hub rather than inside it.
     */
    private boolean isStoppedByHub() {
        Pose2d poseField = robot.drivetrain.getState().Pose;
        return poseField.getX() <= HUB_FACE_X_M - ROBOT_LENGTH_M / 2 + WALL_TOLERANCE_M;
    }

    /**
     * Self-test pass condition.
     *
     * @return True when the robot is stopped in front of the trench bar rather than under it.
     */
    private boolean isStoppedByTrenchBar() {
        Pose2d poseField = robot.drivetrain.getState().Pose;
        return poseField.getX() <= TRENCH_BAR_X_M - ROBOT_LENGTH_M / 2 + WALL_TOLERANCE_M;
    }

    /**
     * Self-test pass condition.
     *
     * @return True when the robot has driven past the blue trench and bump area.
     */
    private boolean hasPassedTrench() {
        Pose2d poseField = robot.drivetrain.getState().Pose;
        return poseField.getX() > TRENCH_PASSED_X_M;
    }
}
