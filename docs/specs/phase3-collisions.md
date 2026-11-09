# Phase 3 spec: collisions, trenches and bumps

Status: ready to implement. Read `CLAUDE.md` (project rules) and `../FRC/STYLE.md` (code style, applies to Java and
C#) first. Everything here is decided; if something in the code contradicts this spec, stop and report rather than
improvising.

## Goal

The robot can no longer drive through the field. It stops against the alliance walls, guardrails, hubs, trench
blocks and tower uprights, slides along them, passes under a trench only with its hood down, and visibly rides over
the bumps. Arcade feel, not physics: no bouncing, no damage, no robot-robot contact (there are no other robots yet).

## Key decision: resolve collisions in the Java bridge, not in Unity

The bridge already runs every 5 ms on the robot's main loop. Each update it checks the robot's footprint against a
fixed list of 2D obstacles and, if they overlap, calls `drivetrain.resetPose(...)` with the pushed-out pose.

Why not Unity: a Unity-side push-out (the existing `/DriverSim/game/poseNudge` path) arrives 30-50 ms late, so a robot
driving into a wall at 3-5 m/s visibly penetrates and jitters by 10-20 cm. In the bridge the worst-case penetration
per update is `v * 5 ms` (2.5 cm at 5 m/s), which render interpolation hides.

Keep the `poseNudge` topic and its handling as they are (unused for now; future moving obstacles may need it).

## Field geometry (field frame, meters, wpiBlue origin)

Extracted from `Assets/ThirdParty/AdvantageScope/Field2026/Field2026.glb` part bounds and cross-checked against the
robot code's `FieldConstants` (hub center x 4.61, trench center y 0.634, trench ceiling 22.25 in). All obstacles are
axis-aligned rectangles. Use the robot code's field size so the bridge and robot code agree:
`L = FieldConstants.FIELD_LENGTH_X` (16.518) and `W = FieldConstants.FIELD_LENGTH_Y` (8.043).

### Perimeter

The robot must stay inside `0 <= x <= L`, `0 <= y <= W` (alliance walls at x = 0 and x = L, guardrails at y = 0 and
y = W). Outposts, depots and the tower's wall-mounted parts sit on or behind these lines; the perimeter covers them.

### Blue-side obstacles

| Name | Kind | x range | y range | Notes |
|---|---|---|---|---|
| blue hub | SOLID | 4.02 .. 5.21 | 3.42 .. 4.62 | whole hub base |
| blue right trench block | SOLID | 4.02 .. 5.21 | 1.27 .. 1.57 | 0.5 m wall between trench lane and bump |
| blue left trench block | SOLID | 4.02 .. 5.21 | W-1.57 .. W-1.27 | y-mirror of the right one (6.473 .. 6.773) |
| blue right trench bar | LOW_CEILING | 4.54 .. 4.69 | 0.00 .. 1.27 | bar across the lane, underside 0.57 m |
| blue left trench bar | LOW_CEILING | 4.54 .. 4.69 | W-1.27 .. W | y-mirror (6.773 .. 8.043) |
| blue tower upright A | SOLID | 1.01 .. 1.10 | 3.28 .. 3.32 | rungs/braces are above robot height |
| blue tower upright B | SOLID | 1.01 .. 1.10 | 4.14 .. 4.18 | |

Not obstacles: bumps (0.17 m ramps, drivable), the tower base plate (0.06 m, drivable), depots (floor level), the
area under the tower rungs (0.66 m and up; the robot drives in under them to climb).

### Red-side obstacles

Every blue obstacle has a red twin by point reflection through the field center, the same mapping as the robot
code's `FieldConstants.redElement`: `x' = L - x`, `y' = W - y` (swap each range's min and max). Do not hand-type red
coordinates; generate them. Checked against the model: red hub = x 11.31 .. 12.50, y 3.42 .. 4.62.

### Robot footprint

A rectangle centered on the robot pose: length (robot x) = `drivetrain.constants.getRobotTotalLength()` (36.5 in,
0.927 m), width (robot y) = `drivetrain.constants.getRobotTotalWidth()` (32 in, 0.813 m), both including bumpers.
Rotated by the pose heading.

