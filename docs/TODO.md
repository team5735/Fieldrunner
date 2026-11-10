# TODO (parked)

Known open items, not planned. Pick one up if it starts to matter.

- **Vision makes Back useless in sim.** The fake Limelights see a tag from everywhere, so the robot's pose estimator
  pulls the heading back to truth within ~0.1 s of `seedFieldCentric`. Fix: give `LimelightSubsystem`'s sim a field of
  view and range against the 2026 AprilTag layout (mount angles are in its comments), and/or a game option that flips
  the robot code's `/vision/enabled`.
- **Post-processing never applies.** The player log says URP's post-processing shaders are stripped from the build
  ("PostProcessing render passes will not execute"). Likely a URP shader-stripping / renderer setting.
- **Turret zero self-test flake.** "turret zeroes on hall switch" times out about 1 run in 7-10, before and after the
  sim refactors. Startup loop overruns appear in failing runs; cause not found.
- **Bump collision uses 160 mesh colliders.** Negligible CPU today; could be a few boxes / ramps.
- **Turret overshoot in sim.** Starting near its reverse limit, the simulated turret swings past a target to the
  forward limit and is re-zeroed, so the first volley after enabling sprays. The with-game shot self-test points the
  robot so the turret needn't move. Look at the turret sim's plant (kV/kA) against the robot code's profile.
