package driversim.game;

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
 * <li>{@code quit} - the game is closing its session; the robot sim exits cleanly.</li>
 * </ul>
 *
 * @param seq     Sequence number from the game.
 * @param verb    What to do.
 * @param text    The single text argument for station and autoWinner; empty otherwise.
 * @param numbers Numeric arguments for resetPose (x, y, heading); empty otherwise.
 */
public record GameCommand(long seq, Verb verb, String text, double[] numbers) {
    /** Command kinds. */
    public enum Verb {
        START,
        ABORT,
        STATION,
        AUTO_WINNER,
        RESET_POSE,
        BALL_LAUNCHED,
        QUIT
    }

    /**
     * Parses one command string.
     *
     * @param wire Command text as published by the game.
     * @return The command, or null when the text is malformed or the verb is unknown.
     */
    public static GameCommand parse(String wire) {
        if (wire == null) {
            return null;
        }
        String[] parts = wire.trim().split("\\s+");
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

        // verb and its arguments
        String verbText = parts[1];
        if (verbText.equals("start") && parts.length == 2) {
            return new GameCommand(seq, Verb.START, "", new double[0]);
        }
        if (verbText.equals("abort") && parts.length == 2) {
            return new GameCommand(seq, Verb.ABORT, "", new double[0]);
        }
        if (verbText.equals("quit") && parts.length == 2) {
            return new GameCommand(seq, Verb.QUIT, "", new double[0]);
        }
        if (verbText.equals("ballLaunched") && parts.length == 2) {
            return new GameCommand(seq, Verb.BALL_LAUNCHED, "", new double[0]);
        }
        if (verbText.equals("station") && parts.length == 3 && isStationName(parts[2])) {
            return new GameCommand(seq, Verb.STATION, parts[2], new double[0]);
        }
        if (verbText.equals("autoWinner") && parts.length == 3 && isAllianceLetter(parts[2])) {
            return new GameCommand(seq, Verb.AUTO_WINNER, parts[2], new double[0]);
        }
        if (verbText.equals("resetPose") && parts.length == 5) {
            double[] numbers = parseNumbers(parts, 2);
            if (numbers == null) {
                return null;
            }
            return new GameCommand(seq, Verb.RESET_POSE, "", numbers);
        }
        return null;
    }

    // ---- low-level helpers (static, no side effects)

    private static boolean isStationName(String text) {
        return text.matches("[BR][123]");
    }

    private static boolean isAllianceLetter(String text) {
        return text.equals("R") || text.equals("B");
    }

    /**
     * Parses every token from an index onward as a finite double.
     *
     * @param parts Tokens.
     * @param from  First token to parse.
     * @return Parsed numbers, or null when any token is not a finite number.
     */
    private static double[] parseNumbers(String[] parts, int from) {
        double[] numbers = new double[parts.length - from];
        for (int i = from; i < parts.length; i++) {
            double value;
            try {
                value = Double.parseDouble(parts[i]);
            } catch (NumberFormatException e) {
                return null;
            }
            if (!Double.isFinite(value)) {
                return null;
            }
            numbers[i - from] = value;
        }
        return numbers;
    }
}