### LOW_CEILING rule (trench bar)

A LOW_CEILING obstacle blocks the robot only while the robot is "tall": hood angle above
`TRENCH_MAX_HOOD_DEG = HoodConstants.LOWEST_ANGLE_DEGREES + 4` (i.e. 12 deg). With the hood down the robot fits under
the 22.25 in bar. This is a deliberate training rule: drivers must get the hood down before entering a trench (the
robot code has `HOOD_EXCLUSION_ZONES` for the same reason). Put the threshold in one named constant with that comment.

## Collision algorithm (Java, pure, unit-tested)

Each bridge update, after `mechanisms.update(...)` and after applying game commands, before publishing:

1. `pose = drivetrain.getState().Pose`; `isTall = snapshot.hoodAngleDegrees() > TRENCH_MAX_HOOD_DEG`.
2. Up to 4 iterations: for each active obstacle (SOLID always; LOW_CEILING only when `isTall`), compute the minimum
   translation vector (MTV) between the robot's oriented rectangle and the obstacle's axis-aligned rectangle with the
   separating axis theorem (axes: field x, field y, robot x, robot y). If they overlap, move the robot center by the
   MTV (pointing from the obstacle toward the robot). Then clamp to the perimeter: for each wall, project the
   rectangle onto the wall normal and push it back inside. Stop early when an iteration moves nothing.
3. Heading never changes. If the total correction exceeds 1 mm, `drivetrain.resetPose(correctedPose)`.
4. Result record: corrected pose, `didCollide` (any correction this update), `isTrenchBlocked` (a LOW_CEILING
   obstacle produced a correction this update).

Penetration depths below 1 mm are ignored so the robot resting against a wall doesn't reset its pose every update.

## Code layout

New Java package `driversim.field` in `bridge/src/main/java/driversim/field/`:

- `FieldRectangle` record: `minXMeters, maxXMeters, minYMeters, maxYMeters`, with `reflectThroughCenter(L, W)` and
  `mirrorAcrossWidth(W)` helpers returning new rectangles.
- `Obstacle` record: `name`, `FieldRectangle boundsField`, `Kind kind` (enum `SOLID`, `LOW_CEILING`).
- `FieldObstacles`: builds the immutable obstacle list (blue table above, left trench parts by y-mirror, red by
  reflection) from `L` and `W` passed in; constants for every blue coordinate with comments naming the field part.
- `CollisionSolver`: pure. Constructed with obstacles, `L`, `W`, robot length/width. Method
  `CollisionResult resolve(Pose2d poseField, boolean isTall)`. No WPILib HAL calls (so it unit-tests without JNI).
- `CollisionResult` record: `Pose2d poseField, boolean didCollide, boolean isTrenchBlocked`.

Bridge (`DriverSimBridge`): build the solver in the constructor from `robot.drivetrain.constants`; run it in
`update()` as above; publish the two flags.

## Protocol change (bump `GameLink.PROTOCOL_VERSION` to 2)

Append to `/DriverSim/robot/state` (Java `RobotStateLayout` and C# `RobotStateLayout` in `Protocol.cs`, kept in sync):

| Index | Name | Meaning |
|---|---|---|
| 18 | `IS_COLLIDING` / `IsColliding` | 1 when the bridge pushed the robot out of something this update |
| 19 | `IS_TRENCH_BLOCKED` / `IsTrenchBlocked` | 1 when a trench bar blocked the robot because its hood was up |
| 20 | `LENGTH` | |

C# `Protocol.Version = 2`; `RobotState` gains `IsColliding` and `IsTrenchBlocked` (flags come from the later state in
`Interpolate`). Update `NtProbe` to print both flags. Update the README protocol table.

## Unity changes

