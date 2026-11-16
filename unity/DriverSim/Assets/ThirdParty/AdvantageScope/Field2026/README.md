# 2026 FRC field (REBUILT) from AdvantageScope

Source: AdvantageScopeAssets release `default-assets-v2`, `Field3d_2026FRCFieldV2.zip`
(https://github.com/Mechanical-Advantage/AdvantageScopeAssets). Redistributed under LICENSE.txt
(Littleton Robotics / FRC 6328).

- `Field2026.glb` = `model.glb`: field and staged fuel (`GE-26900_Fuel*` nodes, 456 balls).
- `Fuel.glb` = `model_0.glb`: one fuel ball.
- `Config.json`: AdvantageScope metadata. Model origin is the field center; glTF Y-up (AdvantageScope rotates
  90 deg about X to its Z-up frame). Field 651.22 x 317.677 in.
- `Field2026-lite.glb`: `Field2026.glb` simplified by `scripts/simplify-field.sh` (meshoptimizer's gltfpack 1.3,
  `-kn -km -noq -si 0.1`). The scene uses this one; `Field2026.glb` is kept as its source.
- `Fuel-lite.glb`: `Fuel.glb` simplified by the same script (`-kn -km -noq -si 0.1 -se 0.05`, 124 triangles).
