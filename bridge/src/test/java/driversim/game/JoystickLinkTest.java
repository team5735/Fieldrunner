package driversim.game;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

class JoystickLinkTest {
    private static final double T0 = 50;  // arbitrary first sample time, seconds
    private static final JoystickSample FORWARD = new JoystickSample(0, -1, 0, 0, 0, 0, 1, -1);

    @Test
    void isNeutralBeforeAnySample() {
        JoystickLink link = new JoystickLink();
        assertSame(JoystickSample.NEUTRAL, link.getSampleToApply(T0));
        assertFalse(link.checkGameGone(T0 + 100));
    }

    @Test
    void appliesTheNewestSampleThenGoesNeutralAfterQuarterSecond() {
        JoystickLink link = new JoystickLink();
        link.accept(FORWARD, T0);
        assertSame(FORWARD, link.getSampleToApply(T0 + 0.1));
        assertSame(FORWARD, link.getSampleToApply(T0 + JoystickLink.NEUTRAL_AFTER_S - 0.01));
        assertSame(JoystickSample.NEUTRAL, link.getSampleToApply(T0 + JoystickLink.NEUTRAL_AFTER_S + 0.01));

        // a new sample revives it
        link.accept(FORWARD, T0 + 1);
        assertEquals(-1, link.getSampleToApply(T0 + 1.05).leftY(), 1e-12);
    }

    @Test
    void reportsGameGoneOncePerSilenceAfterTwoSeconds() {
        JoystickLink link = new JoystickLink();
        link.accept(FORWARD, T0);
        assertFalse(link.checkGameGone(T0 + JoystickLink.GONE_AFTER_S - 0.01));
        assertTrue(link.checkGameGone(T0 + JoystickLink.GONE_AFTER_S + 0.01));
        assertFalse(link.checkGameGone(T0 + JoystickLink.GONE_AFTER_S + 1));
        assertFalse(link.checkGameGone(T0 + 60));

        // the game comes back and goes quiet again
        link.accept(FORWARD, T0 + 70);
        assertFalse(link.checkGameGone(T0 + 71));
        assertTrue(link.checkGameGone(T0 + 73));
    }
}
