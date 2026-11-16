#!/usr/bin/env bash
# Writes Field2026-lite.glb, a simplified copy of the AdvantageScope field model, with meshoptimizer's gltfpack.
#
#   scripts/simplify-field.sh [ratio]     (default: 0.1)
#
# ratio is gltfpack's -si: the share of triangles each mesh keeps. Named nodes and materials are kept (-kn -km) so
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
