# Fieldrunner — notes for agents

Read README.md first: architecture, tasks, protocol. Phase specs live in `docs/specs/`; implement against them.

## Ground rules

- **Robot code stays unmodified.** The changes in `../FRC-2026` (branch `feature/driver-sim`) are the guarded
  `apply from` line at the end of `build.gradle` and one sim-only constant the user approved: `DRIVE_INERTIA` in
  `CompbotTunerConstants` (0.0367 kg m^2 = robot mass x wheel radius^2 / 4, so the CTRE swerve sim accelerates like the
  real robot; Phoenix uses it only in simulation). Anything else sim-specific lives here, in `bridge/` or `gradle/`.
- **All code follows the team style guide, `../FRC/STYLE.md`** (FRC 5735 Control Freaks). Read it before writing
  code here. Java follows it as written and is formatted with `scripts/format.java.sh` (clang-format, the team's
  `.clang-format`, BlockIndent / 120 columns). C# (Unity) and other code follow the same rules, mapped as:
  - no ternaries or value-computing `&&`/`||`; assign a default, then override in an `if`;
  - no composed calls `f(g(x))`, no LINQ/streams: name each intermediate;
  - methods are verbs (`Get`/`Set`/`Is`/`Has`/`Compute`/`Make`/`Update`/`Convert`/`Check`...); class state is read
    with `GetX()`/`IsX()` methods, not public fields; Unity inspector references are `[SerializeField] private` with
    `SetX(...)` setters;
  - structured results are records: Java `record`, C# `readonly struct` with get-only properties set by its
    constructor;
  - units and frames in names (`headingFieldRad`, `eyeHeightM`, `turretRobotRelRad`); durations in seconds;
  - mini blocks with a short comment each; doc comments (Javadoc / C# XML) lead with meaning, one sentence per
    parameter, units and frames stated; no history in comments;
  - class layout: fields (trailing comments), constructor / Unity messages, public API, private stateful helpers,
    then private static pure helpers.
  Keep source files ASCII-only: the build machine's default encoding is US-ASCII and javac rejects characters like
  `²` or `θ`.
- **The bridge runs on the robot's main loop thread** (`TimedRobot.addPeriodic`); never start threads that touch
  robot objects.
- **Protocol changes touch both sides**: `game/RobotStateLayout.java` / `MatchStateLayout.java` and their C# mirrors
  in the Unity project, and bump `GameLink.PROTOCOL_VERSION`.

## Build and verify (from ../FRC-2026)

```bash
export JAVA_HOME=~/.local/jdk-17 PATH=~/.local/jdk-17/bin:$PATH   # this machine has no system JDK
./gradlew testDriverSim          # unit tests
./gradlew selfTestDriverSim      # headless end-to-end check against the real robot code; must print 12/12
                                 # also prints [Driver-Sim drive-feel] lines (time to speed, accel, stopping)
./gradlew selfTestDriverSimWithGame   # same plus the built Unity game (needs scripts/build-unity.sh linux); must print 18/18
```

Redirect Gradle output to a file and grep it; sim runs are chatty. `simulateDriverSim` opens the game window on the
user's display (the sim GUI only with `-PdriverSimGui`); `probeDriverSim` (see README) checks a running sim from the
command line.

## Input owner and the robot controls table

The bridge is the driver station and has one input owner (`driversim.InputOwner`, from `DRIVERSIM_INPUT`, set by
`gradle/driversim.gradle`): `game` (default `simulateDriverSim`: the game's controller/keyboard becomes joystick port 0
and the game's menus enable, pause and disable the robot), `gui` (`-PdriverSimGui`: the WPILib sim GUI supplies
joysticks and enable, the game's joystick and `practice`/`disable`/`pause`/`resume` are ignored) and `selftest`
(self-tests; the game's joystick topic counts only in the self-test's one joystick step). Code that writes the driver
station must respect the owner. The game sends `game/joystick0` only when `bridge/info` says `input=game`.

`unity/DriverSim/Assets/DriverSim/Scripts/Game/RobotControlsLayout.cs` is a table mirroring
`Robot.setupDriverBindings()` and the default drive command in `FRC-2026/.../Robot.java`; it feeds the Controls screen
and generates `DriverControls.inputactions`. **When the robot code's driver bindings change, update the table** (and
the README's controls table).

## Gotchas

- The self-test's step actions run via `asProxy()`: a test command that required subsystems would be cancelled when a
  button it presses schedules a robot command needing the same subsystems.
- The robot code's launch command forces feeding after 1 s (`timeOut > 1`); the self-test checks the
  `launchCalc/*Check` values before that, so gating is verified on its own merits.
- `TalonMechanismLink` works in the mechanism frame: sign flips for `Clockwise_Positive` devices happen there, and
  rotor motion is written as deltas so the robot code's `setPosition` calls (zeroing) keep working.

## Unity game

- Editor 6000.6.8f1; Unity's CLI is `~/.local/bin/unity` (signed in). Build with `scripts/build-unity.sh` (batch
  mode; fails if the project is open in an editor). Verify visually with `scripts/capture-screenshots.sh` and read
  the PNGs; game logs go to `~/.config/unity3d/FRC 5735/Fieldrunner/Player.log`.
- The scene is generated by `Assets/DriverSim/Editor/SceneBuilder.cs`; never hand-edit `DriverSim.unity` (nor
  `DriverControls.inputactions`, which `DriverControlsBuilder` regenerates from `RobotControlsLayout`). The menus are UI
  Toolkit: layout `Assets/DriverSim/UI/DriverSim.uxml`, style `DriverSim.uss`; `GameFlow` is the state machine and
  `MenuView` only shows screens. `scripts/capture-screenshots.sh` runs the game with `--start-in practice` unless the
  game args say otherwise; `--screenshot-menu title|station|controls|options|pause|disconnected|toast` opens a screen.
- Field model node names are `GE-26900: Fuel` etc. (AdvantageScope's Config.json shows sanitized names).
- `Field/FieldFrame.cs` holds the only field-to-Unity conversion; robot code uses the AndyMark 2026 layout size.
- The model's driver station acrylic (`FE-00012`) is 20%-opaque gray; the camera looks through it, so FieldSetup hides
  the panels on the camera's wall. That was the "haze" in early screenshots (not a color-space bug; Linear is fine).
- This machine is a Docker container with the NVIDIA driver's graphics libraries (`NVIDIA_DRIVER_CAPABILITIES=all`): the
  game renders on the RTX 4090 with OpenGL Core. It has no NVIDIA Vulkan ICD manifest, so Vulkan sees only llvmpipe,
  which Unity rejects (falls back to GL); with `VK_DRIVER_FILES` pointing at a manifest for `libGLX_nvidia.so.0` the
  game runs on Vulkan. `/dev/dri` isn't accessible (Intel GPU unusable). Check the player log's `[Driver-Sim] graphics:`
  line.
- `BuildPlayers` sets each platform's graphics API list (it writes them into ProjectSettings.asset) and builds the Mac
  app universal; `mac` builds need the `mac-mono` module (`unity install-modules -e <version> -m mac-mono`).
- Stop sims cleanly: killing the robot sim with a signal makes WPILib's native teardown abort (fmt assertion) and dump
  multi-GB core files into FRC-2026, and the next start can fail while that happens. `System.exit` from the robot
  thread also crashes (SIGSEGV) when the sim GUI is loaded, because the GUI runs on the main thread. The game sends
  `quit` on exit (`--stop-sim-on-exit`, passed by simulateDriverSim) and the bridge calls `robot.endCompetition()`;
  WPILib's `RobotBase.startRobot` then stops the GUI (`HAL.exitMain`), shuts down the HAL and exits in order.
- Known: the post-processing volume (tonemapping/contrast) doesn't visibly apply yet.
- Scratch captures go to `docs/screenshots/_raw/` (git-ignored; `capture-screenshots.sh <name>` writes there).
  Every meaningful render is copied up to `docs/screenshots/NN-phaseX-what.jpg` (next number, never overwrite or
  delete) with a line in `docs/screenshots/README.md`; the user follows progress there. Screenshots are JPEG
  (smaller): the capture script and `--screenshot` write `.jpg`; convert any PNG before adding it.

## Collisions (phase 3)

- The game-side obstacle table (`Field/FieldObstacleLayout.cs`) is the only obstacle source; red obstacles are generated
  by point reflection of the blue ones. `--collision-selftest` checks the layout and `RobotCollision` with no robot sim
  (batch mode prints to stdout, a windowed run writes the same lines to Player.log).
- `Physics.ComputePenetration` needs a collider for the robot: `RobotCollision` keeps an 8 m tall trigger box proxy,
  used only with explicit positions. With a robot-height (0.5 m) box, a deep overlap (a `resetPose` into the hub) picks
  the vertical exit and the horizontal push is lost. `OverlapBox` needs the `...NonAlloc` variant in this Unity version.
- `PoseCorrector.record` expects the pose to include the corrections already requested; it treats the rest of a jump as
  an outside reset and then ignores corrections for older states. Self-test `resetPose` calls (not via the bridge) are
  caught that way. The drivetrain's pose reflected `resetPose` by the next bridge update in every run so far.
- A game run with `-batchmode -nographics` (what `selfTestDriverSimWithGame` uses) has no frame cap; `DriverSimLink` caps
  it at 60 fps in batch mode so the bridge is not flooded.
- `Nt4Client` reports connected only after it has announced its publications: the NT server drops values sent on a
  topic it hasn't seen announced (this once made the game's `hello` vanish).
- WPILib sim sometimes aborts at startup (`fmt/base.h: assertion failed: negative value`, exit 134), seen twice in a row of
  capture runs. It is intermittent and happens before the bridge does anything; wait a few seconds and retry.
- Bump surfaces are 160 mesh colliders (every mesh under the `GE-26100` nodes) with a `BumpSurface` marker; wheel rays
  keep only those hits.
- A `resetPose` (self-test, F6 reset, match or practice start) is a teleport: the game never blends render samples across a jump over
  0.5 m, and `PoseCorrector` ignores corrections stamped within 0.05 s after a pose discontinuity. Without both, a
  blended state from either side of the jump gets "corrected" and the robot is moved to a wrong place (it showed up as
  a flaky bump/trench self-test step).
