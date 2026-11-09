package driversim.mech;

import com.ctre.phoenix6.hardware.TalonFX;
import com.ctre.phoenix6.sim.TalonFXSimState;

/**
 * Connects a simulated TalonFX to a mechanism model: reads the voltage the robot code commands and writes back the
 * rotor motion the model produces.
 *
 * <p>
 * The link opens its own {@link TalonFX} handle on the same CAN ID as the robot code's; Phoenix 6 lets several handles
 * share one device, so the robot code needs no changes. All values crossing this class are in the mechanism frame:
 * positive is the direction the robot code calls positive after its {@code Inverted} setting.
 */
public final class TalonMechanismLink {
    private final TalonFXSimState simState; // Phoenix sim inputs/outputs for the device, chassis frame
    private final double chassisToMechanismSign; // +1 for CounterClockwise_Positive devices, -1 for Clockwise_Positive
    private final double rotorRotationsPerMechanismRotation; // gear ratio between rotor and mechanism

    /**
     * Opens a sim link to a TalonFX by CAN ID.
     *
     * @param canId                              CAN ID of the TalonFX on the default bus.
     * @param isClockwisePositive                True when the robot code configures the motor Clockwise_Positive.
     * @param rotorRotationsPerMechanismRotation Rotor rotations per mechanism rotation (gear reduction).
     */
    public TalonMechanismLink(int canId, boolean isClockwisePositive, double rotorRotationsPerMechanismRotation) {
        TalonFX talon = new TalonFX(canId);
        this.simState = talon.getSimState();
        double sign = 1;
        if (isClockwisePositive) {
            sign = -1;
        }
        this.chassisToMechanismSign = sign;
        this.rotorRotationsPerMechanismRotation = rotorRotationsPerMechanismRotation;
    }

    /**
     * Reads the voltage the device is applying, after feeding it the current battery voltage.
     *
     * @param batteryVolts Battery voltage to supply the motor with.
     * @return Applied voltage, mechanism frame.
     */
    public double readMechanismVolts(double batteryVolts) {
        simState.setSupplyVoltage(batteryVolts);
        double chassisVolts = simState.getMotorVoltage();
        return chassisToMechanismSign * chassisVolts;
    }

    /**
     * Writes the mechanism's motion back to the device's rotor sensor.
     *
     * @param deltaMechanismRotations Mechanism movement since the last write, mechanism rotations.
     * @param mechanismVelocityRps    Current mechanism velocity, mechanism rotations/s.
     */
    public void writeMechanismMotion(double deltaMechanismRotations, double mechanismVelocityRps) {
        double deltaRotorRotations = deltaMechanismRotations * rotorRotationsPerMechanismRotation;
        double rotorVelocityRps = mechanismVelocityRps * rotorRotationsPerMechanismRotation;
        simState.addRotorPosition(chassisToMechanismSign * deltaRotorRotations);
        simState.setRotorVelocity(chassisToMechanismSign * rotorVelocityRps);
    }
}
