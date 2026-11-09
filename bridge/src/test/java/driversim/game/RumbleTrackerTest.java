package driversim.game;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;

import org.junit.jupiter.api.Test;

class RumbleTrackerTest {
    @Test
    void scalesRawValuesToUnitRange() {
        assertEquals(0, RumbleTracker.convertToUnit(0), 1e-12);
        assertEquals(1, RumbleTracker.convertToUnit(65535), 1e-12);
        assertEquals(0.5, RumbleTracker.convertToUnit(32768), 1e-4);
        assertEquals(1, RumbleTracker.convertToUnit(70000), 1e-12);
    }

    @Test
    void reportsOnlyChangesBeyondOneHundredth() {
        RumbleTracker tracker = new RumbleTracker();
        assertNull(tracker.update(0, 0));

        RumbleValues full = tracker.update(65535, 0);
        assertNotNull(full);
        assertEquals(1, full.left(), 1e-12);
        assertEquals(0, full.right(), 1e-12);

        // a change of 1/200 is not reported; 1/50 is
        int halfPercent = 328;
        int twoPercent = 1311;
        assertNull(tracker.update(65535 - halfPercent, 0));
        assertNotNull(tracker.update(65535 - twoPercent, 0));
    }

    @Test
    void alwaysReportsRumbleStopping() {
        RumbleTracker tracker = new RumbleTracker();
        tracker.update(5000, 0);
        RumbleValues stopped = tracker.update(0, 0);
        assertNotNull(stopped);
        assertEquals(0, stopped.left(), 1e-12);
    }
}
