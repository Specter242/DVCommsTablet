#!/usr/bin/env bash
# Put the other mods' assemblies the contract check pins into .deps/ (ignored;
# third-party binaries are never committed). Today that is DV Car Changer
# 1.5.4 (Wiz, MIT), the version the Change page is written for: its GitHub
# release zip, checked against the SHA-256 pinned here. The zip holds the same
# files as Nexus mod 1047's v1.5.4 download. Nothing is run from it: the
# contract check only reads its types. Idempotent: an unpacked copy whose zip
# still matches is kept as it is.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
deps="$repo/.deps"
url="https://github.com/WhistleWiz/dv-car-changer/releases/download/v1.5.4/DVCarChanger.zip"
sha="3fe3c86aace1f3140f03186c40e8bc5dd84461f1a51a40ff14f1a1c5a5847389"
zip="$deps/DVCarChanger-1.5.4.zip"

mkdir -p "$deps"
if [[ ! -f "$zip" ]] || ! sha256sum -c --status <<<"$sha  $zip"; then
    curl -fsSL -o "$zip.part" "$url"
    if ! sha256sum -c --status <<<"$sha  $zip.part"; then
        rm -f "${zip:?}.part"
        echo "DV Car Changer 1.5.4's zip does not match its pinned SHA-256" >&2
        exit 1
    fi
    mv "$zip.part" "$zip"
    rm -rf "${deps:?}/DVCarChanger"
fi
if [[ ! -f "$deps/DVCarChanger/CarChanger.Game.dll" ]]; then
    python3 -I -c 'import sys, zipfile; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])' "$zip" "$deps"
fi
echo "$deps/DVCarChanger: DV Car Changer 1.5.4"
