package driversim.game;

import edu.wpi.first.math.geometry.Pose2d;
import edu.wpi.first.math.geometry.Rotation2d;

/**
 * One command from the game to the bridge, parsed from the {@code /DriverSim/game/command} string topic.
 *
 * <p>
 * Wire format: {@code "<seq> <verb> [args...]"}, space separated, where seq is an integer the game increments with
 * every command so repeats of the same text are still distinct. Verbs:
 * <ul>
 * <li>{@code start} - start a match.</li>
 * <li>{@code abort} - end any match and return to free play.</li>
 * <li>{@code station <B1|B2|B3|R1|R2|R3>} - set the alliance station.</li>
 * <li>{@code autoWinner <R|B>} - report which alliance scored more in autonomous.</li>
 * <li>{@code resetPose <xMeters> <yMeters> <headingRadians>} - place the robot, field frame (wpiBlue origin).</li>
 * <li>{@code ballLaunched} - a ball left the launcher; the flywheel loses some speed.</li>
 * <li>{@code hello} - the game has connected (sent again after every reconnect); the bridge notes a game is
 * attached.</li>
 * <li>{@code quit} - the game is closing its session; the robot sim exits cleanly.</li>
 * </ul>
 *
 * @param seq       Sequence number from the game.
 * @param verb      What to do.
 * @param text      Station name for STATION ("B1".."R3"), alliance letter for AUTO_WINNER ("R" or "B"); empty
 *                  otherwise.
 * @param poseField Where to place the robot for RESET_POSE, field frame; null otherwise.
 */
public record GameCommand(long seq, Verb verb, String text, Pose2d poseField) {
    /** Command kinds. */
    public enum Verb { START, ABORT, STATION, AUTO_WINNER, RESET_POSE, BALL_LAUNCHED, HELLO, QUIT }

    /**
     * Parses one command string.
     *
     * @param wire Command text as published by the game; may be null.
     * @return The command, or null when the text is malformed or the verb is unknown.
     */
    public static GameCommand parse(String wire) {
        if (wire == null) {
            return null;
        }
        String trimmed = wire.trim();
        String[] parts = trimmed.split("\\s+");
        if (parts.length < 2) {
            return null;
        }

        // sequence number
        long seq;
        try {
            seq = Long.parseLong(parts[0]);
        } catch (NumberFormatException e) {
            return null;
        }

        // verbs without arguments
        String verbText = parts[1];
        boolean hasNoArguments = parts.length == 2;
        if (verbText.equals("start") && hasNoArguments) {
            return new GameCommand(seq, Verb.START, "", null);
        }
        if (verbText.equals("abort") && hasNoArguments) {
            return new GameCommand(seq, Verb.ABORT, "", null);
        }
        if (verbText.equals("ballLaunched") && hasNoArguments) {
            return new GameCommand(seq, Verb.BALL_LAUNCHED, "", null);
        }
        if (verbText.equals("hello") && hasNoArguments) {
            return new GameCommand(seq, Verb.HELLO, "", null);
        }
        if (verbText.equals("quit") && hasNoArguments) {
            return new GameCommand(seq, Verb.QUIT, "", null);
        }

        // verbs with one text argument
        boolean hasOneArgument = parts.length == 3;
        if (verbText.equals("station") && hasOneArgument && isStationName(parts[2])) {
            return new GameCommand(seq, Verb.STATION, parts[2], null);
        }
        if (verbText.equals("autoWinner") && hasOneArgument && isAllianceLetter(parts[2])) {
            return new GameCommand(seq, Verb.AUTO_WINNER, parts[2], null);
        }

        // robot placement
        if (verbText.equals("resetPose") && parts.length == 5) {
            Pose2d poseField = convertToPose(parts[2], parts[3], parts[4]);
            if (poseField == null) {
                return null;
            }
            return new GameCommand(seq, Verb.RESET_POSE, "", poseField);
        }
        return null;
    }

    // ---- low-level helpers (static, no side effects)

    /**
     * Whether text names a driver station.
     *
     * @param text Candidate station name.
     * @return True for "B1".."B3" and "R1".."R3".
     */
    private static boolean isStationName(String text) { return text.matches("[BR][123]"); }

    /**
     * Whether text is an alliance letter.
     *
     * @param text Candidate letter.
     * @return True for "R" and "B".
     */
    private static boolean isAllianceLetter(String text) { return text.equals("R") || text.equals("B"); }

    /**
     * Builds a field-frame pose from three number tokens.
     *
     * @param xText       Field x, meters.
     * @param yText       Field y, meters.
     * @param headingText Field heading, radians, counter-clockwise positive.
     * @return The pose, or null when any token is not a finite number.
     */
    private static Pose2d convertToPose(String xText, String yText, String headingText) {
        double xMeters;
        double yMeters;
        double headingRadians;
        try {
            xMeters = Double.parseDouble(xText);
            yMeters = Double.parseDouble(yText);
            headingRadians = Double.parseDouble(headingText);
        } catch (NumberFormatException e) {
            return null;
        }
        boolean isFinite = Double.isFinite(xMeters) && Double.isFinite(yMeters) && Double.isFinite(headingRadians);
        if (!isFinite) {
            return null;
        }
        Rotation2d heading = new Rotation2d(headingRadians);
        return new Pose2d(xMeters, yMeters, heading);
    }
}
