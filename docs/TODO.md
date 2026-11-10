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
- **Field model is raw CAD** (4.3M triangles, ~2,900 meshes). Fine on an RTX 4090 laptop (GPU ~1.4 ms at 1600x900);
  could matter on weak laptops. Fix: simplify at import (drop small parts, merge by material, decimate).
- **Bump collision uses 160 mesh colliders.** Negligible CPU today; could be a few boxes / ramps.
