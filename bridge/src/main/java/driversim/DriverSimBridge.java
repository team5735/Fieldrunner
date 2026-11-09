package driversim;

import java.util.List;

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

import driversim.game.GameCommand;
import driversim.game.GameLink;
import driversim.game.MatchController;
import driversim.game.MatchStateLayout;
import driversim.game.MatchStatus;
import driversim.game.RobotStateLayout;
import driversim.mech.MechanismSnapshot;
import driversim.mech.RobotMechanismSim;
import frc.robot.Robot;
import frc.robot.subsystems.DrivetrainSubsystem;

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
    public static final double PERIOD_S = 0.005; // seconds between bridge updates
    public static final Pose2d DEFAULT_START_POSE = new Pose2d(2.0, 4.03, Rotation2d.kZero); // blue zone, facing hub

    private final DrivetrainSubsystem drivetrain; // the robot's swerve drivetrain (CTRE sim runs its physics)
    private final RobotMechanismSim mechanisms; // every non-drivetrain mechanism
    private final MatchController match; // field-system timing
    private final GameLink link; // NetworkTables topics to the game
    private double lastUpdateS; // robot clock at the previous update, seconds
    private boolean wasFieldControlled; // whether the previous update had a match driving the DS

    /**
     * Attaches the bridge to a constructed robot and places the robot at the default start pose.
     *
     * @param robot The robot instance the sim is running.
     */
    public DriverSimBridge(Robot robot) {
        this.drivetrain = robot.drivetrain;
        this.mechanisms = new RobotMechanismSim(robot.spindex);
        this.match = new MatchController();
        this.link = new GameLink();
        this.lastUpdateS = Timer.getFPGATimestamp();
        this.wasFieldControlled = false;

        drivetrain.resetPose(DEFAULT_START_POSE);
    }

    /** Advances the sim by one bridge period; call from the robot's main loop thread. */
    public void update() {
        // time step
        double nowS = Timer.getFPGATimestamp();
        double dtS = nowS - lastUpdateS;
        lastUpdateS = nowS;

        // mechanisms
        mechanisms.update(RobotController.getBatteryVoltage(), dtS);

        // game commands and collision push-outs
        List<GameCommand> commands = link.takeCommands();
        for (GameCommand command : commands) {
            applyCommand(command, nowS);
        }
        double[] nudge = link.takePoseNudge();
        boolean hasNudge = nudge[0] != 0 || nudge[1] != 0 || nudge[2] != 0;
        if (hasNudge) {
            Pose2d pose = drivetrain.getState().Pose;
            Pose2d nudged = new Pose2d(
                pose.getX() + nudge[0], pose.getY() + nudge[1], pose.getRotation().plus(new Rotation2d(nudge[2]))
            );
            drivetrain.resetPose(nudged);
        }

        // match timing drives the driver station while a match runs
        match.update(nowS);
        MatchStatus status = match.getStatus(nowS);
        applyDriverStation(status);

        // publish to the game
        MechanismSnapshot snapshot = mechanisms.getSnapshot();
        SwerveDriveState driveState = drivetrain.getState();
        double[] robotState = makeRobotState(nowS, driveState, snapshot);
        link.publishRobotState(robotState);
        double[] matchState = makeMatchState(status);
        link.publishMatchState(matchState);
    }

    /**
     * Starts a match as if the game had asked for one.
     *
     * @param nowS Robot clock, seconds.
     */
    public void startMatch(double nowS) {
        match.start(nowS);
    }

    /**
     * Ends any match and returns to free play.
     *
     * @param nowS Robot clock, seconds.
     */
    public void abortMatch(double nowS) {
        match.abort(nowS);
    }

    /**
     * Reports the current match status.
     *
     * @return Match status at the robot clock's current time.
     */
    public MatchStatus getMatchStatus() {
        return match.getStatus(Timer.getFPGATimestamp());
    }

    /**
     * Reports the mechanisms' current state.
     *
     * @return Snapshot as of the last update.
     */
    public MechanismSnapshot getMechanismSnapshot() {
        return mechanisms.getSnapshot();
    }

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
            case AUTO_WINNER -> match.setAutoWinner(command.text().charAt(0));
            case BALL_LAUNCHED -> mechanisms.applyBallLaunched();
            case STATION -> {
                AllianceStationID station = convertStation(command.text());
                DriverStationSim.setAllianceStationId(station);
                DriverStationSim.notifyNewData();
            }
            case RESET_POSE -> {
                double[] numbers = command.numbers();
                Pose2d pose = new Pose2d(numbers[0], numbers[1], new Rotation2d(numbers[2]));
                drivetrain.resetPose(pose);
            }
        }
    }

    /**
     * Writes the match's enable state, mode, time and game message to the simulated driver station while a match runs,
     * and disables the robot once when the match hands control back to free play.
     *
     * @param status Current match status.
     */
    private void applyDriverStation(MatchStatus status) {
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
     * Lays out the robot state array for the game.
     *
     * @param nowS       Robot clock, seconds.
     * @param driveState CTRE drivetrain state: pose (field frame) and chassis speeds (robot frame).
     * @param snapshot   Mechanism state.
     * @return Values laid out per {@link RobotStateLayout}.
     */
    private static double[] makeRobotState(double nowS, SwerveDriveState driveState, MechanismSnapshot snapshot) {
        Pose2d poseField = driveState.Pose;
        Translation2d velocityRobot = new Translation2d(
            driveState.Speeds.vxMetersPerSecond, driveState.Speeds.vyMetersPerSecond
        );
        Translation2d velocityField = velocityRobot.rotateBy(poseField.getRotation());

        double[] state = new double[RobotStateLayout.LENGTH];
        state[RobotStateLayout.PROTOCOL_VERSION] = GameLink.PROTOCOL_VERSION;
        state[RobotStateLayout.TIMESTAMP_S] = nowS;
        state[RobotStateLayout.POSE_X_M] = poseField.getX();
        state[RobotStateLayout.POSE_Y_M] = poseField.getY();
        state[RobotStateLayout.HEADING_RAD] = poseField.getRotation().getRadians();
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
        state[RobotStateLayout.IS_ENABLED] = toDouble(DriverStation.isEnabled());
        state[RobotStateLayout.IS_AUTONOMOUS] = toDouble(DriverStation.isAutonomous());
        return state;
    }

    /**
     * Lays out the match state array for the game.
     *
     * @param status Current match status.
     * @return Values laid out per {@link MatchStateLayout}.
     */
    private static double[] makeMatchState(MatchStatus status) {
        double[] state = new double[MatchStateLayout.LENGTH];
        state[MatchStateLayout.PHASE] = status.phase().ordinal();
        state[MatchStateLayout.MATCH_TIME_S] = status.matchTimeS();
        state[MatchStateLayout.IS_BLUE_HUB_ACTIVE] = toDouble(status.isBlueHubActive());
        state[MatchStateLayout.IS_RED_HUB_ACTIVE] = toDouble(status.isRedHubActive());
        state[MatchStateLayout.STATION] = computeStationIndex();
        state[MatchStateLayout.AUTO_WINNER] = computeAutoWinnerCode(status.gameMessage());
        return state;
    }

    /**
     * Driver station position as a single index.
     *
     * @return 0..2 for Blue 1..3, 3..5 for Red 1..3, -1 when the DS has no station.
     */
    private static int computeStationIndex() {
        if (DriverStation.getAlliance().isEmpty() || DriverStation.getLocation().isEmpty()) {
            return -1;
        }
        int location = DriverStation.getLocation().getAsInt();
        if (DriverStation.getAlliance().get() == Alliance.Red) {
            return 3 + location - 1;
        }
        return location - 1;
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

    private static double toDouble(boolean value) {
        if (value) {
            return 1;
        }
        return 0;
    }
}
