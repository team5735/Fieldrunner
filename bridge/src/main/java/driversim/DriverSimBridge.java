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
import driversim.game.MatchController;
import driversim.game.MatchStateLayout;
import driversim.game.MatchStatus;
import driversim.game.PoseNudge;
import driversim.game.RobotStateLayout;
import driversim.mech.MechanismSnapshot;
import driversim.mech.RobotMechanismSim;

/**
 * Sim-only glue between the unmodified robot code and the Driver-Sim game. Each {@link #update} it advances the
 * mechanism sim, applies commands from the game (match control, robot placement, collision push-outs), drives the
 * simulated driver station during a match, and publishes robot and match state over NetworkTables.
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

    private final Robot robot;                     // the robot program; ended through it when the game quits
    private final DrivetrainSubsystem drivetrain;  // the robot's swerve drivetrain (CTRE sim runs its physics)
    private final RobotMechanismSim mechanisms;    // every non-drivetrain mechanism
    private final MatchController match;           // field-system timing
    private final GameLink link;                   // NetworkTables topics to the game
    private double lastUpdateS;                    // robot clock at the previous update, seconds
    private boolean wasFieldControlled;            // whether the previous update had a match driving the DS

    /**
     * Attaches the bridge to a constructed robot and places the robot at the default start pose.
     *
     * @param robot The robot instance the sim is running.
     */
    public DriverSimBridge(Robot robot) {
        this.robot = robot;
        this.drivetrain = robot.drivetrain;
        this.mechanisms = new RobotMechanismSim(robot.spindex);
        this.match = new MatchController(PERIOD_S);
        this.link = new GameLink();
        this.lastUpdateS = Timer.getFPGATimestamp();
        this.wasFieldControlled = false;

        drivetrain.resetPose(DEFAULT_START_POSE_FIELD);
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

        // game commands and collision push-outs
        List<GameCommand> commands = link.takeCommands();
        for (GameCommand command : commands) {
            applyCommand(command, nowS);
        }
        PoseNudge nudge = link.takePoseNudge();
        if (!nudge.isZero()) {
            Pose2d poseField = drivetrain.getState().Pose;
            Pose2d nudgedPoseField = computeNudgedPose(poseField, nudge);
            drivetrain.resetPose(nudgedPoseField);
        }

        // match timing drives the driver station while a match runs
        match.update(nowS);
        MatchStatus status = match.getStatus(nowS);
        updateDriverStation(status);

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

    // ---- private stateful helpers

    /**
     * Carries out one command from the game.
     *
     * @param command Parsed command.
     * @param nowS    Robot clock, seconds.
     */
    private void applyCommand(GameCommand command, double nowS) {
        switch (command.verb()) {
        case START -> match.start(nowS);
        case ABORT -> match.abort(nowS);
        case BALL_LAUNCHED -> mechanisms.applyBallLaunched();
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

    // ---- low-level helpers (static, no side effects)

    /**
     * Applies a push-out to a pose.
     *
     * @param poseField Current pose, field frame.
     * @param nudge     Push-out to add.
     * @return The pushed pose, field frame.
     */
    private static Pose2d computeNudgedPose(Pose2d poseField, PoseNudge nudge) {
        double xMeters = poseField.getX() + nudge.dxFieldMeters();
        double yMeters = poseField.getY() + nudge.dyFieldMeters();
        Rotation2d headingChange = new Rotation2d(nudge.dHeadingRadians());
        Rotation2d heading = poseField.getRotation().plus(headingChange);
        return new Pose2d(xMeters, yMeters, heading);
    }

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
