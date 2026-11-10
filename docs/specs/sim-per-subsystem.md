# Spec: each subsystem owns its simulation

Status: approved; implement against the plan below. Supersedes the `frc.robot.sim` / `SimConstants` / `RobotSim` parts of
`sim-truth.md` (the ground-truth principles, Unity changes and self-test steps there still hold).

## Context

Today all simulation lives in `frc.robot.sim` (`RobotSim`, `DrivetrainSim`, `MechanismSim`, `LimelightSim`), driven
from `Robot.simulationPeriodic`. That central sim knows every subsystem's devices, gear ratios and quirks. The WPILib
pattern is the reverse: each subsystem owns its own sim objects next to its hardware and steps them in its own
`simulationPeriodic()`, which the `CommandScheduler` calls after `periodic()` whenever the robot runs in simulation
(`Subsystem.simulationPeriodic()` exists on both `SubsystemBase` and the `Subsystem` interface). The subsystem's owners
then write and maintain its sim, and the per-subsystem `Tester` robots (`CURRENT_ROBOT = TURRET` etc.) get a working
sim for free. `DrivetrainSubsystem` already works this way (its CTRE `startSimThread()`).

The ground-truth rules stay: a subsystem's sim reads only actuator outputs and writes only sensors; truth lives in
private sim fields that only `simulationPeriodic` touches.

## Shape (recommended)

Per subsystem, in its own file, WPILib-example style:

- private sim fields beside the hardware (`private final DCMotorSim turretSim = ...`, `TalonFXSimState`, `DIOSim`);
- `@Override public void simulationPeriodic()`: read the Talon's motor voltage, step the WPILib sim, write rotor
  position/velocity and switches (the code now in `MechanismSim.updateTurret` etc. moves here almost verbatim);
- a few public methods only where the Fieldrunner game bridge needs truth or acts on the world. They are prefixed `fr`
  (Fieldrunner): `frGetSimAngleRobotRel()`, `frSimPush(...)`. Each carries a short note that it is for the Fieldrunner
  game bridge, and they sit together at the end of the subsystem's sim section.

| Subsystem | Sim it owns | Outside API (for Fieldrunner) |
|---|---|---|
| `DrivetrainSubsystem` | truth pose integrated from module speeds; `SimTruth` on `Telemetry.field` | `frGetSimTruthPose()`, `frSimPlace(pose)`, `frSimPush(translation, rotation)` |
| `LimelightSubsystem` | publishes `botpose_wpiblue` / `stddevs` = truth + Gaussian noise (it already holds `drivetrain`) | none |
| `TurretSubsystem` | `DCMotorSim`, hall `DIOSim` | `frGetSimAngleRobotRel()` |
| `LauncherSubsystem` | `FlywheelSim` | `frGetSimRpm()`, `frSimBallLaunched()` |
| `IntakeSubsystem` | `SingleJointedArmSim` (arm) + hall `DIOSim`, `DCMotorSim` (roller) | `frGetSimArmAngle()`, `frGetSimRollerVolts()` |
| `ClimberSubsystem` | `DCMotorSim` + two `DIOSim` | `frGetSimExtension()` |
| `HoodSubsystem` | `AnalogInputSim` follows the servo (`PWMSim`) | `frGetSimAngle()` |
| `SpinDexSubsystem` | two `DCMotorSim` -> `SparkFlexSim` (it owns the SparkFlexes, so they can go back to private) | `frGetSimFeedVolts()`, `frGetSimWheelVolts()` |

`frc.robot.sim` and `SimConstants` go away. `Robot` loses `getSim()` and its sim field; `Robot.simulationPeriodic`
is left empty.

## Fieldrunner bridge

Reads truth and acts through the subsystems' sim API instead of `robot.getSim()`:
`robot.drivetrain.frGetSimTruthPose()`, `robot.drivetrain.frSimPush(...)`, `placeRobot` = `frSimPlace` + `resetPose`,
`robot.launcher.frSimBallLaunched()`, mechanism truth getters for `makeRobotState`. Self-test and
`DriveFeelMeasurement` switch the same way. No protocol change.

## Decisions (user, 2026-11-10)

