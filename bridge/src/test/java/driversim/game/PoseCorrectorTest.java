package driversim.game;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

import edu.wpi.first.math.geometry.Pose2d;
import edu.wpi.first.math.geometry.Rotation2d;

class PoseCorrectorTest {
    private static final double STEP_S = 0.005;        // bridge update period, seconds
    private static final double WALL_X_M = 0.4635;     // robot center x when flush against the x = 0 wall
    private static final double LATENCY_S = 0.04;      // age of the state a correction refers to, seconds
    private static final double PUSH_SPEED_MPS = 3.0;  // speed the robot drives into the wall, m/s

    @Test
    void singleCorrectionMovesPoseToCorrectedPosition() {
        PoseCorrector corrector = new PoseCorrector();
        for (int i = 0; i <= 20; i++) {
            corrector.record(i * STEP_S, pose(2.0, 3.0));
        }

        PoseCorrection correction = new PoseCorrection(10 * STEP_S, 1.9, 3.0, 0.0);
        PoseDelta translation = corrector.computeCorrection(correction);

        assertEquals(-0.1, translation.translationField().getX(), 1e-9);
        assertEquals(0.0, translation.translationField().getY(), 1e-9);
    }

    @Test
    void pushingIntoWallWithLateCorrectionsConvergesWithoutOvershoot() {
        PoseCorrector corrector = new PoseCorrector();
        double rawX = 1.0;     // robot code's own integration, never corrected
        double offsetX = 0.0;  // translation the bridge has applied so far
        double poseX = rawX;
        double[] poseHistory = new double[400];
        double minPoseX = Double.MAX_VALUE;
        double highestAfterContact = 0;
        boolean hasTouched = false;
        int lagSteps = (int) Math.round(LATENCY_S / STEP_S);

        // push for one second, then stop for a fifth of a second
        int pushSteps = 200;
        int totalSteps = pushSteps + 40;
        for (int step = 0; step < totalSteps; step++) {
            double timeS = step * STEP_S;
            if (step < pushSteps) {
                rawX -= PUSH_SPEED_MPS * STEP_S;
            }
            poseX = rawX + offsetX;
            poseHistory[step] = poseX;
            corrector.record(timeS, pose(poseX, 2.0));

            // the game answers for the state lagSteps ago, as reported at that time
            if (step >= lagSteps) {
                int oldStep = step - lagSteps;
                double reportedX = poseHistory[oldStep];
                double constrainedX = Math.max(reportedX, WALL_X_M);
                PoseCorrection correction = new PoseCorrection(oldStep * STEP_S, constrainedX, 2.0, 0.0);
                PoseDelta translation = corrector.computeCorrection(correction);
                offsetX += translation.translationField().getX();
                poseX += translation.translationField().getX();
            }

            // track the trajectory
            minPoseX = Math.min(minPoseX, poseX);
            if (poseX < WALL_X_M) {
                hasTouched = true;
            }
            if (hasTouched) {
                highestAfterContact = Math.max(highestAfterContact, poseX);
            }
        }

        // inside the wall by about speed x latency while pushing, never pushed back out past the wall, flush at rest
        double maxPenetrationM = PUSH_SPEED_MPS * (LATENCY_S + 2 * STEP_S);
        assertTrue(minPoseX >= WALL_X_M - maxPenetrationM, "penetration too deep: " + minPoseX);
        assertTrue(highestAfterContact <= WALL_X_M + 0.001, "overshot out of the wall: " + highestAfterContact);
        assertEquals(WALL_X_M, poseX, 0.001);
    }

    @Test
    void staleCorrectionIsIgnored() {
        PoseCorrector corrector = new PoseCorrector();
        for (int i = 0; i <= 40; i++) {
            corrector.record(i * STEP_S, pose(2.0, 3.0));
        }

        PoseDelta first = corrector.computeCorrection(new PoseCorrection(20 * STEP_S, 1.9, 3.0, 0.0));
        PoseDelta stale = corrector.computeCorrection(new PoseCorrection(10 * STEP_S, 1.5, 3.0, 0.0));

        assertEquals(-0.1, first.translationField().getX(), 1e-9);
        assertEquals(0.0, stale.translationField().getNorm(), 1e-12);
    }

