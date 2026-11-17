#!/usr/bin/env bash
# Fails if a build of CommsTablet.dll holds anything of the in-game self-test,
# which the public release leaves out (no developer options or testbeds in a
# public release): its access to the mod's internals, its hooks (a pointer or aim
# put in place of the player's, presses, pages opened for it, VR plates on a
# desktop), its stand-in's place in the Settings app, and the records kept only
# for it. (The mod has no settings of its own: the contract check finds no
# settings type in it.) Names are looked for among the DLL's identifiers (ASCII) and its string
# literals (UTF-16), as AI Engineer's check-public-dll.sh does.
set -euo pipefail
dll="${1:?usage: scripts/check-public-dll.sh CommsTablet.dll}"
names=(ForTest SelfTest HitInPlace SlotsShown MapButtonShown HostPartsShown LocoMarks TrackAtCanvas
       PaperReplaced FailedIn)
labels=("self-test" "selftest" "Test App")
found=()
literals="$(strings -el "$dll")"
for n in "${names[@]}"; do
    if grep -a -q -F "$n" "$dll" || grep -q -F "$n" <<<"$literals"; then found+=("$n"); fi
done
for l in "${labels[@]}"; do
    if grep -q -i -F "$l" <<<"$literals"; then found+=("\"$l\""); fi
done
if (( ${#found[@]} )); then
    echo "The public build holds the self-test's access, hooks, or records: ${found[*]}" >&2
    exit 1
fi
echo "No self-test access, hooks, or records in $(basename "$dll")"
