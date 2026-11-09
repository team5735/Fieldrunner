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