    @Test
    void markedDiscontinuityIgnoresOlderCorrections() {
        PoseCorrector corrector = new PoseCorrector();
        for (int i = 0; i <= 60; i++) {
            corrector.record(i * STEP_S, pose(2.0, 3.0));
        }
        corrector.markDiscontinuity(30 * STEP_S);

        PoseDelta older = corrector.computeCorrection(new PoseCorrection(25 * STEP_S, 1.9, 3.0, 0.0));
        PoseDelta blendedAcrossPlacement = corrector.computeCorrection(new PoseCorrection(31 * STEP_S, 1.9, 3.0, 0.0));
        PoseDelta newer = corrector.computeCorrection(new PoseCorrection(50 * STEP_S, 1.9, 3.0, 0.0));

        assertEquals(0.0, older.translationField().getNorm(), 1e-12);
        assertEquals(0.0, blendedAcrossPlacement.translationField().getNorm(), 1e-12);
        assertEquals(-0.1, newer.translationField().getX(), 1e-9);
    }

    @Test
    void subMillimeterCorrectionIsIgnored() {
        PoseCorrector corrector = new PoseCorrector();
        for (int i = 0; i <= 20; i++) {
            corrector.record(i * STEP_S, pose(2.0, 3.0));
        }

        PoseDelta tiny = corrector.computeCorrection(new PoseCorrection(10 * STEP_S, 1.9996, 3.0, 0.0));

        assertEquals(0.0, tiny.translationField().getNorm(), 1e-12);
    }

    @Test
    void correctionOutsideHistoryIsIgnored() {
        PoseCorrector corrector = new PoseCorrector();
        for (int i = 0; i <= 20; i++) {
            corrector.record(i * STEP_S, pose(2.0, 3.0));
        }

        PoseDelta future = corrector.computeCorrection(new PoseCorrection(5.0, 1.9, 3.0, 0.0));
        PoseDelta ancient = corrector.computeCorrection(new PoseCorrection(-5.0, 1.9, 3.0, 0.0));

        assertEquals(0.0, future.translationField().getNorm(), 1e-12);
        assertEquals(0.0, ancient.translationField().getNorm(), 1e-12);
    }

    @Test
    void appliedCorrectionIsNotCountedTwice() {
        PoseCorrector corrector = new PoseCorrector();
        for (int i = 0; i <= 20; i++) {
            corrector.record(i * STEP_S, pose(2.0, 3.0));
        }
        PoseDelta translation = corrector.computeCorrection(new PoseCorrection(10 * STEP_S, 1.7, 3.0, 0.0));

        // the bridge applies it; the next pose carries the whole 30 cm in one update, and a following correction is
        // measured against the offset already applied
        corrector.record(21 * STEP_S, pose(2.0 + translation.translationField().getX(), 3.0));
        PoseDelta next = corrector.computeCorrection(new PoseCorrection(21 * STEP_S, 1.6, 3.0, 0.0));

        assertEquals(-0.1, next.translationField().getX(), 1e-9);
    }

    @Test
    void headingCorrectionTurnsPoseToCorrectedHeading() {
        PoseCorrector corrector = new PoseCorrector();
        for (int i = 0; i <= 20; i++) {
            corrector.record(i * STEP_S, pose(2.0, 3.0, 0.5));
        }

        PoseDelta delta = corrector.computeCorrection(new PoseCorrection(10 * STEP_S, 2.0, 3.0, 0.4));

        assertEquals(0.0, delta.translationField().getNorm(), 1e-12);
        assertEquals(-0.1, delta.rotation().getRadians(), 1e-9);
    }

