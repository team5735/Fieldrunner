package driversim.game;

import edu.wpi.first.networktables.DoubleArrayPublisher;
import edu.wpi.first.networktables.DoubleArraySubscriber;
import edu.wpi.first.networktables.DoubleArrayTopic;
import edu.wpi.first.networktables.NetworkTableInstance;
import edu.wpi.first.networktables.PubSubOption;
import edu.wpi.first.networktables.StringPublisher;
import edu.wpi.first.networktables.StringSubscriber;
import edu.wpi.first.networktables.StringTopic;

import java.util.ArrayList;
import java.util.List;

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
 * <li>{@code /DriverSim/game/poseCorrection} (double[3]) - {robotTimestampS, xFieldM, yFieldM}: where the robot should
 * have been at that robot-clock time, after the game resolved collisions; see {@link PoseCorrection}.</li>
 * </ul>
 */
public final class GameLink {
    public static final int PROTOCOL_VERSION = 2;       // bump when any topic layout changes
    private static final double STATE_PERIOD_S = 0.01;  // seconds between state sends to the game
    private static final int QUEUE_DEPTH = 128;         // game-to-bridge values buffered between reads
    private static final long SEQ_RESTART_GAP = 1000;   // a sequence this far below the last one is a restarted game

    private final DoubleArrayPublisher robotStatePublisher;        // /DriverSim/robot/state
    private final DoubleArrayPublisher matchStatePublisher;        // /DriverSim/match/state
    private final StringPublisher infoPublisher;                   // /DriverSim/bridge/info
    private final StringSubscriber commandSubscriber;              // /DriverSim/game/command
    private final DoubleArraySubscriber poseCorrectionSubscriber;  // /DriverSim/game/poseCorrection
    private long lastCommandSeq;  // highest command sequence number handled; repeats are dropped

    /**
     * Opens the topics on the default NetworkTables instance (the robot's NT server).
     */
    public GameLink() {
        NetworkTableInstance nt = NetworkTableInstance.getDefault();

        // publish/subscribe options
        PubSubOption statePeriod = PubSubOption.periodic(STATE_PERIOD_S);
        PubSubOption sendAll = PubSubOption.sendAll(true);
        PubSubOption keepDuplicates = PubSubOption.keepDuplicates(true);
        PubSubOption queueDepth = PubSubOption.pollStorage(QUEUE_DEPTH);

        // bridge to game
        DoubleArrayTopic robotStateTopic = nt.getDoubleArrayTopic("/DriverSim/robot/state");
        robotStatePublisher = robotStateTopic.publish(statePeriod, sendAll);
        DoubleArrayTopic matchStateTopic = nt.getDoubleArrayTopic("/DriverSim/match/state");
        matchStatePublisher = matchStateTopic.publish(statePeriod);
        StringTopic infoTopic = nt.getStringTopic("/DriverSim/bridge/info");
        infoPublisher = infoTopic.publish();

        // game to bridge
        StringTopic commandTopic = nt.getStringTopic("/DriverSim/game/command");
        commandSubscriber = commandTopic.subscribe("", sendAll, keepDuplicates, queueDepth);
        DoubleArrayTopic poseCorrectionTopic = nt.getDoubleArrayTopic("/DriverSim/game/poseCorrection");
        poseCorrectionSubscriber = poseCorrectionTopic.subscribe(new double[0], sendAll, keepDuplicates, queueDepth);

        infoPublisher.set("DriverSim bridge protocol " + PROTOCOL_VERSION);
        lastCommandSeq = Long.MIN_VALUE;
    }

    /**
     * Sends the robot state to the game.
     *
     * @param state Values laid out per {@link RobotStateLayout}.
     */
    public void publishRobotState(double[] state) { robotStatePublisher.set(state); }

    /**
     * Sends the match state to the game.
     *
     * @param state Values laid out per {@link MatchStateLayout}.
     */
    public void publishMatchState(double[] state) { matchStatePublisher.set(state); }

    /**
     * Takes every command the game has sent since the last call, oldest first, dropping malformed text and repeated
     * sequence numbers. A sequence number far below the last one (a restarted game) starts a new run.
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

            // drop repeats unless the game restarted its sequence
            long seq = command.seq();
            boolean isRestart = seq < lastCommandSeq - SEQ_RESTART_GAP || seq == 0;
            if (seq <= lastCommandSeq && !isRestart) {
                continue;
            }
            lastCommandSeq = seq;
            commands.add(command);
        }
        return commands;
    }

    /**
     * Takes every pose correction the game has sent since the last call, oldest first, dropping malformed ones.
     *
     * @return New corrections; empty when there are none.
     */
    public List<PoseCorrection> takePoseCorrections() {
        List<PoseCorrection> corrections = new ArrayList<>();
        double[][] queued = poseCorrectionSubscriber.readQueueValues();
        for (double[] values : queued) {
            if (values.length != 3) {
                continue;
            }
            PoseCorrection correction = new PoseCorrection(values[0], values[1], values[2]);
            corrections.add(correction);
        }
        return corrections;
    }
}
