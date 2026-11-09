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
| 16 | Phase 3b title screen: Start Match, Free Practice, Controls, Options, Quit over the live field view; robot sim connected, keyboard active. |
| 17 | Station picker (Start Match / Free Practice): Blue 1-3 / Red 1-3, last choice preselected. |
| 18 | Controls screen: schematic gamepad with each input labelled by its robot action, and the rebinding table (controller and keyboard inputs, per-binding reset, invert drive stick Y). |
| 19 | Options screen: camera view, field of view, details panel, colliders, fullscreen, resolution, graphics quality. |
| 20 | Pause menu during free practice (Restart Match appears only in a match). |
| 21 | In-game HUD with the "Controller connected" toast and the new hotkey line (`--screenshot-menu toast`). |
| 22 | Title screen after the rename: the game is now called Fieldrunner (window title, title screen, executables); plumbing names stay DriverSim. |
| 23 | Title screen with a real controller: "Input: Microsoft Xbox One 2015" after the container got the host's /dev/input. |
| 24 | Phase 3c Controllers screen with the Xbox controller as Driver (large slot on the left) and the keyboard in the middle. |
| 25 | Controllers screen after Left arrow on the keyboard card: the keyboard took the Driver slot and the Xbox controller dropped to the middle. |
| 26 | Controllers screen after Right arrow with the keyboard driving: the slot is empty, a dimmed ghost keyboard ("drives if no one takes the slot") shows, both devices sit in the middle. |
| 27 | Title screen with the new Controllers item and the "Driver: ..." line (was "Input: ..."). |
| 28 | Pause menu with the new Controllers item between Controls and Options. |
