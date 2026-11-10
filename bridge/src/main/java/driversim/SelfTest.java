package driversim;

import static edu.wpi.first.units.Units.Degrees;
import static edu.wpi.first.units.Units.RPM;
import static edu.wpi.first.units.Units.Radians;

import edu.wpi.first.hal.AllianceStationID;
import edu.wpi.first.math.MathUtil;
import edu.wpi.first.math.geometry.Pose2d;
import edu.wpi.first.math.geometry.Rotation2d;
import edu.wpi.first.math.geometry.Translation2d;
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

/**
 * Scripted headless check that the sim world and the bridge make the real robot code work in sim: the robot enables,
 * the turret zeroes on its hall switch and the intake deploys with their sensors agreeing with the true mechanisms,
 * the flywheel reaches speed, the hood follows its servo, the climber reaches its switches, the driver's stick drives
 * the robot (directly, and through the game's joystick topic), Back changes the robot's belief but not the true pose,
 * the shot-gating checks all pass, and a match starts in autonomous. In with-game mode it then waits for the Unity game
 * to attach and checks that the game's collisions reach the true pose: walls, hub and trench blocks stop the robot, the
 * robot code's estimate recovers after a wall, a corner hit turns the truth but not the gyro, a raised hood is blocked
 * by the trench bar, and bumps are drivable.
 *
 * <p>
 * Inputs go through the simulated driver station exactly as the GUI's joysticks would. Run it without the sim GUI
 * (the GUI would overwrite the joystick values). Prints a PASS/FAIL line per step and exits the process with 0 when
 * every step passed, 1 otherwise.
 */
final class SelfTest {
    private static final String JOYSTICK_TOPIC = "/DriverSim/game/joystick0";  // the game's virtual controller topic
    private static final double GAME_DRIVE_DISTANCE_M = 0.5;  // meters the robot must cover under the game's stick
    private static final int DRIVER_PORT = 0;                 // driver controller port
    private static final int AXIS_LEFT_Y = 1;                 // Xbox left stick Y; pushing forward is negative
    private static final int BUTTON_B = 2;                    // Xbox B: dynamic launch at the hub
    private static final String VISION_ENABLED_ENTRY = "/vision/enabled";  // robot code's switch for camera fusion
    private static final int BUTTON_BACK = 7;                     // Xbox Back: the robot code's seedFieldCentric
    private static final double LAUNCH_OVERRIDE_WINDOW_S = 0.95;  // seconds; under the launch command's 1 s override
    private static final Angle INTAKE_DEPLOYED_MAX = Degrees.of(16);  // intake counts as deployed below this
    private static final double FLYWHEEL_TARGET_RPM = 3000;           // flywheel test setpoint, RPM
    private static final double FLYWHEEL_TOLERANCE_RPM = 60;          // flywheel test tolerance, RPM
    private static final double HOOD_TARGET_DEGREES = 30;             // hood test setpoint, degrees
    private static final double HOOD_TOLERANCE_DEGREES = 1.5;         // hood test tolerance, degrees
    private static final double DRIVE_DISTANCE_M = 1.0;               // meters the robot must cover in the drive step
    private static final double FEEDING_VOLTS = -1;            // spindex feed voltage below this means it is feeding
    private static final double GAME_WAIT_S = 30;              // seconds to wait for the game's hello
    private static final double ROBOT_LENGTH_M = 0.927;        // robot footprint along its x, bumpers included, meters
    private static final double WALL_TOLERANCE_M = 0.01;       // meters of slack in the collision checks
    private static final double HUB_FACE_X_M = 4.02;           // field x of the blue hub's near face, meters
    private static final double TRENCH_BAR_X_M = 4.54;         // field x of the blue trench bar's near face, meters
    private static final double TRENCH_PASSED_X_M = 5.5;       // field x past the blue trench and bump, meters
    private static final double HOOD_DOWN_MAX_DEGREES = 10;    // hood counts as lowered below this angle, degrees
    private static final double SETTLE_S = 0.5;                // seconds to let the robot stop after a drive
    private static final double TRUTH_UNCHANGED_M = 0.001;     // meters the truth may move while the robot is still
    private static final double TRUTH_UNCHANGED_DEG = 0.1;     // degrees the truth may turn while the robot is still
    private static final double BACK_TURN_MIN_DEG = 45;        // estimate heading change that shows Back was handled
    private static final double ANGLED_WALL_START_DEG = 30;    // heading of the robot when it meets the wall, degrees
    private static final double ANGLED_WALL_TURN_MIN_DEG = 5;  // degrees the true heading must move toward flush
    private static final double GYRO_UNMOVED_DEG = 1.0;        // degrees the gyro may change while the truth is turned
    private static final double BELIEF_TOLERANCE_DEG = 2.0;    // degrees a mechanism's sensor may differ from the truth
    private static final double WALL_ESTIMATE_LOST_M =
        1.0;  // estimate this far from truth at the wall: odometry kept going
    private static final double WALL_RECOVERED_M = 0.2;  // estimate within this of truth counts as recovered
    private static final double WALL_RECOVERY_S = 5;     // seconds the estimate has to recover after driving away
    private static final double WALL_AWAY_STICK = -0.5;  // stick Y that drives away from the wall
    private static final double WALL_AWAY_S = 1.0;       // seconds driving away so the camera sees a robot off the wall
    private static final Pose2d BACK_TEST_START_FIELD =
        new Pose2d(2.0, 4.03, Rotation2d.fromDegrees(90));  // facing sideways so Back has a heading to reset
    private static final Pose2d ANGLED_WALL_START_FIELD = new Pose2d(
        1.2, 2.0, Rotation2d.fromDegrees(ANGLED_WALL_START_DEG)
    );  // same lane as the wall step, turned so a rear corner meets the wall first
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
    private Pose2d truthBeforeBackField;                 // truth pose when Back was pressed
    private Pose2d estimateBeforeBackField;              // the robot code's estimate when Back was pressed
    private double gyroBeforeWallDeg;                    // gyro yaw when the angled wall step began
    private double truthHeadingAfterWallDeg;             // true heading when the angled wall step ended
    private double gyroAfterWallDeg;                     // gyro yaw when the angled wall step ended
    private boolean wasWallFlush;                        // wall step: truth stopped flush at the wall
    private boolean wasEstimateLostAtWall;               // wall step: estimate kept going through the wall

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
        this.truthBeforeBackField = Pose2d.kZero;
        this.estimateBeforeBackField = Pose2d.kZero;
        this.gyroBeforeWallDeg = 0;
        this.truthHeadingAfterWallDeg = 0;
        this.gyroAfterWallDeg = 0;
        this.wasWallFlush = false;
        this.wasEstimateLostAtWall = false;
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
        steps.add(makeStep("turret belief matches truth", Commands.none(), this::isTurretBeliefTrue, 1));

