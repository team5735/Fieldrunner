package driversim.game;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;

import org.junit.jupiter.api.Test;

import driversim.game.GameCommand.Verb;

class GameCommandTest {
    @Test
    void parsesEveryVerb() {
        assertEquals(Verb.START, GameCommand.parse("1 start").verb());
        assertEquals(Verb.ABORT, GameCommand.parse("2 abort").verb());
        assertEquals(Verb.BALL_LAUNCHED, GameCommand.parse("3 ballLaunched").verb());

        GameCommand station = GameCommand.parse("4 station R2");
        assertEquals(Verb.STATION, station.verb());
        assertEquals("R2", station.text());

        GameCommand winner = GameCommand.parse("5 autoWinner B");
        assertEquals(Verb.AUTO_WINNER, winner.verb());
        assertEquals("B", winner.text());

        GameCommand reset = GameCommand.parse("  6   resetPose 2.5 -1 3.14159 ");
        assertEquals(6, reset.seq());
        assertEquals(Verb.RESET_POSE, reset.verb());
        assertArrayEquals(new double[] {2.5, -1, 3.14159}, reset.numbers(), 1e-12);
    }

    @Test
    void rejectsMalformedText() {
        assertNull(GameCommand.parse(null));
        assertNull(GameCommand.parse(""));
        assertNull(GameCommand.parse("start"));
        assertNull(GameCommand.parse("x start"));
        assertNull(GameCommand.parse("1 launch"));
        assertNull(GameCommand.parse("1 start now"));
        assertNull(GameCommand.parse("1 station G1"));
        assertNull(GameCommand.parse("1 autoWinner X"));
        assertNull(GameCommand.parse("1 resetPose 1 2"));
        assertNull(GameCommand.parse("1 resetPose 1 2 NaN"));
        assertNull(GameCommand.parse("1 resetPose a b c"));
    }
}
