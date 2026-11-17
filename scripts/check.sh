#!/usr/bin/env bash
# Check the mod's contract with the installed game's assemblies under Mono:
# its private members, Harmony targets, and wrapped prefab loads. Unity's
# native side is not needed. Installed Multiplayer, Custom Car Loader, and
# Realistic Firing are loaded in place when present, never copied.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dv="${DV_INSTALL_DIR:-$HOME/.local/share/Steam/steamapps/common/Derail Valley}"
project="$repo/tools/CommsTablet.Check"

command -v mono >/dev/null || { echo "mono is required" >&2; exit 1; }
# PUBLIC_RELEASE=1 checks the public release's build (scripts/package.sh), without the self-test's hooks.
mode=()
[[ "${PUBLIC_RELEASE:-}" == 1 ]] && mode=(-p:PublicRelease=true)
# Fail on a failed build or any compiler warning; never run a stale checker.
if ! build="$(dotnet build "$project/CommsTablet.Check.csproj" -c Release -p:DvInstallDir="$dv" "${mode[@]}" -nologo 2>&1)"; then
    grep -E "error" <<<"$build" | sort -u >&2
    echo "check build failed" >&2
    exit 1
fi
if grep -q -E "warning CS" <<<"$build"; then
    grep -E "warning CS" <<<"$build" | sort -u >&2
    exit 1
fi
# DV Car Changer's and Double Track's pinned assemblies (scripts/fetch-check-mods.sh), when fetched.
pinned=()
[[ -f "$repo/.deps/DVCarChanger/CarChanger.Game.dll" ]] && pinned+=("$repo/.deps/DVCarChanger/CarChanger.Common.dll" "$repo/.deps/DVCarChanger/CarChanger.Game.dll")
[[ -f "$repo/.deps/DoubleTrack/DoubleTrack.dll" ]] && pinned+=("$repo/.deps/DoubleTrack/DoubleTrack.dll")
mono "$project/bin/Release/net48/CommsTablet.Check.exe" "$dv/DerailValley_Data/Managed" \
    "$dv/Mods/Multiplayer/Multiplayer.dll" "$dv/Mods/DVCustomCarLoader/CCL.Importer.dll" \
    "$dv/Mods/RealisticFiring/RealisticFiring.dll" "$dv/Mods/DVLangHelper/DVLangHelper.Data.dll" "${pinned[@]}" "$@"
