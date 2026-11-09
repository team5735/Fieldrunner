package driversim.mech;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

class FeedforwardMechanismSimTest {
    private static final double KS = 0.35; // volts (turret's)
    private static final double KV = 0.75; // volts per rotation/s
    private static final double KA = 0.4; // volts per rotation/s^2

    @Test
    void reachesFeedforwardSteadyStateSpeed() {
        FeedforwardMechanismSim sim = new FeedforwardMechanismSim(KS, KV, KA, 0, -100, 100, 0);
        for (int i = 0; i < 500; i++) {
            sim.update(3.35, 0.02);
        }
        // steady state: V = kS + kV*v  ->  v = (3.35 - 0.35) / 0.75 = 4 rot/s
        assertEquals(4.0, sim.getVelocityRps(), 1e-3);
    }

    @Test
    void staticFrictionHoldsBelowKs() {
        FeedforwardMechanismSim sim = new FeedforwardMechanismSim(KS, KV, KA, 0, -1, 1, 0.2);
        sim.update(0.3, 1.0);
        assertEquals(0.2, sim.getPositionRotations(), 1e-12);
        assertEquals(0.0, sim.getVelocityRps(), 1e-12);
    }

    @Test
    void coastsToAStopWithoutReversing() {
        FeedforwardMechanismSim sim = new FeedforwardMechanismSim(KS, KV, KA, 0, -100, 100, 0);
        sim.update(6, 1.0);
        assertTrue(sim.getVelocityRps() > 1);
        sim.update(0, 5.0);
        assertEquals(0.0, sim.getVelocityRps(), 1e-12);
        assertTrue(sim.getPositionRotations() > 0);
    }

    @Test
    void stopsDeadAtHardStops() {
        FeedforwardMechanismSim sim = new FeedforwardMechanismSim(KS, KV, KA, 0, -0.5, 0.5, 0);
        sim.update(12, 2.0);
        assertEquals(0.5, sim.getPositionRotations(), 1e-12);
        assertEquals(0.0, sim.getVelocityRps(), 1e-12);
        sim.update(-12, 3.0);
        assertEquals(-0.5, sim.getPositionRotations(), 1e-12);
    }

    @Test
    void gravityPullsArmDownWhenUnpowered() {
        // arm 30 degrees above horizontal, gravity torque beats friction
        FeedforwardMechanismSim sim = new FeedforwardMechanismSim(0.05, 6.5, 0.05, 0.5, 0, 0.3, 30.0 / 360);
        sim.update(0, 2.0);
        assertEquals(0.0, sim.getPositionRotations(), 1e-12);
    }

    @Test
    void addVelocityKicksTheMechanism() {
        FeedforwardMechanismSim sim = new FeedforwardMechanismSim(KS, KV, KA, 0, -100, 100, 0);
        sim.addVelocity(-2);
        assertEquals(-2.0, sim.getVelocityRps(), 1e-12);
    }
}
