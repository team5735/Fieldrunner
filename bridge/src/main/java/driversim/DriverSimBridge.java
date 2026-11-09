package driversim;

import com.ctre.phoenix6.swerve.SwerveDrivetrain.SwerveDriveState;

import edu.wpi.first.hal.AllianceStationID;
import edu.wpi.first.math.geometry.Pose2d;
import edu.wpi.first.math.geometry.Rotation2d;
import edu.wpi.first.math.geometry.Translation2d;
import edu.wpi.first.wpilibj.DriverStation;
import edu.wpi.first.wpilibj.DriverStation.Alliance;
import edu.wpi.first.wpilibj.RobotController;
import edu.wpi.first.wpilibj.Timer;
import edu.wpi.first.wpilibj.simulation.DriverStationSim;

import java.util.List;
import java.util.Optional;
import java.util.OptionalInt;

import frc.robot.Robot;
import frc.robot.subsystems.DrivetrainSubsystem;

import driversim.game.GameCommand;
import driversim.game.GameLink;
import driversim.game.JoystickLink;
import driversim.game.JoystickSample;
import driversim.game.MatchController;
import driversim.game.MatchStateLayout;
import driversim.game.MatchStatus;
import driversim.game.PoseCorrection;
import driversim.game.PoseCorrector;
import driversim.game.RobotStateLayout;
import driversim.game.RumbleTracker;
import driversim.game.RumbleValues;
import driversim.mech.MechanismSnapshot;
import driversim.mech.RobotMechanismSim;

/**
 * Sim-only glue between the unmodified robot code and the Fieldrunner game. Each {@link #update} it advances the
 * mechanism sim, applies commands from the game (match control, robot placement) and the game's pose corrections
 * (collisions), drives the simulated driver station, and publishes robot and match state over NetworkTables.
 *
 * <p>
 * The {@link InputOwner} decides how much of the driver station the bridge owns. With {@link InputOwner#GAME} it is the
 * driver station: the game's virtual Xbox controller becomes joystick port 0, free practice and pauses control the
 * enable state, and the robot's rumble goes back to the game. With any other owner it only drives the driver station
 * during a match and leaves joysticks and free-play enable to the sim GUI or the self-test.
 *
 * <p>
 * Register {@link #update} with {@code TimedRobot.addPeriodic} so it runs on the robot's main loop thread, interleaved
 * with the robot code and never concurrently with it.
 */
public final class DriverSimBridge {
    public static final double PERIOD_S = 0.005;  // seconds between bridge updates
    public static final Pose2d DEFAULT_START_POSE_FIELD = new Pose2d(
        2.0, 4.03, Rotation2d.kZero
    );  // field frame: blue alliance zone, in front of the blue hub, facing downfield

    private static final int JOYSTICK_PORT = 0;        // driver station port of the driver's Xbox controller
    private static final int XINPUT_GAMEPAD_TYPE = 1;  // HAL joystick type of an XInput gamepad
    private static final String JOYSTICK_NAME = "Xbox Controller (Fieldrunner)";  // name shown for port 0

    private final Robot robot;                     // the robot program; ended through it when the game quits
    private final DrivetrainSubsystem drivetrain;  // the robot's swerve drivetrain (CTRE sim runs its physics)
    private final RobotMechanismSim mechanisms;    // every non-drivetrain mechanism
    private final MatchController match;           // field-system timing
    private final GameLink link;                   // NetworkTables topics to the game
    private final InputOwner inputOwner;           // who supplies joystick input and free-play enable
    private final JoystickLink joystickLink;       // timing of the game's joystick samples
    private final RumbleTracker rumbleTracker;     // which robot rumble changes the game needs to hear about
    private final PoseCorrector poseCorrector;     // reconciles the robot pose with the game's corrected poses
    private double lastUpdateS;                    // robot clock at the previous update, seconds
    private boolean wasFieldControlled;            // whether the previous update had a match driving the DS
    private boolean isGameAttached;                // whether a game has said hello
    private boolean isSelfTestJoystickOn;          // SELFTEST owner only: whether the game's joystick is applied

