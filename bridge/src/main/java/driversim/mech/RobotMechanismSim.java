package driversim.mech;

import static edu.wpi.first.units.Units.Rotations;

import com.revrobotics.sim.SparkFlexSim;
import com.revrobotics.spark.SparkFlex;

import edu.wpi.first.math.system.plant.DCMotor;
import edu.wpi.first.math.util.Units;
import edu.wpi.first.wpilibj.simulation.AnalogInputSim;
import edu.wpi.first.wpilibj.simulation.DIOSim;
import edu.wpi.first.wpilibj.simulation.PWMSim;

import frc.robot.constants.Constants;
import frc.robot.constants.HoodConstants;
import frc.robot.constants.IntakeConstants;
import frc.robot.constants.LauncherConstants;
import frc.robot.constants.TurretConstants;
import frc.robot.subsystems.SpinDexSubsystem;

/**
 * Simulates every non-drivetrain mechanism on the 2026 robot so the unmodified robot code sees believable sensors:
 * the turret finds its hall switch when zeroing, the flywheel reaches its RPM, the intake arm swings and trips its
 * limit, the hood servo's feedback wire follows the servo, and the climber hits its end-of-travel switches.
 *
 * <p>
 * Each mechanism is a {@link FeedforwardMechanismSim} using the robot code's own feedforward gains where it has
 * them, so mechanisms respond the way the code was tuned to expect. Devices are reached by CAN ID, DIO, PWM and
 * analog channel from {@link Constants}; the spindex SparkFlexes are passed in because REV's sim needs the instance.
 * Call {@link #update} from the robot's main loop thread.
 */
public final class RobotMechanismSim {
    // ---- turret: turret-relative rotations, as reported by the turret TalonFX
    private static final double TURRET_HALL_EDGE_TUR_REL = robotRelToTurretRel(
        TurretConstants.HALL_LIMIT_POS_BOT_REL.in(Rotations)
    ); // turret-relative rotations where the hall switch starts reading engaged (towards the forward stop)
    private static final double TURRET_HARD_STOP_PADDING_ROTATIONS = 0.01; // rotations past each soft limit
    private static final double TURRET_START_TUR_REL = robotRelToTurretRel(
        TurretConstants.START_POS_BOT_REL.in(Rotations)
    ); // where the turret rests at power-on; matches the robot code's initial resetAngle

    // ---- intake slapdown: mechanism rotations from horizontal, matching the TalonFX's reported position
    private static final double INTAKE_KA = 0.05; // volts per (rotation/s^2), not characterised; keeps the arm snappy
    private static final double INTAKE_MIN_ROTATIONS = 0; // hard stop at horizontal
    private static final double INTAKE_MAX_ROTATIONS = Units.degreesToRotations(115); // hard stop past the hall switch
    private static final double INTAKE_HALL_EDGE_ROTATIONS = IntakeConstants.LIMIT_POS
        .in(Rotations); // arm angle where the hall switch starts reading engaged
    private static final double INTAKE_START_ROTATIONS = INTAKE_HALL_EDGE_ROTATIONS; // arm rests on the switch

    // ---- intake roller: roller rotations
    private static final double ROLLER_KS = 0.1; // volts
    private static final double ROLLER_KV = 0.12; // volts per (rotation/s)
    private static final double ROLLER_KA = 0.005; // volts per (rotation/s^2)

    // ---- launcher: flywheel rotations (after the 36:15 pulley step-up)
    private static final double LAUNCHER_KV_PER_RPS = LauncherConstants.KV * 60; // volts per (flywheel rotation/s)
    private static final double LAUNCHER_SPINUP_TIME_CONSTANT_S = 0.4; // seconds, open-loop spin-up time constant
    private static final double LAUNCHER_KA = LAUNCHER_KV_PER_RPS * LAUNCHER_SPINUP_TIME_CONSTANT_S; // volts per rot/s^2
    private static final double LAUNCHER_SHOT_SPEED_LOSS = 0.06; // fraction of flywheel speed each ball takes away

    // ---- climber: travel fraction, 0 = fully retracted (lower switch) to 1 = fully extended (upper switch)
    private static final double CLIMBER_KS = 0.3; // volts
    private static final double CLIMBER_KV = 12.5; // volts per (travel/s); 5 V gives a full stroke in about 2.5 s
    private static final double CLIMBER_KA = 0.5; // volts per (travel/s^2)
    private static final double CLIMBER_ROTOR_ROTATIONS_PER_STROKE = 40; // rotor rotations over the full stroke
    private static final double CLIMBER_SWITCH_BAND = 0.002; // travel fraction within which an end switch reads engaged

