# Design note: Fieldrunner as a general-purpose tool

Status: idea, not planned. Written 2026-11-10 to capture the direction; see `docs/TODO.md` for what's next up.

## Question

Can the game and its bridge work with any team's robot code, not just FRC 5735's, so Fieldrunner can be offered as a
general-purpose driver trainer?

## Where we are

- The game talks NetworkTables (`/DriverSim/*`) to a Java **bridge** that runs inside the robot program.
- The bridge is the driver station (joysticks, enable, match timing) through WPILib's `DriverStationSim`. That part
  knows nothing about our robot.
- Everything else is robot-specific: the bridge calls our subsystems' `fr` methods (`frGetSimTruthPose()`,
  `frSimPush(...)`, `frGetSimAngleRobotRel()`, `frSimBallLaunched()`, ...), and the game draws a hand-built model of
  our robot from a fixed state array (`RobotStateLayout`).

## What the WPILib HAL can and can't give a generic tool

Can (robot-agnostic):

- Driver station, joysticks, match state.
- roboRIO I/O: DIO, PWM, analog inputs, encoders, relays, and every `SimDevice` (generic named values that some vendor
  libraries register; REV's Spark sims do).
- An external, documented protocol for all of the above: WPILib's `halsim_ws` extension (WebSockets, JSON). An outside
  program can set joysticks and driver station state, read PWM and DIO, and set sensor inputs with no Java of its own.

Can't:

1. **CTRE devices aren't HAL devices.** Phoenix 6 simulates TalonFX, CANcoder and Pigeon 2 in its own sim layer,
   reached in-process through `getSimState()`. A HAL-only tool can't see or drive most teams' motors.
2. **Knowing a motor isn't knowing the mechanism.** "TalonFX 22 at 3 V" says nothing about a 10:1 turret with a hall
   switch. Ground-truth physics needs robot-specific models, which is why the sim lives in each subsystem's
   `simulationPeriodic()` (`docs/specs/sim-per-subsystem.md`).
3. **World feedback needs a contract.** Collisions moving the true pose, a game piece entering the intake, the flywheel
   losing speed per shot: the tool must tell the robot's sim something, and the HAL has no concept for it.

So a zero-code-change, HAL-only tool is out of reach for physics. A small opt-in contract is not.

## Proposed shape: three layers, each as generic as it can be

| Layer | Standard | What a team adds |
|---|---|---|
| Driver station, joysticks, match control | `halsim_ws` (or `DriverStationSim` in-process, as today) | nothing |
| Drawing the robot | AdvantageScope's conventions: robot pose as a `Pose2d`/`Pose3d` struct topic, moving parts as a component `Pose3d[]` topic, and a robot model folder (glTF + config) in AdvantageScope's format | nothing, if they already use AdvantageScope's 3D view |
| Physics and world feedback | a small Java library (vendordep) with fixed interfaces | a dozen lines in their sim code |

### Layer 3 library sketch

Interfaces the robot's sim implements or registers (names illustrative):

- **Drivetrain**: supplies chassis speeds from the simulated modules; receives truth-pose corrections from the game
  (today: `frGetSimTruthPose`, `frSimPlace`, `frSimPush`).
- **Intake**: a zone on the robot (robot frame box) and an "is taking pieces" check; receives "piece acquired".
- **Indexer / hopper**: capacity; the game owns the count (as today).
- **Shooter**: exit speed and direction from the robot's state (flywheel speed, hood, turret), or a lookup the team
  supplies; reports launches back (today: `frSimBallLaunched`).
- **Mechanism truth for drawing**: covered by layer 2's component poses.

The library would own the NetworkTables contract with the game, so the game-side protocol stops depending on our
`RobotStateLayout`.

### What changes in the game

- Draw the robot from the team's AdvantageScope-format model and component poses instead of `RobotModel`'s hand-built
  boxes.
- Read the robot pose and component poses from the standard topics.
- Keep the field, fuel, scoring, HUD and menus as they are (season-specific, not robot-specific).

## Costs and risks

- A published library and a versioned protocol: real maintenance (releases, compatibility across WPILib and vendor
  versions, docs for other teams).
- Shot models differ per robot; a team without a calibrated table gets a "close enough" game shot.
- Season churn: field, game pieces and scoring rules change every year; the game layer has to be rebuilt each season
  either way.
- Overlap with existing tools (next section). Building the generic version only makes sense if Fieldrunner's niche
  is worth it to other teams.

## Prior art

- **AdvantageScope** (Mechanical Advantage, 6328): log and live-NT viewer with a 3D field view that already defines the
  robot-model and pose conventions above.
- **maple-sim** (open source FRC sim library): swerve and differential drive physics with field collisions, game
  pieces, intake and projectile helpers, publishing in AdvantageScope's format. Its API is the closest existing thing
  to layer 3.

## Where Fieldrunner fits

The three tools do different jobs. (Details on AdvantageScope and maple-sim are from general knowledge as of
2026-11; check their current docs, including season support, before building on them.)

| | AdvantageScope 3D field | maple-sim | Fieldrunner |
|---|---|---|---|
| What it is | viewer for logs and live NetworkTables | Java physics library inside the robot code | game front end and driver station |
| Simulates | nothing; draws what the robot publishes | drivetrain physics, collisions with field and robots, game pieces, intake and projectile helpers (2D rigid-body engine) | arcade collisions, fuel, scripted shot arcs, scoring |
| Input | none | WPILib sim GUI joysticks | controller or keyboard as the robot's Xbox controller; menus, pause, match flow |
| View | analysis camera, including driver-station viewpoints | (uses AdvantageScope) | driver-station game camera with HUD |
| Robot support | any robot (team-supplied model and pose topics) | any robot that adopts the library | one robot today |

- **AdvantageScope shows what the robot did.** It's the de facto standard for describing a robot in 3D: pose topics,
  component poses, model format.
- **maple-sim computes what physically happens**, for teams willing to take a physics engine into their robot code.
- **Fieldrunner lets a driver practice at the driver station against the real robot code**: controller, camera,
  match flow with hub shifts, scoring, HUD. Not general, not physically rigorous, not an analysis tool.

Overlap:

- With maple-sim: both own a truth pose, collisions and game pieces. A team already on maple-sim would want
  Fieldrunner to display and drive their maple-sim world, not run a second set of physics.
- With AdvantageScope: both draw a 3D field. Ours is a game camera with a HUD; theirs is an analysis view. Their
  robot-model format is the thing worth adopting.

What that means for the general-purpose direction:

1. **Adopt AdvantageScope's robot description as Fieldrunner's input.** Any team that can see their robot in
   AdvantageScope can drive it in Fieldrunner. Biggest win for the least work.
2. **Be physics-pluggable.** Teams on maple-sim keep their physics; Fieldrunner reads poses and game pieces and adds
   the driver-station experience. Teams without it use Fieldrunner's simple Unity physics through the layer-3
   library.
3. **Don't compete with either.** No log analysis, no rigorous physics. The value is the driver-training game.

## Smallest first step if this is picked up

1. Do the TODO's "hook the bridge into the robot code's own entry point" (one entry point, no copied `Main`).
2. Make the game draw the robot from AdvantageScope-format pose topics and a model folder (our robot's model built in
  that format), keeping today's bridge. That alone makes the visuals team-agnostic.
3. Only then extract the `fr` methods into a library with interfaces.