    /**
     * Attaches the bridge to a constructed robot and places the robot at the default start pose.
     *
     * @param robot      The robot instance the sim is running.
     * @param inputOwner Who supplies joystick input and free-play enable for this run.
     */
    public DriverSimBridge(Robot robot, InputOwner inputOwner) {
        this.robot = robot;
        this.drivetrain = robot.drivetrain;
        this.mechanisms = new RobotMechanismSim(robot.spindex);
        this.match = new MatchController(PERIOD_S);
        this.inputOwner = inputOwner;
        this.link = new GameLink(inputOwner);
        this.joystickLink = new JoystickLink();
        this.rumbleTracker = new RumbleTracker();
        this.poseCorrector = new PoseCorrector();
        this.lastUpdateS = Timer.getFPGATimestamp();
        this.wasFieldControlled = false;
        this.isGameAttached = false;
        this.isSelfTestJoystickOn = false;

        drivetrain.resetPose(DEFAULT_START_POSE_FIELD);

        // the game owns the driver station: an Xbox controller on port 0, on the blue 1 station until told otherwise
        if (inputOwner == InputOwner.GAME) {
            declareXboxController();
            DriverStationSim.setAllianceStationId(AllianceStationID.Blue1);
            DriverStationSim.setDsAttached(true);
            DriverStationSim.notifyNewData();
        }
    }

    /** Advances the sim by one bridge period; call from the robot's main loop thread. */
    public void update() {
        // time step
        double nowS = Timer.getFPGATimestamp();
        double dtS = nowS - lastUpdateS;
        lastUpdateS = nowS;

        // mechanisms
        double batteryVolts = RobotController.getBatteryVoltage();
        mechanisms.update(batteryVolts, dtS);

        // game commands
        List<GameCommand> commands = link.takeCommands();
        for (GameCommand command : commands) {
            applyCommand(command, nowS);
        }

        // joystick samples from the game
        List<JoystickSample> samples = link.takeJoystickSamples();
        boolean isJoystickWanted = isSelfTestJoystickOn;
        if (inputOwner == InputOwner.GAME) {
            isJoystickWanted = true;
        }
        if (isJoystickWanted) {
            for (JoystickSample sample : samples) {
                joystickLink.accept(sample, nowS);
            }
        }
        if (inputOwner == InputOwner.GAME && joystickLink.checkGameGone(nowS)) {
            System.out.println("[Driver-Sim] game stopped sending input; pausing");
            match.pause(nowS);
        }

        // collision corrections from the game, reconciled against the pose history
        Pose2d poseField = drivetrain.getState().Pose;
        poseCorrector.record(nowS, poseField.getTranslation());
        List<PoseCorrection> corrections = link.takePoseCorrections();
        Translation2d totalTranslationField = Translation2d.kZero;
        for (PoseCorrection correction : corrections) {
            Translation2d translationField = poseCorrector.computeCorrection(correction);
            totalTranslationField = totalTranslationField.plus(translationField);
        }
        if (totalTranslationField.getNorm() > 0) {
            Translation2d correctedPositionField = poseField.getTranslation().plus(totalTranslationField);
            Pose2d correctedPoseField = new Pose2d(correctedPositionField, poseField.getRotation());
            drivetrain.resetPose(correctedPoseField);
        }

        // match timing drives the driver station while a match runs
        match.update(nowS);
        MatchStatus status = match.getStatus(nowS);
        if (inputOwner == InputOwner.GAME) {
            updateGameDriverStation(status, nowS);
            publishRumble();
        } else {
            updateDriverStation(status);
            if (isSelfTestJoystickOn) {
                applyJoystick(nowS);
            }
        }

        // publish to the game
        MechanismSnapshot snapshot = mechanisms.getSnapshot();
        SwerveDriveState driveState = drivetrain.getState();
        boolean isEnabled = DriverStation.isEnabled();
        boolean isAutonomous = DriverStation.isAutonomous();
        double[] robotState = makeRobotState(nowS, driveState, snapshot, isEnabled, isAutonomous);
        link.publishRobotState(robotState);
        int stationIndex = computeStationIndex();
        double[] matchState = makeMatchState(status, stationIndex);
        link.publishMatchState(matchState);
    }

    /**
     * Starts a match as if the game had asked for one.
     *
     * @param nowS Robot clock, seconds.
     */
    public void startMatch(double nowS) { match.start(nowS); }

    /**
     * Ends any match and returns to free play.
     *
     * @param nowS Robot clock, seconds.
     */
    public void abortMatch(double nowS) { match.abort(nowS); }

    /**
     * Reports the current match status.
     *
     * @return Match status at the robot clock's current time.
     */
    public MatchStatus getMatchStatus() {
        double nowS = Timer.getFPGATimestamp();
        return match.getStatus(nowS);
    }

