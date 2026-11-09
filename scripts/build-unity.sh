#!/usr/bin/env bash
# Builds the Fieldrunner Unity game in batch mode.
#
#   scripts/build-unity.sh [linux|windows|mac|all|scene]     (default: linux)
#
# Output goes to unity/Builds/<Platform>/, where ./gradlew simulateDriverSim finds it. The editor version comes from
# unity/DriverSim/ProjectSettings/ProjectVersion.txt; set UNITY_EDITOR to override the editor binary. The project must
# not be open in the Unity editor while this runs. Building mac needs the editor's Mac Build Support (Mono) module:
#   unity install-modules -e <version> -m mac-mono --accept-eula
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$here/unity/DriverSim"
version="$(sed -n 's/^m_EditorVersion: //p' "$project/ProjectSettings/ProjectVersion.txt")"
editor="${UNITY_EDITOR:-$HOME/Unity/Hub/Editor/$version/Editor/Unity}"
if [[ ! -x "$editor" ]]; then
    echo "Unity editor $version not found at $editor (install it with Unity Hub or set UNITY_EDITOR)" >&2
    exit 1
fi

case "${1:-linux}" in
    linux)   method="DriverSim.Editor.BuildPlayers.BuildLinux" ;;
    windows) method="DriverSim.Editor.BuildPlayers.BuildWindows" ;;
    mac)     method="DriverSim.Editor.BuildPlayers.BuildMac" ;;
    all)     method="DriverSim.Editor.BuildPlayers.BuildAll" ;;
    scene)   method="DriverSim.Editor.SceneBuilder.BuildScene" ;;
    *) echo "usage: $0 [linux|windows|mac|all|scene]" >&2; exit 2 ;;
esac

log="$here/unity/Builds/build.log"
mkdir -p "$(dirname "$log")"
echo "Building ($1) with Unity $version; log: $log"
if ! "$editor" -batchmode -quit -projectPath "$project" -executeMethod "$method" -logFile "$log"; then
    echo "Unity build failed; errors:" >&2
    grep -E "error CS|Exception|\[Driver-Sim\]|Build Finished|BuildResult" "$log" | tail -40 >&2
    exit 1
fi
grep -E "\[Driver-Sim\]" "$log" || true
