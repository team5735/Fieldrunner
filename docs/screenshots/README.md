# Screenshots

Chronological record of the game's renders as it's built, numbered in order and never replaced. Each name says the
phase and what changed. Scratch captures live in `_raw/` (git-ignored, ephemeral).

| # | What |
|---|---|
| 01 | First render: Blue 2 driver view, robot placed by the probe. Staged fuel still visible; bumper numbers show through the robot. |
| 02 | Same run, robot moved downfield to the right of the hub. |
| 03 | Staged fuel hidden, depth-tested bumper numbers; match running (AUTO), robot enabled, turret zeroed. |
| 04 | Overhead debug view (`--view overhead`), blue at the bottom. |
| 05 | Red 2 driver view: mirrored camera, red bumpers. |
| 06 | After a lighting/post-processing pass (post-processing not yet taking effect). |
| 07 | Haze fixed: the camera was looking through the model's 20%-opaque gray driver station acrylic; the panels on the driver's own wall are now hidden. |
| 08 | Blue 1 view, camera now faces the center of the field from every station. |
| 09 | Red 1 view (mirror of Blue 1), after the clean-teardown fix. |
| 10 | Red 2 view after applying the team style guide to all code: same behavior, clean session teardown. |
| 11 | Phase 3 collider overlay (`--show-colliders --view overhead`): cyan = Solid, yellow = LowCeiling boxes over the hubs, trench blocks and bars, tower uprights and the perimeter walls on both sides. |
| 12 | Robot placed with `resetPose` overlapping the blue hub: drawn flush against the hub face; the F1 panel shows the robot code's pose has converged to the same x (3.56) after the game's corrections. |
| 13 | Robot on the blue right bump (`resetPose 4.25 2.4 0`, Blue 1 view): raised and nosed up while the front wheels climb the near slope; the F1 panel's bump line reads rise 0.076 m, nose up 13.8 deg. |
| 14 | Robot under the blue right trench bar with the hood down (8 deg): passes through, collision state clear. |
| 15 | First render on the NVIDIA GPU (RTX 4090, OpenGL Core) after the container got the NVIDIA graphics libraries; same scene as before, Blue 2 in auto. |