    /**
     * Reports the mechanisms' current state.
     *
     * @return Snapshot as of the last update.
     */
    public MechanismSnapshot getMechanismSnapshot() { return mechanisms.getSnapshot(); }

    /**
     * Turns the application of the game's joystick on or off in a self-test run, so the self-test can check that the
     * game's controller drives the robot. The self-test owns the driver station otherwise.
     *
     * @param isOn True to apply the game's joystick samples to port 0 every update, false to stop.
     */
    void setSelfTestJoystickOn(boolean isOn) {
        isSelfTestJoystickOn = isOn;
        if (isOn) {
            declareXboxController();
        }
    }

    /**
     * Reports whether a game has connected.
     *
     * @return True once any game has sent {@code hello}.
     */
    public boolean isGameAttached() { return isGameAttached; }

    // ---- private stateful helpers

    /**
     * Carries out one command from the game.
     *
     * @param command Parsed command.
     * @param nowS    Robot clock, seconds.
     */
    private void applyCommand(GameCommand command, double nowS) {
        // the player's pause and practice commands only mean something when the game owns the driver station
        GameCommand.Verb verb = command.verb();
        boolean isPlayerVerb = switch (verb) {
            case PRACTICE, DISABLE, PAUSE, RESUME -> true;
            default -> false;
        };
        if (isPlayerVerb && inputOwner != InputOwner.GAME) {
            System.out.println("[Driver-Sim] ignoring '" + verb + "': input owner is " + inputOwner.getWireName());
            return;
        }

        switch (command.verb()) {
        case START -> match.start(nowS);
        case ABORT -> match.abort(nowS);
        case BALL_LAUNCHED -> mechanisms.applyBallLaunched();
        case HELLO -> isGameAttached = true;
        case PRACTICE -> match.setPracticeEnabled(true);
        case DISABLE -> match.setPracticeEnabled(false);
        case PAUSE -> {
            System.out.println("[Driver-Sim] paused");
            match.pause(nowS);
        }
        case RESUME -> {
            System.out.println("[Driver-Sim] resumed");
            match.resume(nowS);
        }
        case AUTO_WINNER -> {
            String winnerText = command.text();
            char winner = winnerText.charAt(0);
            match.setAutoWinner(winner);
        }
        case STATION -> {
            String stationName = command.text();
            AllianceStationID station = convertStation(stationName);
            DriverStationSim.setAllianceStationId(station);
            DriverStationSim.notifyNewData();
        }
        case RESET_POSE -> {
            Pose2d poseField = command.poseField();
            drivetrain.resetPose(poseField);
            poseCorrector.markDiscontinuity(nowS);
        }
        case QUIT -> {
            // end the robot loop and let WPILib shut down in order (it stops the sim GUI, then the HAL, then exits);
            // System.exit or a signal would tear down native code under the running GUI and crash
            System.out.println("[Driver-Sim] game closed the session; ending the robot program");
            robot.endCompetition();
        }
        }
    }

    /**
     * Writes the match's enable state, mode, time and game message to the simulated driver station while a match runs,
     * and disables the robot once when the match hands control back to free play.
     *
     * @param status Current match status.
     */
    private void updateDriverStation(MatchStatus status) {
        if (status.isFieldControlled()) {
            DriverStationSim.setDsAttached(true);
            DriverStationSim.setFmsAttached(true);
            DriverStationSim.setTest(false);
            DriverStationSim.setAutonomous(status.isAutonomous());
            DriverStationSim.setEnabled(status.isEnabled());
            DriverStationSim.setMatchTime(status.matchTimeS());
            DriverStationSim.setGameSpecificMessage(status.gameMessage());
            DriverStationSim.notifyNewData();
        } else if (wasFieldControlled) {
            DriverStationSim.setEnabled(false);
            DriverStationSim.setFmsAttached(false);
            DriverStationSim.setMatchTime(-1);
            DriverStationSim.setGameSpecificMessage("");
            DriverStationSim.notifyNewData();
        }
        wasFieldControlled = status.isFieldControlled();
    }