        // intake arm deploys down to its release angle
        Command slapdown = robot.intake.getSlapdownCommand();
        steps.add(makeStep("intake slapdown reaches 15 deg", slapdown, this::isIntakeDeployed, 4));
        steps.add(makeStep("intake belief matches truth", Commands.none(), this::isIntakeBeliefTrue, 1));

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

        // pressing Back resets the robot's belief about its heading, never the real robot
        steps.add(Commands.runOnce(() -> setVisionEnabled(false)));
        steps.add(Commands.runOnce(this::placeForBackTest));
        steps.add(Commands.waitSeconds(SETTLE_S));
        steps.add(Commands.runOnce(this::pressBack));
        steps.add(Commands.waitSeconds(0.2));
        steps.add(Commands.runOnce(this::releaseBack));
        steps.add(Commands.waitSeconds(SETTLE_S));
        steps.add(Commands.runOnce(this::printBackResult));
        steps.add(makeStep("Back leaves truth pose unchanged", Commands.none(), this::isTruthUnchangedByBack, 1));
        steps.add(Commands.runOnce(() -> setVisionEnabled(true)));

        // without a game nothing collides: measure how the drivetrain accelerates and stops (report only)
        if (!isWithGame) {
            DriveFeelMeasurement driveFeel = new DriveFeelMeasurement(robot, bridge);
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
        Command test =
            sequence.ignoringDisable(true).finallyDo(this::handleTestEnded).withName("Fieldrunner self-test");
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

        // the truth stops at the wall while the estimate's odometry keeps going; vision brings the estimate back
        steps.add(makeWallRecoveryStep());

        // a corner-first hit swings the true robot flush with the wall; the gyro does not feel it
        steps.add(makeAngledWallStep());

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
        Command place = Commands.runOnce(() -> bridge.placeRobot(startPoseField));
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
     * Builds the wall step: the robot drives into the blue alliance wall, where the truth must stop while the robot
     * code's estimate keeps going; then it drives a little way off the wall (the robot code ignores camera frames
     * taken within a robot radius of a wall) and holds still, and the estimate must come back to the truth.
     *
     * @return Command for the step.
     */
    private Command makeWallRecoveryStep() {
        // drive into the wall and let the robot stop
        Command place = Commands.runOnce(() -> bridge.placeRobot(WALL_START_FIELD));
        Command pushStick = Commands.runOnce(() -> pushLeftStick(1.0));
        Command drive = Commands.waitSeconds(2.0);
        Command release = Commands.runOnce(this::releaseSticks);
        Command settle = Commands.waitSeconds(SETTLE_S);
        Command markWall = Commands.runOnce(this::recordWallState);
        Command intoWall = Commands.sequence(place, pushStick, drive, release, settle, markWall);

        // drive off the wall and hold still
        Command pushAway = Commands.runOnce(() -> pushLeftStick(WALL_AWAY_STICK));
        Command driveAway = Commands.waitSeconds(WALL_AWAY_S);
        Command stop = Commands.runOnce(this::releaseSticks);
        Command offWall = Commands.sequence(pushAway, driveAway, stop);

        // the estimate has a few seconds to come back
        Command recover =
            makeStep("wall: truth stops, estimate recovers", Commands.none(), this::hasWallStepPassed, WALL_RECOVERY_S);
        Command reportPose = Commands.runOnce(this::printPose);
        return Commands.sequence(intoWall, offWall, recover, reportPose);
    }