    // ---- hood servo: normalized servo position, 0..1
    private static final double SERVO_SLEW_PER_S = 1.0; // servo positions per second the hood servo can move

    // ---- spindex
    private static final double VORTEX_FREE_RPM_PER_VOLT = 6784.0 / 12; // Vortex free speed per volt, unloaded

    private final FeedforwardMechanismSim turret; // turret-relative rotations
    private final TalonMechanismLink turretLink; // turret TalonFX
    private final DIOSim turretHall; // turret hall switch, reads false when engaged

    private final FeedforwardMechanismSim intakeArm; // rotations from horizontal
    private final TalonMechanismLink intakeArmLink; // slapdown TalonFX
    private final DIOSim intakeHall; // intake hall switch, reads false when engaged
    private final FeedforwardMechanismSim intakeRoller; // roller rotations
    private final TalonMechanismLink intakeRollerLink; // roller TalonFX
    private double intakeRollerVolts; // last applied roller voltage, mechanism frame

    private final FeedforwardMechanismSim flywheel; // flywheel rotations
    private final TalonMechanismLink flywheelLink; // left launcher TalonFX (the right one follows it)

    private final FeedforwardMechanismSim climber; // travel fraction
    private final TalonMechanismLink climberLink; // climber TalonFX
    private final DIOSim climberLowerSwitch; // reads false when fully retracted
    private final DIOSim climberUpperSwitch; // reads false when fully extended

    private final PWMSim hoodServoPwm; // hood servo command
    private final AnalogInputSim hoodFeedback; // hood servo feedback wire
    private double hoodServoPosition; // actual servo position, 0..1

    private final SparkFlexSim spindexFeedSim; // spindex feed Vortex
    private final SparkFlexSim spindexWheelSim; // spindex wheel Vortex
    private double spindexFeedVolts; // last applied feed voltage
    private double spindexWheelVolts; // last applied wheel voltage

    /**
     * Opens sim handles on all mechanism devices and puts each mechanism at its power-on position. Construct after the
     * robot code has created its devices.
     *
     * @param spindex The robot's spindex subsystem; its SparkFlex instances are needed by REV's simulator.
     */
    public RobotMechanismSim(SpinDexSubsystem spindex) {
        // turret
        double turretMin = TurretConstants.REVERSE_LIMIT_TUR_REL.in(Rotations)
            - TURRET_HARD_STOP_PADDING_ROTATIONS;
        double turretMax = TurretConstants.FORWARD_LIMIT_TUR_REL.in(Rotations)
            + TURRET_HARD_STOP_PADDING_ROTATIONS;
        turret = new FeedforwardMechanismSim(
            TurretConstants.KS, TurretConstants.KV, TurretConstants.KA, 0, turretMin, turretMax, TURRET_START_TUR_REL
        );
        turretLink = new TalonMechanismLink(Constants.TURRET_MOTOR_ID, true, TurretConstants.GEAR_REDUCTION);
        turretHall = new DIOSim(Constants.TURRET_LIMIT_PIN);

        // intake arm and roller
        intakeArm = new FeedforwardMechanismSim(
            IntakeConstants.KS,
            IntakeConstants.KV,
            INTAKE_KA,
            IntakeConstants.KG,
            INTAKE_MIN_ROTATIONS,
            INTAKE_MAX_ROTATIONS,
            INTAKE_START_ROTATIONS
        );
        intakeArmLink = new TalonMechanismLink(
            Constants.INTAKE_SLAPDOWN_TALONFX_ID, false, IntakeConstants.GEAR_REDUCTION
        );
        intakeHall = new DIOSim(Constants.INTAKE_LIMIT_PIN);
        intakeRoller = new FeedforwardMechanismSim(ROLLER_KS, ROLLER_KV, ROLLER_KA, 0, -1e9, 1e9, 0);
        intakeRollerLink = new TalonMechanismLink(Constants.INTAKE_ROLLER_TALONFX_ID, true, 1);

        // launcher flywheel
        flywheel = new FeedforwardMechanismSim(
            LauncherConstants.KS, LAUNCHER_KV_PER_RPS, LAUNCHER_KA, 0, -1e12, 1e12, 0
        );
        flywheelLink = new TalonMechanismLink(
            Constants.LAUNCHER_LEFT_KRAKEN_ID, false, 1 / LauncherConstants.GEARING
        );

        // climber
        climber = new FeedforwardMechanismSim(CLIMBER_KS, CLIMBER_KV, CLIMBER_KA, 0, 0, 1, 0);
        climberLink = new TalonMechanismLink(Constants.CLIMB_TALON_ID, false, -CLIMBER_ROTOR_ROTATIONS_PER_STROKE);
        climberLowerSwitch = new DIOSim(Constants.CLIMB_UPPER_LIMIT_PIN); // robot code names DIO 0 "limitDown"
        climberUpperSwitch = new DIOSim(Constants.CLIMB_LOWER_LIMIT_PIN); // robot code names DIO 1 "limitUp"

        // hood servo
        hoodServoPwm = new PWMSim(Constants.HOOD_SERVO_PIN);
        hoodFeedback = new AnalogInputSim(Constants.HOOD_FEEDBACK_PIN);
        hoodServoPosition = HoodConstants.LOWEST_SERVO_POSITION;

        // spindex
        spindexFeedSim = new SparkFlexSim(getFeedVortex(spindex), DCMotor.getNeoVortex(1));
        spindexWheelSim = new SparkFlexSim(getWheelVortex(spindex), DCMotor.getNeoVortex(1));

        // publish power-on sensor values before the first robot loop reads them
        writeSwitches();
        hoodFeedback.setVoltage(computeServoFeedbackVolts(hoodServoPosition));
    }

