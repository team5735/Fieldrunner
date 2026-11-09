#!/usr/bin/env bash
# Runs the robot sim + bridge + built game, drives it with probe commands, and saves game screenshots.
#
#   scripts/capture-screenshots.sh <name> "<probe args>" [game args...]
#
#   name        a bare name goes to docs/screenshots/_raw/<name>_0.jpg, _1, _2 (git-ignored scratch); a path
#               ending in .jpg (or .png) is used as given. 3 shots, 6 s apart, starting 12 s in.
#   probe args  comma-separated probeDriverSim args, sent one per second once connected,
#               e.g. "40,station R2,resetPose 12 3 3.14,start"
#   game args   extra game arguments, e.g. --view overhead
#
# Needs a built game (scripts/build-unity.sh) and FRC-2026 next to Driver-Sim. Windows appear on the display.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
robot="$here/../FRC-2026"
out="$1"
if [[ "$out" != *.jpg && "$out" != *.jpeg && "$out" != *.png ]]; then
    mkdir -p "$here/docs/screenshots/_raw"
    out="$here/docs/screenshots/_raw/$out.jpg"
fi
probe_args="$2"
shift 2
log="$(mktemp)"
ulimit -c 0 # a sim killed by the timeout must not leave multi-GB core dumps

export DRIVERSIM_GAME_ARGS="--screenshot $out --screenshot-delay 12 --screenshot-count 3 --screenshot-interval 6 --quit-after-screenshot $*"
if [[ -z "${JAVA_HOME:-}" && -x "$HOME/.local/jdk-17/bin/java" ]]; then
    export JAVA_HOME="$HOME/.local/jdk-17"
fi

cd "$robot"
timeout 90 ./gradlew simulateDriverSim --console=plain >"$log" 2>&1 &
sim_pid=$!
until grep -q "startup complete" "$log"; do
    if ! kill -0 "$sim_pid" 2>/dev/null; then
        echo "robot sim exited early; log: $log" >&2
        exit 1
    fi
    sleep 1
done
./gradlew probeDriverSim -Pargs="$probe_args" --console=plain 2>&1 | grep -E "\[probe\] sent" || true
wait "$sim_pid" || true
ls -1 "${out%.*}"_*."${out##*.}"
