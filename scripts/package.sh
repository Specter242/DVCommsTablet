#!/usr/bin/env bash
# Build a Unity Mod Manager package from the public source tree:
# dist/CommsTablet-<version>.zip, one CommsTablet folder holding the mod's
# assembly, Info.json, license, changelog, and the player readme as README.md.
# It builds the public release (-p:PublicRelease=true: none of the in-game
# self-test's access, hooks, or records), runs the contract check on that
# build, and fails on a compiler warning or if anything of the self-test
# reaches the DLL. Released zips are attached to this repository's GitHub
# releases and listed in repository.json, Unity Mod Manager's update feed.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dv="${DV_INSTALL_DIR:-$HOME/.local/share/Steam/steamapps/common/Derail Valley}"
info="$repo/src/CommsTablet/Info.json"
id="$(python3 -I -c 'import json, sys; print(json.load(open(sys.argv[1]))["Id"])' "$info")"
version="$(python3 -I -c 'import json, sys; print(json.load(open(sys.argv[1]))["Version"])' "$info")"
out="$repo/src/CommsTablet/bin/Release/net48"
zipfile="$repo/dist/$id-$version.zip"

PUBLIC_RELEASE=1 "$repo/scripts/check.sh"
if ! build="$(dotnet build "$repo/src/CommsTablet/CommsTablet.csproj" -c Release -p:DvInstallDir="$dv" -p:PublicRelease=true --no-incremental -nologo 2>&1)"; then
    grep -E "error" <<<"$build" | sort -u >&2
    exit 1
fi
if grep -q -E "warning CS" <<<"$build"; then
    grep -E "warning CS" <<<"$build" | sort -u >&2
    exit 1
fi
"$repo/scripts/check-public-dll.sh" "$out/CommsTablet.dll"

mkdir -p "$repo/dist"
stage="$(mktemp -d "$repo/dist/package.XXXXXX")"
trap 'rm -rf "${stage:?}"' EXIT
mkdir "$stage/$id"
cp "$out/Info.json" "$out/CommsTablet.dll" "$repo/LICENSE" "$repo/CHANGELOG.md" "$stage/$id/"
cp "$repo/PLAYER-README.md" "$stage/$id/README.md"
rm -f "${zipfile:?}"
(cd "$stage" && zip -q -X "$zipfile" "$id/CHANGELOG.md" "$id/CommsTablet.dll" "$id/Info.json" "$id/LICENSE" "$id/README.md")
echo "Built $zipfile"
(cd "$(dirname "$zipfile")" && sha256sum "$(basename "$zipfile")")
