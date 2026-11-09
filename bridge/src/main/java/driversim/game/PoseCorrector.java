package driversim.game;

import edu.wpi.first.math.geometry.Translation2d;

import java.util.ArrayList;
import java.util.List;

/**
 * Reconciles the robot code's pose with the game's corrected poses, which arrive a network round trip late. It carries
 * its own history: a ring of the last {@link #HISTORY_S} seconds of robot poses, each with the total translation the
 * bridge had applied through corrections at that moment.
 *
 * <p>
 * Call {@link #record} once per bridge update with the robot's pose, then {@link #computeCorrection} for each
 * correction taken from the game. The result is the translation to add to the robot's pose now so it matches what the
 * game decided at the correction's timestamp. The corrector tracks the offset it has asked for as an absolute total, so
 * corrections computed from older, not-yet-corrected poses do not count the same push twice.
 */
public final class PoseCorrector {
    public static final double HISTORY_S = 2.0;                // seconds of pose history kept
    public static final double MAX_PLAUSIBLE_SPEED_MPS = 8.0;  // faster pose jumps are outside resets (m/s)
    public static final double MIN_CORRECTION_M = 0.001;       // translations shorter than this are ignored (m)
    public static final double DISCONTINUITY_GUARD_S = 0.05;   // corrections stamped this soon after a jump are ignored

    /** One recorded bridge update. */
    private record Sample(double timestampS, Translation2d positionField, Translation2d offsetField) {}

    private final List<Sample> history;  // oldest first
    private Translation2d offsetField;   // total translation applied through corrections so far, field frame, meters
    private double lastAppliedS;         // timestamp of the last correction applied, robot clock, seconds
    private double lastDiscontinuityS;   // robot clock time of the last outside pose jump, seconds

    /** Creates a corrector with no history and no applied corrections. */
    public PoseCorrector() {
        this.history = new ArrayList<>();
        this.offsetField = Translation2d.kZero;
        this.lastAppliedS = Double.NEGATIVE_INFINITY;
        this.lastDiscontinuityS = Double.NEGATIVE_INFINITY;
    }

    /**
     * Records the robot's pose for this bridge update, and notes an outside reset when the pose jumped farther than
     * the robot could plausibly move (accounting for corrections this corrector already asked for).
     *
     * @param timestampS Robot clock, seconds; must not decrease between calls (a decrease clears the history).
     * @param poseField  Robot position as the robot code reports it now, field frame, meters.
     */
    public void record(double timestampS, Translation2d poseField) {
        // compare with where the previous sample should have moved to
        if (!history.isEmpty()) {
            Sample previous = history.get(history.size() - 1);
            double dtS = timestampS - previous.timestampS();
            Translation2d appliedSinceField = offsetField.minus(previous.offsetField());
            Translation2d expectedField = previous.positionField().plus(appliedSinceField);
            double jumpM = poseField.getDistance(expectedField);
            double plausibleM = MAX_PLAUSIBLE_SPEED_MPS * dtS + MIN_CORRECTION_M;
            if (dtS <= 0) {
                history.clear();
                lastDiscontinuityS = timestampS;
                lastAppliedS = Double.NEGATIVE_INFINITY;
            } else if (jumpM > plausibleM) {
                lastDiscontinuityS = timestampS;
            }
        }

        // append and drop what is too old
        history.add(new Sample(timestampS, poseField, offsetField));
        while (timestampS - history.get(0).timestampS() > HISTORY_S) {
            history.remove(0);
        }
    }

    /**
     * Marks that something outside the corrector moved the robot's pose at a time, such as a {@code resetPose} command.
     * Corrections for states older than that time are ignored afterwards.
     *
     * @param timestampS Robot clock time of the reset, seconds.
     */
    public void markDiscontinuity(double timestampS) { lastDiscontinuityS = timestampS; }

    /**
     * Works out how far to move the robot's pose now so it matches the game's decision at the correction's time, and
     * remembers the total offset that implies. The correction is ignored (zero result) when it is not newer than the
     * last applied one, predates the last pose discontinuity by less than {@link #DISCONTINUITY_GUARD_S} (the game may
     * have blended states from both sides of the jump), falls outside the history, or asks for under
     * {@link #MIN_CORRECTION_M}.
     *
     * @param correction Corrected position from the game.
     * @return Translation to add to the robot's current pose, field frame, meters; zero when nothing should move.
     */
    public Translation2d computeCorrection(PoseCorrection correction) {
        // reject corrections that cannot be placed in the history
        double timestampS = correction.robotTimestampS();
        if (timestampS <= lastAppliedS || timestampS < lastDiscontinuityS + DISCONTINUITY_GUARD_S || history.isEmpty())
        {
            return Translation2d.kZero;
        }
        Sample first = history.get(0);
        Sample last = history.get(history.size() - 1);
        if (timestampS < first.timestampS() || timestampS > last.timestampS()) {
            return Translation2d.kZero;
        }

        // where the robot was then, and the offset in force then
        Sample then = interpolate(history, timestampS);

        // the offset the game wants, measured from the never-corrected position
        Translation2d rawThenField = then.positionField().minus(then.offsetField());
        Translation2d correctedThenField = new Translation2d(correction.xFieldM(), correction.yFieldM());
        Translation2d neededOffsetField = correctedThenField.minus(rawThenField);
        Translation2d translationField = neededOffsetField.minus(offsetField);
        if (translationField.getNorm() < MIN_CORRECTION_M) {
            return Translation2d.kZero;
        }

        // remember the absolute offset so in-flight corrections are not counted twice
        offsetField = neededOffsetField;
        lastAppliedS = timestampS;
        return translationField;
    }

    // ---- low-level helpers (static, no side effects)

    /**
     * Linear blend of the two history samples around a time.
     *
     * @param history    Samples, oldest first, covering the time.
     * @param timestampS Robot clock time to blend at, seconds; within the history's span.
     * @return Position and offset at that time.
     */
    private static Sample interpolate(List<Sample> history, double timestampS) {
        for (int i = history.size() - 1; i > 0; i--) {
            Sample earlier = history.get(i - 1);
            Sample later = history.get(i);
            if (timestampS >= earlier.timestampS()) {
                double spanS = later.timestampS() - earlier.timestampS();
                double fraction = 0;
                if (spanS > 1e-9) {
                    fraction = (timestampS - earlier.timestampS()) / spanS;
                }
                Translation2d position = blend(earlier.positionField(), later.positionField(), fraction);
                Translation2d offset = blend(earlier.offsetField(), later.offsetField(), fraction);
                return new Sample(timestampS, position, offset);
            }
        }
        return history.get(0);
    }

    /**
     * Linear blend of two translations.
     *
     * @param from     Translation at fraction 0, field frame, meters.
     * @param to       Translation at fraction 1, field frame, meters.
     * @param fraction Blend position from 0 to 1.
     * @return The blended translation.
     */
    private static Translation2d blend(Translation2d from, Translation2d to, double fraction) {
        Translation2d difference = to.minus(from);
        Translation2d step = difference.times(fraction);
        return from.plus(step);
    }
}
