package driversim.game;

/**
 * What the game reports about its fuel and scores, decoded from {@code /DriverSim/game/state}.
 *
 * @param hopperCount      Fuel in the robot's hopper.
 * @param ourScore         Fuel scored in the player's own alliance hub.
 * @param autoScore        The part of the score scored during autonomous.
 * @param opponentAutoScore The virtual opponent's autonomous score.
 * @param fieldFuelCount   Fuel resting on the field.
 */
public record GameState(int hopperCount, int ourScore, int autoScore, int opponentAutoScore, int fieldFuelCount) {
    /**
     * Decodes the game's state array.
     *
     * @param values Values laid out per {@link GameStateLayout}; may be null.
     * @return The state, or null when the array is missing, too short or from another protocol version.
     */
    public static GameState parse(double[] values) {
        if (values == null || values.length < GameStateLayout.LENGTH) {
            return null;
        }
        int version = (int) values[GameStateLayout.PROTOCOL_VERSION];
        if (version != GameLink.PROTOCOL_VERSION) {
            return null;
        }
        int hopperCount = (int) values[GameStateLayout.HOPPER_COUNT];
        int ourScore = (int) values[GameStateLayout.OUR_SCORE];
        int autoScore = (int) values[GameStateLayout.AUTO_SCORE];
        int opponentAutoScore = (int) values[GameStateLayout.OPPONENT_AUTO_SCORE];
        int fieldFuelCount = (int) values[GameStateLayout.FIELD_FUEL_COUNT];
        return new GameState(hopperCount, ourScore, autoScore, opponentAutoScore, fieldFuelCount);
    }
}
