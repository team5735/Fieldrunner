package driversim.game;

import java.util.ArrayList;
import java.util.List;

import edu.wpi.first.networktables.DoubleArrayPublisher;
import edu.wpi.first.networktables.DoubleArraySubscriber;
import edu.wpi.first.networktables.NetworkTableInstance;
import edu.wpi.first.networktables.PubSubOption;
import edu.wpi.first.networktables.StringPublisher;
import edu.wpi.first.networktables.StringSubscriber;

/**
 * NetworkTables 4 link between the bridge and the Unity game. All topics live under {@code /DriverSim}; the game
 * connects as an ordinary NT4 client, so AdvantageScope and dashboards can watch the same topics.
 *
 * <p>
 * Bridge to game:
 * <ul>
 * <li>{@code /DriverSim/robot/state} (double[]) - robot pose and mechanisms; layout in {@link RobotStateLayout}.</li>
 * <li>{@code /DriverSim/match/state} (double[]) - match phase and hubs; layout in {@link MatchStateLayout}.</li>
 * <li>{@code /DriverSim/bridge/info} (string) - protocol version, for the game to check compatibility.</li>
 * </ul>
 * Game to bridge:
 * <ul>
 * <li>{@code /DriverSim/game/command} (string) - discrete commands, see {@link GameCommand}.</li>
 * <li>{@code /DriverSim/game/poseNudge} (double[3]) - collision push-out {dx, dy, dHeading} in meters/radians, field
 * frame, added to the robot's current pose.</li>
 * </ul>
 */
public final class GameLink {
    public static final int PROTOCOL_VERSION = 1; // bump when any topic layout changes
    private static final double STATE_PERIOD_S = 0.01; // seconds between state sends to the game
    private static final int QUEUE_DEPTH = 128; // game-to-bridge values buffered between reads

    private final DoubleArrayPublisher robotStatePublisher; // /DriverSim/robot/state
    private final DoubleArrayPublisher matchStatePublisher; // /DriverSim/match/state
    private final StringPublisher infoPublisher; // /DriverSim/bridge/info
    private final StringSubscriber commandSubscriber; // /DriverSim/game/command
    private final DoubleArraySubscriber poseNudgeSubscriber; // /DriverSim/game/poseNudge
    private long lastCommandSeq; // highest command sequence number handled; repeats are dropped

    /**
     * Opens the topics on the default NetworkTables instance (the robot's NT server).
     */
    public GameLink() {
        NetworkTableInstance nt = NetworkTableInstance.getDefault();
        robotStatePublisher = nt.getDoubleArrayTopic("/DriverSim/robot/state")
            .publish(PubSubOption.periodic(STATE_PERIOD_S), PubSubOption.sendAll(true));
        matchStatePublisher = nt.getDoubleArrayTopic("/DriverSim/match/state")
            .publish(PubSubOption.periodic(STATE_PERIOD_S));
        infoPublisher = nt.getStringTopic("/DriverSim/bridge/info").publish();
        commandSubscriber = nt.getStringTopic("/DriverSim/game/command")
            .subscribe(
                "",
                PubSubOption.sendAll(true),
                PubSubOption.keepDuplicates(true),
                PubSubOption.pollStorage(QUEUE_DEPTH)
            );
        poseNudgeSubscriber = nt.getDoubleArrayTopic("/DriverSim/game/poseNudge")
            .subscribe(
                new double[0],
                PubSubOption.sendAll(true),
                PubSubOption.keepDuplicates(true),
                PubSubOption.pollStorage(QUEUE_DEPTH)
            );
        infoPublisher.set("DriverSim bridge protocol " + PROTOCOL_VERSION);
        lastCommandSeq = Long.MIN_VALUE;
    }

    /**
     * Sends the robot state to the game.
     *
     * @param state Values laid out per {@link RobotStateLayout}.
     */
    public void publishRobotState(double[] state) {
        robotStatePublisher.set(state);
    }

    /**
     * Sends the match state to the game.
     *
     * @param state Values laid out per {@link MatchStateLayout}.
     */
    public void publishMatchState(double[] state) {
        matchStatePublisher.set(state);
    }

    /**
     * Takes every command the game has sent since the last call, oldest first, dropping malformed text and repeated
     * sequence numbers. Restarting the game resets its sequence, so a sequence number far below the last one starts
     * a new run instead of being dropped.
     *
     * @return New commands; empty when there are none.
     */
    public List<GameCommand> takeCommands() {
        List<GameCommand> commands = new ArrayList<>();
        String[] wires = commandSubscriber.readQueueValues();
        for (String wire : wires) {
            GameCommand command = GameCommand.parse(wire);
            if (command == null) {
                continue;
            }
            boolean isRestart = command.seq() < lastCommandSeq - 1000 || command.seq() == 0;
            if (command.seq() <= lastCommandSeq && !isRestart) {
                continue;
            }
            lastCommandSeq = command.seq();
            commands.add(command);
        }
        return commands;
    }

    /**
     * Takes and sums every collision push-out the game has sent since the last call.
     *
     * @return Total {dx, dy, dHeading}, meters and radians, field frame; all zero when there are none.
     */
    public double[] takePoseNudge() {
        double[] total = new double[3];
        double[][] nudges = poseNudgeSubscriber.readQueueValues();
        for (double[] nudge : nudges) {
            if (nudge.length != 3) {
                continue;
            }
            total[0] += nudge[0];
            total[1] += nudge[1];
            total[2] += nudge[2];
        }
        return total;
    }
}
