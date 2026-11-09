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

    @Test
    void pauseFreezesMatchClockAndDisablesRobot() {
        MatchController match = new MatchController(UPDATE_PERIOD_S);
        match.start(T0);
        runTo(match, T0, T0 + 5);
        match.pause(T0 + 5);

        // a long pause changes nothing
        match.update(T0 + 60);
        MatchStatus paused = match.getStatus(T0 + 60);
        assertTrue(paused.isPaused());
        assertFalse(paused.isEnabled());
        assertTrue(paused.isFieldControlled());
        assertEquals(Phase.AUTO, paused.phase());
        assertEquals(15, paused.matchTimeS(), 1e-9);

        // resuming restarts the clock from the frozen time
        match.resume(T0 + 60);
        MatchStatus resumed = match.getStatus(T0 + 62);
        assertFalse(resumed.isPaused());
        assertTrue(resumed.isEnabled());
        assertEquals(13, resumed.matchTimeS(), 1e-9);
    }

    @Test
    void pauseKeepsRemainingTimeExactAcrossPhaseBoundaries() {
        MatchController match = new MatchController(UPDATE_PERIOD_S);
        match.start(T0);
        runTo(match, T0, T0 + MatchController.AUTO_S - 1);

        // paused during autonomous, resumed 30 s later: autonomous still has 1 s left, then the gap, then teleop
        match.pause(T0 + MatchController.AUTO_S - 1);
        match.resume(T0 + MatchController.AUTO_S + 29);
        double resumeS = T0 + MatchController.AUTO_S + 29;
        MatchStatus auto = runTo(match, resumeS, resumeS + 0.5);
        assertEquals(Phase.AUTO, auto.phase());
        assertEquals(0.5, auto.matchTimeS(), 0.03);
        MatchStatus gap = runTo(match, resumeS + 0.5, resumeS + 2);
        assertEquals(Phase.AUTO_TO_TELEOP, gap.phase());

        // paused in the gap, then in teleop: the time left is the same after each pause
        match.pause(resumeS + 2);
        match.resume(resumeS + 100);
        double teleopS = resumeS + 100 + (MatchController.AUTO_TO_TELEOP_S - 1) + 10;
        MatchStatus teleop = runTo(match, resumeS + 100, teleopS);
        assertEquals(Phase.TELEOP, teleop.phase());
        assertEquals(MatchController.TELEOP_S - 10, teleop.matchTimeS(), 0.03);
        match.pause(teleopS);
        match.resume(teleopS + 500);
        assertEquals(MatchController.TELEOP_S - 10, match.getStatus(teleopS + 500).matchTimeS(), 1e-9);
    }

    @Test
    void pauseJustPastABoundaryBelongsToTheNextPhase() {
        MatchController match = new MatchController(UPDATE_PERIOD_S);
        match.start(T0);

        // the pause arrives after autonomous has run its length but before the controller was updated
        match.pause(T0 + MatchController.AUTO_S + 0.5);
        MatchStatus paused = match.getStatus(T0 + MatchController.AUTO_S + 40);
        assertEquals(Phase.AUTO_TO_TELEOP, paused.phase());
        match.resume(T0 + MatchController.AUTO_S + 40);
        MatchStatus resumed = match.getStatus(T0 + MatchController.AUTO_S + 40 + 1);
        assertEquals(Phase.AUTO_TO_TELEOP, resumed.phase());
        MatchStatus teleop = runTo(match, T0 + MatchController.AUTO_S + 40, T0 + MatchController.AUTO_S + 40 + 5);
        assertEquals(Phase.TELEOP, teleop.phase());
        assertEquals(MatchController.TELEOP_S - 2.5, teleop.matchTimeS(), 0.03);
    }

    @Test
    void startAndAbortClearAPause() {
        MatchController match = new MatchController(UPDATE_PERIOD_S);
        match.start(T0);
        match.pause(T0 + 1);
        match.abort(T0 + 2);
        assertFalse(match.getStatus(T0 + 2).isPaused());

        match.start(T0 + 3);
        MatchStatus started = match.getStatus(T0 + 3);
        assertFalse(started.isPaused());
        assertTrue(started.isEnabled());
    }

    @Test
    void freePracticeEnablesAndPausesTheRobot() {
        MatchController match = new MatchController(UPDATE_PERIOD_S);
        assertFalse(match.getStatus(T0).isEnabled());

        match.setPracticeEnabled(true);
        MatchStatus practice = match.getStatus(T0);
        assertTrue(practice.isEnabled());
        assertFalse(practice.isFieldControlled());

        match.pause(T0 + 1);
        MatchStatus paused = match.getStatus(T0 + 1);
        assertTrue(paused.isPaused());
        assertFalse(paused.isEnabled());

        match.resume(T0 + 2);
        assertTrue(match.getStatus(T0 + 2).isEnabled());

        match.setPracticeEnabled(false);
        assertFalse(match.getStatus(T0 + 3).isEnabled());
    }

    @Test
    void pauseWithNothingRunningIsIgnoredAndDisableClearsPause() {
        MatchController match = new MatchController(UPDATE_PERIOD_S);
        match.pause(T0);
        assertFalse(match.getStatus(T0).isPaused());

        match.setPracticeEnabled(true);
        match.pause(T0 + 1);
        match.setPracticeEnabled(false);
        MatchStatus status = match.getStatus(T0 + 2);
        assertFalse(status.isPaused());
        assertFalse(status.isEnabled());
    }

    @Test
    void practiceCommandsAreIgnoredDuringAMatch() {
        MatchController match = new MatchController(UPDATE_PERIOD_S);
        match.start(T0);
        match.setPracticeEnabled(false);
        assertTrue(match.getStatus(T0 + 1).isEnabled());
    }
}
