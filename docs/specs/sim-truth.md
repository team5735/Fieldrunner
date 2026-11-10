# Spec: simulation ground truth in the robot code

Status: draft for review (do not start). Read `CLAUDE.md` (project rules) first. Code style: Fieldrunner code follows
`../FRC/STYLE.md`; code added to `FRC-2026` follows that repo's house style instead, written down in
`docs/specs/frc-2026-style.md`. FRC-2026 work goes on branch `feature/driver-sim`. If something in
the code contradicts this spec or a step turns out impossible, stop and report rather than improvising.

## Goal

Make it obvious what the **world** is (ground truth) and what the **robot believes** (its estimate), and make sure
nothing the robot code does can change the world. Today the robot's odometry estimate *is* the world: pressing Back
(`seedFieldCentric`) spins the robot on the field, and Unity's collisions are applied with `drivetrain.resetPose`, so
the robot always knows exactly where it is after hitting a wall.

The new sim lives in the robot code (`FRC-2026`, package `frc.robot.sim`) and runs under `simulationPeriodic`. It works
with plain WPILib simulation and AdvantageScope, with no Unity at all. Unity is an optional add-on that feeds collisions
back into the ground truth; without it, collisions just aren't modeled.

## Principles

1. **Simple first.** This is reference code for students. Prefer the plain, "batteries included" WPILib / CTRE / REV
   way, even when it costs accuracy. No custom physics where a WPILib class exists; no extra threads; no clever
   reconciliation.
2. **Compatible with WPILib sim and AdvantageScope.** Nothing in `frc.robot.sim` knows about Unity or Fieldrunner.
3. **One-way flow.** Ground truth lives in sim-owned fields that robot code has no reference to.

```
robot code --actuator commands (Talon voltage, Spark duty, servo PWM)--> sim physics --> ground truth
                                 Unity collisions (optional), placement --> ground truth
robot code <--sensors (rotor position/velocity, gyro, DIO, analog, Limelight)-- computed from ground truth
```

- The sim **reads** only actuator commands: what the robot does to the world.
- The sim **writes** only through the HAL and vendor sim states: what the robot's sensors report.
- The sim never calls methods that change the robot's belief (`resetPose`, `seedFieldCentric`, `setYaw`,
  `addVisionMeasurement`) and never reads the estimate. Robot code never reads the sim.
- Sensors report what their hardware measures and may diverge from ground truth (a gyro has its own zero; wheel
  encoders count wheel spin, not robot motion). That divergence is correct.

## What owns what

| Thing | Ground truth | Robot's belief |
|---|---|---|
| Chassis pose | `DrivetrainSim` truth `Pose2d` | `drivetrain.getState().Pose` (odometry + vision) |
| Swerve modules, gyro | CTRE `updateSimState()` (DCMotorSims inside; writes the Pigeon) | Talon / CANcoder / Pigeon readings |
| Mechanisms | WPILib sims in `MechanismSim` | Talon / Spark / DIO / analog readings |
| Battery | `BatterySim` -> `RoboRioSim` | `RobotController.getBatteryVoltage()` |
| Vision | truth pose + Gaussian noise -> Limelight NT | `LimelightSubsystem` estimate |
| Fuel, field, collisions, scoring | Unity (optional) | none |
| Match, DS, joysticks | Fieldrunner bridge via `DriverStationSim` | `DriverStation` |

## Robot code changes (`FRC-2026`)

### `Robot`

```java
private RobotSim sim;  // simulation only; null on the real robot

@Override
public void simulationInit() { sim = new RobotSim(this); }

@Override
public void simulationPeriodic() { sim.update(); }

/** Simulation only: the sim world, for tools that drive it (Fieldrunner). Null on a real robot. */
public RobotSim getSim() { return sim; }
```

`simulationPeriodic` runs every 20 ms, after `robotPeriodic`. That rate is fine for everything here; the swerve modules
keep CTRE's own 5 ms sim thread (below).

### `DrivetrainSubsystem`

