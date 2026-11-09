# Phase 3 spec: collisions, trenches and bumps (Unity is the world)

Status: ready to implement. Read `CLAUDE.md` (project rules) and `../FRC/STYLE.md` (code style, applies to Java and
C#; C# mapping in CLAUDE.md) first. Everything here is decided; if something in the code contradicts this spec or a
step turns out impossible, stop and report rather than improvising.

## Goal

The robot can no longer drive through the field. On screen it stops flush against the alliance walls, guardrails,
hubs, trench blocks and tower uprights and slides along them; it passes under a trench bar only with its hood down;
it visibly rides over the bumps. The robot code's own pose (odometry) catches up with what the game shows within about
one network round trip. Arcade feel, not physics: no bouncing, no damage, no speed change.

## Architecture: Unity owns the world, the robot code syncs to it

Think of it like an online game. Unity holds the authoritative world and predicts locally; NetworkTables carries
corrections back to the robot code, which converges.

- The robot code (CTRE swerve sim in Java) still integrates robot motion from the driver's controller. Its pose
  arrives in Unity ~30-50 ms late through `/DriverSim/robot/state`.
- Every frame Unity takes that pose (the interpolated `RobotState` it already renders), resolves it against Unity
  colliders, and **renders the constrained pose immediately**, so the robot never visibly sinks into anything.
- When the constrained pose differs from the reported one, Unity sends a **pose correction**: the absolute
  constrained field position plus the robot-clock timestamp of the state it came from.
- The bridge keeps a short history of its poses and of the corrections it has applied, and applies each correction
  so the robot code's pose matches what Unity decided **at that timestamp**, without double-counting corrections
  that are still in flight (algorithm below).
- Obstacles are Unity colliders. Anything with a collider and a `FieldObstacle` component takes part, so moving
  obstacles (fuel, other robots) can join later without protocol changes.
- Accepted cost: while the robot pushes against something, the robot code's pose can sit up to `speed x round trip`
  (about 10-20 cm at full speed) inside it for a moment. Fine for a trainer.

The relative `/DriverSim/game/poseNudge` topic is removed (its relative deltas double-count while corrections are in
flight); `poseCorrection` replaces it.

## Field geometry (field frame, meters, wpiBlue origin)

Extracted from `Assets/ThirdParty/AdvantageScope/Field2026/Field2026.glb` part bounds and cross-checked against the
robot code's `FieldConstants` (hub center x 4.61, trench center y 0.634, trench ceiling 22.25 in). All obstacles are
axis-aligned rectangles in the field frame. `L = 16.518`, `W = 8.043` (already `FieldFrame.FieldLengthM/WidthM`).

### Perimeter

The robot stays inside `0 <= x <= L`, `0 <= y <= W` (alliance walls at x = 0 and x = L, guardrails at y = 0 and
y = W). Outposts, depots and the tower's wall-mounted parts sit on or behind these lines; the perimeter covers them.
Build the perimeter as four solid boxes 1 m thick lying just outside these lines, long enough to overlap at corners.

### Blue-side obstacles

| Name | Kind | x range | y range | Notes |
|---|---|---|---|---|
| blue hub | Solid | 4.02 .. 5.21 | 3.42 .. 4.62 | whole hub base |
| blue right trench block | Solid | 4.02 .. 5.21 | 1.27 .. 1.57 | 0.5 m wall between trench lane and bump |
| blue left trench block | Solid | 4.02 .. 5.21 | W-1.57 .. W-1.27 | y-mirror of the right one (6.473 .. 6.773) |
| blue right trench bar | LowCeiling | 4.54 .. 4.69 | 0.00 .. 1.27 | bar across the lane, underside 0.57 m |
| blue left trench bar | LowCeiling | 4.54 .. 4.69 | W-1.27 .. W | y-mirror (6.773 .. 8.043) |
| blue tower upright A | Solid | 1.01 .. 1.10 | 3.28 .. 3.32 | rungs/braces are above robot height |
| blue tower upright B | Solid | 1.01 .. 1.10 | 4.14 .. 4.18 | |

Not obstacles: bumps (0.17 m ramps, drivable), the tower base plate (0.06 m, drivable), depots (floor level), the
area under the tower rungs (0.66 m and up; the robot drives in under them to climb).

### Red-side obstacles

Every blue obstacle has a red twin by point reflection through the field center (the robot code's
`FieldConstants.redElement` mapping): `x' = L - x`, `y' = W - y` (swap each range's min and max). Generate them in
code; never hand-type red coordinates. Checked against the model: red hub = x 11.31 .. 12.50, y 3.42 .. 4.62.

### Robot footprint

Rectangle centered on the robot pose: 0.927 m along robot x (36.5 in) by 0.813 m along robot y (32 in), bumpers
included (from `CompbotConstants.getRobotTotalLength/Width`). Name both as constants with that provenance.

### LowCeiling rule (trench bar)

A LowCeiling obstacle blocks the robot only while the robot is "tall": hood angle above
`TrenchMaxHoodDeg = 12` (the robot code's `HoodConstants.LOWEST_ANGLE_DEGREES` of 8, plus 4). With the hood down the
robot fits under the 22.25 in bar. This is a deliberate training rule (the robot code's `HOOD_EXCLUSION_ZONES` exist
for the same reason). One named constant, with that comment.

## Unity work

### Obstacles as colliders

- New C# static class `FieldObstacleLayout` (Field folder): the blue table above as named constants, plus
  `GetAll()` returning every obstacle (blue, y-mirrored left trench parts, red by reflection, and the four perimeter
  boxes) as `readonly struct ObstacleSpec { string Name; float MinXFieldM, MaxXFieldM, MinYFieldM, MaxYFieldM;
  ObstacleKind Kind }`. This table is the only obstacle source.
- New component `FieldObstacle` with an `ObstacleKind` (`Solid`, `LowCeiling`) and a getter.
- `SceneBuilder` creates one child GameObject per obstacle under a `FieldObstacles` root: `BoxCollider` sized from the
  spec (height 0.6 m, bottom at the carpet), positioned with `FieldFrame.ConvertFieldToWorld`, plus a `FieldObstacle`
  with its kind. No renderer by default.
- Debug view: F2 (and command-line `--show-colliders`) toggles translucent boxes drawn over every obstacle collider
  (cyan Solid, yellow LowCeiling), so collider placement can be checked against the field model in screenshots. Add
  F2 to the key help line.

### Robot collision and prediction

- New component `RobotCollision` (Robot folder), run before `RobotModel` each frame (set its script execution order,
  or have `RobotModel` call it). Input: the link's interpolated `RobotState`. Output: the constrained pose that
  `RobotModel` renders (`RobotModel` uses it instead of the raw state's x/y; heading is never changed).
- Algorithm per frame:
  1. Start at the reported pose. `isTall = HoodDeg > TrenchMaxHoodDeg`.
  2. Up to 4 iterations: `Physics.OverlapBox` with the robot footprint box (height 0.5 m, at the candidate pose) to
     find colliders; ignore any without `FieldObstacle`, and LowCeiling ones unless `isTall`. For each,
     `Physics.ComputePenetration` (robot box vs obstacle collider), drop the vertical component, and move the
     candidate by direction x distance. Stop early when nothing moved. Ignore penetrations under 1 mm.
  3. Use a dedicated, collider-less query (box parameters, not a collider on the drawn robot) or a `BoxCollider` on
     an invisible proxy object; either way the robot must never collide with itself. Call `Physics.SyncTransforms()`
     before querying if obstacles moved this frame (they don't yet).
  4. Expose: constrained pose, `IsColliding()` (any correction this frame), `IsTrenchBlocked()` (a LowCeiling
     obstacle moved the robot this frame).
- When the correction is at least 1 mm, send it: `link.SendPoseCorrection(state.TimestampS, constrainedXFieldM,
  constrainedYFieldM)`. Do not send anything while not colliding.

### Bumps (visual only)

- `SceneBuilder` adds a `MeshCollider` (plus a marker component `BumpSurface`) to every mesh under field-model nodes
  whose names start with `GE-26100` (the four bumps), in edit mode so Unity cooks them at build time. Obstacle queries
  must ignore them (they have no `FieldObstacle`).
- `RobotModel.LateUpdate`, after placing the chassis at the constrained pose: raycast straight down from 1 m above
  each of the four wheel contact points (wheel positions already in `BuildRobot`, transformed to world), keeping only
  hits on colliders with `BumpSurface`. A miss means height 0. Raise the body by the average height and pitch/roll it
  from the front-back and left-right differences over the wheelbase/track. Check the direction in a screenshot: the
  robot noses up while climbing the near side of a bump.
- If the bump mesh colliders can't be built (e.g. a non-readable mesh error), fall back to an analytic profile from
  the bump bounds (field x 4.05 .. 5.18 and red twins, peak 0.17 m at the center line) and say so in the report.
- Visual only: the robot code is not told about bumps; no speed change.

### HUD

- When `IsTrenchBlocked()`, show a centered warning "HOOD UP - LOWER IT FOR THE TRENCH" under the clock.
- Add collision state (colliding / trench blocked) and the latest correction sent to the F1 details panel.

### Game hello

On connecting (and on every reconnect), the game sends the command `hello`. The bridge uses it to know a game is
attached (needed by the end-to-end self-test).

### Collision self-check (in the game, no robot sim needed)

New component `CollisionSelfTest`, active only with the command-line flag `--collision-selftest`: it feeds synthetic
poses straight into `RobotCollision` (no network), logs one `[Driver-Sim collision-selftest] PASS|FAIL <name>` line
per case to the player log, then quits with exit code 0 when all passed, 1 otherwise. Cases:
- clear of everything: unchanged, not colliding;
- overlapping the blue hub from the -x side: x ends at `4.02 - 0.927/2` (within 2 mm), y unchanged;
- 45 deg overlapping the hub corner: no overlap remains;
- past the x = 0 wall: pushed fully inside;
- in a corner overlapping two walls: pushed out of both;
- inside the blue right trench bar footprint with hood 8 deg: unchanged; with hood 30 deg: pushed out and
  `IsTrenchBlocked()` true;
- overlapping the red hub mirror case: pushed to `12.50 - ...` mirrored (proves the reflection).

## Bridge (Java) work

### Protocol (bump `GameLink.PROTOCOL_VERSION` and C# `Protocol.Version` to 2)

- Remove `/DriverSim/game/poseNudge`, `PoseNudge`, `GameLink.takePoseNudge` and the bridge's nudge handling; remove
  the C# `SendPoseNudge` and `PoseNudgeTopic`.
- Add `/DriverSim/game/poseCorrection` (double[3], game to bridge, send-all, keep-duplicates, queued like the old
  nudge topic): `{robotTimestampS, xFieldM, yFieldM}` = where the robot should have been at that robot-clock time.
  C# `DriverSimLink.SendPoseCorrection(timestampS, xFieldM, yFieldM)`.
- Add the `hello` verb to `GameCommand` (no arguments).
- No change to the robot-state array layout.
- Update the README protocol table and NtProbe (it may print the last correction applied; optional).

### Pose reconciliation (pure Java, unit-tested): `driversim.game.PoseCorrector`

Carries its own history (say so in its Javadoc). State: a ring buffer of the last 2 s of
`(robotTimestampS, poseField, offsetField)` samples recorded once per bridge update, where `offsetField` is the
cumulative translation the bridge has applied through corrections so far; the current offset; the timestamp of the
last correction applied; the time of the last pose discontinuity.

- `record(timestampS, poseField)`: append a sample with the current offset. If the pose jumped further than
  `MAX_PLAUSIBLE_SPEED_MPS (8) x dt` since the previous sample, treat it as an outside reset (robot code, a
  `resetPose` command, an auto starting) and remember its time.
- `computeCorrection(PoseCorrection correction)` -> the translation to apply now (possibly zero):
  1. Ignore it if its timestamp is not newer than the last applied correction's, older than the last discontinuity,
     or outside the history.
  2. Interpolate the history at the correction's timestamp: `poseThen`, `offsetThen`. The raw (never-corrected)
     position then was `rawThen = poseThen - offsetThen`.
  3. The offset Unity wants is `neededOffset = correctedThen - rawThen`. The translation to apply now is
     `neededOffset - currentOffset`; drop it if under 1 mm.
  4. On a non-zero result, set `currentOffset = neededOffset` and remember the timestamp.
  Setting the offset absolutely (not adding deltas) is what prevents double counting: later corrections computed from
  older, still-uncorrected poses yield the same needed offset plus only the robot's additional push.
- Bridge `update()`: after commands, `record(now, pose)`; take every queued correction (newest last); for each
  non-zero result, `resetPose(currentPose + translation)` (heading unchanged). Also record discontinuities caused by
  the bridge's own `resetPose` command handling.

Unit tests (`bridge/src/test/java/driversim/game/PoseCorrectorTest.java`), at minimum:
- a single correction moves the pose to the corrected position;
- a robot pushing steadily into a wall with corrections arriving 40 ms late converges to the wall without
  overshooting (simulate: raw x decreases at 3 m/s; each correction says "x >= 0.4635" for a state 40 ms old);
- a stale correction (timestamp older than the last applied) is ignored;
- a correction from before an outside reset is ignored;
- sub-millimeter corrections are ignored.

### Self-test

- Plain `./gradlew selfTestDriverSim` (no game) stays 11 steps and must still pass.
- New Gradle task `selfTestDriverSimWithGame`: same headless robot sim (no sim GUI), plus it launches the built game
  with `--stop-sim-on-exit` (reuse the `simulateDriverSim` launch code and the `stopDriverSimGame` finalizer), and sets
  `DRIVERSIM_SELFTEST=with-game`. In that mode, after the existing 11 steps, `SelfTest` waits up to 30 s for the
  game's `hello` (fail the step if it never comes), then runs, driving with the left stick exactly as the existing
  drive step does (blue alliance, field-centric: stick forward = +x), placing the robot with `drivetrain.resetPose`
  first and releasing the stick at the end of each:
  - "wall stops robot": start (1.2, 2.0, 0 deg), stick back (+1 on left Y) 2 s, release, settle 0.5 s; pass if
    x >= 0.927/2 - 0.01.
  - "hub blocks robot": start (3.2, 4.02, 0), forward 2 s, release, settle 0.5 s; pass if x <= 4.02 - 0.927/2 + 0.01.
  - "trench passable with hood down": start (3.3, 0.634, 0), forward 2.5 s; pass if x > 5.5.
  - "raised hood blocks trench": hood held at 30 deg (as in the hood step), start (3.3, 0.634, 0), forward 2 s,
    release, settle 0.5 s; pass if x <= 4.54 - 0.927/2 + 0.01.
  - "bump passable": start (3.3, 2.4, 0), forward 2.5 s; pass if x > 5.5.
  The run must end with `[Driver-Sim self-test] 17/17 steps passed` (11 + hello + 5), exit cleanly, and leave no
  game process behind.

## Acceptance (report actual output for each)

1. `./gradlew testDriverSim` green (including the new PoseCorrector tests).
2. `./gradlew selfTestDriverSim` -> 11/11.
3. `scripts/build-unity.sh linux` succeeds with no `warning CS` in DriverSim code.
4. `unity/Builds/Linux/DriverSim.x86_64 --collision-selftest` exits 0 with every case PASS in the player log.
5. `./gradlew selfTestDriverSimWithGame` -> 17/17, clean exit, no leftover processes, no new core/hs_err files.
6. Screenshots via `scripts/capture-screenshots.sh` (read each PNG before keeping it), copied to `docs/screenshots/`
   as the next numbers (`11-phase3-...`) with a line each in `docs/screenshots/README.md`:
   - `--show-colliders` overhead view (`--view overhead`): collider boxes line up with the hubs, trench blocks/bars,
     tower uprights and walls on both sides;
   - robot placed overlapping the blue hub (`resetPose` into it): drawn flush against the hub face, and the F1 panel
     shows the bridge pose has converged to the same x;
   - robot on a bump: visibly raised and tilted the right way;
   - robot in a trench lane with the hood down.
7. Java formatted with clang-format; README (status table: phase 3 done; protocol table) and CLAUDE.md (gotchas you
   learned) updated.

## Constraints

- Follow `../FRC/STYLE.md` in all code (C# mapping in CLAUDE.md). ASCII-only source.
- No changes to FRC-2026 (the robot code). Everything lives in Driver-Sim.
- `FieldFrame.cs` stays the only field-to-Unity conversion; `FieldObstacleLayout` is the only obstacle source.
- Stop sims cleanly (game quit / `robot.endCompetition()`); never kill them with signals. One sim at a time.
- Do not commit. Leave changes in the working tree for review.

## Out of scope

Fuel, intake/shooting gameplay, scoring (phase 4); other robots; climbing physics; bump effects on the robot code
(speed, gyro pitch); changing drive speed anywhere.
