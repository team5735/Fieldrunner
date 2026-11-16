# Spec: Low graphics quality and a lighter field mesh

Status: done (Part 2 uses gltfpack at -si 0.1). Read `CLAUDE.md` and `../FRC/STYLE.md` (C# mapping in CLAUDE.md) first. If something here turns out
impossible or clearly wrong in practice, make the smallest sensible deviation and list it.

## Goal

Make Fieldrunner playable on weak GPUs (Chromebooks through ChromeOS's Linux container, school laptops). Two parts:

1. a **Low** graphics quality level that strips the expensive rendering features and runs at 30 fps, picked
   automatically on weak GPUs;
2. a **simplified field mesh** for every quality level: today the field still draws about 1.9 million triangles
   after `FieldSetup`'s merge (`field-simplification.md` dropped small parts but did no decimation).

## Part 1: Low quality level

Today there are two quality levels, `Mobile` (index 0) and `PC` (index 1, the Standalone default). `PC` uses
`PC_RPAsset` + `PC_Renderer`: Forward+, SSAO renderer feature, HDR, depth and opaque texture copies, 2048 shadow map
with 2 cascades.

- **Rename the levels** `Low` (index 0) and `High` (index 1). `High` keeps today's PC settings unchanged. Rename the
  assets to match (`Low_RPAsset`, `Low_Renderer`, `High_RPAsset`, `High_Renderer`), keeping their GUIDs so references
  don't break. The Options screen already lists `QualitySettings.names`, so it shows Low / High with no UI change.
- **Low settings** (`Low_RPAsset` / `Low_Renderer`, starting from today's Mobile assets):
  - main light shadows off, additional light shadows off;
  - HDR off, MSAA off, depth texture off, opaque texture off;
  - Forward rendering, no renderer features (no SSAO);
  - post-processing off on the renderer, so the ACES / color-adjustment volume costs nothing. Expect slightly flatter
    colors than High; that's accepted.
- **Frame rate 30 on Low.** `DriverSimLink` sets `Application.targetFrameRate` once at startup (display refresh, or a
  fixed rate in batch mode). Move the windowed choice into one place that runs at startup and whenever the quality
  level changes: Low -> 30; High -> display refresh as today. Batch mode keeps its fixed rate.
- **Render size on Low:** `RenderScaleCap` caps rendered pixels at 1080p's count (`MaxRenderPixels`). On Low, cap at
  720p's count (1280 x 720) instead, same FSR upscale and same 0.5 minimum scale. `RenderScaleCap` caches the active
  URP asset in `OnEnable`; switching quality swaps the asset, so it must notice the change (re-read
  `GraphicsSettings.currentRenderPipeline` / `QualitySettings.renderPipeline` when the level changes), restore the
  old asset's values and apply the cap to the new one.
- **Automatic choice:** with no saved quality preference, `GameOptions` picks Low when the GPU looks weak, otherwise
  High. Weak means `SystemInfo.graphicsDeviceName` contains (case-insensitive) one of `llvmpipe`, `lavapipe`,
  `softpipe`, `SwiftShader`, `virgl`, `Venus`, `Mali`, `Adreno`, `PowerVR`, or contains `Intel` but not `Arc`; or
  `SystemInfo.graphicsMemorySize` is under 2048 MB. The choice is not saved; picking a level in Options saves it as
  today. Extend the existing `[Driver-Sim] graphics:` startup line (`DriverSimLink`) with the memory and the choice:
  `[Driver-Sim] graphics: <device name> (<api>), <memory> MB, quality Low (auto)` (or `High`, `(saved)`,
  `(command line)`).
- **Command line** `--quality low|high` overrides both the saved and the automatic choice for that run, for testing.
- The HUD's perf line and the frame-rate log stay as they are.

## Part 2: simplified field mesh

Unity has no built-in mesh decimator, so this needs a third-party simplifier. **Recommended:** meshoptimizer's
`gltfpack` (MIT, the standard glTF simplifier), run once offline to produce a lighter copy of the field model:

- `scripts/simplify-field.sh` downloads a pinned `gltfpack` release into a git-ignored tools folder and writes
  `Assets/ThirdParty/AdvantageScope/Field2026/Field2026-lite.glb` from the untouched `Field2026.glb`, with flags that
  keep node names and materials (`-kn -km`), no quantization or compression (`-noq`, plain glTF that glTFast reads
  as today), and a simplification ratio and error limit (`-si`, `-se`) tuned as below. Commit the lite GLB (the
  BSD license allows derived copies; add a line to that folder's `README.md` saying how it was made).
- `SceneBuilder` instantiates `Field2026-lite.glb` instead of `Field2026.glb`. The original stays in the repo as the
  source but is no longer referenced by the scene, so it leaves the build (builds get about 18 MB smaller, minus
  the lite file's size).
- This changes one rule of `field-simplification.md`: "no large generated assets in git". The lite GLB is generated
  and committed; it should come out well under the original's 18 MB.
- Nodes the game uses by name must keep working: staged fuel (`GE-26900: Fuel`), driver station acrylic
  (`FE-00012`), bumps (`GE-26100`). The bump `MeshCollider`s get the simplified bump meshes; the collision and
  gameplay self-tests and a bump-ride check (drive over a bump, robot rises and settles as before) must pass.
- **Tuning:** start at `-si 0.25`, then try 0.1 and 0.05, and keep the most aggressive setting whose screenshots show
  no visible difference from the driver stations and overhead. Aim for 200k field triangles or fewer.
- `FieldSetup`'s small-part drop and merge stay as they are, running on the lite model.

Alternative if you don't want a downloaded binary: vendor UnityMeshSimplifier (MIT C# source) into `Editor/` and
simplify in the scene builder, saving meshes as generated, git-ignored assets. More code, and a weaker simplifier.

## Verification

- `scripts/build-unity.sh linux`; player `--collision-selftest` and `--gameplay-selftest` pass on both levels
  (`--quality low` and `--quality high`).
- `./gradlew selfTestDriverSim` and `selfTestDriverSimWithGame` pass (from `../FRC-2026`).
- Screenshots on High before/after the lite field, and on Low, from Blue 1, Red 2 and overhead
  (`scripts/capture-screenshots.sh`). Save the after and Low sets to `docs/screenshots/` with the next numbers and
  README lines; keep before in `_raw/`.
- Report field triangles before/after, build size before/after, and frame-rate log lines on each level at the same
  window size. On this machine, also run with Mesa's software renderer as a weak-GPU stand-in
  (`__GLX_VENDOR_LIBRARY_NAME=mesa LIBGL_ALWAYS_SOFTWARE=1`, if Unity accepts it) and confirm the auto choice picks
  Low and logs it.