    /**
     * Writes the whole driver station for the game-owned input: attached, teleop or autonomous and enable from the
     * match status (free practice and pauses included), match time, game message, and the game's joystick.
     *
     * @param status Current match status.
     * @param nowS   Robot clock, seconds.
     */
    private void updateGameDriverStation(MatchStatus status, double nowS) {
        // mode and enable
        DriverStationSim.setDsAttached(true);
        DriverStationSim.setFmsAttached(status.isFieldControlled());
        DriverStationSim.setTest(false);
        DriverStationSim.setAutonomous(status.isAutonomous());
        DriverStationSim.setEnabled(status.isEnabled());
        DriverStationSim.setMatchTime(status.matchTimeS());
        DriverStationSim.setGameSpecificMessage(status.gameMessage());

        // the game's controller, neutral when it has gone quiet
        JoystickSample sample = joystickLink.getSampleToApply(nowS);
        writeJoystick(sample);
        DriverStationSim.notifyNewData();
    }

    /**
     * Applies the game's joystick to port 0 alone, for a self-test run.
     *
     * @param nowS Robot clock, seconds.
     */
    private void applyJoystick(double nowS) {
        JoystickSample sample = joystickLink.getSampleToApply(nowS);
        writeJoystick(sample);
        DriverStationSim.notifyNewData();
    }

    /** Declares an Xbox controller (6 axes, 10 buttons, 1 D-pad) on port 0 of the simulated driver station. */
    private void declareXboxController() {
        DriverStationSim.setJoystickIsXbox(JOYSTICK_PORT, true);
        DriverStationSim.setJoystickType(JOYSTICK_PORT, XINPUT_GAMEPAD_TYPE);
        DriverStationSim.setJoystickName(JOYSTICK_PORT, JOYSTICK_NAME);
        DriverStationSim.setJoystickAxisCount(JOYSTICK_PORT, JoystickSample.AXIS_COUNT);
        DriverStationSim.setJoystickButtonCount(JOYSTICK_PORT, JoystickSample.BUTTON_COUNT);
        DriverStationSim.setJoystickPOVCount(JOYSTICK_PORT, 1);
    }

    /**
     * Sets port 0's axes, buttons and D-pad; the caller notifies the driver station of new data.
     *
     * @param sample Controller state to write.
     */
    private void writeJoystick(JoystickSample sample) {
        for (int axisIndex = 0; axisIndex < JoystickSample.AXIS_COUNT; axisIndex++) {
            DriverStationSim.setJoystickAxis(JOYSTICK_PORT, axisIndex, sample.getAxis(axisIndex));
        }
        DriverStationSim.setJoystickButtons(JOYSTICK_PORT, sample.buttonBits());
        DriverStationSim.setJoystickPOV(JOYSTICK_PORT, 0, sample.povDeg());
    }

    /** Sends the robot's rumble request to the game when it has changed. */
    private void publishRumble() {
        int leftRaw = DriverStationSim.getJoystickRumble(JOYSTICK_PORT, 0);
        int rightRaw = DriverStationSim.getJoystickRumble(JOYSTICK_PORT, 1);
        RumbleValues rumble = rumbleTracker.update(leftRaw, rightRaw);
        if (rumble != null) {
            link.publishRumble(rumble);
        }
    }

    // ---- low-level helpers (static, no side effects)

    /**
     * Lays out the robot state array for the game.
     *
     * @param nowS         Robot clock, seconds.
     * @param driveState   CTRE drivetrain state: pose (field frame) and chassis speeds (robot frame).
     * @param snapshot     Mechanism state.
     * @param isEnabled    Whether the robot is enabled.
     * @param isAutonomous Whether the robot is in autonomous.
     * @return Values laid out per {@link RobotStateLayout}.
     */
    private static double[] makeRobotState(
        double nowS, SwerveDriveState driveState, MechanismSnapshot snapshot, boolean isEnabled, boolean isAutonomous
    ) {
        // chassis velocity, robot frame to field frame
        Pose2d poseField = driveState.Pose;
        Rotation2d headingField = poseField.getRotation();
        Translation2d velocityRobot =
            new Translation2d(driveState.Speeds.vxMetersPerSecond, driveState.Speeds.vyMetersPerSecond);
        Translation2d velocityField = velocityRobot.rotateBy(headingField);

        // lay out the array
        double[] state = new double[RobotStateLayout.LENGTH];
        state[RobotStateLayout.PROTOCOL_VERSION] = GameLink.PROTOCOL_VERSION;
        state[RobotStateLayout.TIMESTAMP_S] = nowS;
        state[RobotStateLayout.POSE_X_M] = poseField.getX();
        state[RobotStateLayout.POSE_Y_M] = poseField.getY();
        state[RobotStateLayout.HEADING_RAD] = headingField.getRadians();
        state[RobotStateLayout.VELOCITY_X_MPS] = velocityField.getX();
        state[RobotStateLayout.VELOCITY_Y_MPS] = velocityField.getY();
        state[RobotStateLayout.OMEGA_RADPS] = driveState.Speeds.omegaRadiansPerSecond;
        state[RobotStateLayout.TURRET_ROBOT_REL_RAD] = snapshot.turretAngleRobotRelRadians();
        state[RobotStateLayout.HOOD_DEG] = snapshot.hoodAngleDegrees();
        state[RobotStateLayout.FLYWHEEL_RPM] = snapshot.flywheelRpm();
        state[RobotStateLayout.INTAKE_ARM_DEG] = snapshot.intakeArmAngleDegrees();
        state[RobotStateLayout.INTAKE_ROLLER_V] = snapshot.intakeRollerVolts();
        state[RobotStateLayout.SPINDEX_FEED_V] = snapshot.spindexFeedVolts();
        state[RobotStateLayout.SPINDEX_WHEEL_V] = snapshot.spindexWheelVolts();
        state[RobotStateLayout.CLIMBER_EXTENSION] = snapshot.climberExtension();
        state[RobotStateLayout.IS_ENABLED] = convertToDouble(isEnabled);
        state[RobotStateLayout.IS_AUTONOMOUS] = convertToDouble(isAutonomous);
        return state;
    }

