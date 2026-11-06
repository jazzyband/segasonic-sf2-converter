#!/usr/bin/env bash
set -euo pipefail
if [ "$#" -lt 2 ]; then
    echo 'Usage: ./build_linux.sh /path/to/segasonic-decomp /path/to/sound.zip [output-directory]' >&2
    exit 1
fi
ROOT="$(cd -- "$(dirname -- "$0")" && pwd)"
DECOMP="$1"
SOUND="$2"
OUT="${3:-$ROOT/Output}"
mkdir -p "$ROOT/build"
gcc -O2 -fPIC -shared -I"$DECOMP/runtime/include" "$ROOT/native/segasonic_fm_bridge.c" "$DECOMP/runtime/src/ym3438.c" -o "$ROOT/build/libsegasonic_fm.so" -lm
dotnet run --configuration Release --project "$ROOT/src/SegaSonicConverter/SegaSonicConverter.csproj" -- --sound "$SOUND" --fm-library "$ROOT/build/libsegasonic_fm.so" --output "$OUT" --drums-db 5
