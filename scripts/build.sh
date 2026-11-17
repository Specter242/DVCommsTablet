#!/usr/bin/env bash
# Build the mod in Release. Installing it into a game is a separate step.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dv="${DV_INSTALL_DIR:-$HOME/.local/share/Steam/steamapps/common/Derail Valley}"

dotnet build "$repo/src/CommsTablet/CommsTablet.csproj" -c Release -p:DvInstallDir="$dv" "$@"
