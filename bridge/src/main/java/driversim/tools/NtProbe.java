package driversim.tools;

import edu.wpi.first.networktables.DoubleArraySubscriber;
import edu.wpi.first.networktables.DoubleArrayTopic;
import edu.wpi.first.networktables.NetworkTableInstance;
import edu.wpi.first.networktables.StringPublisher;
import edu.wpi.first.networktables.StringTopic;

import driversim.game.MatchController;
import driversim.game.MatchStateLayout;
import driversim.game.RobotStateLayout;

/**
 * Developer tool that connects to a running Fieldrunner robot sim the same way the Unity game does (NT4 client on
 * localhost), prints robot and match state twice a second, and optionally sends game commands.
 *
 * <p>
 * Usage: {@code ./gradlew probeDriverSim -Pargs="[seconds],[command],[command]..."}, comma separated, where each
 * command is a {@link driversim.game.GameCommand} without its sequence number, e.g.
 * {@code -Pargs="30,station R1,start"}. Commands are sent one second apart after connecting.
 */
public final class NtProbe {
    private static final double DEFAULT_RUN_S = 10;       // seconds to run when no duration is given
    private static final long PRINT_PERIOD_MS = 500;      // milliseconds between state lines
    private static final double COMMAND_SPACING_S = 1.0;  // seconds between sent commands

    private NtProbe() { throw new UnsupportedOperationException("This is a utility class!"); }

    /**
     * Runs the probe.
     *
     * @param args Seconds to run (optional, default 10), then commands to send.
     * @throws InterruptedException When interrupted while sleeping.
     */
    public static void main(String... args) throws InterruptedException {
        // arguments
        double runS = DEFAULT_RUN_S;
        int firstCommand = 0;
        if (args.length > 0 && args[0].matches("[0-9.]+")) {
            runS = Double.parseDouble(args[0]);
            firstCommand = 1;
        }

        // connect as an NT4 client, like the game
        NetworkTableInstance nt = NetworkTableInstance.create();
        nt.startClient4("driversim-probe");
        nt.setServer("localhost");
        DoubleArrayTopic robotStateTopic = nt.getDoubleArrayTopic("/DriverSim/robot/state");
        DoubleArraySubscriber robotState = robotStateTopic.subscribe(new double[0]);
        DoubleArrayTopic matchStateTopic = nt.getDoubleArrayTopic("/DriverSim/match/state");
        DoubleArraySubscriber matchState = matchStateTopic.subscribe(new double[0]);
        StringTopic commandTopic = nt.getStringTopic("/DriverSim/game/command");
        StringPublisher command = commandTopic.publish();

        // print state; send queued commands one per second once connected
        long seq = System.currentTimeMillis() % 1000000;
        int nextCommand = firstCommand;
        double nextCommandS = COMMAND_SPACING_S;
        double startS = System.nanoTime() / 1e9;
        double elapsedS = 0;
        while (elapsedS < runS) {
            Thread.sleep(PRINT_PERIOD_MS);
            elapsedS = System.nanoTime() / 1e9 - startS;
            boolean isConnected = nt.isConnected();

            // next command
            boolean hasCommandDue = nextCommand < args.length && elapsedS >= nextCommandS;
            if (isConnected && hasCommandDue) {
                String wire = seq + " " + args[nextCommand];
                command.set(wire);
                nt.flush();
                System.out.println("[probe] sent: " + wire);
                seq++;
                nextCommand++;
                nextCommandS = elapsedS + COMMAND_SPACING_S;
            }

            // state line
            double[] robot = robotState.get();
            double[] match = matchState.get();
            String line = makeStateLine(isConnected, robot, match);
            System.out.println("[probe] " + line);
        }
        nt.close();
        System.exit(0);
    }

    // ---- low-level helpers (static, no side effects)

    /**
     * Describes the bridge's state in one line.
     *
     * @param isConnected Whether the probe is connected to the robot sim.
     * @param robot       Robot state, laid out per {@link RobotStateLayout}; may be empty.
     * @param match       Match state, laid out per {@link MatchStateLayout}; may be empty.
     * @return Human-readable state line.
     */
    private static String makeStateLine(boolean isConnected, double[] robot, double[] match) {
        if (!isConnected) {
            return "not connected";
        }
        if (robot.length < RobotStateLayout.LENGTH || match.length < MatchStateLayout.LENGTH) {
            return "connected, waiting for /DriverSim state";
        }

        // robot
        double xFieldM = robot[RobotStateLayout.POSE_X_M];
        double yFieldM = robot[RobotStateLayout.POSE_Y_M];
        double headingFieldDeg = Math.toDegrees(robot[RobotStateLayout.HEADING_RAD]);
        int isEnabled = (int) robot[RobotStateLayout.IS_ENABLED];
        int isAutonomous = (int) robot[RobotStateLayout.IS_AUTONOMOUS];
        double turretRobotRelDeg = Math.toDegrees(robot[RobotStateLayout.TURRET_ROBOT_REL_RAD]);
        double hoodDeg = robot[RobotStateLayout.HOOD_DEG];
        double flywheelRpm = robot[RobotStateLayout.FLYWHEEL_RPM];
        double intakeArmDeg = robot[RobotStateLayout.INTAKE_ARM_DEG];
        double rollerVolts = robot[RobotStateLayout.INTAKE_ROLLER_V];
        double feedVolts = robot[RobotStateLayout.SPINDEX_FEED_V];
        double climberExtension = robot[RobotStateLayout.CLIMBER_EXTENSION];

        // match
        int phaseIndex = (int) match[MatchStateLayout.PHASE];
        MatchController.Phase phase = MatchController.Phase.values()[phaseIndex];
        double matchTimeS = match[MatchStateLayout.MATCH_TIME_S];
        int isBlueHubActive = (int) match[MatchStateLayout.IS_BLUE_HUB_ACTIVE];
        int isRedHubActive = (int) match[MatchStateLayout.IS_RED_HUB_ACTIVE];
        int station = (int) match[MatchStateLayout.STATION];
        int isPaused = (int) match[MatchStateLayout.IS_PAUSED];

        return String.format(
            "pose=(%.2f, %.2f, %.0f deg) en=%d auto=%d | %s t=%.1f paused=%d hubs B=%d R=%d station=%d | "
                + "turret=%.0f deg "
                + "hood=%.0f fly=%.0f rpm arm=%.0f deg roller=%.1f V feed=%.1f V climb=%.2f",
            xFieldM, yFieldM, headingFieldDeg, isEnabled, isAutonomous, phase, matchTimeS, isPaused, isBlueHubActive,
            isRedHubActive, station, turretRobotRelDeg, hoodDeg, flywheelRpm, intakeArmDeg, rollerVolts, feedVolts,
            climberExtension
        );
    }
}
