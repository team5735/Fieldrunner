package driversim.tools;

import edu.wpi.first.networktables.DoubleArraySubscriber;
import edu.wpi.first.networktables.NetworkTableInstance;
import edu.wpi.first.networktables.StringPublisher;

import driversim.game.MatchController;
import driversim.game.MatchStateLayout;
import driversim.game.RobotStateLayout;

/**
 * Developer tool that connects to a running Driver-Sim robot sim the same way the Unity game does (NT4 client on
 * localhost), prints robot and match state twice a second, and optionally sends game commands.
 *
 * <p>
 * Usage: {@code ./gradlew probeDriverSim -Pargs="[seconds],[command],[command]..."}, comma separated, where each
 * command is a {@link driversim.game.GameCommand} without its sequence number, e.g.
 * {@code -Pargs="30,station R1,start"}. Commands are sent one second apart after connecting.
 */
public final class NtProbe {
    private NtProbe() {
    }

    /**
     * Runs the probe.
     *
     * @param args Seconds to run (default 10), then commands to send.
     * @throws InterruptedException When interrupted while sleeping.
     */
    public static void main(String... args) throws InterruptedException {
        double runS = 10;
        int firstCommand = 0;
        if (args.length > 0 && args[0].matches("[0-9.]+")) {
            runS = Double.parseDouble(args[0]);
            firstCommand = 1;
        }

        // connect as an NT4 client, like the game
        NetworkTableInstance nt = NetworkTableInstance.create();
        nt.startClient4("driversim-probe");
        nt.setServer("localhost");
        DoubleArraySubscriber robotState = nt.getDoubleArrayTopic("/DriverSim/robot/state").subscribe(new double[0]);
        DoubleArraySubscriber matchState = nt.getDoubleArrayTopic("/DriverSim/match/state").subscribe(new double[0]);
        StringPublisher command = nt.getStringTopic("/DriverSim/game/command").publish();

        // print state; send queued commands one per second once connected
        long seq = System.currentTimeMillis() % 1000000;
        int nextCommand = firstCommand;
        double nextCommandS = 1.0;
        double startS = System.nanoTime() / 1e9;
        double nowS = 0;
        while (nowS < runS) {
            Thread.sleep(500);
            nowS = System.nanoTime() / 1e9 - startS;
            if (nt.isConnected() && nextCommand < args.length && nowS >= nextCommandS) {
                String wire = seq + " " + args[nextCommand];
                command.set(wire);
                nt.flush();
                System.out.println("[probe] sent: " + wire);
                seq++;
                nextCommand++;
                nextCommandS = nowS + 1.0;
            }
            double[] robot = robotState.get();
            double[] match = matchState.get();
            System.out.println("[probe] " + formatState(nt.isConnected(), robot, match));
        }
        nt.close();
        System.exit(0);
    }

    // ---- low-level helpers (static, no side effects)

    private static String formatState(boolean isConnected, double[] robot, double[] match) {
        if (!isConnected) {
            return "not connected";
        }
        if (robot.length < RobotStateLayout.LENGTH || match.length < MatchStateLayout.LENGTH) {
            return "connected, waiting for /DriverSim state";
        }
        MatchController.Phase phase = MatchController.Phase.values()[(int) match[MatchStateLayout.PHASE]];
        return String.format(
            "pose=(%.2f, %.2f, %.0f deg) en=%d auto=%d | %s t=%.1f hubs B=%d R=%d station=%d | turret=%.0f deg "
                + "hood=%.0f fly=%.0f rpm arm=%.0f deg roller=%.1f V feed=%.1f V climb=%.2f",
            robot[RobotStateLayout.POSE_X_M],
            robot[RobotStateLayout.POSE_Y_M],
            Math.toDegrees(robot[RobotStateLayout.HEADING_RAD]),
            (int) robot[RobotStateLayout.IS_ENABLED],
            (int) robot[RobotStateLayout.IS_AUTONOMOUS],
            phase,
            match[MatchStateLayout.MATCH_TIME_S],
            (int) match[MatchStateLayout.IS_BLUE_HUB_ACTIVE],
            (int) match[MatchStateLayout.IS_RED_HUB_ACTIVE],
            (int) match[MatchStateLayout.STATION],
            Math.toDegrees(robot[RobotStateLayout.TURRET_ROBOT_REL_RAD]),
            robot[RobotStateLayout.HOOD_DEG],
            robot[RobotStateLayout.FLYWHEEL_RPM],
            robot[RobotStateLayout.INTAKE_ARM_DEG],
            robot[RobotStateLayout.INTAKE_ROLLER_V],
            robot[RobotStateLayout.SPINDEX_FEED_V],
            robot[RobotStateLayout.CLIMBER_EXTENSION]
        );
    }
}
