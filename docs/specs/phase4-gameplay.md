# Phase 4 spec: gameplay (fuel, intake, shooting, scoring, HUD)

Status: approved. Read `CLAUDE.md` (project rules) and `../FRC/STYLE.md` (C# mapping in CLAUDE.md) first. If
the code contradicts this spec or a step turns out impossible, stop and report rather than improvising.

## Goal

The driver can pick up fuel, shoot it with the real robot code's aiming, and score. It plays like a game, not a
simulator: simple, readable rules with clear feedback. Physics only where it makes the robot feel present (the robot
shoves balls around); everything else is scripted.

## Game rules and simplifications (decided)

- **Fuel** is the AdvantageScope `Fuel.glb` ball (0.15 m). At the start of free practice and of each match, fuel
  spawns at the field model's staged positions (`FieldSetup.GetStagedFuelPositions()`, 456 balls). The robot starts
  each match and practice with **8 preloaded** fuel.
- **Hopper** holds **20**. The count lives in the game; the robot code has no ball sensors and never learns it.
- **Intake**: a ball touching the intake zone (a box across the front bumper, the frame's width, 0.25 m deep, 0.2 m
  tall) is taken into the hopper when the arm is down (robot-code arm angle below 40 deg) **and** the roller pulls in
  (roller volts above +2). Full hopper: the ball is not taken and just gets pushed.
- **Shooting**: while the spindex feeds (feed volts below -1), the hopper has fuel and the flywheel is above 500 RPM, one
  ball leaves the turret every 0.125 s (8 balls/s). Each launch sends the existing `ballLaunched` command (the robot
  code's flywheel loses speed). Below 500 RPM nothing launches (jammed).
- **Shot model (honest to RPM, hood and turret)**: exit speed = `rpm x ExitMpsPerRpm`; elevation =
  `90 deg - (hoodDeg + HoodElevationOffsetDeg)` (a low hood is a steep shot, as the robot's table implies); direction =
  robot heading + turret angle; the robot's field velocity is added. Launch point = the drawn turret, 0.5 m up. The two
  constants are fitted once so that every row of the robot code's own shot table (`LaunchCalculator.scoreSpeedMap` /
  `scoreHoodMap`, 56..216 in) lands in the hub opening from that distance; the fitted values and the table copy used to
  check them live in the game with a comment naming the robot-code source.
- **Flight is scripted**, not physics: a ballistic arc (gravity, no drag), moved every frame. A flying ball:
  - **scores** when it crosses the hub opening height (1.83 m) going down within `OpeningRadiusM + AssistMarginM`
    (0.53 + 0.15 m) of the hub center (the margin is the light aim assist; the last 0.2 s of a scoring arc is bent
    toward the center so it visibly drops in);
  - **bounces off the hub** if it enters the hub's body (its footprint below 1.83 m): becomes a physics ball there with
    its velocity reflected and scaled by 0.3;
  - **lands** when it drops below 0.6 m anywhere else: becomes a physics ball with its current velocity.
- **Scored fuel returns**: 1.5 s after scoring, the ball reappears at the hub's exit facing the field center and rolls
  out with a small random spread (REBUILT hubs return fuel to the neutral zone), so fuel never runs out.
- **Field fuel physics** (the only physics): Rigidbody spheres with a sphere collider, high linear damping so they
  settle quickly, a speed cap, ball-ball collisions on. They collide with the carpet, solid obstacles, the hub, bump
  surfaces and the robot; they ignore LowCeiling obstacles (they roll under trench bars) and the robot's collision proxy.
- **The robot pushes fuel**: a kinematic box collider the size of the bumpers follows the drawn (constrained) robot
  pose in `FixedUpdate` via `Rigidbody.MovePosition` / `MoveRotation`. Fuel never pushes the robot.
- **Scoring**:
  - **Free practice**: no timer. The HUD counts fuel scored in **our own alliance's hub** (from the selected station);
    the other hub's fuel isn't counted.
  - **Match**: our score = fuel into our hub while it is active (1 point each); fuel into an inactive hub counts
    nothing and the HUD flashes "HUB INACTIVE". Fuel into the other alliance's hub counts nothing.
  - **Autonomous winner**: at the end of autonomous the game compares our autonomous points with a virtual opponent's
    score drawn per match (random 4..12, shown on the HUD) and sends `autoWinner B|R` (ties go to the opponent). This
    drives the existing hub-shift schedule.
- **Resets**: F6 (reset robot) moves only the robot. Restart Match and starting practice respawn all fuel, clear the
  scores and refill the preload. New hotkey F7: respawn fuel (practice only).

## HUD (top-center, under the match clock)

- `FUEL 12/20` (hopper), always.
- Free practice: `OUR HUB 37`. Match: `SCORE 37` and, during and after autonomous, `AUTO 9 vs 7 (WON)`.
- A `+1` that pops and fades near the clock on each score; `HUB INACTIVE` flash on a shot into an inactive own hub.
- End of match: `FINAL 52` stays on screen until the next match or practice.

Keep the existing corner details panel; add one line there: `fuel: field 448  flying 3  hopper 12`.

## Protocol

- New topic `/DriverSim/game/state` (game to bridge, `double[]`): `[protocolVersion, hopperCount, ourScore,
  autoScore, opponentAutoScore, fieldFuelCount]`. Layout class on both sides (`GameStateLayout.java` / C# mirror), bump
  `GameLink.PROTOCOL_VERSION`. The bridge only reads it (for the self-test and the NT probe).
- New command `spawnFuel <x> <y>` (field frame, meters): places one ball on the carpet. For the self-test and for
  debugging; also bound to F8 in a development build only (a ball 1 m in front of the robot).

## Code layout (Unity, `Assets/DriverSim/Scripts/`)

- `Game/FuelField.cs`: owns all fuel: spawning, the physics balls (pooled), flying balls, the return-from-hub queue,
  counts. One component.
- `Game/FuelBall.cs`: tiny component on each ball (state: resting/physics, flying, scored-hidden).
- `Game/RobotFuel.cs`: hopper count, the intake zone, feeding and launching; reads `RobotState` voltages,
  angles and RPM; sends `ballLaunched`.
- `Game/ShotModel.cs`: static pure helpers: launch velocity from RPM/hood/turret/heading, the arc, the scoring and hub
  tests, and the fitted constants plus the robot-code table copy.
- `Game/Scoreboard.cs`: free-practice and match scores, autonomous winner, HUD values; listens to match phase.
- `Robot/RobotPusher.cs`: the kinematic bumper box that pushes fuel.
- `SceneBuilder` wires them; layers: `Fuel`, `RobotPusher` (set up in the scene builder, with the collision matrix).
- `Robot/GameplaySelfTest.cs`: `--gameplay-selftest` like `--collision-selftest`, no robot sim: every robot-code
  table row scores from its distance; a ferry shot (45 deg, 4000 RPM) from 3 m does not score; intake takes a ball only
  with arm down and roller in; a full hopper refuses; a shot into the hub body bounces; scored fuel returns.

## Bridge

- `GameLink` subscribes to `/DriverSim/game/state`; `DriverSimBridge` exposes the latest values to the self-test;
  `NtProbe` prints them.
- `GameCommand` gains `SPAWN_FUEL`; the bridge forwards nothing (the command is for the game) - it only needs to
  accept it in probe scripts. (The probe sends `spawnFuel` straight to the game's command topic.)

## Self-test (with game) additions

1. "intake picks up fuel": place the robot at a known spot facing +x, `spawnFuel` 0.8 m ahead, deploy the intake
   (D-pad down), hold the right bumper (roller in), drive forward 1 s: hopper count rises by 1.
2. "shot scores in our hub": place the robot 2.5 m from the blue hub facing it, hold B (the robot code's dynamic
   launch aims turret, hood and flywheel): our score rises within 4 s.
3. "free practice counts only our hub": a `spawnFuel` dropped straight into the red hub opening (or a scripted flying
   ball) does not change the blue station's score.

## Out of scope

Other robots, outtake (reversing the roller doesn't eject fuel), fuel jams inside the robot, climbing points, penalties, the outpost and depot as fuel sources, fuel
drawn inside the hopper (the HUD count is enough), sound.

## Verification

- `scripts/build-unity.sh linux`; `--collision-selftest` and `--gameplay-selftest` pass.
- `./gradlew selfTestDriverSim` (unchanged count) and `selfTestDriverSimWithGame` (23 + 3).
- Screenshots (new numbers in `docs/screenshots/`): fuel on the field at practice start; a shot in flight; the HUD with
  hopper and score; a ball rolling out of the hub after a score.
