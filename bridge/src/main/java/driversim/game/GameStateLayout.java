package driversim.game;

/**
 * Index of each value in the {@code /DriverSim/game/state} double array, which the game publishes. The Unity side
 * mirrors these indices in {@code GameStateLayout} (Protocol.cs); change both together and bump
 * {@link GameLink#PROTOCOL_VERSION}.
 */
public final class GameStateLayout {
    public static final int PROTOCOL_VERSION = 0;     // GameLink.PROTOCOL_VERSION
    public static final int HOPPER_COUNT = 1;         // fuel in the robot's hopper
    public static final int OUR_SCORE = 2;            // fuel scored in the player's own alliance hub
    public static final int AUTO_SCORE = 3;           // the part of the score scored during autonomous
    public static final int OPPONENT_AUTO_SCORE = 4;  // the virtual opponent's autonomous score
    public static final int FIELD_FUEL_COUNT = 5;     // fuel resting on the field
    public static final int LENGTH = 6;               // number of values

    private GameStateLayout() { throw new UnsupportedOperationException("This is a utility class!"); }
}
