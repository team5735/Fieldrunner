# FRC-2026 house style (for code added to the robot repo)

`FRC/STYLE.md` does **not** apply inside `FRC-2026`. New code there (e.g. `frc.robot.sim`) should look like the code
already in the repo, so students reading it see one style. This guide is distilled from `Robot`, the subsystems,
`constants/`, `commands/` and `util/` in FRC-2026 (2026 season). When in doubt, copy the nearest existing file.

## Layout and formatting

- 4-space indent, 8-space continuation indent, K&R braces (the VS Code / WPILib default Java formatter).
- Keep lines under about 120 columns.
- One blank line between methods; fields grouped at the top of the class.
- No WPILib license header required (only template-derived files have it).

## Imports

Order, with a blank line between groups: static `edu.wpi.first.units.Units.*` imports; `java.*`; vendor (`com.ctre`,
`com.revrobotics`, `com.pathplanner`); `edu.wpi.first.*` followed directly by `frc.*`. No wildcard imports. Units are
static-imported (`Meters`, `Degrees`, `RotationsPerSecond`, ...).

## Naming

- Classes are named for what they are: `LauncherSubsystem`, `LauncherConstants`, `PIDToPose`, `MatchState`. Sim
  classes follow suit: `RobotSim`, `DrivetrainSim`, `MechanismSim`, `LimelightSim`.
- Methods and fields are camelCase. Getters use `get`/`is` (`getRPM()`, `isAtPosition()`); command factories return a
  `Command` and are named `getXCommand()` or `getX()` (`getIntakeForwardRollCommand()`, `getLaunchFuel()`).
- Constants are `public static final UPPER_SNAKE_CASE`.
- Short, plain field names for hardware: `krakenLeft`, `intakeRoller`, `hallLimit`, `ff`, `table`.

## Constants

- CAN IDs, DIO / PWM / analog channels live in `frc.robot.constants.Constants`, with a trailing `// DIO`-style comment
  for the bus.
- Per-mechanism numbers live in `frc.robot.constants.<Name>Constants` (a plain `public class` of `public static final`
  fields): gains `KS`, `KV`, `KA`, `KG`, `KP`; `GEAR_REDUCTION` / `GEARING` with a comment saying which gear is which.
- Physical quantities use WPILib units types where the existing code does (`Angle`, `AngularVelocity`, `Distance`:
  `Rotations.of(0.25)`), converted with `.in(Unit)` at the point of use. Plain `double` is fine for gains and for
  math inside a method.
- Sim-only numbers (noise sigmas, latency, estimated moments of inertia) go in a new
  `frc.robot.constants.SimConstants`, same shape.

## Classes

- Hardware objects are `private final` fields initialized inline (`new TalonFX(Constants.TURRET_MOTOR_ID)`); the
  constructor applies configuration.
- Subsystems extend `SubsystemBase`; commands are built with the subsystem's factories (`runOnce`, `startEnd`,
  `runEnd`, `startRun`) and given a `.withName(...)`. Bigger commands extend `Command`.
- Utility classes have a private constructor that throws `UnsupportedOperationException("This is a utility class!")`.
- `var` is fine for obvious local types; lambdas and method references are used freely; ternaries are fine for short
  choices.

## Telemetry

- Publish through the team's `NTable` helper: `private final NTable table = NTable.root("launcher");` then
  `table.set("speed rpm", value)`. Keys are short lowercase words, spaces allowed. (Some older code uses
  `SmartDashboard.putX("intake/..."`); new code uses `NTable`.)
- Field visualization goes through `Telemetry.field` (`Telemetry.field.getObject("name").setPose(...)`).

## Comments

- A Javadoc block on each class saying what it is for, and on public methods whose purpose isn't obvious
  (`@param` / `@return` where helpful). Inline `//` comments explain *why*, short, above the line or trailing.
- This code is reference material for students: explain the idea (what is ground truth, why a sensor is written this
  way) in plain language, not the mechanics of Java.
- `// TODO` is used for known follow-ups.
