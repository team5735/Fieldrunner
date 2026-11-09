package driversim.game;

/**
 * Decides which joystick values the bridge should apply to the simulated driver station, given when the game's samples
 * arrived. A silent game first reads as neutral sticks (a network hiccup stops the robot), and after a longer silence
 * as gone. Pure timing logic: callers pass the current time in and apply the result themselves.
 */
public final class JoystickLink {
    public static final double NEUTRAL_AFTER_S =
        0.25;                                       // seconds without a sample before sticks and buttons read neutral
    public static final double GONE_AFTER_S = 2.0;  // seconds without a sample before the game counts as gone

    private JoystickSample lastSample;  // newest sample from the game; neutral until the first arrives
    private double lastSampleS;         // time the newest sample arrived, seconds
    private boolean hasSample;          // whether any sample has ever arrived
    private boolean hasReportedGone;    // whether the current silence has already been reported as "gone"

    /** Creates a link that has heard nothing yet. */
    public JoystickLink() {
        this.lastSample = JoystickSample.NEUTRAL;
        this.lastSampleS = 0;
        this.hasSample = false;
        this.hasReportedGone = false;
    }

    /**
     * Records a sample from the game; the game counts as present again.
     *
     * @param sample The sample the game sent.
     * @param nowS   Time it was received, seconds.
     */
    public void accept(JoystickSample sample, double nowS) {
        lastSample = sample;
        lastSampleS = nowS;
        hasSample = true;
        hasReportedGone = false;
    }

    /**
     * Chooses the values to apply to the driver station now.
     *
     * @param nowS Current time, seconds.
     * @return The newest sample, or neutral before any sample and after {@link #NEUTRAL_AFTER_S} of silence.
     */
    public JoystickSample getSampleToApply(double nowS) {
        if (!hasSample) {
            return JoystickSample.NEUTRAL;
        }
        double silenceS = nowS - lastSampleS;
        if (silenceS > NEUTRAL_AFTER_S) {
            return JoystickSample.NEUTRAL;
        }
        return lastSample;
    }

    /**
     * Checks whether the game has been silent long enough to be considered gone (crashed or killed). Reports each
     * silence once; the next sample re-arms it.
     *
     * @param nowS Current time, seconds.
     * @return True exactly once per silence longer than {@link #GONE_AFTER_S}, and only after at least one sample.
     */
    public boolean checkGameGone(double nowS) {
        if (!hasSample || hasReportedGone) {
            return false;
        }
        double silenceS = nowS - lastSampleS;
        if (silenceS > GONE_AFTER_S) {
            hasReportedGone = true;
            return true;
        }
        return false;
    }
}
