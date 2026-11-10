# Spec: simulation ground truth in the robot code

Status: implemented; the code layout (`frc.robot.sim`, `SimConstants`) is superseded by `sim-per-subsystem.md`. Read `CLAUDE.md` (project rules) first. Code style: Fieldrunner code follows
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
| Chassis pose | `DrivetrainSubsystem` truth `Pose2d` | `drivetrain.getState().Pose` (odometry + vision) |
| Swerve modules, gyro | CTRE `updateSimState()` (DCMotorSims inside; writes the Pigeon) | Talon / CANcoder / Pigeon readings |
| Mechanisms | WPILib sims in each subsystem | Talon / Spark / DIO / analog readings |
| Battery | not modeled (roboRIO sim default) | `RobotController.getBatteryVoltage()` |
| Vision | truth pose + Gaussian noise -> Limelight NT | `LimelightSubsystem` estimate |
| Fuel, field, collisions, scoring | Unity (optional) | none |
| Match, DS, joysticks | Fieldrunner bridge via `DriverStationSim` | `DriverStation` |

## Robot code changes (`FRC-2026`)

The sections below describe the first layout (`frc.robot.sim`, `RobotSim`, `Robot.getSim()`). It is superseded by
`sim-per-subsystem.md`: each subsystem owns its sim, `Robot` has no sim hooks, and the bridge reads through `fr` methods.

### `Robot`