    @Test
    void headingCorrectionTakesTheShortWayAcrossPlusMinusPi() {
        PoseCorrector corrector = new PoseCorrector();
        for (int i = 0; i <= 20; i++) {
            corrector.record(i * STEP_S, pose(2.0, 3.0, 3.1));
        }

        // the game says the heading was -3.1 rad: that is 0.0832 rad further counter-clockwise, not 6.2 rad back
        PoseDelta delta = corrector.computeCorrection(new PoseCorrection(10 * STEP_S, 2.0, 3.0, -3.1));

        assertEquals(2 * Math.PI - 6.2, delta.rotation().getRadians(), 1e-9);
    }

    @Test
    void positionAndHeadingAreCorrectedTogether() {
        PoseCorrector corrector = new PoseCorrector();
        for (int i = 0; i <= 20; i++) {
            corrector.record(i * STEP_S, pose(2.0, 3.0, 1.0));
        }

        PoseDelta delta = corrector.computeCorrection(new PoseCorrection(10 * STEP_S, 1.9, 3.0, 0.9));

        assertEquals(-0.1, delta.translationField().getX(), 1e-9);
        assertEquals(-0.1, delta.rotation().getRadians(), 1e-9);
        assertTrue(delta.isMoving());
    }

    @Test
    void turnOnlyCorrectionIsApplied() {
        PoseCorrector corrector = new PoseCorrector();
        for (int i = 0; i <= 20; i++) {
            corrector.record(i * STEP_S, pose(2.0, 3.0, 0.0));
        }

        PoseDelta delta = corrector.computeCorrection(new PoseCorrection(10 * STEP_S, 2.0, 3.0, 0.05));

        assertEquals(0.05, delta.rotation().getRadians(), 1e-9);
    }

    @Test
    void tinyTurnIsIgnored() {
        PoseCorrector corrector = new PoseCorrector();
        for (int i = 0; i <= 20; i++) {
            corrector.record(i * STEP_S, pose(2.0, 3.0, 0.0));
        }

        PoseDelta delta = corrector.computeCorrection(new PoseCorrection(10 * STEP_S, 2.0, 3.0, 0.0001));

        assertEquals(false, delta.isMoving());
    }

    @Test
    void lateHeadingCorrectionsDoNotTurnTheRobotTwice() {
        PoseCorrector corrector = new PoseCorrector();
        double rawHeading = 0.0;     // wheel-driven heading, never corrected
        double offsetHeading = 0.0;  // turn the bridge has applied so far
        double[] headingHistory = new double[200];
        int lagSteps = (int) Math.round(LATENCY_S / STEP_S);

        // the robot is held at 0.3 rad by a wall (the game's answer), while the wheels keep trying to spin it
        for (int step = 0; step < 100; step++) {
            rawHeading += 0.01;
            double heading = rawHeading + offsetHeading;
            headingHistory[step] = heading;
            corrector.record(step * STEP_S, pose(2.0, 3.0, heading));

            if (step >= lagSteps) {
                int oldStep = step - lagSteps;
                double constrained = Math.min(headingHistory[oldStep], 0.3);
                PoseDelta delta =
                    corrector.computeCorrection(new PoseCorrection(oldStep * STEP_S, 2.0, 3.0, constrained));
                offsetHeading += delta.rotation().getRadians();
            }
        }

        double finalHeading = rawHeading + offsetHeading;
        assertEquals(0.3, finalHeading, 0.02 + 0.01 * lagSteps);
        assertTrue(finalHeading >= 0.3 - 1e-9, "turned back past the constraint: " + finalHeading);
    }

    private static Pose2d pose(double xM, double yM) { return pose(xM, yM, 0.0); }

    private static Pose2d pose(double xM, double yM, double headingRad) {
        return new Pose2d(xM, yM, Rotation2d.fromRadians(headingRad));
    }
}
