package driversim.game;

/**
 * Index of each value in the {@code /DriverSim/match/state} double array. The Unity side mirrors these indices in
 * {@code MatchStateLayout.cs}; change both together and bump {@link GameLink#PROTOCOL_VERSION}.
 */
public final class MatchStateLayout {
    public static final int PHASE = 0; // MatchController.Phase ordinal
    public static final int MATCH_TIME_S = 1; // seconds left in the period; -1 with no match
    public static final int IS_BLUE_HUB_ACTIVE = 2; // 1 when the blue hub scores
    public static final int IS_RED_HUB_ACTIVE = 3; // 1 when the red hub scores
    public static final int STATION = 4; // 0..2 = Blue 1..3, 3..5 = Red 1..3, -1 unknown
    public static final int AUTO_WINNER = 5; // 0 unknown, 1 blue, 2 red
    public static final int LENGTH = 6; // number of values

    private MatchStateLayout() {
        throw new UnsupportedOperationException("This is a utility class!");
    }
}
