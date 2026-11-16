#!/usr/bin/env bash
# Writes Field2026-lite.glb and Fuel-lite.glb, simplified copies of the AdvantageScope field and fuel models, with
# meshoptimizer's gltfpack.
#
#   scripts/simplify-field.sh [ratio]     (default: 0.1)
#
# ratio is gltfpack's -si for the field: the share of triangles each mesh keeps. The fuel ball keeps 10% with a 5% error
# limit (1244 -> 124 triangles). Named nodes and materials are kept (-kn -km) so
# the game finds the staged fuel, driver station acrylic and bump parts by name; -noq writes plain float vertices.
# Needs gltfpack on PATH (https://github.com/zeux/meshoptimizer/releases, gltfpack-ubuntu.zip; tested with 1.3).
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dir="$here/unity/DriverSim/Assets/ThirdParty/AdvantageScope/Field2026"
ratio="${1:-0.1}"
if ! command -v gltfpack >/dev/null; then
    echo "gltfpack not found; install it from https://github.com/zeux/meshoptimizer/releases" >&2
    exit 1
fi
gltfpack -i "$dir/Field2026.glb" -o "$dir/Field2026-lite.glb" -kn -km -noq -si "$ratio" -v
gltfpack -i "$dir/Fuel.glb" -o "$dir/Fuel-lite.glb" -kn -km -noq -si 0.1 -se 0.05 -v
