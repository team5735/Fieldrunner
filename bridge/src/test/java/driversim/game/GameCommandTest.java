package driversim.game;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;

import org.junit.jupiter.api.Test;

import edu.wpi.first.math.geometry.Pose2d;

import driversim.game.GameCommand.Verb;

class GameCommandTest {
    @Test
    void parsesVerbsWithoutArguments() {
        GameCommand start = GameCommand.parse("1 start");
        GameCommand abort = GameCommand.parse("2 abort");
        GameCommand ballLaunched = GameCommand.parse("3 ballLaunched");
        GameCommand quit = GameCommand.parse("7 quit");
        GameCommand hello = GameCommand.parse("8 hello");
        assertEquals(Verb.START, start.verb());
        assertEquals(Verb.ABORT, abort.verb());
        assertEquals(Verb.BALL_LAUNCHED, ballLaunched.verb());
        assertEquals(Verb.QUIT, quit.verb());
        assertEquals(Verb.HELLO, hello.verb());
        assertEquals(7, quit.seq());

        assertEquals(Verb.PRACTICE, GameCommand.parse("9 practice").verb());
        assertEquals(Verb.DISABLE, GameCommand.parse("10 disable").verb());
        assertEquals(Verb.PAUSE, GameCommand.parse("11 pause").verb());
        assertEquals(Verb.RESUME, GameCommand.parse("12 resume").verb());
    }

    @Test
    void parsesTextArguments() {
        GameCommand station = GameCommand.parse("4 station R2");
        assertEquals(Verb.STATION, station.verb());
        assertEquals("R2", station.text());

        GameCommand winner = GameCommand.parse("5 autoWinner B");
        assertEquals(Verb.AUTO_WINNER, winner.verb());
        assertEquals("B", winner.text());
    }

    @Test
    void parsesResetPoseInFieldFrame() {
        GameCommand reset = GameCommand.parse("  6   resetPose 2.5 -1 3.14159 ");
        assertEquals(6, reset.seq());
        assertEquals(Verb.RESET_POSE, reset.verb());

        Pose2d poseField = reset.poseField();
        assertEquals(2.5, poseField.getX(), 1e-12);
        assertEquals(-1, poseField.getY(), 1e-12);
        assertEquals(3.14159, poseField.getRotation().getRadians(), 1e-9);
    }

    @Test
    void parsesSpawnFuelWithAnOptionalHeight() {
        GameCommand onCarpet = GameCommand.parse("13 spawnFuel 2.5 -1");
        assertEquals(Verb.SPAWN_FUEL, onCarpet.verb());
        assertEquals(2.5, onCarpet.poseField().getX(), 1e-12);
        assertEquals(-1, onCarpet.poseField().getY(), 1e-12);
        assertEquals(0, onCarpet.heightM(), 1e-12);

        GameCommand dropped = GameCommand.parse("14 spawnFuel 11.9 4.0 3");
        assertEquals(Verb.SPAWN_FUEL, dropped.verb());
        assertEquals(3, dropped.heightM(), 1e-12);
    }

    @Test
    void rejectsMalformedText() {
        String[] malformed = {
            null,
            "",
            "start",
            "x start",
            "1 launch",
            "1 start now",
            "1 pause now",
            "1 practice 1",
            "1 station G1",
            "1 autoWinner X",
            "1 resetPose 1 2",
            "1 resetPose 1 2 NaN",
            "1 resetPose a b c",
            "1 spawnFuel 1",
            "1 spawnFuel a b",
            "1 spawnFuel 1 2 -3",
            "1 spawnFuel 1 2 3 4",
        };
        for (String wire : malformed) {
            GameCommand command = GameCommand.parse(wire);
            assertNull(command, "should reject: " + wire);
        }
    }
}
