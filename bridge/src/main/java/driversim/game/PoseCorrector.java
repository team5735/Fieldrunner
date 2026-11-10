package driversim.game;

import edu.wpi.first.math.MathUtil;
import edu.wpi.first.math.geometry.Pose2d;
import edu.wpi.first.math.geometry.Rotation2d;
import edu.wpi.first.math.geometry.Translation2d;

import java.util.ArrayList;
import java.util.List;

/**
 * Reconciles the simulated robot's true pose with the game's corrected poses, which arrive a network round trip late.
 * It carries its own history: a ring of the last {@link #HISTORY_S} seconds of true poses, each with the total
 * translation and the total turn the bridge had applied through corrections at that moment.
 *
 * <p>
 * Call {@link #record} once per bridge update with the true pose, then {@link #computeCorrection} for each
 * correction taken from the game. The result is the movement and turn to add to the true pose now so it matches what
 * the game decided at the correction's timestamp. The corrector tracks the offsets it has asked for as absolute totals,
 * so corrections computed from older, not-yet-corrected poses do not count the same push twice. The pose must include
 * the corrections already requested, and only placement ({@link #markDiscontinuity}) may move it discontinuously.
 */
public final class PoseCorrector {
    public static final double HISTORY_S = 2.0;              // seconds of pose history kept
    public static final double MIN_CORRECTION_M = 0.001;     // translations shorter than this are ignored (m)
    public static final double MIN_CORRECTION_RAD = 0.0002;  // turns smaller than this are ignored (radians, ~0.01 deg)
    public static final double DISCONTINUITY_GUARD_S = 0.05;  // corrections stamped this soon after a jump are ignored

    /** One recorded bridge update. */
    private record Sample(
        double timestampS, Translation2d positionField, Translation2d offsetField, double headingRad,
        double headingOffsetRad
    ) {}

    private final List<Sample> history;  // oldest first
    private Translation2d offsetField;   // total translation applied through corrections so far, field frame, meters
    private double headingOffsetRad;     // total turn applied through corrections so far, radians, unwrapped
    private double lastAppliedS;         // timestamp of the last correction applied, robot clock, seconds
    private double lastDiscontinuityS;   // robot clock time of the last placement, seconds

    /** Creates a corrector with no history and no applied corrections. */
    public PoseCorrector() {
        this.history = new ArrayList<>();
        this.offsetField = Translation2d.kZero;
        this.headingOffsetRad = 0;
        this.lastAppliedS = Double.NEGATIVE_INFINITY;
        this.lastDiscontinuityS = Double.NEGATIVE_INFINITY;
    }

    /**
     * Records the true pose for this bridge update.
     *
     * @param timestampS Robot clock, seconds; must not decrease between calls (a decrease clears the history).
     * @param poseField  Robot pose now, including every correction already applied, field frame.
     */
    public void record(double timestampS, Pose2d poseField) {
        // a clock that ran backwards makes the history meaningless
        if (!history.isEmpty()) {
            Sample previous = history.get(history.size() - 1);
            if (timestampS <= previous.timestampS()) {
                history.clear();
                lastDiscontinuityS = timestampS;
                lastAppliedS = Double.NEGATIVE_INFINITY;
            }
        }

        // append and drop what is too old
        Translation2d positionField = poseField.getTranslation();
        double headingRad = poseField.getRotation().getRadians();
        history.add(new Sample(timestampS, positionField, offsetField, headingRad, headingOffsetRad));
        while (timestampS - history.get(0).timestampS() > HISTORY_S) {
            history.remove(0);
        }
    }

    /**
     * Marks that the robot was placed somewhere at a time (practice or match start, reset). Corrections for states
     * older than that time are ignored afterwards.
     *
     * @param timestampS Robot clock time of the placement, seconds.
     */
    public void markDiscontinuity(double timestampS) { lastDiscontinuityS = timestampS; }

    /**
     * Works out how far to move and turn the true pose now so it matches the game's decision at the correction's time,
     * and remembers the total offsets that implies. The correction is ignored (no movement) when it is not newer than
     * the last applied one, predates the last placement by less than {@link #DISCONTINUITY_GUARD_S} (the game may have
     * blended states from both sides of the jump), falls outside the history, or asks for under
     * {@link #MIN_CORRECTION_M} of movement and {@link #MIN_CORRECTION_RAD} of turn.
     *
     * @param correction Corrected pose from the game.
     * @return Movement (field frame, meters) and turn (radians) to add to the true pose; {@link PoseDelta#NONE} when
     *         nothing should move.
     */
    public PoseDelta computeCorrection(PoseCorrection correction) {
        // reject corrections that cannot be placed in the history
        double timestampS = correction.robotTimestampS();
        if (timestampS <= lastAppliedS || timestampS < lastDiscontinuityS + DISCONTINUITY_GUARD_S || history.isEmpty())
        {
            return PoseDelta.NONE;
        }
        Sample first = history.get(0);
        Sample last = history.get(history.size() - 1);
        if (timestampS < first.timestampS() || timestampS > last.timestampS()) {
            return PoseDelta.NONE;
        }

        // where the robot was then, and the offsets in force then
        Sample then = interpolate(history, timestampS);

        // the position offset the game wants, measured from the never-corrected position
        Translation2d rawThenField = then.positionField().minus(then.offsetField());
        Translation2d correctedThenField = new Translation2d(correction.xFieldM(), correction.yFieldM());
        Translation2d neededOffsetField = correctedThenField.minus(rawThenField);
        Translation2d translationField = neededOffsetField.minus(offsetField);

        // the same for heading: what was already applied then, plus the extra turn the game asks for
        double extraTurnThenRad = MathUtil.angleModulus(correction.headingFieldRad() - then.headingRad());
        double neededHeadingOffsetRad = then.headingOffsetRad() + extraTurnThenRad;
        double turnRad = MathUtil.angleModulus(neededHeadingOffsetRad - headingOffsetRad);
        if (translationField.getNorm() < MIN_CORRECTION_M && Math.abs(turnRad) < MIN_CORRECTION_RAD) {
            return PoseDelta.NONE;
        }

        // remember the absolute offsets so in-flight corrections are not counted twice
        offsetField = neededOffsetField;
        headingOffsetRad = headingOffsetRad + turnRad;
        lastAppliedS = timestampS;
        return new PoseDelta(translationField, Rotation2d.fromRadians(turnRad));
    }

    // ---- low-level helpers (static, no side effects)

    /**
     * Linear blend of the two history samples around a time.
     *
     * @param history    Samples, oldest first, covering the time.
     * @param timestampS Robot clock time to blend at, seconds; within the history's span.
     * @return Pose and offsets at that time.
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
                double headingStepRad = MathUtil.angleModulus(later.headingRad() - earlier.headingRad());
                double headingRad = earlier.headingRad() + headingStepRad * fraction;
                double headingOffsetStepRad = later.headingOffsetRad() - earlier.headingOffsetRad();
                double headingOffsetRad = earlier.headingOffsetRad() + headingOffsetStepRad * fraction;
                return new Sample(timestampS, position, offset, headingRad, headingOffsetRad);
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
