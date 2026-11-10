# FRC-2026 house style (for code added to the robot repo)

`FRC/STYLE.md` does **not** apply inside `FRC-2026`. New code there (e.g. a subsystem's `simulationPeriodic()`) should look like the code
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
  code lives in the subsystem it simulates.
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

## Classes

- Hardware objects are `private final` fields initialized inline (`new TalonFX(Constants.TURRET_MOTOR_ID)`); the
  constructor applies configuration.
- Subsystems extend `SubsystemBase`; commands are built with the subsystem's factories (`runOnce`, `startEnd`,
  `runEnd`, `startRun`) and given a `.withName(...)`. Bigger commands extend `Command`.
- Utility classes have a private constructor that throws `UnsupportedOperationException("This is a utility class!")`.
- `var` is fine for obvious local types; lambdas and method references are used freely; ternaries are fine for short
  choices.

## Simulation

- Each subsystem owns its simulation: private sim fields beside its hardware (`DCMotorSim`, `TalonFXSimState`,
  `DIOSim`, ...) built as plain field initializers, stepped in its own `simulationPeriodic()`.
- Sim-only numbers (noise sigmas, estimated inertias, gains without robot-code values) are written where they are
  used: a literal with a trailing comment at a single use site; a local variable when one method uses it more than
  once; a private static final in the subsystem when several of its methods share it. They don't go in `XConstants`.
- Methods that exist for the Fieldrunner game bridge are prefixed `fr` (`frGetSimAngleRobotRel()`, `frSimPush(...)`),
  sit together at the end of the subsystem's sim section, and carry a one-line note that they are for the Fieldrunner
  game bridge. Robot code never calls them.

## Telemetry

- Publish through the team's `NTable` helper: `private final NTable table = NTable.root("launcher");` then
  `table.set("speed rpm", value)`. Keys are short lowercase words, spaces allowed. (Some older code uses
  `SmartDashboard.putX("intake/..."`); new code uses `NTable`.)
- Field visualization goes through `Telemetry.field` (`Telemetry.field.getObject("name").setPose(...)`).

## Comments

- A Javadoc block on each class saying what it is for, and on public methods whose purpose isn't obvious
  (`@param` / `@return` where helpful). A comment that fits on one line is a `//` comment, not a one-line `/** */`
  (`// the robot's true pose, field frame`, not `/** {@return the robot's true pose, field frame} */`). Inline `//`
  comments are short, above the line or trailing.
- Say what the code does. Don't defend it: no answers to questions nobody asked, no "never", "only" or "can't" lists of
  what it doesn't do, no design rationale for the reader to be reassured by. Bad:

  ```java
  // The robot's own pose estimate ({@code drivetrain.getState().Pose}) is never read, so nothing the robot code does
  // to its estimate (resetPose, seedFieldCentric, vision) can move the real robot.
  ```

  Good: `// adds up the chassis speeds the modules produce into a pose`.
  A real gotcha (a vendor quirk, a sign flip, a unit) still belongs in a comment.
- This code is reference material for students: describe what it does in plain language, not the mechanics of Java.
- `// TODO` is used for known follow-ups.