```java
private RobotSim sim;  // simulation only; null on the real robot

// at the end of the constructor (Fieldrunner's bridge is attached right after construction, so the sim must exist
// before simulationInit runs)
if (isSimulation()) {
    sim = new RobotSim(this);
}

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

Accepted simplification: the Pigeon only feels wheel-driven rotation. When Unity rotates the truth (a collision), the
gyro doesn't feel it, so the robot's heading estimate drifts until vision (or the driver's reset) corrects it. That
matches real life closely enough: teams see gyro drift and reset it.

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
- `push(Translation2d translationField, Rotation2d rotation)`: moves and turns the truth pose (collisions, slip).
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
- **Collisions and slip go to truth.** `PoseCorrector` stays, recording the **truth** pose, and now reconciles heading
  the same way it reconciles position; each correction becomes `sim.getDrivetrain().push(translation, rotation)`,
  never `drivetrain.resetPose`. Remove its outside-reset detection: only placement
  moves the truth discontinuously now, and the bridge calls `markDiscontinuity` itself when it places the robot.
- **Placement** (`RESET_POSE`, practice/match start, bridge start): `sim.place(pose)` and also
  `drivetrain.resetPose(pose)`, as the drive team would by starting from a known spot. This is the bridge acting as the
  human, not the sim. Comment it as such.
- **Ball launched** -> `sim.getMechanisms().applyBallLaunched()`.
- **Delete** `bridge/.../mech/` and its test once `MechanismSim` passes the self-test.
- **Correction protocol gains heading:** `/DriverSim/game/poseCorrection` becomes
  `[timestampS, xFieldM, yFieldM, headingFieldRad]` (absolute constrained pose of the state at `timestampS`).

## Unity changes

- **Collision rotation** (`RobotCollision.Resolve`): each push also turns the robot. Contact point
  `c = other.ClosestPoint(robotCenter)`; lever `r = c - center` (horizontal, field frame); turn
  `dTheta = COLLISION_TURN_GAIN * cross(r, push) / |r|^2`, clamped to `MAX_TURN_RAD_PER_FRAME`. The penetration is
  re-measured with the new heading on the next pass. Goal: a robot driven corner-first into a wall swings flush; the
  exact gain is tuned by eye and by the collision self-test (sign check: a robot at 30 degrees pushed into the blue
  alliance wall ends nearer 0 degrees, not farther).
- **Wall friction (slip)**: while pushed, the robot's velocity along the wall is reduced:
  `tangentLoss = min(|vTangent| * dt, WALL_FRICTION * pushedM)` opposite `vTangent` (velocity from `RobotState`).
  The wheels keep turning, so this is slip as the robot code sees it.
- `CollisionResult` gains the constrained heading; the drawn robot uses it; `SendPoseCorrection` sends it.
- `RobotState.cs` parses the new estimate fields; `DebugHud` shows "estimate error: x.xx m, y deg" (no ghost model yet).

## Self-test (new and changed steps)

- Press Back (`seedFieldCentric`): the truth pose does not change.
- Drive into the blue alliance wall with the game attached: the truth stops at the wall, the estimate's odometry keeps
  going, and within a few seconds of holding still Limelight fusion brings the estimate back within 0.2 m of truth.
- With the game attached, drive at the wall at an angle: the truth heading changes (collision rotation) while the
  Pigeon yaw does not.
- Unity `--collision-selftest`: the rotation sign check above and wall friction (sliding along a wall loses speed).
- Existing turret zero, intake, flywheel, hood, climber and driving steps pass against `MechanismSim`.
- `selfTestDriverSim` and `selfTestDriverSimWithGame` pass (step counts grow by the new steps).

Also: plain `./gradlew simulateJava` on FRC-2026 (no Fieldrunner) runs, and AdvantageScope shows `SimTruth` and the
estimate.

## Docs

- `CLAUDE.md` ground rules: point FRC-2026 code at `docs/specs/frc-2026-style.md`; robot code now carries `frc.robot.sim` and the `Robot` sim hooks; the bridge carries only
  the game link (NT protocol, driver station, match, commands, corrections). Remove gotchas about outside resets.
- README architecture diagram and layout.

## Out of scope

- The gyro feeling collision rotation (accepted drift, see above); traction limits other than wall friction; motor
  load from slip; other robots; a drawn estimate ghost.
- Tag visibility, field of view or occlusion for the Limelight sim.
- Drivetrain current in the battery model.

## Decided

- FRC-2026 branch: `feature/driver-sim`.
- `frc.robot.sim` follows `docs/specs/frc-2026-style.md` (the repo's own style), not `FRC/STYLE.md`.


## Implementation plan

Do the steps in order. Each ends with a gate that must pass before the next starts; if a gate can't pass, stop and
report. Don't commit or push in either repo; the reviewer commits.

### Environment

- `export JAVA_HOME=~/.local/jdk-17 PATH=~/.local/jdk-17/bin:$PATH` (no system JDK). Run Gradle from `../FRC-2026`,
  which is on branch `feature/driver-sim`. Redirect Gradle output to a file in your scratchpad and grep it.
- Never kill a running sim with a signal (core dumps, broken next start). Let self-tests finish. If WPILib aborts at
  startup with `fmt/base.h: assertion failed: negative value`, wait a few seconds and retry.
- Java in `FRC-2026` follows `docs/specs/frc-2026-style.md`; Java in `bridge/` and C# follow `../FRC/STYLE.md`
  (format bridge Java with `scripts/format.java.sh`; if clang-format is missing:
  `python3 -m venv <scratch>/cf && <scratch>/cf/bin/pip install clang-format` and put it on PATH). ASCII only.
- Unity: build with `scripts/build-unity.sh linux` (batch mode; fails if an editor has the project open). The
  collision self-test: run the built player with `-batchmode -nographics --collision-selftest` (see README).

### Step 1: truth chassis and Limelight sim in FRC-2026; bridge uses the truth

1. `frc.robot.constants.SimConstants`: Limelight sigmas, latency, fake tag distance/ambiguity; anything else sim-only.
2. `frc.robot.sim.DrivetrainSim`, `LimelightSim`, `RobotSim` as specified (RobotSim without mechanisms or battery
   for now). Construct in the `Robot` constructor under `isSimulation()`; `simulationPeriodic` calls `sim.update()`;
   add `getSim()`. Telemetry: `NTable.root("sim")` (truth pose x/y/deg) plus the `SimTruth` Field2d object.
   `LimelightSim` writes through `NetworkTableInstance.getDefault().getTable(cameraName)` (that is the table
   `LimelightSubsystem` reads via `NTable.root(name)`; check). `stddevs` is 12 doubles (MegaTag1 first six:
   x, y, z, roll, pitch, yaw). The robot rejects frames while its Pigeon reports rotation (limit compares
   deg/s against a rad/s number), so convergence happens while driving straight or holding still; that is the
   robot code's behavior, leave it.
3. Bridge (`DriverSimBridge`):
   - publish truth pose and truth velocity (truth chassis speeds rotated by truth heading) in the existing fields;
     add `ESTIMATE_X_M`, `ESTIMATE_Y_M`, `ESTIMATE_HEADING_RAD` to `RobotStateLayout` (and the C# mirror, which
     only needs to parse them in this step), bump `GameLink.PROTOCOL_VERSION`;
   - `PoseCorrector.record` gets the truth pose; corrections call `sim.getDrivetrain().push(translation, kZero)`
     (heading comes in step 3); remove the outside-reset detection, keep `markDiscontinuity`, call it on every
     placement; update `PoseCorrectorTest`;
   - a single `placeRobot(Pose2d)` helper does `sim.getDrivetrain().place(pose)`, `drivetrain.resetPose(pose)` and
     `markDiscontinuity`, with a comment that this is the drive team placing the robot, not the sim; use it in the
     constructor and for `RESET_POSE`.
4. `SelfTest` and `DriveFeelMeasurement`: place through the bridge's `placeRobot`; every position check reads the
   truth pose (`robot.getSim().getDrivetrain().getTruthPose()`), except checks that are about what the robot code
   believes. New steps: "Back leaves truth pose unchanged" (press Back for one loop, compare truth before/after,
   tolerance 1 mm / 0.1 deg while not driving); and, game attached, "wall: truth stops, estimate recovers" (drive
   into the blue wall 2 s, release; truth is flush, then within 5 s the estimate is within 0.2 m of truth).
5. Gate: `./gradlew testDriverSim`, `./gradlew selfTestDriverSim` (all steps pass; note the new count), and
   `./gradlew simulateJava` on FRC-2026 starts and publishes `/sim` and the `SimTruth` field object (check with the
   NT probe or a short NT client; stop it cleanly via the sim's own exit, or just confirm startup output if a clean
   stop isn't possible without the bridge, and say so).

### Step 2: mechanisms move into FRC-2026

1. `frc.robot.sim.MechanismSim` per the table, carrying over every device ID, gear ratio, sign, sensor position and
   quirk from `bridge/.../mech/RobotMechanismSim.java` (read it and `TalonMechanismLink` first). Gains from the
   team's constants via `LinearSystemId`; estimated inertias in `SimConstants`. Expose what the bridge publishes
   (turret angle robot-relative, hood deg, flywheel RPM, intake arm deg, roller/spindex volts, climber extension)
   as getters, and `applyBallLaunched()`.
2. Battery in `RobotSim.update()`: `BatterySim` + `RoboRioSim.setVInVoltage` from the mechanism sims' currents; feed
   that voltage to each Talon sim's `setSupplyVoltage`.
3. Bridge: read mechanism state from `robot.getSim().getMechanisms()`; `BALL_LAUNCHED` calls its
   `applyBallLaunched()`; delete `bridge/.../mech/` and `FeedforwardMechanismSimTest` (and `MechanismSnapshot` if
   unused). Update the README layout block.
4. Rotor writes: use `setRawRotorPosition` (CTRE's documented pattern). If turret or intake zeroing (robot code
   `setPosition`) breaks the self-test, switch to `addRotorPosition` deltas and comment why.
5. Gate: `selfTestDriverSim` passes, with the turret, intake, flywheel, hood, climber and shot-gating steps unchanged
   in meaning.

### Step 3: heading corrections, collision rotation and wall friction

1. Protocol: correction gains `headingFieldRad`; `PoseCorrection` record, `GameCommand`/`GameLink` parsing,
   `PoseCorrector` reconciles heading like position (heading offset accumulator, angles wrapped with
   `MathUtil.angleModulus`); unit tests for heading; `C#` `SendPoseCorrection` sends it. Bump the protocol version.