1. Sim-only numbers are written where they are used (literal at a single use site, local variable for repeats in one
   method, private static final for numbers several methods of one subsystem share); `SimConstants` is deleted and no
   `XConstants` gains a sim section. The start pose is a literal in `DrivetrainSubsystem`; the bridge reads it with
   `frGetSimTruthPose()` at startup.
2. Battery sag is dropped: no `BatterySim`, `Robot.simulationPeriodic` ends up empty (left as the template has it).
3. Sim objects are plain field initializers, constructed on the real robot too (WPILib example style). If one vendor
   sim class turns out unsafe off-sim (watch `SparkFlexSim`), that one alone is created under `isSimulation()`.

## How it gets built

- Spec update in `Fieldrunner/docs/specs/sim-truth.md` (replace the `frc.robot.sim` section with the per-subsystem
  table above), then a Sonnet (medium) subagent implements, I review and commit, as before.
- FRC-2026 code follows `Fieldrunner/docs/specs/frc-2026-style.md` (incl. the no-defensive-comments and one-line `//`
  rules), plus the `fr` prefix rule for Fieldrunner-facing methods (add it to that guide); bridge Java follows
  `FRC/STYLE.md`.
- Order: move one subsystem at a time from `MechanismSim` into its subsystem (drivetrain truth + Limelight first, then
  turret, intake, launcher, climber, hood, spindex), keeping the self-test green after each; then delete
  `frc.robot.sim`, `SimConstants` and `Robot.getSim()`; then switch the bridge, self-test and docs
  (`Fieldrunner/CLAUDE.md` ground rules and gotchas, README).
- Commits: the reviewer amends the result into the existing unpushed commits on both repos.

## Verification

- `./gradlew build` on FRC-2026 (compiles robot code; real-robot path unaffected).
- `./gradlew testDriverSim`, `selfTestDriverSim` (15/15), `selfTestDriverSimWithGame` (23/23) from FRC-2026.
- A `Tester` robot in sim (e.g. `CURRENT_ROBOT = TURRET`, `simulateJava`) zeroes its turret on the hall switch.
- AdvantageScope / NT shows `SimTruth` next to the estimate in plain `simulateJava`.

## Implementation notes

- Environment, sim-stopping rules, formatter and Unity build as in `sim-truth.md` "Environment". Don't commit or push.
- Start from the current code: `FRC-2026/src/main/java/frc/robot/sim/*.java` and `constants/SimConstants.java` hold
  the working sim; move it, don't rewrite it. Behavior (signs, gear ratios, start positions, switch bands, noise)
  stays identical; the self-tests are the regression check.
- `fr` methods: public, prefixed `fr`, grouped at the end of each subsystem's sim section, each with a one-line
  `// for the Fieldrunner game bridge: ...` comment. Nothing in robot code calls them.
- Sim fields are private. `simulationPeriodic()` is the only robot-code method that touches them (plus the `fr`
  methods).
- `SpinDexSubsystem.feedVortex` / `wheelVortex` go back to `private` once nothing outside uses them.
- `simulationPeriodic` runs every 20 ms from the `CommandScheduler`; use `TimedRobot.kDefaultPeriod` (or measured
  time) for `dt`, consistent across subsystems.
- `DrivetrainSubsystem` implements CTRE's class plus `Subsystem`; override `simulationPeriodic()` there. Keep
  `startSimThread()` as is.
- Gates: after the drivetrain + Limelight move, and after each mechanism, `./gradlew selfTestDriverSim` must pass
  15/15 (the bridge can read through temporary adapters while mid-move, but the final code has none). Final gates:
  `./gradlew build`, `testDriverSim`, `selfTestDriverSim` 15/15, `selfTestDriverSimWithGame` 23/23 (the Unity player
  is already built; no Unity change is needed), and a `Tester` check: with `CURRENT_ROBOT = TURRET` the turret zeroes
  in sim. Do that check without committing the constant change (set it, run, put it back) and say how you ran it.
- Docs: `Fieldrunner/CLAUDE.md` ground rules and gotchas (names of classes and methods), README layout/architecture,
  and `sim-truth.md` pointers if they name removed classes.
