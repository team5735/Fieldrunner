package driversim;

import static edu.wpi.first.units.Units.Degrees;
import static edu.wpi.first.units.Units.Radians;

import edu.wpi.first.hal.AllianceStationID;
import edu.wpi.first.math.geometry.Pose2d;
import edu.wpi.first.math.geometry.Rotation2d;
import edu.wpi.first.math.geometry.Translation2d;
import edu.wpi.first.math.kinematics.ChassisSpeeds;
import edu.wpi.first.wpilibj.DriverStation;
import edu.wpi.first.wpilibj.DriverStation.Alliance;
import edu.wpi.first.wpilibj.Timer;
import edu.wpi.first.wpilibj.simulation.DriverStationSim;

import java.util.List;
import java.util.Optional;
import java.util.OptionalInt;

import frc.robot.Robot;
import frc.robot.subsystems.DrivetrainSubsystem;

import driversim.game.GameCommand;
import driversim.game.GameLink;
import driversim.game.GameState;
import driversim.game.JoystickLink;
import driversim.game.JoystickSample;
import driversim.game.MatchController;
import driversim.game.MatchStateLayout;
import driversim.game.MatchStatus;
import driversim.game.PoseCorrection;
import driversim.game.PoseCorrector;
import driversim.game.PoseDelta;
import driversim.game.RobotStateLayout;
import driversim.game.RumbleTracker;
import driversim.game.RumbleValues;

