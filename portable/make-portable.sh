#!/usr/bin/env bash
# Same as make-portable.ps1 for machines that publish from a script host.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/artifacts/publish"
DST="$ROOT/artifacts/portable"
if [[ ! -d "$SRC" ]]; then
  echo "Publish output not found at $SRC"
  echo "Run the dotnet publish command from BUILD.md first."
  exit 1
fi
rm -rf "$DST"
mkdir -p "$DST"
cp -a "$SRC/." "$DST/"
cp "$(dirname "$0")/portable.txt" "$DST/"
echo "Portable folder: $DST"