2. Unity `RobotCollision`: collision rotation and wall friction as specified; `CollisionResult` carries the
   heading; `RobotModel` draws it. Constants (`CollisionTurnGain`, `MaxTurnRadPerFrame`, `WallFriction`) named in the
   class with units in the trailing comment. Extend `CollisionSelfTest` with the rotation sign check and a wall
   friction check.
3. Self-test (game attached): "angled wall hit turns truth, not gyro": place at 30 degrees facing the wall, drive in,
   check the truth heading moved toward flush and the Pigeon's yaw change is less than 1 degree.
4. Gate: `scripts/build-unity.sh linux`, the player's `--collision-selftest` passes, `selfTestDriverSimWithGame`
   passes (note the new count).

### Step 4: docs

- `CLAUDE.md`: ground rules (robot code carries `frc.robot.sim` and the `Robot` hooks; FRC-2026 code follows
  `docs/specs/frc-2026-style.md`; the bridge is only the game link); build section step counts; remove the
  `TalonMechanismLink` and outside-reset gotchas; add anything you learned.
- `README.md`: architecture diagram (truth in robot code, Unity feeds collisions back), layout, the "robot code owns
  robot motion" paragraph, Status table row for this work.
- Report: files changed per repo, gate outputs (step counts), anything you couldn't do or chose differently and why.