/**
 * Sim-only link between the robot code's simulated world and the Fieldrunner game. Each {@link #update} it applies commands
 * from the game (match control, robot placement) and the game's pose corrections (collisions move and turn the true
 * pose), drives the simulated driver station, and publishes the true robot state, the robot code's own estimate and
 * match state over NetworkTables. The simulated world itself (true pose, mechanisms, sensors) lives in the robot
 * code's subsystems, each in its own {@code simulationPeriodic}; the bridge reads it through their {@code fr} methods.
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

    private static final int JOYSTICK_PORT = 0;        // driver station port of the driver's Xbox controller
    private static final int XINPUT_GAMEPAD_TYPE = 1;  // HAL joystick type of an XInput gamepad
    private static final String JOYSTICK_NAME = "Xbox Controller (Fieldrunner)";  // name shown for port 0

    private final Robot robot;                     // the robot program; ended through it when the game quits
    private final DrivetrainSubsystem drivetrain;  // the robot's swerve drivetrain (CTRE sim runs its physics)
    private final MatchController match;           // field-system timing
    private final GameLink link;                   // NetworkTables topics to the game
    private final InputOwner inputOwner;           // who supplies joystick input and free-play enable
    private final JoystickLink joystickLink;       // timing of the game's joystick samples
    private final RumbleTracker rumbleTracker;     // which robot rumble changes the game needs to hear about
    private final PoseCorrector poseCorrector;     // reconciles the robot pose with the game's corrected poses
    private final Pose2d startPoseField;           // where the sim world started the robot, field frame
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
        this.match = new MatchController(PERIOD_S);
        this.inputOwner = inputOwner;
        this.link = new GameLink(inputOwner);
        this.joystickLink = new JoystickLink();
        this.rumbleTracker = new RumbleTracker();
        this.poseCorrector = new PoseCorrector();
        this.wasFieldControlled = false;
        this.isGameAttached = false;
        this.isSelfTestJoystickOn = false;

        this.startPoseField = drivetrain.frGetSimTruthPose();
        placeRobot(startPoseField);

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
        double nowS = Timer.getFPGATimestamp();

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

        // collision corrections from the game move the true pose, reconciled against the truth history
        Pose2d truthPoseField = drivetrain.frGetSimTruthPose();
        poseCorrector.record(nowS, truthPoseField);
        List<PoseCorrection> corrections = link.takePoseCorrections();
        PoseDelta total = PoseDelta.NONE;
        for (PoseCorrection correction : corrections) {
            PoseDelta delta = poseCorrector.computeCorrection(correction);
            total = total.plus(delta);
        }
        if (total.isMoving()) {
            drivetrain.frSimPush(total.translationField(), total.rotation());
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
        Pose2d truthNowField = drivetrain.frGetSimTruthPose();
        ChassisSpeeds truthSpeedsRobot = drivetrain.getState().Speeds;
        Pose2d estimatePoseField = drivetrain.getState().Pose;
        boolean isEnabled = DriverStation.isEnabled();
        boolean isAutonomous = DriverStation.isAutonomous();
        double[] robotState =
            makeRobotState(nowS, truthNowField, truthSpeedsRobot, estimatePoseField, robot, isEnabled, isAutonomous);
        link.publishRobotState(robotState);
        int stationIndex = computeStationIndex();
        double[] matchState = makeMatchState(status, stationIndex);
        link.publishMatchState(matchState);
    }

    /**
     * Reports where the sim world started the robot.
     *
     * @return Start pose, field frame.
     */
    public Pose2d getStartPoseField() { return startPoseField; }

    /**
     * Puts the robot at a spot, the way the drive team would: the robot is carried there (truth) and the driver
     * starts from a known place (the robot code's pose estimate is reset to match).
     *
     * @param poseField Where to put the robot, field frame.
     */
    public void placeRobot(Pose2d poseField) {
        drivetrain.frSimPlace(poseField);
        drivetrain.resetPose(poseField);
        double nowS = Timer.getFPGATimestamp();
        poseCorrector.markDiscontinuity(nowS);
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
     * Reads the fuel and score state the game last published.
     *
     * @return The state, or null when no game has published one.
     */
    public GameState getGameState() { return link.getGameState(); }

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
        case BALL_LAUNCHED -> robot.launcher.frSimBallLaunched();
        case HELLO -> isGameAttached = true;
        case SPAWN_FUEL -> {
            // the game reads this command from the topic itself
        }
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
            placeRobot(poseField);
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
     * @param nowS              Robot clock, seconds.
     * @param truthPoseField    True robot pose, field frame.
     * @param truthSpeedsRobot  True chassis speeds, robot frame.
     * @param estimatePoseField The robot code's own pose estimate, field frame.
     * @param robot             The robot, whose subsystems hold their mechanisms' true state.
     * @param isEnabled         Whether the robot is enabled.
     * @param isAutonomous      Whether the robot is in autonomous.
     * @return Values laid out per {@link RobotStateLayout}.
     */
    private static double[] makeRobotState(
        double nowS, Pose2d truthPoseField, ChassisSpeeds truthSpeedsRobot, Pose2d estimatePoseField, Robot robot,
        boolean isEnabled, boolean isAutonomous
    ) {
        // chassis velocity, robot frame to field frame by the true heading
        Rotation2d headingField = truthPoseField.getRotation();
        Translation2d velocityRobot =
            new Translation2d(truthSpeedsRobot.vxMetersPerSecond, truthSpeedsRobot.vyMetersPerSecond);
        Translation2d velocityField = velocityRobot.rotateBy(headingField);

        // lay out the array
        double[] state = new double[RobotStateLayout.LENGTH];
        state[RobotStateLayout.PROTOCOL_VERSION] = GameLink.PROTOCOL_VERSION;
        state[RobotStateLayout.TIMESTAMP_S] = nowS;
        state[RobotStateLayout.POSE_X_M] = truthPoseField.getX();
        state[RobotStateLayout.POSE_Y_M] = truthPoseField.getY();
        state[RobotStateLayout.HEADING_RAD] = headingField.getRadians();
        state[RobotStateLayout.VELOCITY_X_MPS] = velocityField.getX();
        state[RobotStateLayout.VELOCITY_Y_MPS] = velocityField.getY();
        state[RobotStateLayout.OMEGA_RADPS] = truthSpeedsRobot.omegaRadiansPerSecond;
        state[RobotStateLayout.TURRET_ROBOT_REL_RAD] = robot.turret.frGetSimAngleRobotRel().in(Radians);
        state[RobotStateLayout.HOOD_DEG] = robot.hood.frGetSimAngle().in(Degrees);
        state[RobotStateLayout.FLYWHEEL_RPM] = robot.launcher.frGetSimRpm();
        state[RobotStateLayout.INTAKE_ARM_DEG] = robot.intake.frGetSimArmAngle().in(Degrees);
        state[RobotStateLayout.INTAKE_ROLLER_V] = robot.intake.frGetSimRollerVolts();
        state[RobotStateLayout.SPINDEX_FEED_V] = robot.spindex.frGetSimFeedVolts();
        state[RobotStateLayout.SPINDEX_WHEEL_V] = robot.spindex.frGetSimWheelVolts();
        state[RobotStateLayout.CLIMBER_EXTENSION] = robot.climber.frGetSimExtension();
        state[RobotStateLayout.IS_ENABLED] = convertToDouble(isEnabled);
        state[RobotStateLayout.IS_AUTONOMOUS] = convertToDouble(isAutonomous);
        state[RobotStateLayout.ESTIMATE_X_M] = estimatePoseField.getX();
        state[RobotStateLayout.ESTIMATE_Y_M] = estimatePoseField.getY();
        state[RobotStateLayout.ESTIMATE_HEADING_RAD] = estimatePoseField.getRotation().getRadians();
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
