package driversim.game;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;

import org.junit.jupiter.api.Test;

class GameStateTest {
    @Test
    void decodesEveryValue() {
        double[] values = {GameLink.PROTOCOL_VERSION, 12, 37, 9, 7, 448};
        GameState state = GameState.parse(values);
        assertEquals(12, state.hopperCount());
        assertEquals(37, state.ourScore());
        assertEquals(9, state.autoScore());
        assertEquals(7, state.opponentAutoScore());
        assertEquals(448, state.fieldFuelCount());
    }

    @Test
    void rejectsMissingShortOrForeignArrays() {
        assertNull(GameState.parse(null));
        assertNull(GameState.parse(new double[0]));
        assertNull(GameState.parse(new double[] {GameLink.PROTOCOL_VERSION, 1, 2}));
        assertNull(GameState.parse(new double[] {GameLink.PROTOCOL_VERSION - 1, 0, 0, 0, 0, 0}));
    }
}