    /**
     * Advances every mechanism by one time step and writes the resulting sensor values back to the devices.
     *
     * @param batteryVolts Battery voltage the motors run from.
     * @param dtS          Time since the last update, seconds.
     */
    public void update(double batteryVolts, double dtS) {
        // turret
        double turretVolts = turretLink.readMechanismVolts(batteryVolts);
        double turretBefore = turret.getPositionRotations();
        turret.update(turretVolts, dtS);
        turretLink.writeMechanismMotion(turret.getPositionRotations() - turretBefore, turret.getVelocityRps());

        // intake arm
        double armVolts = intakeArmLink.readMechanismVolts(batteryVolts);
        double armBefore = intakeArm.getPositionRotations();
        intakeArm.update(armVolts, dtS);
        intakeArmLink.writeMechanismMotion(intakeArm.getPositionRotations() - armBefore, intakeArm.getVelocityRps());

        // intake roller
        intakeRollerVolts = intakeRollerLink.readMechanismVolts(batteryVolts);
        double rollerBefore = intakeRoller.getPositionRotations();
        intakeRoller.update(intakeRollerVolts, dtS);
        intakeRollerLink.writeMechanismMotion(
            intakeRoller.getPositionRotations() - rollerBefore, intakeRoller.getVelocityRps()
        );

        // launcher flywheel
        double flywheelVolts = flywheelLink.readMechanismVolts(batteryVolts);
        double flywheelBefore = flywheel.getPositionRotations();
        flywheel.update(flywheelVolts, dtS);
        flywheelLink.writeMechanismMotion(flywheel.getPositionRotations() - flywheelBefore, flywheel.getVelocityRps());

        // climber
        double climberVolts = climberLink.readMechanismVolts(batteryVolts);
        double climberBefore = climber.getPositionRotations();
        climber.update(-climberVolts, dtS); // negative motor voltage extends
        climberLink.writeMechanismMotion(climber.getPositionRotations() - climberBefore, climber.getVelocityRps());

        // hood servo slews toward its command; the feedback wire reports where it actually is
        double commandedServo = hoodServoPwm.getPosition();
        double maxStep = SERVO_SLEW_PER_S * dtS;
        double servoError = commandedServo - hoodServoPosition;
        hoodServoPosition += Math.max(-maxStep, Math.min(maxStep, servoError));
        hoodFeedback.setVoltage(computeServoFeedbackVolts(hoodServoPosition));

        // spindex: unloaded Vortexes spin at free speed for their applied voltage
        spindexFeedVolts = spindexFeedSim.getAppliedOutput() * batteryVolts;
        spindexWheelVolts = spindexWheelSim.getAppliedOutput() * batteryVolts;
        spindexFeedSim.iterate(spindexFeedVolts * VORTEX_FREE_RPM_PER_VOLT, batteryVolts, dtS);
        spindexWheelSim.iterate(spindexWheelVolts * VORTEX_FREE_RPM_PER_VOLT, batteryVolts, dtS);

        // limit switches
        writeSwitches();
    }

    /**
     * Slows the flywheel as if a ball had just been launched through it.
     */
    public void applyBallLaunched() {
        double lossRps = -LAUNCHER_SHOT_SPEED_LOSS * flywheel.getVelocityRps();
        flywheel.addVelocity(lossRps);
    }

