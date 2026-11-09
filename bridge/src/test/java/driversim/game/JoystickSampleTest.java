package driversim.game;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;

import org.junit.jupiter.api.Test;

class JoystickSampleTest {
    @Test
    void decodesAGoodSample() {
        double[] values = {0.5, -1, 0.25, 0.75, -0.5, 1, 5, 90};
        JoystickSample sample = JoystickSample.parse(values);
        assertNotNull(sample);
        assertEquals(0.5, sample.leftX(), 1e-12);
        assertEquals(-1, sample.leftY(), 1e-12);
        assertEquals(0.25, sample.leftTrigger(), 1e-12);
        assertEquals(0.75, sample.rightTrigger(), 1e-12);
        assertEquals(-0.5, sample.rightX(), 1e-12);
        assertEquals(1, sample.rightY(), 1e-12);
        assertEquals(5, sample.buttonBits());
        assertEquals(90, sample.povDeg());
        assertEquals(-1, sample.getAxis(JoystickSample.AXIS_LEFT_Y), 1e-12);
        assertEquals(0.75, sample.getAxis(JoystickSample.AXIS_RIGHT_TRIGGER), 1e-12);
    }

    @Test
    void rejectsWrongLengthAndNonFiniteValues() {
        assertNull(JoystickSample.parse(null));
        assertNull(JoystickSample.parse(new double[0]));
        assertNull(JoystickSample.parse(new double[] {0, 0, 0, 0, 0, 0, 0}));
        assertNull(JoystickSample.parse(new double[] {0, 0, 0, 0, 0, 0, 0, -1, 0}));
        assertNull(JoystickSample.parse(new double[] {0, Double.NaN, 0, 0, 0, 0, 0, -1}));
        assertNull(JoystickSample.parse(new double[] {0, 0, 0, 0, 0, 0, Double.POSITIVE_INFINITY, -1}));
    }

    @Test
    void clampsOutOfRangeValues() {
        double[] values = {-3, 2, -0.5, 4, 9, -9, 99999, 725};
        JoystickSample sample = JoystickSample.parse(values);
        assertNotNull(sample);
        assertEquals(-1, sample.leftX(), 1e-12);
        assertEquals(1, sample.leftY(), 1e-12);
        assertEquals(0, sample.leftTrigger(), 1e-12);
        assertEquals(1, sample.rightTrigger(), 1e-12);
        assertEquals(1, sample.rightX(), 1e-12);
        assertEquals(-1, sample.rightY(), 1e-12);
        assertEquals(1023, sample.buttonBits());
        assertEquals(5, sample.povDeg());
    }

    @Test
    void releasedDpadIsMinusOne() {
        double[] values = {0, 0, 0, 0, 0, 0, 0, -45};
        JoystickSample sample = JoystickSample.parse(values);
        assertEquals(-1, sample.povDeg());
    }
}
