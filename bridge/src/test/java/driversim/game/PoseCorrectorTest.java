package driversim.game;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

import edu.wpi.first.math.geometry.Translation2d;

class PoseCorrectorTest {
    private static final double STEP_S = 0.005;        // bridge update period, seconds
    private static final double WALL_X_M = 0.4635;     // robot center x when flush against the x = 0 wall
    private static final double LATENCY_S = 0.04;      // age of the state a correction refers to, seconds
    private static final double PUSH_SPEED_MPS = 3.0;  // speed the robot drives into the wall, m/s

    @Test
    void singleCorrectionMovesPoseToCorrectedPosition() {
        PoseCorrector corrector = new PoseCorrector();
        for (int i = 0; i <= 20; i++) {
            corrector.record(i * STEP_S, new Translation2d(2.0, 3.0));
        }

        PoseCorrection correction = new PoseCorrection(10 * STEP_S, 1.9, 3.0);
        Translation2d translation = corrector.computeCorrection(correction);

        assertEquals(-0.1, translation.getX(), 1e-9);
        assertEquals(0.0, translation.getY(), 1e-9);
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
            corrector.record(timeS, new Translation2d(poseX, 2.0));

            // the game answers for the state lagSteps ago, as reported at that time
            if (step >= lagSteps) {
                int oldStep = step - lagSteps;
                double reportedX = poseHistory[oldStep];
                double constrainedX = Math.max(reportedX, WALL_X_M);
                PoseCorrection correction = new PoseCorrection(oldStep * STEP_S, constrainedX, 2.0);
                Translation2d translation = corrector.computeCorrection(correction);
                offsetX += translation.getX();
                poseX += translation.getX();
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
            corrector.record(i * STEP_S, new Translation2d(2.0, 3.0));
        }

        Translation2d first = corrector.computeCorrection(new PoseCorrection(20 * STEP_S, 1.9, 3.0));
        Translation2d stale = corrector.computeCorrection(new PoseCorrection(10 * STEP_S, 1.5, 3.0));

        assertEquals(-0.1, first.getX(), 1e-9);
        assertEquals(0.0, stale.getNorm(), 1e-12);
    }

    @Test
    void correctionFromBeforeOutsideResetIsIgnored() {
        PoseCorrector corrector = new PoseCorrector();
        for (int i = 0; i <= 20; i++) {
            corrector.record(i * STEP_S, new Translation2d(2.0, 3.0));
        }
        // robot code teleports the pose (an auto starting, a resetPose command)
        for (int i = 21; i <= 60; i++) {
            corrector.record(i * STEP_S, new Translation2d(10.0, 5.0));
        }

        Translation2d beforeReset = corrector.computeCorrection(new PoseCorrection(15 * STEP_S, 1.9, 3.0));
        Translation2d blendedAcrossReset = corrector.computeCorrection(new PoseCorrection(24 * STEP_S, 6.0, 4.0));
        Translation2d afterReset = corrector.computeCorrection(new PoseCorrection(50 * STEP_S, 9.9, 5.0));

        assertEquals(0.0, beforeReset.getNorm(), 1e-12);
        assertEquals(0.0, blendedAcrossReset.getNorm(), 1e-12);
        assertEquals(-0.1, afterReset.getX(), 1e-9);
    }

    @Test
    void markedDiscontinuityIgnoresOlderCorrections() {
        PoseCorrector corrector = new PoseCorrector();
        for (int i = 0; i <= 60; i++) {
            corrector.record(i * STEP_S, new Translation2d(2.0, 3.0));
        }
        corrector.markDiscontinuity(30 * STEP_S);

        Translation2d older = corrector.computeCorrection(new PoseCorrection(25 * STEP_S, 1.9, 3.0));
        Translation2d newer = corrector.computeCorrection(new PoseCorrection(50 * STEP_S, 1.9, 3.0));

        assertEquals(0.0, older.getNorm(), 1e-12);
        assertEquals(-0.1, newer.getX(), 1e-9);
    }

    @Test
    void subMillimeterCorrectionIsIgnored() {
        PoseCorrector corrector = new PoseCorrector();
        for (int i = 0; i <= 20; i++) {
            corrector.record(i * STEP_S, new Translation2d(2.0, 3.0));
        }

        Translation2d tiny = corrector.computeCorrection(new PoseCorrection(10 * STEP_S, 1.9996, 3.0));

        assertEquals(0.0, tiny.getNorm(), 1e-12);
    }

    @Test
    void correctionOutsideHistoryIsIgnored() {
        PoseCorrector corrector = new PoseCorrector();
        for (int i = 0; i <= 20; i++) {
            corrector.record(i * STEP_S, new Translation2d(2.0, 3.0));
        }

        Translation2d future = corrector.computeCorrection(new PoseCorrection(5.0, 1.9, 3.0));
        Translation2d ancient = corrector.computeCorrection(new PoseCorrection(-5.0, 1.9, 3.0));

        assertEquals(0.0, future.getNorm(), 1e-12);
        assertEquals(0.0, ancient.getNorm(), 1e-12);
    }

    @Test
    void appliedCorrectionIsNotMistakenForOutsideReset() {
        PoseCorrector corrector = new PoseCorrector();
        for (int i = 0; i <= 20; i++) {
            corrector.record(i * STEP_S, new Translation2d(2.0, 3.0));
        }
        Translation2d translation = corrector.computeCorrection(new PoseCorrection(10 * STEP_S, 1.7, 3.0));

        // the bridge applies it; the next pose carries the whole 30 cm in one update
        corrector.record(21 * STEP_S, new Translation2d(2.0 + translation.getX(), 3.0));
        Translation2d next = corrector.computeCorrection(new PoseCorrection(21 * STEP_S, 1.6, 3.0));

        assertEquals(-0.1, next.getX(), 1e-9);
    }
}
