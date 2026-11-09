package driversim.game;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

import driversim.game.MatchController.Phase;

class MatchControllerTest {
    private static final double UPDATE_PERIOD_S = 0.02;  // seconds between updates in these tests
    private static final double T0 = 100;                // arbitrary match start time, seconds
    private static final double TELEOP_START = T0 + MatchController.AUTO_S + MatchController.AUTO_TO_TELEOP_S;

    /** Runs the controller forward to a time in small steps, like the bridge loop would. */
    private static MatchStatus runTo(MatchController match, double fromS, double toS) {
        for (double t = fromS; t <= toS; t += UPDATE_PERIOD_S) {
            match.update(t);
        }
        match.update(toS);
        return match.getStatus(toS);
    }

    @Test
    void freePlayLeavesDriverStationAlone() {
        MatchController match = new MatchController(UPDATE_PERIOD_S);
        MatchStatus status = match.getStatus(5);
        assertEquals(Phase.FREE_PLAY, status.phase());
        assertFalse(status.isFieldControlled());
        assertTrue(status.isBlueHubActive());
        assertTrue(status.isRedHubActive());
    }

    @Test
    void runsAutoGapTeleopThenPost() {
        MatchController match = new MatchController(UPDATE_PERIOD_S);
        match.start(T0);

        MatchStatus auto = runTo(match, T0, T0 + 5);
        assertEquals(Phase.AUTO, auto.phase());
        assertTrue(auto.isEnabled());
        assertTrue(auto.isAutonomous());
        assertEquals(15, auto.matchTimeS(), 0.03);

        MatchStatus gap = runTo(match, T0 + 5, T0 + 21);
        assertEquals(Phase.AUTO_TO_TELEOP, gap.phase());
        assertFalse(gap.isEnabled());

        MatchStatus teleop = runTo(match, T0 + 21, TELEOP_START + 40);
        assertEquals(Phase.TELEOP, teleop.phase());
        assertTrue(teleop.isEnabled());
        assertFalse(teleop.isAutonomous());
        assertEquals(100, teleop.matchTimeS(), 0.03);

        MatchStatus post = runTo(match, TELEOP_START + 40, TELEOP_START + 141);
        assertEquals(Phase.POST_MATCH, post.phase());
        assertFalse(post.isEnabled());
        assertTrue(post.isFieldControlled());
    }

    @Test
    void autoLoserGetsShiftOne() {
        MatchController match = new MatchController(UPDATE_PERIOD_S);
        match.start(T0);
        runTo(match, T0, T0 + 10);
        match.setAutoWinner('B');

        // transition shift: both active
        MatchStatus transition = runTo(match, T0 + 10, TELEOP_START + 5);
        assertEquals("B", transition.gameMessage());
        assertTrue(transition.isBlueHubActive());
        assertTrue(transition.isRedHubActive());

        // shift 1 (match time 130..105): red (the loser) only
        MatchStatus shift1 = runTo(match, TELEOP_START + 5, TELEOP_START + 20);
        assertFalse(shift1.isBlueHubActive());
        assertTrue(shift1.isRedHubActive());

        // shift 2 (105..80): blue only
        MatchStatus shift2 = runTo(match, TELEOP_START + 20, TELEOP_START + 45);
        assertTrue(shift2.isBlueHubActive());
        assertFalse(shift2.isRedHubActive());

        // endgame (< 30): both
        MatchStatus endgame = runTo(match, TELEOP_START + 45, TELEOP_START + 120);
        assertTrue(endgame.isBlueHubActive());
        assertTrue(endgame.isRedHubActive());
    }

    @Test
    void missingAutoWinnerDefaultsSoShiftsStillRun() {
        MatchController match = new MatchController(UPDATE_PERIOD_S);
        match.start(T0);
        MatchStatus teleop = runTo(match, T0, TELEOP_START + 1);
        assertEquals("R", teleop.gameMessage());
    }

    @Test
    void autoWinnerIgnoredOnceTeleopStarts() {
        MatchController match = new MatchController(UPDATE_PERIOD_S);
        match.start(T0);
        match.setAutoWinner('B');
        runTo(match, T0, TELEOP_START + 1);
        match.setAutoWinner('R');
        assertEquals("B", match.getStatus(TELEOP_START + 1).gameMessage());
    }

    @Test
    void abortReturnsToFreePlayAndStartIgnoredMidMatch() {
        MatchController match = new MatchController(UPDATE_PERIOD_S);
        match.start(T0);
        runTo(match, T0, T0 + 3);
        match.start(T0 + 3);
        assertEquals(17, match.getStatus(T0 + 3).matchTimeS(), 0.03);
        match.abort(T0 + 4);
        assertEquals(Phase.FREE_PLAY, match.getStatus(T0 + 4).phase());
    }
}
