# Spec: field model simplification

Status: approved. Read `CLAUDE.md` and `../FRC/STYLE.md` (C# mapping in CLAUDE.md) first. If something here turns out
impossible or clearly wrong in practice, make the smallest sensible deviation and list it.

## Goal

Make the AdvantageScope field cheap enough for weak GPUs (Chromebooks through ChromeOS's Linux container, school
laptops) without visibly changing what the driver sees. Today `Field2026.glb` is raw CAD: about 4.3 million triangles
in about 2,900 meshes (one draw call each, plus shadow passes), with 28 materials. Most of it is fasteners, tape,
brackets and internal parts that are a few pixels or invisible from a driver station.

## Rules

- **The field asset stays untouched.** `Assets/ThirdParty/AdvantageScope/Field2026/Field2026.glb` is third-party; no
  edits, no re-export. Simplification happens in the scene builder and at startup, from code.
- **No large generated assets in git.** Combined meshes are built at runtime, not saved as `.asset` files.
- **Nodes the game uses by name keep working exactly as now**:
  - staged fuel (`GE-26900: Fuel`): `FieldSetup` records their positions, then hides them;
  - driver station acrylic (`FE-00012`): `FieldSetup.HideAcrylicNear` toggles their renderers per view;
  - bumps (`GE-26100`): `SceneBuilder.AddBumpColliders` gives their meshes `MeshCollider` + `BumpSurface`; the
    colliders must keep using the original, full-detail meshes (the bump ride and wheel rays depend on them).
  Keep these out of every step below.

## Steps

1. **Drop small parts at scene build** (`SceneBuilder`): remove field renderers (or their nodes) whose world-space
   bounds' largest side is under `MinPartSizeM` (start at 0.05 m: bolts, nuts, rivets, washers, cable ties, PEMs,
   spacers). Long thin parts (tape lines, rails, rods) survive because their largest side is long. Log how many parts
   and triangles were dropped.
2. **Merge by material at startup** (`FieldSetup.Awake`, after it has recorded staged fuel and acrylic): combine the
   remaining field meshes into one mesh per material per field region (split the field into a small grid, e.g. 4 x 2
   cells, so frustum culling still helps), with 32-bit index buffers; disable the original renderers. Keep shadow
   casting as the scene builder set it (small parts already cast none): merge shadow casters and non-casters
   separately. Log renderers and triangles before and after.
3. **Measure and report**, in the player log at startup: `[Driver-Sim] field: N renderers, M triangles (from N0,
   M0)`. Also add the field's renderer and triangle counts to the corner details panel's fuel line or a new line.
4. **Tune `MinPartSizeM`** upward (0.05 -> 0.1 -> 0.15 m) only while the before/after screenshots show no visible
   difference from the driver stations and the overhead view; pick the largest value that passes and say why.

Decimation (reducing triangles inside a part) is out of scope: Unity has no built-in mesh decimator.

## Verification

- `scripts/build-unity.sh linux`; player `--collision-selftest` and `--gameplay-selftest` pass.
- `./gradlew selfTestDriverSim` 15/15 and `selfTestDriverSimWithGame` 26/26 (from `../FRC-2026`).
- Before/after screenshot pairs from the same viewpoints: Blue 1 driver station, Red 2 driver station, and overhead
  (`scripts/capture-screenshots.sh`, game args `--station` / `--view overhead`). Save the "after" set to
  `docs/screenshots/` with the next numbers and README lines; keep "before" in `_raw/`. Read them and compare.
- Frame timing from the `[Driver-Sim] frame rate:` log lines before and after at the same window size, on Vulkan if
  possible (`VK_DRIVER_FILES` pointing at an ICD manifest for `libGLX_nvidia.so.0`; on OpenGL Unity reports no GPU
  time). Report renderers, triangles, cpu ms and gpu ms before and after.
- Startup time: the merge must add well under 2 s to startup; report it.
