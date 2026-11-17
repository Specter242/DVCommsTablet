#!/usr/bin/env bash
# Put the other mods' assemblies the contract check pins into .deps/ (ignored;
# third-party binaries are never committed):
# - DV Car Changer 1.5.4 (Wiz, MIT), the version the Change page is written
#   for. Its zip holds the same files as Nexus mod 1047's v1.5.4 download.
# - Double Track 2.1.1 (Chump_the_Lump), the version the Double Track maps
#   are drawn for: only its assembly and Info.json are unpacked.
# Each is its GitHub release zip, checked against the SHA-256 pinned here.
# Nothing is run from them: the contract check only reads their types.
# Idempotent: an unpacked copy whose zip still matches is kept as it is.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
deps="$repo/.deps"
# fetch NAME URL SHA256 ZIP: the zip at ZIP, downloaded when missing or changed; sets fetched=1 when it was.
fetch() {
    local name="$1" url="$2" sha="$3" zip="$4"
    fetched=
    if [[ -f "$zip" ]] && sha256sum -c --status <<<"$sha  $zip"; then
        return 0
    fi
    curl -fsSL -o "$zip.part" "$url"
    if ! sha256sum -c --status <<<"$sha  $zip.part"; then
        rm -f "${zip:?}.part"
        echo "$name's zip does not match its pinned SHA-256" >&2
        exit 1
    fi
    mv "$zip.part" "$zip"
    fetched=1
}

mkdir -p "$deps"

zip="$deps/DVCarChanger-1.5.4.zip"
fetch "DV Car Changer 1.5.4" \
    "https://github.com/WhistleWiz/dv-car-changer/releases/download/v1.5.4/DVCarChanger.zip" \
    "3fe3c86aace1f3140f03186c40e8bc5dd84461f1a51a40ff14f1a1c5a5847389" "$zip"
[[ -n "$fetched" ]] && rm -rf "${deps:?}/DVCarChanger"
if [[ ! -f "$deps/DVCarChanger/CarChanger.Game.dll" ]]; then
    python3 -I -c 'import sys, zipfile; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])' "$zip" "$deps"
fi
echo "$deps/DVCarChanger: DV Car Changer 1.5.4"

zip="$deps/DoubleTrack-2.1.1.zip"
fetch "Double Track 2.1.1" \
    "https://github.com/Chump-the-Lump/DV-DoubleTrack/releases/download/2.1.1/Double.Track.2.1.1.zip" \
    "28b3c5f53a10ed460acd021710e1bdd47acc949ec274918ac28328619d5d52d8" "$zip"
[[ -n "$fetched" ]] && rm -rf "${deps:?}/DoubleTrack"
if [[ ! -f "$deps/DoubleTrack/DoubleTrack.dll" ]]; then
    python3 -I -c '
import sys, zipfile, os
z = zipfile.ZipFile(sys.argv[1])
os.makedirs(sys.argv[2], exist_ok=True)
for n in z.namelist():
    if os.path.basename(n) in ("DoubleTrack.dll", "info.json"):
        with open(os.path.join(sys.argv[2], os.path.basename(n)), "wb") as f:
            f.write(z.read(n))
' "$zip" "$deps/DoubleTrack"
fi
echo "$deps/DoubleTrack: Double Track 2.1.1"