    /**
     * Captures the current state of every mechanism for the game.
     *
     * @return Snapshot of mechanism positions and outputs.
     */
    public MechanismSnapshot getSnapshot() {
        double turretRobotRelRotations = turretRelToRobotRel(turret.getPositionRotations());
        double hoodAngleDegrees = interp1(
            HoodConstants.LOWEST_SERVO_POSITION,
            HoodConstants.HIGHEST_SERVO_POSITION,
            HoodConstants.LOWEST_ANGLE_DEGREES,
            HoodConstants.HIGHEST_ANGLE_DEGREES,
            hoodServoPosition
        );
        return new MechanismSnapshot(
            Units.rotationsToRadians(turretRobotRelRotations),
            hoodAngleDegrees,
            flywheel.getVelocityRps() * 60,
            Units.rotationsToDegrees(intakeArm.getPositionRotations()),
            intakeRollerVolts,
            spindexFeedVolts,
            spindexWheelVolts,
            climber.getPositionRotations()
        );
    }

    // ---- private stateful helpers

    /** Writes every hall/limit switch from the current mechanism positions. DIO reads false when engaged. */
    private void writeSwitches() {
        boolean isTurretHallEngaged = turret.getPositionRotations() >= TURRET_HALL_EDGE_TUR_REL;
        turretHall.setValue(!isTurretHallEngaged);

        boolean isIntakeHallEngaged = intakeArm.getPositionRotations() >= INTAKE_HALL_EDGE_ROTATIONS;
        intakeHall.setValue(!isIntakeHallEngaged);

        boolean isClimberRetracted = climber.getPositionRotations() <= CLIMBER_SWITCH_BAND;
        boolean isClimberExtended = climber.getPositionRotations() >= 1 - CLIMBER_SWITCH_BAND;
        climberLowerSwitch.setValue(!isClimberRetracted);
        climberUpperSwitch.setValue(!isClimberExtended);
    }

    // ---- low-level helpers (static, no side effects)

    private static SparkFlex getFeedVortex(SpinDexSubsystem spindex) {
        return spindex.feedVortex;
    }

    private static SparkFlex getWheelVortex(SpinDexSubsystem spindex) {
        return spindex.wheelVortex;
    }

    /**
     * Converts a robot-relative turret angle to the turret-relative space the turret TalonFX reports, without
     * wrapping, so the hall edge lands inside the turret's continuous travel.
     *
     * @param robotRelRotations Robot-relative turret angle, rotations.
     * @return Turret-relative angle, rotations, in [0, 1).
     */
    private static double robotRelToTurretRel(double robotRelRotations) {
        double zeroOffsetRotations = TurretConstants.ZERO_OFFSET.in(Rotations);
        double turretRel = robotRelRotations - zeroOffsetRotations;
        return turretRel - Math.floor(turretRel);
    }

    /**
     * Converts a turret-relative angle to a robot-relative one.
     *
     * @param turretRelRotations Turret-relative angle, rotations.
     * @return Robot-relative angle, rotations, in [0, 1); 0 faces the robot's front, counter-clockwise positive.
     */
    private static double turretRelToRobotRel(double turretRelRotations) {
        double zeroOffsetRotations = TurretConstants.ZERO_OFFSET.in(Rotations);
        double robotRel = turretRelRotations + zeroOffsetRotations;
        return robotRel - Math.floor(robotRel);
    }

    /**
     * Voltage the hood servo's feedback wire produces at a servo position, from the robot code's calibration points.
     *
     * @param servoPosition Normalized servo position, 0..1.
     * @return Feedback voltage, volts.
     */
    private static double computeServoFeedbackVolts(double servoPosition) {
        return interp1(
            HoodConstants.SERVO_VOLTAGE_REF0,
            HoodConstants.SERVO_VOLTAGE_REF1,
            HoodConstants.SERVO_VOLTAGE_AT_REF0,
            HoodConstants.SERVO_VOLTAGE_AT_REF1,
            servoPosition
        );
    }

    /**
     * Linear interpolation of xq between (x0, y0) and (x1, y1).
     *
     * @param x0 Input value at the first reference point.
     * @param x1 Input value at the second reference point; must not equal x0.
     * @param y0 Output value at the first reference point.
     * @param y1 Output value at the second reference point.
     * @param xq Input value to interpolate at; need not lie between x0 and x1.
     * @return Interpolated output value.
     */
    private static double interp1(double x0, double x1, double y0, double y1, double xq) {
        return y0 + (xq - x0) * (y1 - y0) / (x1 - x0);
    }
}
