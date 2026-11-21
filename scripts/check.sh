#!/usr/bin/env bash
# Check the mod's contract with the installed game's assemblies under Mono:
# its private members, Harmony targets, and wrapped prefab loads. Unity's
# native side is not needed. Installed Multiplayer, Custom Car Loader, and
# Realistic Firing are loaded in place when present, never copied.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dv="${DV_INSTALL_DIR:-$HOME/.local/share/Steam/steamapps/common/Derail Valley}"
project="$repo/tools/CommsTablet.Check"

command -v dotnet >/dev/null || { echo "dotnet is required" >&2; exit 1; }
command -v mono >/dev/null || { echo "mono is required" >&2; exit 1; }
# PUBLIC_RELEASE=1 checks the public release's build (scripts/package.sh), without the self-test's hooks.
mode=()
[[ "${PUBLIC_RELEASE:-}" == 1 ]] && mode=(-p:PublicRelease=true)
cap=()
if command -v systemd-run >/dev/null && systemd-run --user --scope -q -p MemoryMax=6G -p MemorySwapMax=0 true >/dev/null 2>&1; then
    cap=(systemd-run --user --scope -q -p MemoryMax=6G -p MemorySwapMax=0)
fi
# Fail on a failed build or any compiler warning; never run a stale checker.
if ! build="$("${cap[@]}" dotnet build "$project/CommsTablet.Check.csproj" -c Release -p:DvInstallDir="$dv" "${mode[@]}" --no-incremental -nologo 2>&1)"; then
    if grep -q -E "error" <<<"$build"; then
        grep -E "error" <<<"$build" | sort -u >&2
    else
        tail -n 40 <<<"$build" >&2
    fi
    echo "check build failed" >&2
    exit 1
fi
if grep -q -E "warning CS" <<<"$build"; then
    grep -E "warning CS" <<<"$build" | sort -u >&2
    exit 1
fi
# CHECK_RF_DLL checks another Realistic Firing build than the installed one (a test build's, say); other
# mods' DLLs given as arguments are required too (Realistic Brakes': its Core beside it).
# Default auto-detected game mods stay optional when not installed.
# DV Car Changer's and Double Track's pinned assemblies (scripts/fetch-check-mods.sh), when fetched.
check_args=(
    "$dv/DerailValley_Data/Managed"
    --optional "$dv/Mods/Multiplayer/Multiplayer.dll"
    --optional "$dv/Mods/DVCustomCarLoader/CCL.Importer.dll"
    --optional "$dv/Mods/DVLangHelper/DVLangHelper.Data.dll"
)
if [[ -n "${CHECK_RF_DLL:-}" ]]; then
    check_args+=(--require "$CHECK_RF_DLL")
else
    check_args+=(--optional "$dv/Mods/RealisticFiring/RealisticFiring.dll")
fi
[[ -f "$repo/.deps/DVCarChanger/CarChanger.Game.dll" ]] && check_args+=(--require "$repo/.deps/DVCarChanger/CarChanger.Common.dll" --require "$repo/.deps/DVCarChanger/CarChanger.Game.dll")
[[ -f "$repo/.deps/DoubleTrack/DoubleTrack.dll" ]] && check_args+=(--require "$repo/.deps/DoubleTrack/DoubleTrack.dll")
check_args+=("$@")

exe="$project/bin/Release/net48/CommsTablet.Check.exe"
echo "Checking under default locale"
mono "$exe" "${check_args[@]}"
for loc in de_DE.UTF-8 tr_TR.UTF-8; do
    echo "Checking under LC_ALL=$loc"
    LC_ALL="$loc" mono "$exe" "${check_args[@]}"
done