No change. Its CTRE-template `startSimThread()` keeps calling `updateSimState()` every 5 ms. That steps each module's
drive and steer motor (WPILib `DCMotorSim` inside CTRE's sim) and updates the Pigeon from the wheel motion. The Pigeon
keeps its own zero, so it diverges from the field heading until the robot code calibrates it, like a real gyro.

Accepted simplification: collisions cannot rotate the robot (the Pigeon only sees wheel-driven rotation), so Unity
corrections are translation only, as today.

### `frc.robot.sim` (new)

Three small classes plus one that ties them together. Each is a plain class with a constructor and `update()`.

**`RobotSim`**: constructed with the `Robot` so it can reach **device handles only** (the drivetrain for chassis
speeds and module constants, the spindex SparkFlexes for REV's sim). It must not use subsystem state or the pose
estimate. `update()` calls, in order: `mechanisms.update(dt)`, `battery.update()`, `drivetrain.update(dt)`,
`limelights.update()`, then publishes telemetry. `dt` is measured with `Timer`.

**`DrivetrainSim`**: owns the truth pose.
- `update(dt)`: read `drivetrain.getState().Speeds` (robot-relative chassis speeds from the simulated modules; not
  affected by `resetPose` or `seedFieldCentric`) and advance:
  `truthPose = truthPose.exp(new Twist2d(vx * dt, vy * dt, omega * dt))`.
- `place(Pose2d)`: puts the robot somewhere (start of practice or match, reset). Sets the truth pose only.
- `push(Translation2d)`: moves the truth pose by a field-frame translation (collisions).
- `getTruthPose()`.
- Publishes the truth pose as `Telemetry.field.getObject("SimTruth")`, so AdvantageScope and Glass show truth and
  estimate side by side.

**`MechanismSim`**: replaces the bridge's `RobotMechanismSim`, using WPILib sim classes in the standard CTRE pattern:
read `talonSim.getMotorVoltage()`, step the WPILib sim, write `setRawRotorPosition` / `setRotorVelocity` (times the
gear ratio, with the device's inversion sign).

| Mechanism | WPILib class | Sensors written |
|---|---|---|
| Turret | `DCMotorSim` | turret Talon rotor; hall switch `DIOSim` at its position |
| Flywheel | `FlywheelSim` | launcher Talon rotor velocity; `applyBallLaunched()` lowers its speed |
| Intake arm | `SingleJointedArmSim` (gravity, hard stops built in) | slapdown Talon rotor; limit `DIOSim` |
| Intake roller | `DCMotorSim` | roller Talon rotor |
| Climber | `DCMotorSim`, clamped to its stroke | climber Talon rotor; upper and lower `DIOSim` |
| Spindex feed and wheel | `DCMotorSim` -> REV `SparkFlexSim.iterate` | Spark encoders |
| Hood servo | none (no servo sim): feedback follows the commanded `PWMSim` position instantly | `AnalogInputSim` |

Build the plants with `LinearSystemId.createDCMotorSystem(kV, kA)` (or `createFlywheelSystem`) from the team's existing
feedforward gains where they exist, so mechanisms respond as the code was tuned to expect; otherwise `DCMotor` +
gearing + an estimated moment of inertia. Device IDs, gear ratios, sensor positions and quirks (the climber's DIO
names are swapped; REV's sim needs the SparkFlex instances) are in the bridge's current `RobotMechanismSim`; carry them
over. Check that turret and intake zeroing still work with `setRawRotorPosition` (the robot's `setPosition` should only
add an offset on the device); if they don't, use `addRotorPosition` with deltas and say why in a comment.

**Battery** (two lines in `RobotSim`, no class needed):
`RoboRioSim.setVInVoltage(BatterySim.calculateDefaultBatteryLoadedVoltage(currents))`, with the current draw of every
WPILib mechanism sim. The drivetrain's current is left out (CTRE doesn't expose it); accepted.

**`LimelightSim`**: every update, for each camera (`limelight-fone`, `limelight-ftwo`):
1. read the truth pose;
2. add Gaussian noise (`Random.nextGaussian()`; sigma 0.03 m in x and y, 1 degree in yaw, as named constants);
3. publish to the camera's NT table what `LimelightSubsystem.PoseEstimate` parses:
   - `botpose_wpiblue`: `[x, y, 0, 0, 0, yawDeg, latencyMs, 1, 0, avgDistM, avgArea]` + one fiducial
     `[id, txnc, tync, ta, distToCameraM, distToRobotM, ambiguity]` (latency 30 ms, distance 3 m, ambiguity 0.1);
   - `stddevs`: the sigmas (the parser drops the frame if this entry is missing).

No tag geometry and no field of view: the robot "sees" tags everywhere. The robot code's own checks (in-field,
ambiguity, spin rate) still decide what it accepts.

## Fieldrunner changes

- **Bridge publishes truth.** `/DriverSim/robot/state` pose and velocity come from `robot.getSim()` truth (velocity:
  chassis speeds rotated by the truth heading). Also publish the estimate (new fields) so Unity can later show a
  "robot thinks it's here" ghost; bump `GameLink.PROTOCOL_VERSION` and mirror the layout in C#.
- **Collisions go to truth.** `PoseCorrector` stays, recording the **truth** pose; each correction becomes
  `sim.getDrivetrain().push(...)`, never `drivetrain.resetPose`. Remove its outside-reset detection: only placement
  moves the truth discontinuously now, and the bridge calls `markDiscontinuity` itself when it places the robot.
- **Placement** (`RESET_POSE`, practice/match start, bridge start): `sim.place(pose)` and also
  `drivetrain.resetPose(pose)`, as the drive team would by starting from a known spot. This is the bridge acting as the
  human, not the sim. Comment it as such.
- **Ball launched** -> `sim.getMechanisms().applyBallLaunched()`.
- **Delete** `bridge/.../mech/` and its test once `MechanismSim` passes the self-test.
- Unity protocol for corrections is unchanged.

## Self-test (new and changed steps)

- Press Back (`seedFieldCentric`): the truth pose does not change.
- Drive into the blue alliance wall with the game attached: the truth stops at the wall, the estimate's odometry keeps
  going, and within a few seconds Limelight fusion brings the estimate back within 0.2 m of truth.
- Existing turret zero, intake, flywheel, hood, climber and driving steps pass against `MechanismSim`.
- `selfTestDriverSim` and `selfTestDriverSimWithGame` pass (step counts grow by the new steps).

Also: plain `./gradlew simulateJava` on FRC-2026 (no Fieldrunner) runs, and AdvantageScope shows `SimTruth` and the
estimate.

## Docs

- `CLAUDE.md` ground rules: point FRC-2026 code at `docs/specs/frc-2026-style.md`; robot code now carries `frc.robot.sim` and the `Robot` sim hooks; the bridge carries only
  the game link (NT protocol, driver station, match, commands, corrections). Remove gotchas about outside resets.
- README architecture diagram and layout.

## Out of scope

- Collisions that rotate the robot; wheel slip or traction; other robots.
- Tag visibility, field of view or occlusion for the Limelight sim.
- Drivetrain current in the battery model.

## Decided

- FRC-2026 branch: `feature/driver-sim`.
- `frc.robot.sim` follows `docs/specs/frc-2026-style.md` (the repo's own style), not `FRC/STYLE.md`.

## Under discussion

- Whether Unity should also feed back collision rotation and wheel slip (currently out of scope above).
