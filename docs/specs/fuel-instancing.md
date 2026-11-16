# Spec: draw the fuel with GPU instancing

Status: approved. Read `CLAUDE.md` and `../FRC/STYLE.md` (C# mapping in CLAUDE.md) first. If something here turns out
impossible or clearly wrong in practice, make the smallest sensible deviation and list it.

## Goal

Draw every visible fuel ball with one instanced draw call per ball mesh instead of one renderer per ball. Today each
of the up to 456+ balls is a GameObject with its own copy of the `Fuel-lite.glb` visual (a `MeshRenderer`), so the
fuel costs hundreds of draw calls. Each draw call is expensive on ChromeOS's Linux container, where every call
crosses the virtual GPU. Triangle count stays the same (124 per ball).

## Today

`Game/FuelField.cs`:
- `MakeVisualTemplate` builds one inactive `FuelVisualTemplate`: the fuel model scaled to `BallDiameterM` and
  centered, colliders removed, shadows off, foam material settings. Without a model it is a primitive sphere.
- `CreateBall` makes a ball GameObject (Rigidbody, SphereCollider, `FuelBall`) and instantiates the template under it
  as `Visual`.
- `ChangeState` hides a ball by deactivating its GameObject (Hidden), and shows it otherwise (Resting: physics;
  Flying: kinematic, moved along its scripted arc).

## Change

- **Collect the parts once.** In `MakeVisualTemplate`, after the template is built, record each part as a readonly
  struct: its `Mesh`, submesh index, `Material`, and its matrix relative to the template root (so the model's own
  scale and centering offset carry over). The plain-sphere fallback goes through the same path. Turn on
  `enableInstancing` on each recorded material; if a material's shader doesn't support instancing, log a warning
  once.
- **Balls lose their visual.** `CreateBall` no longer instantiates the template. A ball is just its Rigidbody,
  collider and `FuelBall`. The template stays inactive as the source of the meshes and materials.
- **Draw in `LateUpdate`.** Each frame, for every ball whose GameObject is active (Resting or Flying), compute
  `ballTransform.localToWorldMatrix * partMatrix` into a reused `Matrix4x4[]` per part, then draw with
  `Graphics.RenderMeshInstanced`, in batches of at most 1023 instances per call. `RenderParams`: the `Fuel` layer
  (`GameLayers`), shadow casting off and receiving shadows on as today, and world bounds covering the field plus
  flight height, so Unity doesn't cull the whole batch. No per-frame allocations.
- Ball rotation is kept (the matrix comes from the ball's transform, which physics rotates).
- Nothing else reads the ball visual. If the coding turns up a reader (the hopper, the debug HUD, screenshots),
  keep it working and list it.

Out of scope: `DrawMeshInstancedIndirect` / compute buffers (positions live in the CPU physics), GPU Resident
Drawer (needs Forward+, which Low doesn't use), changing the robot's or field's rendering.

## Verification

- `scripts/build-unity.sh linux`; player `--collision-selftest` and `--gameplay-selftest` pass; `./gradlew
  selfTestDriverSim` and `selfTestDriverSimWithGame` pass (from `../FRC-2026`).
- Visual: screenshots on High and Low from Red 2 (balls near the camera) and overhead, compared with the same views
  before the change. They should be identical. Also check in a running game (F8 or `spawnFuel` / practice
  shooting) that flying balls draw along their arc, scored balls disappear and reappear at the hub exit, and
  respawn (F7) shows the full field of fuel. Save the after shots to `docs/screenshots/` with the next numbers and
  README lines.
- Frame rate with Mesa's software renderer on Low at 640x480 and 1280x720 (before: 30 fps capped and 25 fps), using
  `__GLX_VENDOR_LIBRARY_NAME=mesa LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe Fieldrunner.x86_64 -force-glcore
  --quality low -screen-fullscreen 0 -screen-width W -screen-height H`. Also report the High frame rate on the RTX 4090
  before and after. The real gain is expected on Chromebooks and can't be measured here.
- Update CLAUDE.md's fuel notes: balls have no renderers; `FuelField` draws them instanced in `LateUpdate`.
