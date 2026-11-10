# TODO (parked)

Known open items, not planned. Pick one up if it starts to matter.

## Next up

- **Hook the bridge into the robot code's own entry point.** Today `simulateDriverSim` runs `driversim.DriverSimMain`,
  which copies `frc.robot.Main`'s robot switch and can drift from it. Instead: a two-line sim-only hook in
  `Robot.simulationInit()` (`Fieldrunner.attachIfPresent(this)`, looking up `driversim.DriverSimBridge` by name so the
  robot code still compiles without Fieldrunner), `driversim.gradle` puts the bridge classes on the normal
  `simulateJava` classpath when `../Fieldrunner` exists, and `DriverSimMain` is deleted. Modes keep coming from
  `DRIVERSIM_INPUT` / `DRIVERSIM_SELFTEST` (default `gui`, so VS Code's "Simulate Robot Code" keeps the sim GUI's
  joysticks and the game just adds the field). `simulateDriverSim` and the self-tests stay as shortcuts. Verify with
  both self-tests and a plain `simulateJava` + game run.

## Parked

- **Vision makes Back useless in sim.** The fake Limelights see a tag from everywhere, so the robot's pose estimator
  pulls the heading back to truth within ~0.1 s of `seedFieldCentric`. Fix: give `LimelightSubsystem`'s sim a field of
  view and range against the 2026 AprilTag layout (mount angles are in its comments), and/or a game option that flips
  the robot code's `/vision/enabled`.
- **Post-processing never applies.** The player log says URP's post-processing shaders are stripped from the build
  ("PostProcessing render passes will not execute"). Likely a URP shader-stripping / renderer setting.
- **Turret zero self-test flake.** "turret zeroes on hall switch" times out about 1 run in 7-10, before and after the
  sim refactors. Startup loop overruns appear in failing runs; cause not found.
- **Bump collision uses 160 mesh colliders.** Negligible CPU today; could be a few boxes / ramps.
- **Shot self-test bypasses turret aiming.** The with-game "shot scores in our hub" step points the robot so the turret
  needn't move (a workaround from when the turret sim overshot; fixed in FRC-2026's turret sim). Placing the robot
  facing the hub and letting the robot code aim would test aiming too.