    /**
     * Builds the angled wall step: the robot meets the blue alliance wall rear corner first. The game's collision turns
     * the true robot toward flush; the gyro only feels the wheels, so its yaw must not change.
     *
     * @return Command for the step.
     */
    private Command makeAngledWallStep() {
        double[] startS = new double[1];  // written by the step's first command, read by its last
        Command markStart = Commands.runOnce(() -> startS[0] = Timer.getFPGATimestamp());
        Command place = Commands.runOnce(this::placeAtAngledWall);
        Command pushStick = Commands.runOnce(() -> pushLeftStick(1.0));
        Command drive = Commands.waitSeconds(2.0);
        Command release = Commands.runOnce(this::releaseSticks);
        Command settle = Commands.waitSeconds(SETTLE_S);
        Command measure = Commands.runOnce(this::recordAngledWallState);
        BooleanSupplier check = this::hasAngledWallTurnedTruthNotGyro;
        Command recordStep =
            Commands.runOnce(() -> recordResult("angled wall hit turns truth, not gyro", check, startS[0]));
        return Commands.sequence(markStart, place, pushStick, drive, release, settle, measure, recordStep);
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
        Pose2d poseField = getTruthPoseField();
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
        bridge.placeRobot(bridge.getStartPoseField());
        Pose2d poseField = getTruthPoseField();
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

    /** Prints where the robot really ended up and where it thinks it is, so a failed step shows how far off it was. */
    private void printPose() {
        Pose2d truthField = getTruthPoseField();
        Pose2d estimateField = getEstimatePoseField();
        System.out.println(String.format(
            "[Driver-Sim self-test]   robot at (%.3f, %.3f) m, %.1f deg; estimate (%.3f, %.3f) m, %.1f deg",
            truthField.getX(), truthField.getY(), truthField.getRotation().getDegrees(), estimateField.getX(),
            estimateField.getY(), estimateField.getRotation().getDegrees()
        ));
    }

    /**
     * Switches the robot code's camera fusion on or off, the way a driver would from the dashboard. With it off the
     * estimate follows only odometry and the driver's resets, so a heading reset is not undone within a few loops.
     *
     * @param isEnabled True to let the robot code use camera frames.
     */
    private void setVisionEnabled(boolean isEnabled) {
        NetworkTableInstance nt = NetworkTableInstance.getDefault();
        nt.getEntry(VISION_ENABLED_ENTRY).setBoolean(isEnabled);
    }

    /** Places the robot facing sideways, so Back (which resets the robot's heading estimate) has something to do. */
    private void placeForBackTest() { bridge.placeRobot(BACK_TEST_START_FIELD); }

    /** Remembers truth and estimate, then presses Back. */
    private void pressBack() {
        truthBeforeBackField = getTruthPoseField();
        estimateBeforeBackField = getEstimatePoseField();
        DriverStationSim.setJoystickButton(DRIVER_PORT, BUTTON_BACK, true);
        DriverStationSim.notifyNewData();
    }

    /** Prints how far Back moved the truth and turned the estimate, so a failed step shows which side was wrong. */
    private void printBackResult() {
        Pose2d truthField = getTruthPoseField();
        double movedM = truthField.getTranslation().getDistance(truthBeforeBackField.getTranslation());
        double turnedDeg = truthField.getRotation().minus(truthBeforeBackField.getRotation()).getDegrees();
        System.out.println(String.format(
            "[Driver-Sim self-test]   Back: truth moved %.4f m, turned %.3f deg; estimate turned %.1f deg", movedM,
            turnedDeg, getEstimateTurnedByBackDeg()
        ));
    }

    /** Releases the driver's Back button. */
    private void releaseBack() {
        DriverStationSim.setJoystickButton(DRIVER_PORT, BUTTON_BACK, false);
        DriverStationSim.notifyNewData();
    }

    /** Places the robot angled to the wall and remembers the gyro's yaw. */
    private void placeAtAngledWall() {
        bridge.placeRobot(ANGLED_WALL_START_FIELD);
        gyroBeforeWallDeg = robot.drivetrain.getPigeon2().getYaw().getValueAsDouble();
    }

    /** Angled wall step: notes where the truth heading and the gyro ended up. */
    private void recordAngledWallState() {
        truthHeadingAfterWallDeg = getTruthPoseField().getRotation().getDegrees();
        gyroAfterWallDeg = robot.drivetrain.getPigeon2().getYaw().getValueAsDouble();
        printPose();
        System.out.println(String.format(
            "[Driver-Sim self-test]   truth heading %.1f deg (started %.1f), gyro changed %.2f deg",
            truthHeadingAfterWallDeg, ANGLED_WALL_START_DEG, gyroAfterWallDeg - gyroBeforeWallDeg
        ));
    }

    /** Wall step: notes whether the truth stopped at the wall and the estimate went on through it. */
    private void recordWallState() {
        wasWallFlush = isFlushWithWall();
        double estimateErrorM = getEstimateErrorM();
        wasEstimateLostAtWall = estimateErrorM > WALL_ESTIMATE_LOST_M;
        printPose();
        System.out.println(String.format("[Driver-Sim self-test]   estimate error at the wall %.2f m", estimateErrorM));
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
        bridge.placeRobot(bridge.getStartPoseField());
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
     * Where the robot really is in the sim world.
     *
     * @return True pose, field frame.
     */
    private Pose2d getTruthPoseField() { return robot.drivetrain.frGetSimTruthPose(); }

    /**
     * Where the robot code believes it is.
     *
     * @return Pose estimate, field frame.
     */
    private Pose2d getEstimatePoseField() { return robot.drivetrain.getState().Pose; }

    /**
     * How far the robot code's position estimate is from the truth.
     *
     * @return Distance, meters.
     */
    private double getEstimateErrorM() {
        Translation2d truthField = getTruthPoseField().getTranslation();
        Translation2d estimateField = getEstimatePoseField().getTranslation();
        return truthField.getDistance(estimateField);
    }

    /**
     * How far the robot code's heading estimate has turned since Back was pressed.
     *
     * @return Absolute heading change, degrees.
     */
    private double getEstimateTurnedByBackDeg() {
        Rotation2d estimateTurned = getEstimatePoseField().getRotation().minus(estimateBeforeBackField.getRotation());
        return Math.abs(estimateTurned.getDegrees());
    }

    /**
     * Self-test pass condition.
     *
     * @return True when Back changed the robot's heading estimate but the truth did not move or turn.
     */
    private boolean isTruthUnchangedByBack() {
        Pose2d truthField = getTruthPoseField();
        double movedM = truthField.getTranslation().getDistance(truthBeforeBackField.getTranslation());
        Rotation2d turned = truthField.getRotation().minus(truthBeforeBackField.getRotation());
        boolean isTruthStill = movedM < TRUTH_UNCHANGED_M && Math.abs(turned.getDegrees()) < TRUTH_UNCHANGED_DEG;

        // Back must have reached the robot code: its heading estimate turned, even if briefly
        double estimateTurnedDeg = getEstimateTurnedByBackDeg();
        boolean wasBackHandled = estimateTurnedDeg > BACK_TURN_MIN_DEG;
        return isTruthStill && wasBackHandled;
    }

    /**
     * Self-test pass condition.
     *
     * @return True when the true heading moved toward flush by at least the minimum, and the gyro did not follow.
     */
    private boolean hasAngledWallTurnedTruthNotGyro() {
        boolean isFlushOrNearer =
            Math.abs(truthHeadingAfterWallDeg) <= ANGLED_WALL_START_DEG - ANGLED_WALL_TURN_MIN_DEG;
        boolean isGyroUnmoved = Math.abs(gyroAfterWallDeg - gyroBeforeWallDeg) < GYRO_UNMOVED_DEG;
        return isFlushOrNearer && isGyroUnmoved;
    }

    /**
     * Self-test pass condition.
     *
     * @return True when the truth stopped at the wall, the estimate went through it, and the estimate is back near the
     *         truth now.
     */
    private boolean hasWallStepPassed() {
        boolean isEstimateRecovered = getEstimateErrorM() < WALL_RECOVERED_M;
        return wasWallFlush && wasEstimateLostAtWall && isEstimateRecovered;
    }

    /**
     * Self-test pass condition.
     *
     * @return True once the turret has found its hall switch.
     */
    private boolean isTurretZeroed() { return robot.turret.isZeroed; }

    /**
     * Self-test pass condition: after the robot code zeroed the turret, what its sensor says is where the turret
     * really is.
     *
     * @return True when the robot code's turret angle is within tolerance of the sim's true angle.
     */
    private boolean isTurretBeliefTrue() {
        double beliefRad = robot.turret.getAngle().in(Radians);
        double truthRad = robot.turret.frGetSimAngleRobotRel().in(Radians);
        double errorDeg = Math.toDegrees(MathUtil.angleModulus(beliefRad - truthRad));
        return Math.abs(errorDeg) < BELIEF_TOLERANCE_DEG;
    }

    /**
     * Self-test pass condition: the intake sensor agrees with where the arm really is.
     *
     * @return True when the robot code's slapdown angle is within tolerance of the sim's true angle.
     */
    private boolean isIntakeBeliefTrue() {
        double beliefDeg = robot.intake.getSlapdownPosition().in(Degrees);
        double truthDeg = robot.intake.frGetSimArmAngle().in(Degrees);
        return Math.abs(beliefDeg - truthDeg) < BELIEF_TOLERANCE_DEG;
    }

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
        Pose2d poseField = getTruthPoseField();
        return poseField.getX() - driveStartXMeters > DRIVE_DISTANCE_M;
    }

    /**
     * Self-test pass condition.
     *
     * @return True once the robot has driven the test distance downfield under the game's stick.
     */
    private boolean hasGameDrivenRobot() {
        Pose2d poseField = getTruthPoseField();
        return poseField.getX() - driveStartXMeters > GAME_DRIVE_DISTANCE_M;
    }

    /**
     * Self-test pass condition.
     *
     * @return True while the spindex is feeding balls to the launcher.
     */
    private boolean isSpindexFeeding() {
        double feedVolts = robot.spindex.frGetSimFeedVolts();
        return feedVolts < FEEDING_VOLTS;
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
        double hoodDegrees = robot.hood.frGetSimAngle().in(Degrees);
        return hoodDegrees < HOOD_DOWN_MAX_DEGREES;
    }

    /**
     * Self-test pass condition.
     *
     * @return True when the robot's bumper rests against the x = 0 wall rather than inside it.
     */
    private boolean isFlushWithWall() {
        Pose2d poseField = getTruthPoseField();
        return poseField.getX() >= ROBOT_LENGTH_M / 2 - WALL_TOLERANCE_M;
    }

    /**
     * Self-test pass condition.
     *
     * @return True when the robot is stopped in front of the blue hub rather than inside it.
     */
    private boolean isStoppedByHub() {
        Pose2d poseField = getTruthPoseField();
        return poseField.getX() <= HUB_FACE_X_M - ROBOT_LENGTH_M / 2 + WALL_TOLERANCE_M;
    }

    /**
     * Self-test pass condition.
     *
     * @return True when the robot is stopped in front of the trench bar rather than under it.
     */
    private boolean isStoppedByTrenchBar() {
        Pose2d poseField = getTruthPoseField();
        return poseField.getX() <= TRENCH_BAR_X_M - ROBOT_LENGTH_M / 2 + WALL_TOLERANCE_M;
    }

    /**
     * Self-test pass condition.
     *
     * @return True when the robot has driven past the blue trench and bump area.
     */
    private boolean hasPassedTrench() {
        Pose2d poseField = getTruthPoseField();
        return poseField.getX() > TRENCH_PASSED_X_M;
    }
}