    /**
     * Lays out the match state array for the game.
     *
     * @param status       Current match status.
     * @param stationIndex Driver station index from {@link #computeStationIndex}.
     * @return Values laid out per {@link MatchStateLayout}.
     */
    private static double[] makeMatchState(MatchStatus status, int stationIndex) {
        double[] state = new double[MatchStateLayout.LENGTH];
        state[MatchStateLayout.PHASE] = status.phase().ordinal();
        state[MatchStateLayout.MATCH_TIME_S] = status.matchTimeS();
        state[MatchStateLayout.IS_BLUE_HUB_ACTIVE] = convertToDouble(status.isBlueHubActive());
        state[MatchStateLayout.IS_RED_HUB_ACTIVE] = convertToDouble(status.isRedHubActive());
        state[MatchStateLayout.STATION] = stationIndex;
        state[MatchStateLayout.AUTO_WINNER] = computeAutoWinnerCode(status.gameMessage());
        state[MatchStateLayout.IS_PAUSED] = convertToDouble(status.isPaused());
        return state;
    }

    /**
     * Driver station position as a single index, read from the simulated driver station.
     *
     * @return 0..2 for Blue 1..3, 3..5 for Red 1..3, -1 when the DS has no station.
     */
    private static int computeStationIndex() {
        Optional<Alliance> alliance = DriverStation.getAlliance();
        OptionalInt location = DriverStation.getLocation();
        if (alliance.isEmpty() || location.isEmpty()) {
            return -1;
        }
        int stationNumber = location.getAsInt();
        if (alliance.get() == Alliance.Red) {
            return 3 + stationNumber - 1;
        }
        return stationNumber - 1;
    }

    /**
     * Encodes the autonomous winner from the game-specific message.
     *
     * @param gameMessage "B", "R" or empty.
     * @return 1 for blue, 2 for red, 0 when unknown.
     */
    private static int computeAutoWinnerCode(String gameMessage) {
        if (gameMessage.equals("B")) {
            return 1;
        }
        if (gameMessage.equals("R")) {
            return 2;
        }
        return 0;
    }

    /**
     * Converts a station name to the HAL's station ID.
     *
     * @param name "B1".."B3" or "R1".."R3".
     * @return Matching station ID; Unknown for anything else.
     */
    private static AllianceStationID convertStation(String name) {
        return switch (name) {
            case "B1" -> AllianceStationID.Blue1;
            case "B2" -> AllianceStationID.Blue2;
            case "B3" -> AllianceStationID.Blue3;
            case "R1" -> AllianceStationID.Red1;
            case "R2" -> AllianceStationID.Red2;
            case "R3" -> AllianceStationID.Red3;
            default -> AllianceStationID.Unknown;
        };
    }

    /**
     * Converts a flag to the 0/1 the state arrays carry.
     *
     * @param value Flag to convert.
     * @return 1 for true, 0 for false.
     */
    private static double convertToDouble(boolean value) {
        if (value) {
            return 1;
        }
        return 0;
    }
}
