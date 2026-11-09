package driversim.mech;

/**
 * State of the robot's mechanisms at one instant, as the game needs to draw and play them.
 *
 * @param turretAngleRobotRelRadians Turret heading relative to the robot's front, radians, counter-clockwise positive.
 * @param hoodAngleDegrees           Hood launch angle, degrees (8 = lowest, 45 = highest).
 * @param flywheelRpm                Flywheel speed, RPM.
 * @param intakeArmAngleDegrees      Intake arm angle above horizontal, degrees (about 15 deployed, 105 stowed).
 * @param intakeRollerVolts          Intake roller voltage; positive pulls balls in.
 * @param spindexFeedVolts           Spindex feed voltage; negative feeds balls to the launcher.
 * @param spindexWheelVolts          Spindex wheel voltage; negative turns the hopper forward.
 * @param climberExtension           Climber travel, 0 fully retracted to 1 fully extended.
 */
public record MechanismSnapshot(
    double turretAngleRobotRelRadians,
    double hoodAngleDegrees,
    double flywheelRpm,
    double intakeArmAngleDegrees,
    double intakeRollerVolts,
    double spindexFeedVolts,
    double spindexWheelVolts,
    double climberExtension
) {}