1. **Bumps lift and tilt the robot (visual only).** In `SceneBuilder`, add a `MeshCollider` to every mesh under
   field-model nodes whose names start with `GE-26100` (the four bumps), in edit mode so Unity cooks them at build
   time. Nothing else in the scene has colliders (robot parts and the arena floor already remove theirs). In
   `RobotModel.LateUpdate`, raycast straight down from 1 m above each of the four wheel contact points (wheel
   positions already in `BuildRobot`, transformed to world after the chassis pose is applied); a hit gives that
   wheel's ground height, a miss means 0. Raise the body by the average height and pitch/roll it from the front-back
   and left-right height differences over the wheelbase/track. Verify the tilt direction in a screenshot (the robot
   should nose up climbing the near side of a bump). If the mesh colliders can't be built (e.g. mesh not readable),
   fall back to an analytic profile from the bump bounds and say so in the report.
2. **HUD.** When `IsTrenchBlocked`, show a centered warning "HOOD UP - LOWER IT FOR THE TRENCH" under the clock.
   Add both flags to the F1 details panel.
3. No Unity-side collision response; the bridge owns it.

## Tests and acceptance

All of these must pass; report their actual output.

1. **Unit tests** (`bridge/src/test/java/driversim/field/`), at minimum:
   - robot clear of everything: unchanged pose, `didCollide` false;
   - robot overlapping the blue hub from the -x side: pushed to `x = 4.02 - length/2` (within 1 mm), y unchanged;
   - robot at 45 deg overlapping the hub corner: no overlap remains afterwards;
   - robot past the x = 0 wall: pushed so its rearmost corner is at x = 0;
   - robot in a corner overlapping two walls: pushed out of both;
   - trench bar: overlap ignored when not tall, resolved and `isTrenchBlocked` true when tall;
   - red obstacles are exact point reflections of blue ones (check the hub and one trench block);
   - sub-millimeter overlap: pose unchanged, `didCollide` false.
2. **Self-test** (`SelfTest.java`), new steps after the existing ones, driving with the left stick exactly as the
   existing drive step does (blue alliance, field-centric: stick forward = +x). Place the robot with
   `drivetrain.resetPose` first. Each step releases the stick at the end.
   - "wall stops robot": start (1.2, 2.0, 0 deg), stick back (+1 on left Y) 2 s; pass if x >= length/2 - 0.03.
   - "hub blocks robot": start (3.2, 4.02, 0), stick forward 2 s; pass if x <= 4.02 - length/2 + 0.03.
   - "trench passable with hood down": start (3.3, 0.634, 0), forward 2.5 s; pass if x > 5.5.
   - "raised hood blocks trench": hood held at 30 deg (as in the hood step), start (3.3, 0.634, 0), forward 2 s;
     pass if x <= 4.54 - length/2 + 0.03 and the bridge's last result reported `isTrenchBlocked`.
   - "bump passable": start (3.3, 2.4, 0), forward 2.5 s; pass if x > 5.5.
   The final line must read `[Driver-Sim self-test] 16/16 steps passed`.
3. **Screenshots** with `scripts/capture-screenshots.sh` (Unity rebuilt first with `scripts/build-unity.sh`):
   robot placed overlapping the blue hub (it should appear pushed flush against the hub face), robot on a bump
   (visibly raised and tilted), robot in a trench lane with the hood down. Look at each PNG before keeping it; copy
   good ones to `docs/screenshots/` as the next numbers (`11-phase3-...png`, ...) with a line each in
   `docs/screenshots/README.md`. Never overwrite or delete existing screenshots.
4. `./gradlew testDriverSim selfTestDriverSim` from FRC-2026 is green; Java formatted with clang-format; Unity build
   has no `warning CS` in DriverSim code.

## Constraints

- Follow `../FRC/STYLE.md` in all code (see the C# mapping in CLAUDE.md). ASCII-only source.
- No changes to FRC-2026 (the robot code). Everything lives in Driver-Sim.
- Keep `FieldFrame.cs` as the only field-to-Unity conversion; keep the Java obstacle table as the only obstacle source.
- Stop sims cleanly (the game's quit / `robot.endCompetition()`); never kill them with signals. Run one sim at a time.
- Update README (status table: phase 3 done, protocol table) and CLAUDE.md gotchas if you learn something non-obvious.
- Do not commit. Leave changes in the working tree for review.

## Out of scope

Fuel, intake/shooting gameplay, scoring (phase 4); other robots; climbing physics; robot-height effects other than the
trench bar; changing drive speed on bumps.
