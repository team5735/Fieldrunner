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
        assertEquals(Verb.START, start.verb());
        assertEquals(Verb.ABORT, abort.verb());
        assertEquals(Verb.BALL_LAUNCHED, ballLaunched.verb());
        assertEquals(Verb.QUIT, quit.verb());
        assertEquals(7, quit.seq());
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
    void rejectsMalformedText() {
        String[] malformed = {
            null,
            "",
            "start",
            "x start",
            "1 launch",
            "1 start now",
            "1 station G1",
            "1 autoWinner X",
            "1 resetPose 1 2",
            "1 resetPose 1 2 NaN",
            "1 resetPose a b c",
        };
        for (String wire : malformed) {
            GameCommand command = GameCommand.parse(wire);
            assertNull(command, "should reject: " + wire);
        }
    }
}
